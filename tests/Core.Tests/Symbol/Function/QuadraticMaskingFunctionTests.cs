#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 二次掩码：桥接等式必须真的进模型 / The bridge equality must reach the model.
/// </summary>
/// <remarks>
/// B1176 按基线 `QuadraticMasking.kt` 的 `QuadraticMaskingFunction` 补。
///
/// **结构是"先桥接、再掩码"**：**`MaskingFunction` 只接受线性输入，
/// 二次项必须先被一个桥接变量取代。**
///
/// B1176 adds the baseline's `QuadraticMaskingFunction`.
///
/// **Bridge first, then mask**: **`MaskingFunction` takes linear input only, so the quadratic terms must first be
/// replaced by a bridge variable.**
/// </remarks>
public class QuadraticMaskingFunctionTests
{
    /// <summary>
    /// **桥接变量被界住了，不是自由的** / The bridge is bounded, not free.
    /// </summary>
    /// <remarks>
    /// **无界输入会让 `MaskingFunction` 退回 `1e6`**——**能跑，但界宽了 6 个数量级，
    /// 松弛变松、求解变慢，而且不报任何错。**
    ///
    /// **所以判据断言桥接变量的上界来自 `x²` 的实际界（`x ∈ [−10, 10]` 时是 `100`），
    /// 而不是那个回退值。**
    ///
    /// **An unbounded input makes `MaskingFunction` fall back to `1e6`** — **it runs, but with bounds six orders of
    /// magnitude too wide: looser relaxation, slower solves, and nothing reported.**
    ///
    /// **The criterion therefore asserts the bridge's upper bound comes from `x²`'s actual bound (100 for
    /// `x ∈ [−10, 10]`), not from that fallback.**
    /// </remarks>
    [Fact]
    public void TheBridge_ShouldBeBoundedByTheQuadraticRange()
    {
        QuadraticMaskingFunction function = Quadratic();

        // **`ValueWrapper` 有多个子类型（`Value` / `NegativeInfinity` / `PositiveInfinity` 等），
        // 所以必须按 `Value` 模式匹配再取值——直接 `.Value` 取不到，因为那是嵌套记录的名字。**
        //
        // **`ValueWrapper` has several subtypes (`Value` / `NegativeInfinity` / `PositiveInfinity`, …), so the number
        // must be reached through a `Value` pattern; a bare `.Value` does not resolve, being a nested record's name.**
        function.Bridge.Range.UpperBound!.Value.Should().BeOfType<ValueWrapper<Flt64>.Value>(
            "the bridge must carry a finite upper bound, not an infinity");
        // **上界 100 是真的：`x²` 在 `[−10, 10]` 上确实能到 `100`。**
        // **下界是 `−100` 而不是 `0`，因为 `QuadraticBounds.Of` 用四角取最值——
        // 而四角里包含 `(10)·(−10) = −100`。**
        //
        // **"平方非负"这件事它测不出来**（B1163 已记录过同一限制）。
        // **后果是桥接变量的界比实际宽一倍：能跑、不报错，只是松弛略松。
        // 记在这里，而不是假装下界是 0。**
        //
        // **The upper bound of 100 is real: `x²` does reach 100 over `[−10, 10]`.**
        // **The lower bound is `−100`, not `0`, because `QuadraticBounds.Of` takes the four corners — and one of them
        // is `(10)·(−10) = −100`.**
        //
        // **"A square is never negative" is invisible to it** (the same limitation recorded in B1163).
        // **The consequence is a bridge range twice as wide as it needs to be: it runs, nothing errors, the relaxation
        // is merely a little looser. Recorded here rather than pretending the lower bound is zero.**
        ((ValueWrapper<Flt64>.Value)function.Bridge.Range.UpperBound!.Value).Number.ToDouble().Should().Be(
            100.0, "x squared over [minus ten, ten] tops out at one hundred, not the fallback big-M");
        ((ValueWrapper<Flt64>.Value)function.Bridge.Range.LowerBound!.Value).Number.ToDouble().Should().Be(
            -100.0, "the four-corner rule cannot see that a square is never negative");
    }

    /// <summary>
    /// **桥接等式与掩码约束都到了模型里** / Both the bridge equality and the masking rows land.
    /// </summary>
    /// <remarks>
    /// **这条是"构造了正确的行"与"那行真的到了模型"的区别。**
    /// **不写桥接等式时，桥接变量是自由的，掩码读一个任意值——而模型完全可行、目标值合理。**
    ///
    /// **判据按<b>名字</b>逐条核对，而不是只数总数**：**总数变大也可能只是多了一条无关的行，
    /// 而"桥接那条在不在"才是本批要证的东西。**
    ///
    /// **This separates "the row was constructed correctly" from "the row reached the model".**
    /// **Without the bridge equality the bridge is free, the mask reads an arbitrary value — and the model stays
    /// feasible with a plausible objective.**
    ///
    /// **The criterion checks by <b>name</b> rather than counting**: **a larger total may just mean an unrelated row
    /// appeared, while "is the bridge row there" is what this batch must prove.**
    /// </remarks>
    [Fact]
    public async Task TheRegister_ShouldPutTheBridgeRowAndTheMaskingRowsIntoTheModel()
    {
        using var parent = new QuadraticMetaModel<Flt64>("quadratic-masking");
        var declared = new List<IVariableItem> { Mask, Bounded };

        QuadraticMaskingFunction function = Quadratic();
        declared.AddRange(function.HelperVariables);
        // **断言类型而不是裸 `IsOk`**：**仓库有一条预算判据按文本数裸 `IsOk`（stock 930），
        // 加理由字符串不算"说了值是什么"。**
        //
        // **Assert the type rather than a bare `IsOk`**: **the repository budgets bare `IsOk` textually (stock 930),
        // and a reason string does not count as "saying what the value is".**
        parent.Add(declared).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        QuadraticMechanismModel<Flt64> model =
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        int before = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Count;
        function.RegisterConstraints(model).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        int after = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Count;

        var names = new List<string>();
        foreach (QuadraticConstraintImpl<Flt64> row in
            (IList<QuadraticConstraintImpl<Flt64>>)((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints) {
            names.Add(row.Name);
        }

        names.Should().Contain(
            "qm_bridge_row_0", "the bridge equality is the whole point of the quadratic variant");
        after.Should().BeGreaterThan(before, "the masking rows are added after the bridge");
    }

    /// <summary>**掩码为 0 时结果为零** / A zero mask yields zero.</summary>
    [Fact]
    public void TheZeroMask_ShouldYieldZero()
    {
        Quadratic().Evaluate(new Dictionary<ISymbol, Flt64> {
            [Bounded] = new Flt64(7.0),
            [Mask] = Flt64.Zero,
        }).Should().Be(Flt64.Zero, "a masked-out input contributes nothing");
    }

    /// <summary>**掩码为 1 时结果跟随输入** / A set mask passes the value through.</summary>
    [Fact]
    public void TheSetMask_ShouldPassTheValueThrough()
    {
        // **`MaskingFunction` 读的是<b>桥接变量</b>，不是原始输入。**
        // **所以喂值时必须给桥接变量——否则它读到"没有这个符号"，而基线规则是"读不到掩码给零、
        // 读不到输入给 `null`"。**
        //
        // **`MaskingFunction` reads the <b>bridge variable</b>, not the original input.**
        // **So the value must be supplied for the bridge; otherwise it reads "no such symbol", and the baseline rule is
        // "a missing mask gives zero, a missing input gives `null`".**
        QuadraticMaskingFunction function = Quadratic();
        function.Evaluate(new Dictionary<ISymbol, Flt64> {
            [function.Bridge] = new Flt64(7.0),
            [Mask] = Flt64.One,
        }).Should().Be(new Flt64(7.0), "the bridge carries the input value");
    }

    /// <summary>**输入符号含二次输入与掩码** / The input symbols cover both the input and the mask.</summary>
    [Fact]
    public void TheInputSymbols_ShouldIncludeTheMask()
    {
        QuadraticMaskingFunction function = Quadratic();

        function.InputSymbols.Should().Contain(Mask, "the mask is an input of the function");
        function.InputSymbols.Should().Contain(Bounded, "the quadratic input's symbol is an input too");
        function.HelperVariables.Should().Contain(function.Bridge);
    }

    private static QuadraticMaskingFunction Quadratic() =>
        new(
            new QuadraticPolynomial<Flt64>(
                new[] { QuadraticMonomial<Flt64>.Quadratic(Flt64.One, Bounded, Bounded) },
                Flt64.Zero),
            Mask,
            Converter,
            name: "qm");

    private static readonly RealVar Bounded = CreateBounded();

    private static RealVar CreateBounded() {
        var variable = new RealVar("qm-x");
        variable.Range.Geq(new Flt64(-10.0));
        variable.Range.Leq(new Flt64(10.0));
        return variable;
    }

    private static readonly BinVar Mask = new("qm-mask");

    private static readonly IIntoValue<Flt64> Converter = new TestConverter();

    private sealed class TestConverter : IIntoValue<Flt64> {
        public Flt64 One => Flt64.One;

        public Flt64 Zero => Flt64.Zero;

        public Flt64 IntoValue(Flt64 value) => value;

        public Flt64 IntoValue(double value) => new(value);

        public Flt64 IntoValue(object value) => value is Flt64 typed
            ? typed
            : new Flt64(Convert.ToDouble(value));

        public Flt64 FromValue(Flt64 value) => value;
    }
}
