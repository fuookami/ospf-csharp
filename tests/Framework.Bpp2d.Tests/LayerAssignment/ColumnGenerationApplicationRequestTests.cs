// 应用请求与执行器装配（B666）的判据 / Criteria for the application request and executor assembly (B666).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **应用请求与执行器装配（B666）** / The application request and executor assembly (B666).
/// </summary>
/// <remarks>
/// 判据的重点：**需求项经目录取形状**（目录缺项跳过）、**算法配置的三个上限来自请求**、
/// **装配带过去的是请求里的值而不是默认值**。
///
/// The criteria focus on **shapes coming from the catalogue** (missing entries skipped), on **the algorithm config's three limits coming from the request**, and on **the assembly carrying the
/// request's values rather than defaults**.
/// </remarks>
public class ColumnGenerationApplicationRequestTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static ActualItem<FltX> Item(string id, double width = 20.0) =>
        new(new ItemId(id), id, new RectanglePackingShape2<FltX>(Q(width), Q(10.0)),
            AllowRotate: true, Weight: Q(1.0));

    private static ColumnGenerationApplicationRequest Request(
        IReadOnlyDictionary<ItemId, ulong>? items = null,
        IReadOnlyDictionary<ItemId, ActualItem<FltX>>? catalogue = null,
        int iterationLimit = 32,
        int maxColumns = 16,
        ColumnGenerationStandardExecutorConfig? config = null) =>
        new(
            items ?? new Dictionary<ItemId, ulong>(),
            catalogue ?? new Dictionary<ItemId, ActualItem<FltX>>(),
            Array.Empty<SheetType<FltX>>(),
            IterationLimit: iterationLimit,
            MaxColumnsPerIteration: maxColumns,
            ExecutorConfig: config);

    /// <summary>
    /// **需求项经目录取形状，目录缺项跳过** / Demand entries take their shapes from the catalogue, skipping missing items.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 两个物品、目录里只有一个 → **只有那一个产需求**，不抛。
    /// 2. 不要求面积 → **一条件数需求**，件数与请求一致。
    /// 3. 要求面积 → **两条**，面积是"包围面积 × 件数"。
    /// 4. 请求里没有物品 → **空清单**。
    ///
    /// Four paired points: two items with only one in the catalogue yield **demand entries for that one alone** rather than a throw; without the area demand there is **one amount entry** whose
    /// count matches the request; with it there are **two**, the area being bounding area times the count; and an empty item list yields **an empty list**.
    /// </remarks>
    [Fact]
    public void ToDemandEntries_ShouldResolveShapesThroughTheCatalogue() {
        var items = new Dictionary<ItemId, ulong>
        {
            [new ItemId("I1")] = 4UL,
            [new ItemId("I2")] = 5UL,
        };
        var catalogue = new Dictionary<ItemId, ActualItem<FltX>> { [new ItemId("I1")] = Item("I1") };

        IReadOnlyList<Bpp2dAssignmentDemandEntry> amounts = Request(items, catalogue).ToDemandEntries();
        amounts.Should().HaveCount(1, "I2 has no catalogue entry");
        ((Bpp2dAssignmentDemandValue.Amount)amounts[0].Demand).Value.Should().Be(4UL);

        IReadOnlyList<Bpp2dAssignmentDemandEntry> both = Request(items, catalogue).ToDemandEntries(withAreaDemand: true);
        both.Should().HaveCount(2);
        both[0].Mode.Should().Be(Bpp2dAssignmentDemandMode.ItemAmount);
        ((Bpp2dAssignmentDemandValue.Area)both[1].Demand).Value.Value.ToFlt64().ToDouble()
            .Should().BeApproximately(800.0, 1e-6, "20×10 per piece at four units");

        Request().ToDemandEntries(withAreaDemand: true).Should().BeEmpty();
    }

    /// <summary>
    /// **三个上限来自请求，装配带过去的是请求的值** / All three limits come from the request, and the assembly carries them.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 算法配置的**迭代上限、每轮列数、是否执行最终 MILP** 都是请求给的值（`7` / `3` / `false`），
    ///    不是默认的 `64` / `32` / `true`。
    /// 2. 执行器组带过去的**面积系数与容差**是配置里的非默认值（`2.5` / `1e-3`）。
    /// 3. 执行器组的**需求项与请求派生的一致**。
    /// 4. 没给配置时用默认配置，且请求里的上限仍然生效。
    ///
    /// Four paired points: the algorithm config's **iteration limit, per-round column count and final-MILP flag** all take the request's values (`7`, `3`, `false`) rather than the defaults
    /// `64`/`32`/`true`; the executor set carries the config's **non-default area coefficient and tolerance** (`2.5` and `1e-3`); its **demand entries match the request's derivation**; and an unset
    /// config falls back to the default while the request's limits still apply.
    /// </remarks>
    [Fact]
    public void ToAlgorithmConfigAndExecution_ShouldCarryTheRequestValues() {
        var items = new Dictionary<ItemId, ulong> { [new ItemId("I1")] = 4UL };
        var catalogue = new Dictionary<ItemId, ActualItem<FltX>> { [new ItemId("I1")] = Item("I1") };
        var config = new ColumnGenerationStandardExecutorConfig(
            RmpAreaCoefficient: new FltX(2.5),
            ReducedCostTolerance: new FltX(1e-3));

        ColumnGenerationApplicationRequest request = Request(
            items, catalogue, iterationLimit: 7, maxColumns: 3, config: config);
        request.MaxColumnsPerIteration.Should().Be(3);

        // **最终 MILP 关掉**：请求的开关不能被默认的 `true` 顶替。
        // **The final MILP is off**: the request's flag must not be replaced by the default `true`.
        ColumnGenerationApplicationRequest disabled = request with { FinalMilpEnabled = false };
        ColumnGenerationConfig algorithmConfig = disabled.ToAlgorithmConfig();
        algorithmConfig.IterationLimit.Should().Be(7, "the caller's iteration limit is used");
        algorithmConfig.MaxColumnsPerIteration.Should().Be(3, "the caller's column cap is used");
        algorithmConfig.FinalMilpEnabled.Should().BeFalse("the caller turned the final MILP off");

        var execution = new ColumnGenerationStandardExecution(request, new NoOpSolver());
        execution.DemandEntries.Should().HaveCount(1, "the assembly derives the same demand entries");
        execution.Config.RmpAreaCoefficientValue.ToFlt64().ToDouble()
            .Should().BeApproximately(2.5, 1e-9, "the config's area coefficient reaches the executors");
        execution.ReducedCostFilter.Tolerance.ToFlt64().ToDouble()
            .Should().BeApproximately(1e-3, 1e-9, "the config's tolerance reaches the filter");

        var defaulted = new ColumnGenerationStandardExecution(Request(items, catalogue), new NoOpSolver());
        defaulted.Config.RmpAreaCoefficientValue.ToFlt64().ToDouble()
            .Should().BeApproximately(1.0, 1e-9, "an unset config falls back to the default");
    }

    /// <summary>
    /// 不参与求解的空求解器 / A no-op solver that takes no part.
    /// </summary>
    /// <remarks>
    /// 装配不求解，所以它的所有方法都抛——**误用它就会立刻炸**，而不是悄悄返回空数据。
    ///
    /// The assembly does not solve, so every method throws: **misusing it fails immediately** rather than quietly returning empty data.
    /// </remarks>
    private sealed class NoOpSolver : Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver
    {
        public string Name => "noop";

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name, LinearMetaModel<Flt64> metaModel, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("assembly only");

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name, LinearMetaModel<Flt64> metaModel, ulong amount, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("assembly only");

        public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("assembly only");

        public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name, LinearMetaModel<Flt64> metaModel, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("assembly only");

        public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("assembly only");
    }
}
