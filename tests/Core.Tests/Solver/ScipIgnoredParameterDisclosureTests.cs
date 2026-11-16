#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// 报告的“未生效参数”只装真的未生效的 / The report's "not applied" bag holds only what was not applied.
/// </summary>
/// <remarks>
/// **SCIP 的一处披露被更正：**`parallel/maxnthreads` 被记进 `ignoredParameters`，而它<b>已经生效</b>。
///
/// **`ignoredParameters` 的文档语义是"已声明但未生效的参数"（`SolverReportMetadata`）。**
/// **于是这里出现了一个不可能的句子：一个被 `SetIntParameter` 成功设下去、失败时还会 `ParameterFailure` 的参数，
/// 同时被报告声称没有生效。**
///
/// **这个错误的特殊之处：它不会让任何行为判据变红。** **线程上限真的被设下去了，
/// 求解行为完全正确；错的只是那句关于自己的话。**
///
/// **所以判据必须读源码，而不是跑求解。** **这与前几批的结构判据同源，但锚点不同：
/// 前几批数"定义次数"，这里查"某个键是否出现在某个表里"。**
///
/// **A SCIP disclosure was corrected:** `parallel/maxnthreads` was recorded in `ignoredParameters` while it <b>is</b>
/// applied.
///
/// **`ignoredParameters` is documented as "requested parameters that were not applied".** **So the report contained an
/// impossible sentence: a parameter that is successfully set via `SetIntParameter` — and returns `ParameterFailure` when
/// it fails — was also claimed not to be applied.**
///
/// **What makes this error special: it turns no behavioural criterion red.** **The thread limit really is set and the
/// solving behaviour is entirely correct; what was wrong is the sentence about itself.**
///
/// **So the criterion must read source rather than run a solve.** **Same family as the earlier structural criteria, but a
/// different anchor: those counted definitions, this one asks whether a key appears in a particular table.**
/// </remarks>
public class ScipIgnoredParameterDisclosureTests
{
    /// <summary>
    /// **已生效的参数不得出现在 `ignoredParameters` 里** /
    /// An applied parameter must not appear in `ignoredParameters`.
    /// </summary>
    [Fact]
    public void AnAppliedParameter_ShouldNotBeReportedAsIgnored() {
        string path = Path.Combine(
            RepositoryRoot(),
            "src/Core.Plugin.Scip/ScipLinearNativeSolver.cs");
        string text = File.ReadAllText(path);

        // **它确实被设下去了。**
        // **It really is set.**
        text.Should().Contain(
            "name: \"parallel/maxnthreads\"",
            "the parameter is applied through SetIntParameter");

        // **所以它不得被记进 `ignored`。**
        // **So it must not be recorded under `ignored`.**
        text.Should().NotContain(
            "ignored[\"parallel/maxnthreads\"]",
            "that bag means 'not applied', and this parameter is applied");
    }

    /// <summary>
    /// **`ignored` 与 `effective` 都只装真参数，不装"事实"** /
    /// Both bags hold real parameters only, never "facts".
    /// </summary>
    /// <remarks>
    /// **这一条钉的是这里<b>一度写错</b>的方向：** **我起初把差异塞成 `effective["concurrentSolving"]`，
    /// 那就凭空造出了一个 SCIP 没有的参数名——而读取方会以为它是一个可以下发的设置。**
    ///
    /// **"不是参数的事实"该写在文档里，不该伪装成参数。**
    ///
    /// **判据因此检查两个表里都不出现 `concurrentSolving` 这个键。**
    /// **它同时挡住了"把能力差异写成参数"这种更隐蔽的失真。**
    ///
    /// **This pins the direction <b>got wrong once</b>: I first put the difference in as
    /// `effective["concurrentSolving"]`, which fabricates a parameter name SCIP does not have — and a reader would take it
    /// for a setting that can be sent.**
    ///
    /// **A fact that is not a parameter belongs in the docs, not disguised as one.**
    ///
    /// **So the criterion checks that neither bag carries a `concurrentSolving` key.** **It also blocks the subtler
    /// distortion of writing a capability difference as a parameter.**
    /// </remarks>
    [Fact]
    public void NeitherBag_ShouldCarryAFabricatedParameterName() {
        string path = Path.Combine(
            RepositoryRoot(),
            "src/Core.Plugin.Scip/ScipLinearNativeSolver.cs");
        string text = File.ReadAllText(path);

        text.Should().NotContain(
            "effective[\"concurrentSolving\"]",
            "a capability difference is not a SCIP parameter");
        text.Should().NotContain(
            "ignored[\"concurrentSolving\"]",
            "a capability difference is not a SCIP parameter");
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
