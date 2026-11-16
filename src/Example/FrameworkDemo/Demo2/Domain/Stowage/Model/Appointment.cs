#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 货物与舱位之间的预约映射，表示优化过程中必须遵守的预分配装载关系。
/// Appointment mapping between items and positions, representing pre-assigned stowage relationships that must be respected during optimisation.
/// </summary>
/// <remarks>
/// 移植。
///
/// **一处基线没有的语义差异**：`Position` 是 `data class`（值相等），
/// C# 的 `Position` 是 `sealed class` 且**没有重写 `Equals`/`GetHashCode`**（引用相等）。
/// 因此这里的字典以**对象标识**为键：两个字段完全相同但分别构造的 `Position`
/// 在本实现里是两个不同的键，在是同一个。依赖值相等的调用方必须复用同一个 `Position` 实例。
///
/// Ports its.
///
/// **One semantic difference the code does not have**: its `Position` is a `data class` (value equality), while C#'s `Position` is a
/// `sealed class` that **does not override `Equals`/`GetHashCode`** (reference equality). So these maps key on **object identity**: two separately
/// constructed `Position`s with identical fields are two different keys here and one key in the code. A caller relying on value equality must reuse
/// the same `Position` instance.
/// </remarks>
public sealed class Appointment
{
    private readonly IReadOnlyDictionary<Item, Position> _appointment;
    private IReadOnlyDictionary<Position, IReadOnlyList<Item>>? _reverse;

    /// <summary>
    /// 创建预约映射 / Create the appointment mapping.
    /// </summary>
    /// <param name="appointment">货物到其预约舱位的映射 / The mapping from items to their appointed positions.</param>
    public Appointment(IReadOnlyDictionary<Item, Position> appointment)
    {
        _appointment = appointment;
    }

    /// <summary>货物到其预约舱位的映射 / The mapping from items to their appointed positions.</summary>
    public IReadOnlyDictionary<Item, Position> Mapping => _appointment;

    /// <summary>从舱位到其预约货物的反向映射（惰性构建）/ Reverse mapping from positions to their appointed items (built lazily).</summary>
    public IReadOnlyDictionary<Position, IReadOnlyList<Item>> Reverse
    {
        get
        {
            if (_reverse is not null) {
                return _reverse;
            }
            var grouped = new Dictionary<Position, List<Item>>();
            foreach (KeyValuePair<Item, Position> entry in _appointment) {
                if (!grouped.TryGetValue(entry.Value, out List<Item>? items)) {
                    items = new List<Item>();
                    grouped[entry.Value] = items;
                }
                items.Add(entry.Key);
            }
            var reverse = new Dictionary<Position, IReadOnlyList<Item>>();
            foreach (KeyValuePair<Position, List<Item>> pair in grouped) {
                reverse[pair.Key] = pair.Value;
            }
            _reverse = reverse;
            return _reverse;
        }
    }

    /// <summary>
    /// 查询货物的预约舱位 / Look up the appointed position of an item.
    /// </summary>
    /// <param name="item">货物 / The item.</param>
    /// <returns>预约舱位，未预约时为 null / The appointed position, or null when unappointed.</returns>
    public Position? PositionOf(Item item) =>
        _appointment.TryGetValue(item, out Position? position) ? position : null;

    /// <summary>
    /// 查询舱位上的预约货物 / Look up the items appointed to a position.
    /// </summary>
    /// <param name="position">舱位 / The position.</param>
    /// <returns>预约货物列表，无预约时为空列表 / The appointed items, empty when none.</returns>
    public IReadOnlyList<Item> ItemsOf(Position position) =>
        Reverse.TryGetValue(position, out IReadOnlyList<Item>? items) ? items : System.Array.Empty<Item>();
}
