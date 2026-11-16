#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 事务化驱动：三种失败结局 / Three failure outcomes.
/// </summary>
/// <remarks>
/// 对齐基线 `DeferredFunctionFallback.kt` 的 `materializeDeferredFunctionFallbacks` 与 `rollbackErrors`。
///
/// **回滚目标是"循环开始前的行数"，不是"上一个结构之前"。**
///
/// adds
///
/// **The rollback target is the row count before the loop, not before the previous structure.**
/// </remarks>
public class DeferredFunctionFallbackDriverTests
{
    /// <summary>
    /// **失败时回滚到<b>开始时</b>的行数，已成功的结构也要撤掉** /
    /// A failure rolls back to the starting count, undoing successful structures too.
    /// </summary>
    /// <remarks>
    /// **这是本批的核心判据。**
    ///
    /// **第一个结构成功（追加了 2 行），第二个失败。
    /// 如果只回滚"上一个结构之前"，那 2 行会留下，调用方拿到一个部分成功的模型层。**
    ///
    /// **判据让目标记录每一次 `Rollback` 的实参，断言它是<b>起始值</b>而不是中间值。**
    ///
    /// **This is the batch's core criterion.**
    ///
    /// **The first structure succeeds (two rows appended) and the second fails.** **Rolling back only "before the previous
    /// structure" leaves those two rows behind and hands the caller a partly successful model layer.**
    ///
    /// **The criterion records every `Rollback` argument and asserts it is the <b>starting</b> value, not an
    /// intermediate one.**
    /// </remarks>
    [Fact]
    public void AFailure_ShouldRollBackToTheStartNotOneStep() {
        var target = new RecordingTarget(initial: 5);
        var call = new MaterializerCall { Steps = new[] { Success(2), Fail() } };

        Result<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>> result =
            DeferredFunctionFallbackDriver.Materialize(
                new DeferredFunctionStructure[] { Logical(), Broken() },
                target,
                new ScriptedMaterializer(call));

        result.Should().BeOfType<
            Failed<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>>>();

        target.Rollbacks.Should().HaveCount(1);
        target.Rollbacks[0].Should().Be(
            5, "the checkpoint is the count before the loop, so already-appended rows are undone too");
        target.Rows.Should().Be(5, "the target is back where it started");
    }

    /// <summary>
    /// **回滚也失败时升级成 `Fatal`，且两个错误都留** /
    /// A failed rollback becomes `Fatal` with both errors kept.
    /// </summary>
    /// <remarks>
    /// **`Failed` 的语义是"可以再试一次"，而回滚失败意味着模型层停在未知状态——
    /// 再试一次不会更糟，但也不会更好。所以必须当作不可恢复。**
    ///
    /// **两个错误都要留：原始错误说明"为什么开始回滚"，回滚错误说明"为什么现在回不去"。**
    ///
    /// **`Failed` means "retryable", while a failed rollback leaves the model layer in an unknown state — retrying is
    /// neither worse nor better, so it must count as unrecoverable.**
    ///
    /// **Both errors are kept: the original says "why the rollback started", the rollback error says "why it cannot get
    /// back now".**
    /// </remarks>
    [Fact]
    public void AFailedRollback_ShouldBecomeFatalWithBothErrors() {
        var target = new RecordingTarget(initial: 5) { FailRollback = true };
        var call = new MaterializerCall { Steps = new[] { Success(2), Fail() } };

        Result<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>> result =
            DeferredFunctionFallbackDriver.Materialize(
                new DeferredFunctionStructure[] { Logical(), Broken() },
                target,
                new ScriptedMaterializer(call));

        result.Should().BeOfType<
            Fatal<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>>>();
        ((Fatal<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>>)result)
            .Errors.Should().HaveCount(2, "the original failure and the rollback failure");

        // **`Result<T, C, E>` 自带 `Select` 扩展，`IEnumerable.Select` 会被它遮蔽——用显式循环。**
        // **`Result<T, C, E>` ships its own `Select` extension that shadows `IEnumerable.Select`; use an explicit loop.**
        var messages = new List<string>();
        foreach (Error<ErrorCode> error in ((Fatal<
            IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>>)result)
            .Errors) {
            messages.Add(error.Message);
        }

        string joined = string.Join(" | ", messages);
        joined.Should().Contain("scripted failure");
        joined.Should().Contain("scripted rollback failure");
    }

    /// <summary>
    /// **全部成功时给出每个结构的区域，且区域连续** /
    /// On success each structure gets a region, and the regions are contiguous.
    /// </summary>
    /// <remarks>
    /// **区域是 `[first, first + count)`。**
    /// **判据断言"后一个的起点等于前一个的末端"——这同时检查了 `first` 记录在追加<b>之前</b>，
    /// 以及 `count` 是追加的<b>实际</b>行数。**
    ///
    /// **Regions are `[first, first + count)`.**
    /// **The criterion asserts "the next start equals the previous end", which checks both that `first` was recorded
    /// <b>before</b> appending and that `count` is the <b>actual</b> number of rows appended.**
    /// </remarks>
    [Fact]
    public void OnSuccess_TheRegionsShouldBeContiguous() {
        var target = new RecordingTarget(initial: 0);
        var call = new MaterializerCall { Steps = new[] { Success(2), Success(3) } };

        Result<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>> result =
            DeferredFunctionFallbackDriver.Materialize(
                new DeferredFunctionStructure[] { Logical(), Semi() },
                target,
                new ScriptedMaterializer(call));

        result.Should().BeOfType<
            Ok<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>>>();
        result.Value.Should().HaveCount(2);

        result.Value[0].FirstConstraintIndex.Should().Be(0);
        result.Value[0].ConstraintCount.Should().Be(2);
        result.Value[1].FirstConstraintIndex.Should().Be(2);
        result.Value[1].ConstraintCount.Should().Be(3);

        result.Value[0].LastConstraintIndex.Should().Be(
            result.Value[1].FirstConstraintIndex, "regions do not overlap and leave no gap");
        target.Rows.Should().Be(5);
        target.Rollbacks.Should().BeEmpty("nothing failed, so nothing rolls back");
    }

    /// <summary>
    /// **追加失败也要回滚** / A failed append also rolls back.
    /// </summary>
    /// <remarks>
    /// **追加失败与物化失败是两条不同的路径：
    /// 前者说明目标拒绝了这些行，后者说明结构本身产不出行。**
    /// **两条都必须回到起点，否则"部分写入"就留在了目标里。**
    ///
    /// **A failed append and a failed materialization are two paths: the former means the target refused the rows, the
    /// latter means the structure produced none.** **Both must return to the start, otherwise a partial write stays in
    /// the target.**
    /// </remarks>
    [Fact]
    public void AFailedAppend_ShouldAlsoRollBack() {
        var target = new RecordingTarget(initial: 10) { FailAppend = true };
        var call = new MaterializerCall { Steps = new[] { Success(2) } };

        Result<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>> result =
            DeferredFunctionFallbackDriver.Materialize(
                new DeferredFunctionStructure[] { Logical() },
                target,
                new ScriptedMaterializer(call));

        result.Should().BeOfType<
            Failed<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("scripted append failure");

        target.Rollbacks.Should().HaveCount(1);
        target.Rollbacks[0].Should().Be(10);
    }

    /// <summary>
    /// **空结构列表是一个成功的空事务** / An empty structure list is a successful empty transaction.
    /// </summary>
    /// <remarks>
    /// **不追加、不回滚、返回空列表。**
    /// **如果实现把"空"当成失败，那么"没有延迟函数"这个完全正常的情形会让整个构建失败。**
    ///
    /// **No append, no rollback, an empty list.**
    /// **Treating "empty" as a failure would make the entirely normal case "there are no deferred functions" fail the
    /// whole build.**
    /// </remarks>
    [Fact]
    public void AnEmptyList_ShouldSucceedWithNothing() {
        var target = new RecordingTarget(initial: 7);

        Result<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>> result =
            DeferredFunctionFallbackDriver.Materialize(
                Array.Empty<DeferredFunctionStructure>(), target,
                new ScriptedMaterializer(new MaterializerCall()));

        result.Should().BeOfType<
            Ok<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>>>();
        result.Value.Should().BeEmpty();
        target.Rows.Should().Be(7);
        target.Rollbacks.Should().BeEmpty();
    }

    /// <summary>一个会记录调用并可选地失败的假目标 / A fake target that records calls and can fail.</summary>
    private sealed class RecordingTarget : IDeferredFunctionFallbackTarget
    {
        public RecordingTarget(int initial) => Rows = initial;

        public int Rows { get; private set; }

        public bool FailAppend { get; init; }

        public bool FailRollback { get; init; }

        public List<int> Rollbacks { get; } = new();

        public int ConstraintCount => Rows;

        public Result<Success, ErrorCode, Error<ErrorCode>> Append(
            IReadOnlyList<LinearInequality<Flt64>> constraints) {
            if (FailAppend) {
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument, "scripted append failure");
            }

            Rows += constraints.Count;
            return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
        }

        /// <summary>**驱动记下的区域，按顺序** / The regions the driver recorded, in order.</summary>
        /// <remarks>
        /// **B1239 新增这个成员之后，这条判据才能验"区域真的被记下来了"。**
        /// **在此之前驱动只是把它们放进返回值，而返回值在一个函数返回后就被丢弃了。**
        ///
        /// **收在这里而不是只数次数：** **区域的<b>行号</b>才是第三个过滤条件要用的东西，
        /// 而"记了几条"证明不了行号对不对。**
        ///
        /// **Only with this member (added in B1239) can a criterion check that regions are really recorded. Before it the
        /// driver merely put them in a return value that was discarded when the function returned.**
        ///
        /// **Collected rather than counted: the region's <b>row numbers</b> are what the third filter condition needs, and
        /// "how many were recorded" cannot prove those are right.**
        /// </remarks>
        public List<DeferredFunctionConstraintRegion> Regions { get; } = new();

        public Result<Success, ErrorCode, Error<ErrorCode>> RecordRegion(DeferredFunctionConstraintRegion region) {
            Regions.Add(region);
            return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
        }

        public Result<Success, ErrorCode, Error<ErrorCode>> Rollback(int constraintCount) {
            Rollbacks.Add(constraintCount);
            if (FailRollback) {
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument, "scripted rollback failure");
            }

            Rows = constraintCount;
            return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
        }
    }

    /// <summary>一次物化的脚本：第几个结构产出几行、是否失败 / How many rows, and whether to fail.</summary>
    /// <remarks>
    /// **用"第 N 个结构"做键而不是用类型：这样同一个类型可以既成功又失败，
    /// 而"第二个失败"这个情形就写得出来。**
    ///
    /// **Keyed by ordinal rather than by type, so the same type can both succeed and fail and "the second one fails" is
    /// expressible.**
    /// </remarks>
    private sealed class MaterializerCall
    {
        private int _next;

        /// <summary>按顺序给出的每一步 / One entry per structure, in order.</summary>
        public (int Rows, bool Fail)[] Steps { get; init; } = Array.Empty<(int, bool)>();

        public (int Rows, bool Fail) Next() =>
            _next < Steps.Length ? Steps[_next++] : (0, false);
    }

    private sealed class ScriptedMaterializer : IDeferredFunctionFallbackMaterializer
    {
        private readonly MaterializerCall _call;

        public ScriptedMaterializer(MaterializerCall call) => _call = call;

        public Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
            Materialize(DeferredFunctionStructure structure) {
            (int rows, bool fail) = _call.Next();
            if (fail) {
                return new Failed<
                    DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument, "scripted failure");
            }

            var constraints = new LinearInequality<Flt64>[rows];
            for (int index = 0; index < rows; ++index) {
                constraints[index] = new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(
                        Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Rhs: new LinearPolynomial<Flt64>(
                        Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: Comparison.LE,
                    Name: $"scripted_{index}");
            }

            return new Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
                new DeferredFunctionFallbackConstraints(constraints));
        }
    }

    private static (int Rows, bool Fail) Success(int rows) => (rows, false);

    private static (int Rows, bool Fail) Fail() => (0, true);

    private static SemiStructure Semi() =>
        new(LowerBound: new Flt64(3.0), UpperBound: new Flt64(10.0),
            ResultVariable: Real, IndicatorVariable: Bit,
            Converter: IIntoValue<Flt64>.Identity, Name: "semi");

    private static BinaryLogicStructure Logical() =>
        new(BinaryLogicOperation.And, new[] { Bit }, Bit,
            IIntoValue<Flt64>.Identity, "logic");

    /// <summary>一个永远不会被真正物化的占位结构 / A placeholder never actually materialized.</summary>
    private static AbsStructure Broken() =>
        new(Input: new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
            ResultVariable: Real, PositiveVariable: Positive, NegativeVariable: Negative,
            SignVariable: Bit, PositiveBigM: new Flt64(100.0),
            NegativeBigM: new Flt64(100.0), Converter: IIntoValue<Flt64>.Identity);

    private static readonly RealVar Real = new("drv-real");

    private static readonly BinVar Bit = new("drv-bit");

    private static readonly RealVar Positive = new("drv-pos");

    private static readonly RealVar Negative = new("drv-neg");
}
