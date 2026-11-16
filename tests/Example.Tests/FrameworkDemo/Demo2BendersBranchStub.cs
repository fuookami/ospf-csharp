#nullable enable

using System;
using Fuookami.Ospf.Quantities;
using Fuookami.Ospf.Core.Variable;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 最小的 Benders 求解器桩，只为让 demo2 的 Benders 分支**真的被进入** /
/// The smallest Benders solver stub, only so demo2's Benders branch is **actually entered**.
/// </summary>
/// <remarks>
/// 这个桩**不解任何模型**，它只记录"主问题被要求求解过几次"，然后返回一个明确的失败。
/// 它存在的理由只有一个： 证明"不带求解器时那条分支走不到"，于是**要判断这条分支能不能走，必须带一个**。
///
/// **它不假装求解成功**：返回成功就等于给出一条没有实测依据的"接上了"的证据。
/// The stub **solves nothing**; it records how many times a master solve was requested and then fails explicitly. Its only reason to exist is the earlier finding that the branch
/// is unreachable without a solver. **It never pretends to succeed**: returning success would be evidence of "it is wired" with nothing behind it.
/// </remarks>
public sealed class RecordingBendersSolver : IBendersDecompositionSolver
{
    /// <summary>主问题求解次数 / Master solve requests.</summary>
    public int MasterCalls { get; private set; }

    /// <summary>子问题求解次数 / Sub solve requests.</summary>
    public int SubCalls { get; private set; }

    /// <inheritdoc />
    public string Name => "recording_benders_stub";

    /// <inheritdoc />
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        ArgumentNullException.ThrowIfNull(metaModel);
        ++MasterCalls;
        return Task.FromResult(Results.Failed<SolverOutput>(new Err<ErrorCode>(
            ErrorCode.ApplicationFailed,
            "the recording stub solves nothing / 记录桩不解任何模型")));
    }

    /// <inheritdoc />
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(converter);
        return SolveMasterAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack);
    }

    /// <inheritdoc />
    public Task<Result<IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>> SolveSubAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IVariableItem objectVariable,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        ArgumentNullException.ThrowIfNull(metaModel);
        ++SubCalls;
        return Task.FromResult(Results.Failed<IBendersDecompositionSolver.LinearSubResult>(new Err<ErrorCode>(
            ErrorCode.ApplicationFailed,
            "the recording stub solves nothing / 记录桩不解任何模型")));
    }

    /// <inheritdoc />
    public Task<Result<IBendersDecompositionSolver.LinearSubResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveSubAsAsync<V>(
        LinearMetaModel<Flt64> metaModel,
        IVariableItem objectVariable,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        FrameworkSolveOptions options)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(options);
        return Task.FromResult(Results.Failed<IBendersDecompositionSolver.LinearSubResultOf<V>>(new Err<ErrorCode>(
            ErrorCode.ApplicationFailed,
            "the recording stub solves nothing / 记录桩不解任何模型")));
    }
}
