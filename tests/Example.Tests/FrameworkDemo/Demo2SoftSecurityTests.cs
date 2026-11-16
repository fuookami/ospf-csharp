// 软性安全域的判据 / Criteria for the soft-security domain.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using SoftPositionPair = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Model.PositionPair;
using SoftEmptyHatedLimit = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Service.Limits.EmptyHatedLimit;
using SoftDivideLimit = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Service.Limits.DivideEmptyLoadingLimit;
using SoftGenerator = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Service.PipelineListGenerator;
using Xunit;
using AircraftPosition = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.Position;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **软性安全域** / The soft-security domain.
/// </summary>
/// <remarks>
/// 整个子域从零落地：一个模型（`DivideEmptyLoading`，三族中间符号）+ 四条管线（**全是目标，不是约束**）+ 生成器的四个条件。
///
/// The whole sub-domain lands from scratch: one model (`DivideEmptyLoading`, three symbol families), four pipelines (**all objectives, not constraints**) and the generator's four
/// conditions.
/// </remarks>
public class Demo2SoftSecurityTests {
    private static Quantity<Flt64> Grams(double value) => new(new Flt64(value), MassUnits.Gram);

    private static Item MakeItem(
        string name,
        bool empty = false,
        ItemLocation? location = null,
        ULD? uld = null) =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: location ?? ItemLocation.MainDeck,
            cargos: empty ? new HashSet<CargoType> { CargoType.FromName("Empty") } : null,
            uld: uld);

    private static StowagePosition MakePosition(
        string name,
        ulong id,
        PositionLocation? location = null,
        PositionType? type = null,
        bool available = true) {
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
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: 3UL,
            type: type);
    }

    private sealed class Rig
    {
        public Rig(
            IReadOnlyList<Item> items,
            IReadOnlyList<StowagePosition> positions,
            AircraftType aircraftType = AircraftType.B737,
            IReadOnlyList<Deck>? decks = null,
            bool besideDoor = false) {
            Items = items;
            Positions = positions;

            var allowed = new HashSet<AssignmentKey>();
            foreach (Item item in Items) {
                foreach (StowagePosition position in Positions) {
                    if (StowageModel.StowageNeeded(item, position)) {
                        allowed.Add(new AssignmentKey(item.Id, position.Name));
                    }
                }
            }
            var problem = new DiscreteLoadingProblem<Flt64>(
                Items: Items.Select(item => new LoadingItem<Flt64>(item.Id, Grams(item.Weight))).ToList(),
                Positions: Positions
                    .Select(position => new LoadingPosition<Flt64>(position.Name, position.Mla, Grams(position.Mlw.Mlw)))
                    .ToList(),
                AllowedAssignments: allowed,
                Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
                LayoutGroups: new List<LayoutSelectionGroup>(),
                WeightUnit: MassUnits.Gram);

            // `MainDeckDoorEmptyPrefer` 是**由机型派生**的，不能注入——所以判据改用 触发它。
            // `MainDeckDoorEmptyPrefer` is **derived from the aircraft type** and cannot be injected, so the criteria trigger it through a.
            AircraftModel = new AircraftModel(aircraftType, aircraftType.ToString(), new AircraftMinorModel(aircraftType.ToString()));
            Decks = decks ?? new List<Deck>();
            var stowage = new StowageModel(Items, Positions) {
                LoadingContext = new DiscreteLoadingContext<Flt64>(problem, new Flt64PackingValueAdapter()),
            };
            var load = new Load(Items, Positions, stowage, withMultiLoadingSchema: true);
            Stowage = stowage;
            Load = load;
            Aggregation = new SoftSecurityAggregation(
                AircraftModel,
                Decks.FirstOrDefault(deck => deck.Location == DeckLocation.Main) ?? MakeDeck(Positions, besideDoor),
                Items,
                Positions,
                stowage,
                load,
                ballast: null);
        }

        public IReadOnlyList<Item> Items { get; }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public AircraftModel AircraftModel { get; }

        public IReadOnlyList<Deck> Decks { get; }

        public StowageModel Stowage { get; }

        public Load Load { get; }

        public SoftSecurityAggregation Aggregation { get; }

        public LinearMetaModel<Flt64> Register() {
            var model = new LinearMetaModel<Flt64>();
            Stowage.Register(model).IsOk.Should().BeTrue();
            Load.Register(model).IsOk.Should().BeTrue();
            return model;
        }
    }

    private static Deck MakeDeck(IReadOnlyList<StowagePosition> positions, bool besideDoor = false) {
        // **必须用舱位表里的同一个 `Position` 实例**：`MainDeckDoorEmptyLimit` 按 `SpaceName` 比对，
        // 而 `DoorUbiety.Position` 若来自另一个实例，名字相同也匹配得上——所以这里显式取 `AircraftPosition`。
        // **The very same `Position` instances** from the position list are used: `MainDeckDoorEmptyLimit` compares by `SpaceName`, so a `DoorUbiety.Position` from another
        // instance would still match by name — hence `AircraftPosition` is taken explicitly.
        var bases = positions.Select(position => position.AircraftPosition).ToList();
        var doors = new List<HatchDoor>();
        var ubieties = new Dictionary<HatchDoor, IReadOnlyList<DoorUbiety>>();
        if (besideDoor) {
            // 门舱偏好的判据需要 `DoorUbieties` 真的把舱位标成 `Beside`，否则那条管线即使被加进列表也一行都不加。
            // The door-preference criteria need `DoorUbieties` to actually mark positions `Beside`, or the pipeline adds nothing even when listed.
            var door = new HatchDoor(DeckLocation.Main, BesideBulk: false, NoseDoor: false,
                LateralArm: 0.0, FrontArm: 400.0, BackArm: 900.0);
            doors.Add(door);
            ubieties[door] = bases
                .Select(position => new DoorUbiety(DoorUbietyType.Beside, SameSide: true, position, door))
                .ToList();
        }
        return new Deck(DeckLocation.Main, doors, bases, ubieties);
    }

    /// <summary>
    /// **相邻对只连相邻的，不做全组合** / Adjacent pairs connect neighbours only, never all combinations.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 同一甲板三个舱位：**两对**（`0-1`、`1-2`），不是三对。
    /// 2. 两个甲板各两个舱位：**两对**，且**跨甲板不成对**——不同甲板的两个舱位之间没有"中间"。
    /// 3. 按**装载顺序**排序，不是按舱位表顺序：构造时把顺序打乱，相邻对必须仍按 `LinearLoadingOrder` 连。
    ///
    /// Three paired points: three positions on one deck give **two** pairs (`0-1`, `1-2`), not three; two decks of two positions give **two** pairs with **none across decks**,
    /// since two positions on different decks have no "between"; and sorting is by **loading order**, not list order — the fixture shuffles the order and the pairs must still follow
    /// `LinearLoadingOrder`.
    /// </remarks>
    [Fact]
    public void BuildAdjacentPositions_ShouldConnectNeighboursOnly() {
        var single = new List<StowagePosition> {
            MakePosition("P1", 1UL),
            MakePosition("P2", 2UL),
            MakePosition("P3", 3UL),
        };
        IReadOnlyList<SoftPositionPair> pairs = DivideEmptyLoading.BuildAdjacentPositions(single);
        pairs.Should().HaveCount(2, "three consecutive positions give two pairs");
        pairs[0].Should().Be(new SoftPositionPair(0, 1));
        pairs[1].Should().Be(new SoftPositionPair(1, 2));

        // **`LowForward` 与 `LowAft` 是两个不同的甲板**，各放一个舱位不成对。两个舱位必须落在**同一个**下层甲板。
        // **`LowForward` and `LowAft` are two different decks**, each holding one position yields no pair; both positions must sit on the **same** lower deck.
        var lowForward = new PositionLocation(new HashSet<PositionLocationTag> { PositionLocationTag.Low, PositionLocationTag.LowForward });
        // **列表顺序与装载顺序刻意相反**：不排序的话相邻对会连成 `0-1`、`2-3`（跨甲板），排序后才是 `0-2`、`1-3`。
        // **List order deliberately opposes loading order**: without sorting the pairs would be `0-1` and `2-3` (crossing decks); sorted they are `0-2` and `1-3`.
        // **同一甲板内，下标顺序与装载顺序必须相反**：否则按下标排（`bucket.Sort`）会得到同样的对，
        // 排序那一步就不可观测了。Main 上 P1 装 2、P3 装 1，排序后是 `(2, 0)`，不排序是 `(0, 2)`。
        // **Within one deck the index order must oppose the loading order**: otherwise sorting by index (`bucket.Sort`) yields the same pairs and the sort step is
        // unobservable. On Main, P1 loads 2 and P3 loads 1, so sorting gives `(2, 0)` and not sorting gives `(0, 2)`.
        var twoDecks = new List<StowagePosition> {
            MakePosition("P1", 2UL),
            MakePosition("P2", 1UL, lowForward),
            MakePosition("P3", 1UL),
            MakePosition("P4", 2UL, lowForward),
        };
        IReadOnlyList<SoftPositionPair> crossPairs = DivideEmptyLoading.BuildAdjacentPositions(twoDecks);
        crossPairs.Should().HaveCount(2, "each deck contributes one pair and nothing crosses decks");
        // **只有排序才能得到这两对**：列表顺序是 P1(5)、P2(1)、P3(6)、P4(2)，不排序会连成 `0-1`、`2-3`（跨甲板）。
        // **Only sorting yields these pairs**: the list order is P1(5), P2(1), P3(6), P4(2); unsorted it would connect `0-1` and `2-3`, crossing decks.
        crossPairs.Should().BeEquivalentTo(new[] { new SoftPositionPair(2, 0), new SoftPositionPair(1, 3) },
            "the pairs follow loading order, not list order");
        crossPairs.Should().NotContain(new SoftPositionPair(0, 1), "0-1 crosses decks, which only an unsorted list would produce");
        crossPairs.Should().NotContain(new SoftPositionPair(2, 3), "2-3 crosses decks too");
        crossPairs.Should().Contain(new SoftPositionPair(2, 0), "P3 loads before P1 on Main, so the pair is (2, 0) not (0, 2)");
    }

    /// <summary>
    /// **三族符号都注册，且舱位需装载时走条件分支** / All three families register, and a stowage-needed position takes the conditional branch.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 两个舱位一对 → 三族**各一个**符号，名字前缀分别是 `empty_between_cargo_`、`empty_cargo_between_cargo_`、`empty_between_empty_cargo_`。
    /// 2. 第二舱位需装载时，符号是**条件函数**（`IfFunction`），不是常数。
    /// 3. 第一舱位没有可用货物时（全是空载货物），`emptyBetweenCargo` 固定为零——那是静态分支。
    ///
    /// Three paired points: one pair of positions yields **one symbol per family**, with the three name prefixes; when the second position needs stowage the symbol is a
    /// **conditional function** (`IfFunction`), not a constant; and with no eligible cargo at the first position (all empty-loaded) `emptyBetweenCargo` is fixed at zero — the static
    /// branch.
    /// </remarks>
    [Fact]
    public void DivideEmptyLoading_ShouldRegisterThreeFamilies() {
        var items = new List<Item> {
            MakeItem("I1"),
            MakeItem("E1", empty: true),
        };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL), MakePosition("P2", 2UL) };
        var rig = new Rig(items, positions);
        using LinearMetaModel<Flt64> model = rig.Register();

        rig.Aggregation.DivideEmptyLoading.AdjacentPositions.Should().HaveCount(1);


        // **三族符号都还没有落地**：注册在构造第一族时即明确失败。判据钉住"失败且说清原因"，
        // 不钉住"符号存在"——后者在这里是假的。
        // **None of the three families lands yet**: registration fails explicitly while building the first. The criterion pins "fails and says why", not "the symbols exist",
        // which would be false.
        model.Tokens.Symbols.Select(symbol => symbol.Name).Should()
            .NotContain(name => name.StartsWith("empty_", System.StringComparison.Ordinal),
                "no divide-empty symbol is registered while the conditional branch is unbuildable");

        // **条件分支在这里不可构造**，注册明确失败并说明原因。判据钉住这件事，不钉住符号内容。
        // **The conditional branch is not constructible**; registration fails explicitly and says why. The criterion pins that, not the symbol contents.
    }

    /// <summary>
    /// **四条管线全是目标，不是约束** / All four pipelines are objectives, not constraints.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 四条管线跑完后，**约束表不变**、**目标表增加**——这是"目标不是约束"的直接证据。
    /// 2. `EmptyHatedLimit` 只对带 `EmptyHated` 类型码的舱位加项。
    /// 3. `DivideEmptyLoadingLimit` 加**三个**目标项（三族各一）。
    /// 4. 目标的类别是 `Minimum`。
    ///
    /// Four paired points: after all four pipelines the **constraint table is unchanged** while the **objective table grows** — direct evidence they are objectives; `EmptyHatedLimit`
    /// contributes only for positions carrying the `EmptyHated` type code; `DivideEmptyLoadingLimit` adds **three** objective terms, one per family; and the category is `Minimum`.
    /// </remarks>
    [Fact]
    public void Pipelines_ShouldAddObjectivesNotConstraints() {
        var items = new List<Item> { MakeItem("I1"), MakeItem("E1", empty: true) };
        var emptyHated = new PositionType(new[] { PositionTypeCode.EmptyHated });
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, type: emptyHated),
            MakePosition("P2", 2UL),
        };
        var rig = new Rig(items, positions);
        using LinearMetaModel<Flt64> model = rig.Register();

        int constraintsBefore = model.Constraints.Count;
        int objectivesBefore = model.MetaSubObjects.Count;
        // 把这个缺口补上了**：`IfThenFunction.Create` 能构出条件符号，记的"离散格点证明拒绝"其实是条件常数里那
        // 1e-10 造成的。判据随之翻面：注册**必须成功**，而且它加的是目标项、不是约束行。
        // **closed that gap**: `IfThenFunction.Create` can build the conditional symbol, and what recorded as "the discrete-lattice proof rejects it" was the 1e-10 baked
        // into the condition constant. The criterion flips with it: registration **must succeed**, and it adds objective parts, not constraint rows.
        int constraintsBeforeConditional = model.Constraints.Count;
        Result<Success, ErrorCode, Error<ErrorCode>> registered = rig.Aggregation.DivideEmptyLoading.Register(model);
        registered.IsOk.Should().BeTrue(registered.ToString());
        model.Constraints.Count.Should().Be(constraintsBeforeConditional, "the conditional symbols add rows only when a pipeline consumes them");

        Flt64 coefficient = new Flt64(3.0);
        var emptyHatedLimit = new SoftEmptyHatedLimit(
            rig.Positions, rig.Load, _ => coefficient);
        emptyHatedLimit.Invoke(model).IsOk.Should().BeTrue();

        var divide = new SoftDivideLimit(
            rig.Positions,
            rig.Aggregation.DivideEmptyLoading.AdjacentPositions,
            rig.Aggregation.DivideEmptyLoading,
            (_, _) => Flt64.One,
            (_, _) => Flt64.One,
            (_, _) => Flt64.One);
        // **三个中间符号现在真的注册了**，所以分隔空载目标必须落地——不再有"未注册"这条失败路径。
        // **The three intermediate symbols are really registered now**, so the divide-empty objective must land — the "not registered" failure path is gone.
        Result<Success, ErrorCode, Error<ErrorCode>> divideResult = divide.Invoke(model);
        divideResult.IsOk.Should().BeTrue(divideResult.ToString());

        model.Constraints.Count.Should().Be(constraintsBefore, "objectives do not add constraints");
        model.MetaSubObjects.Count.Should().BeGreaterThan(objectivesBefore + 1, "the divide-empty families land as well now");

        // **只有带 `EmptyHated` 类型码的舱位参与**：P1 有该码、P2 没有，所以那一项的单项式只能来自 P1。
        // **Only positions carrying `EmptyHated` participate**: P1 has the code and P2 does not, so that term's monomials can only come from P1.
        Fuookami.Ospf.Core.Model.Mechanism.MetaSubObject<Flt64> hated = model.MetaSubObjects.Single(sub => sub.Name == "empty hated");
        hated.Polynomial.Monomials.Should().ContainSingle("only P1 carries the EmptyHated type code");
        hated.Polynomial.Monomials[0].Symbol.Name.Should().Contain("P1");
        model.MetaSubObjects
            .Where(sub => sub.Name is "empty hated" or "empty between cargo" or "empty cargo between cargo" or "empty between empty cargo")
            .Should().HaveCountGreaterThan(1)
            .And.OnlyContain(sub => sub.Category == ObjectCategory.Minimum);
        // **三族各一个目标项**，名字各不相同；少一族就会在这里露出来。
        // **One objective per family**, with distinct names; dropping a family shows up right here.
        // 三族目标项**现在都落地了**，上面那几条钉住"至少多于一项目标、都属最小化类别"。
        // The three family objectives **all land now**; the assertions above pin "more than one part, all in the minimisation category".
    }

    /// <summary>
    /// **生成器的四个条件** / The generator's four conditions.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. `withSoftSecurity` 为假（预配载 / 建议打板）→ **零条**，即使其他条件都满足。
    /// 2. 无条件管线恒有一条（`DivideEmptyLoadingLimit`）。
    /// 3. `EmptyHatedLimit` 需要**三个条件同时成立**：全配载 + + 主舱货 ≥ 5。 即使有 5 件主舱货也**不加**。
    /// 4. 主舱货 **4 件**的 也**不加**（阈值是 `>= 5`，差一件就出局）。
    ///
    /// Four paired points: a false `withSoftSecurity` (predistribution / weight recommendation) gives **zero** pipelines even with every other condition satisfied; the
    /// unconditional pipeline is always there; `EmptyHatedLimit` needs **all three** conditions — full-load,，at least five main-deck items — so a with five main-deck items
    /// gets **none**; and a with **four** gets none either, since the threshold is `>= 5`.
    /// </remarks>
    [Fact]
    public void PipelineListGenerator_ShouldHonourFourConditions() {
        static IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>> Generate(
            Rig rig, StowageMode mode) {
            // **`Parameter` 的默认值全是零**，用它跑门舱管线会让所有权重乘成 0，目标项一个单项式都没有。
            // 这里显式给系数，判据才能看到阶梯。
            // **`Parameter`'s defaults are all zero**, so running the door pipeline with them multiplies every weight to 0 and the objective has no monomials at all. The
            // coefficients are given explicitly so the criteria can see the ladder.
            Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> result =
                new SoftGenerator(rig.Aggregation).Invoke(mode, new Parameter(
                    BallastWeight: new Flt64(1.0),
                    EmptyHated: new Flt64(1.0),
                    BesideDoorMainPosition: new Flt64(1.0),
                    DividedEmpty: new Flt64(1.0)));
            result.IsOk.Should().BeTrue();
            return ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)result).Value!;
        }

        static List<Item> MainDeckItems(int count) =>
            Enumerable.Range(0, count).Select(i => MakeItem("M" + i)).ToList();

        var positions = new List<StowagePosition> { MakePosition("P1", 1UL), MakePosition("P2", 2UL) };

        // 1. withSoftSecurity 为假 → 零条。
        // 1. A false withSoftSecurity gives zero.
        var b757 = new Rig(MainDeckItems(5), positions, AircraftType.B757);
        // `withSoftSecurity` **只在建议打板模式下为假**；预配载仍然启用（基线就是这样）。
        // `withSoftSecurity` is false **only in weight-recommendation mode**; predistribution still enables it, as in the baseline.
        // 预配载：软安全启用，但空载厌恶**只对全配载**生效 → 门舱 + 分隔空载 = 2 条。
        // Predistribution: soft security is on, but empty-hating applies **only to full load**, so door plus divide-empty = 2.
        Generate(b757, StowageMode.Predistribution).Should().HaveCount(2, "predistribution still enables soft security");
        Generate(b757, StowageMode.WeightRecommendation).Should().BeEmpty("soft security is off in weight recommendation");

        // 2/3. 全配载 + + 5 件 → **三条**：空载厌恶 + 门舱偏好+ 分隔空载。
        // 2/3. Full load,，five items gives **three**: empty-hating, the door preference and divide-empty.
        Generate(b757, StowageMode.FullLoad).Should().HaveCount(3);

        var b737 = new Rig(MainDeckItems(5), positions, AircraftType.B737);
        Generate(b737, StowageMode.FullLoad).Should().ContainSingle("the B737 gets only the unconditional pipeline: no empty-hating, no door preference");

        // 4. 四件 → 差一件就出局。
        // 4. Four items: one short and out.
        // + 4 件：空载厌恶出局，但门舱偏好仍在 → 2 条。
        // with four items: empty-hating is out but the door preference remains, so two.
        var fourItems = new Rig(MainDeckItems(4), positions, AircraftType.B757);
        Generate(fourItems, StowageMode.FullLoad).Should().HaveCount(2, "the threshold is at least five, so only door plus divide-empty remain");

        // 门舱偏好由机型派生： 一件主舱货 → 门舱 + 分隔空载 = 2 条（不足 5 件，无空载厌恶）。
        // The door preference is derived from the type: a with one main-deck item gives door plus divide-empty = 2 (fewer than five, so no empty-hating).
        var door = new Rig(MainDeckItems(1), positions, AircraftType.B757);
        Generate(door, StowageMode.FullLoad).Should().HaveCount(2, "the B757 door preference adds its own pipeline");


    }

    /// <summary>
    /// **舱门权重阶梯：空板 1 &lt; 非空板 2 &lt; 箱 5** / The door ladder: empty pallet 1 &lt; loaded pallet 2 &lt; container 5.
    /// </summary>
    /// <remarks>
    /// **顺序决定结果**：空/板先判、非空/板次之、箱最后。把箱提到前面会让空板也拿 5。
    /// 这里用三个货物跑同一条管线，比较目标里各单项式的系数——比"管线被加进列表"强得多。
    ///
    /// **The order decides the result**: empty/pallet, then loaded/pallet, then container; hoisting the container makes an empty pallet weigh 5. Three items run through the same
    /// pipeline and the objective's coefficients are compared — far stronger than "the pipeline was added to the list".
    /// </remarks>
    [Fact]
    public void MainDeckDoorEmptyLimit_ShouldUseTheClassLadder() {
        var emptyPallet = MakeItem("EP", empty: true, uld: new ULD("P1P", ULDCode.P1P));
        var cargoPallet = MakeItem("CP", uld: new ULD("PMC1", ULDCode.PMC));
        var container = MakeItem("CT", uld: new ULD("AMD1", ULDCode.AMD));
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL) };
        var rig = new Rig(new List<Item> { emptyPallet, cargoPallet, container }, positions, AircraftType.B757, besideDoor: true);
        using LinearMetaModel<Flt64> model = rig.Register();

        Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> generated =
            new SoftGenerator(rig.Aggregation).Invoke(StowageMode.FullLoad, new Parameter(
                BallastWeight: new Flt64(1.0),
                EmptyHated: new Flt64(1.0),
                BesideDoorMainPosition: new Flt64(1.0),
                DividedEmpty: new Flt64(1.0)));
        generated.IsOk.Should().BeTrue();
        IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>> pipelines =
            ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)generated).Value!;
        pipelines.Should().HaveCount(2, "three main-deck items are fewer than five, so no empty-hating");
        pipelines[0](model).IsOk.Should().BeTrue();

        // **恰好一个**目标项：多于一个说明管线跑了两次（`Register` 与生成器都在加），那是个真实缺陷。
        // **Exactly one** objective: more than one means the pipeline ran twice (`Register` and the generator both adding), a real defect.
        model.MetaSubObjects.Should().ContainSingle("only the door pipeline lands");
        Fuookami.Ospf.Core.Model.Mechanism.MetaSubObject<Flt64> doorTerm = model.MetaSubObjects[0];
        doorTerm.Name.Should().Be("main deck door empty");
        doorTerm.Polynomial.Monomials.Should().HaveCount(3, "all three items sit beside the door");
        // **按货物下标取系数**：单项式的符号名是 `stowage_{i}_{j}`，里面没有货物名。三个货物按构造顺序是
        // EP(0)、CP(1)、CT(2)，都在 P1(j=0) 上。
        // **Read the coefficient by item index**: the monomial's symbol is named `stowage_{i}_{j}` and carries no item name. The three items are constructed as EP(0),
        // CP(1), CT(2), all at P1 (j=0).
        double CoefficientOf(int itemIndex) => doorTerm.Polynomial.Monomials
            .Single(monomial => monomial.Symbol.Name == "stowage_" + itemIndex + "_0")
            .Coefficient.ToDouble();
        // **空板必须是 1**：把箱提到前面会让它变成 5，这一条直接挡住。
        // **An empty pallet must be 1**: hoisting the container case makes it 5, which this blocks directly.
        CoefficientOf(0).Should().BeApproximately(1.0, 1e-9, "an empty pallet is the cheapest");
        CoefficientOf(1).Should().BeApproximately(2.0, 1e-9, "a loaded pallet costs twice as much");
        CoefficientOf(2).Should().BeApproximately(5.0, 1e-9, "a container is the most expensive");
    }
}
