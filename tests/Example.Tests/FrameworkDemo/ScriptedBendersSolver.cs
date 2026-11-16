using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using BendersSolverApi = Fuookami.Ospf.Framework.Solver.IBendersDecompositionSolver;
using FrameworkSolveOptions = Fuookami.Ospf.Framework.Solver.FrameworkSolveOptions;
using SolverOutput = Fuookami.Ospf.Core.Solver.Output.SolverOutput;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 按脚本回答的假 Benders 后端，供多条判据共用 / A scripted fake Benders backend shared by several criteria.
/// </summary>
/// <remarks>
/// 从 `Demo2BendersLoopTests` 里提出来：这里新增的判据要**经由 `FullLoadApplication`** 观察同一条循环，
/// 复制一份假后端只会让两处慢慢走散。
///
/// Hoisted out of `Demo2BendersLoopTests` : the criteria added in observe the same loop **through `FullLoadApplication`**, and a duplicated fake would
/// only let the two drift apart.
/// </remarks>
internal sealed class ScriptedBendersSolver : BendersSolverApi
{
    private readonly Queue<(double Master, double Sub)> _script;
    private readonly int _solutionLength;
    public int MasterCalls { get; private set; }
    public int SubCalls { get; private set; }

    public ScriptedBendersSolver(IEnumerable<(double Master, double Sub)> script, int solutionLength = 3) {
        _script = new Queue<(double Master, double Sub)>(script);
        _solutionLength = solutionLength;
    }

    public string Name => "scripted";

    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        MasterCalls++;
        (double master, double _) = _script.Count > 0 ? _script.Peek() : (0.0, 0.0);
        // **按主问题的 token 数量产解向量**：框架算法按**位置 → 变量**（`models.Master.Tokens.Tokens`）读主问题解，
        // 固定长度 3 的向量在单模型主问题（12 个 token）上根本对不上，于是算法在主问题那一步就失败。
        // 末尾那个位置是 theta（划分器最后加进去的），给一个非零值，让追索投影读得到。
        //
        // **The vector follows the master's token count**: the framework algorithm reads the master solution **by position → variable**
        // (`models.Master.Tokens.Tokens`), so a fixed length of three cannot line up with a twelve-token master and the algorithm fails right there.
        // The last slot is theta (the partitioner adds it last); give it a non-zero value so the recourse projection can be read.
        Flt64[] masterValues = ScriptedValues(metaModel);
        Flt64[] values = masterValues;
        SolverOutput output = new FeasibleSolverOutput<Flt64>(
            Obj: new Flt64(master),
            Solution: new Solution<Flt64>(values),
            Time: TimeSpan.Zero,
            PossibleBestObj: new Flt64(master),
            Gap: Flt64.Zero);
        return Task.FromResult(Results.Ok(output));
    }

    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options) =>
        throw new NotSupportedException("the loop never calls the options overload");

    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        throw new NotSupportedException("the loop never asks for a converted master");




    /// <summary>
    /// **按变量下界产一个合法取值**：一律给 0 会让"合并解违反变量范围"——那是夹具的问题，不是链路的问题。
    /// **A legal value per variable**: writing zero everywhere violates the lower bounds, which is a fixture problem rather than a chain problem.
    /// </summary>
    /// <param name="model">模型 / The model.</param>
    /// <returns>与模型变量数等长的取值 / Values as long as the model's variables.</returns>
    private static Flt64[] ScriptedValues(LinearMetaModel<Flt64> model) {
        var variables = model.Tokens.Tokens.Select(token => token.Variable).ToList();
        var values = new Flt64[System.Math.Max(variables.Count, 1)];
        for (int i = 0; i < values.Length; i++) {
            Flt64? lower = i < variables.Count ? variables[i].LowerBound?.Value.UnwrapOrNull() : null;
            values[i] = lower is { } bound && System.Math.Abs(bound.ToDouble()) < double.MaxValue ? bound : Flt64.Zero;
        }
        return values;
    }
    public Task<Result<BendersSolverApi.LinearSubResult, ErrorCode, Error<ErrorCode>>> SolveSubAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IVariableItem objectVariable,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        SubCalls++;
        (double _, double sub) = _script.Count > 0 ? _script.Dequeue() : (0.0, 0.0);
        // **子问题解向量也跟着子模型的变量数**： 之后的下一个阻塞是"子问题解长度不匹配: 3"。
        // 与主问题同一条规矩：算法按模型变量身份读解，**固定长度就是错的**。
        //
        // **The sub vector follows the sub model's variable count too**: the next blocker after was "subproblem solution length mismatch: 3". Same rule as the
        // master: the algorithm reads values by variable identity, so a fixed length is simply wrong.
        Flt64[] values = ScriptedValues(metaModel);
        var result = new FeasibleSolverOutput<Flt64>(
            Obj: new Flt64(sub),
            Solution: new Solution<Flt64>(values),
            Time: TimeSpan.Zero,
            PossibleBestObj: new Flt64(sub),
            Gap: Flt64.Zero);
        // **统一报告必须给**： 量到失败在子问题之后，失败原文是"可行子问题缺少统一报告"。
        // 旧适配器可能留空 `Report`，而空值不能用来证明最优——算法因此拒绝继续。
        //
        // **The unified report is required**: measured the failure after the sub, and the message was "feasible subproblem is missing its unified report".
        // Legacy adapters may leave `Report` null, and null cannot prove optimality, so the algorithm refuses to continue.
        BendersSolverApi.LinearSubResult subResult =
            new BendersSolverApi.LinearFeasibleResult(
                result,
                // **对偶要给全**：算法按**子问题自己的约束对象**取对偶（`ToConstraintDual`），
                // 给一个空字典等于"每条约束都没对偶"，于是割无从生成。这里给每条约束一个 0 值——**这是脚本的取值，不是真实对偶**，
                // 它的作用只是让链路走通；真正的解算还是求解器的事。
                //
                // **Every constraint needs a dual**: the algorithm reads duals **by the sub's own constraint objects** (`ToConstraintDual`), so an empty dictionary
                // means "no constraint has a dual" and no cut can be built. This fills each constraint with zero — **a script value, not a real dual**; its only job is to make
                // the chain run. The actual solve remains the solver's.
                metaModel.Constraints.ToDictionary(constraint => constraint, _ => Flt64.Zero),
                Cuts: null) {
                // **统一报告必须填**： 定位到算法因"缺少统一报告"拒绝继续。
                Report = result.ToSolveReport(),
            };
        return Task.FromResult(Results.Ok(subResult));
    }

    public Task<Result<BendersSolverApi.LinearSubResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveSubAsAsync<V>(
        LinearMetaModel<Flt64> metaModel,
        IVariableItem objectVariable,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        FrameworkSolveOptions options)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        throw new NotSupportedException("the loop never asks for a converted sub result");

}
