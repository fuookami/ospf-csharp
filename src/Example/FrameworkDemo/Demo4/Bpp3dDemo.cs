#nullable enable

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
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4;
/// <summary>
/// 货物：具有尺寸和重量属性。Package with dimensions and weight.
/// </summary>
public sealed class PackageItem {
    public string Id { get; }
    public double Width { get; }
    public double Height { get; }
    public double Depth { get; }
    public double Weight { get; }

    public PackageItem(string id, double width, double height, double depth, double weight) {
        Id = id;
        Width = width;
        Height = height;
        Depth = depth;
        Weight = weight;
    }
}

/// <summary>
/// 装载容器：具有容量属性。Bin with capacity attributes.
/// </summary>
public sealed class LoadingBin {
    public string Id { get; }
    public double Width { get; }
    public double Height { get; }
    public double Depth { get; }
    public double MaxWeight { get; }

    public LoadingBin(string id, double width, double height, double depth, double maxWeight) {
        Id = id;
        Width = width;
        Height = height;
        Depth = depth;
        MaxWeight = maxWeight;
    }
}

/// <summary>
/// 分支定价演示：三维装箱问题（BPP3D）。
/// Branch-and-price demo: Three-dimensional Bin Packing Problem (BPP3D).
///
/// 此演示展示 BPP3D 框架的域模型设置。
/// 完整的列生成求解需要 BPP3D 框架的列生成算法。
/// This demo shows the BPP3D framework domain model setup.
/// Full column generation solving requires the BPP3D framework's column generation algorithm.
/// </summary>
public sealed class Bpp3dDemo {
    private static readonly List<PackageItem> Items = new()
    {
        new PackageItem("item-1", 10.0, 10.0, 10.0, 5.0),
        new PackageItem("item-2", 20.0, 15.0, 10.0, 8.0),
        new PackageItem("item-3", 15.0, 10.0, 20.0, 6.0),
        new PackageItem("item-4", 25.0, 20.0, 15.0, 12.0),
        new PackageItem("item-5", 10.0, 10.0, 5.0, 3.0)
    };

    private static readonly LoadingBin BinTemplate = new("bin-1", 50.0, 50.0, 50.0, 50.0);

    public IReadOnlyList<PackageItem> Packages => Items;
    public LoadingBin Bin => BinTemplate;

    /// <summary>
    /// 构建 BPP3D 域模型（不求解，用于测试验证域模型设置）。
    /// Build the BPP3D domain model (without solving, for test verification of domain model setup).
    /// </summary>
    public Result<Success, ErrorCode, Error<ErrorCode>> BuildModel() {
        // Validate domain model setup
        if (Items.Count == 0) {
            return Results.Failed<Success>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, "No items to pack"));
        }

        // Check all items fit in bin dimensions
        foreach (PackageItem item in Items) {
            if (item.Width > BinTemplate.Width ||
                item.Height > BinTemplate.Height ||
                item.Depth > BinTemplate.Depth) {
                return Results.Failed<Success>(
                    new Err<ErrorCode>(ErrorCode.ApplicationError,
                        $"Item {item.Id} exceeds bin dimensions"));
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}

/// <summary>
/// 简化的一维装箱 ILP 模型（基于 BPP3D 域模型）。
/// Simplified 1D bin packing ILP model (based on BPP3D domain model).
///
/// Uses item weights as 1D sizes, minimizes the number of bins used.
/// This demonstrates how to build an ILP model for bin packing within the BPP3D domain.
/// 使用物品重量作为一维尺寸，最小化使用的箱子数量。
/// 演示如何在 BPP3D 域中构建装箱 ILP 模型。
/// </summary>
public sealed class BinPackingModel {
    private readonly List<PackageItem> _items;
    private readonly LoadingBin _bin;
    private readonly int _maxBins;

    // x[i][b] = 1 if item i is assigned to bin b
    private readonly List<List<BinVar>> _x = new();
    // y[b] = 1 if bin b is used
    private readonly List<BinVar> _y = new();
    private readonly LinearMetaModel<Flt64> _metaModel = new("demo4-bin-packing", ObjectCategory.Minimum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;
    public IReadOnlyList<PackageItem> Items => _items;

    /// <summary>
    /// 创建装箱模型。Create bin packing model.
    /// </summary>
    /// <param name="items">待装箱物品 / Items to pack.</param>
    /// <param name="bin">容器模板 / Bin template.</param>
    /// <param name="maxBins">最大可用容器数 / Maximum number of bins available.</param>
    public BinPackingModel(IReadOnlyList<PackageItem> items, LoadingBin bin, int maxBins) {
        _items = items.ToList();
        _bin = bin;
        _maxBins = maxBins;
    }

    /// <summary>
    /// 构建装箱 ILP 模型（每个物品恰好分配到一个容器，最小化使用容器数）。
    /// Build the bin packing ILP model (each item assigned to exactly one bin, minimize bins used).
    /// </summary>
    public Result<Success, ErrorCode, Error<ErrorCode>> BuildModel() {
        Result<Success, ErrorCode, Error<ErrorCode>> r1 = InitVariables();
        if (r1.IsFailed) {
            return r1;
        }

        Result<Success, ErrorCode, Error<ErrorCode>> r2 = InitBinPackingObjective();
        if (r2.IsFailed) {
            return r2;
        }

        Result<Success, ErrorCode, Error<ErrorCode>> r3 = InitBinPackingConstraints();
        if (r3.IsFailed) {
            return r3;
        }

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitVariables() {
        // y[b] = 1 if bin b is used
        for (int b = 0; b < _maxBins; b++) {
            var yb = new BinVar($"y_{b}");
            _y.Add(yb);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(yb);
            if (result.IsFailed) {
                return result;
            }
        }

        // x[i][b] = 1 if item i is assigned to bin b
        for (int i = 0; i < _items.Count; i++) {
            var row = new List<BinVar>();
            for (int b = 0; b < _maxBins; b++) {
                var xib = new BinVar($"x_{i}_{b}");
                row.Add(xib);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(xib);
                if (result.IsFailed) {
                    return result;
                }
            }
            _x.Add(row);
        }

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitBinPackingObjective() {
        // minimize sum(y[b]) = number of bins used
        var objPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (BinVar yb in _y) {
            objPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, yb));
        }
        return _metaModel.AddObject(
            ObjectCategory.Minimum,
            objPoly.ToLinearPolynomial(),
            "binsUsed",
            "Number of Bins Used");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitBinPackingConstraints() {
        // Constraint 1: Each item assigned to exactly one bin
        // sum_b(x[i][b]) = 1 for each item i
        for (int i = 0; i < _items.Count; i++) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            for (int b = 0; b < _maxBins; b++) {
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _x[i][b]));
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Eq(Flt64.One);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"item_{i}_assigned");
            if (result.IsFailed) {
                return result;
            }
        }

        // Constraint 2: Item can only be assigned to a bin if that bin is used
        // x[i][b] <= y[b] for each item i and bin b
        for (int i = 0; i < _items.Count; i++) {
            for (int b = 0; b < _maxBins; b++) {
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _x[i][b]));
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One.Negate(), _y[b]));
                LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Le(Flt64.Zero);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"item_{i}_bin_{b}_link");
                if (result.IsFailed) {
                    return result;
                }
            }
        }

        // Constraint 3: Weight capacity per bin
        // sum_i(weight[i] * x[i][b]) <= bin.MaxWeight * y[b] for each bin b
        for (int b = 0; b < _maxBins; b++) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            for (int i = 0; i < _items.Count; i++) {
                poly.AddMonomial(new LinearMonomial<Flt64>(new Flt64(_items[i].Weight), _x[i][b]));
            }
            poly.AddMonomial(new LinearMonomial<Flt64>(new Flt64(_bin.MaxWeight).Negate(), _y[b]));
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Le(Flt64.Zero);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"bin_{b}_capacity");
            if (result.IsFailed) {
                return result;
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
