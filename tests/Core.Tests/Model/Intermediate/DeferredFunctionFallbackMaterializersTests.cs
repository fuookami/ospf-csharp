#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 物化层：三种失败必须分开 / The three failures must stay apart.
/// </summary>
/// <remarks>
/// 对齐基线 `DeferredFunctionFallback.kt` 的六个物化器。
///
/// **"用错物化器"与"能力还没有"是两件事，修法完全不同。**
///
/// **"Wrong materializer" and "capability absent" are different things with different fixes.**
/// </remarks>
public class DeferredFunctionFallbackMaterializersTests
{
    /// <summary>
    /// **类型不对与未移植给出不同的消息** / A wrong type and an unported row generator say different things.
    /// </summary>
    /// <remarks>
    /// **这是这里的核心判据。**
    ///
    /// **`Pwl` 物化器收到一个 `SemiStructure`：类型不对。**
    /// **`Pwl` 物化器收到一个 `UnivariateLinearPiecewiseStructure`：类型对，但行生成还没移植。**
    ///
    /// **两条消息都必须出现，而且<b>必须不同</b>。**
    /// **合成一条的话，调用方看到"不支持"会先去查自己传错了类型，而真正的原因是
    /// 那个能力在这里还不存在——排查方向直接错掉。**
    ///
    /// **This is the batch's core criterion.**
    ///
    /// **The `Pwl` materializer receiving a `SemiStructure` is a type mismatch; receiving a
    /// `UnivariateLinearPiecewiseStructure` is the right type with an unported row generator.**
    ///
    /// **Both messages must exist and <b>must differ</b>.** **Collapsed into one, a caller seeing "unsupported" checks its
    /// own type first while the real cause is an absent capability — the investigation goes the wrong way.**
    /// </remarks>
    [Fact]
    public void TheWrongType_ShouldStillBeReportedAsSuch()
    {
        Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> wrongType =
            DeferredFunctionFallbackMaterializers.Pwl.Materialize(Semi());

        wrongType.Should().BeOfType<
            Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();

        string wrongMessage =
            ((Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>)wrongType)
            .Error.Message;

        wrongMessage.Should().Contain("unsupported");
        wrongMessage.Should().Contain("SemiStructure");

        // **此后"未移植"这一格空了：七个结构里已经没有不产生行的。
        // 所以这里改为断言那条措辞<b>不再出现</b>——否则它就成了一个永远为假的类别。**
        //
        // **The "not ported" cell is empty: none of the seven structures lacks row generation.
        // So this asserts that wording <b>no longer appears</b>, rather than keeping a category with no instances.**
        IDeferredFunctionFallbackMaterializer core = DeferredFunctionFallbackMaterializers.Core;
        foreach (DeferredFunctionStructure structure in new DeferredFunctionStructure[] {
            Semi(), Logic(), Masking(), Indicator(), Abs(), Max(), Piecewise(),
        }) {
            Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> result =
                core.Materialize(structure);
            string detail = result is Failed<
                DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> failed
                    ? failed.Error.Message
                    : string.Empty;

            detail.Should().NotContain(
                "not ported yet",
                "every structure now generates rows, so the port-gap wording has no instances");
            detail.Should().NotContain("unsupported");
        }
    }

    /// <summary>
    /// **三个结构明确未移植，四个能物化** / Three structures are explicitly unported; four can materialize.
    /// </summary>
    /// <remarks>
    /// **物化层的完整性由<b>行生成</b>决定，而不是由分派表决定。**
    ///
    /// **判据逐个结构过一遍，把"能"与"不能"都固定下来：
    /// PWL、MAX 不能；SEMI、二值逻辑、指示、掩码、ABS 能。**
    ///
    /// **ABS 的行生成补上之后，分法从 4/3 变成 5/2。**
    ///
    /// **只断言"能的那四个能"的话，三个空着的格子永远不会被注意到。**
    ///
    /// **The layer's completeness is decided by <b>row generation</b>, not by the dispatch table.**
    ///
 /// **The criterion walks every structure; all seven materialize.**
    ///
    /// **With ABS row generation in place the split goes from 4/3 to 5/2.**
    ///
    /// **Asserting only that the four can work would leave the three blank cells unnoticed.**
    /// </remarks>
    [Fact]
    public void TheSevenStructures_ShouldAllMaterialize()
    {
        IDeferredFunctionFallbackMaterializer core = DeferredFunctionFallbackMaterializers.Core;

        foreach (DeferredFunctionStructure structure in new DeferredFunctionStructure[] {
            Semi(), Logic(), Masking(), Indicator(), Abs(), Max(), Piecewise(),
        }) {
            Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> result =
                core.Materialize(structure);
            string detail = result is Failed<
                DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> failed
                    ? failed.Error.Message
                    : "ok";
            result.Should().BeOfType<
                Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>(
                $"{structure.GetType().Name} should materialize, got: {detail}");
        }

        // **此后没有"能/不能"的分法了——七格全满。**
        // **There is no can/cannot split left; all seven cells are full.**
        PiecewiseMaterializes(core).Should().BeTrue(
            "the last unported cell was PWL, and it is closed now");
    }

    /// <summary>
    /// **内层的失败原样传出，不折成"不支持"** / An inner failure is forwarded, never folded into "unsupported".
    /// </summary>
    /// <remarks>
    /// **二值逻辑结构在输入非法时自己会失败（例如输入不是二值）。
    /// 那条消息带着具体原因，折成"不支持"会让它消失。**
    ///
    /// **判据构造一个输入非二值的逻辑结构，断言物化器传出来的是那条具体消息。**
    ///
    /// **A binary-logic structure fails on its own when an input is not binary.** **That message carries the concrete
    /// cause, and folding it into "unsupported" loses it.**
    ///
    /// **The criterion builds a logic structure with a non-binary input and asserts the concrete message comes through.**
    /// </remarks>
    [Fact]
    public void TheInnerFailure_ShouldComeThrough()
    {
        var broken = new BinaryLogicStructure(
            Operation: BinaryLogicOperation.And,
            Inputs: new[] { (IVariableItem)Real },
            ResultVariable: Bit, Converter: IIntoValue<Flt64>.Identity, Name: "broken");

        Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> result =
            DeferredFunctionFallbackMaterializers.Core.Materialize(broken);

        result.Should().BeOfType<
            Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();
        string message =
            ((Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message;
        message.Should().Contain("invalid binary logic structure inputs");
        message.Should().NotContain("unsupported");
        message.Should().NotContain("not ported yet");
    }

    /// <summary>
    /// **专用物化器只接受自己的类型** / A specialized materializer accepts only its own type.
    /// </summary>
    /// <remarks>
    /// **`Semi` 收到二值逻辑结构必须拒绝——即使 core 物化器两条都能处理。**
    /// **若某个专用物化器"顺手"接下了不属于它的类型，
    /// 那么 core 里的分派就形同虚设，而错误会出现在别处。**
    ///
    /// **`Semi` must reject a binary-logic structure even though the core materializer handles both.**
    /// **A specialized materializer that "helpfully" takes a foreign type makes the core dispatch decorative, and the error
    /// surfaces elsewhere.**
    /// </remarks>
    [Fact]
    public void TheSpecialized_ShouldRejectForeignTypes()
    {
        DeferredFunctionFallbackMaterializers.Semi.Materialize(Logic()).Should()
            .BeOfType<Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();
        DeferredFunctionFallbackMaterializers.BinaryLogic.Materialize(Semi()).Should()
            .BeOfType<Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();

        DeferredFunctionFallbackMaterializers.Semi.Materialize(Semi()).Should()
            .BeOfType<Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();
        DeferredFunctionFallbackMaterializers.BinaryLogic.Materialize(Logic()).Should()
            .BeOfType<Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **core 物化器对完全未知的类型给出 `deferred function` 消息** /
    /// The core materializer reports an unknown type as `deferred function`.
    /// </summary>
    /// <remarks>
    /// **分派表里没有的类型必须落到最后那一支，而不是静默返回空列表。**
    /// **"静默返回空约束"是最危险的失败方式：模型照常建立、求解成功、只是少了那个函数的行。**
    ///
    /// **A type missing from the dispatch table must fall to the final branch rather than quietly returning an empty
    /// list.** **"Quietly empty" is the most dangerous failure: the model builds, solves fine, and simply lacks that
    /// function's rows.**
    /// </remarks>
    [Fact]
    public void TheUnknownType_ShouldNotReturnAnEmptyList()
    {
        Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> result =
            DeferredFunctionFallbackMaterializers.Core.Materialize(new UnknownStructure());

        result.Should().BeOfType<
            Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();
        ((Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("unsupported deferred function fallback structure");
        ((Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("UnknownStructure");
    }

    private sealed record UnknownStructure : DeferredFunctionStructure;

    private static SemiStructure Semi() =>
        new(LowerBound: new Flt64(3.0), UpperBound: new Flt64(10.0),
            ResultVariable: Real, IndicatorVariable: Bit,
            Converter: IIntoValue<Flt64>.Identity, Name: "semi");

    private static BinaryLogicStructure Logic() =>
        new(BinaryLogicOperation.And, new[] { Bit }, Bit,
            IIntoValue<Flt64>.Identity, "logic");

    private static UnivariateLinearPiecewiseStructure Piecewise() =>
        new(Input: new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
            Breakpoints: new[] { new Flt64(0.0), new Flt64(1.0) },
            Slopes: new[] { Flt64.One }, Intercepts: new[] { Flt64.Zero },
            ResultVariable: Real, SelectorVariables: new[] { Bit }) {
            // **给了显式 M 就不需要输入范围证明；转换器仍然必需（行要落成 Flt64）。**
            // **An explicit M removes the need for an input-range proof; the converter is still required so the rows land
            // as Flt64.**
            ExplicitM = new Flt64(100.0),
            Converter = IIntoValue<Flt64>.Identity,
        };

    // **`AbsStructure` 要三个辅助列与两个 Big-M：正、负、符号，以及正负两侧的 M。**
    // **`AbsStructure` needs three helper columns and two Big-Ms: positive, negative and sign.**
    private static AbsStructure Abs() => new(
        Input: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
        // **三个辅助列必须互不相同——键唯一性校验会拒绝复用。**
        // **The three helpers must be distinct; the earlier key-uniqueness check rejects reuse.**
        ResultVariable: Real, PositiveVariable: AbsPos, NegativeVariable: AbsNeg, SignVariable: Bit,
        PositiveBigM: new Flt64(100.0), NegativeBigM: new Flt64(100.0),
        Converter: IIntoValue<Flt64>.Identity);

    private static MaxStructure Max() => new(
        Inputs: new[] {
            new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
        },
        ResultVariable: Real, SelectorVariables: new[] { Bit },
        BigMValues: new[] { new Flt64(100.0) },
        Converter: IIntoValue<Flt64>.Identity);

    private static MaskingStructure Masking() =>
        new(Value: new ConditionalValue(
                Input: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                ResultVariable: Real,
                Bounds: new ConditionBounds<Flt64>(new Flt64(-1.0), new Flt64(1.0)),
                Name: "cv"),
            Mask: Bit, Converter: IIntoValue<Flt64>.Identity);

    private static IndicatorStructure Indicator() => new(
        Input: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
        // **这里的 `ResultVariable` 是<b>指示列</b>（必须是二值），不是数值结果列。**
        // **Here `ResultVariable` is the <b>indicator</b> column (binary), not a numeric result.**
        ResultVariable: Bit, BigM: new Flt64(100.0), Tolerance: new Flt64(1e-6),
        Converter: IIntoValue<Flt64>.Identity, Name: "ind");

    private static readonly RealVar Real = new("mat-real");

    private static readonly BinVar Bit = new("mat-bit");

    private static readonly RealVar AbsPos = new("mat-abs-pos");

    private static readonly RealVar AbsNeg = new("mat-abs-neg");

    private static bool PiecewiseMaterializes(IDeferredFunctionFallbackMaterializer core) =>
        core.Materialize(Piecewise()) is
            Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>;
}
