// 冗余上下文（B627）的判据 / Criteria for the redundancy context (B627).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
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
/// **冗余上下文（B627）** / The redundancy context (B627).
/// </summary>
/// <remarks>
/// 判据的重点：**只在预配载给管线**、未初始化时报错、以及两个目标名与类别。
///
/// The criteria focus on **pipelines existing only under predistribution**, erroring when uninitialised, and the two objectives' names and categories.
/// </remarks>
public class Demo2RedundancyContextTests {
    private static Item MakeItem(string name) =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority("P1", new Fuookami.Ospf.Math.Algebra.Number.UInt64(1UL), CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck);

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

    private static RequestDTO Request() =>
        new("REQ",
            new List<CargoInput> { new("A", 100.0, 1, "PEK", "SHA"), new("B", 100.0, 1, "PEK", "SHA") },
            new List<PositionInput> { new("P1", 1000.0, 100.0, 0.0), new("P2", 1000.0, 200.0, 0.0) });

    private static (StowageModel Stowage, Load Load, List<Item> Items, List<StowagePosition> Positions) Rig() {
        var items = new List<Item> { MakeItem("A"), MakeItem("B") };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        StowageModel stowage = MakeStowage(items, positions);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true);
        return (stowage, load, items, positions);
    }

    /// <summary>
    /// **只在预配载给目标；其余模式一条都不给** / Objectives exist only under predistribution; every other mode gives none.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. **预配载** → 两条目标，名字是 `"experimental longitudinal balance"` 与 `"redundancy"`。
    /// 2. **全载** → **零条**目标，且返回成功（不是失败）。
    /// 3. **建议打板** → **零条**目标。这一点容易写成"少加几条"，基线是**一条都不加**。
    ///
    /// Three paired points: **predistribution** gives two objectives named `"experimental longitudinal balance"` and `"redundancy"`; **full load** gives **zero** objectives and still
    /// succeeds; and **weight recommendation** gives **zero** — easy to write as "fewer pipelines", but the baseline adds **none**.
    /// </remarks>
    [Fact]
    public void PipelineListGenerator_ShouldOnlyFireDuringPredistribution() {
        (StowageModel Stowage, Load Load, List<Item> Items, List<StowagePosition> Positions) rig = Rig();
        var aggregation = new RedundancyAggregation(rig.Items, rig.Positions, rig.Load);
        var generator = new PipelineListGenerator(aggregation);

        foreach (StowageMode mode in new[] { StowageMode.FullLoad, StowageMode.WeightRecommendation }) {
            Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> found =
                generator.Invoke(mode, new Parameter());
            found.IsOk.Should().BeTrue();
            ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)found)
                .Value!.Should().BeEmpty("redundancy pipelines only exist under predistribution, not " + mode);
        }

        Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> predistribution =
            generator.Invoke(StowageMode.Predistribution, new Parameter());
        predistribution.IsOk.Should().BeTrue();
        IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>> pipelines =
            ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)predistribution).Value!;
        pipelines.Should().HaveCount(2);
    }

    /// <summary>
    /// **注册后两个目标都在，且名字与类别正确** / Both objectives land on registration with the right names and categories.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. **预配载注册后有两个目标**。
    /// 2. 名字是 `"experimental longitudinal balance"` 与 `"redundancy"`（**带空格**）。
    /// 3. 两条都是 `Minimum`。
    /// 4. **全载注册后零目标**——模型上一条都没有，"报错"与"静默成功"的区别靠返回值分。
    ///
    /// Four paired points: predistribution registration yields two objectives; the names are `"experimental longitudinal balance"` and `"redundancy"` (**with spaces**); both are
    /// `Minimum`; and full-load registration yields **zero**, with the return value distinguishing "errored" from "silently succeeded".
    /// </remarks>
    [Fact]
    public void Register_ShouldAddBothObjectivesUnderPredistribution() {
        (StowageModel Stowage, Load Load, List<Item> Items, List<StowagePosition> Positions) rig = Rig();
        var aggregation = new RedundancyAggregation(rig.Items, rig.Positions, rig.Load);
        var context = new RedundancyContext(aggregation);

        using var predModel = new LinearMetaModel<Flt64>();
        rig.Stowage.Register(predModel).IsOk.Should().BeTrue();
        rig.Load.Register(predModel).IsOk.Should().BeTrue();
        context.Register(StowageMode.Predistribution, new Parameter(), predModel).IsOk.Should().BeTrue();

        predModel.MetaSubObjects.Should().HaveCount(2);
        // **顺序必须钉住**：两个目标都是 `Minimum`，内容也一样多，`BeEquivalentTo` 对顺序不敏感，
        // 会让"交换两条管线"的变异存活（实测存活过一次）。
        // **The order must be pinned**: both objectives are `Minimum` with similar content, and `BeEquivalentTo` is order-insensitive, which lets a pipeline-swap mutant survive
        // (measured once).
        predModel.MetaSubObjects.Select(entry => entry.Name).Should().Equal(new[] {
            "experimental longitudinal balance", "redundancy",
        });
        predModel.MetaSubObjects.Select(entry => entry.Category).Should().OnlyContain(category => category == ObjectCategory.Minimum);

        (StowageModel Stowage, Load Load, List<Item> Items, List<StowagePosition> Positions) fullRig = Rig();
        var fullAggregation = new RedundancyAggregation(fullRig.Items, fullRig.Positions, fullRig.Load);
        using var fullModel = new LinearMetaModel<Flt64>();
        fullRig.Stowage.Register(fullModel).IsOk.Should().BeTrue();
        fullRig.Load.Register(fullModel).IsOk.Should().BeTrue();
        new RedundancyContext(fullAggregation)
            .Register(StowageMode.FullLoad, new Parameter(), fullModel).IsOk.Should().BeTrue();
        fullModel.MetaSubObjects.Should().BeEmpty("full load contributes no redundancy objective");
    }

    /// <summary>
    /// **未初始化时 `Register` 报错；`Init` 幂等且失败时不交出半个聚合** / `Register` errors when uninitialised; `Init` is idempotent and never hands out a half-built aggregation.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 未 `Init` 就 `Register` → 失败，消息直说未初始化。
    /// 2. 上游没有配载聚合 → `Init` 失败，且 `Aggregation` 仍是 `null`。
    /// 3. 连续两次 `Init` → 同一个实例（幂等而不是重建）。
    ///
    /// Three paired points: `Register` before `Init` fails with a message naming the cause; `Init` without an upstream stowage aggregation fails and leaves `Aggregation` **null**; and
    /// two consecutive `Init` calls keep the **same instance**.
    /// </remarks>
    [Fact]
    public void Context_ShouldGuardUninitialisedAndBeIdempotent() {
        using var model = new LinearMetaModel<Flt64>();
        var context = new RedundancyContext();
        Try rejected = context.Register(StowageMode.Predistribution, new Parameter(), model);
        rejected.IsOk.Should().BeFalse();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)rejected).Error.Message.Should().Contain("尚未初始化");

        Try failedInit = context.Init(new StowageContext(), Request());
        failedInit.IsOk.Should().BeFalse("the stowage context carries no aggregation");
        context.Aggregation.Should().BeNull("no half-built aggregation is handed out");

        (StowageModel Stowage, Load Load, List<Item> Items, List<StowagePosition> Positions) rig = Rig();
        var prebuilt = new RedundancyAggregation(rig.Items, rig.Positions, rig.Load);
        var injected = new RedundancyContext(prebuilt);
        injected.Init(new StowageContext(), Request()).IsOk.Should().BeTrue();
        injected.Aggregation.Should().BeSameAs(prebuilt);
        injected.Init(new StowageContext(), Request()).IsOk.Should().BeTrue();
        injected.Aggregation.Should().BeSameAs(prebuilt, "a second Init must not rebuild the aggregation");
    }
}
