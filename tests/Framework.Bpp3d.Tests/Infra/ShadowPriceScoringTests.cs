// 影子价格感知评分测试 / Shadow-price-aware scoring tests.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Application;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Linq;
using System;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

using InfraItem = Fuookami.Ospf.Framework.Bpp3d.Infra.Item;
using ItemModel = Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Infra;

/// <summary>
/// 影子价格感知的层评分 / Shadow-price-aware layer scoring.
/// </summary>
/// <remarks>
/// `EnableShadowPriceAwareRequestScore` 过去是一个**没有人读取的布尔值**：`ScoreByShadowPrice` 只是个 `object?` 占位。
/// 这些测试要求"评分真的算得出来、真的影响顺序"。
///
/// `EnableShadowPriceAwareRequestScore` used to be a boolean **nobody read**: `ScoreByShadowPrice` was just an `object?` placeholder. These tests require
/// the score to actually be computed and to actually affect the ordering.
/// </remarks>
public sealed class ShadowPriceScoringTests {
    private static Quantity<FltX> M(double value) => new(new FltX(value), SIBaseUnits.Meter);

    private static BinLayer LayerOf(params string[] itemIds) {
        var units = new List<QuantityPlacement3<InfraItem, FltX>>();
        foreach (string id in itemIds) {
            units.Add(new QuantityPlacement3<InfraItem, FltX>(
                new InfraItem(id, id), new QuantityPoint3<FltX>(M(0), M(0), M(0))));
        }
        return new BinLayer {
            Shape = new Container3Shape<FltX>(M(2), M(2), M(2)),
            Units = units,
        };
    }

    private static Bpp3dLayerGenerationRequest<FltX> Request(
        Dictionary<DemandModeKey, FltX> prices,
        IReadOnlyList<LayerGenerationDemandEntry>? entries = null) =>
        new(Iteration: 1, Items: new List<object>(), ExistingLayers: new List<BinLayer>(),
            ShadowPrices: prices, MaxCandidates: 8, DemandEntries: entries);

    private static DemandModeKey ItemKeyOf(string id) =>
        new(new Bpp3dDemandMode.ItemAmount(), new Bpp3dDemandKey.ItemKey(id));

    /// <summary>**评分 = Σ 影子价格 × 贡献量** / The score is the sum of shadow price times contribution.</summary>
    [Fact]
    public void Score_ShouldMultiplyPriceByContribution() {
        var prices = new Dictionary<DemandModeKey, FltX> {
            [ItemKeyOf("A")] = new FltX(3),
            [ItemKeyOf("B")] = new FltX(5),
        };
        BinLayer layer = LayerOf("A", "A", "B");

        double score = Bpp3dShadowPriceScoring.Score(layer, Request(prices));

        // A 出现两次（2 × 3 = 6），B 出现一次（1 × 5 = 5）→ 11。
        // A appears twice (2 × 3 = 6), B once (1 × 5 = 5) → 11.
        score.Should().Be(11.0);
    }

    /// <summary>**只看本层真正含有的货物** / Only the items the layer actually contains count.</summary>
    [Fact]
    public void Score_ShouldIgnoreAbsentItems() {
        var prices = new Dictionary<DemandModeKey, FltX> {
            [ItemKeyOf("A")] = new FltX(3),
            [ItemKeyOf("Z")] = new FltX(100),
        };
        BinLayer layer = LayerOf("A");

        Bpp3dShadowPriceScoring.Score(layer, Request(prices)).Should().Be(3.0);
    }

    /// <summary>**没有影子价格时评分为零** / No shadow prices means a zero score.</summary>
    [Fact]
    public void Score_ShouldBeZeroWithoutPrices() =>
        Bpp3dShadowPriceScoring.Score(LayerOf("A"), Request(null!)).Should().Be(0.0);

    /// <summary>**显式需求条目优先于影子价格表的键** / Explicit demand entries take precedence over the price keys.</summary>
    /// <remarks>
    /// 请求同时带 `A`（价格 3）与 `B`（价格 5），但 `DemandEntries` 只声明 `A`。若实现退回影子价格表的键，
    /// 评分会是 3 + 5；只看显式条目才是 3。**这一条是分得出两种实现的唯一夹具。**
    ///
    /// The request carries both `A` (price 3) and `B` (price 5), but `DemandEntries` declares only `A`. An implementation that falls back to the price keys
    /// would score 3 + 5; only the explicit entries give 3. **This is the only fixture that distinguishes the two.**
    /// </remarks>
    [Fact]
    public void Score_ShouldPreferExplicitDemandEntries() {
        var prices = new Dictionary<DemandModeKey, FltX> {
            [ItemKeyOf("A")] = new FltX(3),
            [ItemKeyOf("B")] = new FltX(5),
        };
        var entries = new List<LayerGenerationDemandEntry> {
            new(new Bpp3dDemandMode.ItemAmount(), new Bpp3dDemandKey.ItemKey("A")),
        };
        BinLayer layer = LayerOf("A", "B");

        Bpp3dShadowPriceScoring.Score(layer, Request(prices, entries)).Should().Be(3.0);
    }

    /// <summary>**物料键贡献为零，而不是猜一个数** / A material key contributes zero rather than a guess.</summary>
    [Fact]
    public void Score_ShouldGiveZeroForUnsupportedKeys() {
        var prices = new Dictionary<DemandModeKey, FltX> {
            [new DemandModeKey(new Bpp3dDemandMode.ItemMaterialAmount(),
                new Bpp3dDemandKey.MaterialKeyRef(new ItemModel.MaterialKey("M1", ItemModel.MaterialType.RawMaterial)))] = new FltX(9),
        };

        Bpp3dShadowPriceScoring.Score(LayerOf("A"), Request(prices)).Should().Be(0.0);
    }

    /// <summary>**开关为真时请求带评分，为假时不带** / The switch attaches a scorer when true and leaves it null when false.</summary>
    /// <remarks>
    /// 只断言"开启时有评分"会放过一个**恒带评分**的实现。两条分支一起断言才证明开关被读取。
    ///
    /// Asserting only "there is a scorer when enabled" would let an implementation that **always** attaches one pass. Asserting both branches proves the
    /// switch is read.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task RequestBuilder_ShouldAttachTheScorerOnlyWhenEnabled() {
        var state = new ColumnGenerationState<FltX>(
            Iteration: 1,
            Columns: new List<BinLayer>(),
            Bins: new List<Bin<BinLayer, FltX>>(),
            ShadowPrices: new Dictionary<DemandModeKey, FltX>(),
            ContinuousRadiusSolverPrototypes: new List<ContinuousCylinderRadiusSolverPrototype>(),
            ContinuousRadiusSolverResults: new Dictionary<string, FltX>(),
            PwlContinuousRadiusResults: new Dictionary<string, IReadOnlyDictionary<string, FltX>>());
        var items = new List<InfraItem>();
        var cgConfig = new ColumnGenerationConfig();

        ColumnGenerationLayerRequestBuilder<FltX> enabled =
            new ColumnGenerationStandardExecutors(
                new NullSolver(), new List<(InfraItem, ulong)>(),
                null, null, new ColumnGenerationStandardExecutorConfig())
            .RequestBuilder();
        Bpp3dLayerGenerationRequest<FltX> withScorer = await enabled(state, items, cgConfig);
        withScorer.ScoreByShadowPrice.Should().NotBeNull();

        ColumnGenerationLayerRequestBuilder<FltX> disabled =
            new ColumnGenerationStandardExecutors(
                new NullSolver(), new List<(InfraItem, ulong)>(),
                null, null,
                new ColumnGenerationStandardExecutorConfig(EnableShadowPriceAwareRequestScore: false))
            .RequestBuilder();
        Bpp3dLayerGenerationRequest<FltX> withoutScorer = await disabled(state, items, cgConfig);
        withoutScorer.ScoreByShadowPrice.Should().BeNull();
    }

    /// <summary>**候选层真的按评分降序排** / Candidates really are ranked by descending score.</summary>
    /// <remarks>
    /// 只断言"评分算得对"会放过一个**算完就丢**的实现——那正是修复前的状态（评分被构造、被传递、然后没人读）。
    /// 这里用 `filterByReducedCost` 捕获**实际到达过滤器的顺序**：低分在前、高分在后时，排序后应颠倒。
    ///
    /// Asserting only "the score is computed correctly" would let an implementation **compute and then drop** it — exactly the pre-fix state (the scorer
    /// was built, passed along, and read by nobody). This captures the **order that actually reaches the filter**: with the low score first and the high
    /// score second, ranking must reverse them.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task Algorithm_ShouldRankCandidatesByScore() {
        BinLayer low = LayerOf("A");
        BinLayer high = LayerOf("A", "A");
        var prices = new Dictionary<DemandModeKey, FltX> { [ItemKeyOf("A")] = new FltX(1) };

        var observed = new List<string>();
        int calls = 0;
        var algorithm = new ColumnGenerationAlgorithm<FltX>(
            layerGenerator: new FixedGenerator(() => calls++ == 0
                ? new List<Bpp3dLayerGenerationResult<FltX>> {
                    new(low, new FltX(-1.0), "low"),
                    new(high, new FltX(-1.0), "high"),
                }
                : new List<Bpp3dLayerGenerationResult<FltX>>()),
            rmpSolver: async _ => {
                await System.Threading.Tasks.Task.CompletedTask;
                return new Utils.Functional.Ok<ColumnGenerationLpResult<FltX>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>(
                    new ColumnGenerationLpResult<FltX>(prices, new FltX(10.0)));
            },
            layerRequestBuilder: async (state, items, config) => {
                await System.Threading.Tasks.Task.CompletedTask;
                return new Bpp3dLayerGenerationRequest<FltX>(
                    Iteration: state.Iteration,
                    Items: items.Cast<object>().ToList(),
                    ExistingLayers: state.Columns,
                    ShadowPrices: prices,
                    MaxCandidates: 8,
                    ScoreByShadowPrice: Bpp3dShadowPriceScoring.Scorer<FltX>());
            },
            filterByReducedCost: (state, candidates) => {
                observed.AddRange(candidates.Select(c => c.Source));
                return System.Threading.Tasks.Task.FromResult(candidates);
            });

        await algorithm.SolveAsync(
            items: new List<InfraItem>(),
            config: new ColumnGenerationConfig(IterationLimit: 2, FinalMilpEnabled: false));

        // 生成顺序是 low、high；排序后 high（2 × 1）在前。
        // Generation order is low then high; after ranking, high (2 × 1) comes first.
        observed.Should().Equal(new[] { "high", "low" });
    }

    /// <summary>按调用次数返回固定候选的生成器 / A generator returning fixed candidates per call.</summary>
    private sealed class FixedGenerator : IBpp3dLayerGenerator<FltX> {
        private readonly Func<List<Bpp3dLayerGenerationResult<FltX>>> _next;
        public FixedGenerator(Func<List<Bpp3dLayerGenerationResult<FltX>>> next) => _next = next;
        public System.Threading.Tasks.Task<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>> GenerateAsync(
            Bpp3dLayerGenerationRequest<FltX> request) =>
            System.Threading.Tasks.Task.FromResult<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>>(_next());
    }

    /// <summary>
    /// 本测试不进入求解路径，故桩只报名字 / This test never reaches the solve path, so the stub only names itself.
    /// </summary>
    private sealed class NullSolver : IColumnGenerationSolver {
        public string Name => "noop";

        private static System.Threading.Tasks.Task<Utils.Functional.Result<T, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>> Fail<T>() =>
            System.Threading.Tasks.Task.FromResult(
                (Utils.Functional.Result<T, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>)
                new Utils.Functional.Failed<T, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>(
                    new Utils.Error.Err<Utils.Error.ErrorCode>(Utils.Error.ErrorCode.Unknown, "no solve in this fixture")));

        public System.Threading.Tasks.Task<Utils.Functional.Result<Core.Solver.Output.FeasibleSolverOutput<Flt64>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>>
            SolveMILPAsync(string name, Core.Model.Mechanism.LinearMetaModel<Flt64> metaModel, bool toLogModel = false,
                Core.Model.Basic.RegistrationStatusCallBack? registrationStatusCallBack = null,
                Core.Solver.Output.SolvingStatusCallBack? solvingStatusCallBack = null) =>
            Fail<Core.Solver.Output.FeasibleSolverOutput<Flt64>>();

        public System.Threading.Tasks.Task<Utils.Functional.Result<(Core.Solver.Output.FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>>
            SolveMILPAsync(string name, Core.Model.Mechanism.LinearMetaModel<Flt64> metaModel, ulong amount, bool toLogModel = false,
                Core.Model.Basic.RegistrationStatusCallBack? registrationStatusCallBack = null,
                Core.Solver.Output.SolvingStatusCallBack? solvingStatusCallBack = null) =>
            Fail<(Core.Solver.Output.FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool)>();

        public System.Threading.Tasks.Task<Utils.Functional.Result<Core.Solver.Output.FeasibleSolverOutput<V>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>>
            SolveMILPAsAsync<V>(string name, Core.Model.Mechanism.LinearMetaModel<Flt64> metaModel, Core.Solver.Value.IIntoValue<V> converter,
                bool toLogModel = false,
                Core.Model.Basic.RegistrationStatusCallBack? registrationStatusCallBack = null,
                Core.Solver.Output.SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, Math.Algebra.Concept.IRealNumber<V>, Math.Algebra.Concept.INumberField<V> =>
            Fail<Core.Solver.Output.FeasibleSolverOutput<V>>();

        public System.Threading.Tasks.Task<Utils.Functional.Result<IColumnGenerationSolver.LpResult, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>>
            SolveLPAsync(string name, Core.Model.Mechanism.LinearMetaModel<Flt64> metaModel, bool toLogModel = false,
                Core.Model.Basic.RegistrationStatusCallBack? registrationStatusCallBack = null,
                Core.Solver.Output.SolvingStatusCallBack? solvingStatusCallBack = null) =>
            Fail<IColumnGenerationSolver.LpResult>();

        public System.Threading.Tasks.Task<Utils.Functional.Result<IColumnGenerationSolver.LpResultOf<V>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>>
            SolveLPAsAsync<V>(string name, Core.Model.Mechanism.LinearMetaModel<Flt64> metaModel, Core.Solver.Value.IIntoValue<V> converter,
                bool toLogModel = false,
                Core.Model.Basic.RegistrationStatusCallBack? registrationStatusCallBack = null,
                Core.Solver.Output.SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, Math.Algebra.Concept.IRealNumber<V>, Math.Algebra.Concept.INumberField<V> =>
            Fail<IColumnGenerationSolver.LpResultOf<V>>();
    }
}
