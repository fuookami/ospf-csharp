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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo5
{
    /// <summary>
    /// 下料需求：具有宽度和数量。Cutting demand with width and quantity.
    /// </summary>
    public sealed class StockDemand
    {
        public int Index { get; }
        public double Width { get; }
        public int Quantity { get; }

        public StockDemand(int index, double width, int quantity)
        {
            Index = index;
            Width = width;
            Quantity = quantity;
        }
    }

    /// <summary>
    /// 一维下料问题演示（CSP1D）。
    /// One-dimensional Cutting Stock Problem demo (CSP1D).
    ///
    /// CSP1D 框架尚未在 C# 端实现，此演示使用核心建模 API 展示 CSP1D 主问题。
    /// The CSP1D framework is not yet implemented on the C# side;
    /// this demo uses the core modeling API to show the CSP1D master problem.
    /// </summary>
    public sealed class CuttingStockDemo
    {
        private const double RawLength = 100.0;

        private static readonly List<StockDemand> Demands = new()
        {
            new StockDemand(0, 45.0, 97),
            new StockDemand(1, 36.0, 610),
            new StockDemand(2, 31.0, 395),
            new StockDemand(3, 14.0, 211)
        };

        private readonly List<List<int>> _patterns = new();
        private readonly List<UIntVar> _patternVars = new();
        private readonly LinearMetaModel<Flt64> _metaModel = new("demo5-cutting-stock", ObjectCategory.Minimum);

        public LinearMetaModel<Flt64> MetaModel => _metaModel;
        public IReadOnlyList<StockDemand> StockDemands => Demands;

        /// <summary>
        /// 构建下料主问题模型。
        /// Build the cutting stock master problem model.
        /// </summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> BuildModel()
        {
            GenerateInitialPatterns();

            var r1 = InitVariables();
            if (r1.IsFailed) return r1;

            var r2 = InitObjective();
            if (r2.IsFailed) return r2;

            var r3 = InitDemandConstraints();
            if (r3.IsFailed) return r3;

            return Results.Ok(Results.SuccessInstance);
        }

        private void GenerateInitialPatterns()
        {
            // Initial patterns: one per demand, maximizing that single item
            for (var d = 0; d < Demands.Count; d++)
            {
                var pattern = new int[Demands.Count];
                pattern[d] = (int)(RawLength / Demands[d].Width);
                _patterns.Add(pattern.ToList());
            }
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitVariables()
        {
            for (var p = 0; p < _patterns.Count; p++)
            {
                var y = new UIntVar($"pattern_{p}");
                _patternVars.Add(y);
                var result = _metaModel.Add(y);
                if (result.IsFailed) return result;
            }
            return Results.Ok(Results.SuccessInstance);
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective()
        {
            // minimize sum of all pattern usages
            var objPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (var y in _patternVars)
            {
                objPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, y));
            }
            return _metaModel.AddObject(
                ObjectCategory.Minimum,
                objPoly.ToLinearPolynomial(),
                "totalRolls",
                "Total Raw Rolls Used");
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitDemandConstraints()
        {
            // For each demand d: sum(pattern[d] * y[pattern]) >= demand.quantity
            for (var d = 0; d < Demands.Count; d++)
            {
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                for (var p = 0; p < _patterns.Count; p++)
                {
                    if (_patterns[p][d] > 0)
                    {
                        poly.AddMonomial(new LinearMonomial<Flt64>(
                            new Flt64(_patterns[p][d]),
                            _patternVars[p]));
                    }
                }
                var constraint = poly.ToLinearPolynomial().Ge(new Flt64(Demands[d].Quantity));
                var result = _metaModel.AddConstraint(constraint, group: null, name: $"demand_{d}");
                if (result.IsFailed) return result;
            }
            return Results.Ok(Results.SuccessInstance);
        }
    }
}
