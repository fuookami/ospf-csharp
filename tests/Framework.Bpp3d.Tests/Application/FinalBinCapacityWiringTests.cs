// 最终解载重约束接线测试 / Tests for the final-solve capacity wiring.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Bpp3d.Application;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

using InfraItem = Fuookami.Ospf.Framework.Bpp3d.Infra.Item;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Application;

/// <summary>
/// 最终解阶段的载重约束接线 / The capacity wiring on the final-solve stage.
/// </summary>
/// <remarks>
/// `EnableFinalBinCapacityConstraint` 过去只是一个**没有人读取的布尔值**：设成 `true` 或 `false`，走的路一模一样。
/// 这里要求"开关真的改变结果"。
///
/// `EnableFinalBinCapacityConstraint` used to be a boolean **nobody read**: setting it to `true` or `false` took exactly the same path. These tests
/// require the switch to actually change the outcome.
/// </remarks>
public sealed class FinalBinCapacityWiringTests {
    private static Quantity<FltX> M(double value) => new(new FltX(value), SIBaseUnits.Meter);
    private static Quantity<FltX> Kg(double value) => new(new FltX(value), SIBaseUnits.Kilogram);

    private static Bin<BinLayer, FltX> BinOf(InfraItem? item, double capacityKg) {
        var layer = new BinLayer {
            Shape = new Container3Shape<FltX>(M(2), M(2), M(2)),
            Units = item is null
                ? Array.Empty<QuantityPlacement3<InfraItem, FltX>>()
                : new[] {
                    new QuantityPlacement3<InfraItem, FltX>(item, new QuantityPoint3<FltX>(M(0), M(0), M(0))),
                },
        };
        var binType = new BinType<FltX>("B1", M(2), M(2), M(2), Kg(capacityKg));
        return new Bin<BinLayer, FltX>(binType, new[] {
            new QuantityPlacement3<BinLayer, FltX>(layer, new QuantityPoint3<FltX>(M(0), M(0), M(0))),
        });
    }

    private static ColumnGenerationStandardExecutors Executors(
        Bin<BinLayer, FltX> bin,
        ColumnGenerationStandardExecutorConfig config) =>
        new(new RejectingSolver(), new List<(InfraItem, ulong)>(), null, new[] { bin }, config);

    private static ColumnGenerationState<FltX> State() => new(
        Iteration: 1,
        Columns: new List<BinLayer>(),
        Bins: new List<Bin<BinLayer, FltX>>(),
        ShadowPrices: new Dictionary<DemandModeKey, FltX>(),
        ContinuousRadiusSolverPrototypes: new List<ContinuousCylinderRadiusSolverPrototype>(),
        ContinuousRadiusSolverResults: new Dictionary<string, FltX>(),
        PwlContinuousRadiusResults: new Dictionary<string, IReadOnlyDictionary<string, FltX>>());

    /// <summary>**开启时超载的重量在求解之前就被拒绝** / An overweight bin is rejected before the solve.</summary>
    /// <remarks>
    /// 夹具里的求解器一律失败，而断言要求错误信息是**载重**而不是求解器自己的失败——
    /// 于是"检查先于求解"这件事也被顺带钉住。
    ///
    /// The solver in the fixture always fails, and the assertion demands a **capacity** message rather than the solver's own failure, so "the check
    /// runs before the solve" is pinned as a side effect.
    /// </remarks>
    [Fact]
    public async Task FinalSolver_ShouldRejectOverweightBinsWhenEnabled() {
        ColumnGenerationStandardExecutors executors = Executors(
            BinOf(new InfraItem("I1", "Heavy", Shape: null, Weight: Kg(500)), 100),
            new ColumnGenerationStandardExecutorConfig());

        Result<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> result =
            await executors.FinalSolver()(State());

        result.IsOk.Should().BeFalse();
        Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().Contain("weight capacity");
        failed.Message.Should().Contain("bin[0]");
    }

    /// <summary>**关掉开关后不再做载重校验** / The check disappears when the switch is off.</summary>
    /// <remarks>
    /// 这是"开关真的被读取"的证据。**若断言只看"开启时失败"，一个恒校验的实现同样通过。**
    ///
    /// This is the evidence that the switch is read. **Asserting only "it fails when enabled" would also pass with an implementation that always
    /// validates.**
    /// </remarks>
    [Fact]
    public async Task FinalSolver_ShouldSkipTheCheckWhenDisabled() {
        ColumnGenerationStandardExecutors executors = Executors(
            BinOf(new InfraItem("I1", "Heavy", Shape: null, Weight: Kg(500)), 100),
            new ColumnGenerationStandardExecutorConfig(EnableFinalBinCapacityConstraint: false));

        Result<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> result =
            await executors.FinalSolver()(State());

        result.IsOk.Should().BeFalse();
        Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().NotContain("weight capacity");
    }

    /// <summary>**重量未知不等于重量为零** / Unknown weight is not zero weight.</summary>
    [Fact]
    public async Task FinalSolver_ShouldRejectItemsWithoutWeight() {
        ColumnGenerationStandardExecutors executors = Executors(
            BinOf(new InfraItem("I1", "Unknown"), 100),
            new ColumnGenerationStandardExecutorConfig());

        Result<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> result =
            await executors.FinalSolver()(State());

        result.IsOk.Should().BeFalse();
        Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().Contain("carries no weight");
    }

    /// <summary>**未超载的箱子走到求解阶段** / A bin within capacity reaches the solve stage.</summary>
    [Fact]
    public async Task FinalSolver_ShouldLetBinsWithinCapacityThrough() {
        ColumnGenerationStandardExecutors executors = Executors(
            BinOf(new InfraItem("I1", "Light", Shape: null, Weight: Kg(50)), 100),
            new ColumnGenerationStandardExecutorConfig());

        Result<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> result =
            await executors.FinalSolver()(State());

        result.IsOk.Should().BeFalse();
        Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().NotContain("weight capacity");
        failed.Message.Should().NotContain("carries no weight");
    }

    /// <summary>构造装有多件货物的箱子 / Build a bin holding several items.</summary>
    private static Bin<BinLayer, FltX> BinOfMany(double capacityKg, params InfraItem[] items) {
        var layer = new BinLayer {
            Shape = new Container3Shape<FltX>(M(2), M(2), M(2)),
            Units = items
                .Select(item => new QuantityPlacement3<InfraItem, FltX>(item, new QuantityPoint3<FltX>(M(0), M(0), M(0))))
                .ToList(),
        };
        var binType = new BinType<FltX>("B1", M(2), M(2), M(2), Kg(capacityKg));
        return new Bin<BinLayer, FltX>(binType, new[] {
            new QuantityPlacement3<BinLayer, FltX>(layer, new QuantityPoint3<FltX>(M(0), M(0), M(0))),
        });
    }

    /// <summary>**载重是逐件累加的，不是只看第一件** / The loaded weight is the sum over items, not just the first one.</summary>
    /// <remarks>
    /// 两件 60 kg、容量 100 kg：**单看任何一件都不超重，合起来超重**。只断言"500 kg 单件被拒"的夹具
    /// 分不出"求和"与"只看第一件"——这正是上一轮存活的那个变异。
    ///
    /// Two 60 kg items against a 100 kg capacity: **neither alone is too heavy, the pair is**. A fixture that only asserts "a single 500 kg item is
    /// rejected" cannot tell "sum" apart from "only the first item" — which is exactly the mutation that survived the first round.
    /// </remarks>
    [Fact]
    public async Task FinalSolver_ShouldSumItemWeights() {
        ColumnGenerationStandardExecutors executors = Executors(
            BinOfMany(100,
                new InfraItem("I1", "A", Shape: null, Weight: Kg(60)),
                new InfraItem("I2", "B", Shape: null, Weight: Kg(60))),
            new ColumnGenerationStandardExecutorConfig());

        Result<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> result =
            await executors.FinalSolver()(State());

        result.IsOk.Should().BeFalse();
        Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().Contain("weight capacity");

        // 同一批次里两件 60 kg 但容量 200 kg → 装得下（排除"只要多件就失败"的实现）。
        // Two 60 kg items against a 200 kg capacity → fits (rules out "any multi-item bin fails").
        ColumnGenerationStandardExecutors roomy = Executors(
            BinOfMany(200,
                new InfraItem("I1", "A", Shape: null, Weight: Kg(60)),
                new InfraItem("I2", "B", Shape: null, Weight: Kg(60))),
            new ColumnGenerationStandardExecutorConfig());
        Result<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> roomyResult =
            await roomy.FinalSolver()(State());
        roomyResult.Should().BeOfType<Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>>()
            .Which.Message.Should().NotContain("weight capacity");
    }

    /// <summary>**混用不同重量单位必须报错，不能静默相加** / Mixing weight units must fail rather than be silently added.</summary>
    /// <remarks>
    /// 把 kg 与 m 当成同一种量相加在数值上永远算得出来，**所以夹具必须真的混两个单位**。
    ///
    /// Adding kilograms to metres always produces a number, **so the fixture has to genuinely mix two units**.
    /// </remarks>
    [Fact]
    public async Task FinalSolver_ShouldRejectMixedWeightUnits() {
        ColumnGenerationStandardExecutors executors = Executors(
            BinOfMany(100,
                new InfraItem("I1", "A", Shape: null, Weight: Kg(10)),
                new InfraItem("I2", "B", Shape: null, Weight: M(1))),
            new ColumnGenerationStandardExecutorConfig());

        Result<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> result =
            await executors.FinalSolver()(State());

        result.IsOk.Should().BeFalse();
        Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().Contain("mixes weight units");
    }

    /// <summary>构造层深超过箱深的箱子 / Build a bin whose layers are deeper than the bin.</summary>
    private static Bin<BinLayer, FltX> DeepBin(double binDepth, params double[] layerDepths) {
        var placements = new List<QuantityPlacement3<BinLayer, FltX>>();
        double offset = 0.0;
        foreach (double layerDepth in layerDepths) {
            var layer = new BinLayer {
                Shape = new Container3Shape<FltX>(M(2), M(2), M(layerDepth)),
                Units = Array.Empty<QuantityPlacement3<InfraItem, FltX>>(),
            };
            placements.Add(new QuantityPlacement3<BinLayer, FltX>(
                layer, new QuantityPoint3<FltX>(M(0), M(0), M(offset))));
            offset += layerDepth;
        }
        var binType = new BinType<FltX>("B1", M(2), M(2), M(binDepth), Kg(100));
        return new Bin<BinLayer, FltX>(binType, placements);
    }

    /// <summary>**层深是逐层累加的，三层各一半合起来超深就被拒** / Layer depths accumulate: three half-depth layers that exceed the bin are rejected.</summary>
    /// <remarks>
    /// 每一层单独看都小于箱深（5 < 8），**只看单层或只看最后一层都会放过**。所以夹具必须是三层而不是一层。
    ///
    /// Each layer alone is shallower than the bin (5 < 8), so **looking at one layer or at the last layer lets it through**. The fixture therefore has to
    /// use three layers rather than one.
    /// </remarks>
    [Fact]
    public async Task FinalSolver_ShouldRejectBinsWhoseLayersExceedTheDepth() {
        ColumnGenerationStandardExecutors executors = Executors(
            DeepBin(8, 5, 5, 5), new ColumnGenerationStandardExecutorConfig());

        Result<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> result =
            await executors.FinalSolver()(State());

        result.IsOk.Should().BeFalse();
        Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().Contain("exceeds its depth");
        failed.Message.Should().Contain("bin[0]");
    }

    /// <summary>**层深放得下时通过深度校验** / Layer depths that fit pass the depth check.</summary>
    [Fact]
    public async Task FinalSolver_ShouldLetFittingLayersThrough() {
        ColumnGenerationStandardExecutors executors = Executors(
            DeepBin(8, 2, 2, 2), new ColumnGenerationStandardExecutorConfig());

        Result<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> result =
            await executors.FinalSolver()(State());

        result.IsOk.Should().BeFalse();
        Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().NotContain("exceeds its depth");
    }

    /// <summary>**关掉深度开关后不再校验** / The depth check disappears when its switch is off.</summary>
    [Fact]
    public async Task FinalSolver_ShouldSkipTheDepthCheckWhenDisabled() {
        ColumnGenerationStandardExecutors executors = Executors(
            DeepBin(8, 5, 5, 5),
            new ColumnGenerationStandardExecutorConfig(EnableFinalBinDepthConstraint: false));

        Result<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> result =
            await executors.FinalSolver()(State());

        result.IsOk.Should().BeFalse();
        Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().NotContain("exceeds its depth");
    }

    /// <summary>一律失败的求解器 / A solver that always fails.</summary>
    private sealed class RejectingSolver : IColumnGenerationSolver {
        public string Name => "rejecting";

        private static Task<Result<T, ErrorCode, Error<ErrorCode>>> Fail<T>() =>
            Task.FromResult(
                (Result<T, ErrorCode, Error<ErrorCode>>)new Failed<T, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.Unknown, "no solve in this fixture")));

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) => Fail<FeasibleSolverOutput<Flt64>>();

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>>
            SolveMILPAsync(
                string name,
                LinearMetaModel<Flt64> metaModel,
                ulong amount,
                bool toLogModel = false,
                RegistrationStatusCallBack? registrationStatusCallBack = null,
                SolvingStatusCallBack? solvingStatusCallBack = null) =>
            Fail<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool)>();

        public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> => Fail<FeasibleSolverOutput<V>>();

        public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) => Fail<IColumnGenerationSolver.LpResult>();

        public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> => Fail<IColumnGenerationSolver.LpResultOf<V>>();
    }
}
