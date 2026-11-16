#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

/// <summary>
/// 位置位置标签。Position location tags for classification.
/// </summary>
public enum PositionLocationTag
{
    Main,
    Low,
    LowForward,
    LowAft,
    Bulk,
    Head,
    Tail
}

/// <summary>
/// 位置位置。Position location with classification tags.
/// </summary>
public sealed class PositionLocation
{
    /// <summary>标签集 / Tag set</summary>
    public IReadOnlySet<PositionLocationTag> Tags { get; }

    public PositionLocation(IReadOnlySet<PositionLocationTag> tags)
    {
        Tags = tags;
        Main = tags.Contains(PositionLocationTag.Main);
        Head = tags.Contains(PositionLocationTag.Head);
        Tail = tags.Contains(PositionLocationTag.Tail);
        NormalMain = Main && !Head && !Tail;
        SpecialMain = Head || Tail;
        Low = tags.Contains(PositionLocationTag.Low);
        LowForward = tags.Contains(PositionLocationTag.LowForward);
        LowAft = tags.Contains(PositionLocationTag.LowAft);
        Bulk = tags.Contains(PositionLocationTag.Bulk);
        LowNotBulk = Low && !Bulk;
        Location = Main ? DeckLocation.Main
            : LowForward ? DeckLocation.LowForward
            : LowAft ? DeckLocation.LowAft
            : DeckLocation.Main;
    }

    /// <summary>是否主甲板 / Whether main deck</summary>
    public bool Main { get; }
    /// <summary>是否头部 / Whether head</summary>
    public bool Head { get; }
    /// <summary>是否尾部 / Whether tail</summary>
    public bool Tail { get; }
    /// <summary>是否普通主甲板 / Whether normal main</summary>
    public bool NormalMain { get; }
    /// <summary>是否特殊主甲板 / Whether special main (head/tail)</summary>
    public bool SpecialMain { get; }
    /// <summary>是否下层 / Whether lower deck</summary>
    public bool Low { get; }
    /// <summary>是否前下层 / Whether lower forward</summary>
    public bool LowForward { get; }
    /// <summary>是否后下层 / Whether lower aft</summary>
    public bool LowAft { get; }
    /// <summary>是否散货区 / Whether bulk</summary>
    public bool Bulk { get; }
    /// <summary>是否下层非散货 / Whether lower non-bulk</summary>
    public bool LowNotBulk { get; }
    /// <summary>甲板位置 / Deck location</summary>
    public DeckLocation Location { get; }

    /// <summary>头部位置 / Head position</summary>
    public static readonly PositionLocation HeadLocation = new(new HashSet<PositionLocationTag> { PositionLocationTag.Main, PositionLocationTag.Head });
    /// <summary>尾部位置 / Tail position</summary>
    public static readonly PositionLocation TailLocation = new(new HashSet<PositionLocationTag> { PositionLocationTag.Main, PositionLocationTag.Tail });
    /// <summary>普通主甲板位置 / Normal main position</summary>
    public static readonly PositionLocation NormalMainLocation = new(new HashSet<PositionLocationTag> { PositionLocationTag.Main });
    /// <summary>前下散货位置 / Low forward bulk position</summary>
    public static readonly PositionLocation LowForwardBulkLocation = new(new HashSet<PositionLocationTag> { PositionLocationTag.Low, PositionLocationTag.LowForward, PositionLocationTag.Bulk });
    /// <summary>前下非散货位置 / Low forward non-bulk position</summary>
    public static readonly PositionLocation LowForwardNotBulkLocation = new(new HashSet<PositionLocationTag> { PositionLocationTag.Low, PositionLocationTag.LowForward });
    /// <summary>后下散货位置 / Low aft bulk position</summary>
    public static readonly PositionLocation LowAftBulkLocation = new(new HashSet<PositionLocationTag> { PositionLocationTag.Low, PositionLocationTag.LowAft, PositionLocationTag.Bulk });
    /// <summary>后下非散货位置 / Low aft non-bulk position</summary>
    public static readonly PositionLocation LowAftNotBulkLocation = new(new HashSet<PositionLocationTag> { PositionLocationTag.Low, PositionLocationTag.LowAft });

    /// <summary>是否包含指定标签 / Whether contains specified tag</summary>
    public bool Contains(PositionLocationTag tag) => Tags.Contains(tag);
}

/// <summary>
/// 位置坐标。Position coordinate with arm measurements and offsets.
/// </summary>
public sealed class PositionCoordinate
{
    /// <summary>前力臂 / Front arm (inch)</summary>
    public double FrontArm { get; }
    /// <summary>后力臂 / Back arm (inch)</summary>
    public double BackArm { get; }
    /// <summary>左力臂 / Left arm (inch)</summary>
    public double LeftArm { get; }
    /// <summary>右力臂 / Right arm (inch)</summary>
    public double RightArm { get; }
    /// <summary>ULD 偏移 / ULD offsets</summary>
    public IReadOnlyDictionary<ULDCode, double> Offsets { get; }
    /// <summary>纵向力臂 / Longitudinal arm (average of front and back)</summary>
    public double LongitudinalArm => (FrontArm + BackArm) / 2.0;
    /// <summary>横向力臂 / Lateral arm (average of left and right)</summary>
    public double LateralArm => (LeftArm + RightArm) / 2.0;
    /// <summary>是否跨越中心 / Whether transverse (crosses centerline)</summary>
    public bool Transverse => LeftArm <= 0.0 && 0.0 <= RightArm;
    /// <summary>是否在左侧 / Whether on left side</summary>
    public bool OnLeft => !Transverse && LateralArm <= 0.0;
    /// <summary>是否在右侧 / Whether on right side</summary>
    public bool OnRight => !Transverse && LateralArm > 0.0;

    /// <summary>
    /// 是否完整落在给定的力臂区间内 / Whether this coordinate lies entirely within the given arm range.
    /// </summary>
    /// <param name="frontArm">区间一端 / One end of the range.</param>
    /// <param name="backArm">区间另一端 / The other end.</param>
    /// <returns>是否落在区间内 / Whether it lies within.</returns>
    /// <remarks>
    /// 两端**不保证先后**：航空力臂里前后是站位数，前端不一定数值更小，所以先归一化再比。
    ///
    /// The two ends are **not ordered**: in aviation the arm is a station number, so "front" is not necessarily the smaller one. Both are normalised first, so a reversed pair still compares correctly.
    ///
    /// </remarks>
    public bool Between(double frontArm, double backArm) {
        double lower = System.Math.Min(frontArm, backArm);
        double upper = System.Math.Max(frontArm, backArm);
        return lower <= FrontArm && BackArm <= upper;
    }

    /// <summary>
    /// 给定力臂是否落在本坐标的纵向范围内 / Whether the given arm lies within this coordinate's longitudinal span.
    /// </summary>
    /// <param name="arm">要检查的力臂 / The arm to check.</param>
    /// <returns>是否落在范围内 / Whether it lies within.</returns>
    /// <remarks>
    /// **不做归一化**：直接按"前臂 ≤ 力臂 ≤ 后臂"判断。这与 <see cref="Between"/>/<see cref="WithIntersectionWith"/> 有意不同——
    /// 那两者要先归一化，因为它们比较的是**两个区间**，而这里比的是一个点与本坐标，前提是本坐标的前后臂本身就是有序的。
    /// 如果哪天有坐标的前臂大于后臂，用它切出的限制线会全部落空——那正是"切不出线"该有的表现，而不是悄悄换一套区间。
    ///
    /// **Not normalised**: it compares "front ≤ arm ≤ back" directly. That is deliberately different from <see cref="Between"/> and
    /// <see cref="WithIntersectionWith"/>, which normalise because they compare **two ranges**; here a point is compared to this coordinate, on the premise that this coordinate's
    /// own front and back arms are already ordered. A coordinate with a reversed pair would produce no limit lines at all — which is the right behaviour for "cannot be cut into
    /// lines", rather than quietly swapping in a different range.
    /// </remarks>
    public bool On(double arm) => FrontArm <= arm && arm <= BackArm;

    /// <summary>
    /// 是否与给定的力臂区间**有交集** / Whether this coordinate intersects the given arm range.
    /// </summary>
    /// <param name="frontArm">区间一端 / One end of the range.</param>
    /// <param name="backArm">区间另一端 / The other end.</param>
    /// <returns>是否有交集 / Whether they intersect.</returns>
    /// <remarks>
    /// 与 <see cref="Between"/> 是**两件事**：`Between` 要求位置整个落进区间，`WithIntersectionWith` 只要碰到就算。
    /// 用 `Between` 代替它会让压在区域边界上的舱位被漏掉——那些恰好是最该检查的一批。
    ///
    /// This is a **different question** from <see cref="Between"/>: `Between` requires the whole position inside the range, `WithIntersectionWith` is satisfied by touching.
    /// Substituting `Between` would drop every position straddling a zone boundary — exactly the ones that most need checking.
    /// </remarks>
    public bool WithIntersectionWith(double frontArm, double backArm) {
        double lower = System.Math.Min(frontArm, backArm);
        double upper = System.Math.Max(frontArm, backArm);
        return FrontArm <= upper && BackArm >= lower;
    }

    public PositionCoordinate(double frontArm, double backArm, double leftArm, double rightArm, IReadOnlyDictionary<ULDCode, double>? offsets = null)
    {
        FrontArm = frontArm;
        BackArm = backArm;
        LeftArm = leftArm;
        RightArm = rightArm;
        Offsets = offsets ?? new Dictionary<ULDCode, double>();
    }
}

/// <summary>
/// 位置形状。Position shape with dimensions.
/// </summary>
/// <param name="Length">长度 / Length (inch)</param>
/// <param name="Width">宽度 / Width (inch)</param>
/// <param name="Volume">体积 / Volume (cubic inch)</param>
/// <param name="DeclaredArea">申报面积；给出时优先于长×宽 / A declared area, preferred over length × width when given.</param>
/// <remarks>
/// <see cref="Area"/> 优先取申报值：货站的实际可用面积可能与"长×宽"差很多（切角、导轨、ULD 锁扣占位），而表面密度是**按面积算重量**的，
/// 面积错一点重量就错一片。取值就是 `declaredArea ?: (length * width)`。
///
/// <see cref="Area"/> prefers the declared value: a real position's usable area can differ a lot from length × width (cut corners, rails, ULD lock space), and surface density is
/// **weight per area**, so a wrong area skews everything. The value is `declaredArea ?: (length * width)`.
/// </remarks>
public sealed record PositionShape(double Length, double Width, double Volume, double? DeclaredArea = null)
{
    /// <summary>位置面积 / The position area (square inch)</summary>
    public double Area => DeclaredArea ?? Length * Width;
}

/// <summary>
/// 装载位置。Cargo position with coordinates, shape, and location.
/// </summary>
public sealed class Position
{
    /// <summary>位置 ID / Position ID</summary>
    public ulong Id { get; }
    /// <summary>空间名称 / Space name</summary>
    public string SpaceName { get; }
    /// <summary>尺寸代码 / Size code</summary>
    public string SizeCode { get; }
    /// <summary>线性装载顺序 / Linear loading order</summary>
    public byte LinearLoadingOrder { get; }
    /// <summary>坐标 / Coordinate</summary>
    public PositionCoordinate Coordinate { get; }
    /// <summary>形状 / Shape</summary>
    public PositionShape Shape { get; }
    /// <summary>位置分类 / Location classification</summary>
    public PositionLocation Location { get; }
    /// <summary>字母数字空间名称 / Alpha-numeric space name</summary>
    public string AlphaSpaceName { get; }
    /// <summary>启用的 ULD 集合 / Enabled ULD set</summary>
    public IReadOnlySet<ULDCode> EnabledULDs { get; }

    /// <summary>装载顺序对象 / The loading-order object</summary>
    /// <remarks>
    /// `Position._loadingOrder` 是**可后置赋值**的：初始化器先造出全部舱位，再按装载顺序串起前后驱。
    /// C# 侧原先没有这个属性，于是 `NeighbourCalculator` 的拓扑分支无法落地。这里补成可写属性，默认 `null`。
    ///
 /// In the implementation `Position._loadingOrder` is **assigned after construction**: the initializer builds every position, then links predecessors and successors in loading order.
    /// The C# side had no such property, which is why the topological neighbour branch could not be implemented. It is writable here and defaults to `null`.
    /// </remarks>
    public LoadingOrder? LoadingOrder { get; set; }

    public Position(
        ulong id,
        string spaceName,
        string sizeCode,
        byte linearLoadingOrder,
        PositionCoordinate coordinate,
        PositionShape shape,
        PositionLocation location)
    {
        Id = id;
        SpaceName = spaceName;
        SizeCode = sizeCode;
        LinearLoadingOrder = linearLoadingOrder;
        Coordinate = coordinate;
        Shape = shape;
        Location = location;
        AlphaSpaceName = new string(spaceName.Where(char.IsLetterOrDigit).ToArray());
        EnabledULDs = new HashSet<ULDCode>(coordinate.Offsets.Keys);
    }

    /// <inheritdoc />
    public override string ToString() => SpaceName;

    /// <summary>按 <see cref="Id"/> 判断相等 / Equality is by <see cref="Id"/>.</summary>
    /// <remarks>
    /// **本类曾是 `sealed class` 且未重写相等**，所以
    /// `LoadingOrder.DirectPrec`、`Appointment` 等**以 `Position` 为键的集合全都按引用比较**——
    /// 两个 `Id` 相同的位置对象会被当成不同的键，集合查找、去重、邻接关系全部悄悄出错。
    ///
    /// 值相等比较的是**全部构造参数**，其中 `coordinate` 是普通 `class`（引用相等）、
    /// `shape` 与 `location` 是值对象（值相等）。但 `Id` 被标注为"唯一标识"，
    /// **同 `Id` 必然同其余字段**，所以按 `Id` 比较与可观察行为一致，且更简单。
    ///
    /// **`Position` used to be a `sealed class` without `Equals`/`GetHashCode`**, so every collection keyed by
    /// `Position` (`LoadingOrder.DirectPrec`, `Appointment`, …) compared by reference — two position objects with the same `Id` became distinct keys, and
    /// lookups, de-duplication and adjacency all silently broke.
    ///
 /// The implementation `data class` compares **all constructor parameters**, of which `coordinate` is a plain `class` (reference equality) while `shape` and
 /// `location` are `data class`es (value equality). But `Id` is documented as the unique identifier in the implementation, and **the same `Id` implies the same
    /// everything else**, so comparing by `Id` matches its observable behaviour and is simpler.
    /// </remarks>
    public override bool Equals(object? obj) => obj is Position other && Id == other.Id;

    /// <inheritdoc />
    public override int GetHashCode() => Id.GetHashCode();
}
