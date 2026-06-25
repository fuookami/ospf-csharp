#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Heuristic.Pso;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo7;
/// <summary>
/// 启发式优化演示：PSO 求解简单函数优化。
/// Heuristic optimization demo: PSO solving a simple function optimization.
///
/// 演示1：CallBackModel 上的 PSO 最小化 (x - 1)^2
/// Demo1: PSO minimizing (x - 1)^2 on CallBackModel
///
/// 演示2：LinearMetaModel 转换为 CallBackModel 后用 PSO 求解
/// Demo2: LinearMetaModel converted to CallBackModel, then solved with PSO
/// </summary>
public sealed class HeuristicOptimizationDemo {
    private readonly List<RealVar> _variables = new();
    private readonly CallBackModel<Flt64> _callBackModel;
    private readonly ParticleSwarmOptimizationAlgorithm _solver;

    public CallBackModel<Flt64> Model => _callBackModel;
    public ParticleSwarmOptimizationAlgorithm Solver => _solver;

    public HeuristicOptimizationDemo() {
        // Demo1 pattern: minimize (x - 1)^2 via PSO on CallBackModel
        _callBackModel = CallBackModel<Flt64>.Create(
            ObjectCategory.Minimum,
            new IdentityFlt64Converter(),
            initialSolutionGenerator: (s, v) => new Flt64(Random.Shared.NextDouble() * 10.0));

        var x = new RealVar("x");
        _variables.Add(x);
        _callBackModel.Tokens.Add(x);

        _callBackModel.Minimize(
            solution => (solution[0] - Flt64.One) * (solution[0] - Flt64.One),
            name: "quadratic");

        _solver = new ParticleSwarmOptimizationAlgorithm(new PsoPolicy(
            maxIterations: 100,
            particleCount: 50));
    }

    /// <summary>
    /// 获取变量列表（用于测试验证）。
    /// Get variable list (for test verification).
    /// </summary>
    public IReadOnlyList<RealVar> Variables => _variables;

    /// <summary>
    /// 使用 PSO 求解回调模型。Solve the callback model using PSO.
    ///
    /// Demonstrates the pattern:
    ///   1. Create CallBackModel with objective function.
    ///   2. Create PSO solver with policy parameters.
    ///   3. Call solver.InvokeAsync(model) to run optimization.
    ///   4. Extract best solution from returned individuals.
    ///
    /// 演示模式：
    ///   1. 创建带回调目标函数的 CallBackModel。
    ///   2. 创建带策略参数的 PSO 求解器。
    ///   3. 调用 solver.InvokeAsync(model) 运行优化。
    ///   4. 从返回的个体中提取最优解。
    /// </summary>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>最优个体列表 / Best individuals found.</returns>
    public async Task<Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>> SolveAsync(
        CancellationToken cancellationToken = default) => await _solver.InvokeAsync(_callBackModel, cancellationToken: cancellationToken);
}

/// <summary>
/// 线性模型转 PSO 演示：将 LinearMetaModel 转换为 CallBackModel。
/// Linear model to PSO demo: convert LinearMetaModel to CallBackModel.
/// </summary>
public sealed class LinearToHeuristicDemo {
    private readonly LinearMetaModel<Flt64> _metaModel;
    private readonly List<URealVar> _variables = new();

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    public LinearToHeuristicDemo() {
        _metaModel = new LinearMetaModel<Flt64>("demo7-linear-to-heuristic", ObjectCategory.Maximum);

        // Variables: x, y in [0, 2]
        var x = new URealVar("x");
        var y = new URealVar("y");
        _variables.Add(x);
        _variables.Add(y);

        _metaModel.Add(x);
        _metaModel.Add(y);

        // maximize x + y
        var objPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        objPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, x));
        objPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, y));
        _metaModel.AddObject(
            ObjectCategory.Maximum,
            objPoly.ToLinearPolynomial(),
            "sum",
            "Maximize x + y");
    }

    /// <summary>
    /// 构建模型（添加约束 x + y &lt;= 10）。
    /// Build model (add constraint x + y &lt;= 10).
    /// </summary>
    public Result<Success, ErrorCode, Error<ErrorCode>> BuildModel() {
        // Add constraint: x + y <= 10
        // This bounds the feasible region for the maximization problem.
        URealVar x = _variables[0];
        URealVar y = _variables[1];

        var constraintPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        constraintPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, x));
        constraintPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, y));
        LinearInequality<Flt64> constraint = constraintPoly.ToLinearPolynomial().Le(new Flt64(10.0));
        Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: "x_plus_y_limit");
        if (result.IsFailed) {
            return result;
        }

        return Results.Ok(Results.SuccessInstance);
    }
}

/// <summary>
/// Flt64 恒等转换器。Identity converter for Flt64.
/// </summary>
public sealed class IdentityFlt64Converter : IFlt64ValueConverter<Flt64> {
    public Flt64 Zero => Flt64.Zero;
    public Flt64 One => Flt64.One;
    public Flt64 IntoValue(Flt64 value) => value;
    public Flt64 FromValue(Flt64 value) => value;
}
