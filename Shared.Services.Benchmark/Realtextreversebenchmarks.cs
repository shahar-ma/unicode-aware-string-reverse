using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Shared.Services.Benchmark;

/// <summary>
/// Benchmarks Reverse() against real extracted text rather than synthetic
/// data, to catch realistic clustering patterns (long ASCII runs
/// interrupted by occasional diacritics/emoji) that the uniform-random
/// generators in StringReverseBenchmarks don't produce.
/// </summary>
/// <remarks>
/// EDIT THE PATHS in <see cref="Samples"/> below to point at your own
/// files before running. For the "ANSI" (Windows-1252) sample specifically,
/// true code-page decoding requires the System.Text.Encoding.CodePages
/// NuGet package plus registering the provider once (see Program.cs) -
/// without it, GetEncoding(1252) throws NotSupportedException on
/// .NET Core/5+. Falling back to Encoding.Latin1 will run, but it is NOT
/// the same encoding: Latin-1 treats 0x80-0x9F as control characters,
/// while Windows-1252 maps that range to printable punctuation (curly
/// quotes, em-dash, etc.) - exactly the bytes an "ANSI" test is meant to
/// exercise, so Latin-1 as a substitute would silently under-test this.
/// </remarks>
[MemoryDiagnoser]
public class RealTextReverseBenchmarks
{
    public readonly record struct TextSample(string Name, string Path, ExtractKind Kind, Encoding? Encoding = null)
    {
        // BenchmarkDotNet prints this in the summary table's Sample column -
        // keep it short and identifying, not the full path.
        public override string ToString() => Name;
    }

    private string? _text;

    public enum ExtractKind
    {
        PlainText,
        Docx,
        Xlsx,
    }

    [ParamsSource(nameof(Samples))]
    public TextSample Sample { get; set; }

    public static IEnumerable<TextSample> Samples()
    {
        yield return new TextSample("PlainText_Utf8", Path.Combine(AppContext.BaseDirectory, "sample_utf8.txt"), ExtractKind.PlainText, Encoding.UTF8);
        yield return new TextSample("PlainText_Utf16", Path.Combine(AppContext.BaseDirectory, "sample_utf16.txt"), ExtractKind.PlainText, Encoding.Unicode);
        yield return new TextSample("PlainText_Windows1252", Path.Combine(AppContext.BaseDirectory, "sample_ansi.txt"), ExtractKind.PlainText, GetWindows1252());
        yield return new TextSample("WordDoc", Path.Combine(AppContext.BaseDirectory, "sample.docx"), ExtractKind.Docx);
        yield return new TextSample("ExcelSheet", Path.Combine(AppContext.BaseDirectory, "sample.xlsx"), ExtractKind.Xlsx);
    }
    [Benchmark]
    public string? Reverse() => _text.Reverse();

    [GlobalSetup]
    public void Setup()
    {
        if (!File.Exists(Sample.Path))
            throw new FileNotFoundException("Missing sample file.",Path.GetFileName(Sample.Path));
        _text = Sample.Kind switch
        {
            ExtractKind.PlainText => File.ReadAllText(Sample.Path, Sample.Encoding ?? Encoding.UTF8),
            ExtractKind.Docx => ExtractDocxText(Sample.Path),
            ExtractKind.Xlsx => ExtractXlsxText(Sample.Path),
            _ => throw new NotSupportedException($"Unhandled kind: {Sample.Kind}")
        };
    }

    // .docx is a zip archive; the visible document text lives in
    // word/document.xml as a sequence of <w:t> run-text elements. Matching
    // on LocalName rather than a hardcoded namespace URI keeps this
    // resilient to the small XML-namespace variations different Word
    // versions sometimes produce.
    private static string ExtractDocxText(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        ZipArchiveEntry? entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException($"'{path}' has no word/document.xml - is it a valid .docx?");

        using Stream stream = entry.Open();
        XDocument doc = XDocument.Load(stream);
        return doc.GetWordText();
    }

    // .xlsx encodes cell text three different, all-valid ways depending on
    // the writer, and a file may mix them:
    //   t="s"          -> <v> holds an INDEX into xl/sharedStrings.xml
    //   t="inlineStr"  -> text lives in a nested <is><t>...</t></is>
    //   t="str"        -> the string itself sits directly in <v> (this is
    //                     technically "cached formula string result" per
    //                     the OOXML spec, but plenty of non-Excel writers
    //                     use it for plain string cells too)
    // Cells with no "t" attribute (or t="n"/"b"/etc.) are numeric/boolean/
    // date-serial - not text content, and must be skipped so they don't
    // pollute the sample with number noise.
    private static string ExtractXlsxText(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);

        List<ZipArchiveEntry> entries = [.. archive.Entries.Where(e => e.IsExcelWorkSheet())];
        if (entries.Count != 0)
            return archive.GetExcelTextFromArchive(entries);

        string allEntries = string.Join("\n  ", archive.Entries.Select(e => e.FullName));
        throw new InvalidDataException(
            $"'{path}' has no xl/worksheets/*.xml - this doesn't look like standard OOXML .xlsx packaging. Actual entries found:\n  {allEntries}");
    }

    private static Encoding? GetWindows1252()
    {
        try
        {
            return Encoding.GetEncoding(1252);
        }
        catch (NotSupportedException)
        {
            // System.Text.Encoding.CodePages isn't referenced/registered.
            // Returning null falls back to Encoding.UTF8 in Setup(), which
            // will mis-decode a genuinely Windows-1252 file - see the
            // remarks on this class for why that's not an equivalent test.
            return null;
        }
    }
}
