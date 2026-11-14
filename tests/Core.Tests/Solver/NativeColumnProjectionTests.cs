#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// 列对齐：错位是静默的，所以必须显式抓 / Misalignment is silent, so it must be caught explicitly.
/// </summary>
/// <remarks>
/// B1208 按基线 `NativePiecewiseReport.kt` 的 `nativeColumns` 补。
///
/// B1208 adds the baseline's `nativeColumns`.
/// </remarks>
public class NativeColumnProjectionTests
{
    /// <summary>
    /// **对齐正确时给出键到列号** / A correct alignment gives key → column.
    /// </summary>
    [Fact]
    public void ACorrectAlignment_ShouldGiveColumns() {
        Result<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>> result =
            NativeColumnProjection.Columns(
                new[] { View(0, A), View(1, B), View(2, C) },
                new[] { TokenOf(A, 0), TokenOf(B, 1), TokenOf(C, 2) });

        result.Should().BeOfType<
            Ok<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>>>();
        result.Value[A.Key].Should().Be(0);
        result.Value[B.Key].Should().Be(1);
        result.Value[C.Key].Should().Be(2);
    }

    /// <summary>
    /// **没有 `origin` 的变量列是合法的** / A variable column without `origin` is legal.
    /// </summary>
    /// <remarks>
    /// **手工构造的模型没有 origin，那是一个<b>合法</b>情形，不是错误。**
    ///
    /// **如果实现要求"origin 必须存在"，所有手工模型都无法走 native 路径——
    /// 而那正是本方向大量判据的构造方式。**
    ///
    /// **A hand-built model has no origins, and that is a <b>legal</b> case, not an error.**
    ///
    /// **Demanding origins would lock every hand-built model out of the native path — which is exactly how most criteria
    /// in this direction are built.**
    /// </remarks>
    [Fact]
    public void MissingOrigins_ShouldBeLegal() {
        NativeColumnProjection.Columns(
                new[] { View(0, null), View(1, null) },
                new[] { TokenOf(A, 0), TokenOf(B, 1) })
            .Should().BeOfType<
                Ok<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>>>(
                "a hand-built model has no origin columns to compare");
    }

    /// <summary>
    /// **列错位被拒** / Misaligned columns are rejected.
    /// </summary>
    /// <remarks>
    /// **这是本批最有内容的一条。**
    ///
    /// **两个列表长度相同、键各自唯一，但顺序不一样：** **变量列是 `[A, B]`，token 列是 `[B, A]`。**
    ///
    /// **这种错位不会让任何一步报错——它会安静地让按列号取到的值属于另一个变量，
    /// 于是报告里的数字张冠李戴。**
    ///
    /// **它是"静默的"，所以必须显式抓。**
    ///
    /// **The batch's most substantive criterion.**
    ///
    /// **Two lists of equal length with individually unique keys but in different order: variables `[A, B]`, tokens
    /// `[B, A]`.**
    ///
    /// **Such misalignment raises nothing — it quietly makes the value at a column belong to another variable, so the
    /// report attributes numbers to the wrong ones.**
    ///
    /// **It is silent, which is why it must be caught explicitly.**
    /// </remarks>
    [Fact]
    public void MisalignedColumns_ShouldBeRejected() {
        Result<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>> result =
            NativeColumnProjection.Columns(
                new[] { View(0, A), View(1, B) },
                new[] { TokenOf(B, 0), TokenOf(A, 1) });

        result.Should().BeOfType<
            Failed<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("keys differ");
    }

    /// <summary>
    /// **数量不一致先被拒** / A count mismatch is rejected first.
    /// </summary>
    /// <remarks>
    /// **先确认两个列表可以逐位比较，否则后面全是越界。**
    ///
    /// **判据用一个同时"数量不符"且"键重复"的输入，断言拿到的是数量消息——
    /// 两条消息完全不同，所以这个断言能区分顺序。**
    ///
    /// **Establish that the lists can be compared position by position, otherwise everything after is out of range.**
    ///
    /// **The criterion uses an input that is <b>both</b> a count mismatch and a duplicate-key case and asserts the count
    /// message — the two messages are entirely different, so the assertion pins the order.**
    /// </remarks>
    [Fact]
    public void TheCountCheck_ShouldComeFirst() {
        Result<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>> result =
            NativeColumnProjection.Columns(
                new[] { View(0, A) },
                new[] { TokenOf(B, 0), TokenOf(B, 1) });

        result.Should().BeOfType<
            Failed<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("counts differ");
    }

    /// <summary>
    /// **token 键重复被拒** / Duplicate token keys are rejected.
    /// </summary>
    /// <remarks>
    /// **键要当字典的键用，重复会让后写的覆盖先写的——于是列号悄悄少一个。**
    ///
    /// **Keys become dictionary keys; a duplicate lets a later write overwrite an earlier, so one column quietly
    /// disappears.**
    /// </remarks>
    [Fact]
    public void DuplicateTokenKeys_ShouldBeRejected() {
        Result<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>> result =
            NativeColumnProjection.Columns(
                new[] { View(0, null), View(1, null) },
                new[] { TokenOf(A, 0), TokenOf(A, 1) });

        result.Should().BeOfType<
            Failed<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("duplicated");
    }

    /// <summary>
    /// **列号取的是 token 的顺序，不是 `SolverIndex`** / Columns come from token order, not `SolverIndex`.
    /// </summary>
    /// <remarks>
    /// **基线的 `keys.withIndex()` 用的是<b>列表下标</b>，不是 token 自己的 `solverIndex`。**
    ///
    /// **判据构造一个 `SolverIndex` 与列表下标不一致的输入：** **两个 token 的 `SolverIndex` 是 `5` 与 `9`，
    /// 而列表位置是 `0` 与 `1`。**
    ///
    /// **正确的实现给出 `0` 与 `1`（列表下标）；如果误用 `SolverIndex`，会给出 `5` 与 `9`——
    /// 而那会让解向量按错误的列去索引。**
    ///
    /// **The baseline's `keys.withIndex()` uses the <b>list index</b>, not the token's own `solverIndex`.**
    ///
    /// **The criterion builds an input where the two differ: `SolverIndex` values `5` and `9` at list positions `0` and
    /// `1`.**
    ///
    /// **The correct implementation yields `0` and `1`; using `SolverIndex` would yield `5` and `9`, indexing the solution
    /// vector at the wrong columns.**
    /// </remarks>
    [Fact]
    public void Columns_ShouldComeFromListOrderNotSolverIndex() {
        Result<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>> result =
            NativeColumnProjection.Columns(
                new[] { View(0, A), View(1, B) },
                new[] { TokenOf(A, 5), TokenOf(B, 9) });

        result.Should().BeOfType<
            Ok<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>>>();
        result.Value[A.Key].Should().Be(0, "the list index is the column");
        result.Value[B.Key].Should().Be(1);
    }

    /// <summary>
    /// **空输入通过** / Empty input passes.
    /// </summary>
    [Fact]
    public void EmptyInput_ShouldPass() {
        NativeColumnProjection.Columns(
                Array.Empty<ModelViewVariable>(), Array.Empty<Token<Flt64>>())
            .Value.Should().BeEmpty();
    }

    private static ModelViewVariable View(int index, IVariableItem? origin) =>
        new(index, new Flt64(0.0), new Flt64(10.0), Continuous.Instance,
            $"v{index}", origin: origin);

    private static Token<Flt64> TokenOf(IVariableItem variable, int solverIndex) =>
        new(variable, solverIndex, new Dictionary<object, Action<bool>>());

    private static readonly RealVar A = new("col-a");

    private static readonly RealVar B = new("col-b");

    private static readonly RealVar C = new("col-c");
}
