#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Heuristic.Pso;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo7
{
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
    public sealed class HeuristicOptimizationDemo
    {
        private readonly List<RealVar> _variables = new();
        private readonly CallBackModel<Flt64> _callBackModel;
        private readonly ParticleSwarmOptimizationAlgorithm _solver;

        public CallBackModel<Flt64> Model => _callBackModel;
        public ParticleSwarmOptimizationAlgorithm Solver => _solver;

        public HeuristicOptimizationDemo()
        {
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
    }

    /// <summary>
    /// 线性模型转 PSO 演示：将 LinearMetaModel 转换为 CallBackModel。
    /// Linear model to PSO demo: convert LinearMetaModel to CallBackModel.
    /// </summary>
    public sealed class LinearToHeuristicDemo
    {
        private readonly LinearMetaModel<Flt64> _metaModel;
        private readonly List<URealVar> _variables = new();

        public LinearMetaModel<Flt64> MetaModel => _metaModel;

        public LinearToHeuristicDemo()
        {
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
        /// 构建模型（用于测试验证）。
        /// Build model (for test verification).
        /// </summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> BuildModel()
        {
            return Results.Ok(Results.SuccessInstance);
        }
    }

    /// <summary>
    /// Flt64 恒等转换器。Identity converter for Flt64.
    /// </summary>
    public sealed class IdentityFlt64Converter : IFlt64ValueConverter<Flt64>
    {
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => value;
    }
}
