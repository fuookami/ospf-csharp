#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

/// <summary>
/// 舱门邻接类型。Door ubiety type describing spatial relationship.
/// </summary>
public enum DoorUbietyType
{
    Front,
    AdjacentFront,
    Beside,
    Opposite,
    AdjacentBehind,
    Behind
}

/// <summary>
/// 舱门邻接关系。Door ubiety relationship between position and hatch door.
/// </summary>
/// <param name="Type">邻接类型 / Ubiety type</param>
/// <param name="SameSide">是否同侧 / Whether on same side</param>
/// <param name="Position">装载位置 / Cargo position</param>
/// <param name="Door">舱门 / Hatch door</param>
public sealed record DoorUbiety(
    DoorUbietyType Type,
    bool SameSide,
    Position Position,
    HatchDoor Door);

/// <summary>
/// 舱门。Hatch door with spatial coordinates.
/// </summary>
/// <param name="Location">甲板位置 / Deck location</param>
/// <param name="BesideBulk">是否靠近散货区 / Whether beside bulk area</param>
/// <param name="NoseDoor">是否机头门 / Whether nose door</param>
/// <param name="LateralArm">横向力臂 / Lateral arm (inch)</param>
/// <param name="FrontArm">前力臂 / Front arm (inch)</param>
/// <param name="BackArm">后力臂 / Back arm (inch)</param>
public sealed record HatchDoor(
    DeckLocation Location,
    bool BesideBulk,
    bool NoseDoor,
    double LateralArm,
    double FrontArm,
    double BackArm);
