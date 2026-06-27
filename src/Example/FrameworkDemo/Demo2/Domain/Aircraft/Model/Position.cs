#nullable enable

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
public sealed record PositionShape(double Length, double Width, double Volume);

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
}
