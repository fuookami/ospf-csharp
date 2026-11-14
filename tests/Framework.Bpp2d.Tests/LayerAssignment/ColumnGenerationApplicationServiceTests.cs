// 应用服务求解入口（B667）的判据 / Criteria for the application service's solve entry (B667).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Bpp2d.Application.Service;
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
/// **应用服务求解入口（B667）** / The application service's solve entry (B667).
/// </summary>
/// <remarks>
/// 判据的重点：**算法拿到的是请求的配置**、**尾板按需求裁剪后再装箱**（KPI 的件数是裁剪后的）、
/// **算法失败原样透传**、**响应分三块**。
///
/// The criteria focus on **the algorithm receiving the request's config**, on **the tail sheet being trimmed to the demand before packing** (the KPI's item count is the trimmed one), on **algorithm
/// failures passing through unchanged**, and on **the response having three parts**.
/// </remarks>
public class ColumnGenerationApplicationServiceTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static ActualItem<FltX> Item(string id) =>
        new(new ItemId(id), id, new RectanglePackingShape2<FltX>(Q(20.0), Q(10.0)),
            AllowRotate: true, Weight: Q(1.0));

    /// <remarks>**一块带 4 个 `I1` 放置的层**：需求登记 2 件时尾板应裁到 2 件。</remarks>
    private static Layer<FltX> Layer(int count) {
        var units = new Placement2<FltX>[count];
        for (int index = 0; index < count; index++) {
            units[index] = new Placement2<FltX>(
                Shape: new RectanglePackingShape2<FltX>(Q(20.0), Q(10.0)),
                Position: new QuantityPoint2<FltX>(Q(index * 20.0), Q(0.0)),
                Rotated: false,
                ItemId: "I1");
        }
        return new Layer<FltX>(
            0L, null,
            new SheetType<FltX>(Q(100.0), Q(100.0), Q(1000.0), new SheetTypeId("S"), true),
            new Sheet2Shape<FltX>(Q(100.0), Q(100.0)),
            units);
    }

    private static Sheet<FltX> SheetWith(Layer<FltX> layer) =>
        new(layer.Sheet!, new[] { new SheetLayer<FltX>(layer.Sheet!, layer.Shape, new[] { layer }) });

    private static ColumnGenerationResult Result(int unitCount, IReadOnlyList<Layer<FltX>>? columns = null) {
        Layer<FltX> layer = Layer(unitCount);
        return new ColumnGenerationResult(
            Columns: columns ?? new[] { layer },
            Sheets: new[] { SheetWith(layer) },
            IterationCount: 1,
            TerminatedByIterationLimit: false,
            TerminatedByTimeLimit: false,
            LpSolvedTimes: 1,
            FinalSolved: true,
            LpObjectives: new List<FltX?> { new FltX(1.0) },
            FinalObjective: new FltX(1.0),
            Elapsed: TimeSpan.FromMilliseconds(5.0));
    }

    private static ColumnGenerationApplicationRequest Request(
        ulong demand = 2UL,
        int iterationLimit = 32,
        int maxColumns = 16,
        bool finalMilp = true) =>
        new(
            new Dictionary<ItemId, ulong> { [new ItemId("I1")] = demand },
            new Dictionary<ItemId, ActualItem<FltX>> { [new ItemId("I1")] = Item("I1") },
            new[] { new SheetType<FltX>(Q(100.0), Q(100.0), Q(1000.0), new SheetTypeId("S"), true) },
            IterationLimit: iterationLimit,
            MaxColumnsPerIteration: maxColumns,
            FinalMilpEnabled: finalMilp);

    /// <summary>
    /// **裁剪在前、装箱在后，算法拿到请求配置，响应分三块** / Trim first, pack after, the algorithm sees the request's config, and the response has three parts.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 算法收到的**三个上限来自请求**（`7` / `3` / `false`），不是默认的 `64` / `32` / `true`。
    /// 2. 需求 2 件、层里 4 件 → **KPI 的件数是 2**：裁剪发生在装箱之前，不是之后。
    /// 3. 响应**三块都在**：列生成结果、装箱结果、KPI。
    /// 4. KPI 的板材数等于裁剪后的板材数。
    ///
    /// Four paired points: the algorithm sees **the request's three limits** (`7`, `3`, `false`) rather than the defaults `64`/`32`/`true`; a demand of two against a four-piece layer makes **the
    /// KPI's item count two**, since trimming happens before packing rather than after; the response carries **all three parts**; and the KPI's sheet count matches the trimmed sheet count.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldTrimBeforePackingAndPassTheRequestConfig() {
        ColumnGenerationConfig? seen = null;
        var service = new ColumnGenerationApplicationService(
            new NoOpSolver(),
            (request, execution, config) =>
            {
                seen = config;
                execution.DemandEntries.Should().NotBeEmpty("the executors were built from the request");
                return Task.FromResult(Results.Ok<ColumnGenerationResult>(Result(4)));
            });

        Result<ColumnGenerationApplicationResponse, ErrorCode, Error<ErrorCode>> solved =
            await service.SolveAsync(Request(demand: 2UL, iterationLimit: 7, maxColumns: 3, finalMilp: false));

        solved.IsOk.Should().BeTrue();
        ColumnGenerationApplicationResponse response =
            ((Ok<ColumnGenerationApplicationResponse, ErrorCode, Error<ErrorCode>>)solved).Value;

        seen.Should().NotBeNull("the runner receives a config");
        seen!.IterationLimit.Should().Be(7, "the caller's iteration limit reaches the algorithm");
        seen.MaxColumnsPerIteration.Should().Be(3, "the caller's column cap reaches the algorithm");
        seen.FinalMilpEnabled.Should().BeFalse("the caller turned the final MILP off");

        response.Kpi.ItemCount.Should().Be(2,
            "trimming happens before packing, so the KPI sees the demanded count rather than the layer's four");
        response.Kpi.SheetCount.Should().Be(response.Result.Sheets.Count, "the KPI counts the trimmed sheets");
        response.Result.Columns.Should().NotBeEmpty("the column-generation result survives");
        response.Packing.Should().NotBeNull("packing is carried separately");
    }

    /// <summary>
    /// **算法失败原样透传** / An algorithm failure passes through unchanged.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 算法失败 → 服务失败，**不是**"成功但响应为空"。
    /// 2. 错误消息原样保留。
    /// 3. 失败时不进入装箱——没有结果可装。
    ///
    /// Three paired points: an algorithm failure makes the service fail, **not** succeed with an empty response; the message is preserved; and packing never starts, since there is nothing to pack.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldPropagateAlgorithmFailures() {
        var service = new ColumnGenerationApplicationService(
            new NoOpSolver(),
            (request, execution, config) => Task.FromResult(
                Results.Failed<ColumnGenerationResult>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, "cg exploded"))));

        Result<ColumnGenerationApplicationResponse, ErrorCode, Error<ErrorCode>> solved =
            await service.SolveAsync(Request());

        solved.IsOk.Should().BeFalse("a broken algorithm is not an empty response");
        ((Failed<ColumnGenerationApplicationResponse, ErrorCode, Error<ErrorCode>>)solved)
            .Error.Message.Should().Be("cg exploded");
    }

    /// <summary>不参与求解的空求解器 / A no-op solver.</summary>
    private sealed class NoOpSolver : Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver
    {
        public string Name => "noop";

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name, LinearMetaModel<Flt64> metaModel, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("runner injected");

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name, LinearMetaModel<Flt64> metaModel, ulong amount, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("runner injected");

        public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("runner injected");

        public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name, LinearMetaModel<Flt64> metaModel, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("runner injected");

        public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("runner injected");
    }
}
