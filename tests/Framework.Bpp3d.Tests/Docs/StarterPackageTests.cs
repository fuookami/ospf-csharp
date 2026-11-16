// Starter 包的依赖完整性测试 / Dependency-integrity tests for the starter packages.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 各 starter 包必须**引用各自的框架模块**且**产出唯一程序集名**。
/// Each starter package must **reference its framework modules** and **produce a unique assembly name**.
/// </summary>
/// <remarks>
/// 之前，五个 starter 的 csproj **完全相同**：`AssemblyName` 都是 `Fuookami.Ospf.Starters`，
/// 且**一个 `ProjectReference` 都没有**。也就是说它们既互相覆盖程序集，又不引用任何东西——
/// 装上去什么都拿不到，而构建与测试全程绿灯。
///
/// Before the five starter csprojs were **byte-identical**: every `AssemblyName` was `Fuookami.Ospf.Starters` and there was **not a single
/// `ProjectReference`**. So they overwrote each other's assemblies and referenced nothing — installing one gave you nothing, while build and tests stayed
/// green throughout.
/// </remarks>
public sealed class StarterPackageTests {
    private static readonly Dictionary<string, string[]> Expected = new() {
        ["Bpp3d"] = new[] { "Framework.Bpp3d", "Framework", "Core", "Math", "Utils" },
        ["Csp1d"] = new[] { "Framework.Csp1d", "Framework", "Core", "Math", "Utils" },
        ["GanttScheduling"] = new[] {
            "Framework.GanttScheduling", "Framework.NetworkScheduling", "Framework", "Core", "Math", "Utils",
        },
        ["GanttNetworkScheduling"] = new[] {
            "Framework.GanttNetworkScheduling", "Framework.GanttScheduling", "Framework.NetworkScheduling",
            "Framework", "Core", "Math", "Utils",
        },
        ["Framework"] = new[] { "Framework", "Core", "Math", "Utils" },
        ["Core"] = new[] { "Core", "Math", "Utils" },
    };

    private static string SolutionRoot()
    {
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && directory is not null; ++depth) {
            if (File.Exists(Path.Combine(directory, "ospf-csharp.sln"))) {
                return directory;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new DirectoryNotFoundException("ospf-csharp.sln not found from the test output directory");
    }

    private static XDocument LoadCsproj(string starter) =>
        XDocument.Load(Path.Combine(SolutionRoot(), "src", "Starters", starter, $"Starters.{starter}.csproj"));

    [Fact]
    public void EveryStarter_ShouldProduceAUniqueAssemblyName() {
        var names = new List<string>();
        foreach (string starter in Expected.Keys) {
            string? assemblyName = LoadCsproj(starter)
                .Descendants("AssemblyName")
                .Select(element => element.Value)
                .SingleOrDefault();
            names.Add(assemblyName ?? "(missing)");
        }
        names.Should().OnlyHaveUniqueItems();
        names.Should().NotContain("(missing)");
        names.Should().Contain($"Fuookami.Ospf.Starters.{Expected.Keys.First()}");
    }

    [Fact]
    public void EveryStarter_ShouldNameItsAssemblyAfterItself() {
        foreach (string starter in Expected.Keys) {
            string? assemblyName = LoadCsproj(starter)
                .Descendants("AssemblyName")
                .Select(element => element.Value)
                .SingleOrDefault();
            assemblyName.Should().Be($"Fuookami.Ospf.Starters.{starter}", starter);
        }
    }

    /// <summary>**每个 starter 必须真的引用它的框架模块** / Every starter must actually reference its framework modules.</summary>
    [Fact]
    public void EveryStarter_ShouldReferenceItsFrameworkModules() {
        foreach (KeyValuePair<string, string[]> pair in Expected) {
            var references = LoadCsproj(pair.Key)
                .Descendants("ProjectReference")
                .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
                .ToList();
            references.Should().NotBeEmpty($"{pair.Key} must reference something");

            foreach (string module in pair.Value) {
                references.Should().Contain(
                    reference => reference.Contains($"{module}/{module}.csproj", StringComparison.Ordinal),
                    $"{pair.Key} must reference {module}"
                );
            }
        }
    }

    /// <summary>**每个 starter 的 ProjectReference 必须指向真实存在的项目** / Every ProjectReference must point at an existing project.</summary>
    [Fact]
    public void EveryStarter_ShouldReferenceExistingProjects() {
        var missing = new List<string>();
        foreach (string starter in Expected.Keys) {
            foreach (XElement element in LoadCsproj(starter).Descendants("ProjectReference")) {
                string relative = (string?)element.Attribute("Include") ?? string.Empty;
                string resolved = Path.GetFullPath(
                    Path.Combine(SolutionRoot(), "src", "Starters", starter, relative.Replace('\\', '/'))
                );
                if (!File.Exists(resolved)) {
                    missing.Add($"{starter} -> {relative}");
                }
            }
        }
        missing.Should().BeEmpty();
    }

    [Fact]
    public void EveryStarter_ShouldShipBothLanguages() {
        foreach (string starter in Expected.Keys) {
            File.Exists(Path.Combine(SolutionRoot(), "src", "Starters", starter, "README.md"))
                .Should().BeTrue($"{starter} must ship README.md");
        }
    }
}
