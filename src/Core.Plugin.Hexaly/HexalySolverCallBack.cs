#nullable enable

using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Hexaly.Optimizer;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Hexaly;

/// <summary>创建环境函数类型 / Creating environment function type.</summary>
/// <param name="optimizer">Hexaly 优化器 / Hexaly optimizer.</param>
/// <returns>操作结果 / Operation result.</returns>
public delegate Result<Success, ErrorCode, Error<ErrorCode>> CreatingEnvironmentFunction(HexalyOptimizer optimizer);

/// <summary>Hexaly 原生回调函数类型 / Hexaly native callback function type.</summary>
/// <param name="optimizer">Hexaly 优化器 / Hexaly optimizer.</param>
/// <param name="callbackType">回调类型 / Callback type.</param>
public delegate void NativeCallBack(HexalyOptimizer optimizer, HxCallbackType callbackType);

/// <summary>Hexaly 求解器回调函数类型 / Hexaly solver callback function type.</summary>
public delegate Result<Success, ErrorCode, Error<ErrorCode>> HexalySolverFunction(
    SolverStatus? status, HexalyOptimizer optimizer, IReadOnlyList<HxExpression> variables, IReadOnlyList<HxExpression> constraints);

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
/// Hexaly 求解器回调管理器 / Hexaly solver callback manager.
/// </summary>
public sealed class HexalySolverCallBack : ICloneable {
    /// <summary>原生回调函数 / Native callback function.</summary>
    public NativeCallBack? NativeCallback { get; set; }
    /// <summary>创建环境函数 / Creating environment function.</summary>
    public CreatingEnvironmentFunction? CreatingEnvironmentFunction { get; set; }

    private readonly Dictionary<CallBackPoint, List<HexalySolverFunction>> _map = new();

    public HexalySolverCallBack(
        NativeCallBack? nativeCallback = null,
        CreatingEnvironmentFunction? creatingEnvironmentFunction = null,
        Dictionary<CallBackPoint, List<HexalySolverFunction>>? map = null) {
        NativeCallback = nativeCallback;
        CreatingEnvironmentFunction = creatingEnvironmentFunction;
        if (map is not null) {
            foreach (KeyValuePair<CallBackPoint, List<HexalySolverFunction>> kv in map) {
                _map[kv.Key] = new List<HexalySolverFunction>(kv.Value);
            }
        }
    }

    /// <summary>在建模完成后添加回调函数 / Add callback after modeling.</summary>
    public HexalySolverCallBack AfterModeling(HexalySolverFunction f) => Add(CallBackPoint.AfterModeling, f);
    /// <summary>在配置阶段添加回调函数 / Add callback at configuration phase.</summary>
    public HexalySolverCallBack Configuration(HexalySolverFunction f) => Add(CallBackPoint.Configuration, f);
    /// <summary>在分析解阶段添加回调函数 / Add callback at analyzing solution phase.</summary>
    public HexalySolverCallBack AnalyzingSolution(HexalySolverFunction f) => Add(CallBackPoint.AnalyzingSolution, f);
    /// <summary>在求解失败后添加回调函数 / Add callback after failure.</summary>
    public HexalySolverCallBack AfterFailure(HexalySolverFunction f) => Add(CallBackPoint.AfterFailure, f);

    private HexalySolverCallBack Add(CallBackPoint p, HexalySolverFunction f) {
        if (!_map.TryGetValue(p, out List<HexalySolverFunction>? list)) {
            _map[p] = list = new List<HexalySolverFunction>();
        }
        list.Add(f);
        return this;
    }

    /// <summary>检查是否包含指定时机的回调 / Check if callback at point is contained.</summary>
    public bool Contains(CallBackPoint point) => _map.ContainsKey(point);

    /// <summary>获取指定时机的回调函数列表 / Get callback function list at point.</summary>
    public IReadOnlyList<HexalySolverFunction>? Get(CallBackPoint point) =>
        _map.TryGetValue(point, out List<HexalySolverFunction>? l) ? l : null;

    /// <summary>执行创建环境回调 / Execute creating-environment callback if present.</summary>
    public Result<Success, ErrorCode, Error<ErrorCode>>? ExecIfContain(HexalyOptimizer optimizer) =>
        CreatingEnvironmentFunction?.Invoke(optimizer);

    /// <summary>执行指定时机回调 / Execute callbacks at point if present.</summary>
    public Result<Success, ErrorCode, Error<ErrorCode>>? ExecIfContain(
        CallBackPoint point, SolverStatus? status, HexalyOptimizer optimizer,
        IReadOnlyList<HxExpression> variables, IReadOnlyList<HxExpression> constraints) {
        if (!_map.TryGetValue(point, out List<HexalySolverFunction>? list) || list.Count == 0) {
            return null;
        }

        Result<Success, ErrorCode, Error<ErrorCode>>? last = null;
        foreach (HexalySolverFunction f in list) {
            last = f(status, optimizer, variables, constraints);
            if (last is Failed<Success, ErrorCode, Error<ErrorCode>>
                or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return last;
            }
        }
        return last;
    }

    /// <summary>复制回调管理器 / Copy (deep) callback manager.</summary>
    public HexalySolverCallBack Copy() => new(NativeCallback, CreatingEnvironmentFunction, _map);
    object ICloneable.Clone() => Copy();
}
