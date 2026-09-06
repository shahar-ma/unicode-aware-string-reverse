using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Shared.Services.Benchmark;

public static class Program
{
    public static void Main(string[] args)
    {
        // Discovers every [MemoryDiagnoser]/[Benchmark]-decorated class
        // in this assembly (StringReverseBenchmarks,
        // RealTextReverseBenchmarks, and any future ones) instead of
        // hardcoding a single class - lets you pick interactively, or
        // pass a filter like `--filter *RealText*` on the command line.
#if DEBUG
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new DebugInProcessConfig());
#else
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
#endif
    }
}
