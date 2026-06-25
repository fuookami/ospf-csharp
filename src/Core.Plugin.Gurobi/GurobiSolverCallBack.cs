#nullable enable

using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Gurobi;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>创建环境函数类型 / Creating environment function type.</summary>
/// <param name="env">Gurobi 环境 / Gurobi environment.</param>
/// <returns>操作结果 / Operation result.</returns>
public delegate Result<Success, ErrorCode, Error<ErrorCode>> CreatingEnvironmentFunction(GRBEnv env);

/// <summary>Gurobi 原生回调函数类型 / Gurobi native callback function type.</summary>
/// <param name="callback">Gurobi 原生回调 / Gurobi native callback.</param>
public delegate void NativeCallBack(GRBCallback callback);

/// <summary>线性求解器回调函数类型 / Linear solver callback function type.</summary>
public delegate Task<Result<Success, ErrorCode, Error<ErrorCode>>> LinearSolverFunction(
    SolverStatus? status, GRBModel grbModel, IReadOnlyList<GRBVar> variables, IReadOnlyList<GRBConstr> constraints);

/// <summary>二次求解器回调函数类型 / Quadratic solver callback function type.</summary>
public delegate Task<Result<Success, ErrorCode, Error<ErrorCode>>> QuadraticSolverFunction(
    SolverStatus? status, GRBModel grbModel, IReadOnlyList<GRBVar> variables, IReadOnlyList<GRBQConstr> constraints);

/// <summary>
/// 求解器回调时机枚举 / Solver callback point enum.
/// </summary>
public enum CallBackPoint {
    /// <summary>建模完成后 / After modeling.</summary>
    AfterModeling,
    /// <summary>配置阶段 / Configuration phase.</summary>
    Configuration,
    /// <summary>分析解阶段 / Analyzing solution phase.</summary>
    AnalyzingSolution,
    /// <summary>求解失败后 / After failure.</summary>
    AfterFailure,
}

/// <summary>
/// Gurobi 线性求解器回调管理器 / Gurobi linear solver callback manager.
/// </summary>
public sealed class GurobiLinearSolverCallBack : ICloneable {
    /// <summary>原生回调函数 / Native callback function.</summary>
    public NativeCallBack? NativeCallback { get; set; }
    /// <summary>创建环境函数 / Creating environment function.</summary>
    public CreatingEnvironmentFunction? CreatingEnvironmentFunction { get; set; }

    private readonly Dictionary<CallBackPoint, List<LinearSolverFunction>> _map = new();

    public GurobiLinearSolverCallBack(
        NativeCallBack? nativeCallback = null,
        CreatingEnvironmentFunction? creatingEnvironmentFunction = null,
        Dictionary<CallBackPoint, List<LinearSolverFunction>>? map = null) {
        NativeCallback = nativeCallback;
        CreatingEnvironmentFunction = creatingEnvironmentFunction;
        if (map is not null) {
            foreach (KeyValuePair<CallBackPoint, List<LinearSolverFunction>> kv in map) {
                _map[kv.Key] = new List<LinearSolverFunction>(kv.Value);
            }
        }
    }

    /// <summary>在建模完成后添加回调函数 / Add callback after modeling.</summary>
    public GurobiLinearSolverCallBack AfterModeling(LinearSolverFunction f) => Add(CallBackPoint.AfterModeling, f);
    /// <summary>在配置阶段添加回调函数 / Add callback at configuration phase.</summary>
    public GurobiLinearSolverCallBack Configuration(LinearSolverFunction f) => Add(CallBackPoint.Configuration, f);
    /// <summary>在分析解阶段添加回调函数 / Add callback at analyzing solution phase.</summary>
    public GurobiLinearSolverCallBack AnalyzingSolution(LinearSolverFunction f) => Add(CallBackPoint.AnalyzingSolution, f);
    /// <summary>在求解失败后添加回调函数 / Add callback after failure.</summary>
    public GurobiLinearSolverCallBack AfterFailure(LinearSolverFunction f) => Add(CallBackPoint.AfterFailure, f);

    private GurobiLinearSolverCallBack Add(CallBackPoint p, LinearSolverFunction f) {
        if (!_map.TryGetValue(p, out List<LinearSolverFunction>? list)) {
            _map[p] = list = new List<LinearSolverFunction>();
        }

        list.Add(f);
        return this;
    }

    /// <summary>检查是否包含指定时机的回调 / Check if callback at point is contained.</summary>
    public bool Contains(CallBackPoint point) => _map.ContainsKey(point);

    /// <summary>获取指定时机的回调函数列表 / Get callback function list at point.</summary>
    public IReadOnlyList<LinearSolverFunction>? Get(CallBackPoint point) =>
        _map.TryGetValue(point, out List<LinearSolverFunction>? l) ? l : null;

    /// <summary>执行创建环境回调 / Execute creating-environment callback if present.</summary>
    public Result<Success, ErrorCode, Error<ErrorCode>>? ExecIfContain(GRBEnv env) => CreatingEnvironmentFunction?.Invoke(env);

    /// <summary>执行指定时机回调 / Execute callbacks at point if present.</summary>
    public async Task<Result<Success, ErrorCode, Error<ErrorCode>>?> ExecIfContainAsync(
        CallBackPoint point, SolverStatus? status, GRBModel grbModel,
        IReadOnlyList<GRBVar> variables, IReadOnlyList<GRBConstr> constraints) {
        if (!_map.TryGetValue(point, out List<LinearSolverFunction>? list) || list.Count == 0) {
            return null;
        }

        Result<Success, ErrorCode, Error<ErrorCode>>? last = null;
        foreach (LinearSolverFunction f in list) {
            last = await f(status, grbModel, variables, constraints).ConfigureAwait(false);
            if (last is Failed<Success, ErrorCode, Error<ErrorCode>>
                or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return last;
            }
        }
        return last;
    }

    /// <summary>复制回调管理器 / Copy (deep) callback manager.</summary>
    public GurobiLinearSolverCallBack Copy() => new(NativeCallback, CreatingEnvironmentFunction, _map);
    object ICloneable.Clone() => Copy();
}

/// <summary>
/// Gurobi 二次求解器回调管理器 / Gurobi quadratic solver callback manager.
/// </summary>
public sealed class GurobiQuadraticSolverCallBack : ICloneable {
    /// <summary>原生回调函数 / Native callback function.</summary>
    public NativeCallBack? NativeCallback { get; set; }
    /// <summary>创建环境函数 / Creating environment function.</summary>
    public CreatingEnvironmentFunction? CreatingEnvironmentFunction { get; set; }

    private readonly Dictionary<CallBackPoint, List<QuadraticSolverFunction>> _map = new();

    public GurobiQuadraticSolverCallBack(
        NativeCallBack? nativeCallback = null,
        CreatingEnvironmentFunction? creatingEnvironmentFunction = null,
        Dictionary<CallBackPoint, List<QuadraticSolverFunction>>? map = null) {
        NativeCallback = nativeCallback;
        CreatingEnvironmentFunction = creatingEnvironmentFunction;
        if (map is not null) {
            foreach (KeyValuePair<CallBackPoint, List<QuadraticSolverFunction>> kv in map) {
                _map[kv.Key] = new List<QuadraticSolverFunction>(kv.Value);
            }
        }
    }

    /// <summary>在建模完成后添加回调函数 / Add callback after modeling.</summary>
    public GurobiQuadraticSolverCallBack AfterModeling(QuadraticSolverFunction f) => Add(CallBackPoint.AfterModeling, f);
    /// <summary>在配置阶段添加回调函数 / Add callback at configuration phase.</summary>
    public GurobiQuadraticSolverCallBack Configuration(QuadraticSolverFunction f) => Add(CallBackPoint.Configuration, f);
    /// <summary>在分析解阶段添加回调函数 / Add callback at analyzing solution phase.</summary>
    public GurobiQuadraticSolverCallBack AnalyzingSolution(QuadraticSolverFunction f) => Add(CallBackPoint.AnalyzingSolution, f);
    /// <summary>在求解失败后添加回调函数 / Add callback after failure.</summary>
    public GurobiQuadraticSolverCallBack AfterFailure(QuadraticSolverFunction f) => Add(CallBackPoint.AfterFailure, f);

    private GurobiQuadraticSolverCallBack Add(CallBackPoint p, QuadraticSolverFunction f) {
        if (!_map.TryGetValue(p, out List<QuadraticSolverFunction>? list)) {
            _map[p] = list = new List<QuadraticSolverFunction>();
        }

        list.Add(f);
        return this;
    }

    /// <summary>检查是否包含指定时机的回调 / Check if callback at point is contained.</summary>
    public bool Contains(CallBackPoint point) => _map.ContainsKey(point);

    /// <summary>获取指定时机的回调函数列表 / Get callback function list at point.</summary>
    public IReadOnlyList<QuadraticSolverFunction>? Get(CallBackPoint point) =>
        _map.TryGetValue(point, out List<QuadraticSolverFunction>? l) ? l : null;

    /// <summary>执行创建环境回调 / Execute creating-environment callback if present.</summary>
    public Result<Success, ErrorCode, Error<ErrorCode>>? ExecIfContain(GRBEnv env) => CreatingEnvironmentFunction?.Invoke(env);

    /// <summary>执行指定时机回调 / Execute callbacks at point if present.</summary>
    public async Task<Result<Success, ErrorCode, Error<ErrorCode>>?> ExecIfContainAsync(
        CallBackPoint point, SolverStatus? status, GRBModel grbModel,
        IReadOnlyList<GRBVar> variables, IReadOnlyList<GRBQConstr> constraints) {
        if (!_map.TryGetValue(point, out List<QuadraticSolverFunction>? list) || list.Count == 0) {
            return null;
        }

        Result<Success, ErrorCode, Error<ErrorCode>>? last = null;
        foreach (QuadraticSolverFunction f in list) {
            last = await f(status, grbModel, variables, constraints).ConfigureAwait(false);
            if (last is Failed<Success, ErrorCode, Error<ErrorCode>>
                or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return last;
            }
        }
        return last;
    }

    /// <summary>复制回调管理器 / Copy (deep) callback manager.</summary>
    public GurobiQuadraticSolverCallBack Copy() => new(NativeCallback, CreatingEnvironmentFunction, _map);
    object ICloneable.Clone() => Copy();
}
