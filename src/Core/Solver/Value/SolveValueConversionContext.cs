#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using System;

namespace Fuookami.Ospf.Core.Solver.Value;
/// <summary>
/// 求解值转换上下文，管理当前的转换策略。
/// Solve value conversion context, managing the current conversion policy.
/// </summary>
public static class SolveValueConversionContext {
    [ThreadStatic]
    private static SolveValueConversionPolicy? _currentPolicy;

    /// <summary>
    /// 获取当前的值转换策略。
    /// Get the current solve value conversion policy.
    /// </summary>
    public static SolveValueConversionPolicy CurrentSolveValueConversionPolicy()
        => _currentPolicy ?? SolveValueConversionPolicy.AllowRounding;

    /// <summary>
    /// 设置当前的值转换策略（线程静态）。
    /// Set the current solve value conversion policy (thread-static).
    /// </summary>
    public static IDisposable SetPolicy(SolveValueConversionPolicy policy) {
        SolveValueConversionPolicy? previous = _currentPolicy;
        _currentPolicy = policy;
        return new PolicyScope(previous);
    }

    /// <summary>
    /// 将 Flt64 转换为求解器 double 值。
    /// Convert Flt64 to solver double value.
    /// </summary>
    public static double ToSolverDouble(this Flt64 value, SolveValueConversionPolicy? policy = null) {
        SolveValueConversionPolicy effectivePolicy = policy ?? CurrentSolveValueConversionPolicy();
        double d = value.ToDouble();
        return effectivePolicy switch {
            SolveValueConversionPolicy.Strict => d,
            SolveValueConversionPolicy.AllowRounding => System.Math.Round(d, 15),
            _ => d,
        };
    }

    private sealed class PolicyScope : IDisposable {
        private readonly SolveValueConversionPolicy? _previous;

        public PolicyScope(SolveValueConversionPolicy? previous) {
            _previous = previous;
        }

        public void Dispose() => _currentPolicy = _previous;
    }
}
