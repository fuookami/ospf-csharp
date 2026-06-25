#nullable enable

using BenchmarkDotNet.Running;
using System.Reflection;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// Benchmark runner entry.
/// <para>
/// Usage:
/// <code>dotnet run -c Release --project src/Benchmark -- --filter '*'</code>
/// </para>
/// </summary>
public static class Program {
    /// <summary>Entry point.</summary>
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
}
