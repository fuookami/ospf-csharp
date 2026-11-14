#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Framework.Bpp2d.Application;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Application.Service;

/// <summary>
/// 列生成的应用响应 / The column-generation application response.
/// </summary>
/// <param name="Result">列生成结果 / The column-generation result.</param>
/// <param name="Packing">装箱结果 / The packing result.</param>
/// <param name="Kpi">装箱指标 / The packing indicators.</param>
/// <remarks>
/// 与基线 `ColumnGenerationApplicationResponse` 对齐：**三块分开**——列生成给了什么、装成了什么、装得好不好。
/// 合成一个对象会让调用方无法只取其中一块（比如只想看 KPI 的报表）。
///
/// Aligned with the baseline's `ColumnGenerationApplicationResponse`: **three separate parts** — what column generation produced, what packing made of it, and how good the packing is. Merging
/// them would leave a caller unable to take just one, such as a report that only wants the KPIs.
/// </remarks>
public sealed record ColumnGenerationApplicationResponse(
    ColumnGenerationResult Result,
    PackingResult Packing,
    ColumnGenerationPackingSnapshot Kpi);

/// <summary>
/// 算法运行器：给定请求、执行器组与配置，产出列生成结果 / The algorithm runner: given a request, an executor set and a config, produce a column-generation result.
/// </summary>
/// <remarks>
/// 抽成命名委托而不是内联 `Func`，是因为内联的泛型括号有三层，**写错一层编译器只会报"应输入 &gt;"**，
/// 而命名委托让签名只有一处、可读。替身也就能注入——判据不必跑真实的 LP/MILP。
///
/// A named delegate rather than an inline `Func`, because the inline form nests three layers of generics and **one misplaced bracket yields only an "expected &gt;"**; a named delegate puts the
/// signature in one readable place and lets a stand-in be injected, so criteria need not run a real LP/MILP.
/// </remarks>
public delegate Task<Result<ColumnGenerationResult, ErrorCode, Error<ErrorCode>>> ColumnGenerationAlgorithmRunner(
    ColumnGenerationApplicationRequest request,
    ColumnGenerationStandardExecution execution,
    ColumnGenerationConfig config);

/// <summary>
/// 列生成应用服务 / The column-generation application service.
/// </summary>
/// <remarks>
/// 与基线 `ColumnGenerationApplicationService.solve` 对齐，五步：
///
/// 1. 从请求装出执行器组（B666）。
/// 2. 用候选生成器产出**种子列**——首次 LP 必须可行，空变量模型的 RMP 会直接不可行、拿不到对偶解。
/// 3. 跑列生成算法。
/// 4. **尾板裁剪**：列生成保证"覆盖需求"，最后一块板材通常超出需求量，按需求裁剪后再装箱，
///    否则装箱会多装一件、KPI 的余料也失真。
/// 5. 装箱并算 KPI。
///
/// 第 4 步是本服务独有的价值：**列生成只保证够用，不保证刚好**。
///
/// Aligned with the baseline's `solve`, in five steps: assemble the executor set from the request (B666); produce **seed columns** with the candidate generator, since the first LP must be feasible
/// and an empty-variable RMP is immediately infeasible with no dual solution; run the column-generation algorithm; **trim the tail sheet**, since column generation guarantees coverage and the last
/// sheet usually overshoots the demand, so packing it untrimmed ships an extra piece and skews the remaining-area KPI; and pack and compute the KPIs. Step four is this service's own contribution:
/// **column generation guarantees enough, never exactly enough**.
/// </remarks>
public sealed class ColumnGenerationApplicationService
{
    private readonly IColumnGenerationSolver _solver;
    private readonly ColumnGenerationAlgorithmRunner? _algorithmRunner;

    /// <summary>
    /// 构造应用服务 / Construct the application service.
    /// </summary>
    /// <param name="solver">求解器 / The solver.</param>
    /// <param name="algorithmRunner">算法运行器，null 表示用默认算法 / The algorithm runner, null meaning the default algorithm.</param>
    public ColumnGenerationApplicationService(
        IColumnGenerationSolver solver,
        ColumnGenerationAlgorithmRunner? algorithmRunner = null)
    {
        ArgumentNullException.ThrowIfNull(solver);
        _solver = solver;
        _algorithmRunner = algorithmRunner;
    }

    /// <summary>
    /// 求解一个应用请求 / Solve one application request.
    /// </summary>
    /// <param name="request">应用请求 / The application request.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>应用响应 / The application response.</returns>
    public async Task<Result<ColumnGenerationApplicationResponse, ErrorCode, Error<ErrorCode>>> SolveAsync(
        ColumnGenerationApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var execution = new ColumnGenerationStandardExecution(request, _solver);
        ColumnGenerationConfig algorithmConfig = request.ToAlgorithmConfig();

        Task<Result<ColumnGenerationResult, ErrorCode, Error<ErrorCode>>> run = _algorithmRunner is null
            ? RunDefaultAlgorithmAsync(request, execution, algorithmConfig, cancellationToken)
            : _algorithmRunner(request, execution, algorithmConfig);
        Result<ColumnGenerationResult, ErrorCode, Error<ErrorCode>> outcome = await run.ConfigureAwait(false);
        if (outcome is not Ok<ColumnGenerationResult, ErrorCode, Error<ErrorCode>> outcomeOk)
        {
            return outcome is Failed<ColumnGenerationResult, ErrorCode, Error<ErrorCode>> outcomeFailed
                ? Results.Failed<ColumnGenerationApplicationResponse>(outcomeFailed.Error)
                : throw new InvalidOperationException("unreachable");
        }
        ColumnGenerationResult result = outcomeOk.Value;

        // **按需求裁剪尾板**：列生成只保证够用，最后一块通常超出。
        // **Trim the tail sheet to the demand**: column generation guarantees enough, and the last sheet usually overshoots.
        var demands = new Dictionary<string, ulong>(request.Items.Count);
        foreach (KeyValuePair<ItemId, ulong> pair in request.Items)
        {
            demands[pair.Key.Value] = pair.Value;
        }
        IReadOnlyList<Sheet<FltX>> trimmedSheets = new TailSheetOptimizer(result.Columns)
            .Optimize(result.Sheets, demands);
        ColumnGenerationResult trimmed = result with { Sheets = trimmedSheets };

        Result<PackingResult, ErrorCode, Error<ErrorCode>> packed = Pack(trimmedSheets, request.Kerf);
        if (packed is not Ok<PackingResult, ErrorCode, Error<ErrorCode>> packedOk)
        {
            return packed is Failed<PackingResult, ErrorCode, Error<ErrorCode>> packedFailed
                ? Results.Failed<ColumnGenerationApplicationResponse>(packedFailed.Error)
                : throw new InvalidOperationException("unreachable");
        }

        ColumnGenerationPackingSnapshot kpi = new ColumnGenerationPackingAnalyzer()
            .Analyze(trimmed, packedOk.Value);

        return Results.Ok<ColumnGenerationApplicationResponse>(
            new ColumnGenerationApplicationResponse(trimmed, packedOk.Value, kpi));
    }

    /// <summary>
    /// 跑默认算法 / Run the default algorithm.
    /// </summary>
    /// <remarks>
    /// 组装的顺序照抄基线：**种子列先于一切**。算法拿不到种子时首轮 RMP 是空变量模型，
    /// LP 必然不可行，对偶解也拿不到，整轮白跑。
    ///
    /// The assembly follows the baseline's order: **seed columns come first**. Without seeds the first RMP is an empty-variable model, the LP is necessarily infeasible, no duals appear and the
    /// round is wasted.
    /// </remarks>
    private async Task<Result<ColumnGenerationResult, ErrorCode, Error<ErrorCode>>> RunDefaultAlgorithmAsync(
        ColumnGenerationApplicationRequest request,
        ColumnGenerationStandardExecution execution,
        ColumnGenerationConfig algorithmConfig,
        CancellationToken cancellationToken)
    {
        // 两个回调各写成一句：内联会让泛型括号叠三层，写错一层只报"应输入 XXX"。
        // **Two callbacks, one statement each**: inlining stacks three layers of generic brackets, and one wrong level only yields "expected XXX".
        Func<ColumnGenerationState, IReadOnlyList<Bpp2dLayerGenerationResult<FltX>>, Task<Result<IReadOnlyList<Bpp2dLayerGenerationResult<FltX>>, ErrorCode, Error<ErrorCode>>>> filtering =
            (state, candidates) => Task.FromResult(Results.Ok<IReadOnlyList<Bpp2dLayerGenerationResult<FltX>>>(
                execution.ReducedCostFilter.Filter(state, candidates)));
        Func<Task<Result<IReadOnlyList<Layer<FltX>>, ErrorCode, Error<ErrorCode>>>> seeding =
            () => Task.FromResult(Results.Ok<IReadOnlyList<Layer<FltX>>>(
                SeedColumns.Build(
                    execution.LayerGenerator,
                    execution.RequestBuilder,
                    request.IterationLimit,
                    request.MaxColumnsPerIteration)));

        var algorithm = new ColumnGenerationAlgorithm(
            layerGenerator: execution.LayerGenerator,
            rmpSolver: execution.RmpSolver,
            finalMilpSolver: execution.FinalSolver,
            layerRequestBuilder: execution.RequestBuilder,
            filterByReducedCost: filtering,
            initialColumns: seeding);
        return await algorithm.SolveAsync(algorithmConfig, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 把板材装进装箱输入 / Turn sheets into packing inputs.
    /// </summary>
    /// <remarks>
    /// **刀缝一路带到底**：漏掉它会让装箱按零刀缝排，件与件之间不留切割余量。
    ///
    /// **The kerf travels all the way down**: dropping it packs with zero kerf, leaving no cutting allowance between pieces.
    /// </remarks>
    private static Result<PackingResult, ErrorCode, Error<ErrorCode>> Pack(
        IReadOnlyList<Sheet<FltX>> sheets,
        Quantity<FltX>? kerf)
    {
        var inputs = new List<PackedSheetInput>(sheets.Count);
        foreach (Sheet<FltX> sheet in sheets)
        {
            var placements = new List<Placement2<FltX>>();
            foreach (SheetLayer<FltX> sheetLayer in sheet.SheetLayers)
            {
                foreach (Layer<FltX> layer in sheetLayer.Layers)
                {
                    placements.AddRange(layer.Units);
                }
            }
            inputs.Add(new PackedSheetInput(
                Type: new Sheet2Shape<FltX>(sheet.Width, sheet.Height),
                Placements: placements,
                BatchNo: sheet.BatchNo,
                TypeCode: sheet.Type.TypeCode));
        }
        return new Packer().Invoke(inputs, kerf: kerf);
    }
}
