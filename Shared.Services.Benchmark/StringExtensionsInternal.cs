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

    internal static string BuildBmpUnicode(this Random rng, int length)
    {
        StringBuilder sb = new(length);
        for (int index = 0; index < length; index++)
        {
            sb.Append(rng.RandomNonMarkBmpChar());
        }
        return sb.ToString();
    }

    internal static string BuildCombiningMarks(this Random rng, int length)
    {
        StringBuilder sb = new(length);
        int written = 0;
        while (written + 1 < length)
        {
            char baseChar = (char)rng.Next('a', 'z' + 1);
            char mark = (char)rng.Next(StringExtensions.MinDiacriticsBlockValue, StringExtensions.LatinCapitalLetterLjWithCaron); // combining diacritics block
            sb.Append(baseChar).Append(mark);
            written += 2;
        }
        if (written < length)
        {
            sb.Append('x');
        }
        return sb.ToString();
    }

    internal static string BuildSurrogatePairs(this Random rng, int length)
    {
        StringBuilder sb = new(length);
        int written = 0;
        while (written + 1 < length)
        {
            int codepoint = rng.Next(StringExtensions.MinSurrogatePairValue, StringExtensions.MaxSurrogatePairValue);
            sb.Append(char.ConvertFromUtf32(codepoint));
            written += 2;
        }
        if (written < length)
        {
            sb.Append('x');
        }
        return sb.ToString();
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

    private static char RandomNonMarkBmpChar(this Random rng)
    {
        while (true)
        {
            char c = (char)rng.Next(StringExtensions.MinUnicodeValue, StringExtensions.MaxUnicodeValue);
            UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (!cat.IsUnicodeCategoryMark())
                return c;
        }
    }
}
