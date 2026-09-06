using System;
using BenchmarkDotNet.Attributes;

namespace Shared.Services.Benchmark;
// Run with: dotnet run -c Release  (from a console-app project referencing this file)
// Run with: dotnet run -c Release -- --filter *Reverse_BU* (for a single method)
[MemoryDiagnoser]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class StringReverseBenchmarks
{
    private string _ascii = string.Empty;
    private string _bmpUnicode = string.Empty;
    private string _combiningMarks = string.Empty;
    private string _surrogatePairs = string.Empty;

    // Lengths in UTF-16 code units, covering the end cases (0, 1) plus
    // a spread of realistic and stress-test sizes.
    [Params(0, 1, 16, 256, 4_096, 65_536, 1_048_576)]

    public int Length { get; set; }

    [Benchmark(Baseline = true, Description = "Array.Reverse() of ASCII")]
    public string Reverse_Ascii_AR() => _ascii.ReverseString()!;

    [Benchmark(Description = "New ASCII Reverse")]
    public string Reverse_Ascii_New() => _ascii.Reverse()!;

    [Benchmark(Description = "Array.Reverse() of BmpUnicode")]
    public string Reverse_BU_AR() => _bmpUnicode.ReverseString()!;

    [Benchmark(Description = "New BmpUnicode Reverse")]
    public string Reverse_BU_New() => _bmpUnicode.Reverse()!;

    [Benchmark(Description = "Array.Reverse() of CombiningMarks")]
    public string Reverse_CM_AR() => _combiningMarks.ReverseString()!;

    [Benchmark(Description = "New CombiningMarks Reverse")]
    public string Reverse_CM_New() => _combiningMarks.Reverse()!;

    [Benchmark(Description = "Array.Reverse() of SurrogatePairs")]
    public string Reverse_SP_AR() => _surrogatePairs.ReverseString()!;

    [Benchmark(Description = "New SurrogatePairs Reverse")]
    public string Reverse_SP_New() => _surrogatePairs.Reverse()!;

    [GlobalSetup]
    public void Setup()
    {
        Random rng = new(42);
        _ascii = rng.BuildAscii(Length);
        _bmpUnicode = rng.BuildBmpUnicode(Length);
        _combiningMarks = rng.BuildCombiningMarks(Length);
        _surrogatePairs = rng.BuildSurrogatePairs(Length);
    }
}
