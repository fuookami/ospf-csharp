#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo9
{
    /// <summary>
    /// 远程求解器演示：构建可序列化模型以供远程求解。
    /// Remote solver demo: build a serializable model for remote solving.
    ///
    /// 此演示展示如何构建一个可以导出并发送到远程求解服务的模型。
    /// This demo shows how to build a model that can be exported and sent to a remote solving service.
    /// </summary>
    public sealed class RemoteSolverDemo
    {
        private readonly List<BinVar> _x = new();
        private LinearExpressionSymbol? _objective;
        private readonly LinearMetaModel<Flt64> _metaModel = new("demo9-remote-solver", ObjectCategory.Maximum);

        public LinearMetaModel<Flt64> MetaModel => _metaModel;

        /// <summary>
        /// 构建远程求解模型（简单的资源分配问题）。
        /// Build the remote solver model (simple resource allocation problem).
        /// </summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> BuildModel()
        {
            var r1 = InitVariables();
            if (r1.IsFailed) return r1;

            var r2 = InitObjective();
            if (r2.IsFailed) return r2;

            var r3 = InitConstraints();
            if (r3.IsFailed) return r3;

            return Results.Ok(Results.SuccessInstance);
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitVariables()
        {
            // 5 binary decision variables
            for (var i = 0; i < 5; i++)
            {
                var x = new BinVar($"x_{i}");
                _x.Add(x);
                var result = _metaModel.Add(x);
                if (result.IsFailed) return result;
            }
            return Results.Ok(Results.SuccessInstance);
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective()
        {
            // maximize sum of weighted variables
            var profits = new[] { 10.0, 15.0, 8.0, 12.0, 20.0 };
            var objPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            for (var i = 0; i < _x.Count; i++)
            {
                objPoly.AddMonomial(new LinearMonomial<Flt64>(new Flt64(profits[i]), _x[i]));
            }
            _objective = new LinearExpressionSymbol(objPoly, name: "profit");
            _metaModel.Add(_objective);

            return _metaModel.AddObject(
                ObjectCategory.Maximum,
                _objective.Polynomial,
                "profit",
                "Total Profit");
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints()
        {
            // Resource constraints: sum of weighted selections <= capacity
            var weights = new[] { 3.0, 5.0, 2.0, 4.0, 6.0 };
            var capacity = 10.0;

            var constraintPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            for (var i = 0; i < _x.Count; i++)
            {
                constraintPoly.AddMonomial(new LinearMonomial<Flt64>(new Flt64(weights[i]), _x[i]));
            }
            var constraint = constraintPoly.ToLinearPolynomial().Le(new Flt64(capacity));
            return _metaModel.AddConstraint(constraint, group: null, name: "capacity");
        }

        /// <summary>
        /// 模拟远程求解（本地构建模型后，在实际场景中会导出并发送到远程服务）。
        /// Simulate remote solving (in real scenarios, the model would be exported and sent to a remote service).
        /// </summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> SimulateRemoteExport()
        {
            // In a real implementation, this would:
            // 1. Export the meta-model to a serializable format (JSON/protobuf)
            // 2. Send to a remote solving service via HTTP/gRPC
            // 3. Receive and parse the solution
            // For now, we verify the model is well-formed
            if (_metaModel.MetaSubObjects.Count == 0)
            {
                return Results.Failed<Success>(
                    new Err<ErrorCode>(ErrorCode.ApplicationError, "Model has no objectives"));
            }
            return Results.Ok(Results.SuccessInstance);
        }
    }
}
