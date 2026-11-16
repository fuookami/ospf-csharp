// 配载约束管线的判据（此前）/ Criteria for the stowage constraint pipelines, second batch.

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
/// **配载约束管线此前** / The second batch of stowage constraint pipelines.
/// </summary>
/// <remarks>
/// 四个类，四组"三支/两支/两两组合"的分支结构。判据的重点是**分支边界**：哪个分支该触发、哪个不该，以及**组合不是排列**。
///
/// Four classes, four branch structures (three-way, two-way, pairwise). The criteria focus on **branch boundaries** — which fires, which must not — and on **combinations
/// rather than permutations**.
/// </remarks>
public class Demo2StowageLimit2Tests {
    private static Quantity<Flt64> Grams(double value) => new(new Flt64(value), MassUnits.Gram);

    private static Item MakeItem(
        string name,
        ItemStatus status = ItemStatus.Optional,
        ItemLocation? location = null,
        IReadOnlySet<CargoType>? cargos = null,
        string destination = "SHA") =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: "PEK",
            destination: destination,
            status: status,
            location: location ?? ItemLocation.MainDeck,
            cargos: cargos);

    // 散货货物必须声明散货**货位**（`ItemLocation.LowBulk`），否则散货舱会因"非散货舱禁止装载散货物/散货舱禁止非散货物"把它拒掉，
    // `StowageNeeded` 为假，整条管线一行都加不出来。
    // A bulk item must declare a **bulk location** (`ItemLocation.LowBulk`), or the bulk hold refuses it via the bulk/non-bulk exclusion, `StowageNeeded` is false, and the
    // pipeline emits nothing at all.
    private static ItemLocation BulkLocation => ItemLocation.LowBulk;

    private static StowagePosition MakePosition(
        string name,
        ulong id,
        double capacity = 1000.0,
        ulong mla = 3UL,
        bool available = true,
        bool stowageNeeded = true,
        bool adjustmentNeeded = false,
        bool predicateWeightNeeded = false,
        bool recommendedWeightNeeded = false,
        PositionLocation? location = null,
        PositionType? type = null,
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
            status: new PositionStatus(stowageNeeded, adjustmentNeeded, predicateWeightNeeded, recommendedWeightNeeded),
            mlw: new MaxLoadWeightLimit(capacity),
            mla: mla,
            loadedItems: loadedItems,
            plw: predicateWeightNeeded ? new PositionPredicateLoadWeight(0.0, 0.0, capacity) : null,
            type: type);
    }

    private sealed class Rig
    {
        public Rig(IReadOnlyList<Item> items, IReadOnlyList<StowagePosition> positions) {
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
                new Dictionary<FlightPhase, FuelConstant> {
                    [FlightPhase.TakeOff] = new FuelConstant(Density: 0.8, Weight: 1000.0, Index: 1000.0),
                    [FlightPhase.Landing] = new FuelConstant(Density: 0.8, Weight: 2000.0, Index: 2000.0),
                },
                Payload);
            MaxLoadWeight = new MaxLoadWeight(Positions, TotalWeight);
        }

        public IReadOnlyList<Item> Items { get; }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public StowageModel Stowage { get; }

        public Load Load { get; }

        public Payload Payload { get; }

        public TotalWeight TotalWeight { get; }

        public MaxLoadWeight MaxLoadWeight { get; }

        public LinearMetaModel<Flt64> RegisterAll() {
            var model = new LinearMetaModel<Flt64>();
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
    /// **推荐重量行是耦合行，两个条件都要查** / The recommended weight row couples `z` with the actual load, and both conditions matter.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 需要决策**且**需要推荐重量的舱位有一行，比较符 `LE`，右端常数是 `mlw`。
    /// 2. 左端**两个**单式：`z_j` 系数 1、`actualLoaded_j` 系数 `mlw`。只加 `z_j` 就是漏了耦合。
    /// 3. 需要决策但**不需要**推荐重量、以及不需要决策但需要推荐重量的舱位，都**没有行**。
    ///
    /// 第 2 点是这条约束的全部意义：`z ≤ mlw` 单独成立时推荐量仍会顶到上限，而真实装载已经占了一部分。
    ///
    /// Three paired points: a position that both takes a decision **and** needs a recommended weight gets a row with `LE` and `mlw` on the right; the left has **two** monomials —
    /// `z_j` with coefficient 1 and `actualLoaded_j` with coefficient `mlw` (a lone `z_j` drops the coupling); and positions missing either condition get **no** row. Point two is
    /// the whole meaning: `z ≤ mlw` alone lets the recommendation reach the cap while the actual load already occupies part of it.
    /// </remarks>
    [Fact]
    public void RecommendLoadWeightLimit_ShouldCoupleZWithTheActualLoad() {
        var items = new List<Item> { MakeItem("I1") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, capacity: 700.0, recommendedWeightNeeded: true),
            MakePosition("P2", 2UL, capacity: 900.0, recommendedWeightNeeded: true, stowageNeeded: false),
            MakePosition("P3", 3UL, capacity: 800.0, recommendedWeightNeeded: false),
        };
        var rig = new Rig(items, positions);
        using LinearMetaModel<Flt64> model = rig.RegisterAll();

        new RecommendLoadWeightLimit(rig.Positions, rig.Load).Invoke(model).IsOk.Should().BeTrue();
        IReadOnlyList<string> names = RowNames(model);
        names.Should().Contain("recommend_load_weight_limit_P1");
        names.Should().NotContain("recommend_load_weight_limit_P2", "P2 takes no decision");
        names.Should().NotContain("recommend_load_weight_limit_P3", "P3 needs no recommended weight");

        LinearInequalityConstraint<Flt64> row = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "recommend_load_weight_limit_P1");
        row.Inequality.Comparison.Should().Be(Comparison.LE);
        row.Inequality.Rhs.Constant.ToDouble().Should().Be(700.0, "the right-hand side is P1's mlw");
        row.Inequality.Lhs.Monomials.Should().HaveCount(2, "z_j and actualLoaded_j are both on the left");
        double zCoefficient = row.Inequality.Lhs.Monomials
            .Where(monomial => monomial.Symbol.Name.StartsWith("z_", System.StringComparison.Ordinal))
            .Sum(monomial => monomial.Coefficient.ToDouble());
        zCoefficient.Should().BeApproximately(1.0, 1e-9);
        double actualCoefficient = row.Inequality.Lhs.Monomials
            .Where(monomial => !monomial.Symbol.Name.StartsWith("z_", System.StringComparison.Ordinal))
            .Sum(monomial => monomial.Coefficient.ToDouble());
        actualCoefficient.Should().BeApproximately(700.0, 1e-9, "the actual load is scaled by mlw");
    }

    /// <summary>
    /// **普通散货两两最多一件，且是组合不是排列** / At most one normal bulk item per pair, as combinations not permutations.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 两件普通散货在同一散货舱：**恰好一行**（三件货两两组合 = 3 行，不是 6 行）。
    /// 2. 航材（AOG）参与的配对**没有行**——那是 `AOGMATBulkConflictLimit` 的事。
    /// 3. `AOGMATAppointed` 舱位、以及已装货的舱位**没有行**。
    ///
    /// 第 1 点用三件货：写成排列会有 6 行，组合是 3 行，差值直接暴露。
    ///
    /// Three paired points: two normal bulk items in the same bulk hold give **exactly one row** (three items pairwise = 3 rows, not 6); a pair involving AOG has **no** row —
    /// that is `AOGMATBulkConflictLimit`'s job; and `AOGMATAppointed` positions plus positions already holding cargo get **no** row. Point one uses three items: permutations give
    /// 6 rows and combinations 3, so the difference is immediate.
    /// </remarks>
    [Fact]
    public void NormalBulkDestinationAssignmentLimit_ShouldUseCombinations() {
        var aog = CargoType.FromName("AOG");
        var items = new List<Item> {
            MakeItem("B1", location: BulkLocation),
            MakeItem("B2", location: BulkLocation),
            MakeItem("B3", location: BulkLocation),
            MakeItem("A1", location: BulkLocation, cargos: new HashSet<CargoType> { aog }),
        };
        var bulkLocation = new PositionLocation(new HashSet<PositionLocationTag> { PositionLocationTag.Bulk });
        // P2 用**普通**散货舱位配 `AOGMATAppointed` 类型码：这样 B1/B2 的 `StowageNeeded` 仍为真（`Enabled` 只在货物是航材时才放行
        // `AOGMATAppointed`，普通散货会被拒），所以这一对**本来就加不出行**，删掉过滤条件也测不出来。因此 P2 改成
        // `NormalBulkAppointed`——那才是"该被过滤条件挡住、而 `StowageNeeded` 为真"的唯一组合。
        // P2 pairs a **normal** bulk hold with the `AOGMATAppointed` code: `StowageNeeded` for B1/B2 is then false anyway (`Enabled` only admits AOG cargo there), so that pair
        // emits nothing and dropping the filter is invisible. P2 therefore uses `NormalBulkAppointed`, the only code where the filter is what stops the row while
        // `StowageNeeded` is true.
        var appointed = new PositionType(new[] { PositionTypeCode.NormalBulkAppointed });
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, location: bulkLocation),
            MakePosition("P2", 2UL, location: bulkLocation, type: appointed),
            MakePosition("P3", 3UL, location: bulkLocation, loadedItems: new List<Item> { MakeItem("L1", location: BulkLocation) }),
        };
        var rig = new Rig(items, positions);
        using LinearMetaModel<Flt64> model = rig.RegisterAll();

        new NormalBulkDestinationAssignmentLimit(rig.Items, rig.Positions, rig.Stowage).Invoke(model).IsOk.Should().BeTrue();
        IReadOnlyList<string> names = RowNames(model)
            .Where(name => name.StartsWith("normal_bulk_destination_assignment_limit_", System.StringComparison.Ordinal))
            .ToList();

        // 两个可用舱位（P1 普通散货、P2 普散货指定）× C(3,2) = 6 行。写成排列会是 12 行。
        // Two usable positions (P1 plain bulk, P2 normal-bulk-appointed) × C(3,2) = 6 rows; permutations would give 12.
        names.Should().HaveCount(6, "two usable positions times C(3,2) combinations, not permutations");
        names.Should().Contain("normal_bulk_destination_assignment_limit_B1_B2_P1");
        names.Should().Contain("normal_bulk_destination_assignment_limit_B1_B3_P1");
        names.Should().Contain("normal_bulk_destination_assignment_limit_B2_B3_P1");
        names.Should().Contain("normal_bulk_destination_assignment_limit_B1_B2_P2");
        names.Should().Contain("normal_bulk_destination_assignment_limit_B1_B3_P2");
        names.Should().Contain("normal_bulk_destination_assignment_limit_B2_B3_P2");
        names.Should().NotContain(name => name.Contains("A1", System.StringComparison.Ordinal),
            "AOG cargo is AOGMATBulkConflictLimit's business");
        // P2 是普散货指定舱位：普通散货在那里**允许**，所以行存在。
        // P2 is normal-bulk-appointed, where normal bulk **is** allowed, so its rows exist.
        //
        // **一处不可观测已记录**：`AOGMATAppointed` 这个过滤条件在本仓删掉也测不出来——`Enabled` 对 `AOGMATAppointed` 舱位直接拒绝
        // 非航材货物，于是普通散货的 `StowageNeeded` 恒为假，前一行 `!StowageNeeded` 已经把它挡住。也就是说本仓里
        // "航材指定舱位不放普通散货"由 `Enabled` 负责，这条过滤是**防御性的重复**。基线同样如此（`enabled` 有同一组规则）。
        // **One unobservable recorded**: deleting the `AOGMATAppointed` filter is invisible here — `Enabled` refuses non-AOG cargo outright at such a position, so normal
        // bulk's `StowageNeeded` is identically false and the preceding `!StowageNeeded` already rejects it. In this port "an AOG/MAT-appointed position takes no normal bulk"
        // is `Enabled`'s job and this filter is a **defensive duplicate** — the baseline is the same, its `enabled` carrying the same rules.
        names.Should().NotContain(name => name.Contains("_P3", System.StringComparison.Ordinal),
            "a position already holding cargo is excluded");

        LinearInequalityConstraint<Flt64> row = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "normal_bulk_destination_assignment_limit_B1_B2_P1");
        row.Inequality.Comparison.Should().Be(Comparison.LE);
        row.Inequality.Rhs.Constant.ToDouble().Should().Be(1.0);
        row.Inequality.Lhs.Monomials.Should().HaveCount(2);
    }

    /// <summary>
    /// **航材与散货：第三支可达，前两支被 `Enabled` 抢先** / AOG versus bulk: branch three is reachable, branches one and two are pre-empted by `Enabled`.
    /// </summary>
    /// <remarks>
    /// 这是这里最重要的发现，**不是本移植的退让，是基线的性质**：
    ///
    /// `AOGMATBulkConflictLimit` 的前两支都要求 `Stowage.stowageNeeded(item, position)` 成立，而 `stowageNeeded` 又要求
    /// `position.enabled(item).ok`。但 `Position.enabled` **已经**包含三条规则：
    ///
    /// | `Enabled` 的规则 | 抢先封死的支 |
    /// | --- | --- |
    /// | 航材指定舱位只能装载航材 | 第 1 支（该舱位的普通散货） |
    /// | 普散货指定舱位只能装载普通散货 | 第 2 支（该舱位的航材） |
    /// | 已装航材的散舱不能装载普通散货 | 第 1 支（已装航材的舱位） |
    /// | 已装普通散货的散舱不能装载航材 | 第 2 支（已装普通散货的舱位） |
    ///
    /// 于是前两支的 `stowageNeeded` 恒为假，**永远加不出行**——基线里也一样，因为它的 `enabled` 有同一组规则。
    /// 照抄，并把这件事写在这里：若将来有人"清理死代码"删掉前两支，行为不变；若有人删掉 `Enabled` 里的某条规则，
    /// 对应的支会立刻活过来，而两条路径的语义并不相同（一条是变量固定为 0，一条是组合根本不存在）。
    ///
    /// 判据因此只断言：**第三支的配对行存在且是 `≤ 1`**，以及前两支对应的舱位**没有行**（记录它们的不可达）。
    ///
    /// This is the batch's most important finding, and **it is the baseline's property, not a concession of this port**. Both of the first two branches require
    /// `Stowage.stowageNeeded(item, position)`, which requires `position.enabled(item).ok`, and `Position.enabled` **already** carries the four rules tabulated above — so
    /// `stowageNeeded` is identically false there and those branches **never emit a row**, in the baseline too since its `enabled` has the same rules. Copied as-is and
    /// recorded: deleting the dead branches would change nothing, while deleting a rule from `Enabled` would bring the matching branch to life with different semantics (a
    /// variable pinned to zero versus a combination that does not exist at all). The criteria therefore assert only that **branch three's pair row exists and is `≤ 1`**, and that
    /// the positions matching branches one and two get **no** row, recording their unreachability.
    /// </remarks>
    [Fact]
    public void AOGMATBulkConflictLimit_ShouldOnlyReachTheThirdBranch() {
        var aog = CargoType.FromName("AOG");
        // **航材必须排在普通散货之前**。第 3 支写的是 `for i1... for i2 in (i1+1) until size`，且**只有 `item1` 被要求是航材**
        // （那个 `!IsAogMat(item1)` 的守卫本身不可观测：`item1` 若不含 AOG/MAT，`item2` 那侧的对称条件会把整对跳过，
        // 加不加这一条得到的行集相同——基线里也一样，是防御性重复）：
        // 于是这一对只在下标 `i(AOG) < i(bulk)` 时才会被枚举到。反过来排（普通散货在前、航材在后）那一对**永远不会加行**——
        // 这是基线的真实性质，不是本移植的 bug。判据按可达的顺序构造，并在此记录这个不对称。
        // **The AOG item must come before the normal bulk one.** Branch three is `for i1... for i2 in (i1+1) until size` with **only `item1` required to be AOG**, so a pair is
        // enumerated only when `index(AOG) < index(bulk)`. With the opposite order (bulk first, AOG after) that pair **never gets a row** — the baseline's real behaviour, not a
        // bug of this port. The criteria build the reachable order and record the asymmetry here.
        var items = new List<Item> {
            MakeItem("A1", location: BulkLocation, cargos: new HashSet<CargoType> { aog }),
            MakeItem("B1", location: BulkLocation),
            // 主甲板的一对：两件都在主甲板，`Enabled` 都放行，只有 `location.bulk` 过滤能挡住它们。
            // A main-deck pair: both items are main deck and `Enabled` admits them, so only the `location.bulk` filter can exclude them.
            MakeItem("A2", cargos: new HashSet<CargoType> { aog }),
            MakeItem("B2"),
        };
        var bulkLocation = new PositionLocation(new HashSet<PositionLocationTag> { PositionLocationTag.Bulk });
        var aogAppointed = new PositionType(new[] { PositionTypeCode.AOGMATAppointed });
        var bulkAppointed = new PositionType(new[] { PositionTypeCode.NormalBulkAppointed });
        var positions = new List<StowagePosition> {
            // P1 装过航材：第 1 支的条件成立，但 `Enabled` 已拒普通散货，`StowageNeeded` 为假。
            // P1 already holds AOG: branch one's condition holds, yet `Enabled` refuses normal bulk so `StowageNeeded` is false.
            MakePosition("P1", 1UL, location: bulkLocation, loadedItems: new List<Item> {
                MakeItem("L1", location: BulkLocation, cargos: new HashSet<CargoType> { aog }),
            }),
            MakePosition("P2", 2UL, location: bulkLocation, type: bulkAppointed),
            MakePosition("P3", 3UL, location: bulkLocation),
            MakePosition("P4", 4UL),
            MakePosition("P5", 5UL, location: bulkLocation, stowageNeeded: false),
            MakePosition("P6", 6UL, location: bulkLocation, type: aogAppointed),
        };
        var rig = new Rig(items, positions);
        using LinearMetaModel<Flt64> model = rig.RegisterAll();

        new AOGMATBulkConflictLimit(rig.Items, rig.Positions, rig.Stowage).Invoke(model).IsOk.Should().BeTrue();
        IReadOnlyList<string> names = RowNames(model)
            .Where(name => name.StartsWith("aot_mat_bulk_conflict_limit_", System.StringComparison.Ordinal))
            .ToList();

        // 第三支：P3 上是**配对行** `≤ 1`，不是单个 `= 0`。
        // Branch three: P3 gets a **pair row** `≤ 1`, not a single `= 0`.
        names.Should().Contain("aot_mat_bulk_conflict_limit_A1_B1_P3");
        LinearInequalityConstraint<Flt64> pair = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "aot_mat_bulk_conflict_limit_A1_B1_P3");
        pair.Inequality.Comparison.Should().Be(Comparison.LE);
        pair.Inequality.Rhs.Constant.ToDouble().Should().Be(1.0);
        pair.Inequality.Lhs.Monomials.Should().HaveCount(2);

        // 前两支不可达：对应舱位一行都没有。
        // Branches one and two are unreachable: their positions get no row at all.
        names.Should().NotContain(name => name.EndsWith("_P1", System.StringComparison.Ordinal),
            "Enabled already refuses normal bulk at a hold loaded with AOG");
        names.Should().NotContain(name => name.EndsWith("_P2", System.StringComparison.Ordinal),
            "Enabled already refuses AOG at a normal-bulk-appointed hold");
        names.Should().NotContain(name => name.EndsWith("_P6", System.StringComparison.Ordinal),
            "Enabled already refuses normal bulk at an AOG/MAT-appointed hold");

        // **一处不可观测已记录**：删掉 `!position.Location.Bulk` 也测不出来。`Enabled` 有一条"非散货舱禁止装载散货装载物"
        // 与"散货舱禁止装载非散货装载物"，而第 3 支要求 `item1` 是航材、`item2` 是散货——主甲板舱位对 `item2` 的 `StowageNeeded`
        // 因此为假，配对行本来就加不出来。这条过滤同样是**防御性的重复**。
        // **One unobservable recorded**: deleting `!position.Location.Bulk` is invisible too. `Enabled` excludes bulk cargo from non-bulk positions and vice versa, while branch
        // three needs `item1` to be AOG and `item2` to be bulk — a main-deck position therefore fails `StowageNeeded` for `item2` and the pair row cannot appear anyway. This
        // filter is likewise a **defensive duplicate**.
        names.Should().NotContain(name => name.EndsWith("_P4", System.StringComparison.Ordinal),
            "P4 is a main-deck position and gets no pair row either way");
        names.Should().NotContain(name => name.EndsWith("_P5", System.StringComparison.Ordinal), "P5 is unavailable");
    }

    /// <summary>
    /// **装载顺序只给可用非散货舱位** / The loading order rows cover available non-bulk positions only.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 两个可用非散货舱位之间有一行，比较符是 `GE`（先装的不少于后装的）。
    /// 2. 任一端是散货舱：**没有行**。
    /// 3. 任一端不可用：**没有行**。
    /// 4. 邻接引用未知舱位：**失败**，不是跳过。
    ///
    /// 第 2 点是最容易漏的：散货舱没有顺序，加行在物理上不可能被违反，只会白占约束表。
    ///
    /// Four paired points: an available non-bulk pair gets a `GE` row (the earlier position carries no less than the later); any bulk end gets **no** row; any unavailable end gets
    /// **no** row; and an adjacency naming an unknown position **fails** rather than being skipped. Point two is the easiest to drop: a bulk hold has no order, so the row can
    /// never be violated physically and only fills the table.
    /// </remarks>
    [Fact]
    public void LoadingOrderLimit_ShouldSkipBulkAndUnavailablePositions() {
        var items = new List<Item> { MakeItem("I1") };
        var bulkLocation = new PositionLocation(new HashSet<PositionLocationTag> { PositionLocationTag.Bulk });
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL),
            MakePosition("P2", 2UL),
            MakePosition("P3", 3UL, location: bulkLocation),
            MakePosition("P4", 4UL, stowageNeeded: false),
        };
        var neighbours = new List<Neighbour> {
            new(NeighbourType.TopologicalLoadingOrder, new PositionPair(positions[0].AircraftPosition, positions[1].AircraftPosition)),
            new(NeighbourType.TopologicalLoadingOrder, new PositionPair(positions[0].AircraftPosition, positions[2].AircraftPosition)),
            new(NeighbourType.TopologicalLoadingOrder, new PositionPair(positions[0].AircraftPosition, positions[3].AircraftPosition)),
        };
        var rig = new Rig(items, positions);
        using LinearMetaModel<Flt64> model = rig.RegisterAll();

        new LoadingOrderLimit(rig.Positions, neighbours, rig.Load).Invoke(model).IsOk.Should().BeTrue();
        IReadOnlyList<string> names = RowNames(model);
        names.Should().Contain("loading_order_limit_P1_P2");
        names.Should().NotContain("loading_order_limit_P1_P3", "P3 is a bulk hold and has no loading order");
        names.Should().NotContain("loading_order_limit_P1_P4", "P4 is unavailable");

        LinearInequalityConstraint<Flt64> row = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "loading_order_limit_P1_P2");
        row.Inequality.Comparison.Should().Be(Comparison.GE, "the earlier position carries no less than the later");
        row.Inequality.Lhs.Monomials.Should().ContainSingle();
        row.Inequality.Rhs.Monomials.Should().ContainSingle();

        // 邻接引用未知舱位：明确失败。
        // An adjacency naming an unknown position: an explicit failure.
        var outsider = new AircraftPosition(
            id: 99UL, spaceName: "P9", sizeCode: "A", linearLoadingOrder: 9,
            coordinate: new PositionCoordinate(5000.0, 5100.0, -40.0, -40.0),
            shape: new PositionShape(50.0, 40.0, 50.0 * 40.0 * 60.0),
            location: PositionLocation.NormalMainLocation);
        var badNeighbours = new List<Neighbour> {
            new(NeighbourType.TopologicalLoadingOrder, new PositionPair(positions[0].AircraftPosition, outsider)),
        };
        Result<Success, ErrorCode, Error<ErrorCode>> bad =
            new LoadingOrderLimit(rig.Positions, badNeighbours, rig.Load).Invoke(model);
        bad.IsOk.Should().BeFalse("the adjacency names a position outside the list");
        (bad as Failed<Success, ErrorCode, Error<ErrorCode>>)!.Error.Message.Should().Contain("未知舱位");

        // **第二条分支也要单独钉**：只测 `j1 < 0` 时，删掉 `j2 < 0` 的守卫仍然会红（因为 `j1` 已经拦下），等于没覆盖。
        // **The second branch needs its own case**: testing only `j1 < 0` still goes red when the `j2 < 0` guard is deleted (the first one already rejected), covering nothing.
        var badSecond = new List<Neighbour> {
            new(NeighbourType.TopologicalLoadingOrder, new PositionPair(outsider, positions[1].AircraftPosition)),
        };
        Result<Success, ErrorCode, Error<ErrorCode>> badSecondResult =
            new LoadingOrderLimit(rig.Positions, badSecond, rig.Load).Invoke(model);
        badSecondResult.IsOk.Should().BeFalse("the second endpoint is outside the list");
        (badSecondResult as Failed<Success, ErrorCode, Error<ErrorCode>>)!.Error.Message.Should().Contain("未知舱位");
    }
}
