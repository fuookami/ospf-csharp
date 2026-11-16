// 力矩模型的判据 / Criteria for the torque model.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **力矩模型** / The torque model.
/// </summary>
/// <remarks>
/// 四族符号：纵向力矩（按阶段）、横向力矩、CLIM（仅宽体）、指数（按阶段）。四处容易做错：
///
/// | 处 | 做错了会怎样 |
/// | --- | --- |
/// | 燃油只进起飞/着陆 | 零油重阶段凭空多出燃油指数 |
/// | 干操作重量以"重力 × 力臂"进力矩 | 先乘力臂会把"重量×力臂"当重量求重力，差一个 g，数值差近十倍但看起来合理 |
/// | CLIM 只注册宽体机 | 窄体机注册 CLIM 会让约束生成器以为有 CLIM 限制可加 |
/// | 救生筏力矩用 `Arm` 反推力臂 | 直接把指数当力臂用，量纲不同 |
///
/// Four families with the four mistakes tabulated above. The dry-operating one is the nastiest: gravity-first versus arm-first differ by a factor of about ten and both look
/// like plausible torque values, so nothing reports it.
/// </remarks>
public class Demo2TorqueTests {
    private static Quantity<Flt64> Grams(double value) => new(new Flt64(value), MassUnits.Gram);

    private static Item MakeItem(string name) =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: CargoType.FromCode(CargoCode.BAL),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck);

    private static StowagePosition MakePosition(string name, ulong id, PositionLocation location) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(500.0, 900.0, 40.0, 60.0),
            shape: new PositionShape(50.0, 40.0, 50.0 * 40.0 * 60.0),
            location: location);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(true, false, true, true),
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: 3UL,
            plw: new PositionPredicateLoadWeight(350.0, 300.0, 400.0));
    }

    private static DiscreteLoadingContext<Flt64> MakeKernel(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions) {
        var loadingItems = items.Select(item => new LoadingItem<Flt64>(item.Name, Grams(item.Weight))).ToList();
        var loadingPositions = positions
            .Select(position => new LoadingPosition<Flt64>(position.Name, position.Mla, Grams(position.Mlw.Mlw)))
            .ToList();
        var allowed = new HashSet<AssignmentKey>();
        for (int i = 0; i < items.Count; ++i) {
            for (int j = 0; j < positions.Count; ++j) {
                if (Stowage.StowageNeeded(items[i], positions[j])) {
                    allowed.Add(new AssignmentKey(items[i].Name, positions[j].Name));
                }
            }
        }
        var problem = new DiscreteLoadingProblem<Flt64>(
            Items: loadingItems,
            Positions: loadingPositions,
            AllowedAssignments: allowed,
            Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
            LayoutGroups: new List<LayoutSelectionGroup>(),
            WeightUnit: MassUnits.Gram);
        return new DiscreteLoadingContext<Flt64>(problem, new Flt64PackingValueAdapter());
    }

    private static readonly Fuselage Body = new(new Liferaft(50.0, 12.0), Dow: 40000.0, Doi: 10.0, BalancedArm: 900.0);

    private static readonly Dictionary<FlightPhase, FuelConstant> Fuel = new() {
        [FlightPhase.TakeOff] = new FuelConstant(0.8, 8000.0, 5.0),
        [FlightPhase.Landing] = new FuelConstant(0.8, 2000.0, 2.0),
    };

    private static (Torque Torque, LinearMetaModel<Flt64> Model, Load Load) Prepare(
        bool wideBody = false,
        bool registerLoad = true,
        IReadOnlyDictionary<FlightPhase, FuelConstant>? fuel = null) {
        var items = new List<Item> { MakeItem("I1") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, PositionLocation.NormalMainLocation),
            MakePosition("P2", 2UL, PositionLocation.LowForwardNotBulkLocation),
        };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        if (registerLoad) {
            load.Register(model).IsOk.Should().BeTrue();
        }
        var torque = new Torque(Body, fuel ?? Fuel, formula, positions, load, wideBody);
        return (torque, model, load);
    }

    /// <summary>
    /// **燃油只进起飞/着陆** / Fuel enters take-off and landing only.
    /// </summary>
    /// <remarks>
    /// 三点成对：零油重常数 = 干操作力矩 + 救生筏力矩（**不含燃油**）；起飞再加 8000 kg 的指数 5；着陆加 2。同一口径与 `TotalWeight` 一致。
    ///
    /// Three paired points: the zero-fuel constant is the dry-operating torque plus the liferaft's (**no fuel**); take-off adds the index 5 of its 8000 kg; landing adds 2.
    /// The same convention as `TotalWeight`.
    /// </remarks>
    [Fact]
    public void Register_ShouldAddFuelOnlyForTakeOffAndLanding() {
        (Torque torque, LinearMetaModel<Flt64> model, Load _) = Prepare();
        torque.Register(model).IsOk.Should().BeTrue();

        double dry = torque.DryOperatingTorque();
        double liferaftArm = new Formula(new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767")), 100.0, 20.0, 120.0, 5000.0, 0.0)
            .Arm(12.0, 50.0);
        double liferaft = 50.0 * Torque.TorquePerWeight(liferaftArm);

        double zeroFuel = torque.LongitudinalTorque(FlightPhase.ZeroFuel)!.Polynomial.Constant.ToDouble();
        double takeOff = torque.LongitudinalTorque(FlightPhase.TakeOff)!.Polynomial.Constant.ToDouble();
        double landing = torque.LongitudinalTorque(FlightPhase.Landing)!.Polynomial.Constant.ToDouble();

        zeroFuel.Should().BeApproximately(dry + liferaft, 1e-6, "zero fuel carries no fuel");
        takeOff.Should().BeApproximately(zeroFuel + 5.0, 1e-6);
        landing.Should().BeApproximately(zeroFuel + 2.0, 1e-6);
        landing.Should().NotBeApproximately(takeOff, 1e-6, "the two phases carry different fuel indices");
    }

    /// <summary>
    /// **零油重阶段即使燃油表里有条目也不加** / The zero-fuel phase ignores a fuel entry even when the map has one.
    /// </summary>
    /// <remarks>
    /// 上一条判据里燃油表**没有** `ZeroFuel` 键，所以"燃油只进起飞/着陆"那道守卫根本没被触及——把守卫改成"总是加"仍然绿。
    /// 这一条故意在燃油表里放一个零油重条目（真实的调用方错误），钉住"零油重不吃燃油"是**代码保证**而不是"表里恰好没有"。
    ///
    /// 与"起飞/着陆仍各加自己的燃油"成对：守卫不能宽到把起飞也漏掉。
    ///
    /// The criterion above has **no** `ZeroFuel` key in the fuel map, so the guard never fires and changing it to "always" stays green. This one deliberately puts a zero-fuel
    /// entry in the map (a real caller error) to pin "zero fuel takes no fuel" as a **code guarantee**, not as "the map happens to lack the key". Paired with "take-off and landing
    /// still add theirs", so the guard cannot be widened until it swallows those too.
    /// </remarks>
    [Fact]
    public void Register_ShouldIgnoreAZeroFuelEntryInTheFuelMap() {
        var fuelWithZeroFuel = new Dictionary<FlightPhase, FuelConstant> {
            [FlightPhase.TakeOff] = new FuelConstant(0.8, 8000.0, 5.0),
            [FlightPhase.Landing] = new FuelConstant(0.8, 2000.0, 2.0),
            [FlightPhase.ZeroFuel] = new FuelConstant(0.8, 1000.0, 99.0),
        };
        (Torque torque, LinearMetaModel<Flt64> model, Load _) = Prepare(fuel: fuelWithZeroFuel);
        torque.Register(model).IsOk.Should().BeTrue();

        double liferaftArm = new Formula(new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767")), 100.0, 20.0, 120.0, 5000.0, 0.0)
            .Arm(12.0, 50.0);
        double liferaft = 50.0 * Torque.TorquePerWeight(liferaftArm);

        torque.LongitudinalTorque(FlightPhase.ZeroFuel)!.Polynomial.Constant.ToDouble()
            .Should().BeApproximately(torque.DryOperatingTorque() + liferaft, 1e-6,
                "the zero-fuel phase takes no fuel even when the map offers one");
        torque.Index(FlightPhase.ZeroFuel)!.Polynomial.Constant.ToDouble()
            .Should().BeApproximately(10.0 + 12.0, 1e-9, "the 99 index must not leak in");
        torque.LongitudinalTorque(FlightPhase.TakeOff)!.Polynomial.Constant.ToDouble()
            .Should().BeApproximately(torque.DryOperatingTorque() + liferaft + 5.0, 1e-6,
                "take-off still adds its own fuel");
    }

    /// <summary>
    /// **干操作力矩用干操作重量与平衡力臂** / The dry-operating torque uses the dry operating weight and the balanced arm.
    /// </summary>
    /// <remarks>
    /// 干操作力矩必须是 `40000 × TorquePerWeight(900)`，即用 `Dow`（40000，不是总重）与 `BalancedArm`（900，不是 `StandardDatum` 120）。
    /// 这两处取错都会得到一个合理量级的力矩，只有把"用了哪个重量、哪个力臂"分别钉住才看得出。
    ///
    /// **记录一处不可观测**：这里写的是"先取重力再乘力臂"。在千克力单位制下 `Gravity(1 kg) = 1 kgf`，`TorquePerWeight(arm)` 恰好等于
    /// `arm` 本身（实测 900 → 900），所以"先取重力"与"先乘力臂"在这套单位下**数值完全相同**，没有判据能区分。换成牛顿就会差 9.8 倍。
    /// 代码保持顺序（先重力后力臂），但这个性质只靠注释记着，不伪装成已被判据覆盖。
    ///
    /// The dry-operating torque must be `40000 × TorquePerWeight(900)` — `Dow` (40000, not the total weight) times `BalancedArm` (900, not `StandardDatum` 120). Getting either
    /// wrong still yields a plausible torque, so only pinning "which weight, which arm" shows it. **One unobservable recorded**: the code takes gravity first and then the arm.
    /// In kilogram-force, `Gravity(1 kg) = 1 kgf` and `TorquePerWeight(arm)` is exactly `arm` (measured: 900 → 900), so "gravity first" and "arm first" are **numerically
    /// identical** here and no criterion can separate them. In newtons they would differ by 9.8×. The code keeps its order, but that property lives in this comment
    /// rather than posing as test coverage.
    /// </remarks>
    [Fact]
    public void DryOperatingTorque_ShouldUseTheDryOperatingWeightAndBalancedArm() {
        (Torque torque, LinearMetaModel<Flt64> _, Load _) = Prepare();
        double perWeight = Torque.TorquePerWeight(900.0);

        torque.DryOperatingTorque().Should().BeApproximately(40000.0 * perWeight, 1e-6);
        torque.DryOperatingTorque().Should().NotBeApproximately(40000.0 * Torque.TorquePerWeight(120.0), 1e-6,
            "the balanced arm is 900, not the standard datum 120");
        torque.DryOperatingTorque().Should().NotBeApproximately(40100.0 * perWeight, 1e-6,
            "the dry operating weight is 40000, not the total with the liferaft");
    }

    /// <summary>
    /// **救生筏力矩从指数反推力臂** / The liferaft's torque recovers the arm from its index.
    /// </summary>
    /// <remarks>
    /// 救生筏重量 50、指数 12。正确值用 `Arm(12, 50)` 反推理臂；错误写法是把 12 直接当力臂（量纲不同）。两者数值差几百倍，而都能算出一个数。
    ///
    /// Liferaft weight 50, index 12. The correct value recovers the arm with `Arm(12, 50)`; the wrong one treats 12 as an arm directly (different dimensions). They differ by
    /// hundreds of times and both produce a number.
    /// </remarks>
    [Fact]
    public void Register_ShouldRecoverTheLiferaftArmFromItsIndex() {
        (Torque torque, LinearMetaModel<Flt64> model, Load _) = Prepare();
        torque.Register(model).IsOk.Should().BeTrue();

        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        double arm = formula.Arm(12.0, 50.0);
        double expectedLiferaft = 50.0 * Torque.TorquePerWeight(arm);
        double wrongLiferaft = 50.0 * Torque.TorquePerWeight(12.0);

        double zeroFuel = torque.LongitudinalTorque(FlightPhase.ZeroFuel)!.Polynomial.Constant.ToDouble();
        (zeroFuel - torque.DryOperatingTorque()).Should().BeApproximately(expectedLiferaft, 1e-6);
        (zeroFuel - torque.DryOperatingTorque()).Should().NotBeApproximately(wrongLiferaft, 1e-6,
            "the index is not an arm");
    }

    /// <summary>
    /// **CLIM 只在宽体机上注册** / CLIM registers on wide-body aircraft only.
    /// </summary>
    /// <remarks>
    /// 成对：窄体机 `ClimSymbol` 为 `null`、模型里没有 `clim`；宽体机两者都有。窄体机注册一个 CLIM 符号会让约束生成器以为有 CLIM 限制可加。
    ///
    /// Paired: a narrow-body has `ClimSymbol` `null` and no `clim` in the model; a wide-body has both. A CLIM symbol on a narrow-body tells the constraint generator there is a
    /// CLIM limit to add.
    /// </remarks>
    [Fact]
    public void Register_ShouldSkipClimOnNarrowBodyAircraft() {
        (Torque narrow, LinearMetaModel<Flt64> narrowModel, Load _) = Prepare(wideBody: false);
        narrow.Register(narrowModel).IsOk.Should().BeTrue();
        narrow.ClimSymbol.Should().BeNull("the narrow-body has no CLIM");
        narrowModel.Constraints.Select(constraint => constraint.Name).Should().NotContain("clim");

        (Torque wide, LinearMetaModel<Flt64> wideModel, Load _) = Prepare(wideBody: true);
        wide.Register(wideModel).IsOk.Should().BeTrue();
        wide.ClimSymbol.Should().NotBeNull("the wide-body registers CLIM");
        wide.ClimSymbol!.Name.Should().Be("clim");
    }

    /// <summary>
    /// **指数族用指数而不是力矩** / The index family sums indices, not torques.
    /// </summary>
    /// <remarks>
    /// 指数常数 = 干操作指数 10 + 救生筏指数 12 +（起飞/着陆的）燃油指数；**不是**干操作为力矩、也不是救生筏的重量。
    /// 与纵向力矩成对：同一个阶段的两族符号常数不同，且差值不等于重量乘以某个系数。
    ///
    /// The index constant is dry-operating index 10 + liferaft index 12 + (for take-off/landing) the fuel index — **not** the dry-operating torque and not the liferaft's weight.
    /// Paired with the longitudinal torque: the two families of the same phase have different constants, and the gap is not a weight times some coefficient.
    /// </remarks>
    [Fact]
    public void Register_ShouldSumIndicesForTheIndexFamily() {
        (Torque torque, LinearMetaModel<Flt64> model, Load _) = Prepare();
        torque.Register(model).IsOk.Should().BeTrue();

        double zeroFuelIndex = torque.Index(FlightPhase.ZeroFuel)!.Polynomial.Constant.ToDouble();
        double takeOffIndex = torque.Index(FlightPhase.TakeOff)!.Polynomial.Constant.ToDouble();

        zeroFuelIndex.Should().BeApproximately(10.0 + 12.0, 1e-9);
        takeOffIndex.Should().BeApproximately(10.0 + 12.0 + 5.0, 1e-9);
        torque.Index(FlightPhase.ZeroFuel)!.Name.Should().Be("index_zerofuel");
        torque.LongitudinalTorque(FlightPhase.ZeroFuel)!.Name.Should().Be("longitudinal_torque_zerofuel");
        zeroFuelIndex.Should().NotBeApproximately(
            torque.LongitudinalTorque(FlightPhase.ZeroFuel)!.Polynomial.Constant.ToDouble(), 1e-6,
            "the index family and the torque family are different quantities");
    }

    /// <summary>
    /// **缺装载符号时明确失败** / Missing load symbols fail loudly.
    /// </summary>
    /// <remarks>
    /// 空壳版本对任何输入都返回 `Ok`。缺符号时报成功会让调用方以为力矩建好了，而模型里一行都没有——整条重心约束随之消失。
    ///
    /// The shell returned `Ok` for any input. Reporting success while a symbol is missing tells the caller the torques exist when the model holds none — and the whole
    /// centre-of-gravity constraint disappears with them.
    /// </remarks>
    [Fact]
    public void Register_ShouldFailWithoutTheLoadSymbols() {
        (Torque torque, LinearMetaModel<Flt64> model, Load _) = Prepare(registerLoad: false);
        torque.Register(model).IsOk.Should().BeFalse();
        torque.LongitudinalTorque(FlightPhase.TakeOff).Should().BeNull();
        torque.LateralTorqueSymbol.Should().BeNull();
    }
}
