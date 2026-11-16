#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// 条件数值与门控：包络证明的保守性 / The conservatism of an envelope proof.
/// </summary>
/// <remarks>
/// 对齐基线 `NativeIndicator.kt` 的 `prepareConditionalValue` 与
/// `NativeMasking.kt` 的 `prepareNativeMasking` 补。
///
/// Adds </remarks>
public class NativeMaskingTests
{
    /// <summary>
    /// **负系数必须对调上下界** / A negative coefficient must swap the bounds.
    /// </summary>
    /// <remarks>
    /// **这是这里最有内容的一条。**
    ///
    /// **包络算法是"按系数符号取对应的那一端"：**
    ///
    /// ```
    /// actualLower = constant + Σ (coefficient > 0 ? coefficient × lb : coefficient × ub)
    /// actualUpper = constant + Σ (coefficient > 0 ? coefficient × ub : coefficient × lb)
    /// ```
    ///
    /// **负系数要把上下界对调——否则一个负系数会把"最坏情形"算成"最好情形"。**
    ///
    /// **判据用一个<b>负系数</b>的算例让差别显形：** **`x ∈ [1, 10]`，表达式 `-2x`，
    /// 声明区间 `[-20, -2]`。**
    ///
    /// | | actualLower | actualUpper |
    /// | --- | --- | --- |
    /// | **对调（正确）** | **`-2 × 10 = -20`** | **`-2 × 1 = -2`** |
    /// | **不对调（错）** | **`-2 × 1 = -2`** | **`-2 × 10 = -20`** |
    ///
    /// **不对调时 `actualLower = -2`、`actualUpper = -20`，于是 `actualLower > actualUpper`——
    /// 而检查是 `actualLower < lower || actualUpper > upper`，`-2 < -20` 为假、
    /// `-20 > -2` 为假，于是<b>误判为通过</b>。**
    ///
    /// **所以不对调的实现会在这个算例上给出"通过"，而正确实现也给出"通过"——
    /// 两者在这个算例上<b>分不开</b>。**
    ///
    /// **必须再加一个<b>声明区间收紧</b>的算例：** **声明 `[-20, -3]`。
    /// 正确实现算出 `actualUpper = -2 > -3`，拒绝；不对调的实现算出 `actualUpper = -20 ≤ -3`，通过。**
    ///
    /// **判据把两个算例都写出来，因为这正是"同一个算例分不开两种实现"的现场。**
    ///
    /// **The batch's most substantive criterion.**
    ///
    /// **The envelope takes the corresponding end by sign** (see the formula above): **a negative coefficient must swap
    /// them, otherwise it would compute the best case where the worst belongs.**
    ///
    /// **The criterion makes the difference visible with a <b>negative</b> coefficient: `x ∈ [1, 10]`, expression `-2x`,
    /// declared range `[-20, -2]`.** **Swapping gives `(-20, -2)`; not swapping gives `(-2, -20)`.**
    ///
    /// **Without the swap `actualLower > actualUpper`, and the check `actualLower < lower || actualUpper > upper` is false
    /// on both sides — so it <b>wrongly passes</b>.**
    ///
    /// **So on that case the broken and the correct implementation <b>cannot be told apart</b>.** **A second case tightens
    /// the declared range to `[-20, -3]`: the correct one computes `actualUpper = -2 > -3` and rejects, the broken one
    /// computes `-20 <= -3` and passes.**
    ///
    /// **The criterion writes both, because this is exactly a case where one example cannot separate two
    /// implementations.**
    /// </remarks>
    [Fact]
    public void ANegativeCoefficient_ShouldSwapTheBounds() {
        // **声明 `[-20, -2]`：两种实现都通过。**
        // **Declared [-20, -2]: both implementations pass.**
        Prepare(Negated(declaredLower: -20.0, declaredUpper: -2.0))
            .Should().BeOfType<
                Ok<NativeConditionalValueData, ErrorCode, Error<ErrorCode>>>();

        // **声明 `[-20, -3]`：正确实现拒绝，不对界的实现通过。**
        // **Declared [-20, -3]: the correct one rejects, the broken one passes.**
        Prepare(Negated(declaredLower: -20.0, declaredUpper: -3.0))
            .Should().BeOfType<
                Failed<NativeConditionalValueData, ErrorCode, Error<ErrorCode>>>(
                "the true maximum is -2, which exceeds the declared -3");
    }

    /// <summary>
    /// **正系数用下界算下界、上界算上界** / A positive coefficient uses lower-for-lower and upper-for-upper.
    /// </summary>
    [Fact]
    public void APositiveCoefficient_ShouldNotSwap() {
        Prepare(Doubled(declaredLower: 2.0, declaredUpper: 20.0))
            .Should().BeOfType<
                Ok<NativeConditionalValueData, ErrorCode, Error<ErrorCode>>>();

        Prepare(Doubled(declaredLower: 2.0, declaredUpper: 19.0))
            .Should().BeOfType<
                Failed<NativeConditionalValueData, ErrorCode, Error<ErrorCode>>>(
                "the true maximum is 20, which exceeds the declared 19");
    }

    /// <summary>
    /// **`LowerBound is null` 这条守卫在真实变量上<b>不可达</b>，所以判据证明的是它不可达** /
    /// The null-bound guard is <b>unreachable</b> on real variables, so the criterion proves that.
    /// </summary>
    /// <remarks>
    /// **第九条"不可测"发现。**
    ///
    /// **我原本写的是"无界变量被拒"。一跑就绿——但变异（把 `return Fail` 改成 `continue`）也绿。**
    ///
    /// **查下来：`RealVar` 的 `Range` <b>总是</b>有默认上下界（约 `±4.5e15`），
    /// 所以 `LowerBound is null` 在真实变量上永远不成立。**
    ///
    /// **也就是说那个 `if` 是一条<b>纯防御</b>分支，靠公开 API 构造不出它的前提。**
    ///
    /// **这解释了两件事：**
    /// - **为什么"无界变量"算例绿**：那个变量其实有界，走的是正常路径
    /// - **为什么变异也绿**：变异改的是一条根本走不到的行
    ///
    /// **所以判据改成<b>断言这个前提不成立</b>：真实变量的上下界都存在。
    /// 这样"那行代码走不到"就从一个推测变成了一个有断言支撑的事实。**
    ///
    /// **同时补一条真正可达的拒绝路径：** **包络越界。** **那才是这个函数在日常使用中
    /// 唯一会拒绝的情形。**
    ///
    /// **The ninth "untestable" finding.**
    ///
    /// **I originally wrote "an unbounded variable is refused". It was green on the first run — but so was the mutation
    /// (changing `return Fail` to `continue`).**
    ///
    /// **What it found: `RealVar`'s `Range` <b>always</b> carries default bounds (about `+-4.5e15`), so
    /// `LowerBound is null` never holds on a real variable.**
    ///
    /// **So that `if` is a <b>purely defensive</b> branch whose precondition the public API cannot construct.**
    ///
    /// **That explains both: why the "unbounded" case was green (the variable was bounded, taking the normal path) and why
    /// the mutation was green (it edited a line that never runs).**
    ///
    /// **The criterion now asserts the precondition does not hold: a real variable has both bounds.** **That turns "the
    /// line never runs" from a guess into a fact with an assertion behind it.**
    ///
    /// **A genuinely reachable rejection is added alongside it: the envelope leaving the declared range — the only refusal
    /// this function performs in daily use.**
    /// </remarks>
    [Fact]
    public void TheNullBoundGuard_ShouldBeUnreachable() {
        RealVar plain = new("nmk-plain");

        plain.LowerBound.Should().NotBeNull(
            "a real variable always carries default bounds, so the null guard cannot fire");
        plain.UpperBound.Should().NotBeNull();

        // **真正可达的拒绝：包络越界。`x ∈ [1, 10]`、表达式 `2x`、
        // 声明 `[2, 20]` 通过，声明 `[2, 19]` 被拒。**
        //
        // **The genuinely reachable refusal: the envelope leaving the range. With x in [1, 10] and 2x, declaring [2, 20]
        // passes while [2, 19] is rejected.**
        Prepare(Doubled(declaredLower: 2.0, declaredUpper: 19.0))
            .Should().BeOfType<
                Failed<NativeConditionalValueData, ErrorCode, Error<ErrorCode>>>(
                "the true maximum is 20, which exceeds the declared 19");
    }

    /// <summary>
    /// **结果变量出现在自己的输入里被拒** / The result appearing in its own input is refused.
    /// </summary>
    /// <remarks>
    /// **`result = result + 1` 无法用包络证明：** **把 `result` 的上下界代进去，
    /// 得到的包络必然比 `result` 自己的范围宽——而那样的证明是循环的。**
    ///
    /// **`result = result + 1` cannot be proven by an envelope: substituting the result's own bounds yields an envelope
    /// necessarily wider than its range, and that proof is circular.**
    /// </remarks>
    [Fact]
    public void TheResultInItsOwnInput_ShouldBeRefused() {
        RealVar bounded = Bounded("nmk-self", -10.0, 10.0);

        var value = new ConditionalValue(
            Input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(1.0), bounded) },
                new Flt64(1.0)),
            ResultVariable: bounded,
            Bounds: new ConditionBounds<Flt64>(new Flt64(-100.0), new Flt64(100.0)),
            Name: "value");

        NativeMasking.PrepareConditionalValue(value, IIntoValue<Flt64>.Identity)
            .Should().BeOfType<
                Failed<NativeConditionalValueData, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **零系数在返回前被丢掉** / Zero coefficients are dropped before returning.
    /// </summary>
    /// <remarks>
    /// **它们不影响包络（已经算过了），但会让指纹与渲染带上无意义的项。**
    ///
    /// **判据用两个系数相加为零的项：** **`1x + (-1)x` 合并成 `0`，于是 `Terms` 为空。**
    /// **如果实现在合并后不丢零，`Terms` 会有一个值为 `0` 的项。**
    ///
    /// **They no longer affect the envelope (already computed) but would put meaningless terms into fingerprints and
    /// rendering.**
    ///
    /// **The criterion uses two coefficients summing to zero: `1x + (-1)x` combines to `0`, so `Terms` is empty.** **An
    /// implementation that keeps zero after combining leaves a term with value `0`.**
    /// </remarks>
    [Fact]
    public void ZeroCoefficients_ShouldBeDropped() {
        Result<NativeConditionalValueData, ErrorCode, Error<ErrorCode>> result =
            NativeMasking.PrepareConditionalValue(
                new ConditionalValue(
                    Input: new LinearPolynomial<Flt64>(
                        new[] {
                            new LinearMonomial<Flt64>(new Flt64(1.0), X),
                            new LinearMonomial<Flt64>(new Flt64(-1.0), X),
                        },
                        new Flt64(3.0)),
                    ResultVariable: Result,
                    Bounds: new ConditionBounds<Flt64>(new Flt64(-10.0), new Flt64(10.0)),
                    Name: "value"),
                IIntoValue<Flt64>.Identity);

        result.Should().BeOfType<
            Ok<NativeConditionalValueData, ErrorCode, Error<ErrorCode>>>();
        result.Value.Terms.Should().BeEmpty(
            "the two coefficients cancel and the zero term is dropped");
        result.Value.Constant.Should().Be(3.0);
    }

    /// <summary>
    /// **包络是保守的：它假设所有变量同时取极值** / The envelope is conservative: it assumes all variables reach extremes at once.
    /// </summary>
    /// <remarks>
    /// **这是这套算法最重要的一个性质，也是最容易被当成 bug 的一个。**
    ///
    /// **`x ∈ [0, 10]`、`y ∈ [0, 10]`，表达式 `x + y`：** **包络是 `[0, 20]`。**
    ///
    /// **但真实可行域也许有 `x + y ≤ 10` 的约束——那时 `x + y` 永远到不了 `20`。**
    ///
    /// **所以声明 `[0, 20]` 通过、声明 `[0, 15]` 被拒，
    /// 而后者其实是一个<b>合法的、更紧的</b>声明。**
    ///
    /// **这是"拒绝一个能用的模型比接受一个不能用的模型安全"的具体体现。**
    /// **判据把这个"误拒"写成断言，而不是把它当成缺陷绕过。**
    ///
    /// **The most important property of this algorithm, and the one most likely mistaken for a bug.**
    ///
    /// **With `x, y ∈ [0, 10]` and the expression `x + y`, the envelope is `[0, 20]`.**
    ///
    /// **The real feasible region may carry `x + y <= 10`, so the sum can never reach `20`.**
    ///
    /// **So declaring `[0, 20]` passes and declaring `[0, 15]` is rejected — while the latter is a <b>legal, tighter</b>
    /// declaration.**
    ///
    /// **That is "refusing a usable model beats accepting an unusable one" made concrete.** **The criterion asserts the
    /// over-rejection rather than routing around it.**
    /// </remarks>
    [Fact]
    public void TheEnvelope_ShouldOverReject() {
        var sum = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(new Flt64(1.0), X),
                new LinearMonomial<Flt64>(new Flt64(1.0), Y),
            },
            new Flt64(0.0));

        NativeMasking.PrepareConditionalValue(
                new ConditionalValue(sum, Result,
                    new ConditionBounds<Flt64>(new Flt64(0.0), new Flt64(20.0)), "value"),
                IIntoValue<Flt64>.Identity)
            .Should().BeOfType<
                Ok<NativeConditionalValueData, ErrorCode, Error<ErrorCode>>>();

        NativeMasking.PrepareConditionalValue(
                new ConditionalValue(sum, Result,
                    new ConditionBounds<Flt64>(new Flt64(0.0), new Flt64(15.0)), "value"),
                IIntoValue<Flt64>.Identity)
            .Should().BeOfType<
                Failed<NativeConditionalValueData, ErrorCode, Error<ErrorCode>>>(
                "the envelope cannot see the coupling constraint, so it refuses a tighter legal range");
    }

    /// <summary>
    /// **门控：定义不能引用 mask 自己或结果列** / Masking: the definition cannot reference the mask or the result.
    /// </summary>
    [Fact]
    public void TheMaskDefinition_ShouldNotReferenceItsOwnResult() {
        MaskingStructure SelfReference(IReadOnlyList<IVariableItem> items) =>
            new(
                Value: new ConditionalValue(
                    new LinearPolynomial<Flt64>(
                        new[] { new LinearMonomial<Flt64>(new Flt64(1.0), X) },
                        new Flt64(0.0)),
                    Result, new ConditionBounds<Flt64>(new Flt64(-10.0), new Flt64(10.0)),
                    "value"),
                Mask: Mask,
                Converter: IIntoValue<Flt64>.Identity) {
                MaskDefinition = new LinearPolynomial<Flt64>(
                    items.Select(static item =>
                        new LinearMonomial<Flt64>(new Flt64(1.0), item)).ToList(),
                    new Flt64(0.0)),
            };

        NativeMasking.Prepare(SelfReference(new IVariableItem[] { Mask }))
            .Should().BeOfType<Failed<NativeMaskingData, ErrorCode, Error<ErrorCode>>>(
                "the definition cannot reference the mask itself");

        NativeMasking.Prepare(SelfReference(new IVariableItem[] { Result }))
            .Should().BeOfType<Failed<NativeMaskingData, ErrorCode, Error<ErrorCode>>>(
                "the definition cannot reference the result column either");

        NativeMasking.Prepare(SelfReference(new IVariableItem[] { X }))
            .Should().BeOfType<Ok<NativeMaskingData, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **门控：有定义时带上定义，无定义时为 `null`** / With a definition it is carried; without one it is null.
    /// </summary>
    [Fact]
    public void TheDefinition_ShouldBeOptional() {
        MaskingStructure Build(LinearPolynomial<Flt64>? definition) =>
            new(
                Value: new ConditionalValue(
                    new LinearPolynomial<Flt64>(
                        new[] { new LinearMonomial<Flt64>(new Flt64(1.0), X) },
                        new Flt64(0.0)),
                    Result, new ConditionBounds<Flt64>(new Flt64(-10.0), new Flt64(10.0)),
                    "value"),
                Mask: Mask,
                Converter: IIntoValue<Flt64>.Identity) {
                MaskDefinition = definition,
            };

        NativeMasking.Prepare(Build(null)).Value.Definition.Should().BeNull();
        NativeMasking.Prepare(Build(
                new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(new Flt64(2.0), X) },
                    new Flt64(1.0))))
            .Value.Definition!.Constant.Should().Be(1.0);
    }

    private static Result<NativeConditionalValueData, ErrorCode, Error<ErrorCode>> Prepare(
        ConditionalValue value) =>
        NativeMasking.PrepareConditionalValue(value, IIntoValue<Flt64>.Identity);

    private static ConditionalValue Negated(double declaredLower, double declaredUpper) =>
        new(
            Input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(-2.0), X) },
                new Flt64(0.0)),
            ResultVariable: Result,
            Bounds: new ConditionBounds<Flt64>(
                new Flt64(declaredLower), new Flt64(declaredUpper)),
            Name: "value");

    private static ConditionalValue Doubled(double declaredLower, double declaredUpper) =>
        new(
            Input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2.0), X) },
                new Flt64(0.0)),
            ResultVariable: Result,
            Bounds: new ConditionBounds<Flt64>(
                new Flt64(declaredLower), new Flt64(declaredUpper)),
            Name: "value");

    private static RealVar Bounded(string name, double lower, double upper) {
        var variable = new RealVar(name);
        variable.Range.Geq(new Flt64(lower));
        variable.Range.Leq(new Flt64(upper));
        return variable;
    }

    /// <summary>**`x ∈ [1, 10]`：负系数算例的前提。** / x in [1, 10]: the premise of the negative case.</summary>
    private static readonly RealVar X = Bounded("nmk-x", 1.0, 10.0);

    private static readonly RealVar Y = Bounded("nmk-y", 0.0, 10.0);

    private static readonly RealVar Result = new("nmk-result");

    private static readonly BinVar Mask = new("nmk-mask");
}
