using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Quality;

/// <summary>
/// 打包元数据**必须是真的** / The packaging metadata must be real.
/// </summary>
/// <remarks>
/// `dotnet pack` 本来就跑得通——**这正是问题**：它默认打出 `1.0.0`、没有作者、没有自述文件，
/// 于是"能打包"这件事看起来已经完成，而包里的信息是假的。这类"已经能跑，但内容是编的"状态，
/// 比"跑不起来"更难发现。
///
/// 本仓的版本号**不自己定**：跟着能力文档走（当前是 `1.1.2`）。
/// 所以这条判据做两件事：**元数据齐全**，以及**版本号与能力文档里记的一致**——
/// 换代时两边必须一起动，**不许只改一边**。
///
/// `dotnet pack` already worked — **that was the problem**: it produced `1.0.0` by default, with no authors and no readme, so "it can pack" looked finished while
/// the package carried false information. That state — runs fine, content invented — is harder to notice than a plain failure. This repo's version is **not its own**:
/// it follows the capability document (currently `1.1.2`). The criteria therefore check two things: the metadata is complete, and
/// **the version matches what the capability document records** — bumping the version means moving both, **never just one**.
/// </remarks>
public sealed class PackagingMetadataTests
{
    /// <summary>
    /// **元数据四项齐全** / The four metadata items are present.
    /// </summary>
    [Fact]
    public void ThePackagingMetadata_ShouldBeComplete() {
        string props = ReadRepositoryFile(Path.Combine("Directory.Build.props"));

        props.Should().Contain("<Authors>", "a package without authors carries no provenance");
        props.Should().Contain("<PackageReadmeFile>",
            "the packer warns about a missing readme; leaving the warning in means the warning is ignored forever");
        props.Should().Contain("<RepositoryUrl>", "a package that cannot be traced back to its source is unfalsifiable");

        // **README 必须真的被塞进包里**：只写 `<PackageReadmeFile>` 而不 Include，打包会直接失败——
        // 判据在这里把两件事一起钉住，免得有人只删 Include。
        //
        // **The README must actually be packed**: setting `<PackageReadmeFile>` without the Include makes packing fail outright. This criterion pins both, so nobody
        // can delete just the Include.
        props.Should().Contain("Pack=\"true\"", "the readme file item must be marked for packing");
    }

    /// <summary>
    /// **版本号与能力文档一致** / The version matches the capability document.
    /// </summary>
    /// <remarks>
    /// 版本号跟能力文档走，不跟本仓的心情走。文档里记着它跟到哪一版，这里要求两边**同一个数**。
    ///
    /// The version follows the capability document, not this repo's mood. The document records the version it tracks, and this criterion requires the **same number**
    /// in both places.
    /// </remarks>
    [Fact]
    public void TheVersion_ShouldMatchTheBaselineRecordedInTheCapabilityDocument() {
        string props = ReadRepositoryFile("Directory.Build.props");
        Match declared = Regex.Match(props, "<Version>([^<]+)</Version>");
        declared.Success.Should().BeTrue("the version must be declared explicitly, not left to the 1.0.0 default");

        string status = ReadRepositoryFile(Path.Combine("docs", "kotlin-capability-matrix.md"));

        status.Should().Contain(declared.Groups[1].Value,
            "the capability document records which baseline version this port tracks; a version that appears in only one of the two places is a lie in the other");
    }


    /// <summary>
    /// **可执行工程不发布** / The executable projects are not published.
    /// </summary>
    /// <remarks>
    /// `dotnet pack` 默认**什么都打**：示例和基准工程一起进了 34 个包里，其中一个叫 `Benchmark.1.1.2.nupkg`——
    /// **连产品前缀都没有**。这两个工程是"怎么用"和"跑起来看数字"，**不是谁该依赖的库**。
    ///
    /// `dotnet pack` **packs everything by default**: the example and benchmark projects came out among the 34 packages, one of them named `Benchmark.1.1.2.nupkg` —
    /// **without even the product prefix**. Those two show how to use the framework and how to measure it; **they are not libraries anyone should depend on**.
    /// </remarks>
    [Fact]
    public void TheExecutableProjects_ShouldNotBePublished() {
        foreach (string project in new[] {
            Path.Combine("src", "Example", "Example.csproj"),
            Path.Combine("src", "Benchmark", "Benchmark.csproj"),
        }) {
            ReadRepositoryFile(project).Should().Contain("<IsPackable>false</IsPackable>",
                project + " must not be published; `dotnet pack` would otherwise turn it into an artifact");
        }
    }

    /// <summary>
    /// **每个要发布的项目都带产品前缀** / Every published project carries the product prefix.
    /// </summary>
    /// <remarks>
    /// `Benchmark.1.1.2.nupkg` 那个包说明默认命名会漏（它按**工程文件名**取），而其余 33 个是 `Fuookami.Ospf.*`。
    /// 判据遍历 `src/**/*.csproj`，跳过 `IsPackable=false` 的，检查剩下的包名都以 `Fuookami.Ospf.` 开头——
    /// **新加一个可发布工程却忘了设 `PackageId`，这里就会红**。
    ///
    /// The `Benchmark.1.1.2.nupkg` package showed the default naming leaks (it takes the **project file stem**), while the other 33 read `Fuookami.Ospf.*`. This
    /// criterion walks `src/**/*.csproj`, skips `IsPackable=false`, and checks the rest all start with `Fuookami.Ospf.` — **a new publishable project that forgets
    /// `PackageId` reds here.**
    /// </remarks>
    [Fact]
    public void EveryPublishedProject_ShouldCarryTheProductPrefix() {
        string root = FindRepositoryRoot();
        var offenders = new List<string>();

        foreach (string project in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)) {
            // **跳过构建产物**：BenchmarkDotNet 会在 `bin/` 里生成一个临时工程，
            // 它的名字是个 GUID，把它算进来会让判据红在一个**不是我们写的工程**上。
            //
            // **Skip build output**: BenchmarkDotNet generates a temporary project under `bin/` whose name is a GUID, and counting it would red the criterion on a
            // project **nobody here wrote**.
            string normalized = project.Replace(Path.DirectorySeparatorChar, '/');
            if (normalized.Contains("/bin/", StringComparison.Ordinal)
                || normalized.Contains("/obj/", StringComparison.Ordinal)) {
                continue;
            }

            string text = File.ReadAllText(project);
            if (text.Contains("<IsPackable>false</IsPackable>", StringComparison.Ordinal)) {
                continue;
            }

            Match packageId = Regex.Match(text, "<PackageId>([^<]+)</PackageId>");
            Match assembly = Regex.Match(text, "<AssemblyName>([^<]+)</AssemblyName>");
            string name = packageId.Success ? packageId.Groups[1].Value
                : assembly.Success ? assembly.Groups[1].Value
                : Path.GetFileNameWithoutExtension(project);

            if (!name.StartsWith("Fuookami.Ospf.", StringComparison.Ordinal)) {
                offenders.Add(Path.GetFileName(project) + " -> " + name);
            }
        }

        offenders.Should().BeEmpty("a published package must carry the product prefix; otherwise it is indistinguishable from a scratch artifact");
    }


    /// <summary>
    /// **不许有 prerelease 依赖** / No prerelease dependencies.
    /// </summary>
    /// <remarks>
    /// 之前 `Pomelo.EntityFrameworkCore.MySql` 是 `9.0.0-preview.3.efcore.9.0.0`，于是每次打包都报
    /// **NU5104**："A stable release of a package should not have a prerelease dependency"——
    /// 我们的包版本是稳定的 `1.1.2`，依赖却是预览版。这个警告**从来没人处理**，因为它只是警告。
    ///
    /// 判据直接扫 `Directory.Packages.props` 的 `<PackageVersion... Version="..." />`，
    /// **任何带 `-preview` / `-alpha` / `-beta` / `-rc` 的版本都会红**——想用预览版，就得先改这条判据，也就得先说明理由。
    ///
    /// Before `Pomelo.EntityFrameworkCore.MySql` was `9.0.0-preview.3.efcore.9.0.0`, so every pack warned **NU5104**: "A stable release of a package should not have a
    /// prerelease dependency" — our packages are the stable `1.1.2` while the dependency was a preview. Nobody ever handled the warning, because it is only a warning.
    /// This criterion scans the `<PackageVersion... Version="..." />` entries in `Directory.Packages.props`: **any `-preview`, `-alpha`, `-beta` or `-rc` reds it** —
    /// wanting a preview means changing this criterion first, which means stating a reason first.
    /// </remarks>
    [Fact]
    public void NoDependency_ShouldBeAPrerelease() {
        string props = ReadRepositoryFile(Path.Combine("Directory.Packages.props"));

        // **只看 XML 行**：注释里会提到预览版本号，把注释算进来会让判据红在自己的解释上。
        // **Only XML lines**: the comments mention a preview version (the earlier note spells it out), and counting them would red the criterion on its own explanation.
        var offenders = new List<string>();
        foreach (string line in props.Split('\n')) {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("<!--", StringComparison.Ordinal) || trimmed.StartsWith("B848", StringComparison.Ordinal)) {
                continue;
            }
            Match version = Regex.Match(line, "<PackageVersion Include=\"([^\"]+)\" Version=\"([^\"]+)\"");
            if (!version.Success) {
                continue;
            }
            string declared = version.Groups[2].Value;
            if (declared.Contains("-preview", StringComparison.OrdinalIgnoreCase)
                || declared.Contains("-alpha", StringComparison.OrdinalIgnoreCase)
                || declared.Contains("-beta", StringComparison.OrdinalIgnoreCase)
                || declared.Contains("-rc", StringComparison.OrdinalIgnoreCase)) {
                offenders.Add(version.Groups[1].Value + " -> " + declared);
            }
        }

        offenders.Should().BeEmpty("a stable package with a prerelease dependency warns NU5104 on every pack, and warnings nobody handles are noise");
    }

    /// <summary>从测试进程往上找仓根 / Walk up from the test process to the repository root.</summary>
    /// <returns>仓根路径 / The repository root.</returns>
    private static string FindRepositoryRoot() {
        if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "Directory.Build.props"))) {
            return Directory.GetCurrentDirectory();
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException("could not locate the repository root from " + AppContext.BaseDirectory);
    }

    /// <summary>从仓根读文件 / Read a file from the repository root.</summary>
    /// <param name="relative">相对路径 / Relative path.</param>
    /// <returns>文件内容 / The file content.</returns>
    private static string ReadRepositoryFile(string relative) {
        string direct = Path.Combine(Directory.GetCurrentDirectory(), relative);
        if (File.Exists(direct)) {
            return File.ReadAllText(direct);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            string candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) {
                return File.ReadAllText(candidate);
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException("could not locate " + relative);
    }
}
