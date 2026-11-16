// 配载约束管线的判据（此前）与生成器 / Criteria for the third pipeline batch and the generator.

using System;
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
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using StowagePipelineListGenerator = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service.PipelineListGenerator;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **配载约束管线此前与生成器** / The third pipeline batch and the generator.
/// </summary>
/// <remarks>
/// 三个相邻/冲突类 + 生成器。判据的重点：
///
/// 1. 相邻类每条邻接加**两行**（同向与交叉），条件用"或"。
/// 2. 生物散货冲突三支**都跑**（不像 `AOGMATBulkConflictLimit` 那样互斥）。
/// 3. 生成器 13 条（全配载）/ 14 条（预配载），且两条条件管线的条件**不同**、两条相邻管线用的邻接类型**不同**。
///
/// Three adjacency/conflict classes plus the generator. The criteria focus on: the adjacency classes adding **two rows** per adjacency (aligned and crossed) with an "or" guard;
/// the biological bulk conflict running **all three branches** (unlike `AOGMATBulkConflictLimit`'s mutually exclusive three); and the generator giving 13 pipelines in full-load
/// mode and 14 in predistribution, with the two conditional pipelines testing **different** conditions and the two adjacency pipelines using **different** neighbour types.
/// </remarks>
public class Demo2StowageLimit3Tests {
    private static Quantity<Flt64> Grams(double value) => new(new Flt64(value), MassUnits.Gram);

    private static readonly CargoType Eld = CargoType.FromName("ELD");
    private static readonly CargoType Live = CargoType.FromName("AVI");
    private static readonly CargoType DryIce = CargoType.FromName("ICE");

    private static Item MakeItem(
        string name,
        ItemStatus status = ItemStatus.Optional,
        ItemLocation? location = null,
        IReadOnlySet<CargoType>? cargos = null) =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: "PEK",
            destination: "SHA",
            status: status,
            location: location ?? ItemLocation.MainDeck,
            cargos: cargos);

    private static StowagePosition MakePosition(
        string name,
        ulong id,
        bool stowageNeeded = true,
        bool predicateWeightNeeded = false,
        bool recommendedWeightNeeded = false,
        PositionLocation? location = null,
        IReadOnlyList<Item>? loadedItems = null) {
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
            status: new PositionStatus(stowageNeeded, false, predicateWeightNeeded, recommendedWeightNeeded),
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: 3UL,
            loadedItems: loadedItems,
            plw: predicateWeightNeeded ? new PositionPredicateLoadWeight(0.0, 0.0, 1000.0) : null);
    }

    private sealed class Rig
    {
        public Rig(
            IReadOnlyList<Item> items,
            IReadOnlyList<StowagePosition> positions,
            BiologicalLimit? biologicalLimit = null,
            IReadOnlyDictionary<NeighbourType, IReadOnlyList<Neighbour>>? neighbours = null) {
            Items = items;
            Positions = positions;
            BiologicalLimit = biologicalLimit ?? new BiologicalLimit(
                new List<(CargoType, CargoType)>(),
                new List<(CargoType, CargoType)>(),
                new List<CargoType>());
            Neighbours = neighbours ?? new Dictionary<NeighbourType, IReadOnlyList<Neighbour>>();

            // **只保留聚合自带的模型**。第一版另建了一套 `Stowage`/`Load`，于是 `RegisterAll` 注册的是那一套，
            // 而 `PipelineListGenerator` 用的是 `Aggregation.Stowage`——两个不同实例，`loaded_i` 符号在后者里根本没建，第一条管线就失败。
            // **Keep only the aggregation's own models.** The first draft built a separate `Stowage`/`Load`, so `RegisterAll` registered that set while
            // `PipelineListGenerator` used `Aggregation.Stowage` — two different instances, `loaded_i` never built in the latter, and the first pipeline failed.
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

            var aircraftModel = new AircraftModel(AircraftType.B737, "B737", new AircraftMinorModel("B737"));
            var fuselage = new Fuselage(null, Dow: 40000.0, Doi: 10.0, BalancedArm: 900.0);
            var fuel = new Dictionary<FlightPhase, FuelConstant> {
                [FlightPhase.TakeOff] = new FuelConstant(Density: 0.8, Weight: 1000.0, Index: 1000.0),
                [FlightPhase.Landing] = new FuelConstant(Density: 0.8, Weight: 2000.0, Index: 2000.0),
            };
            Aggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.StowageAggregation(
                aircraftModel,
                new Flight(new FlightNo("CA1"), new IATACode("PEK"), new IATACode("SHA")),
                Items,
                Positions,
                new Appointment(new Dictionary<Item, AircraftPosition>()),
                BiologicalLimit,
                Neighbours,
                plannedPayload: 500.0,
                maxPayload: 2000.0,
                withMultiLoadingSchema: true,
                formula: new Formula(
                    aircraftModel, lip: 100.0, chord: 20.0, standardDatum: 120.0, forceDistanceCoefficient: 5000.0, doiCorrection: 7.0),
                fuselage: fuselage,
                fuel: fuel);
            Aggregation.Stowage.LoadingContext = new DiscreteLoadingContext<Flt64>(problem, new Flt64PackingValueAdapter());
        }

        public IReadOnlyList<Item> Items { get; }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public BiologicalLimit BiologicalLimit { get; }

        public IReadOnlyDictionary<NeighbourType, IReadOnlyList<Neighbour>> Neighbours { get; }

        public StowageModel Stowage => Aggregation.Stowage;

        public Load Load => Aggregation.Load;

        public MaxLoadWeight MaxLoadWeight => Aggregation.MaxLoadWeight;

        public Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.StowageAggregation Aggregation { get; }

        public LinearMetaModel<Flt64> RegisterAll() {
            var model = new LinearMetaModel<Flt64>();
            Aggregation.Stowage.Register(model).IsOk.Should().BeTrue();
            Aggregation.Load.Register(model).IsOk.Should().BeTrue();
            Aggregation.Payload.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();
            Aggregation.TotalWeight.Register(model).IsOk.Should().BeTrue();
            Aggregation.MaxLoadWeight.Register(model).IsOk.Should().BeTrue();
            return model;
        }
    }

    private static IReadOnlyList<string> RowNames(LinearMetaModel<Flt64> model) =>
        model.Constraints.Select(constraint => constraint.Name).ToList();

    /// <summary>
    /// **ELD 相邻每条邻接两行，同向与交叉都覆盖** / ELD adjacency gives two rows per adjacency, aligned and crossed.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 一条邻接、两件 ELD 货：**恰好两行**（`i1_P1_i2_P2` 与 `i1_P2_i2_P1`）。只写一行会漏掉交叉朝向。
    /// 2. 两行都是 `≤ 1`、各含两个单式。
    /// 3. 非 ELD 货物参与时**没有行**。
    ///
    /// Three paired points: one adjacency with two ELD items gives **exactly two rows** (the aligned and the crossed one) — a single row misses the crossed orientation; both
    /// rows are `≤ 1` with two monomials each; and a pair with a non-ELD item gets **no** row.
    /// </remarks>
    [Fact]
    public void ELDAdjacentLimit_ShouldAddBothOrientations() {
        var items = new List<Item> {
            MakeItem("E1", cargos: new HashSet<CargoType> { Eld }),
            MakeItem("E2", cargos: new HashSet<CargoType> { Eld }),
            MakeItem("G1"),
        };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL), MakePosition("P2", 2UL) };
        var neighbours = new Dictionary<NeighbourType, IReadOnlyList<Neighbour>> {
            [NeighbourType.Physics] = new List<Neighbour> {
                new(NeighbourType.Physics, new PositionPair(positions[0].AircraftPosition, positions[1].AircraftPosition)),
            },
        };
        var rig = new Rig(items, positions, neighbours: neighbours);
        using LinearMetaModel<Flt64> model = rig.RegisterAll();

        new ELDAdjacentLimit(rig.Items, rig.Positions, neighbours[NeighbourType.Physics], rig.Stowage).Invoke(model)
            .IsOk.Should().BeTrue();
        IReadOnlyList<string> names = RowNames(model)
            .Where(name => name.StartsWith("eld_adjacent_limit_", System.StringComparison.Ordinal))
            .ToList();

        names.Should().HaveCount(2, "one adjacency gives the aligned and the crossed row");
        names.Should().Contain("eld_adjacent_limit_E1_E2_P1_P2");
        names.Should().Contain("eld_adjacent_limit_E1_E2_P2_P1");
        names.Should().NotContain(name => name.Contains("G1", System.StringComparison.Ordinal), "G1 is not an ELD item");

        // **"或"与"且"的区别需要一个只有一件能放一侧的用例**：把 P2 改成不可用，于是 `(E1,P1)` 成立而 `(E2,P2)` 不成立。
        // "或"，这一对仍加行；写成"且"则整对消失。
 // **"or" versus "and" needs a case where only one item fits one side**: make P2 unavailable so `(E1,P1)` holds while `(E2,P2)` does not. This implementation uses "or" and the
        // pair still gets a row; "and" would drop it entirely.
        var halfPositions = new List<StowagePosition> {
            MakePosition("P1", 1UL),
            MakePosition("P2", 2UL, stowageNeeded: false),
        };
        var halfNeighbours = new Dictionary<NeighbourType, IReadOnlyList<Neighbour>> {
            [NeighbourType.Physics] = new List<Neighbour> {
                new(NeighbourType.Physics, new PositionPair(halfPositions[0].AircraftPosition, halfPositions[1].AircraftPosition)),
            },
        };
        var halfRig = new Rig(new List<Item> {
            MakeItem("E1", cargos: new HashSet<CargoType> { Eld }),
            MakeItem("E2", cargos: new HashSet<CargoType> { Eld }),
        }, halfPositions, neighbours: halfNeighbours);
        using LinearMetaModel<Flt64> halfModel = halfRig.RegisterAll();
        new ELDAdjacentLimit(halfRig.Items, halfRig.Positions, halfNeighbours[NeighbourType.Physics], halfRig.Stowage)
            .Invoke(halfModel).IsOk.Should().BeTrue();
        RowNames(halfModel).Should().Contain("eld_adjacent_limit_E1_E2_P1_P2",
            "one item fitting one side is enough: the baseline guard is an or");

        foreach (string name in names) {
            LinearInequalityConstraint<Flt64> row = model.Constraints
                .OfType<LinearInequalityConstraint<Flt64>>()
                .Single(constraint => constraint.Name == name);
            row.Inequality.Comparison.Should().Be(Comparison.LE);
            row.Inequality.Rhs.Constant.ToDouble().Should().Be(1.0);
            row.Inequality.Lhs.Monomials.Should().HaveCount(2);
        }
    }

    /// <summary>
    /// **生物相邻的类型对无方向** / The biological adjacency type pairs are undirected.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. 一件 `AVI` + 一件 `ICE`，限制对是 `(AVI, ICE)`：有行。
    /// 2. 把两件货的**顺序反过来**（`ICE` 在前）：**同样有行**——只写一个朝向会漏掉这种。
    ///
    /// Two paired points: one `AVI` and one `ICE` with the limit pair `(AVI, ICE)` gives rows; and reversing the **cargo order** (`ICE` first) gives rows too — a single
    /// orientation would miss it.
    /// </remarks>
    [Fact]
    public void BiologicalAdjacentLimit_ShouldMatchBothCargoOrders() {
        var biologicalLimit = new BiologicalLimit(
            new List<(CargoType, CargoType)> { (Live, DryIce) },
            new List<(CargoType, CargoType)>(),
            new List<CargoType>());
        var neighbours = new List<Neighbour>();

        foreach (bool reversed in new[] { false, true }) {
            List<Item> items = reversed
                ? new List<Item> {
                    MakeItem("I1", cargos: new HashSet<CargoType> { DryIce }),
                    MakeItem("I2", cargos: new HashSet<CargoType> { Live }),
                }
                : new List<Item> {
                    MakeItem("I1", cargos: new HashSet<CargoType> { Live }),
                    MakeItem("I2", cargos: new HashSet<CargoType> { DryIce }),
                };
            var positions = new List<StowagePosition> { MakePosition("P1", 1UL), MakePosition("P2", 2UL) };
            neighbours = new List<Neighbour> {
                new(NeighbourType.IndirectPhysics, new PositionPair(positions[0].AircraftPosition, positions[1].AircraftPosition)),
            };
            var rig = new Rig(items, positions, biologicalLimit, new Dictionary<NeighbourType, IReadOnlyList<Neighbour>> {
                [NeighbourType.IndirectPhysics] = neighbours,
            });
            using LinearMetaModel<Flt64> model = rig.RegisterAll();

            new BiologicalAdjacentLimit(rig.Items, rig.Positions, neighbours, rig.BiologicalLimit, rig.Stowage)
                .Invoke(model).IsOk.Should().BeTrue();
            RowNames(model).Count(name => name.StartsWith("biological_adjacent_limit_", System.StringComparison.Ordinal))
                .Should().Be(2, "the pair matches in either cargo order, and each match gives two orientations");
        }
    }

    /// <summary>
    /// **生物散货冲突三支都跑，不是互斥** / All three biological bulk-conflict branches run; they are not exclusive.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 空舱位（两支都没装）：**配对行** `≤ 1`。
    /// 2. 已装 `AVI` 与 `ICE` 各一件：第 1、2 支**同时**触发，`ICE` 与 `AVI` 各得一行 `= 0`——这是"三支都跑"的关键。
    ///    `AOGMATBulkConflictLimit` 在那里是互斥的，照那个写会让这里只剩一支的行。
    /// 3. 主甲板与不可用散货舱位**没有行**。
    ///
    /// Three paired points: an empty position (neither type loaded) gets the **pair row** `≤ 1`; a position already holding one `AVI` and one `ICE` fires branches one and two
    /// **at once**, giving a `= 0` row each for `ICE` and `AVI` — the crux of "all three run", where copying `AOGMATBulkConflictLimit`'s exclusivity would leave only one branch's
    /// rows; and main-deck plus unavailable bulk positions get **no** row.
    /// </remarks>
    [Fact]
    public void BiologicalBulkConflictLimit_ShouldRunAllThreeBranches() {
        var biologicalLimit = new BiologicalLimit(
            new List<(CargoType, CargoType)>(),
            new List<(CargoType, CargoType)> { (Live, DryIce) },
            new List<CargoType>());
        var bulkLocation = new PositionLocation(new HashSet<PositionLocationTag> { PositionLocationTag.Bulk });
        var items = new List<Item> {
            MakeItem("I1", location: ItemLocation.LowBulk, cargos: new HashSet<CargoType> { Live }),
            MakeItem("I2", location: ItemLocation.LowBulk, cargos: new HashSet<CargoType> { DryIce }),
        };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, location: bulkLocation),
            MakePosition("P2", 2UL, location: bulkLocation, loadedItems: new List<Item> {
                MakeItem("L1", location: ItemLocation.LowBulk, cargos: new HashSet<CargoType> { Live }),
                MakeItem("L2", location: ItemLocation.LowBulk, cargos: new HashSet<CargoType> { DryIce }),
            }),
            MakePosition("P3", 3UL),
            MakePosition("P4", 4UL, location: bulkLocation, stowageNeeded: false),
            MakePosition("P5", 5UL, location: bulkLocation, loadedItems: new List<Item> {
                MakeItem("L3", location: ItemLocation.LowBulk, cargos: new HashSet<CargoType> { DryIce }),
            }),
        };
        var rig = new Rig(items, positions, biologicalLimit);
        using LinearMetaModel<Flt64> model = rig.RegisterAll();

        new BiologicalBulkConflictLimit(rig.Items, rig.Positions, rig.BiologicalLimit, rig.Stowage).Invoke(model)
            .IsOk.Should().BeTrue();
        IReadOnlyList<string> names = RowNames(model)
            .Where(name => name.StartsWith("biological_bulk_conflict_limit_", System.StringComparison.Ordinal))
            .ToList();

        // 第 3 支：空舱位上是配对行。
        // Branch 3: an empty position gets the pair row.
        names.Should().Contain("biological_bulk_conflict_limit_I1_I2_P1");
        LinearInequalityConstraint<Flt64> pair = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "biological_bulk_conflict_limit_I1_I2_P1");
        pair.Inequality.Rhs.Constant.ToDouble().Should().Be(1.0);
        pair.Inequality.Lhs.Monomials.Should().HaveCount(2);

        // 第 1、2 支同时触发：两种货物各得一行 `= 0`。
        // Branches 1 and 2 fire at once: each cargo type gets a `= 0` row.
        names.Should().Contain("biological_bulk_conflict_limit_I1_P2", "AVI is zeroed because ICE is already loaded");
        names.Should().Contain("biological_bulk_conflict_limit_I2_P2", "ICE is zeroed because AVI is already loaded");
        //
        // **一处不可观测已记录**：把第 2 支的 `holdsType1` 清掉（让第 2 支永不触发）测不出来，因为第 1 支与第 2 支的条件
        // **完全对称**（都是 `holdsType1 && holdsType2`），两个 `= 0` 循环在同一个舱位上同时跑，删掉任一支的守卫都只是让
        // 那一支退化，另一支仍然把同一对货物归零——**两个方向的行名不同，但行集相同**。
        // 要区分需要"只有第 2 支能触发"的舱位，而两个条件对称意味着这种舱位不存在。
        //
        // **One unobservable recorded**: clearing branch two's `holdsType1` (making branch two never fire) is invisible, because branches one and two have **identical**
        // conditions (`holdsType1 && holdsType2`) and both `= 0` loops run on the same position; deleting either guard merely degenerates that branch while the other still zeroes
        // the same pair — **the two directions have different row names but the same row set**. Telling them apart needs a position only branch two can fire on, which the
        // symmetry rules out.
        LinearInequalityConstraint<Flt64> zero = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "biological_bulk_conflict_limit_I1_P2");
        zero.Inequality.Comparison.Should().Be(Comparison.EQ);
        zero.Inequality.Rhs.Constant.ToDouble().Should().Be(0.0);

        // **只有 type1 的舱位**：第 1 支不触发（没有 type2），第 2 支要触发（有 type2? 不——它要求有 type2 且有 type1）。
        // 本仓的三支里第 2 支的条件是 `holdsType2 && holdsType1`，与第 1 支**完全对称**，因此"只把第 1 支留下"的变异在
        // 同时装了两类的舱位上仍然触发第 2 支……除非把 `holdsType2` 清掉。下面这个舱位只装 `ICE`（type2），
        // 于是第 2 支的 `holdsType2 && holdsType1` 为假、第 1 支也为假，两支都不加行——这是对的。
        // **A position holding only type1**: branch one does not fire (no type2) and branch two requires both, so neither fires — correct.
        names.Should().NotContain(name => name.EndsWith("_P5", System.StringComparison.Ordinal),
            "P5 holds only one of the two types, so neither zeroing branch applies");

        // P3 是**主甲板**舱位：两件散货在那里 `StowageNeeded` 为假（散货舱互斥规则），所以"没有行"由 `Enabled` 保证，
        // `location.bulk` 过滤删掉也测不出来——**一处不可观测已记录**。
        // P3 is a **main-deck** position where both bulk items fail `StowageNeeded` (the bulk/non-bulk exclusion), so the absent rows come from `Enabled` and deleting the
        // `location.bulk` filter is invisible — **an unobservable, recorded**.
        names.Should().NotContain(name => name.EndsWith("_P3", System.StringComparison.Ordinal), "P3 is a main-deck position");
        names.Should().NotContain(name => name.EndsWith("_P4", System.StringComparison.Ordinal), "P4 is unavailable");
    }

    /// <summary>
    /// **生成器 13 / 14 条，条件管线与邻接类型各不相同** / The generator gives 13 or 14 pipelines, with distinct conditional tests and neighbour types.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 无条件 **11 条**；预配载模式多一条 `LoadingOrderLimit` → **12 条**。
    /// 2. 需要谓词重量时多一条、需要推荐重量时多一条，且**两个条件是独立的**：只开谓词 → 12 条；只开推荐 → 12 条。
    /// 3. 两条相邻管线用的邻接类型不同：只给 `Physics` 时 ELD 有行、生物相邻没有；只给 `IndirectPhysics` 时相反。
    /// 4. 缺邻接表时**不抛异常**，只是那一族一行都不加。
    ///
    /// Four paired points: **eleven** are unconditional and predistribution adds `LoadingOrderLimit` for **twelve**; enabling the predicate weight adds one and enabling the
    /// recommended weight adds one, with the **two conditions independent** (predicate only → 12, recommended only → 12); the two adjacency pipelines use different neighbour
    /// types (only `Physics` gives ELD rows and no biological ones, only `IndirectPhysics` the reverse); and a missing neighbour table **does not throw**, it merely adds nothing for
    /// that family.
    /// </remarks>
    [Fact]
    public void PipelineListGenerator_ShouldHonourModeAndBothConditions() {
        var items = new List<Item> {
            MakeItem("E1", cargos: new HashSet<CargoType> { Eld }),
            MakeItem("E2", cargos: new HashSet<CargoType> { Eld }),
            MakeItem("I1", cargos: new HashSet<CargoType> { Live }),
            MakeItem("I2", cargos: new HashSet<CargoType> { DryIce }),
        };
        var biologicalLimit = new BiologicalLimit(
            new List<(CargoType, CargoType)> { (Live, DryIce) },
            new List<(CargoType, CargoType)>(),
            new List<CargoType>());

        static IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>> Generate(
            Rig rig, StowageMode mode) {
            Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> result =
                StowagePipelineListGenerator.Invoke(rig.Aggregation, mode);
            result.IsOk.Should().BeTrue();
            return ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)result).Value!;
        }

        // 1. 模式差异。
        // 1. The mode difference.
        var plain = new Rig(items, new List<StowagePosition> { MakePosition("P1", 1UL), MakePosition("P2", 2UL) }, biologicalLimit);
        // **无条件管线是 11 条**（第 4、5 条各按一个标志、第 12 条按模式）。写成 13 是把两个条件管线当成了无条件。
        // **Eleven pipelines are unconditional** (the fourth and fifth gated on a flag, the twelfth on the mode); writing 13 counts the two conditional ones as unconditional.
        Generate(plain, StowageMode.FullLoad).Should().HaveCount(11);
        Generate(plain, StowageMode.Predistribution).Should().HaveCount(12, "predistribution adds the loading order pipeline");

        // 2. 两个条件独立。
        // 2. The two conditions are independent.
        var predicateOnly = new Rig(items, new List<StowagePosition> {
            MakePosition("P1", 1UL, predicateWeightNeeded: true),
            MakePosition("P2", 2UL),
        }, biologicalLimit);
        Generate(predicateOnly, StowageMode.FullLoad).Should().HaveCount(12, "the predicate pipeline is added");

        var recommendOnly = new Rig(items, new List<StowagePosition> {
            MakePosition("P1", 1UL, recommendedWeightNeeded: true),
            MakePosition("P2", 2UL),
        }, biologicalLimit);
        Generate(recommendOnly, StowageMode.FullLoad).Should().HaveCount(12, "the recommend pipeline is added");

        // 3. 邻接类型不同：只给 Physics 时 ELD 有行、生物相邻没有。
        // 3. Distinct neighbour types: with Physics only, ELD emits rows and the biological adjacency does not.
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL), MakePosition("P2", 2UL) };
        var physicsOnly = new Rig(items, positions, biologicalLimit, new Dictionary<NeighbourType, IReadOnlyList<Neighbour>> {
            [NeighbourType.Physics] = new List<Neighbour> {
                new(NeighbourType.Physics, new PositionPair(positions[0].AircraftPosition, positions[1].AircraftPosition)),
            },
        });
        using (LinearMetaModel<Flt64> model = physicsOnly.RegisterAll()) {
            foreach (Func<LinearMetaModel<Flt64>, Try> pipeline in Generate(physicsOnly, StowageMode.FullLoad)) {
                pipeline(model).IsOk.Should().BeTrue();
            }
            IReadOnlyList<string> names = RowNames(model);
            names.Should().Contain(name => name.StartsWith("eld_adjacent_limit_", System.StringComparison.Ordinal),
                "ELD uses Physics");
            names.Should().NotContain(name => name.StartsWith("biological_adjacent_limit_", System.StringComparison.Ordinal),
                "the biological adjacency uses IndirectPhysics, which is absent here");
        }

        // 4. 缺邻接表不抛异常：全配载模式仍能跑完。
        // 4. A missing neighbour table does not throw: full-load mode still runs to completion.
        using (LinearMetaModel<Flt64> model = plain.RegisterAll()) {
            foreach (Func<LinearMetaModel<Flt64>, Try> pipeline in Generate(plain, StowageMode.FullLoad)) {
                pipeline(model).IsOk.Should().BeTrue("an empty neighbour list is not an error");
            }
            RowNames(model).Should().NotContain(name => name.StartsWith("eld_adjacent_limit_", System.StringComparison.Ordinal));
            RowNames(model).Should().NotContain(name => name.StartsWith("biological_adjacent_limit_", System.StringComparison.Ordinal));
        }
    }
}
