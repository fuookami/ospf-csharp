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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo3
{
    /// <summary>
    /// 切割需求：具有宽度和需求数量。Cutting demand with width and demand quantity.
    /// </summary>
    public sealed class CuttingDemand
    {
        public int Index { get; }
        public double Width { get; }
        public double Demand { get; }

        public CuttingDemand(int index, double width, double demand)
        {
            Index = index;
            Width = width;
            Demand = demand;
        }
    }

    /// <summary>
    /// 切割模式：描述如何从原料切割出各产品。Cutting pattern describing how to cut products from raw material.
    /// </summary>
    public sealed class CuttingPattern
    {
        public int Index { get; }
        public IReadOnlyList<int> Quantities { get; }

        public CuttingPattern(int index, IReadOnlyList<int> quantities)
        {
            Index = index;
            Quantities = quantities;
        }
    }

    /// <summary>
    /// 列生成演示：一维下料问题（CSP1D）。
    /// Column generation demo: One-dimensional Cutting Stock Problem (CSP1D).
    ///
    /// CSP1D 框架尚未在 C# 端实现，此演示展示核心建模模式。
    /// The CSP1D framework is not yet implemented on the C# side; this demo shows the core modeling pattern.
    /// </summary>
    public sealed class ColumnGenerationDemo
    {
        private const double RawLength = 1000.0;

        private static readonly List<CuttingDemand> Demands = new()
        {
            new CuttingDemand(0, 450.0, 97.0),
            new CuttingDemand(1, 360.0, 610.0),
            new CuttingDemand(2, 310.0, 395.0),
            new CuttingDemand(3, 140.0, 211.0)
        };

        private readonly List<CuttingPattern> _patterns = new();
        private readonly List<UIntVar> _y = new();
        private readonly LinearMetaModel<Flt64> _metaModel = new("demo3-csp1d", ObjectCategory.Minimum);

        public LinearMetaModel<Flt64> MetaModel => _metaModel;
        public IReadOnlyList<CuttingPattern> Patterns => _patterns;

        /// <summary>
        /// 构建 CSP1D 主问题模型（使用初始切割模式）。
        /// Build the CSP1D master problem model with initial cutting patterns.
        /// </summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> BuildModel()
        {
            GenerateInitialPatterns();

            var r1 = InitVariables();
            if (r1.IsFailed) return r1;

            var r2 = InitObjective();
            if (r2.IsFailed) return r2;

            var r3 = InitConstraints();
            if (r3.IsFailed) return r3;

            return Results.Ok(Results.SuccessInstance);
        }

        private void GenerateInitialPatterns()
        {
            // Generate initial single-item patterns: each demand gets its own pattern
            for (var d = 0; d < Demands.Count; d++)
            {
                var quantities = new int[Demands.Count];
                quantities[d] = (int)(RawLength / Demands[d].Width);
                _patterns.Add(new CuttingPattern(d, quantities));
            }
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitVariables()
        {
            // y[pattern] = number of times this pattern is used
            foreach (var pattern in _patterns)
            {
                var y = new UIntVar($"y_{pattern.Index}");
                _y.Add(y);
                var result = _metaModel.Add(y);
                if (result.IsFailed) return result;
            }
            return Results.Ok(Results.SuccessInstance);
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective()
        {
            // minimize total number of raw material cuts: sum(y)
            var objPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (var y in _y)
            {
                objPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, y));
            }
            return _metaModel.AddObject(
                ObjectCategory.Minimum,
                objPoly.ToLinearPolynomial(),
                "totalCuts",
                "Total Raw Material Cuts");
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints()
        {
            // For each demand d: sum(pattern.quantities[d] * y[pattern]) >= demand
            for (var d = 0; d < Demands.Count; d++)
            {
                var demandPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                for (var p = 0; p < _patterns.Count; p++)
                {
                    if (_patterns[p].Quantities[d] > 0)
                    {
                        demandPoly.AddMonomial(new LinearMonomial<Flt64>(
                            new Flt64(_patterns[p].Quantities[d]),
                            _y[p]));
                    }
                }
                var constraint = demandPoly.ToLinearPolynomial().Ge(new Flt64(Demands[d].Demand));
                var result = _metaModel.AddConstraint(constraint, group: null, name: $"demand_{d}");
                if (result.IsFailed) return result;
            }
            return Results.Ok(Results.SuccessInstance);
        }
    }
}
