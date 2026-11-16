#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;

/// <summary>
/// 枚举飞机类别（客机或货机）。
/// Enumerates the aircraft categories (passenger or cargo).
/// </summary>
public enum AircraftCategory {
    /// <summary>客机 / Passenger.</summary>
    Passenger,
    /// <summary>货机 / Cargo.</summary>
    Cargo
}

/// <summary>
/// 表示飞机容量的密封类（专门用于客运或货运）。
/// Sealed class representing aircraft capacity, specialized for passenger or cargo.
/// </summary>
public abstract class AircraftCapacity {
    /// <summary>容量类别 / The capacity category.</summary>
    public abstract AircraftCategory Category { get; }

    /// <summary>
    /// 将每个舱位映射到座位数的乘客容量。
    /// Passenger capacity mapping each class to a seat count.
    /// </summary>
    /// <param name="cabinCapacity">舱位容量映射 / Class to seat count mapping.</param>
    public sealed class PassengerCapacity(IReadOnlyDictionary<Infrastructure.PassengerClassId, ulong> cabinCapacity) : AircraftCapacity {
        /// <summary>
        /// 各舱位的座位数 / The seat count per cabin.
        /// </summary>
        /// <remarks>
        /// **构造参数不是成员**：主构造函数的 `Capacity` 只在类内可见，外面读不到这张表——于是"这架飞机有几等舱、各多少座"
        /// 在类型外不可见。补一个只读属性把它露出来。
        ///
        /// **A primary-constructor parameter is not a member**: `Capacity` is visible only inside the class, so the cabin table — how many cabins this aircraft has and how many seats
        /// each holds — was unreachable from outside. A read-only property exposes it.
        /// </remarks>
        public IReadOnlyDictionary<Infrastructure.PassengerClassId, ulong> Capacity { get; } = cabinCapacity;

        /// <summary>总座位数 / Total seat count.</summary>
        public ulong Total { get; } = ComputeTotal(cabinCapacity);

        /// <inheritdoc/>
        public override AircraftCategory Category => AircraftCategory.Passenger;

        /// <summary>
        /// 获取给定舱位的座位数。
        /// Gets the seat count for the given class.
        /// </summary>
        /// <param name="cls">舱位 / The passenger class.</param>
        /// <returns>座位数 / The seat count.</returns>
        public ulong GetCapacity(Infrastructure.PassengerClassId cls)
            => Capacity.TryGetValue(cls, out ulong count) ? count : 0UL;

        /// <summary>
        /// 检查飞机是否能承载给定舱位的有效载荷。
        /// Checks whether the aircraft can carry the given payload per class.
        /// </summary>
        /// <param name="payload">有效载荷 / The payload.</param>
        /// <returns>是否可承载 / Whether can carry.</returns>
        public bool Enabled(IReadOnlyDictionary<Infrastructure.PassengerClassId, ulong> payload) {
            foreach (var kv in payload) {
                if (GetCapacity(kv.Key) < kv.Value) return false;
            }
            return true;
        }

        private static ulong ComputeTotal(IReadOnlyDictionary<Infrastructure.PassengerClassId, ulong> capacity) {
            ulong sum = 0;
            foreach (var v in capacity.Values) sum += v;
            return sum;
        }
    }

    /// <summary>
    /// 作为重量/体积值的货物容量。
    /// Cargo capacity as a weight/volume value.
    /// </summary>
    /// <param name="value">容量值 / The capacity value.</param>
    public sealed class CargoCapacity(Flt64 value) : AircraftCapacity {
        /// <summary>
        /// 货量 / The cargo capacity value.
        /// </summary>
        /// <remarks>
        /// 同上：构造参数不是成员，**货机装了多重的东西在外面读不到**。这是 `capacity` 属性。
        ///
 /// Same as above: the constructor parameter is not a member, so **how much a freighter holds was unreadable from outside**. The implementation exposes it as `capacity`.
        /// </remarks>
        public Flt64 ContainedCapacity { get; } = value;

        /// <inheritdoc/>
        public override AircraftCategory Category => AircraftCategory.Cargo;

        /// <summary>
        /// 检查飞机是否能承载给定重量的有效载荷。
        /// Checks whether the aircraft can carry the given payload weight.
        /// </summary>
        /// <param name="payload">有效载荷 / The payload.</param>
        /// <returns>是否可承载 / Whether can carry.</returns>
        public bool Enabled(Flt64 payload) => ContainedCapacity >= payload;
    }
}
