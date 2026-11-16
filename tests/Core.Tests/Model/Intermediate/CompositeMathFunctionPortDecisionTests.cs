#nullable enable

using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// **为什么本仓没有 `CompositeMathFunction` 这个基类** /
/// Why the `CompositeMathFunction` base class is deliberately not implemented.
/// </summary>
/// <remarks>
/// **这里的结论是一个"不做"的决定，
/// 而它必须带着可执行的理由，否则下一个人会重新做一遍同样的调查。**
///
/// 那个基类只做三件事：
///
/// 1. **`helperVariables` = 所有子函数辅助变量的拼接**
/// 2. **`registerConstraints` = `registerFunctionsAtomically(model, innerFunctions)`**（全成或全不成）
/// 3. **在注册辅助 token 与注册约束之前<b>各校验一次</b>捕获的输入边界**（`validateCapturedPolynomialBounds`）
///
/// **三条在本仓的情况各不相同：**
///
/// | 那一件事 | 本仓状态 | 结论 |
/// | --- | --- | --- |
/// | 辅助变量拼接 | 每个叶子函数各自持有 | 已有，不需要基类 |
/// | 原子注册 | 没有 buffered 注册，但**不可观测** | 不实现 |
/// | 捕获边界校验 | 叶子函数处完全没有 | **如此会得到一个没人赋的字段** |
///
/// **第二条为什么不可观测：** **buffered 注册之所以必要，是因为 `registerConstraints` 可能被
/// 调用在一个<b>已经存活</b>的模型上（比如重注册），那时"失败后留下半截行"会被观察到。
/// 而本仓的构建流程是"每次 `InvokeAsync` 建一个新模型，失败就整个丢弃"——半截行没有观察者。**
///
/// **第三条为什么不能如此：** **`CapturedInputBounds` 这一类字段在本仓
/// <b>没有任何生产者</b>。** **照着校验加进来，加到的就是一个恒为 null 的字段上的检查——
/// 那正是"永不触发的检查"。**
///
/// **所以这里的交付是<b>一个有理由的"不做"</b>，而不是"还没做"。**
///
/// **The result is a "do not implement" decision that must
/// carry executable reasons, or the next person redoes the same investigation.**
///
/// That base class does exactly three things, and the three differ here: helper concatenation already exists per
/// leaf; the atomic registration is <b>unobservable</b> because every `InvokeAsync` builds a fresh model and discards it on
/// failure, so there is no observer for half-written rows; and the captured-bounds validation would attach to a field that
/// <b>nothing produces</b> — precisely the never-firing check that has to be avoided.
///
/// **So this ships a <b>reasoned "no"</b>, not an unfinished "not yet".**
/// </remarks>
public class CompositeMathFunctionPortDecisionTests
{
    /// <summary>
    /// **本仓没有复合函数基类，而每个叶子函数的签名里也没有"捕获边界"这个参数** /
    /// No composite base exists here, and no leaf takes a captured-bounds argument.
    /// </summary>
    /// <remarks>
    /// **这条是缺口判据：** **有人开始实现这个基类（或给叶子加边界参数）时它会变红，
    /// 而那时该做的是先回答"谁来生产那些边界"——那是同一个尚未回答的问题。**
    ///
    /// **This is the gap criterion: it goes red the moment someone starts implementing the base (or adds a bounds argument to a
    /// leaf), and then the first question to answer is "who produces those bounds" — a question still unanswered.**
    /// </remarks>
    [Fact]
    public void ThereShouldBeNoCompositeBaseAndNoBoundsArgument() {
        string root = RepositoryRoot();
        string functionDirectory = System.IO.Path.Combine(root, "src/Core/Symbol/Function");

        string[] files = Directory.GetFiles(functionDirectory, "*.cs", SearchOption.AllDirectories);
        foreach (string path in files) {
            string text = File.ReadAllText(path);
            text.Should().NotContain(
                "class CompositeMathFunction",
                "if the base appears, this criterion must be rewritten to say what made porting worthwhile after all");
        }

        foreach (string leaf in new[] { "PositivePart.cs", "Clamp.cs", "DeadZone.cs", "Distances.cs" }) {
            string path = System.IO.Path.Combine(functionDirectory, leaf);
            if (!File.Exists(path)) {
                continue;
            }
            string text = File.ReadAllText(path);
            text.Should().NotContain(
                "inputBounds",
                $"{leaf} takes no captured input bounds today; adding one requires a producer first");
        }
    }

    /// <summary>
    /// **原子注册所需的原语其实已经在手** / The primitive atomic registration would need already exists.
    /// </summary>
    /// <remarks>
    /// **另一条路是用 `ConstraintBufferModel` 缓冲，然后用 `addConstraints` 一次刷入。**
    /// **本仓没有 buffer，但有 `TruncateConstraintsTo`——"先记行数、失败时截回去"是同一件事的另一种做法，
    /// 而且不需要一个新的模型包装类型。**
    ///
    /// **这条判据的作用是<b>留一张地图</b>：** **如果哪天真需要一个可观测的原子注册
    /// （比如允许在存活模型上重注册），不必从零设计——原语在这里。**
    ///
    /// **而它同时钉住"现在没有这个需要"：** **本仓只有构建期注册，失败即丢弃整个模型。**
    ///
    /// **The other route buffers through `ConstraintBufferModel` and flushes with `addConstraints`. This repo has no buffer but
    /// does have `TruncateConstraintsTo` — "record the row count, truncate on failure" is the same thing without a new
    /// wrapper type.**
    ///
    /// **This criterion is a <b>map</b>: should an observable atomic registration ever be needed (re-registering into a live
    /// model, say), the primitive is here rather than to be designed from scratch — while also pinning that the need does
    /// not exist today, since registration happens only during a build that is discarded on failure.**
    /// </remarks>
    [Fact]
    public void TheAtomicPrimitive_ShouldAlreadyBeAvailable() {
        string root = RepositoryRoot();
        string model = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Model/Mechanism/MechanismModel.cs"));

        model.Should().Contain("TruncateConstraintsTo(",
            "the primitive for an all-or-nothing registration already exists; do not design a buffer model before using it");
        model.Should().NotContain("ConstraintBufferModel",
            "no buffer type is needed while registration happens only during a build that a failure discards");
    }

    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(System.IO.Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}
