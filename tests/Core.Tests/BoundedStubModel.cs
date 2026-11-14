// 带边界的回调模型测试替身 / A bounded call-back-model test double.
//
// **为什么需要它**：在此之前全仓没有任何测试能走到"边界夹紧"这条路径。`StubModel` 的令牌表为空，
// `NumericSupport.LowerBoundOf` 恒返回 `null`，于是从 Batch 159（变异）到 Batch 168（解更新）之间所有
// "结果在边界内"的断言都是恒真的——生成值本来就落在那个区间里，断言无论如何都通过。
//
// **Why it exists**: before this, no test in the repository could reach the bound-clamping path. `StubModel`'s token table is empty,
// `NumericSupport.LowerBoundOf` always returns `null`, so every "result is within bounds" assertion between Batch 159 (mutation) and
// Batch 168 (solution update) was vacuously true — the generated values already sat inside the interval and the assertion passed
// regardless.
//
// 关键点在于 `ExpressionRange.SetRange` 是 public 的：给变量挂上区间只需要一行，不必经约束系统。
// The key fact is that `ExpressionRange.SetRange` is public: attaching an interval to a variable takes one line and does not need the
// constraint system.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 带边界的回调模型 / A call-back model with real variable bounds.
/// </summary>
internal sealed class BoundedStubModel<TObj> : IAbstractCallBackModelInterface<TObj, Flt64, Flt64> {
    /// <summary>
    /// 构造带边界的模型 / Construct a bounded model.
    /// </summary>
    /// <param name="dimension">变量数量 / The variable count.</param>
    /// <param name="lower">每维下界 / The lower bound per dimension.</param>
    /// <param name="upper">每维上界 / The upper bound per dimension.</param>
    public BoundedStubModel(int dimension, double lower, double upper) {
        var table = new ConcurrentAutoTokenTable<Flt64>(
            LinearCategory.Instance,
            Array.Empty<IIntermediateSymbol>(),
            null,
            true);
        for (int index = 0; index < dimension; index++) {
            var variable = new RealVar($"x{index}");
            variable.Range.SetRange(
                ValueRange<Flt64>.Of(new Flt64(lower), new Flt64(upper), new Interval.Closed(), new Interval.Closed())
                    .Value!);
            table.Add(variable);
        }
        Tokens = table;
    }

    /// <summary>默认目标值 / The default objective.</summary>
    public Flt64 DefaultObjective => new(0.0);

    /// <summary>目标方向 / The objective direction.</summary>
    public ObjectCategory ObjectCategory => ObjectCategory.Minimum;

    /// <summary>变量令牌表 / The variable token table.</summary>
    public IAbstractMutableTokenTable<Flt64> Tokens { get; }

    /// <summary>约束 / The constraints.</summary>
    public IReadOnlyList<(Func<IReadOnlyList<Flt64>, bool?> Extractor, string Name)> Constraints =>
        Array.Empty<(Func<IReadOnlyList<Flt64>, bool?>, string)>();

    /// <summary>目标函数 / The objective functions.</summary>
    public IReadOnlyList<(Func<IReadOnlyList<Flt64>, TObj?> Extractor, string Name)> ObjectiveFunctions =>
        Array.Empty<(Func<IReadOnlyList<Flt64>, TObj?>, string)>();

    /// <summary>目标运算 / The objective operation.</summary>
    /// <param name="lhs">左值 / The left value.</param>
    /// <param name="rhs">右值 / The right value.</param>
    /// <returns>运算结果 / The result.</returns>
    public Flt64 Operation(Flt64 lhs, Flt64 rhs) => lhs.Plus(rhs);

    /// <summary>默认目标值 / The default objective value.</summary>
    /// <returns>目标值 / The objective value.</returns>
    public Flt64 ObjectiveValue() => DefaultObjective;

    /// <summary>目标对象的值 / The value of an objective object.</summary>
    /// <param name="obj">目标对象 / The objective object.</param>
    /// <returns>目标值 / The objective value.</returns>
    public Flt64 ObjectiveValue(TObj obj) => DefaultObjective;

    /// <summary>第一个分量作为目标 / The first component serves as the objective.</summary>
    /// <param name="solution">解向量 / The solution vector.</param>
    /// <returns>目标值 / The objective value.</returns>
    public Flt64 Objective(IReadOnlyList<Flt64> solution) => solution[0];

    /// <summary>按目标比较 / Compare by objective.</summary>
    /// <param name="lhs">左值 / The left value.</param>
    /// <param name="rhs">右值 / The right value.</param>
    /// <returns>比较结果 / The comparison.</returns>
    public Order CompareObjective(Flt64 lhs, Flt64 rhs) => OrderHelpers.OrderOf(lhs.CompareTo(rhs));

    /// <summary>约束恒满足 / Constraints always hold.</summary>
    /// <param name="solution">解向量 / The solution vector.</param>
    /// <returns>是否满足 / Whether it holds.</returns>
    public bool? ConstraintSatisfied(IReadOnlyList<Flt64> solution) => true;

    /// <summary>刷新 / Flush.</summary>
    public void Flush() { }

    /// <summary>释放 / Dispose.</summary>
    public void Dispose() { }
}

/// <summary>
/// 带边界模型的自检 / Self-checks for the bounded model.
/// </summary>
/// <summary>
/// 带边界模型的自检 / Self-checks for the bounded model.
/// </summary>
public sealed class BoundedStubModelTests {
    /// <summary>**边界确实能读到** / The bounds are actually readable.</summary>
    /// <remarks>
    /// 这条是整套边界测试的地基：如果它不成立，所有"结果在边界内"的断言又会退化成恒真。
    /// This is the foundation of every bounds test: if it fails, every "within bounds" assertion degrades to vacuously true again.
    /// </remarks>
    [Fact]
    public void Bounds_ShouldBeReadable() {
        var model = new BoundedStubModel<object?>(3, -10.0, 10.0);

        NumericSupport.LowerBoundOf<object?, Flt64, Flt64>(model, 0, IIntoValue<Flt64>.Identity)
            .Should().Be(-10.0);
        NumericSupport.UpperBoundOf<object?, Flt64, Flt64>(model, 2, IIntoValue<Flt64>.Identity)
            .Should().Be(10.0);
    }

    /// <summary>**越界下标仍返回 null** / Out-of-range indices still yield null.</summary>
    [Fact]
    public void Bounds_ShouldBeNullOutOfRange() {
        var model = new BoundedStubModel<object?>(2, -1.0, 1.0);

        NumericSupport.LowerBoundOf<object?, Flt64, Flt64>(model, 5, IIntoValue<Flt64>.Identity)
            .Should().BeNull();
    }

    /// <summary>**与空令牌表的对照** / Contrast with the empty token table.</summary>
    /// <remarks>
    /// 明确指出 `StubModel` 读不到边界，正是此前所有边界断言恒真的原因。
    /// Explicitly records that `StubModel` cannot read bounds, which is exactly why every earlier bounds assertion was vacuous.
    /// </remarks>
    [Fact]
    public void PlainStub_ShouldHaveNoBounds() {
        var model = new StubModel(value => value[0]);

        NumericSupport.LowerBoundOf<object?, Flt64, Flt64>(model, 0, IIntoValue<Flt64>.Identity)
            .Should().BeNull("空令牌表读不到边界，这正是此前断言恒真的原因 / an empty table has no bounds");
    }
}
