#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
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
        BendersStrategy.SupportedAircraft(AircraftTypeInput.B767).Should().BeFalse();
        BendersStrategy.SupportedAircraft(AircraftTypeInput.B747).Should().BeFalse();
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
        var demo = new AircraftLoadingDemo();

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

    [Fact]
    public void BuildModel_ShouldGenerateNotes()
    {
        // Arrange
        var demo = new AircraftLoadingDemo();

        // Act
        demo.BuildModel();

        // Assert
        demo.Notes.Should().NotBeEmpty();
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
    public void AirworthinessSecurityContext_Init_ShouldReturnOk()
    {
        // Arrange
        var aircraftCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.AircraftContext();
        var stowageCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.StowageContext();
        var macCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.MacContext();
        var ctx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.AirworthinessSecurityContext();
        var request = AircraftLoadingDemo.CreateSampleRequest();

        // Act
        aircraftCtx.Init(request);
        stowageCtx.Init(aircraftCtx, request);
        macCtx.Init(aircraftCtx, stowageCtx, request);
        var result = ctx.Init(aircraftCtx, stowageCtx, macCtx, request);

        // Assert
        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        ctx.Aggregation.Should().NotBeNull();
    }

    [Fact]
    public void SoftSecurityContext_Init_ShouldReturnOk()
    {
        // Arrange
        var aircraftCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.AircraftContext();
        var stowageCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.StowageContext();
        var ctx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.SoftSecurityContext();
        var request = AircraftLoadingDemo.CreateSampleRequest();

        // Act
        aircraftCtx.Init(request);
        stowageCtx.Init(aircraftCtx, request);
        var result = ctx.Init(aircraftCtx, stowageCtx, request);

        // Assert
        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        ctx.Aggregation.Should().NotBeNull();
    }

    [Fact]
    public void MacOptimizationContext_Init_ShouldReturnOk()
    {
        // Arrange
        var aircraftCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.AircraftContext();
        var stowageCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.StowageContext();
        var macCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.MacContext();
        var ctx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.MacOptimizationContext();
        var request = AircraftLoadingDemo.CreateSampleRequest();

        // Act
        aircraftCtx.Init(request);
        stowageCtx.Init(aircraftCtx, request);
        macCtx.Init(aircraftCtx, stowageCtx, request);
        var result = ctx.Init(aircraftCtx, stowageCtx, macCtx, request);

        // Assert
        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        ctx.Aggregation.Should().NotBeNull();
    }

    [Fact]
    public void PayloadMaximizationContext_Init_ShouldReturnOk()
    {
        // Arrange
        var aircraftCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.AircraftContext();
        var stowageCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.StowageContext();
        var ctx = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization.PayloadMaximizationContext();
        var request = AircraftLoadingDemo.CreateSampleRequest();

        // Act
        aircraftCtx.Init(request);
        stowageCtx.Init(aircraftCtx, request);
        var result = ctx.Init(aircraftCtx, stowageCtx, request);

        // Assert
        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        ctx.Aggregation.Should().NotBeNull();
    }

    [Fact]
    public void AirworthinessSecurityAggregation_Register_ShouldReturnOk()
    {
        // Arrange
        var aggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.AirworthinessSecurityAggregation();

        // Act
        var result = aggregation.Register(StowageMode.FullLoad, null!);

        // Assert
        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void SoftSecurityAggregation_Register_ShouldReturnOk()
    {
        // Arrange
        var aggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.SoftSecurityAggregation();

        // Act
        var result = aggregation.Register(StowageMode.FullLoad, null!);

        // Assert
        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void MacOptimizationAggregation_Register_ShouldReturnOk()
    {
        // Arrange
        var aggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.MacOptimizationAggregation();

        // Act
        var result = aggregation.Register(StowageMode.FullLoad, null!);

        // Assert
        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void PayloadMaximizationAggregation_ShouldBeCreatable()
    {
        // Act
        var aggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization.PayloadMaximizationAggregation();

        // Assert
        aggregation.Should().NotBeNull();
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
        goodScore.Should().BeGreaterThanOrEqualTo(0.0);
        poorScore.Should().BeLessThanOrEqualTo(100.0);
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
        stagnantScore.Should().BeGreaterThanOrEqualTo(improvingScore);
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
