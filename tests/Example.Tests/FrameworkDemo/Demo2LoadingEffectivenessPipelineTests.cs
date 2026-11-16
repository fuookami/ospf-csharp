// 装载效能管线编排的判据 / Criteria for the loading-effectiveness pipeline ordering.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Service;
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
using Xunit;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **装载效能管线编排** / The loading-effectiveness pipeline ordering.
/// </summary>
/// <remarks>
/// 判据的重点：**三条条件管线各自的条件**、**`reweighNeeded` 恒假**这个基线怪癖、以及**七条无法构建的管线被显式列出**。
///
/// The criteria focus on **each conditional pipeline's condition**, the code quirk that **`reweighNeeded` is always false**, and **the seven unbuildable pipelines being listed
/// explicitly**.
/// </remarks>
public class Demo2LoadingEffectivenessPipelineTests {
    private static Item MakeItem(string name, string source, string destination, ItemOrder? order = null) =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority("P1", new Fuookami.Ospf.Math.Algebra.Number.UInt64(1UL), CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: source,
            destination: destination,
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck,
            order: order);

    private static StowagePosition MakePosition(string name, ulong id, byte order) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "N",
            linearLoadingOrder: order,
            coordinate: new PositionCoordinate(100.0 * id, 100.0 * id + 100.0, -60.0, -10.0),
            shape: new PositionShape(100.0, 50.0, 5000.0),
            location: PositionLocation.NormalMainLocation);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(true, false, false, false),
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: 3UL);
    }

    private static StowageModel MakeStowage(
        IReadOnlyList<Item> items, IReadOnlyList<StowagePosition> positions) {
        var allowed = new HashSet<AssignmentKey>();
        foreach (Item item in items) {
            foreach (StowagePosition position in positions) {
                if (StowageModel.StowageNeeded(item, position)) {
                    allowed.Add(new AssignmentKey(item.Id, position.Name));
                }
            }
        }
        var problem = new DiscreteLoadingProblem<Flt64>(
            Items: items.Select(item => new LoadingItem<Flt64>(
                item.Id, new Quantity<Flt64>(new Flt64(item.Weight), MassUnits.Gram))).ToList(),
            Positions: positions.Select(position => new LoadingPosition<Flt64>(
                position.Name, position.Mla,
                new Quantity<Flt64>(new Flt64(position.Mlw.Mlw), MassUnits.Gram))).ToList(),
            AllowedAssignments: allowed,
            Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
            LayoutGroups: new List<LayoutSelectionGroup>(),
            WeightUnit: MassUnits.Gram);
        return new StowageModel(items, positions) {
            LoadingContext = new DiscreteLoadingContext<Flt64>(problem, new Flt64PackingValueAdapter()),
        };
    }

    private static LoadingEffectivenessAggregation MakeAggregation(
        StowageMode mode,
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        Flight? flight = null,
        IReadOnlyDictionary<string, IReadOnlyList<int>>? cargosBySource = null) =>
        new(mode, items, positions, MakeStowage(items, positions), flight: flight, cargosBySource: cargosBySource);

    private static int Count(LoadingEffectivenessAggregation aggregation, StowageMode mode) {
        Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> generated =
            new PipelineListGenerator(aggregation).Invoke(mode, new Parameter());
        generated.IsOk.Should().BeTrue();
        return ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)generated).Value!.Count;
    }

    /// <summary>
    /// **`reweighNeeded` 恒为 `false`，因为站点集合是空的** / `reweighNeeded` is always false, since its station set is empty.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 预配载下**也是 `false`**——集合为空，`contains` 恒假。所以 `ItemReweighNeededLimit` 在**永不触发**。
    /// 2. 其余两种模式恒 `false`（基线写死）。
    /// 3. 配置入口**保留着**：静态集合是空的但可变，不是把 `false` 硬编码进判定。
    ///
    /// Three paired points: under predistribution it is **also false**, because the empty set never contains anything, so `ItemReweighNeededLimit` **never fires** in the code; the
    /// other two modes are hard-coded false; and **the knob is kept** — the static set is empty but mutable, rather than `false` being hard-coded into the test.
    /// </remarks>
    [Fact]
    public void Flight_ReweighNeededShouldAlwaysBeFalse() {
        var flight = new Flight(new FlightNo("CA1"), new IATACode("PEK"), new IATACode("SHA"));
        Flight.ReweighNeededStations.Should().BeEmpty("the baseline's set is empty");
        flight.ReweighNeeded(StowageMode.Predistribution).Should().BeFalse(
            "an empty station set never matches, so the reweigh pipeline never fires");
        flight.ReweighNeeded(StowageMode.FullLoad).Should().BeFalse();
        flight.ReweighNeeded(StowageMode.WeightRecommendation).Should().BeFalse();
    }

    /// <summary>
    /// **`Register` 走生成器，产出与生成器一致的目标** / `Register` goes through the generator and produces the same objectives.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 预配载下跑完 `Register`，模型里有**两个**目标（提前装载 + 预留）。
    /// 2. 两个目标名都是**带空格**的（`"item ahead load"`、`"item reserve"`）。
    /// 3. 全载下跑完 **0 个**目标——两条货物目标都是预配载专属。
    ///
    /// Three paired points: after `Register` under predistribution the model holds **two** objectives (ahead-load and reserve); both names **carry a space**; and under full load there
    /// are **zero** objectives, since both item objectives are predistribution-only.
    /// </remarks>
    [Fact]
    public void Register_ShouldRouteThroughTheGenerator() {
        var items = new List<Item> {
            MakeItem("A", "PEK", "SHA", new ItemOrder { Hardstand = 1 }), MakeItem("B", "PEK", "CAN"),
        };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        var sources = new Dictionary<string, IReadOnlyList<int>> { ["PEK"] = new[] { 0, 1 } };

        // `Stowage.Register` 对同一实例不可重复调用，所以每种模式各建一个聚合。
        // `Stowage.Register` cannot run twice on one instance, so each mode gets its own aggregation.
        LoadingEffectivenessAggregation pre =
            MakeAggregation(StowageMode.Predistribution, items, positions, cargosBySource: sources);
        using var preModel = new LinearMetaModel<Flt64>();
        pre.Stowage.Register(preModel).IsOk.Should().BeTrue();
        pre.Register(StowageMode.Predistribution, new Parameter(), preModel).IsOk.Should().BeTrue();
        preModel.MetaSubObjects.Should().HaveCount(2, "ahead-load and reserve are both predistribution-only");
        preModel.MetaSubObjects.Select(entry => entry.Name)
            .Should().BeEquivalentTo(new[] { "item ahead load", "item reserve" });

        LoadingEffectivenessAggregation full =
            MakeAggregation(StowageMode.FullLoad, items, positions, cargosBySource: sources);
        using var fullModel = new LinearMetaModel<Flt64>();
        full.Stowage.Register(fullModel).IsOk.Should().BeTrue();
        full.Register(StowageMode.FullLoad, new Parameter(), fullModel).IsOk.Should().BeTrue();
        fullModel.MetaSubObjects.Should().BeEmpty("no item objective applies to full load");
    }
}
