#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4
{
    /// <summary>
    /// 货物：具有尺寸和重量属性。Package with dimensions and weight.
    /// </summary>
    public sealed class PackageItem
    {
        public string Id { get; }
        public double Width { get; }
        public double Height { get; }
        public double Depth { get; }
        public double Weight { get; }

        public PackageItem(string id, double width, double height, double depth, double weight)
        {
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
    public sealed class LoadingBin
    {
        public string Id { get; }
        public double Width { get; }
        public double Height { get; }
        public double Depth { get; }
        public double MaxWeight { get; }

        public LoadingBin(string id, double width, double height, double depth, double maxWeight)
        {
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
    public sealed class Bpp3dDemo
    {
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
        public Result<Success, ErrorCode, Error<ErrorCode>> BuildModel()
        {
            // Validate domain model setup
            if (Items.Count == 0)
            {
                return Results.Failed<Success>(
                    new Err<ErrorCode>(ErrorCode.ApplicationError, "No items to pack"));
            }

            // Check all items fit in bin dimensions
            foreach (var item in Items)
            {
                if (item.Width > BinTemplate.Width ||
                    item.Height > BinTemplate.Height ||
                    item.Depth > BinTemplate.Depth)
                {
                    return Results.Failed<Success>(
                        new Err<ErrorCode>(ErrorCode.ApplicationError,
                            $"Item {item.Id} exceeds bin dimensions"));
                }
            }

            return Results.Ok(Results.SuccessInstance);
        }
    }
}
