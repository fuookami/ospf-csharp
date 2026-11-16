#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;

/// <summary>
/// 标准受限主问题求解器 / The standard restricted-master-problem solver.
/// </summary>
/// <remarks>
/// 这是列生成每轮的心脏，六步：
///
/// 1. 建模型（名字 `前缀-迭代号`）。
/// 2. 注册 RMP 构件（非精确分配、装载、需求约束）。
/// 3. **有新列才加列**；没有列时直接跳过——加个空列表会把装载项重复追加一遍。
/// 4. 注册面积最小化目标（RMP 阶段唯一的目标，系数来自配置）。
/// 5. 求 LP。
/// 6. **从对偶解取需求影子价格**，回给主循环供下一轮候选筛选使用。
///
/// 第 5、6 步是"求解 → 定价"这条链的两半：**LP 解本身没有用，对偶值才是下一轮的输入**。
/// 求完 LP 不取对偶值，下一轮的检验数全是零，候选筛选会退化成"全要"或"全不要"。
///
/// The heart of each column-generation round, in six steps: build the model (named `prefix-iteration`); register the RMP pieces (imprecise assignment,
/// load, demand constraints); **add columns only when there are new ones**, since adding an empty list would append the load terms twice; register the area-minimisation objective, the only
/// objective in the RMP stage, with its coefficient from the config; solve the LP; and **read the demand shadow prices off the dual solution**, handing them back for the next round's candidate
/// filtering. Steps five and six are two halves of one solve-then-price chain: **the LP solution itself is useless and the dual values are the next round's input**. Skipping them leaves every
/// reduced cost at zero next round and degenerates the candidate filter into taking everything or nothing.
/// </remarks>
public sealed class StandardRmpSolver : IColumnGenerationRmpSolver
{
    private readonly IReadOnlyDictionary<ItemId, ulong> _items;
    private readonly IReadOnlyList<Bpp2dAssignmentDemandEntry> _demandEntries;
    private readonly IColumnGenerationSolver _solver;
    private readonly ColumnGenerationStandardExecutorConfig _config;

    /// <summary>
    /// 构造 RMP 求解器 / Construct the RMP solver.
    /// </summary>
    /// <param name="items">物品需求量 / The item demands.</param>
    /// <param name="demandEntries">需求项 / The demand entries.</param>
    /// <param name="solver">求解器 / The solver.</param>
    /// <param name="config">执行器配置 / The executor configuration.</param>
    public StandardRmpSolver(
        IReadOnlyDictionary<ItemId, ulong> items,
        IReadOnlyList<Bpp2dAssignmentDemandEntry> demandEntries,
        IColumnGenerationSolver solver,
        ColumnGenerationStandardExecutorConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(demandEntries);
        ArgumentNullException.ThrowIfNull(solver);
        _items = items;
        _demandEntries = demandEntries;
        _solver = solver;
        _config = config ?? new ColumnGenerationStandardExecutorConfig();
    }

    /// <inheritdoc/>
    public async Task<Result<ColumnGenerationLpResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
        ColumnGenerationState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        string solveName = $"{_config.RmpSolveNamePrefix}-{state.Iteration}";
        LinearMetaModel<Flt64> model = new(solveName);
        var context = new LayerAssignmentContext(_items);

        Result<RmpAssignmentArtifacts, ErrorCode, Error<ErrorCode>> artifacts =
            context.RegisterRmp(model, _demandEntries, overEnabled: false);
        if (artifacts is not Ok<RmpAssignmentArtifacts, ErrorCode, Error<ErrorCode>> artifactsOk)
        {
            return artifacts is Failed<RmpAssignmentArtifacts, ErrorCode, Error<ErrorCode>> failed
                ? Results.Failed<ColumnGenerationLpResult>(failed.Error)
                : throw new InvalidOperationException("unreachable");
        }
        RmpAssignmentArtifacts rmp = artifactsOk.Value;

        // **有新列才加列**：空列表会产生一次无意义的装载项追加。
        // **Columns are added only when there are new ones**: an empty list appends a round of load terms for nothing.
        if (state.Columns.Count > 0)
        {
            Result<IReadOnlyList<Layer<FltX>>, ErrorCode, Error<ErrorCode>> added =
                context.AddColumns(rmp, 0UL, state.Columns, model);
            if (added is not Ok<IReadOnlyList<Layer<FltX>>, ErrorCode, Error<ErrorCode>>)
            {
                return added is Failed<IReadOnlyList<Layer<FltX>>, ErrorCode, Error<ErrorCode>> addFailed
                    ? Results.Failed<ColumnGenerationLpResult>(addFailed.Error)
                    : throw new InvalidOperationException("unreachable");
            }
        }

        Try area = new AreaMinimization(rmp.Assignment, _config.RmpAreaCoefficientValue).Invoke(model);
        if (!area.IsOk)
        {
            return area is Failed<Success, ErrorCode, Error<ErrorCode>> areaFailed
                ? Results.Failed<ColumnGenerationLpResult>(areaFailed.Error)
                : throw new InvalidOperationException("unreachable");
        }

        Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> solved =
            await _solver.SolveLPAsync(solveName, model, _config.RmpToLogModel).ConfigureAwait(false);
        if (solved is not Ok<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> solvedOk)
        {
            return solved is Failed<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> solvedFailed
                ? Results.Failed<ColumnGenerationLpResult>(solvedFailed.Error)
                : throw new InvalidOperationException("unreachable");
        }
        IColumnGenerationSolver.LpResult lp = solvedOk.Value;

        // **不求对偶值这一步就白做**：下一轮的检验数全靠它。
        // **Without the dual values this whole step is wasted**: every reduced cost next round depends on them.
        var duals = new MetaDualSolution(
            lp.DualSolution,
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>());
        IReadOnlyDictionary<DemandModeKey, FltX> shadowPrices =
            DemandShadowPrices.Extract(_demandEntries, duals, DemandShadowPrices.DefaultConstraintName);

        return Results.Ok<ColumnGenerationLpResult>(new ColumnGenerationLpResult(
            ShadowPrices: shadowPrices,
            Objective: new FltX(lp.Obj.ToDouble()),
            Info: new Dictionary<string, string>
            {
                ["solver"] = _solver.Name,
                ["model"] = model.Name,
                ["lp_time_ms"] = lp.Time.TotalMilliseconds.ToString("F0"),
                ["lp_gap"] = lp.Gap.ToDouble().ToString("R"),
                ["lp_objective"] = lp.Obj.ToDouble().ToString("R"),
                ["column_count"] = state.Columns.Count.ToString(),
            }));
    }
}
