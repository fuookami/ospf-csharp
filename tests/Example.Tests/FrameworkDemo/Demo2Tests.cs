#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

public class Demo2InfrastructureTests
{
    [Fact]
    public void StowageMode_ShouldHaveCorrectProperties()
    {
        // Assert
        StowageMode.Predistribution.WithMacOptimization().Should().BeTrue();
        StowageMode.Predistribution.WithSoftSecurity().Should().BeTrue();
        StowageMode.Predistribution.WithPayloadMaximization().Should().BeFalse();

        StowageMode.FullLoad.WithMacOptimization().Should().BeTrue();
        StowageMode.FullLoad.WithSoftSecurity().Should().BeTrue();
        StowageMode.FullLoad.WithPayloadMaximization().Should().BeFalse();

        StowageMode.WeightRecommendation.WithMacOptimization().Should().BeFalse();
        StowageMode.WeightRecommendation.WithSoftSecurity().Should().BeFalse();
        StowageMode.WeightRecommendation.WithPayloadMaximization().Should().BeTrue();
    }

    [Fact]
    public void SolveMode_Milp_ShouldBeSealed()
    {
        var mode = new SolveMode.Milp();
        mode.Should().BeOfType<SolveMode.Milp>();
    }

    [Fact]
    public void SolveMode_Benders_ShouldHoldConfig()
    {
        var config = new EffectiveBendersAdaptiveConfig(4, 64, 1e-6, 8, 4);
        var mode = new SolveMode.Benders(config);
        mode.Config.Should().Be(config);
    }

    [Fact]
    public void BendersStrategy_TuneAdaptiveConfig_ShouldBoostIterations()
    {
        var config = new BendersAdaptiveConfig(MinBinaryVariables: 4, MaxIterations: 64, Tolerance: 1e-6);
        var tuned = BendersStrategy.TuneAdaptiveConfig(config, binaryVariables: 200);
        tuned.MaxIterations.Should().BeGreaterThan(config.MaxIterations);
        tuned.Tolerance.Should().BeGreaterThanOrEqualTo(config.Tolerance);
    }

    [Fact]
    public void BendersStrategy_TuneAdaptiveConfig_ShouldNotBoostSmallProblems()
    {
        var config = new BendersAdaptiveConfig(MinBinaryVariables: 4, MaxIterations: 64, Tolerance: 1e-6);
        var tuned = BendersStrategy.TuneAdaptiveConfig(config, binaryVariables: 10);
        tuned.MaxIterations.Should().Be(config.MaxIterations);
    }

    [Fact]
    public void BendersStrategy_ResolveSolveMode_MilpPath()
    {
        var request = AircraftLoadingDemo.CreateSampleRequest();
        var notes = new System.Collections.Generic.List<string>();
        var mode = BendersStrategy.ResolveSolveMode(request, notes);
        mode.Should().BeOfType<SolveMode.Milp>();
    }

    [Fact]
    public void BendersStrategy_ResolveSolveMode_BendersPath()
    {
        var request = AircraftLoadingDemo.CreateSampleRequest() with
        {
            SolvePolicy = new SolvePolicy(PreferBenders: true),
            BendersAdaptive = new BendersAdaptiveConfig(MinBinaryVariables: 1, MaxIterations: 32)
        };
        var notes = new System.Collections.Generic.List<string>();
        var mode = BendersStrategy.ResolveSolveMode(request, notes);
        mode.Should().BeOfType<SolveMode.Benders>();
    }

    [Fact]
    public void BendersStrategy_SupportedAircraft()
    {
        BendersStrategy.SupportedAircraft(AircraftTypeInput.B737).Should().BeTrue();
        BendersStrategy.SupportedAircraft(AircraftTypeInput.B757).Should().BeTrue();
        // **/ 在是支持的**：排除 `Unknown`。
        // **/ are supported in the code** : only `Unknown` is excluded.
        BendersStrategy.SupportedAircraft(AircraftTypeInput.B767).Should().BeTrue();
        BendersStrategy.SupportedAircraft(AircraftTypeInput.B747).Should().BeTrue();
        BendersStrategy.SupportedAircraft(AircraftTypeInput.Unknown).Should().BeFalse();
    }

    [Fact]
    public void BendersStrategy_ResolveQualityGuardConfig_Defaults()
    {
        var config = BendersStrategy.ResolveQualityGuardConfig(null);
        config.WeakGapMultiplier.Should().Be(20.0);
        config.WeakGapFloor.Should().Be(1e-5);
        config.IterationPressurePercent.Should().Be(90);
    }

    [Fact]
    public void DiagnosticsHelper_BuildStructured_ShouldParseGroupedNotes()
    {
        var notes = new System.Collections.Generic.List<string>
        {
            "diagnostic|group=airworthiness|code=envelope_range_invalid|msg=test message"
        };
        var result = DiagnosticsHelper.BuildStructured(notes);
        result.Should().HaveCount(1);
        result[0].Level.Should().Be("diagnostic");
        result[0].Group.Should().Be("airworthiness");
        result[0].Code.Should().Be("envelope_range_invalid");
        result[0].Message.Should().Be("test message");
    }

    [Fact]
    public void ResponseDTO_NoSolution_ShouldHaveDiagnostics()
    {
        var notes = new System.Collections.Generic.List<string> { "test note" };
        var response = ResponseDTO.NoSolution("Infeasible", notes);
        response.Succeed.Should().BeFalse();
        response.Status.Should().Be("Infeasible");
        response.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void ResponseDTO_Optimal_ShouldHaveAssignments()
    {
        var assignments = new[] { "C1->P1", "C2->P2" };
        var notes = new System.Collections.Generic.List<string>();
        var response = ResponseDTO.Optimal(42.0, assignments, notes);
        response.Succeed.Should().BeTrue();
        response.Status.Should().Be("Optimal");
        response.Objective.Should().Be(42.0);
        response.Assignments.Should().BeEquivalentTo(assignments);
    }
}

public class Demo2DomainTests
{
    [Fact]
    public void AircraftType_ShouldClassifyCorrectly()
    {
        AircraftType.B737.IsNarrowBody().Should().BeTrue();
        AircraftType.B737.IsWideBody().Should().BeFalse();
        AircraftType.B767.IsNarrowBody().Should().BeFalse();
        AircraftType.B767.IsWideBody().Should().BeTrue();
        AircraftType.B757.BallastNeeded().Should().BeTrue();
        AircraftType.B737.BallastNeeded().Should().BeFalse();
    }

    [Fact]
    public void FlightPhase_ShouldHaveThreeValues()
    {
        System.Enum.GetValues<FlightPhase>().Should().HaveCount(3);
    }

    [Fact]
    public void ULDCode_ShouldClassifyCorrectly()
    {
        ULDCode.PAG.GetCategory().Should().Be(ULDCategory.Pallet);
        ULDCode.AKE.GetCategory().Should().Be(ULDCategory.Container);
    }

    [Fact]
    public void MAC_SemanticType_ShouldCompare()
    {
        var mac1 = new MAC(25.0);
        var mac2 = new MAC(30.0);
        mac1.PartialOrd(mac2).Should().BeOfType<Order.Less>();
        mac2.PartialOrd(mac1).Should().BeOfType<Order.Greater>();
        mac1.PartialOrd(mac1).Should().BeOfType<Order.Equal>();
    }
}

public class Demo2ApplicationTests
{
    [Fact]
    public void BuildModel_ShouldReturnOk()
    {
        // Arrange
        var demo = new AircraftLoadingDemo();

        // Act
        Result<Success, ErrorCode, Error<ErrorCode>> result = demo.BuildModel();

        // Assert
        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void Model_ShouldHaveCorrectName()
    {
        // Arrange
        AircraftLoadingDemo demo = new();

        // Act
        demo.BuildModel();

        // Assert
        demo.MetaModel.Name.Should().Be("demo2-aircraft-loading");
    }

    [Fact]
    public void Model_ShouldHaveObjective()
    {
        // Arrange
        var demo = new AircraftLoadingDemo();

        // Act
        demo.BuildModel();

        // Assert
        demo.MetaModel.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
    }

    /// <summary>
    /// **示例请求现在真的能初始化域，所以不该再有"域初始化失败"的备注** / The sample request now really initialises the domain, so no "domain init failed" note should appear.
    /// </summary>
    /// <remarks>
    /// 原来的判据钉的是 `Notes` **非空**——而它非空的唯一来源是 `Domain init failed:`（示例请求缺空间表/装载顺序/几何，被飞机初始化器挡下，
    /// 所以备注里写着"P0 stub 预期如此"）。请求修好之后那条备注不再出现，**判据于是变红：它钉的是缺口，不是能力**。
    ///
    /// 现在钉两边：**不出现"域初始化失败"** / 模型对象的类别仍然是 `Minimum`（域建起来之后模型照样能建）。
    /// `BuildModel` 成功路径上的注册与求解还没接（文件里三处 `TODO`），**那是一条独立的、早于这里的缺口**，不在这一条判据里假装已完成。
    ///
    /// The old criterion pinned `Notes` being **non-empty** — and its only source was `Domain init failed:` (the sample request lacked the space table, loading orders and geometry,
    /// so the aircraft initialiser stopped it, and the note said "expected for P0 stubs"). With the request fixed that note never appears, **so the criterion went red: it pinned a gap,
    /// not a capability.**
    ///
    /// It now pins both sides: **no "domain init failed" note**; and the meta model's category is still `Minimum` (the model is still buildable once the domain exists). Registration and
    /// solving on `BuildModel`'s success path are not wired yet (three `TODO`s in that file) — **a separate gap that predates**, not something this criterion pretends is done.
    /// </remarks>
    [Fact]
    public void BuildModel_ShouldInitialiseTheDomainInsteadOfNotingAFailure()
    {
        // Arrange
        var demo = new AircraftLoadingDemo();

        // Act
        demo.BuildModel();

        // Assert
        demo.Notes.Should().NotContain(
            note => note.Contains("Domain init failed"),
            "the sample request must clear the aircraft initialiser now");
        demo.Notes.Should().NotContain(
            note => note.Contains("Domain registration failed"),
            "the domains must register with the model now");
        demo.MetaModel.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);

        // **模型里必须真的有东西**：注册成功的唯一可观证据是 token 与约束/目标。只看"返回 Ok"会让
        // "注册一个空聚合也返回 Ok"通过——**那正是 记过的那种空壳**。
        // **The model must really hold something**: the only observable evidence of registration is tokens plus constraints/objectives. Asserting "returns Ok" alone
        // would let "registering an empty aggregation also returns Ok" pass — **exactly the shell recorded**.
        demo.MetaModel.Tokens.Tokens.Should().NotBeEmpty("registration must produce decision variables");
        // 这条断言只需要名字，用内联循环而不是 LINQ：`Select` 在这里会撞上 `Result` 的同名扩展。
        // The assertion only needs names, so an inline loop is used instead of LINQ: `Select` here collides with the `Result` extension of the same name.
        System.Collections.Generic.List<string> names = [];
        foreach (Token<Fuookami.Ospf.Math.Algebra.Number.Flt64> token in demo.MetaModel.Tokens.Tokens) {
            names.Add(token.Name);
        }
        string joined = string.Join(",", names);
        joined.Should().Contain("assignment_C1_P1", "the stowage model must be registered");
        joined.Should().Contain("occupied_P1", "the stowage model must register one occupancy variable per position");
        // 把这四层归结为"不新增 token"是错的——量错了计数器**：`Tokens.Tokens` 只装决策变量，
        // 而这四层叠的是**表达式符号**。逐层增量（约束/变量/符号）见 `Demo2RegistrationDeltaTests`，它把每一层都钉住了。
        // **’s summary that these four layers add no tokens was wrong — it read the wrong counter**: `Tokens.Tokens` holds only decision variables,
        // while these four layers stack **expression symbols**. The per-layer deltas (rows/variables/symbols) live in `Demo2RegistrationDeltaTests`, which pins every layer.

        demo.MetaModel.Constraints.Should().NotBeEmpty("registration must produce rows, not only variables");
    }

    [Fact]
    public void CreateSampleRequest_ShouldReturnValidRequest()
    {
        var request = AircraftLoadingDemo.CreateSampleRequest();
        request.Id.Should().Be("sample-001");
        request.Cargos.Should().HaveCount(3);
        request.Positions.Should().HaveCount(2);
        request.AircraftType.Should().Be(AircraftTypeInput.B737);
    }
}

public class Demo2P2ContextTests
{

    [Fact]
    public void SoftSecurityContext_Init_ShouldFailWhenUpstreamIsNotInitialised()
    {
        // **起软安全真正依赖上游聚合**，所以这条判据从"壳返回 Ok"改成"上游没初始化就明确失败"。
        // 旧判据在实现落地后反而成了"允许空壳"的许可证：它断言 `Ok`，而 `Ok` 正是空壳的形状。
        //
        // **Since soft security genuinely depends on the upstream aggregations**, so this criterion changed from "the shell returns Ok" to "an uninitialised upstream fails
        // explicitly". The old one had become a licence for the shell to stay empty: it asserted `Ok`, which is precisely the shell's shape.
        var aircraftCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.AircraftContext();
        var stowageCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.StowageContext();
        var ctx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.SoftSecurityContext();
        var request = AircraftLoadingDemo.CreateSampleRequest();

        var result = ctx.Init(aircraftCtx, stowageCtx, request);

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
            "the aircraft and stowage contexts were never initialised");
        // **消息必须点名"飞机"**：两个守卫共用"尚未初始化"四个字，只查那一半会让"把配载守卫的措辞抄成飞机"这种
        // 变异存活（实测存活过一次）。
        // **The message must name the aircraft**: both guards share the phrase "not initialised", so checking only that lets a mutant that copies the stowage guard's wording into
        // the aircraft one survive (measured once).
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Contain("飞机上下文尚未初始化");
    }

    [Fact]
    public void MacOptimizationContext_Init_ShouldFailWhenUpstreamIsNotInitialised()
    {
        // **起重心优化真正依赖三个上游聚合**，所以这条判据从"壳返回 Ok"改成"上游没初始化就明确失败"。
        // 旧判据断言 `Ok`，而 `Ok` 正是空壳的形状——实现落地后它成了"允许空壳"的许可证。
        //
        // **Since MAC optimisation genuinely depends on three upstream aggregations**, so this criterion changed from "the shell returns Ok" to "an uninitialised upstream fails
        // explicitly". The old one asserted `Ok`, which is precisely the shell's shape — after the implementation landed it became a licence for the shell to stay empty.
        var aircraftCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.AircraftContext();
        var stowageCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.StowageContext();
        var macCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.MacContext();
        var ctx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.MacOptimizationContext();
        var request = AircraftLoadingDemo.CreateSampleRequest();

        var result = ctx.Init(aircraftCtx, stowageCtx, macCtx, request);

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
            "none of the three upstream contexts was initialised");
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Contain("飞机聚合未初始化");
    }

    [Fact]
    public void PayloadMaximizationContext_Init_ShouldFailWhenUpstreamIsNotInitialised()
    {
        // **起业载最大化真正依赖上游聚合**，所以这条判据从"壳返回 Ok"改成"上游没初始化就明确失败"。
        // 旧判据断言 `Ok`，而 `Ok` 正是空壳的形状——实现落地后它成了"允许空壳"的许可证。
        //
        // **Since payload maximization genuinely depends on the upstream aggregations**, so this criterion changed from "the shell returns Ok" to "an uninitialised upstream
        // fails explicitly". The old one asserted `Ok`, which is precisely the shell's shape — after the implementation landed it became a licence for the shell to stay empty.
        var aircraftCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.AircraftContext();
        var stowageCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.StowageContext();
        var ctx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization.PayloadMaximizationContext();
        var request = AircraftLoadingDemo.CreateSampleRequest();

        var result = ctx.Init(aircraftCtx, stowageCtx, request);

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
            "the aircraft and stowage contexts were never initialised");
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Contain("飞机上下文尚未初始化");
    }

    [Fact]
    public void AirworthinessSecurityAggregation_ShouldRequireItsModels()
    {
        // 这个聚合不再是空壳：它需要 9 个构造参数才能建起来，注册也真的会往模型里加约束。
        // 此前这里钉的是"无参构造 + Register 返回 Ok"——那正是空壳的形状，判据跟着空壳一起骗人。
        // The aggregation is no longer a shell: it needs 9 constructor arguments and registration really adds constraints. This used to pin "no-arg constructor plus Register
        // returns Ok" — the shell's shape, with a criterion lying along with it.
        System.Type type = typeof(Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.AirworthinessSecurityAggregation);
        type.GetConstructor(System.Type.EmptyTypes).Should().BeNull("there is no parameterless constructor any more");
        type.GetConstructors().Should().ContainSingle("the aggregation takes its models up front");
    }

    [Fact]
    public void SoftSecurityAggregation_ShouldRequireItsModels()
    {
        // **起这个聚合不再有无参构造**：它已经真正持有七个上游模型与一个自建的 `DivideEmptyLoading`。
        // 这条判据从"壳返回 Ok"改成"壳的形态消失"——旧判据在实现落地后反而成了"允许空壳"的许可证。
        // **Since the aggregation has no parameterless constructor**: it genuinely holds seven upstream models plus a self-built `DivideEmptyLoading`. This criterion changed
        // from "the shell returns Ok" to "the shell's shape is gone" — the old one had become a licence for the shell to stay empty.
        System.Type type = typeof(Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.SoftSecurityAggregation);
        type.GetConstructor(System.Type.EmptyTypes).Should().BeNull("there is no parameterless constructor any more");
        type.GetConstructors().Should().ContainSingle("the aggregation takes its models up front");
    }

    [Fact]
    public void MacOptimizationAggregation_ShouldRequireItsModels()
    {
        // **起这个聚合不再有无参构造**：它真正持有飞机型号、公式、总重、扭矩与安定面，并自建三个模型。
        // 旧判据只断言"`Register` 返回 Ok"，而 `Ok` 正是空壳的形状——实现落地后它成了"允许空壳"的许可证。
        // **Since the aggregation has no parameterless constructor**: it holds the aircraft model, formula, total weight, torque and stabilizers, and builds three models.
        // The old criterion only asserted "`Register` returns Ok", which is precisely the shell's shape — after the implementation landed it became a licence for the shell to stay empty.
        System.Type type = typeof(Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.MacOptimizationAggregation);
        type.GetConstructor(System.Type.EmptyTypes).Should().BeNull("there is no parameterless constructor any more");
        type.GetConstructors().Should().ContainSingle("the aggregation takes its inputs up front");
    }

    [Fact]
    public void PayloadMaximizationAggregation_ShouldRequireItsModels()
    {
        // **起这个聚合不再有无参构造**：它真正持有飞机型号与业载模型。旧判据只断言"能创建"，
        // 那在空壳阶段为真，实现落地后就成了"允许空壳"的许可证。
        // **Since the aggregation has no parameterless constructor**: it genuinely holds the aircraft model and the payload. The old criterion only asserted "creatable", true
        // while the shell existed and a licence for it to stay empty afterwards.
        System.Type type = typeof(Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization.PayloadMaximizationAggregation);
        type.GetConstructor(System.Type.EmptyTypes).Should().BeNull("there is no parameterless constructor any more");
        type.GetConstructors().Should().ContainSingle("the aggregation takes its two models up front");
    }
}

public class Demo2ApplicationEntryTests
{
    [Fact]
    public void FullLoadApplication_Execute_ShouldReturnResponse()
    {
        // Arrange
        var app = new FullLoadApplication();
        var request = AircraftLoadingDemo.CreateSampleRequest();

        // Act
        var (response, render) = app.Execute(request);

        // Assert
        response.Should().NotBeNull();
    }

    [Fact]
    public void PredistributionApplication_Execute_ShouldReturnResponse()
    {
        // Arrange
        var app = new PredistributionApplication();
        var request = AircraftLoadingDemo.CreateSampleRequest();

        // Act
        var (response, render) = app.Execute(request);

        // Assert
        response.Should().NotBeNull();
    }

    [Fact]
    public void WeightRecommendationApplication_Execute_ShouldReturnResponse()
    {
        // Arrange
        var app = new WeightRecommendationApplication();
        var request = AircraftLoadingDemo.CreateSampleRequest();

        // Act
        var (response, render) = app.Execute(request);

        // Assert
        response.Should().NotBeNull();
    }

    [Fact]
    public void LoadingOrderApplication_Execute_ShouldReturnResponse()
    {
        // Arrange
        var app = new LoadingOrderApplication();
        var request = AircraftLoadingDemo.CreateSampleRequest();

        // Act
        var response = app.Execute(request);

        // Assert
        response.Should().NotBeNull();
    }
}

public class Demo2BendersTests
{
    [Fact]
    public void BendersSolver_Result_ShouldHaveRuntimeMetrics()
    {
        // Arrange
        var snapshot = new BendersIterationSnapshot(MasterObj: 10.0, Gap: 0.01);
        var metrics = new BendersRuntimeMetrics(
            ExecutedIterations: 5,
            TotalCuts: 12,
            IterationSnapshots: new[] { snapshot });

        // Assert
        metrics.ExecutedIterations.Should().Be(5);
        metrics.TotalCuts.Should().Be(12);
        metrics.IterationSnapshots.Should().HaveCount(1);
        metrics.IterationSnapshots[0].MasterObj.Should().Be(10.0);
    }

    [Fact]
    public void BendersResult_ShouldHoldAllFields()
    {
        // Arrange
        var result = new BendersResult(
            Obj: 42.0,
            Solution: new[] { 1.0, 0.0, 1.0 },
            Gap: 0.001,
            TimeMs: 1500,
            BendersIterations: 3,
            RuntimeMetrics: new BendersRuntimeMetrics(3, 6, Array.Empty<BendersIterationSnapshot>()));

        // Assert
        result.Obj.Should().Be(42.0);
        result.Solution.Should().HaveCount(3);
        result.Gap.Should().Be(0.001);
        result.BendersIterations.Should().Be(3);
        result.RuntimeMetrics.Should().NotBeNull();
    }

    [Fact]
    public void BendersStrategy_ResolveQualityScore_ShouldReturnScore()
    {
        // Arrange
        var adaptive = new EffectiveBendersAdaptiveConfig(4, 64, 1e-6, 8, 4);
        var qualityGuard = new BendersQualityGuardConfig();

        // Act - good convergence should give low score
        double goodScore = BendersStrategy.ResolveQualityScore(
            adaptive, qualityGuard,
            bendersIterations: 3, bendersGap: 1e-7, bendersTimeMs: 100);

        // Act - poor convergence should give high score
        double poorScore = BendersStrategy.ResolveQualityScore(
            adaptive, qualityGuard,
            bendersIterations: 60, bendersGap: 0.01, bendersTimeMs: 10000);

        // Assert
        goodScore.Should().BeLessThan(poorScore);
        // **`>= 0` 与 `<= 100` 是空断言**：`ResolveQualityScore` 最后一行就是 `Clamp(0, 100)`，
        // 这两句在那之后恒真。换成"好的一半都不到"，那才是能红的东西。
        //
        // **`>= 0` and `<= 100` were vacuous**: the last line of `ResolveQualityScore` is `Clamp(0, 100)`, so both held unconditionally. "Less than half"
        // is something that can actually fail.
        goodScore.Should().BeLessThan(poorScore / 2.0, "a run that converged in 3 iterations must be far better than one that used 60");
    }

    [Fact]
    public void BendersStrategy_ResolveQualityScore_WithSnapshots_ShouldAccountForTrajectory()
    {
        // Arrange
        var adaptive = new EffectiveBendersAdaptiveConfig(4, 64, 1e-6, 8, 4);
        var qualityGuard = new BendersQualityGuardConfig();

        var improvingSnapshots = new List<double> { 100.0, 90.0, 80.0, 70.0, 60.0, 50.0 };
        var stagnantSnapshots = new List<double> { 100.0, 99.9, 99.8, 99.7, 99.6, 99.5 };

        // Act
        double improvingScore = BendersStrategy.ResolveQualityScore(
            adaptive, qualityGuard,
            bendersIterations: 6, bendersGap: 0.001, bendersTimeMs: 500,
            iterationSnapshots: improvingSnapshots);

        double stagnantScore = BendersStrategy.ResolveQualityScore(
            adaptive, qualityGuard,
            bendersIterations: 6, bendersGap: 0.001, bendersTimeMs: 500,
            iterationSnapshots: stagnantSnapshots);

        // Assert - stagnant trajectory should give higher (worse) score
        // **必须是严格的 `>`**：`>=` 在"轨迹风险完全没被计入"时也成立，等于没测这一项。
        // **Strictly `>`**: `>=` also holds when the trajectory risk is not counted at all, which tests nothing.
        stagnantScore.Should().BeGreaterThan(improvingScore, "with equal gap, time and iterations only the trajectory term can differ");
    }

    [Fact]
    public void FullLoadApplication_WithBendersMode_ShouldResolveSolveMode()
    {
        // Arrange
        var request = AircraftLoadingDemo.CreateSampleRequest() with
        {
            SolvePolicy = new SolvePolicy(PreferBenders: true, BendersFallbackToMilp: true),
            BendersAdaptive = new BendersAdaptiveConfig(MinBinaryVariables: 1, MaxIterations: 32)
        };
        var notes = new System.Collections.Generic.List<string>();

        // Act
        var mode = BendersStrategy.ResolveSolveMode(request, notes);

        // Assert
        mode.Should().BeOfType<SolveMode.Benders>();
        var bendersMode = (SolveMode.Benders)mode;
        bendersMode.Config.MaxIterations.Should().BeGreaterThanOrEqualTo(32);
    }

    [Fact]
    public void FullLoadApplication_Execute_WithBendersRequest_ShouldReturnResponse()
    {
        // Arrange
        var app = new FullLoadApplication();
        var request = AircraftLoadingDemo.CreateSampleRequest() with
        {
            SolvePolicy = new SolvePolicy(PreferBenders: true, BendersFallbackToMilp: true),
            BendersAdaptive = new BendersAdaptiveConfig(MinBinaryVariables: 1, MaxIterations: 8)
        };

        // Act - no solver provided, should report unavailable
        var (response, render) = app.Execute(request, bendersSolver: null);

        // Assert
        response.Should().NotBeNull();
        response.Notes.Should().NotBeNull();
        // Without a solver, it should indicate Benders solver unavailable
        response.Succeed.Should().BeFalse();
    }

    [Fact]
    public void FullLoadApplication_Execute_WithFallback_ShouldReturnResponse()
    {
        // Arrange
        var app = new FullLoadApplication();
        var request = AircraftLoadingDemo.CreateSampleRequest() with
        {
            SolvePolicy = new SolvePolicy(PreferBenders: true, BendersFallbackToMilp: true),
            BendersAdaptive = new BendersAdaptiveConfig(MinBinaryVariables: 1, MaxIterations: 8)
        };

        // Act
        var (response, render) = app.Execute(request);

        // Assert
        response.Should().NotBeNull();
        response.Notes.Should().NotBeNull();
    }

    [Fact]
    public void BendersQualityGuardConfig_Defaults_ShouldBeReasonable()
    {
        // Arrange
        var config = new BendersQualityGuardConfig();

        // Assert
        config.WeakGapMultiplier.Should().BeGreaterThan(1.0);
        config.WeakGapFloor.Should().BeGreaterThan(0.0);
        config.IterationPressurePercent.Should().BeInRange(1, 100);
        config.CutDensityThreshold.Should().BeGreaterThanOrEqualTo(0.0);
        config.TrajectoryMinSnapshots.Should().BeGreaterThanOrEqualTo(2);
        // Weights should sum to 1.0
        double weightSum = config.ScoreGapWeight + config.ScoreTimeWeight +
            config.ScoreIterationWeight + config.ScoreCutDensityWeight + config.ScoreTrajectoryWeight;
        weightSum.Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void BendersStrategy_ResolveQualityReason_WeakGapTimeExceeded()
    {
        // Arrange
        var adaptive = new EffectiveBendersAdaptiveConfig(4, 64, 1e-6, 8, 4);
        var qualityGuard = new BendersQualityGuardConfig();

        // Act - gap is weak and time exceeds guard
        string? reason = BendersStrategy.ResolveQualityReason(
            adaptive, qualityGuard,
            bendersIterations: 10,
            bendersGap: 0.1, // weak gap
            bendersTimeMs: 999999); // very long time

        // Assert
        reason.Should().NotBeNull();
    }
}
