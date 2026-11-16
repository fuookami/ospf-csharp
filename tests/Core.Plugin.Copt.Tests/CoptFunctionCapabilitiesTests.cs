#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Function;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Copt.Tests;

/// <summary>
/// COPT 能力探测的契约 / The contract of COPT's capability probe.
/// </summary>
/// <remarks>
/// 这一层判断：**"SDK 有这个方法"与"我们写进去的约束被采纳"是两件事，
/// 而 `Detect` 只回答前者。**
///
/// **这里把这条区分写进用户能读到的地方**（能力声明），而不是只留在台账里。
///
/// The judgment: **"the SDK has this method" and "the constraint we wrote was adopted" are two things, and
/// `Detect` answers only the first.**
///
/// ** puts that distinction where the user can read it — the capability declaration — rather than leaving it in
/// the ledger.**
/// </remarks>
public class CoptFunctionCapabilitiesTests
{
    /// <summary>
    /// **没有 SDK 时一项都不声明，且版本保持 `null`** /
    /// With no SDK nothing is claimed, and the version stays `null`.
    /// </summary>
    /// <remarks>
    /// **`null` 与 `0` 的区别就是这一条判据的全部内容：** **"未探测"和"探测到空"是两件事，
    /// 混起来会读成"支持 0 项，版本 ''"，读者无法区分"这台机器没有 SDK"和"这个 SDK 什么都不支持"。**
    ///
    /// **The distinction between `null` and `0` is all this criterion contains: "not probed" and "probed and empty" are
    /// different facts, and merging them reads as "supports 0, version ''", leaving no way to tell "no SDK here" from
    /// "this SDK supports nothing".**
    /// </remarks>
    [Fact]
    public void WithNoSdk_ShouldClaimNothingAndLeaveTheVersionNull() {
        FunctionSolverCapabilities capabilities = CoptFunctionCapabilities.Detect(modelType: null);

        capabilities.Solver.Should().Be("COPT");
        capabilities.Version.Should().BeNull("an absent SDK is not probed, not probed-and-empty");
        capabilities.Supported.Should().BeEmpty("nothing can be claimed about a type that was never inspected");
    }

    /// <summary>
    /// **探测到的能力不包含"实测"这个断言** /
    /// A detected capability says nothing about having been measured.
    /// </summary>
    /// <remarks>
    /// **这一条判据的内容是<b>没有</b>某个东西：能力声明里不得出现任何"已验证 / verified / measured"的字样。**
    ///
    /// **为什么值得单独钉：** **它们都明确写着"真跑求解器的验证未做"。
    /// 而能力声明是用户读到的第一手材料——如果它在某处暗示"已验证"，
    /// 那么台账里那句诚实的说明就被覆盖了。**
    ///
    /// **This criterion is about <b>the absence</b> of something: the capability declaration must carry no
    /// "verified / measured" wording.**
    ///
    /// **Why it is worth pinning: the capability declarations state plainly that the real-solver verification is not done. The
    /// capability declaration is the first-hand material a user reads — if it implies "verified" anywhere, the honest note
    /// in the ledger is overridden.**
    /// </remarks>
    [Fact]
    public void TheDetectionPath_ShouldNotClaimVerification() {
        string path = Path.Combine(
            RepositoryRoot(),
            "src/Core.Plugin.Copt/CoptFunctionCapabilities.cs");
        string text = File.ReadAllText(path);

        // **只看代码行，不看注释——把判据自己的说明也算进去会让它命中自己。**
        // **Only code lines, not comments: counting the criterion's own explanation would make it hit itself.**
        string[] code = text.Split('\n')
            .Where(static line => !line.TrimStart().StartsWith("//", System.StringComparison.Ordinal))
            .ToArray();

        code.Should().NotContain(line => line.Contains("已验证", System.StringComparison.Ordinal),
            "detection is not verification");
        code.Should().NotContain(line => line.Contains("verified", System.StringComparison.OrdinalIgnoreCase),
            "detection is not verification");
    }

    /// <summary>
    /// **探测按签名匹配，不是按名字** / Detection matches by signature, not by name.
    /// </summary>
    /// <remarks>
    /// **`AddSos` 有三个重载。按名字会命中任意一个，而只有 `(Var[], double[], int)` 是 PWL 需要的那个。**
    /// **所以这里必须用 `GetMethod` 而不是 `GetMember`／`GetMethods` 过滤名字。**
    ///
    /// **这条判据与 `GurobiFunctionCapabilities` 形成对照：** **那里只查名字，并记了一个 `ponytail:` 天花板。
    /// 这里查签名，因为COPT 探测就查签名。**
    ///
    /// **`AddSos` has three overloads; matching by name hits any of them, and only `(Var[], double[], int)` is the one PWL
    /// needs.** **So `GetMethod` must be used, not a name filter over `GetMethods`.**
    ///
    /// **This contrasts with `GurobiFunctionCapabilities`, which checks names only and records a `ponytail:` ceiling. This
    /// one checks signatures, because its COPT probe does.**
    /// </remarks>
    [Fact]
    public void TheProbe_ShouldMatchBySignature() {
        string path = Path.Combine(
            RepositoryRoot(),
            "src/Core.Plugin.Copt/CoptFunctionCapabilities.cs");
        string text = File.ReadAllText(path);

        text.Should().Contain("GetMethod(", "signature matching, not name matching");
        text.Should().Contain("MakeArrayType()", "the array parameter type is computed, not hard-coded");
        text.Should().Contain("ReturnType", "the return type is part of the check");
    }

    /// <summary>
    /// **仓库根目录从测试运行目录往上找** / Walk up to the repository root.
    /// </summary>
    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}
