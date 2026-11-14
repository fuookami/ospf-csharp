// 装载效能收尾（B625）的判据 / Criteria for the loading-effectiveness wrap-up (B625).

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
/// **装载效能收尾（B625）** / The loading-effectiveness wrap-up (B625).
/// </summary>
/// <remarks>
/// 六条拖车校验、按来源分组的保序性、以及十二条管线的条件矩阵。
///
/// The six trailer validations, the order-preserving source grouping, and the twelve-pipeline condition matrix.
/// </remarks>
public class Demo2LoadingEffectivenessWrapUpTests {
    private static Item MakeItem(string name, string source = "PEK", string destination = "SHA") =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority("P1", new Fuookami.Ospf.Math.Algebra.Number.UInt64(1UL), CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: source,
            destination: destination,
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

    private static RequestDTO Request(
        IReadOnlyList<CargoInput>? cargos = null,
        IReadOnlyList<TrailerInput>? trailers = null) =>
        new(
            "REQ",
            cargos ?? new List<CargoInput> { new("A", 100.0, 1, "PEK", "SHA") },
            new List<PositionInput> { new("P1", 1000.0, 100.0, 0.0) },
            Trailers: trailers);

    /// <summary>
    /// **六条拖车校验各自能拦住自己的坏输入** / Each of the six trailer validations catches its own bad input.
    /// </summary>
    /// <remarks>
    /// 六点成对，逐条对应基线的六条校验：
    ///
    /// 1. **空白名称** → 失败。
    /// 2. **重名** → 失败。
    /// 3. **顺序越界**（256 与 −1） → 失败。
    /// 4. **同车内重复货物** → 失败。
    /// 5. **一件货属于两辆车** → 失败。
    /// 6. **引用不存在的货物** → 失败。
    ///
    /// Six paired points, one per baseline validation: **a blank name**; **a duplicate name**; **an out-of-range order** (256 and −1); **a duplicate item within a trailer**; **an item in two
    /// trailers**; and **a reference to a missing cargo**.
    /// </remarks>
    [Fact]
    public void BuildTrailers_ShouldEnforceSixValidations() {
        var items = new List<Item> { MakeItem("A"), MakeItem("B") };

        Result<IReadOnlyList<Trailer>, ErrorCode, Error<ErrorCode>> Build(IReadOnlyList<TrailerInput> trailers) =>
            AggregationInitializer.BuildTrailers(Request(trailers: trailers), items);

        Build(new List<TrailerInput> { new(" ", TrailerTypeInput.Transit, 1, new[] { "A" }) })
            .IsOk.Should().BeFalse("a blank name is rejected");
        ((Failed<IReadOnlyList<Trailer>, ErrorCode, Error<ErrorCode>>)Build(
            new List<TrailerInput> { new(" ", TrailerTypeInput.Transit, 1, new[] { "A" }) }))!
            .Error.Message.Should().Contain("名称不能为空");

        Build(new List<TrailerInput> {
            new("T1", TrailerTypeInput.Transit, 1, new[] { "A" }),
            new("T1", TrailerTypeInput.Transit, 2, new[] { "B" }),
        }).IsOk.Should().BeFalse("duplicate names are rejected");

        Build(new List<TrailerInput> { new("T1", TrailerTypeInput.Transit, 256, new[] { "A" }) })
            .IsOk.Should().BeFalse("order 256 is out of range");
        Build(new List<TrailerInput> { new("T1", TrailerTypeInput.Transit, -1, new[] { "A" }) })
            .IsOk.Should().BeFalse("order -1 is out of range");

        // **必须断言消息**：去掉同车去重后，跨车那条校验会**顺带**把同一个输入拦下来（消息不同），
        // 只看 `IsOk` 的判据会把这个变异放过去。
        // **The message must be asserted**: without the within-trailer check the cross-trailer one also rejects the same input (with a different message), so a criterion that only reads
        // `IsOk` lets this mutant through.
        Result<IReadOnlyList<Trailer>, ErrorCode, Error<ErrorCode>> duplicateWithin =
            Build(new List<TrailerInput> { new("T1", TrailerTypeInput.Transit, 1, new[] { "A", "A" }) });
        duplicateWithin.IsOk.Should().BeFalse("a duplicate item within one trailer is rejected");
        ((Failed<IReadOnlyList<Trailer>, ErrorCode, Error<ErrorCode>>)duplicateWithin).Error.Message
            .Should().Contain("同一拖车的货物不能重复", "the within-trailer rule fires before the cross-trailer one");

        Build(new List<TrailerInput> {
            new("T1", TrailerTypeInput.Transit, 1, new[] { "A" }),
            new("T2", TrailerTypeInput.Transit, 2, new[] { "A" }),
        }).IsOk.Should().BeFalse("an item cannot belong to two trailers");

        Build(new List<TrailerInput> { new("T1", TrailerTypeInput.Transit, 1, new[] { "ZZ" }) })
            .IsOk.Should().BeFalse("an unknown cargo reference is rejected");

        // 干净输入通过，且类型映射是显式的。
        // Clean input passes and the type mapping is explicit.
        Result<IReadOnlyList<Trailer>, ErrorCode, Error<ErrorCode>> good = Build(new List<TrailerInput> {
            new("T1", TrailerTypeInput.Warehouse, 2, new[] { "A" }),
            new("T2", TrailerTypeInput.Hardstand, 1, new[] { "B" }),
        });
        good.IsOk.Should().BeTrue();
        IReadOnlyList<Trailer> built = ((Ok<IReadOnlyList<Trailer>, ErrorCode, Error<ErrorCode>>)good).Value!;
        built[0].Type.Should().Be(TrailerType.Warehouse);
        built[0].Order.Should().Be(2);
        built[1].Type.Should().Be(TrailerType.Hardstand);
    }

    /// <summary>
    /// **按来源分组保序，且容许重复来源** / Source grouping preserves order and tolerates repeats.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 三个来源交错的请求 → 分组键按**首次出现顺序**：`PEK`、`CTU`、`CAN`。
    /// 2. **同来源的下标按请求顺序**，不排序。
    /// 3. **重复来源不会合并成新组**——这正是基线的 `groupBy` 语义。
    ///
    /// Three paired points: an interleaved request groups keys by **first appearance** (`PEK`, `CTU`, `CAN`); **indices within a source follow request order** rather than being sorted; and
    /// **a repeated source does not open a new group**, which is exactly the baseline's `groupBy`.
    /// </remarks>
    [Fact]
    public void CargosBySource_ShouldPreserveOrder() {
        var cargos = new List<CargoInput> {
            new("A", 1.0, 1, "PEK", "SHA"),
            new("B", 1.0, 1, "CTU", "SHA"),
            new("C", 1.0, 1, "PEK", "CAN"),
            new("D", 1.0, 1, "CAN", "SHA"),
            new("E", 1.0, 1, "PEK", "SHA"),
        };
        IReadOnlyDictionary<string, IReadOnlyList<int>> grouped =
            AggregationInitializer.CargosBySource(Request(cargos));

        grouped.Keys.Should().Equal(new[] { "PEK", "CTU", "CAN" }, "keys follow first appearance");
        grouped["PEK"].Should().Equal(new[] { 0, 2, 4 }, "indices follow request order");
        grouped["CTU"].Should().Equal(new[] { 1 });
        grouped["CAN"].Should().Equal(new[] { 3 });
    }

    /// <summary>
    /// **十二条管线的条件矩阵** / The twelve-pipeline condition matrix.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. **预配载**：提前装载、预留、建议数量、建议重量、顺序反转（5 条预配载专属）+ 同来源相邻、同目的地相邻、优先级顺序（3 条无条件）= **8 条**。
    /// 2. **全载**：同来源相邻、同目的地相邻、优先级顺序（3 条无条件）+ 同来源早装（来源映射非空）= **4 条**。
    ///    **拖车那两条需要 `TrailerLoading`，它只在 `DeriveSubModels` 被调用后才存在。**
    /// 3. **建议打板**：3 条无条件 + 同来源早装 = **4 条**。
    /// 4. **全载且来源映射为空** → **5 条**（少了同来源早装）；**不派生子模型** → **2 条**（相邻两条也一起没了）。
    ///
    /// Four paired points: **predistribution** gives ahead-load, reserve, both advice objectives and order reversal (five predistribution-only) plus both adjacencies and priority order
    /// (three unconditional), so **eight**; **full load** gives the three unconditional plus source-early, so **four**, with the two trailer pipelines requiring `TrailerLoading`, which exists
    /// only after `DeriveSubModels` runs; **weight recommendation** gives three unconditional plus source-early, so **four**; and **full load with an empty source map** gives **three**.
    /// </remarks>
    [Fact]
    public void PipelineListGenerator_ShouldApplyTheTwelveConditions() {
        var items = new List<Item> { MakeItem("A"), MakeItem("B", "CTU", "CAN") };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        var sources = new Dictionary<string, IReadOnlyList<int>> { ["PEK"] = new[] { 0 }, ["CTU"] = new[] { 1 } };

        int Count(StowageMode mode, bool derive, bool withSources) {
            StowageModel stowage = MakeStowage(items, positions);
            var aggregation = new LoadingEffectivenessAggregation(
                mode, items, positions, stowage,
                trailers: new List<Trailer>(),
                flight: new Flight(new FlightNo("CA1"), new IATACode("PEK"), new IATACode("SHA")),
                cargosBySource: withSources ? sources : null);
            using var model = new LinearMetaModel<Flt64>();
            stowage.Register(model).IsOk.Should().BeTrue();
            var load = new Load(items, positions, stowage, withMultiLoadingSchema: true);
            load.Register(model).IsOk.Should().BeTrue();
            if (derive) {
                aggregation.DeriveSubModels(load);
            }
            Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> generated =
                new PipelineListGenerator(aggregation).Invoke(mode, new Parameter());
            generated.IsOk.Should().BeTrue();
            return ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)generated)
                .Value!.Count;
        }

        Count(StowageMode.Predistribution, derive: true, withSources: true).Should().Be(8,
            "five predistribution-only pipelines plus three unconditional ones");
        Count(StowageMode.FullLoad, derive: true, withSources: true).Should().Be(6,
            "three unconditional, source-early, and the two trailer pipelines");
        Count(StowageMode.WeightRecommendation, derive: true, withSources: true).Should().Be(4,
            "three unconditional plus source-early");
        Count(StowageMode.FullLoad, derive: true, withSources: false).Should().Be(5,
            "without a source map the source-early pipeline is dropped, leaving three unconditional and two trailer pipelines");
        // 不派生子模型时**相邻那两条也一起消失**（它们读 `TransferAdjacentLoading`），只剩优先级顺序与同来源早装。
        // Without deriving sub-models **both adjacencies vanish too** (they read `TransferAdjacentLoading`), leaving only priority order and source-early.
        Count(StowageMode.FullLoad, derive: false, withSources: true).Should().Be(2,
            "priority order and source-early are the only pipelines that need no sub-model");
    }
}
