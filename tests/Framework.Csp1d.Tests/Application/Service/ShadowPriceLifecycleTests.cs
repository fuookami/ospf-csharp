// `Csp1dShadowPriceLifecycle` 主路径的判据 / Criteria for the `Csp1dShadowPriceLifecycle` main path.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Service.Pipeline;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Application.Service;

/// <summary>
/// **影子价格主路径：有管线时走 `Refresh`，无管线时返回空映射** / The main path: `Refresh` when pipelines exist, an empty map when none do.
/// </summary>
/// <remarks>
/// **B389 记的缺口**："`Csp1dShadowPriceLifecycle` 主路径无判据"——它是
/// `ExtractShadowPriceMap` 注释里**推荐走的那条路**，却从未被测过。
///
/// 三个可观察的分支：
///
/// | 分支 | 写错的后果 |
/// | --- | --- |
/// | `_cgPipelines.Count > 0` 才走 `Refresh` | 有管线却跳过 ⇒ **影子价格全丢** |
/// | `Refresh` 失败要向上传播 | 静默继续 ⇒ **调用方以为拿到了价格** |
/// | 转换用 `ConvertSolverValue` | 不转换 ⇒ **单位/量纲错** |
///
/// **B389's gap**: the main path had no criteria — it is **the route `ExtractShadowPriceMap` recommends** yet was never tested. Three observable branches (table above).
/// </remarks>
public class ShadowPriceLifecycleTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Product<Flt64> MakeProduct() =>
        new(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(60.0) });

    /// <summary>**有管线时，`Refresh` 回填的影子价格必须出现在结果里** / With a pipeline, the prices `Refresh` fills must appear in the result.</summary>
    /// <remarks>
    /// 一条 `YieldConstraintPipeline` 带一条约束、对偶 7.5。
    /// 若实现跳过 `Refresh`（或忘了传 `model`），**结果会是空映射**。
    ///
    /// One `YieldConstraintPipeline` with one constraint and dual 7.5. An implementation that skips `Refresh` (or forgets `model`) yields **an empty map**.
    /// </remarks>
    [Fact]
    public void ExtractFromDualSolution_ShouldRefreshThroughThePipelines() {
        var demandA = new ProductDemand<Flt64>(MakeProduct(), Millimetres(60.0));
        var demands = new List<ProductDemand<Flt64>> { demandA };
        var key = new ProductDemandShadowPriceKey("A", "mm");
        var config = new YieldModelingConfig<Flt64>(
            OverProductionPenalty: new Dictionary<ProductDemandShadowPriceKey, Flt64> { [key] = new Flt64(1.0) },
            OverProductionUpperBound: new Dictionary<ProductDemandShadowPriceKey, Flt64> { [key] = new Flt64(5.0) });
        var yieldAggregation = new YieldAggregation(config, demands);
        var model = new LinearMetaModel<Flt64>();
        yieldAggregation.Register(model).IsOk.Should().BeTrue();
        var pipeline = new YieldConstraintPipeline<Flt64>(yieldAggregation, config, demands);
        pipeline.Invoke(model).IsOk.Should().BeTrue();

        MathConstraint bound = model.Constraints.Single(c => c.Name.Contains("over_production_bound"));
        var dualSolution = new Dictionary<MathConstraint, Flt64> { [bound] = new Flt64(7.5) };

        var lifecycle = new Csp1dShadowPriceLifecycle<Flt64>(
            domainValueSample: new Flt64(0.0),
            cgPipelines: new List<ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>> { pipeline });

        Result<ShadowPriceMap<Flt64>, ErrorCode, Error<ErrorCode>> result =
            lifecycle.ExtractFromDualSolution(model, dualSolution);

        result.IsOk.Should().BeTrue();
        Flt64? price = result.Value[new YieldOverProductionBoundShadowPriceKey("A", "mm")];
        price.Should().NotBeNull("the pipeline's Refresh must have populated the framework map");
        price!.Value.ToDouble().Should().BeApproximately(
            7.5,
            1e-9,
            "the dual value must survive Refresh and the conversion");
    }

    /// <summary>**没有管线时返回空映射，不报错**——反向对照 / No pipelines means an empty map and no error — the reverse control.</summary>
    /// <remarks>
    /// 少了这条，"**无管线时也硬造价格**"的实现可能蒙混过关。
    ///
    /// Without this, an implementation that **invents prices when there are no pipelines** could slip through.
    /// </remarks>
    [Fact]
    public void ExtractFromDualSolution_ShouldReturnAnEmptyMapWithoutPipelines() {
        var model = new LinearMetaModel<Flt64>();
        var dualSolution = new Dictionary<MathConstraint, Flt64>();

        var lifecycle = new Csp1dShadowPriceLifecycle<Flt64>(domainValueSample: new Flt64(0.0));

        Result<ShadowPriceMap<Flt64>, ErrorCode, Error<ErrorCode>> result =
            lifecycle.ExtractFromDualSolution(model, dualSolution);

        result.IsOk.Should().BeTrue("no pipelines is not an error");
        result.Value[new YieldOverProductionBoundShadowPriceKey("A", "mm")].Should().BeNull(
            "with no pipelines nothing can populate the map");
    }

    /// <summary>**`Extractor()` 非 null 时，`Put(extractor)` 必须真的注册进映射** / A non-null `Extractor()` must actually be registered into the map.</summary>
    /// <remarks>
    /// **B390 记的缺口**：CSP1D 的 `Extractor()` 都返回 `(_, _) => Flt64.Zero`，
    /// 所以 `Put(extractor)` 这条路径**从未被走到**。
    ///
    /// 这里用一个**自定义管线**，它的 `Extractor()` 返回 `map.Invoke(arg) + 3`——
    /// 只要它被注册，`Invoke` 的结果就会**多出 3**。
    ///
    /// | 实现错法 | 结果 |
    /// | --- | --- |
    /// | 不调用 `Put(extractor)` | 0 |
    /// | 正确 | **3** |
    ///
    /// **B390's gap**: every CSP1D `Extractor()` returns `(_, _) => Flt64.Zero`, so the `Put(extractor)` path **was never exercised**. This uses a **custom pipeline**
    /// whose `Extractor()` returns `map.Invoke(arg) + 3`: if it gets registered, `Invoke` gains **3** (table above).
    /// </remarks>
    [Fact]
    public void ExtractFromDualSolution_ShouldRegisterANonNullExtractor() {
        var model = new LinearMetaModel<Flt64>();
        var dualSolution = new Dictionary<MathConstraint, Flt64>();
        var pipeline = new ExtractorPipeline();

        var lifecycle = new Csp1dShadowPriceLifecycle<Flt64>(
            domainValueSample: new Flt64(0.0),
            cgPipelines: new List<ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>> { pipeline });

        Result<ShadowPriceMap<Flt64>, ErrorCode, Error<ErrorCode>> result =
            lifecycle.ExtractFromDualSolution(model, dualSolution);

        result.IsOk.Should().BeTrue();
        pipeline.ExtractorCallCount.Should().BeGreaterThan(
            0,
            "the lifecycle must ask every pipeline for its extractor");

        // **必须验证"真的被注册"，不只是"被问过"。** 第一版只数调用次数，
        // 于是"问了但没 `Put`"的变异**存活**——`Extractor()` 照样被调用，判据照样绿。
        //
        // `FrameworkShadowPriceMap.Invoke(arg)` 会把**所有已注册提取器**的结果相加。
        // 自定义提取器返回 `7`，所以注册成功后 `Invoke` 必须给出 **7**。
        //
        // **Verify it was actually registered, not merely asked for.** The first version only counted calls, so a mutant that **asks but never `Put`s** survived.
        // `FrameworkShadowPriceMap.Invoke(arg)` sums **every registered extractor**, and the custom one returns `7`, so a successful registration must make `Invoke` yield **7**.
        var argumentPlan = new CuttingPlan<Flt64>(
            id: "arg-plan",
            material: new Material<Flt64>(
                id: "M1",
                name: "material-1",
                widthRange: new WidthRange<Flt64>(
                    Width: new QuantityRange<Flt64>(
                        LowerBound: Millimetres(6000.0),
                        UpperBound: Millimetres(6000.0)),
                    Step: Millimetres(1.0)),
                length: Millimetres(6000.0)),
            slices: new List<CuttingPlanSlice<Flt64>>());
        lifecycle.FrameworkShadowPriceMap.Invoke(new Csp1dCuttingPlanShadowPriceArguments<Flt64>(argumentPlan)).ToDouble()
            .Should().BeApproximately(
                7.0,
                1e-9,
                "the extractor must be registered so Invoke sees its contribution");
    }

    /// <summary>**`Extractor()` 被调用恰好一次**——反向对照 / `Extractor()` is called exactly once — the reverse control.</summary>
    /// <remarks>
    /// 少了这条，"**根本不问提取器**"的实现也可能蒙混过关。
    ///
    /// Without this, an implementation that **never asks for the extractor** could slip through.
    /// </remarks>
    [Fact]
    public void ExtractFromDualSolution_ShouldAskEachPipelineForItsExtractorOnce() {
        var model = new LinearMetaModel<Flt64>();
        var dualSolution = new Dictionary<MathConstraint, Flt64>();
        var first = new ExtractorPipeline();
        var second = new ExtractorPipeline();

        var lifecycle = new Csp1dShadowPriceLifecycle<Flt64>(
            domainValueSample: new Flt64(0.0),
            cgPipelines: new List<ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>> { first, second });

        lifecycle.ExtractFromDualSolution(model, dualSolution).IsOk.Should().BeTrue();

        first.ExtractorCallCount.Should().Be(1, "each pipeline is asked exactly once");
        second.ExtractorCallCount.Should().Be(1, "each pipeline is asked exactly once");
    }

    /// <summary>**`Refresh` 失败必须向上传播，不能被吞掉** / A failing `Refresh` must propagate, not be swallowed.</summary>
    /// <remarks>
    /// **B390 记的缺口**：判据里 `Refresh` 总是成功，所以"失败被吞掉"没测过。
    ///
    /// 后果很具体：`Refresh` 失败意味着**影子价格没算出来**。若被吞掉，
    /// 调用方拿到一个**看似成功但为空**的映射，**定价阶段就用零对偶去算 reduced cost**——
    /// 而且**没有任何报错**。
    ///
    /// **B390's gap**: `Refresh` always succeeded in the criteria, so "failure swallowed" was untested. The consequence is concrete: a failed `Refresh` means **no shadow
    /// prices were computed**; swallowing it hands the caller a **seemingly successful but empty** map, and **pricing then computes reduced cost from zero duals with no
    /// error at all**.
    /// </remarks>
    [Fact]
    public void ExtractFromDualSolution_ShouldPropagateARefreshFailure() {
        var model = new LinearMetaModel<Flt64>();
        var dualSolution = new Dictionary<MathConstraint, Flt64>();
        var failing = new FailingRefreshPipeline();

        var lifecycle = new Csp1dShadowPriceLifecycle<Flt64>(
            domainValueSample: new Flt64(0.0),
            cgPipelines: new List<ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>> { failing });

        Result<ShadowPriceMap<Flt64>, ErrorCode, Error<ErrorCode>> result =
            lifecycle.ExtractFromDualSolution(model, dualSolution);

        result.IsFailed.Should().BeTrue(
            "a failed Refresh must surface as a failed extraction, not an empty successful map");
    }

    /// <summary>**`Refresh` 返回 `Fatal` 时也必须向上传播** / A `Fatal` from `Refresh` must propagate too.</summary>
    /// <remarks>
    /// **B391 记的缺口**："`Fatal` 分支无判据"。
    ///
    /// `Result` 有三种情况：`Ok` / `Failed` / **`Fatal`**。`Fatal` 表示**不可恢复**的错误
    /// （一组错误而非单个）。CSP1D 的 `Refresh` 默认实现**从不返回 `Fatal`**，
    /// 所以这条分支在生产路径上走不到——**但它是接口允许的返回值**，
    /// 任何自定义管线都可以返回它。
    ///
    /// 若实现把 `Fatal` 当成 `Ok` 继续往下走，调用方会拿到一个
    /// **看似成功的空映射**，而真正的错误（可能是多条）**全部丢失**。
    ///
    /// **B391's gap**: "the `Fatal` branch has no criteria". `Result` has three cases — `Ok` / `Failed` / **`Fatal`** — and `Fatal` means **unrecoverable** (a list of errors
    /// rather than one). CSP1D's default `Refresh` **never returns it**, so the branch is unreachable on the production path — **but the interface permits it**, and any custom
    /// pipeline may return it. Treating `Fatal` as `Ok` hands the caller a **seemingly successful empty map** while the real errors (**possibly several**) are **all lost**.
    /// </remarks>
    [Fact]
    public void ExtractFromDualSolution_ShouldPropagateAFatalRefresh() {
        var model = new LinearMetaModel<Flt64>();
        var dualSolution = new Dictionary<MathConstraint, Flt64>();
        var fatal = new FatalRefreshPipeline();

        var lifecycle = new Csp1dShadowPriceLifecycle<Flt64>(
            domainValueSample: new Flt64(0.0),
            cgPipelines: new List<ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>> { fatal });

        Result<ShadowPriceMap<Flt64>, ErrorCode, Error<ErrorCode>> result =
            lifecycle.ExtractFromDualSolution(model, dualSolution);

        result.Should().BeOfType<Fatal<ShadowPriceMap<Flt64>, ErrorCode, Error<ErrorCode>>>(
            "a Fatal refresh must surface as a Fatal extraction, keeping every error");
        ((Fatal<ShadowPriceMap<Flt64>, ErrorCode, Error<ErrorCode>>)result).Errors
            .Should().HaveCount(2, "both errors must survive, not just the first");
    }

    /// <summary>**`Extractor()` 返回 null 时不注册任何东西** / A null `Extractor()` registers nothing.</summary>
    /// <remarks>
    /// **B391 记的缺口**："`Extractor()` 返回 null 时不 `Put` 这条没单独断言"。
    ///
    /// 若实现对 null 也调 `Put`，`Invoke` 会因为**调用一个 null 委托**而抛异常——
    /// 但那是在**定价阶段**才炸，离这里很远。
    ///
    /// **B391's gap**: "the null `Extractor()` case was not asserted separately". If the implementation `Put`s a null, `Invoke` throws **when calling a null delegate** — but
    /// that blows up **during pricing**, far away from here.
    /// </remarks>
    [Fact]
    public void ExtractFromDualSolution_ShouldRegisterNothingWhenTheExtractorIsNull() {
        var model = new LinearMetaModel<Flt64>();
        var dualSolution = new Dictionary<MathConstraint, Flt64>();
        var nullExtractor = new NullExtractorPipeline();

        var lifecycle = new Csp1dShadowPriceLifecycle<Flt64>(
            domainValueSample: new Flt64(0.0),
            cgPipelines: new List<ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>> { nullExtractor });

        lifecycle.ExtractFromDualSolution(model, dualSolution).IsOk.Should().BeTrue();
        nullExtractor.ExtractorCallCount.Should().Be(1);

        var argumentPlan = new CuttingPlan<Flt64>(
            id: "arg-plan",
            material: new Material<Flt64>(
                id: "M1",
                name: "material-1",
                widthRange: new WidthRange<Flt64>(
                    Width: new QuantityRange<Flt64>(
                        LowerBound: Millimetres(6000.0),
                        UpperBound: Millimetres(6000.0)),
                    Step: Millimetres(1.0)),
                length: Millimetres(6000.0)),
            slices: new List<CuttingPlanSlice<Flt64>>());
        lifecycle.FrameworkShadowPriceMap
            .Invoke(new Csp1dCuttingPlanShadowPriceArguments<Flt64>(argumentPlan))
            .ToDouble()
            .Should().BeApproximately(
                0.0,
                1e-9,
                "a null extractor contributes nothing, and invoking must not throw");
    }

    /// <summary>**`Int64` 实例化时 `ConvertDualValue` 必须抛出，且异常信息要指出类型** / With `Int64`, `ConvertDualValue` must throw and name the type.</summary>
    /// <remarks>
    /// **B393 记的缺口**："`Int64` / `Flt32` 实例化 CSP1D 会在运行期抛异常，本批只记录未修"。
    ///
    /// 本批查了基线：`extractFromDualSolution` 写的是
    /// `(convertSolverValue(...) as Ok).value`——`Failed` 时 `as Ok` 得 null、`.value` 立刻抛。
    /// **所以抛异常与基线等价，不是 C# 偏离。**
    ///
    /// 这条判据钉住的是**异常信息的质量**：抛异常时至少要**说出类型名**，
    /// 否则用户拿到 `InvalidOperationException` 却不知道是 `Int64` 还是 `Flt32` 的问题。
    ///
    /// | 实现 | 结果 |
    /// | --- | --- |
    /// | 静默返回 `default` | **判据红**（不抛） |
    /// | 抛但不带类型名 | **判据红**（信息缺失） |
    /// | 抛且带类型名 | **绿** |
    ///
    /// **B393's gap**: "`Int64` / `Flt32` blow up at runtime; recorded but not fixed". This batch read the baseline: `extractFromDualSolution` writes
    /// `(convertSolverValue(...) as Ok).value`, so `Failed` yields null and `.value` throws — **the throw is baseline parity, not a C# deviation**. This criterion pins
    /// **the quality of the message**: it must at least **name the type**, or users get an `InvalidOperationException` without knowing whether `Int64` or `Flt32` is at
    /// fault (table above).
    /// </remarks>
    [Fact]
    public void ConvertDualValue_ShouldThrowNamingTheTypeForInt64() {
        var lifecycle = new Csp1dShadowPriceLifecycle<Int64>(domainValueSample: new Int64(0));

        global::System.Action convert = () => lifecycle.ConvertDualValue(new Flt64(1.5));

        convert.Should().Throw<global::System.InvalidOperationException>()
            .WithMessage("*Int64*", "the message must name the offending type so the failure is diagnosable");
    }

    /// <summary>`Flt64` 实例化时 `ConvertDualValue` 正常返回——反向对照 / With `Flt64` it returns normally — the reverse control.</summary>
    /// <remarks>
    /// 少了这条，"**恒抛异常**"的实现也能满足上一条。
    ///
    /// Without this, an implementation that **always throws** would also satisfy the previous criterion.
    /// </remarks>
    [Fact]
    public void ConvertDualValue_ShouldReturnNormallyForFlt64() {
        var lifecycle = new Csp1dShadowPriceLifecycle<Flt64>(domainValueSample: new Flt64(0.0));

        lifecycle.ConvertDualValue(new Flt64(1.5)).ToDouble().Should().BeApproximately(1.5, 1e-9);
    }

    /// <summary>`Refresh` 返回 `Fatal` 的管线 / A pipeline whose `Refresh` returns `Fatal`.</summary>
    private sealed class FatalRefreshPipeline : ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>> {
        public Try Invoke(LinearMetaModel<Flt64> model) => Results.Ok<Success>(Results.SuccessInstance);

        Try ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>.Refresh(
            AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments> shadowPriceMap,
            LinearMetaModel<Flt64> model,
            MetaDualSolution shadowPrices) => new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                new List<Error<ErrorCode>> {
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, "first"),
                    new Err<ErrorCode>(ErrorCode.Unknown, "second"),
                });
    }

    /// <summary>`Extractor()` 返回 null 的管线 / A pipeline whose `Extractor()` returns null.</summary>
    private sealed class NullExtractorPipeline : ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>> {
        public int ExtractorCallCount { get; private set; }

        public Try Invoke(LinearMetaModel<Flt64> model) => Results.Ok<Success>(Results.SuccessInstance);

        public ShadowPriceExtractor<AbstractCsp1dShadowPriceArguments, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>? Extractor() {
            ExtractorCallCount++;
            return null;
        }
    }

    /// <summary>`Refresh` 总是失败的管线 / A pipeline whose `Refresh` always fails.</summary>
    private sealed class FailingRefreshPipeline : ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>> {
        public Try Invoke(LinearMetaModel<Flt64> model) => Results.Ok<Success>(Results.SuccessInstance);

        Try ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>.Refresh(
            AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments> shadowPriceMap,
            LinearMetaModel<Flt64> model,
            MetaDualSolution shadowPrices) => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "refresh failed"));
    }

    /// <summary>带可观测提取器的管线 / A pipeline with an observable extractor.</summary>
    private sealed class ExtractorPipeline : ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>> {
        public int ExtractorCallCount { get; private set; }

        public Try Invoke(LinearMetaModel<Flt64> model) => Results.Ok<Success>(Results.SuccessInstance);

        public ShadowPriceExtractor<AbstractCsp1dShadowPriceArguments, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>? Extractor() {
            ExtractorCallCount++;
            // **返回非零**，这样"注册了没有"可以从 `Invoke` 的结果看出来。
            // **Non-zero**, so whether it was registered is visible in `Invoke`'s result.
            return (_, _) => new Flt64(7.0);
        }
    }

    /// <summary>**`ConvertDualValue` 与内部转换一致** / `ConvertDualValue` agrees with the internal conversion.</summary>
    /// <remarks>
    /// 公开方法 `ConvertDualValue` 与内部 `ConvertSolverValue` 必须给同一答案，
    /// 否则**调用方自己转换的值与映射里的值会不一致**。
    ///
    /// The public `ConvertDualValue` and the internal `ConvertSolverValue` must agree, or **a caller's own conversion disagrees with what the map holds**.
    /// </remarks>
    [Fact]
    public void ConvertDualValue_ShouldMatchTheMappedValue() {
        var demandA = new ProductDemand<Flt64>(MakeProduct(), Millimetres(60.0));
        var demands = new List<ProductDemand<Flt64>> { demandA };
        var key = new ProductDemandShadowPriceKey("A", "mm");
        var config = new YieldModelingConfig<Flt64>(
            OverProductionPenalty: new Dictionary<ProductDemandShadowPriceKey, Flt64> { [key] = new Flt64(1.0) },
            OverProductionUpperBound: new Dictionary<ProductDemandShadowPriceKey, Flt64> { [key] = new Flt64(5.0) });
        var yieldAggregation = new YieldAggregation(config, demands);
        var model = new LinearMetaModel<Flt64>();
        yieldAggregation.Register(model).IsOk.Should().BeTrue();
        var pipeline = new YieldConstraintPipeline<Flt64>(yieldAggregation, config, demands);
        pipeline.Invoke(model).IsOk.Should().BeTrue();

        MathConstraint bound = model.Constraints.Single(c => c.Name.Contains("over_production_bound"));
        var dualSolution = new Dictionary<MathConstraint, Flt64> { [bound] = new Flt64(7.5) };

        var lifecycle = new Csp1dShadowPriceLifecycle<Flt64>(
            domainValueSample: new Flt64(0.0),
            cgPipelines: new List<ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>> { pipeline });

        Result<ShadowPriceMap<Flt64>, ErrorCode, Error<ErrorCode>> result =
            lifecycle.ExtractFromDualSolution(model, dualSolution);
        result.IsOk.Should().BeTrue();

        Flt64? mapped = result.Value[new YieldOverProductionBoundShadowPriceKey("A", "mm")];
        mapped.Should().NotBeNull();
        mapped!.Value.ToDouble().Should().BeApproximately(
            lifecycle.ConvertDualValue(new Flt64(7.5)).ToDouble(),
            1e-9,
            "the public conversion must agree with what the map holds");
    }
}
