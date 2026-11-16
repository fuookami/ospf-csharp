// 配载约束管线的判据（此前）/ Criteria for the stowage constraint pipelines, first batch.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using AircraftPosition = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.Position;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **配载约束管线此前** / The first batch of stowage constraint pipelines.
/// </summary>
/// <remarks>
/// 六个类，六个"容易顺手补一行/漏一行"的地方。每条的判据都成对：**该有的行在、不该有的行不在**。
/// 只断言"行数大于零"在这六个类里全部无效——它们的正常形态本来就各只加几行。
///
/// Six classes, six places where a row is easily added or dropped. Every criterion is paired: **the row that should exist does, and the one that should not does not**. Asserting
/// "the row count is positive" is useless across all six, since their normal form is only a handful of rows each.
/// </remarks>
public class Demo2StowageLimitTests {
    private static Quantity<Flt64> Grams(double value) => new(new Flt64(value), MassUnits.Gram);

    private static Item MakeItem(
        string name,
        ItemStatus status = ItemStatus.Optional,
        ItemLocation? location = null) =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: "PEK",
            destination: "SHA",
            status: status,
            location: location ?? ItemLocation.MainDeck);

    private static StowagePosition MakePosition(
        string name,
        ulong id,
        double capacity = 1000.0,
        ulong mla = 3UL,
        bool available = true,
        PositionLocation? location = null,
        PositionType? type = null) {
        var position = new AircraftPosition(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: (byte)id,
            coordinate: new PositionCoordinate(400.0 + (double)id * 200.0, 600.0 + (double)id * 200.0, -40.0, -40.0),
            shape: new PositionShape(50.0, 40.0, 50.0 * 40.0 * 60.0),
            location: location ?? PositionLocation.NormalMainLocation);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(available, false, false, false),
            mlw: new MaxLoadWeightLimit(capacity),
            mla: mla,
            type: type);
    }

    private sealed class Rig
    {
        public Rig(
            IReadOnlyList<Item>? items = null,
            IReadOnlyList<StowagePosition>? positions = null,
            Appointment? appointment = null) {
            Items = items ?? new List<Item> { MakeItem("I1"), MakeItem("I2") };
            Positions = positions ?? new List<StowagePosition> { MakePosition("P1", 1UL), MakePosition("P2", 2UL) };
            Appointment = appointment ?? new Appointment(new Dictionary<Item, AircraftPosition>());

            var allowed = new HashSet<AssignmentKey>();
            foreach (Item item in Items) {
                foreach (StowagePosition position in Positions) {
                    if (StowageModel.StowageNeeded(item, position)) {
                        allowed.Add(new AssignmentKey(item.Id, position.Name));
                    }
                }
            }
            var loadingItems = Items.Select(item => new LoadingItem<Flt64>(item.Id, Grams(item.Weight))).ToList();
            var loadingPositions = Positions
                .Select(position => new LoadingPosition<Flt64>(position.Name, position.Mla, Grams(position.Mlw.Mlw)))
                .ToList();
            var problem = new DiscreteLoadingProblem<Flt64>(
                Items: loadingItems,
                Positions: loadingPositions,
                AllowedAssignments: allowed,
                Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
                LayoutGroups: new List<LayoutSelectionGroup>(),
                WeightUnit: MassUnits.Gram);
            Stowage = new StowageModel(Items, Positions) {
                LoadingContext = new DiscreteLoadingContext<Flt64>(problem, new Flt64PackingValueAdapter()),
            };
            Load = new Load(Items, Positions, Stowage, withMultiLoadingSchema: true);
            Payload = new Payload(Positions, Load, plannedPayload: 500.0, maxPayload: 2000.0);
            TotalWeight = new TotalWeight(
                new Fuselage(null, Dow: 40000.0, Doi: 10.0, BalancedArm: 900.0),
                // 燃油表不能为空：`TotalWeight.Register` 按阶段建符号，空表直接失败。
                // The fuel map cannot be empty: `TotalWeight.Register` builds a symbol per phase and fails outright on an empty map.
                new Dictionary<FlightPhase, FuelConstant> {
                    [FlightPhase.TakeOff] = new FuelConstant(Density: 0.8, Weight: 1000.0, Index: 1000.0),
                    [FlightPhase.Landing] = new FuelConstant(Density: 0.8, Weight: 2000.0, Index: 2000.0),
                },
                Payload);
            MaxLoadWeight = new MaxLoadWeight(Positions, TotalWeight);
        }

        public IReadOnlyList<Item> Items { get; }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public Appointment Appointment { get; }

        public StowageModel Stowage { get; }

        public Load Load { get; }

        public Payload Payload { get; }

        public TotalWeight TotalWeight { get; }

        public MaxLoadWeight MaxLoadWeight { get; }

        public LinearMetaModel<Flt64> RegisterAll() {
            var model = new LinearMetaModel<Flt64>();
            // 顺序是硬要求：装载 → 载荷 → 业载 → 总重 → 最大装载重量。少注册业载会让 `TotalWeight.Register` 直接失败。
            // The order is a hard requirement: stowage → load → payload → total weight → maximum load weight. Skipping the payload makes `TotalWeight.Register` fail outright.
            Stowage.Register(model).IsOk.Should().BeTrue();
            Load.Register(model).IsOk.Should().BeTrue();
            Payload.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();
            TotalWeight.Register(model).IsOk.Should().BeTrue();
            MaxLoadWeight.Register(model).IsOk.Should().BeTrue();
            return model;
        }
    }

    private static IReadOnlyList<string> RowNames(LinearMetaModel<Flt64> model) =>
        model.Constraints.Select(constraint => constraint.Name).ToList();

    /// <summary>
    /// **按状态给 `loaded_i` 加行，`Reserved` / `Loaded` 不加** / Rows on `loaded_i` by status, none for `Reserved` / `Loaded`.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. `Preassigned` 与 `AdjustmentNeeded` 各有一行，比较符是 `EQ`、右端常数 1。
    /// 2. `Optional` 有一行，比较符是 `LE`。
    /// 3. `Reserved` 与 `Loaded` **没有行**（`else -> {}`）。
    /// 4. 行名带货物名，能按名字定位。
    ///
    /// 第 3 点是关键：`Reserved` 看起来该有 `loaded_i = 0`，补上会让约束表多一行而模型照样可解。
    ///
    /// Four paired points: `Preassigned` and `AdjustmentNeeded` each get a row with `EQ` and a constant 1; `Optional` gets one with `LE`; `Reserved` and `Loaded` get **none**
    /// (its `else -> {}`); and the row names carry the item name so they can be located. Point three is the crux: `Reserved` looks like it deserves
    /// `loaded_i = 0`, and adding it yields one extra row while the model still solves.
    /// </remarks>
    [Fact]
    public void ItemAssignmentLimit_ShouldAddRowsByStatusOnly() {
        var rig = new Rig(items: new List<Item> {
            MakeItem("Pre", ItemStatus.Preassigned),
            MakeItem("Adj", ItemStatus.AdjustmentNeeded),
            MakeItem("Opt", ItemStatus.Optional),
            MakeItem("Res", ItemStatus.Reserved),
            MakeItem("Loa", ItemStatus.Loaded),
        });
        using LinearMetaModel<Flt64> model = rig.RegisterAll();
        new ItemAssignmentLimit(rig.Items, rig.Stowage).Invoke(model).IsOk.Should().BeTrue();

        IReadOnlyList<string> names = RowNames(model);
        names.Should().Contain("item_assignment_limit_Pre");
        names.Should().Contain("item_assignment_limit_Adj");
        names.Should().Contain("item_assignment_limit_Opt");
        names.Should().NotContain("item_assignment_limit_Res", "the baseline's else branch adds nothing for Reserved");
        names.Should().NotContain("item_assignment_limit_Loa", "the baseline's else branch adds nothing for Loaded");

        LinearInequalityConstraint<Flt64> pre = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "item_assignment_limit_Pre");
        pre.Inequality.Comparison.Should().Be(Comparison.EQ);
        pre.Inequality.Rhs.Constant.ToDouble().Should().Be(1.0);

        LinearInequalityConstraint<Flt64> opt = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "item_assignment_limit_Opt");
        opt.Inequality.Comparison.Should().Be(Comparison.LE);
        opt.Inequality.Rhs.Constant.ToDouble().Should().Be(1.0);
    }

    /// <summary>
    /// **预约行只给有预约的货物，第二段不加行** / Appointment rows only for appointed items, and the second half adds no row.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 有预约的货物有一行 `stowage = 1`，没预约的没有。
    /// 2. 行数**等于预约数**——第二段（数量校验）不加行，否则会与内核的 `MaxCount` 重复。
    /// 3. 预约舱位不在当前舱位表里时**失败**，不是跳过。
    ///
    /// Three paired points: an appointed item gets a `stowage = 1` row and an unappointed one does not; the row count **equals the appointment count** — the second half adds
    /// nothing, or it would duplicate the kernel's `MaxCount`; and an appointed position outside the current list **fails** rather than being skipped.
    /// </remarks>
    [Fact]
    public void AppointmentLimit_ShouldAddOneRowPerAppointmentOnly() {
        var items = new List<Item> { MakeItem("I1"), MakeItem("I2") };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL), MakePosition("P2", 2UL) };
        var appointment = new Appointment(new Dictionary<Item, AircraftPosition> {
            [items[0]] = positions[1].AircraftPosition,
        });
        var rig = new Rig(items: items, positions: positions, appointment: appointment);
        using LinearMetaModel<Flt64> model = rig.RegisterAll();

        new AppointmentLimit(rig.Items, rig.Positions, rig.Appointment, rig.Stowage).Invoke(model).IsOk.Should().BeTrue();
        IReadOnlyList<string> names = RowNames(model);
        names.Should().Contain("appointment_limit_I1_P2");
        names.Should().NotContain("appointment_limit_I2_P2", "I2 has no appointment");
        names.Count(name => name.StartsWith("appointment_limit_", System.StringComparison.Ordinal))
            .Should().Be(1, "the count half adds no row, otherwise it duplicates the kernel's MaxCount");

        LinearInequalityConstraint<Flt64> row = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "appointment_limit_I1_P2");
        row.Inequality.Comparison.Should().Be(Comparison.EQ);
        row.Inequality.Rhs.Constant.ToDouble().Should().Be(1.0);
        row.Inequality.Lhs.Monomials.Should().ContainSingle("the row names exactly one stowage variable");

        // **第二段一行都不加**：把"数量校验"写成加一行 `Σ stowage ≤ mla` 会让这里变成 2。
        // **The second half adds no row at all**: writing the count check as a `Σ stowage ≤ mla` row would make this 2.
        model.Constraints.Count.Should().BeGreaterThan(0);
        names.Count(name => name.StartsWith("appointment_limit_", System.StringComparison.Ordinal))
            .Should().Be(1, "one appointment means exactly one row, no matter how many positions there are");

        // 预约舱位不在表里：明确失败。
        // An appointed position outside the list: an explicit failure.
        StowagePosition outsider = MakePosition("P9", 9UL);
        var badAppointment = new Appointment(new Dictionary<Item, AircraftPosition> { [items[1]] = outsider.AircraftPosition });
        var badRig = new Rig(items: items, positions: positions, appointment: badAppointment);
        using LinearMetaModel<Flt64> badModel = badRig.RegisterAll();
        Result<Success, ErrorCode, Error<ErrorCode>> outOfScope =
            new AppointmentLimit(badRig.Items, badRig.Positions, badAppointment, badRig.Stowage).Invoke(badModel);
        outOfScope.IsOk.Should().BeFalse("the appointed position is not part of the current scope");
        // **断言是哪一条分支**：下标为 -1 时后面的 `StowageSymbol(i, -1)` 也返回 `null`，同样会失败。
        // 只看 `IsOk` 为假时，删掉范围校验仍然会红——但红在"符号未注册"上，等于没钉住。
        // **Assert which branch fires**: with index -1 the later `StowageSymbol(i, -1)` also returns `null` and fails too. Asserting only that `IsOk` is false would still go red
        // after the scope check is deleted — but on "symbol not registered", pinning nothing.
        (outOfScope as Failed<Success, ErrorCode, Error<ErrorCode>>)!.Error.Message
            .Should().Contain("不属于当前配载范围", "the scope check must be the one that fires");
    }

    /// <summary>
    /// **不合法对加 `stowage = 0`，合法对不加** / Illegal pairs get `stowage = 0`, legal ones do not.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 主甲板货物 vs 散货舱：不合法，有一行且右端是 0。
    /// 2. 主甲板货物 vs 主甲板舱位：合法，**没有行**。
    /// 3. 不可用舱位：即使不接纳也不加行（`status.available` 过滤）。
    ///
    /// 第 3 点最难看出来：行本身是对的（不可用舱位的装载量恒为 0，`= 0` 成立），只是白撑大约束表。
    ///
    /// Three paired points: a main-deck item against a bulk position is illegal and gets one row with 0 on the right; a main-deck item against a main-deck position is legal
    /// and gets **no** row; and an unavailable position gets no row even when it refuses the item (the `status.available` filter). Point three is the hardest to spot: the row is
    /// true (an unavailable position's load is identically zero, so `= 0` holds), it merely swells the table for nothing.
    /// </remarks>
    [Fact]
    public void StowageLimit_ShouldZeroOnlyTheIllegalAvailablePairs() {
        var bulkLocation = new PositionLocation(new HashSet<PositionLocationTag> { PositionLocationTag.Bulk });
        // **再加一件不可用的货物**：`Loaded` 状态的货物本身不参与决策，即使舱位拒它也**不加行**。
        // **One more, unavailable item**: a `Loaded` item takes no decision, so even a refusing position gets **no** row.
        var items = new List<Item> { MakeItem("I1"), MakeItem("I2", ItemStatus.Loaded) };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL),
            MakePosition("P2", 2UL, location: bulkLocation),
            MakePosition("P3", 3UL, location: bulkLocation, available: false),
        };
        var rig = new Rig(items: items, positions: positions);
        using LinearMetaModel<Flt64> model = rig.RegisterAll();

        new StowageLimit(rig.Items, rig.Positions, rig.Stowage).Invoke(model).IsOk.Should().BeTrue();
        IReadOnlyList<string> names = RowNames(model);
        names.Should().Contain("stowage_limit_I1_P2", "a bulk position refuses a main-deck item");
        names.Should().NotContain("stowage_limit_I1_P1", "P1 accepts the item");
        names.Should().NotContain("stowage_limit_I1_P3", "P3 is unavailable, so no row is needed");
        names.Should().NotContain("stowage_limit_I2_P2", "I2 is Loaded, so it takes no decision at all");

        LinearInequalityConstraint<Flt64> row = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "stowage_limit_I1_P2");
        row.Inequality.Comparison.Should().Be(Comparison.EQ);
        row.Inequality.Rhs.Constant.ToDouble().Should().Be(0.0);
    }

    /// <summary>
    /// **件数与重量上限只给可用舱位** / The count and weight caps cover available positions only.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. `load_amount_j ≤ mla_j` 的右端是**该舱位的** `mla`（两个舱位给不同的 `mla`，否则分不清是不是取错了下标）。
    /// 2. `estimate_load_weight_j ≤ max_load_weight_j` 的右端是**符号**，有单式（不是常数）。
    /// 3. 两个类都**跳过不可用舱位**。
    /// 4. 行名带舱位名。
    ///
    /// 第 1 点用一个 `mla = 7` 的舱位：若实现取了 `positions[0].Mla`，右端会变成 3。
    ///
    /// Four paired points: `load_amount_j ≤ mla_j` uses **that position's** `mla` (two positions get different values, or a wrong index cannot be detected);
    /// `estimate_load_weight_j ≤ max_load_weight_j` uses the **symbol**, which has monomials; both classes **skip unavailable positions**; and the row names carry the position
    /// name. Point one uses a position with `mla = 7` so an implementation reading `positions[0].Mla` shows 3 instead.
    /// </remarks>
    [Fact]
    public void LoadCaps_ShouldUseTheRightHandSidePerPosition() {
        var items = new List<Item> { MakeItem("I1") };
        var positions = new List<StowagePosition> {
            // 两个舱位的 `mla` 与容量都不同：否则"取错下标"与"取第一个"给出同一个结果。
            // Both positions get distinct `mla` and capacity, or "wrong index" and "always the first" produce the same result.
            MakePosition("P1", 1UL, capacity: 1000.0, mla: 3UL),
            MakePosition("P2", 2UL, capacity: 500.0, mla: 7UL),
            MakePosition("P3", 3UL, available: false),
        };
        var rig = new Rig(items: items, positions: positions);
        using LinearMetaModel<Flt64> model = rig.RegisterAll();

        new LoadAmountLimit(rig.Positions, rig.Load).Invoke(model).IsOk.Should().BeTrue();
        new LoadWeightLimit(rig.Positions, rig.Load, rig.MaxLoadWeight).Invoke(model).IsOk.Should().BeTrue();

        IReadOnlyList<string> names = RowNames(model);
        names.Should().Contain("load_amount_limit_P1");
        names.Should().Contain("load_amount_limit_P2");
        names.Should().NotContain("load_amount_limit_P3", "P3 is unavailable");
        names.Should().Contain("load_weight_limit_P1");
        names.Should().Contain("load_weight_limit_P2");
        names.Should().NotContain("load_weight_limit_P3", "P3 is unavailable");

        LinearInequalityConstraint<Flt64> amount = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "load_amount_limit_P2");
        amount.Inequality.Comparison.Should().Be(Comparison.LE);
        amount.Inequality.Rhs.Constant.ToDouble().Should().Be(7.0, "P2's own mla, not P1's");

        LinearInequalityConstraint<Flt64> weight = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "load_weight_limit_P2");
        // **不可观测已记录**：`MaxLoadWeight` 的符号是**常数多项式**（`Register` 把 `EffectiveMlw` 包成常数），
        // 所以"用符号"与"用 `EffectiveMlw` 标量"在非分段线性时给出同一个右端，本 fixture 无法区分。
        // 要区分需要分段线性的 `MaxLoadWeightLimit`（此时符号是按零油重的阶跃、标量是单值），本仓的 `MaxLoadWeight.Register` 目前也只包常数。
        // 因此这里断言的是"右端取该舱位自己的容量"，能钉住取错下标，钉不住符号/标量之分。
        // **An unobservable, recorded**: `MaxLoadWeight`'s symbol is a **constant polynomial** (`Register` wraps `EffectiveMlw` in a constant), so "use the symbol" and "use the
        // `EffectiveMlw` scalar" give the same right-hand side without piecewise linearity and this fixture cannot tell them apart. Telling them apart needs a piecewise
        // `MaxLoadWeightLimit` (the symbol is then a step by zero-fuel weight while the scalar is one value), and `MaxLoadWeight.Register` currently wraps a constant anyway.
        // What is asserted here is that the right-hand side takes **that position's own capacity**, which pins a wrong index but not symbol-versus-scalar.
        weight.Inequality.Rhs.Constant.ToDouble().Should().Be(500.0, "P2's own capacity, not P1's");
    }

    /// <summary>
    /// **禁止放空只给带 `EmptyForbidden` 码的可用舱位** / The no-empty rows cover available positions with the `EmptyForbidden` code only.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 带码且可用：有一行 `estimate_loaded = 1`。
    /// 2. 带码但不可用：**没有行**。
    /// 3. 可用但没有码：**没有行**（少查类型码会给所有舱位强制装载，舱位多于货物时立刻不可行）。
    ///
    /// Three paired points: coded and available gets an `estimate_loaded = 1` row; coded but unavailable gets **none**; available but uncoded gets **none** (dropping the code
    /// forces loading everywhere and turns infeasible as soon as positions outnumber items).
    /// </remarks>
    [Fact]
    public void EmptyForbiddenLimit_ShouldCoverCodedAvailablePositionsOnly() {
        var forbidden = new PositionType(new[] { PositionTypeCode.EmptyForbidden });
        var items = new List<Item> { MakeItem("I1") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, type: forbidden),
            MakePosition("P2", 2UL, type: forbidden, available: false),
            MakePosition("P3", 3UL),
        };
        var rig = new Rig(items: items, positions: positions);
        using LinearMetaModel<Flt64> model = rig.RegisterAll();

        new EmptyForbiddenLimit(rig.Positions, rig.Load).Invoke(model).IsOk.Should().BeTrue();
        IReadOnlyList<string> names = RowNames(model);
        names.Should().Contain("empty_forbidden_limit_P1");
        names.Should().NotContain("empty_forbidden_limit_P2", "P2 is unavailable");
        names.Should().NotContain("empty_forbidden_limit_P3", "P3 carries no EmptyForbidden code");

        LinearInequalityConstraint<Flt64> row = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "empty_forbidden_limit_P1");
        row.Inequality.Comparison.Should().Be(Comparison.EQ);
        row.Inequality.Rhs.Constant.ToDouble().Should().Be(1.0);
    }
}
