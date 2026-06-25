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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2
{
    /// <summary>
    /// 产品：具有名称属性。Product with name attribute.
    /// </summary>
    public sealed class Product
    {
        public int Index { get; }
        public string Name { get; }

        public Product(int index, string name)
        {
            Index = index;
            Name = name;
        }
    }

    /// <summary>
    /// 公司：具有针对各产品的成本。Company with cost per product.
    /// </summary>
    public sealed class AssignmentCompany
    {
        public int Index { get; }
        public string Name { get; }
        public IReadOnlyDictionary<int, Flt64> Cost { get; }

        public AssignmentCompany(int index, string name, IReadOnlyDictionary<int, Flt64> cost)
        {
            Index = index;
            Name = name;
            Cost = cost;
        }
    }

    /// <summary>
    /// 整数规划演示：最小成本产品-公司分配。
    /// Integer programming demo: minimum cost product-to-company assignment.
    /// </summary>
    public sealed class AssignmentDemo
    {
        private static readonly List<Product> Products = new()
        {
            new Product(0, "P1"),
            new Product(1, "P2"),
            new Product(2, "P3"),
            new Product(3, "P4")
        };

        private static readonly List<AssignmentCompany> Companies = new()
        {
            new AssignmentCompany(0, "C1", new Dictionary<int, Flt64>
            {
                { 0, new Flt64(2) }, { 1, new Flt64(3) }, { 2, new Flt64(1) }, { 3, new Flt64(4) }
            }),
            new AssignmentCompany(1, "C2", new Dictionary<int, Flt64>
            {
                { 0, new Flt64(3) }, { 1, new Flt64(2) }, { 2, new Flt64(4) }, { 3, new Flt64(1) }
            }),
            new AssignmentCompany(2, "C3", new Dictionary<int, Flt64>
            {
                { 0, new Flt64(1) }, { 1, new Flt64(4) }, { 2, new Flt64(2) }, { 3, new Flt64(3) }
            }),
            new AssignmentCompany(3, "C4", new Dictionary<int, Flt64>
            {
                { 0, new Flt64(4) }, { 1, new Flt64(1) }, { 2, new Flt64(3) }, { 3, new Flt64(2) }
            })
        };

        private readonly List<List<BinVar>> _x = new();
        private LinearExpressionSymbol? _cost;
        private readonly LinearMetaModel<Flt64> _metaModel = new("demo2-assignment", ObjectCategory.Minimum);

        public LinearMetaModel<Flt64> MetaModel => _metaModel;

        /// <summary>
        /// 构建分配模型（不求解，用于测试验证）。
        /// Build the assignment model (without solving, for test verification).
        /// </summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> BuildModel()
        {
            var r1 = InitVariables();
            if (r1.IsFailed) return r1;

            var r2 = InitSymbols();
            if (r2.IsFailed) return r2;

            var r3 = InitObjective();
            if (r3.IsFailed) return r3;

            var r4 = InitConstraints();
            if (r4.IsFailed) return r4;

            return Results.Ok(Results.SuccessInstance);
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitVariables()
        {
            foreach (var company in Companies)
            {
                var companyVars = new List<BinVar>();
                foreach (var product in Products)
                {
                    var x = new BinVar($"x_{company.Index}_{product.Index}");
                    companyVars.Add(x);
                    var result = _metaModel.Add(x);
                    if (result.IsFailed) return result;
                }
                _x.Add(companyVars);
            }
            return Results.Ok(Results.SuccessInstance);
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols()
        {
            // cost = sum(company.cost[product] * x[company, product])
            var costPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (var company in Companies)
            {
                foreach (var product in Products)
                {
                    costPoly.AddMonomial(new LinearMonomial<Flt64>(
                        company.Cost[product.Index],
                        _x[company.Index][product.Index]));
                }
            }
            _cost = new LinearExpressionSymbol(costPoly, name: "cost");
            _metaModel.Add(_cost);

            return Results.Ok(Results.SuccessInstance);
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective()
        {
            return _metaModel.AddObject(
                ObjectCategory.Minimum,
                _cost!.Polynomial,
                "cost",
                "Total Assignment Cost");
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints()
        {
            // Each company assigns at most 1 product: sum(x[company, *]) <= 1
            foreach (var company in Companies)
            {
                var companyPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                foreach (var product in Products)
                {
                    companyPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _x[company.Index][product.Index]));
                }
                var constraint = companyPoly.ToLinearPolynomial().Le(Flt64.One);
                var result = _metaModel.AddConstraint(constraint, group: null, name: $"company_{company.Index}_max1");
                if (result.IsFailed) return result;
            }

            // Each product assigned exactly once: sum(x[*, product]) = 1
            foreach (var product in Products)
            {
                var productPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                foreach (var company in Companies)
                {
                    productPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _x[company.Index][product.Index]));
                }
                var constraint = productPoly.ToLinearPolynomial().Eq(Flt64.One);
                var result = _metaModel.AddConstraint(constraint, group: null, name: $"product_{product.Index}_assigned");
                if (result.IsFailed) return result;
            }

            return Results.Ok(Results.SuccessInstance);
        }
    }
}
