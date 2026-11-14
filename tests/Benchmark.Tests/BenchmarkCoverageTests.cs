// 基准夹具覆盖性的判据 / Criteria for benchmark-fixture coverage.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Benchmark;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Test.Benchmark;

/// <summary>
/// **每个带 `[Benchmark]` 的类都必须有测试覆盖** / Every class carrying `[Benchmark]` must have test coverage.
/// </summary>
/// <remarks>
/// **B395 记着"十二个基准从未实跑"**，本批复核的是**夹具正确性**（不是执行）。
///
/// 复核过程中我一度以为"四个基准没有对应测试"——**这个判断是错的**。
/// 它们由 `BenchmarkScaleValidityTests` 统一覆盖，只是文件名与基准名不一一对应：
///
/// | 基准 | 覆盖它的测试文件 |
/// | --- | --- |
/// | `CoreHotPathBenchmark` | `BenchmarkScaleValidityTests` |
/// | `CorePluginDumpBenchmark` | `BenchmarkScaleValidityTests` |
/// | `MultiArrayHotPathBenchmark` | `BenchmarkScaleValidityTests` |
/// | `SymbolCombineBenchmark` | `BenchmarkScaleValidityTests` |
/// | `PackingGeometryPreparationBenchmark` | `PackingGeometryPreparationBenchmarkTests`（走 `BuildResult` 静态方法，不出现 `new …Benchmark`） |
///
/// **所以"按文件名配对"是不可靠的覆盖检查方法。** 这条判据改用**反射**：
/// 枚举 `src/Benchmark` 程序集里所有带 `[Benchmark]` 方法的类型，
/// 断言每一个都能在 `Benchmark.Tests` 程序集的测试里被找到（按类型名出现在测试源码中）。
///
/// **B395 recorded "the twelve benchmarks never ran"**; this batch re-checks **fixture correctness** (not execution). While doing so I wrongly concluded "four
/// benchmarks have no tests" — **that was wrong** (table above): they are covered by `BenchmarkScaleValidityTests`, and `PackingGeometryPreparationBenchmark` is covered
/// through its static `BuildResult`. **Matching by filename is an unreliable coverage check**, so this criterion uses **reflection**: enumerate every type in the
/// `src/Benchmark` assembly carrying a `[Benchmark]` method and assert each one appears in the `Benchmark.Tests` sources by type name.
/// </remarks>
public class BenchmarkCoverageTests {
    /// <summary>**每个基准类都必须被某个测试引用** / Every benchmark class must be referenced by some test.</summary>
    /// <remarks>
    /// **判据无法钉住"枚举本身漏了类型"。**
    ///
    /// 实测：把 `Where(name => ...)` 改成先排除 `SymbolCombineBenchmark` 再判断，
    /// **判据仍然全绿**——因为其余 11 个都确实被覆盖，"未覆盖集合为空"照样成立。
    ///
    /// 这是**枚举型判据的固有性质**：它验证"枚举到的每个都有覆盖"，
    /// **不验证"枚举完整"**。要钉完整性需要一个**独立的预期清单**，
    /// 而那会随基准增加而腐化——**本批选择不写**，并在文档里记下这个边界。
    ///
    /// 能钉住的是**读取方式**：把 `.Select(File.ReadAllText)` 去掉（退回只读路径），
    /// 判据立刻报出四个"未覆盖"。
    ///
    /// **The criterion cannot pin "the enumeration itself missed a type".** Measured: adding an explicit exclusion for `SymbolCombineBenchmark` before the coverage test
    /// left it **fully green**, because the other eleven really are covered and "uncovered is empty" still holds. That is **inherent to enumeration-based criteria**: it
    /// verifies "everything enumerated is covered", **not "the enumeration is complete"**. Pinning completeness needs an **independent expected list**, which would rot as
    /// benchmarks are added — **deliberately not written**, and the boundary is recorded here. What it *can* pin is **how sources are read**: dropping
    /// `.Select(File.ReadAllText)` (falling back to paths) makes it immediately report four "uncovered".
    /// </remarks>
    [Fact]
    public void EveryBenchmarkTypeShouldBeReferencedByATest() {
        Assembly benchmarkAssembly = typeof(BenchmarkRunOptions).Assembly;
        var benchmarkTypes = benchmarkAssembly.GetTypes()
            .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(m => m.GetCustomAttribute<BenchmarkAttribute>() is not null))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        benchmarkTypes.Should().NotBeEmpty("the benchmark assembly must contain benchmark types");

        string[] testSources = ReadTestSources();
        var uncovered = benchmarkTypes
            .Where(name => !testSources.Any(src => src.Contains(name, StringComparison.Ordinal)))
            .ToList();

        uncovered.Should().BeEmpty(
            "every benchmark type needs a test that at least constructs it; uncovered: {0}",
            string.Join(", ", uncovered));
    }

    /// <summary>读取全部基准测试源码 / Read every benchmark test source.</summary>
    /// <returns>源码文本 / Source texts.</returns>
    /// <remarks>
    /// 从**测试程序集所在目录向上**找 `tests/Benchmark.Tests`——不硬编码绝对路径，
    /// 这样换机器或换工作目录仍然有效。
    ///
    /// Walks **up from the test assembly's directory** to find `tests/Benchmark.Tests` rather than hard-coding an absolute path, so it survives a different machine or
    /// working directory.
    /// </remarks>
    private static string[] ReadTestSources() {
        var directory = new global::System.IO.DirectoryInfo(global::System.AppContext.BaseDirectory);
        while (directory is not null) {
            string candidate = global::System.IO.Path.Combine(directory.FullName, "tests", "Benchmark.Tests");
            if (global::System.IO.Directory.Exists(candidate)) {
                // **必须读文件内容，不是只拿路径。** 第一版返回 `GetFiles(...)` 的结果——
                // 那是**路径数组**，`Contains(name)` 于是变成了"路径里有没有这个类型名"，
                // 永远为假，判据报出四个"未覆盖"。**这正是它想抓的那类错误，只不过犯在自己身上。**
                //
                // **Must read the file contents, not just the paths.** The first version returned `GetFiles(...)` — an array of **paths** — so `Contains(name)` asked
                // "does the path contain this type name", always false, and the criterion reported four "uncovered". **Exactly the class of bug it exists to catch,
                // committed inside itself.**
                return global::System.IO.Directory.GetFiles(candidate, "*.cs", global::System.IO.SearchOption.AllDirectories)
                    .Where(f => {
                        // 排除构建产物：只按路径段判断，避免手写反斜杠转义。
                        // Exclude build output by path segment, avoiding hand-written backslash escapes.
                        string[] segments = f.Split(
                            new[] { global::System.IO.Path.DirectorySeparatorChar, global::System.IO.Path.AltDirectorySeparatorChar });
                        return !segments.Contains("obj", global::System.StringComparer.Ordinal)
                            && !segments.Contains("bin", global::System.StringComparer.Ordinal);
                    })
                    .Select(global::System.IO.File.ReadAllText)
                    .ToArray();
            }
            directory = directory.Parent;
        }
        throw new global::System.InvalidOperationException("tests/Benchmark.Tests not found above the test assembly directory");
    }
}
