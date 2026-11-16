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
/// 指示结构：保留列集合必须在递归之前完整 / The retained set must be complete before recursing.
/// </summary>
/// <remarks>
/// 对齐基线 `NativeIndicator.kt` 的 `prepareNativeIndicator` / `prepareIndicator`。
///
/// Adds </remarks>
public class NativeIndicatorTests
{
    /// <summary>
    /// **基本形状：系数、常数、阈值、M 与区间** / The basic shape: terms, constant, threshold, M and range.
    /// </summary>
    [Fact]
    public void TheBasicShape_ShouldBeCarried() {
        Result<NativeIndicatorData, ErrorCode, Error<ErrorCode>> result =
            NativeIndicator.Prepare(Basic());

        result.Should().BeOfType<Ok<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>();
        NativeIndicatorData data = result.Value;
        data.Constant.Should().Be(0.0);
        data.Tolerance.Should().Be(1.0);
        data.BigM.Should().Be(100.0);
        data.ResultKey.Should().Be(Result.Key);
        data.Terms.Should().HaveCount(1);
        data.Terms[X.Key].Should().Be(2.0);
        data.LowerBound.Should().Be(-100.0, "with no condition bounds the range falls back to +-M");
        data.UpperBound.Should().Be(100.0);
    }

    /// <summary>
    /// **零带容差的严格性由<b>上游</b>负责，本层的同一检查不可达** /
    /// The band's strictness belongs to the <b>upstream</b> layer; this layer's copy is unreachable.
    /// </summary>
    /// <remarks>
    /// **第十条"不可测"发现，而且它是一条<b>惰性重复</b>。**
    ///
    /// **我写了一条"`band == tolerance` 被拒"的判据，一跑就绿。但变异（把 `>=` 改成 `>`）<b>也绿</b>。**
    ///
    /// **查下来：`IndicatorStructure.GenerateConstraints` 在更早的地方就检查了**
    /// **（`ZeroBand.Tolerance >= Tolerance` 直接 `Reject`），
    /// 而本函数<b>第一步</b>就是调用 `GenerateConstraints`。**
    ///
    /// **所以本层那个 `bandTolerance >= tolerance` 永远走不到——
    /// 走到那里时它已经被上游拒掉了。**
    ///
    /// **这份实现照抄了基线的重复检查（基线也有这两层），所以<b>保留</b>它；
    /// 但判据必须说清楚<b>是哪一层</b>在生效，否则下一个人会以为本层被覆盖了。**
    ///
    /// **判据现在的断言是：三档都被拒/通过，但原因记在上游。**
    /// **"同一件事有两道检查"与"两道检查都必要"是两件事。**
    ///
    /// **The tenth "untestable" finding, and an <b>idle duplicate</b>.**
    ///
    /// **I wrote a criterion saying "`band == tolerance` is rejected"; it was green, but the mutation (`>=` to `>`)
    /// <b>was green too</b>.**
    ///
    /// **What it found: `IndicatorStructure.GenerateConstraints` already checks `ZeroBand.Tolerance >= Tolerance` and
    /// rejects, and this function's <b>first step</b> is to call it.**
    ///
    /// **So this layer's own `bandTolerance >= tolerance` never runs — by the time control reaches it, the upstream has
    /// already refused.**
    ///
    /// **This implementation copies the baseline's duplicate check (the baseline has both layers too), so it is
    /// <b>kept</b>; but the criterion must say <b>which layer</b> is doing the work, otherwise the next reader assumes this
    /// one is covered.**
    ///
    /// **"Two checks for one thing" and "both checks are needed" are different statements.**
    /// </remarks>
    [Fact]
    public void TheBandTolerance_ShouldBeRejectedUpstream() {
        NativeIndicator.Prepare(Banded(0.5))
            .Should().BeOfType<Ok<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>();

        // **`band == tolerance` 与 `band > tolerance` 都被拒——但拒绝发生在
        // `GenerateConstraints` 里，不是在本层。**
        //
        // **Both `band == tolerance` and `band > tolerance` are rejected — but inside `GenerateConstraints`, not here.**
        foreach (double band in new[] { 1.0, 1.5 }) {
            Result<NativeIndicatorData, ErrorCode, Error<ErrorCode>> result =
                NativeIndicator.Prepare(Banded(band));

            result.Should().BeOfType<
                Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>();
            ((Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>)result)
                .Error.Message.Should().Contain(
                    "zero band",
                    "the rejection comes from GenerateConstraints, which this layer calls first");
        }
    }

    /// <summary>
    /// **阈值与 M 必须<b>严格为正** / The threshold and M must both be <b>strictly positive</b>.
    /// </summary>
    [Fact]
    public void ToleranceAndBigM_ShouldBeStrictlyPositive() {
        NativeIndicator.Prepare(With(tolerance: 0.0))
            .Should().BeOfType<Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>(
                "a zero threshold leaves the true branch undefined");

        NativeIndicator.Prepare(With(bigM: 0.0))
            .Should().BeOfType<Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>(
                "a zero M cannot relax the fallback");

        // **正例要用足够大的 M：** **`x ∈ [1, 10]`、系数 `2` 的包络是 `[2, 20]`，
        // 而缺省条件区间就是 `±M`——所以 `M` 必须 `< 20` 才能装下它。**
        // **原算例写 `bigM: 0.5`，装不下，失败是对的：那是算例的问题，不是实现的问题。**
        //
        // **The positive case needs a large enough M: with `x in [1, 10]` and coefficient `2` the envelope is `[2, 20]`,
        // and the default condition range is `+-M`, so M must exceed 20.** **The original case used `bigM: 0.5`, which
        // cannot hold it — the failure was right, and the case was wrong.**
        NativeIndicator.Prepare(With(tolerance: 0.5, bigM: 1000.0))
            .Should().BeOfType<Ok<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **输入不能碰自己这一族的保留列** / The input must not touch its own family's retained columns.
    /// </summary>
    /// <remarks>
    /// **这是这里最有内容的一条：** **`resultKeys` 收集了结果列、等价结果、零带侧列、
    /// 以及合取/差值的两个结果列，而主输入的每一个项都不许是其中之一。**
    ///
    /// **判据逐个来源测：** **结果列、等价结果、零带侧列、合取结果、差值结果。**
    /// **只测结果列的话，另外四条收集路径可以整段缺失而判据全绿。**
    ///
    /// **The batch's most substantive criterion: `resultKeys` collects the result, the equivalents, the zero-band side
    /// column, and both result columns of the conjunction and difference; no main-input term may be any of them.**
    ///
    /// **The criterion covers each source: the result, an equivalent, the band side, the conjunction result, the difference
    /// result.** **Testing only the result would let the other four collection paths vanish unnoticed.**
    /// </remarks>
    [Fact]
    public void TheInput_ShouldNotTouchAnyRetainedColumn() {
        // **输入项是结果列自己。**
        // **The input term is the result column itself.**
        NativeIndicator.Prepare(Basic(inputVariable: Result))
            .Should().BeOfType<Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>();

        // **输入项是等价结果：也被拒，但同样由上游拒（见下面那条"归属"注释）。**
        // **The input term is an equivalent result: rejected too, also upstream (see the attribution note below).**
        NativeIndicator.Prepare(Basic() with { EquivalentResults = new[] { X } })
            .Should().BeOfType<Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>(
                "an equivalent result is retained, so it cannot be an input");

        // **输入项是零带侧列。**
        // **The input term is the zero-band side column.**
        NativeIndicator.Prepare(Banded(0.5, sideVariable: X))
            .Should().BeOfType<Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>(
                "the band's side column is retained, so it cannot be an input");

        // **输入项是合取结果。**
        // **The input term is the conjunction result.**
        NativeIndicator.Prepare(Basic() with {
            Conjunction = new ConjoinedIndicator(SubCondition(), X, "and"),
        }).Should().BeOfType<Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>(
            "the conjunction result is retained, so it cannot be an input");

        // **输入项是差值结果。**
        // **The input term is the difference result.**
        NativeIndicator.Prepare(Basic() with {
            Difference = new DifferenceIndicator(SubCondition(), X, "xor"),
        }).Should().BeOfType<Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>(
            "the difference result is retained, so it cannot be an input");
    }

    /// <summary>
    /// **哪些拒绝由本层负责、哪些由上游负责** / Which refusals belong here and which to the upstream layer.
    /// </summary>
    /// <remarks>
    /// **这是一条"归属"判据，写它是因为这里有两条变异<b>抓不住</b>，而原因都是上游先拒掉。**
    ///
    /// **`GenerateConstraints` 会检查的东西（本层的同名检查因此不可达）：**
    ///
    /// | 检查 | 上游 `GenerateConstraints` | 本层 |
    /// | --- | --- | --- |
    /// | **结果列是二值** | ✅ 拒 | **不可达** |
    /// | **零带容差 `< tolerance`** | ✅ 拒 | **不可达** |
    /// | **等价结果与输入重合** | ✅ 拒 | **不可达** |
    /// | **合取/差值结果与输入重合** | ✅ 拒 | **不可达** |
    ///
    /// **本层真正独有的：**
    ///
    /// | 检查 | 理由 |
    /// | --- | --- |
    /// | **数值可用性（`usable`）** | **上游只看符号关系，不看数值范围与 `maximumMagnitude`** |
    /// | **包络落在条件区间内** | **上游不知道变量的界** |
    /// | **子条件递归后的 `terms` 不碰本族保留列** | **上游看不到递归结果** |
    /// | **`positiveOnZero` 与蕴含后件冲突** | **上游不看这对语义** |
    /// | **后件常数与阈值的关系** | **同上** |
    ///
    /// **所以"本层的检查不可达"不等于"本层多余"——
    /// 而是说它的作用被上游覆盖了，本层的价值在别的几条。**
    ///
    /// **这也解释了为什么这里只有 2 个变异能抓住：** **另外两个测的是上游的行为，
    /// 改本层当然不会红。**
    ///
    /// **An "attribution" criterion, written because two mutations in <b>could not be caught</b> — both because
    /// the upstream refuses first.**
    ///
    /// **What `GenerateConstraints` checks, making this layer'schecks of the same name unreachable** (see the first table). **What is
    /// genuinely unique to this layer** (see the second table).
    ///
    /// **So "this layer's check is unreachable" does not mean "this layer is redundant" — its checks are covered
    /// upstream, and its real value is elsewhere.**
    ///
    /// **That also explains why only two mutations could be caught here: the other two test upstream behaviour, and
    /// editing this layer naturally cannot turn them red.**
    /// </remarks>
    [Fact]
    public void TheRefusalOwnership_ShouldBeRecorded() {
        // **本层独有的第一条：数值可用性。`maximumMagnitude` 只被本层看。**
        // **The first unique one: numeric usability. Only this layer reads maximumMagnitude.**
        NativeIndicator.Prepare(Basic(), maximumMagnitude: 1.0)
            .Should().BeOfType<Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>(
                "the coefficients exceed the magnitude limit, which the upstream never inspects");

        // **本层独有的第二条：包络必须落在条件区间内——上游不知道变量的界。**
        // **The second unique one: the envelope must fit the condition range; the upstream knows nothing of variable
        // bounds.**
        NativeIndicator.Prepare(Basic() with {
            ConditionBounds = new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(5.0)),
        }).Should().BeOfType<Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>(
            "the envelope [2, 20] exceeds [-5, 5], a fact only this layer can see");
    }

    /// <summary>
    /// **子条件不能碰本族的保留列** / A sub-condition must not touch this family's retained columns.
    /// </summary>
    /// <remarks>
    /// **这就是"`resultKeys` 必须在递归<b>之前</b>完整"的用处所在：**
    /// **子条件的合法性取决于它有没有碰到本族保留列，而那个集合只有看完了合取/差值/零带的声明才完整。**
    ///
    /// **先递归再收集，就会让一个子条件在还不知道自己不许碰哪些列的情况下通过检查。**
    ///
    /// **判据让子条件的输入项恰为本族的结果列：** **那时子条件自己的准备会失败
    /// （子条件的结果就是它自己），而本族这一层<b>也</b>该报错。**
    ///
    /// **This is where "resultKeys must be complete before recursing" earns its keep: a sub-condition's legality depends
    /// on this family's retained columns, which are only complete after the conjunction/difference/zero-band declarations
    /// are read.**
    ///
    /// **Recursing first would let a sub-condition pass before knowing which columns it must not touch.**
    ///
    /// **The criterion makes the sub-condition's input term exactly this family's result: the sub-condition's own
    /// preparation then fails (its result is itself), and this layer must fail too.**
    /// </remarks>
    [Fact]
    public void ASubCondition_ShouldNotTouchRetainedColumns() {
        // **合取的子条件，其输入项是本族的结果列。**
        // **A conjunction sub-condition whose input term is this family's result.**
        NativeIndicator.Prepare(Basic(inputVariable: Y) with {
            Conjunction = new ConjoinedIndicator(
                Basic(inputVariable: Result, resultVariable: SubResult), X, "and"),
        }).Should().BeOfType<Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>();

        // **正常情形：子条件干净、合取结果不碰本族保留列。**
        // **The normal case: a clean sub-condition and a conjunction result outside this family's retained set.**
        NativeIndicator.Prepare(Basic(inputVariable: Y) with {
            Conjunction = new ConjoinedIndicator(
                Basic(inputVariable: X, resultVariable: SubResult), ConjResult, "and"),
        }).Should().BeOfType<Ok<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **`positiveOnZero` 与蕴含后件不能并存** / `positiveOnZero` and an implied consequent cannot coexist.
    /// </summary>
    /// <remarks>
    /// **前者让结果 0 激活正分支，后者让前件激活后件——两者对同一个 0 给出相反的约定。**
    ///
    /// **The former activates the positive branch on result zero, the latter activates the consequent — two opposite
    /// conventions for the same zero.**
    /// </remarks>
    [Fact]
    public void PositiveOnZero_ShouldConflictWithAnImpliedConsequent() {
        IndicatorStructure Implied(bool positiveOnZero) => Basic(inputVariable: Y) with {
            PositiveOnZero = positiveOnZero,
            ImpliedCondition = new ImpliedCondition(
                new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(new Flt64(1.0), Z) }, new Flt64(0.0)),
                ImpliedIndicator,
                new ConditionBounds<Flt64>(new Flt64(-10.0), new Flt64(10.0)),
                "implied"),
        };

        NativeIndicator.Prepare(Implied(positiveOnZero: true))
            .Should().BeOfType<Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>(
                "the two give opposite conventions for result zero");

        NativeIndicator.Prepare(Implied(positiveOnZero: false))
            .Should().BeOfType<Ok<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **条件数值结果不能是本族保留列，但可以是后件结果** /
    /// The conditional value must not be a retained column, yet it may be the consequent's result.
    /// </summary>
    /// <remarks>
    /// **这是两套集合的不对称，照抄基线：**
    ///
    /// | 集合 | 内容 | 谁被拒 |
    /// | --- | --- | --- |
    /// | **`resultKeys`** | **结果、等价、零带侧、合取/差值结果** | **主输入项、子条件项** |
    /// | **单独列出的两个** | **`conditionalValue.ResultKey`、`impliedCondition.ResultKey`** | **只拒主输入项** |
    ///
    /// **所以一个变量可以同时是"条件数值结果"与"某一族的等价结果"而不冲突——
    /// 而它如果是主输入的项就会被拒。**
    ///
    /// **The asymmetry between two sets, copied from the baseline** (see the table above).
    ///
    /// **So a variable can be both a conditional-value result and some family's equivalent result without conflict — while
    /// the same variable as a main-input term is refused.**
    /// </remarks>
    [Fact]
    public void TheConditionalValue_ShouldNotBeARetainedColumn() {
        NativeIndicator.Prepare(Basic(inputVariable: Y) with {
            EquivalentResults = new[] { CondValueResult },
            ConditionalValue = CondValue(result: CondValueResult),
        }).Should().BeOfType<Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>(
            "it is already an equivalent result, which is retained");

        NativeIndicator.Prepare(Basic(inputVariable: Y) with {
            ConditionalValue = CondValue(result: CondValueResult),
        }).Should().BeOfType<Ok<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>(
            "on its own a conditional-value result is legal");
    }

    /// <summary>
    /// **条件区间缺省时回落到 `±M`，给定时被采用** / The condition range falls back to +-M or is taken as given.
    /// </summary>
    [Fact]
    public void TheConditionRange_ShouldFallBackToBigM() {
        NativeIndicator.Prepare(Basic()).Value.LowerBound.Should().Be(-100.0);
        NativeIndicator.Prepare(Basic()).Value.UpperBound.Should().Be(100.0);

        // **声明区间要被采用，且必须装得下包络 `[2, 20]`，所以用 `[-30, 30]`。**
        // **The declared range must be adopted and hold the envelope [2, 20], so [-30, 30].**
        NativeIndicator.Prepare(Basic() with {
            ConditionBounds = new ConditionBounds<Flt64>(new Flt64(-30.0), new Flt64(30.0)),
        }).Value.LowerBound.Should().Be(-30.0);

        // **包络越界时被拒：`x ∈ [1, 10]`、`2x`，声明 `[-5, 5]` 装不下 `[2, 20]`。**
        // **Out-of-range envelopes are refused: x in [1, 10] with 2x cannot fit inside [-5, 5].**
        NativeIndicator.Prepare(Basic() with {
            ConditionBounds = new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(5.0)),
            Input = Twice(X),
        }).Should().BeOfType<Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>>();
    }

    private static LinearPolynomial<Flt64> Twice(IVariableItem variable) =>
        new(new[] { new LinearMonomial<Flt64>(new Flt64(2.0), variable) }, new Flt64(0.0));

    private static IndicatorStructure Basic(
        IVariableItem? inputVariable = null, IVariableItem? resultVariable = null) =>
        new(
            Input: new LinearPolynomial<Flt64>(
                new[] {
                    new LinearMonomial<Flt64>(new Flt64(2.0), inputVariable ?? X),
                },
                new Flt64(0.0)),
            ResultVariable: resultVariable ?? Result,
            BigM: new Flt64(100.0),
            Tolerance: new Flt64(1.0),
            Converter: IIntoValue<Flt64>.Identity,
            Name: "indicator");

    private static IndicatorStructure Banded(double band, IVariableItem? sideVariable = null) =>
        Basic() with {
            ZeroBand = new ZeroBand<Flt64>(new Flt64(band), sideVariable ?? BandSide),
        };

    private static IndicatorStructure With(double tolerance = 1.0, double bigM = 100.0) =>
        new(
            Input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2.0), X) }, new Flt64(0.0)),
            ResultVariable: Result,
            BigM: new Flt64(bigM),
            Tolerance: new Flt64(tolerance),
            Converter: IIntoValue<Flt64>.Identity,
            Name: "indicator");

    /// <summary>**一个干净的子条件，结果是 `SubResult`。** / A clean sub-condition whose result is `SubResult`.</summary>
    private static IndicatorStructure SubCondition() =>
        Basic(inputVariable: X, resultVariable: SubResult);

    private static ConditionalValue CondValue(IVariableItem result) =>
        new(
            Input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(1.0), Z) }, new Flt64(0.0)),
            ResultVariable: result,
            Bounds: new ConditionBounds<Flt64>(new Flt64(-10.0), new Flt64(10.0)),
            Name: "cond");

    private static RealVar Bounded(string name, double lower, double upper) {
        var variable = new RealVar(name);
        variable.Range.Geq(new Flt64(lower));
        variable.Range.Leq(new Flt64(upper));
        return variable;
    }

    private static readonly RealVar X = Bounded("ni-x", 1.0, 10.0);

    private static readonly RealVar Y = Bounded("ni-y", 0.0, 10.0);

    private static readonly RealVar Z = Bounded("ni-z", -10.0, 10.0);

    /// <summary>**结果列必须是二值的：`GenerateConstraints` 的第一道检查就是它。** / The result must be binary.</summary>
    private static readonly BinVar Result = new("ni-result");

    private static readonly BinVar SubResult = new("ni-sub");

    private static readonly BinVar ConjResult = new("ni-conj");

    private static readonly RealVar CondValueResult = new("ni-condv");

    private static readonly BinVar BandSide = new("ni-band");

    private static readonly BinVar ImpliedIndicator = new("ni-implied");
}
