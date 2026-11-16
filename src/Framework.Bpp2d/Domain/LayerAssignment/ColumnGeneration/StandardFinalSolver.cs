#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;

/// <summary>
/// 标准最终阶段求解器 / The standard final-stage solver.
/// </summary>
/// <remarks>
/// 流程固定为六步：
///
/// 1. **没有列就直接返回**（`skipped=no_columns`），连模型都不建——建一个空模型再求解是纯浪费。
/// 2. 注册最终阶段的四个构件（分配、装载、需求约束、容量）。
/// 3. **三个目标依次注册**：面积（主）→ 板数（次）→ 板材类型成本（更次）。
/// 4. 求解 MILP。
/// 5. **把解写回模型令牌**，再按令牌读每列的份数——不写回就读到全零，板材会一块也不产出。
/// 6. 按份数产出板材：一份一块。
///
/// The flow is fixed at six steps: **return immediately when there are no columns** (`skipped=no_columns`), not even building a model, since solving an
/// empty one is pure waste; register the final stage's four artifacts (assignment, load, demand constraints, capacity); **register the three objectives in order** — area (primary), sheet count
/// (secondary), per-type cost (tertiary); solve the MILP; **write the solution back into the model tokens** and only then read each column's copies, since skipping the write-back reads zeros and
/// yields no sheets at all; and emit one sheet per copy.
/// </remarks>
public sealed class StandardFinalSolver : IColumnGenerationFinalSolver
{
    private readonly IReadOnlyList<Bpp2dAssignmentDemandEntry> _demandEntries;
    private readonly IReadOnlyList<SheetType<FltX>> _sheetTypes;
    private readonly IColumnGenerationSolver _solver;
    private readonly ColumnGenerationStandardExecutorConfig _config;

    /// <summary>
    /// 构造最终阶段求解器 / Construct the final-stage solver.
    /// </summary>
    /// <param name="demandEntries">需求项 / The demand entries.</param>
    /// <param name="sheetTypes">板材类型 / The sheet types.</param>
    /// <param name="solver">求解器 / The solver.</param>
    /// <param name="config">执行器配置 / The executor configuration.</param>
    public StandardFinalSolver(
        IReadOnlyList<Bpp2dAssignmentDemandEntry> demandEntries,
        IReadOnlyList<SheetType<FltX>> sheetTypes,
        IColumnGenerationSolver solver,
        ColumnGenerationStandardExecutorConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(demandEntries);
        ArgumentNullException.ThrowIfNull(sheetTypes);
        ArgumentNullException.ThrowIfNull(solver);
        _demandEntries = demandEntries;
        _sheetTypes = sheetTypes;
        _solver = solver;
        _config = config ?? new ColumnGenerationStandardExecutorConfig();
    }

    /// <summary>
    /// 求解最终 MILP / Solve the final MILP.
    /// </summary>
    /// <param name="state">当前状态 / The current state.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>最终结果 / The final result.</returns>
    public async Task<Result<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
        ColumnGenerationState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        // **没有列就不建模型**：`skipped=no_columns` 告诉调用方是"没东西可排"而不是"求解失败"。
        // **No columns means no model**: `skipped=no_columns` tells the caller there was nothing to lay out rather than that the solve failed.
        if (state.Columns.Count == 0)
        {
            return Results.Ok<ColumnGenerationFinalResult>(new ColumnGenerationFinalResult(
                Columns: state.Columns,
                Sheets: Array.Empty<Sheet<FltX>>(),
                Objective: null,
                Info: new Dictionary<string, string> { ["skipped"] = "no_columns" }));
        }

        string solveName = $"{_config.FinalSolveNamePrefix}-{state.Iteration}";
        LinearMetaModel<Flt64> model = NewModel(solveName);
        var context = new LayerAssignmentContext();

        Result<FinalAssignmentArtifacts, ErrorCode, Error<ErrorCode>> artifacts =
            context.RegisterFinal(model, state.Columns, _demandEntries, _config.EnableFinalCapacityConstraint);
        if (artifacts is not Ok<FinalAssignmentArtifacts, ErrorCode, Error<ErrorCode>> artifactsOk)
        {
            return artifacts is Failed<FinalAssignmentArtifacts, ErrorCode, Error<ErrorCode>> artifactsFailed
                ? Results.Failed<ColumnGenerationFinalResult>(artifactsFailed.Error)
                : throw new InvalidOperationException("unreachable");
        }
        FinalAssignmentArtifacts finalArtifacts = artifactsOk.Value;

        // **目标顺序不能乱**：面积是主目标，板数与类型成本是次目标。
        // **The objective order matters**: area is primary, sheet count and per-type cost are secondary.
        Try area = context.RegisterSheetAreaMinimization(
            model, finalArtifacts.Assignment, state.Columns, _config.SheetAreaWeightValue);
        if (!area.IsOk)
        {
            return FailFrom(area);
        }

        Try amount = context.RegisterSheetAmountMinimization(
            model,
            finalArtifacts.Assignment,
            state.Columns,
            _config.SheetAmountWeightValue.Times(_config.FinalSheetAmountCoefficientValue));
        if (!amount.IsOk)
        {
            return FailFrom(amount);
        }

        // **成本表为空或权重为零时整条目标不建**（`Build` 返回 null），且这不算失败。
        // **An empty cost table or a zero weight means the objective is not built at all** (`Build` returns null), and that is not a failure.
        SheetAmountMinimization? typeCost = SheetTypeCostMinimization.Build(
            state.Columns, finalArtifacts.Assignment, _config.CostMap, _config.SheetTypeCostWeightValue);
        if (typeCost is not null)
        {
            Try invoked = typeCost.Invoke(model);
            if (!invoked.IsOk)
            {
                return FailFrom(invoked);
            }
        }

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await _solver.SolveMILPAsync(
                solveName, model, _config.FinalToLogModel).ConfigureAwait(false);
        if (solved is not Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solvedOk)
        {
            return solved is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solvedFailed
                ? Results.Failed<ColumnGenerationFinalResult>(solvedFailed.Error)
                : throw new InvalidOperationException("unreachable");
        }
        FeasibleSolverOutput<Flt64> output = solvedOk.Value;

        // **解必须写回模型令牌**，否则下面按令牌读份数会全是零、板材一块也不产出。
        // **The solution must go back into the model tokens**, otherwise reading the copies below yields zeros and no sheets are produced.
        model.SetSolution(output.Solution.Values);

        double tolerance = _config.IntegralityToleranceValue.ToFlt64().ToDouble();
        var picks = new List<(int Index, Layer<FltX> Layer, FltX Copies)>();
        for (int index = 0; index < state.Columns.Count; index++)
        {
            FltX copies = TokenValue(model, finalArtifacts.Assignment.YOf(index));
            // **容差用来挡掉浮点残渣**：`1.9999999` 当作 2 份，`0.0000001` 当作 0 份。
            // **The tolerance absorbs floating-point residue**: `1.9999999` counts as 2 copies and `0.0000001` as none.
            if (copies.ToFlt64().ToDouble() > tolerance)
            {
                picks.Add((index, state.Columns[index], copies));
            }
        }

        IReadOnlyList<Layer<FltX>> selectedColumns = picks.Select(pick => pick.Layer).ToArray();
        SheetType<FltX>? fallbackType = _sheetTypes.FirstOrDefault(sheet => sheet.IsMain)
            ?? _sheetTypes.FirstOrDefault();
        IReadOnlyList<Sheet<FltX>> selectedSheets = BuildSheets(picks, fallbackType);

        return Results.Ok<ColumnGenerationFinalResult>(new ColumnGenerationFinalResult(
            // **一列都没选中时退回全部列**：返回空列会让下游以为"没有可排的层"，而实际上是整数性容差把所有列都判掉了。
            // **When no column is selected, fall back to all of them**: returning none would read downstream as "no layers to lay out" when in fact the integrality tolerance rejected them all.
            Columns: selectedColumns.Count > 0 ? selectedColumns : state.Columns,
            Sheets: selectedSheets,
            Objective: new FltX(output.Obj.ToDouble()),
            Info: new Dictionary<string, string>
            {
                ["solver"] = _solver.Name,
                ["model"] = model.Name,
                ["milp_time_ms"] = (output.Time.TotalMilliseconds).ToString("F0"),
                ["milp_gap"] = output.Gap.ToDouble().ToString("R"),
                ["milp_objective"] = output.Obj.ToDouble().ToString("R"),
                ["selected_sheet_count"] = selectedSheets.Count.ToString(),
                ["selected_layer_count"] = selectedColumns.Count.ToString(),
            }));
    }

    /// <summary>
    /// 建一个最小化模型 / Build a minimisation model.
    /// </summary>
    private static LinearMetaModel<Flt64> NewModel(string name) => new(name);

    /// <summary>
    /// 读一个变量的令牌值 / Read a variable's token value.
    /// </summary>
    /// <param name="model">模型 / The model.</param>
    /// <param name="variable">变量 / The variable.</param>
    /// <returns>令牌值；没有令牌或没有解时为 0 / The token value, zero when the token or the solution is missing.</returns>
    /// <remarks>
    /// **找不到令牌不抛错，返回零**：目标里的系数变量可能因为目标未注册而没进模型，
    /// 抛错会让整个最终阶段白跑。零份数随后被容差挡掉，效果等同"这列没被选中"。
    ///
    /// **A missing token returns zero rather than throwing**: an objective's coefficient variable may never have entered the model because its objective was not registered, and throwing would waste
    /// the whole final stage. A zero copy count is then rejected by the tolerance, which reads the same as "this column was not selected".
    /// </remarks>
    private static FltX TokenValue(LinearMetaModel<Flt64> model, IVariableItem? variable)
    {
        if (variable is null)
        {
            return new FltX(0.0);
        }
        Flt64? result = model.Tokens.Find(variable)?.Result;
        // ponytail: 令牌值经 `double` 中转，末位精度与 `decimal` 直读不同。份数是整数，取整后无差别；真需要精确到小数位时改用
        // `ResultWith(FltX 转换器)`。
        // ponytail: the token value passes through `double`, so its last digits differ from reading `decimal` directly. Copies are integers and identical after flooring; switch to
        // `ResultWith(the FltX converter)` if fractional precision ever matters.
        return result is null ? new FltX(0.0) : new FltX(result.Value.ToDouble());
    }

    /// <summary>
    /// 按份数产出板材 / Emit one sheet per copy.
    /// </summary>
    /// <param name="picks">选中的列与份数 / The selected columns with their copies.</param>
    /// <param name="fallbackType">兜底板材类型 / The fallback sheet type.</param>
    /// <returns>板材清单 / The sheets.</returns>
    /// <remarks>
    /// 四点照做 `buildSheets`：
    ///
    /// 1. 板材类型**取该列自带的那块**，没有才用兜底（主板材优先）。
    /// 2. 份数**向下取整并至少一份**：`0.4` 也算一份——已经过了容差筛选，能到这里就说明它算被选中。
    /// 3. 每块板材的 `SheetLayer` 形状用**该列自己的形状**，不是板材类型的形状（列可能只占板材一角）。
    /// 4. 一份一块，不合并。
    ///
    /// Four things copied from its `buildSheets`: the sheet type **comes from the column itself**, with the fallback used only when absent (main sheet first); the copy count **floors to
    /// at least one**, so `0.4` still yields a sheet, since surviving the tolerance filter means it counted as selected; each sheet's `SheetLayer` shape uses **the column's own shape** rather than
    /// the sheet type's, because a column may occupy only a corner; and one copy yields exactly one sheet, with no merging.
    /// </remarks>
    private static IReadOnlyList<Sheet<FltX>> BuildSheets(
        IReadOnlyList<(int Index, Layer<FltX> Layer, FltX Copies)> picks,
        SheetType<FltX>? fallbackType)
    {
        var result = new List<Sheet<FltX>>();
        foreach ((int _, Layer<FltX> layer, FltX copies) in picks)
        {
            SheetType<FltX>? type = layer.Sheet ?? fallbackType;
            if (type is null)
            {
                continue;
            }
            int count = System.Math.Max(1, (int)System.Math.Floor(copies.ToFlt64().ToDouble()));
            for (int copy = 0; copy < count; copy++)
            {
                result.Add(new Sheet<FltX>(
                    Type: type,
                    Layers: new[] {
                        new SheetLayer<FltX>(
                            type, layer.Shape, new[] { layer }),
                    }));
            }
        }
        return result;
    }

    /// <summary>
    /// 把一次失败的 `Try` 转成本方法的失败 / Turn a failed `Try` into this method's failure.
    /// </summary>
    private static Result<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>> FailFrom(Try tryResult) =>
        tryResult is Failed<Success, ErrorCode, Error<ErrorCode>> failed
            ? Results.Failed<ColumnGenerationFinalResult>(failed.Error)
            : throw new InvalidOperationException("a failed Try must be a Failed value");
}
