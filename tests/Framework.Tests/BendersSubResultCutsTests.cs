using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests;

/// <summary>
/// B837：Benders 子问题结果**必须真的把割交出来** / A Benders sub-result must really hand back its cuts.
/// </summary>
/// <remarks>
/// 这三条判据原来钉在 **Core 版**的 `IBendersDecompositionSolver` 上——那一版把割放在一个**计算属性**里，
/// 而派生记录用位置参数声明同名成员**压不住**它，于是接口转回基类、再转自己：**每次读 `Cuts` 都栈溢出**（实测 24,001 帧），
/// 而且**传进去的列表根本没被保存**（B835 修的就是这个）。
///
/// B836 发现 Core 版**一个实现者都没有**（六个插件实现的都是框架版），B837 直接把它删了，
/// 判据搬到框架版这份**唯一剩下的实现**上。框架版从结构上就不会出那个问题：
/// **割是基类构造函数的参数**（`LinearSubResult(cuts)`），派生记录把同一个值传上去——存下来的，不是算出来的。
///
/// 这三条判据的价值不只是"读一次不炸"，而是**钉住"交出来的就是传进去的那一份"**：位置参数与基类成员同名时，
/// C# 很容易让参数被"吃掉"而不报警（B835 里就是这样），**只有 `BeSameAs` 能发现**。
///
/// These three criteria used to pin the **core** version of `IBendersDecompositionSolver`, which kept the cuts in a **computed property** while the derived records
/// declared a same-named positional parameter that **could not override** it — the interface returned to the base and then to itself: **every read of `Cuts` overflowed
/// the stack** (measured at 24,001 frames), and the list passed in **was never stored** (what B835 fixed). B836 found that the core version had **no implementers** at
/// all (all six plugins implement the framework one), so B837 deleted it and moved the criteria onto the framework version — the only remaining implementation.
/// Structurally it cannot have that problem: **the cuts are a base-constructor parameter** (`LinearSubResult(cuts)`), and the derived records pass the same value up —
/// stored, not computed. Their value is not just "reading does not crash" but **"the value handed out is the very one handed in"**: when a positional parameter and a
/// base member share a name, C# can let the parameter be swallowed without any warning (which is what happened in B835), and **only `BeSameAs` catches it**.
/// </remarks>
public sealed class BendersSubResultCutsTests
{
    private static List<LinearInequality<Flt64>> Cuts() => [];

    private static FeasibleSolverOutput<Flt64> Output() => new(
        Obj: Flt64.Zero,
        Solution: new Solution<Flt64>([Flt64.Zero]),
        Time: TimeSpan.Zero,
        PossibleBestObj: Flt64.Zero,
        Gap: Flt64.Zero);

    /// <summary>
    /// **可行结果：基类读出来的就是传进去的那一份列表** / A feasible result hands back the very list it was given.
    /// </summary>
    [Fact]
    public void AFeasibleResult_ShouldReturnTheStoredCuts() {
        List<LinearInequality<Flt64>> cuts = Cuts();

        var result = new IBendersDecompositionSolver.LinearFeasibleResult(
            Output(),
            new Dictionary<MathConstraint, Flt64>(),
            cuts);

        IBendersDecompositionSolver.LinearSubResult asBase = result;
        asBase.Cuts.Should().BeSameAs(cuts);
    }

    /// <summary>
    /// **不可行结果同理** / The infeasible result behaves the same way.
    /// </summary>
    /// <remarks>
    /// 两条都要测：两个记录各自把割传给基类，**漏掉一个不会让另一个变红**。
    ///
    /// Both are covered: each record passes its cuts to the base separately, so **missing one would not turn the other red**.
    /// </remarks>
    [Fact]
    public void AnInfeasibleResult_ShouldReturnTheStoredCuts() {
        List<LinearInequality<Flt64>> cuts = Cuts();

        IBendersDecompositionSolver.LinearSubResult asBase =
            new IBendersDecompositionSolver.LinearInfeasibleResult(
                new Dictionary<MathConstraint, Flt64>(),
                cuts);

        asBase.Cuts.Should().BeSameAs(cuts);
    }

    /// <summary>
    /// **没有割时读出来是 `null`，不是空列表** / No cuts reads as `null`, not as an empty list.
    /// </summary>
    /// <remarks>
    /// 下游（`BendersSolver`）用 `if (cuts != null)` 判"这一轮有没有割"。把 `null` 换成空列表，判据不会红、循环照样跑，
    /// **但"没有割"和"有零个割"变成了同一件事**。
    ///
    /// Downstream (`BendersSolver`) tests `if (cuts != null)` for "did this round produce cuts". Turning `null` into an empty list keeps the criteria green and the
    /// loop running, **but makes "no cuts" and "zero cuts" the same thing.**
    /// </remarks>
    [Fact]
    public void AResultWithoutCuts_ShouldReadAsNull() {
        var result = new IBendersDecompositionSolver.LinearFeasibleResult(
            Output(),
            new Dictionary<MathConstraint, Flt64>(),
            Cuts: null);

        ((IBendersDecompositionSolver.LinearSubResult)result).Cuts.Should().BeNull();
    }
}
