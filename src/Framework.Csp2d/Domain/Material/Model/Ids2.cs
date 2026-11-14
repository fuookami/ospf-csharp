// 二维套裁的标识与生产对象契约 / 2D cutting stock identifiers and production object contracts.
// 与固定 Kotlin 基线 `csp2d/domain/material/model/Ids2.kt` 对应。
// Mirrors the pinned Kotlin baseline's `Ids2.kt`.

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;

/// <summary>生产对象编号 / A production object identifier.</summary>
public interface IProductionId2 {
    /// <summary>裸值 / The raw value.</summary>
    string Value { get; }
}

/// <summary>产品编号 / A product identifier.</summary>
public interface IProductId2 : IProductionId2 { }

/// <summary>物料编号 / A material identifier.</summary>
public interface IMaterialId2 : IProductionId2 { }

/// <summary>设备编号 / A machine identifier.</summary>
public interface IMachineId2 : IProductionId2 { }

/// <summary>副产物编号 / A by-product identifier.</summary>
public interface ICostarId2 : IProductionId2 { }

/// <summary>裁切图案编号 / A cutting pattern identifier.</summary>
public interface IPatternId2 {
    /// <summary>裸值 / The raw value.</summary>
    string Value { get; }
}

/// <summary>裁切方案编号 / A cutting plan identifier.</summary>
public interface IPlanId2 {
    /// <summary>裸值 / The raw value.</summary>
    string Value { get; }
}

/// <summary>
/// 字符串编号的通用实现 / Generic implementation of a string identifier.
/// </summary>
/// <param name="Value">裸值 / The raw value.</param>
/// <remarks>
/// **一个类同时实现四个生产对象编号**，与基线一致：产品、物料、设备、副产物共享同一编号口径，
/// 各自只在**接口层面**区分。拆成四个类会让调用方每次都要多写一次转换，而**语义区分本来靠接口就够了**。
///
/// **One class implements all four production-object identifiers**, matching the baseline: products, materials, machines and by-products share one identifier convention
/// and differ only **at the interface level**. Splitting into four classes would force a conversion at every call site while **the interface already carries the
/// distinction**.
/// </remarks>
public sealed record Csp2dId(string Value) : IProductId2, IMaterialId2, IMachineId2, ICostarId2;

/// <summary>裁切图案编号实现 / Cutting pattern identifier implementation.</summary>
/// <param name="Value">裸值 / The raw value.</param>
public sealed record PatternId2Impl(string Value) : IPatternId2;

/// <summary>裁切方案编号实现 / Cutting plan identifier implementation.</summary>
/// <param name="Value">裸值 / The raw value.</param>
public sealed record PlanId2Impl(string Value) : IPlanId2;

/// <summary>编号工厂 / Identifier factories.</summary>
public static class Csp2dIds {
    /// <summary>由字符串构造产品编号 / Build a product identifier from a string.</summary>
    /// <param name="value">裸值 / The raw value.</param>
    /// <returns>产品编号 / The product identifier.</returns>
    public static IProductId2 ProductId2Of(string value) => new Csp2dId(value);

    /// <summary>由字符串构造物料编号 / Build a material identifier from a string.</summary>
    /// <param name="value">裸值 / The raw value.</param>
    /// <returns>物料编号 / The material identifier.</returns>
    public static IMaterialId2 MaterialId2Of(string value) => new Csp2dId(value);

    /// <summary>由字符串构建设备编号 / Build a machine identifier from a string.</summary>
    /// <param name="value">裸值 / The raw value.</param>
    /// <returns>设备编号 / The machine identifier.</returns>
    public static IMachineId2 MachineId2Of(string value) => new Csp2dId(value);

    /// <summary>由字符串构造副产物编号 / Build a by-product identifier from a string.</summary>
    /// <param name="value">裸值 / The raw value.</param>
    /// <returns>副产物编号 / The by-product identifier.</returns>
    public static ICostarId2 CostarId2Of(string value) => new Csp2dId(value);

    /// <summary>由字符串构造裁切图案编号 / Build a cutting pattern identifier from a string.</summary>
    /// <param name="value">裸值 / The raw value.</param>
    /// <returns>裁切图案编号 / The cutting pattern identifier.</returns>
    public static IPatternId2 PatternId2Of(string value) => new PatternId2Impl(value);

    /// <summary>由字符串构造裁切方案编号 / Build a cutting plan identifier from a string.</summary>
    /// <param name="value">裸值 / The raw value.</param>
    /// <returns>裁切方案编号 / The cutting plan identifier.</returns>
    public static IPlanId2 PlanId2Of(string value) => new PlanId2Impl(value);
}

/// <summary>
/// 生产对象：可被套裁出的东西 / A production object: something the cutting stock process can produce.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <remarks>
/// **`SheetShape()` 本批未移植**：基线返回 `Sheet2Shape&lt;V&gt;`，而这个类型当前住在 `Framework.Bpp2d` 里。
/// 让 `Csp2d` 依赖 `Bpp2d` 会把模块归属倒过来（套裁去依赖装箱），所以先只保留面积与尺寸，
/// **等第二个消费者出现时再把形状类型提到公共层**。
///
/// **`Area()` 同样未移植**：C# 的 `Quantity&lt;V&gt;` **没有乘积运算符**，唯一的乘积助手 `QuantityArithmetic.Product` 住在 `Framework.Bpp2d` 里——
/// 用它就是同一处归属倒挂。**等公共层的量运算出现时一并接上。**
///
/// **`Area()` is not ported either**: C# `Quantity&lt;V&gt;` **has no product operator**, and the only product helper `QuantityArithmetic.Product` lives in
/// `Framework.Bpp2d` — using it is the same ownership inversion. **It comes back when a shared quantity-arithmetic layer exists.**
///
/// **`SheetShape()` is not ported in this batch**: the baseline returns `Sheet2Shape&lt;V&gt;`, which currently lives in `Framework.Bpp2d`. Making `Csp2d` depend on `Bpp2d`
/// inverts module ownership (cutting stock depending on bin packing), so only area and dimensions are kept for now — **the shape type moves to a shared layer once a
/// second consumer appears**.
/// </remarks>
public interface IProduction2<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>编号 / The identifier.</summary>
    IProductionId2 Id { get; }

    /// <summary>名称 / The name.</summary>
    string Name { get; }

    /// <summary>宽度 / The width.</summary>
    Quantity<V> Width { get; }

    /// <summary>高度；卷材物料为空 / The height; null for a coil material.</summary>
    Quantity<V>? Height { get; }

    /// <summary>单位重量 / The unit weight.</summary>
    Quantity<V>? UnitWeight { get; }
}
