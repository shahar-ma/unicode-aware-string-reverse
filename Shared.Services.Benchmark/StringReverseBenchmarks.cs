using System;
using BenchmarkDotNet.Attributes;

#if CPU_MEM
using Microsoft.VSDiagnostics;
#endif
namespace Shared.Services.Benchmark;

[MemoryDiagnoser]
#if CPU_MEM
// Set the project to Release, then run it with Ctrl+F5 (Start Without Debugging) from inside Visual Studio
// — not dotnet run from a terminal
[CPUUsageDiagnoser(OpenDiagsessionInVS = true)]
#else
// Run with: dotnet run -c Release  (from a console-app project referencing this file)
// Run with: dotnet run -c Release -- --filter *Reverse_BU_1* (for a single method)
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
#endif
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

    [Benchmark(Baseline = true)]
    public string Reverse_ASCII_1() => _ascii.ReverseString()!;

    [Benchmark]
    public string Reverse_ASCII_2() => _ascii.Reverse()!;

    [Benchmark]
    public string Reverse_BU_1() => _bmpUnicode.ReverseString()!;

    [Benchmark]
    public string Reverse_BU_2() => _bmpUnicode.Reverse()!;

    [Benchmark]
    public string Reverse_CM_1() => _combiningMarks.ReverseString()!;

    [Benchmark]
    public string Reverse_CM_2() => _combiningMarks.Reverse()!;

    [Benchmark]
    public string Reverse_SP_1() => _surrogatePairs.ReverseString()!;

    [Benchmark]
    public string Reverse_SP_2() => _surrogatePairs.Reverse()!;

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
