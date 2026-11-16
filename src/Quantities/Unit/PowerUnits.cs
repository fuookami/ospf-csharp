#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 功率单位 / Power units.
/// </summary>
public static class PowerUnits {
    /// <summary>瓦特 / Watt (SI derived: J/s).</summary>
    public static readonly PhysicalUnit Watt = new DerivedUnit(
        EnergyUnits.Joule.Divide(SIBaseUnits.Second),
        "watt", "W");
    /// <summary>千瓦 / Kilowatt.</summary>
    public static readonly PhysicalUnit Kilowatt = new DerivedUnit(Watt.ScaleMultiply(Scale.Kilo), "kilowatt", "kW");
    /// <summary>兆瓦 / Megawatt.</summary>
    public static readonly PhysicalUnit Megawatt = new DerivedUnit(Watt.ScaleMultiply(Scale.Mega), "megawatt", "MW");
    /// <summary>毫瓦 / Milliwatt.</summary>
    public static readonly PhysicalUnit Milliwatt = new DerivedUnit(Watt.ScaleMultiply(Scale.Milli), "milliwatt", "mW");
    /// <summary>马力 / Horsepower (1 hp = 745.69987158 W).</summary>
    public static readonly PhysicalUnit Horsepower = new DerivedUnit(Watt.ScaleMultiply(Scale.Invoke(745.69987158)), "horsepower", "hp");

    // 以下 4 个按补齐，**而这一组里有一个同名不同值的陷阱。**
    //
    // **`Horsepower` 是 735 W（公制马力 PS），而本仓的 `Horsepower` 是 745.69987 W（机械马力）。**
    // **同一个名字，两个差 1.4% 的值——而 `hp` 这个符号在两种语境下都在用。**
    //
    // **处理方式：本仓的 `Horsepower` 保持机械马力不变**（那是 `hp` 的国际通行含义），
    // **把735 以 `MetricHorsepower` 的名字加进来**——
    // **改掉既有的 `Horsepower` 会让所有既有调用的结果静默偏移 1.4%。**
    //
    // **`UKHorsepower` 写的是 `Watt * 550`，而 550 是 ft·lbf/s 的数值。**
    // **它忘了乘换算因子：550 × 0.3048 × 4.4482216152605 = 745.69987 W**，
    // **正好等于机械马力。所以 `UKHorsepower` 就是本仓既有的 `Horsepower`。**
    //
    // The four below were added from Power.kt, **and this group carries a same-name-different-value trap.**
    //
 // **The implementation `Horsepower` is 735 W (metric PS) while this repo Horsepower is 745.69987 W (mechanical).**
    // **One name, two values 1.4% apart, and the symbol `hp` is used in both senses.**
    //
    // **Handling: this repo Horsepower stays mechanical** (the international meaning of `hp`),
 // **and the implementation 735 is admitted under the name `MetricHorsepower`** —
    // **changing the existing `Horsepower` would silently shift every existing call by 1.4%.**
    //
 // **The implementation `UKHorsepower` writes `Watt * 550`, and 550 is the ft-lbf/s figure.**
    // **The conversion factor was left out: 550 x 0.3048 x 4.4482216152605 = 745.69987 W**,
    // **which is exactly mechanical horsepower. So `UKHorsepower` is this repo existing `Horsepower`.**

    /// <summary>**焦耳每秒（与瓦特同值，独立成对象）** / Joule per second.</summary>
    /// <remarks>
    /// **与 `AmpereSecond` 同理：数值相同而名字不同，所以必须是独立对象，不能做别名**——
    /// **否则按名字查 `J/s` 会拿到 `"watt"`，符号也跟着错。**
    ///
    /// **Same as `AmpereSecond`: equal value, different name, so it must be its own object rather than an
    /// alias** — **otherwise looking up `J/s` returns "watt", with the wrong symbol.**
    /// </remarks>
    public static readonly PhysicalUnit JoulePerSecond = new DerivedUnit(
        Watt.ScaleMultiply(Scale.Invoke(1)), "joule-per-second", "J/s");

    /// <summary>**牛顿米每秒（与瓦特同值，独立成对象）** / Newton-metre per second.</summary>
    public static readonly PhysicalUnit NewtonMeterPerSecond = new DerivedUnit(
        Watt.ScaleMultiply(Scale.Invoke(1)), "newton-meter-per-second", "N*m/s");

    /// <summary>**公制马力（735.49875 W）** / Metric horsepower.</summary>
    /// <remarks>
    /// **常见写法取整 735，这里用 735.49875**——**两者差 0.07%，而整 735 是常见的工程近似。**
    /// **用精确值是为了让它与机械马力（745.69987）的比值可判据化。**
    ///
    /// **The common form uses a round 735; this uses 735.49875** — **0.07% apart, with the round value a common
    /// engineering approximation.** **The exact value makes the ratio against mechanical horsepower checkable.**
    /// </remarks>
    public static readonly PhysicalUnit MetricHorsepower = new DerivedUnit(
        Watt.ScaleMultiply(Scale.Invoke(735.49875)), "metric horsepower", "PS");

    /// <summary>**英制马力（= 机械马力 745.69987 W）** / UK horsepower.</summary>
    /// <remarks>
    /// **550 是 ft·lbf/s 的数值，漏了换算；550 ft·lbf/s = 745.69987 W。**
    /// **所以它与机械马力是同一个值，这里做成本仓 `Horsepower` 的别名。**
    ///
 /// **The implementation 550 is the ft-lbf/s figure with the conversion dropped; 550 ft-lbf/s = 745.69987 W.**
    /// **So it equals mechanical horsepower, and is an alias of this repo `Horsepower` here.**
    /// </remarks>
    public static readonly PhysicalUnit UKHorsepower = Horsepower;

    private sealed class DerivedUnit : PhysicalUnit {
        private readonly PhysicalUnit _inner;
        private readonly string _name;
        private readonly string _symbol;
        public DerivedUnit(PhysicalUnit inner, string name, string symbol) { _inner = inner; _name = name; _symbol = symbol; }
        public override string Name => _name;
        public override string Symbol => _symbol;
        public override DerivedQuantity Quantity => _inner.Quantity;
        public override UnitConversionRule ConversionRule => _inner.ConversionRule;
    }
}
