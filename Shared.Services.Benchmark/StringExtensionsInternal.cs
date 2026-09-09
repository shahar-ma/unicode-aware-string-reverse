using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Shared.Services.Benchmark;

internal static class StringExtensionsInternal
{
    private const char SPACE = ' ';
    private const string TEXT_ELEMENT = "t";
    private const string VALUE_ELEMENT = "v";
    private static readonly char[] BmpUnicodeChars = [.. Enumerable.Range(0x0080, 0x10000 - 0x0080)
            .Select(i => (char)i)
            .Where(c => !char.IsSurrogate(c) && 
                        !CharUnicodeInfo.GetUnicodeCategory(c).IsUnicodeCategoryMark())];

    internal static string BuildBmpUnicode(this Random rng, int length)
    {
        if (length == 0) return string.Empty;

        return string.Create(length, rng, static (span, random) =>
        {
            random.GetItems(BmpUnicodeChars, span);
        });
    }

    internal static string BuildCombiningMarks(this Random rng, int length)
    {
        if (length == 0) return string.Empty;

        return string.Create(length, rng, static (span, random) =>
        {
            int i = 0;
            int maxPairIndex = span.Length - 1;

            while (i < maxPairIndex)
            {
                span[i] = (char)random.Next('a', 'z' + 1);
                span[i + 1] = (char)random.Next(StringExtensions.MinDiacriticsBlockValue, StringExtensions.LatinCapitalLetterLjWithCaron);
                i += 2;
            }

            // Handle odd length tail directly inside the span
            if (i < span.Length)
            {
                span[i] = 'x';
            }
        });
    }

    internal static string BuildSurrogatePairs(this Random rng, int length)
    {
        if (length == 0) return string.Empty;
        return string.Create(length, rng, static (span, random) =>
        {
            int i = 0;
            int maxPairIndex = span.Length - 1;

            while (i < maxPairIndex)
            {
                int codepoint = random.Next(StringExtensions.MinSurrogatePairValue, StringExtensions.MaxSurrogatePairValue);

                // Decode UTF-32 code point directly to UTF-16 surrogate pair
                codepoint -= 0x10000;
                span[i] = (char)((codepoint >> 10) + 0xD800);     // High surrogate
                span[i + 1] = (char)((codepoint & 0x3FF) + 0xDC00); // Low surrogate

                i += 2;
            }

            if (i < span.Length)
            {
                span[i] = 'x';
            }
        });
    }

    internal static void GetExcelCellInlineStringValues(this StringBuilder sb, XElement cell)
    {
        foreach (XElement t in cell.Descendants().Where(e => e.Name.LocalName == TEXT_ELEMENT))
        {
            sb.Append(t.Value).Append(SPACE);
        }
    }

    internal static void GetExcelCellSharedStringValue(this StringBuilder sb, XElement cell, string?[] sharedStrings)
    {
        XElement? v = cell.GetExcelCellValue();
        if (v is not null
            && int.TryParse(v.Value, out int index)
            && index >= 0
            && index < sharedStrings.Length)
        {
            sb.Append(sharedStrings[index]).Append(SPACE);
        }
    }

    internal static void GetExcelCellStringValue(this StringBuilder sb, XElement cell)
    {
        XElement? v = cell.GetExcelCellValue();
        if (v is not null)
            sb.Append(v.Value).Append(SPACE);
    }

    internal static string GetExcelTextFromArchive(this ZipArchive archive, List<ZipArchiveEntry> entries)
    {
        StringBuilder sb = new();
        string?[] sharedStrings = archive.GetSharedStrings();
        entries.ForEach(entry => sb.AppendWorksheetCellText(entry, sharedStrings));
        return sb.ToString();
    }

    internal static string?[] GetSharedStrings(this ZipArchive archive)
    {
        ZipArchiveEntry? sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        return sharedStringsEntry?.LoadSharedStrings() ?? [];
    }

    internal static string GetWordText(this XDocument document)
    {
        StringBuilder sb = new();
        foreach (XElement paragraph in document.Descendants().Where(e => e.Name.LocalName == "p"))
        {
            sb.GetWordParagraphText(paragraph);
        }
        return sb.ToString();
    }

    internal static bool IsExcelWorkSheet(this ZipArchiveEntry entry)
    {
        return entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendWorksheetCellText(this StringBuilder sb, ZipArchiveEntry entry, string?[] sharedStrings)
    {
        using Stream stream = entry.Open();
        XDocument doc = XDocument.Load(stream);

        foreach (XElement cell in doc.Descendants().Where(e => e.Name.LocalName == "c"))
        {
            string? cellType = cell.Attribute("t")?.Value;

            switch (cellType)
            {
                case "inlineStr":
                    sb.GetExcelCellInlineStringValues(cell);
                    break;
                case "str":
                    sb.GetExcelCellStringValue(cell);
                    break;
                case "s":
                    sb.GetExcelCellSharedStringValue(cell, sharedStrings);
                    break;
                default: // numeric, boolean, date-serial, error, etc. - not text, skip.
                    break;
            }
        }
    }

    private static XElement? GetExcelCellValue(this XElement cell) => cell.Elements().FirstOrDefault(e => e.Name.LocalName == VALUE_ELEMENT);
    private static void GetWordParagraphText(this StringBuilder sb, XElement paragraph)
    {
        List<XElement> textElements = [.. paragraph.Descendants().Where(e => e.Name.LocalName == "t")];
        textElements.ForEach(textRun => sb.Append(textRun.Value));
        sb.Append('\n');
    }
    private static string[] LoadSharedStrings(this ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        XDocument doc = XDocument.Load(stream);
        return [.. doc.Descendants()
            .Where(e => e.Name.LocalName == "si")
            .Select(si => string.Concat(si.Descendants().Where(e => e.Name.LocalName == "t").Select(t => t.Value)))];
    }
}
