#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchSelection;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// Demo4 pricing, shadow-price extraction, and CG iteration tests.
/// </summary>
public class Demo4PricingTests {
    #region Shadow Price Map Tests

    [Fact]
    public void FlightShadowPriceMap_InitiallyEmpty_InvokeReturnsZero() {
        var map = new FlightShadowPriceMap();
        // Invoke with null tasks should return zero
        Flt64 result = map.Invoke(null, null);
        Assert.Equal(Flt64.Zero, result);
    }

    [Fact]
    public void FlightShadowPriceMap_PutShadowPrice_AndRetrieve() {
        var map = new FlightShadowPriceMap();
        var key = new Fuookami.Ospf.Framework.Model.ShadowPriceKey(typeof(FlightShadowPriceMap));
        map.Put(new Fuookami.Ospf.Framework.Model.ShadowPrice(key, new Flt64(42.0)));

        Assert.NotNull(map[key]);
        Assert.Equal(new Flt64(42.0), map[key]!.Price);
    }

    [Fact]
    public void FlightShadowPriceMap_Invoke_NullTasks_ReturnsZero() {
        var map = new FlightShadowPriceMap();
        Flt64 result = map.Invoke(null, null);
        Assert.Equal(Flt64.Zero, result);
    }

    [Fact]
    public void FlightShadowPriceMap_Shrink_RemovesZeroValues() {
        var map = new FlightShadowPriceMap();
        var key1 = new Fuookami.Ospf.Framework.Model.ShadowPriceKey(typeof(string));
        var key2 = new Fuookami.Ospf.Framework.Model.ShadowPriceKey(typeof(int));

        map.Put(new Fuookami.Ospf.Framework.Model.ShadowPrice(key1, Flt64.Zero));
        map.Put(new Fuookami.Ospf.Framework.Model.ShadowPrice(key2, new Flt64(10.0)));

        Assert.Equal(2, map.Map.Count);

        map.Shrink();

        Assert.Single(map.Map);
        Assert.NotNull(map[key2]);
    }

    #endregion

    #region Parameter Tests

    [Fact]
    public void Parameter_DefaultValues() {
        var p = new Parameter();
        Assert.Equal(new Flt64(60.0), p.ResolvedFleetBalanceSlack);
        Assert.Equal(new Flt64(600.0), p.ResolvedFleetBalanceBaseSlack);
        Assert.Equal(Flt64.Zero, p.ResolvedExecutorLeisureCoeff);
        Assert.Equal(new Flt64(9999.0), p.ResolvedTaskCancelCoeff);
    }

    [Fact]
    public void Parameter_CustomValues() {
        var p = new Parameter(
            FleetBalanceSlack: new Flt64(100.0),
            FleetBalanceBaseSlack: new Flt64(500.0),
            ExecutorLeisureCoeff: new Flt64(1.0),
            TaskCancelCoeff: new Flt64(5000.0));
        Assert.Equal(new Flt64(100.0), p.ResolvedFleetBalanceSlack);
        Assert.Equal(new Flt64(500.0), p.ResolvedFleetBalanceBaseSlack);
        Assert.Equal(new Flt64(1.0), p.ResolvedExecutorLeisureCoeff);
        Assert.Equal(new Flt64(5000.0), p.ResolvedTaskCancelCoeff);
    }

    [Fact]
    public void Parameter_PartialOverride() {
        var p = new Parameter(TaskCancelCoeff: new Flt64(7777.0));
        Assert.Equal(new Flt64(60.0), p.ResolvedFleetBalanceSlack); // default
        Assert.Equal(new Flt64(7777.0), p.ResolvedTaskCancelCoeff); // custom
    }

    #endregion

    #region BunchCompilationContext Tests

    [Fact]
    public void BunchCompilationContext_CanBeCreated() {
        var ctx = new BunchCompilationContext();
        Assert.NotNull(ctx);
    }

    [Fact]
    public void BunchCompilationContext_WithCustomParameter() {
        var p = new Parameter(TaskCancelCoeff: new Flt64(8888.0));
        var ctx = new BunchCompilationContext(p);
        Assert.NotNull(ctx);
    }

    [Fact]
    public void BunchCompilationContext_Aggregation_InitiallyNull() {
        var ctx = new BunchCompilationContext();
        // Aggregation is null! (null-forgiving) before SetAggregation is called
        Assert.Null(ctx.Aggregation);
    }

    #endregion

    #region Aggregation Tests

    [Fact]
    public void BunchCompilation_Aggregation_CanBeCreated() {
        var now = DateTimeOffset.UtcNow;
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(now, now.AddDays(7)),
            false,
            TimeSpan.FromHours(1));

        var aggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Aggregation(
            timeWindow,
            new List<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft>(),
            new List<FlightTask>());

        Assert.NotNull(aggregation);
        Assert.Empty(aggregation.RecoveryNeededAircrafts);
        Assert.Empty(aggregation.RecoveryNeededFlightTasks);
        Assert.NotNull(aggregation.TaskTime);
        Assert.NotNull(aggregation.Compilation);
    }

    [Fact]
    public void BunchCompilation_Aggregation_BunchesInitiallyEmpty() {
        var now = DateTimeOffset.UtcNow;
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(now, now.AddDays(7)),
            false,
            TimeSpan.FromHours(1));

        var aggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Aggregation(
            timeWindow,
            new List<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft>(),
            new List<FlightTask>());

        Assert.Empty(aggregation.Bunches);
        Assert.Empty(aggregation.LastIterationBunches);
        Assert.Empty(aggregation.RemovedBunches);
    }

    #endregion

    #region BunchSelectionContext Tests

    [Fact]
    public void BunchSelectionContext_CanBeCreated() {
        var now = DateTimeOffset.UtcNow;
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(now, now.AddDays(7)),
            false,
            TimeSpan.FromHours(1));
        var bunchGenCtx = new BunchGenerationContext();
        var bunchCompCtx = new BunchCompilationContext();

        var ctx = new BunchSelectionContext(
            aircrafts: new List<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft>(),
            recoveryNeededAircrafts: new List<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft>(),
            recoveryNeededFlightTasks: new List<FlightTask>(),
            timeWindow: timeWindow,
            bunchGenerationContext: bunchGenCtx,
            bunchCompilationContext: bunchCompCtx);

        Assert.NotNull(ctx);
        Assert.Empty(ctx.Aircrafts);
        Assert.Empty(ctx.RecoveryNeededAircrafts);
        Assert.Empty(ctx.RecoveryNeededFlightTasks);
    }

    [Fact]
    public void BunchSelectionContext_InitCompilation_Succeeds() {
        var now = DateTimeOffset.UtcNow;
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(now, now.AddDays(7)),
            false,
            TimeSpan.FromHours(1));
        var bunchGenCtx = new BunchGenerationContext();
        var bunchCompCtx = new BunchCompilationContext();

        var ctx = new BunchSelectionContext(
            aircrafts: new List<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft>(),
            recoveryNeededAircrafts: new List<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft>(),
            recoveryNeededFlightTasks: new List<FlightTask>(),
            timeWindow: timeWindow,
            bunchGenerationContext: bunchGenCtx,
            bunchCompilationContext: bunchCompCtx);

        var result = ctx.InitCompilation(Array.Empty<FlightTaskBunch>());
        Assert.True(result.IsOk);
    }

    [Fact]
    public void BunchSelectionContext_InitCompilation_SetsAggregation() {
        var now = DateTimeOffset.UtcNow;
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(now, now.AddDays(7)),
            false,
            TimeSpan.FromHours(1));
        var bunchGenCtx = new BunchGenerationContext();
        var bunchCompCtx = new BunchCompilationContext();

        var ctx = new BunchSelectionContext(
            aircrafts: new List<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft>(),
            recoveryNeededAircrafts: new List<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft>(),
            recoveryNeededFlightTasks: new List<FlightTask>(),
            timeWindow: timeWindow,
            bunchGenerationContext: bunchGenCtx,
            bunchCompilationContext: bunchCompCtx);

        ctx.InitCompilation(Array.Empty<FlightTaskBunch>());

        // After init, the aggregation should be set
        Assert.NotNull(bunchCompCtx.Aggregation);
    }

    #endregion

    #region PipelineListGenerator Tests

    [Fact]
    public void PipelineListGenerator_GeneratesPipelines() {
        var now = DateTimeOffset.UtcNow;
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(now, now.AddDays(7)),
            false,
            TimeSpan.FromHours(1));

        var aggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Aggregation(
            timeWindow,
            new List<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft>(),
            new List<FlightTask>());

        var generator = new PipelineListGenerator(aggregation);
        var result = generator.Invoke();

        Assert.True(result.IsOk);
    }

    [Fact]
    public void PipelineListGenerator_WithCustomParameter() {
        var now = DateTimeOffset.UtcNow;
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(now, now.AddDays(7)),
            false,
            TimeSpan.FromHours(1));

        var aggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Aggregation(
            timeWindow,
            new List<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft>(),
            new List<FlightTask>());

        var p = new Parameter(ExecutorLeisureCoeff: new Flt64(5.0));
        var generator = new PipelineListGenerator(aggregation, p);
        var result = generator.Invoke();

        Assert.True(result.IsOk);
    }

    #endregion

    #region Column Add/Remove Tests

    [Fact]
    public void Aggregation_AddColumns_EmptyList() {
        var now = DateTimeOffset.UtcNow;
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(now, now.AddDays(7)),
            false,
            TimeSpan.FromHours(1));

        var aggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Aggregation(
            timeWindow,
            new List<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft>(),
            new List<FlightTask>());

        var result = aggregation.AddColumns(0UL, Array.Empty<FlightTaskBunch>(), new object());
        Assert.True(result.IsOk);
        var added = ((Fuookami.Ospf.Utils.Functional.Ok<IReadOnlyList<FlightTaskBunch>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)result).Value;
        Assert.Empty(added);
    }

    [Fact]
    public void Aggregation_RemoveColumns_EmptyList() {
        var now = DateTimeOffset.UtcNow;
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(now, now.AddDays(7)),
            false,
            TimeSpan.FromHours(1));

        var aggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Aggregation(
            timeWindow,
            new List<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft>(),
            new List<FlightTask>());

        var result = aggregation.RemoveColumns(
            new Flt64(100.0),
            1000UL,
            _ => Flt64.Zero,
            new HashSet<FlightTaskBunch>(),
            new HashSet<FlightTaskBunch>(),
            new object());

        Assert.True(result.IsOk);
    }

    #endregion

    #region Shadow Price Extractor Type Tests

    [Fact]
    public void ShadowPriceExtractor_DelegateType_Exists() {
        // Verify the shadow price extractor delegate type is accessible
        var extractorType = typeof(Fuookami.Ospf.Framework.Model.ShadowPriceExtractor<
            IGanttSchedulingShadowPriceArguments<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft, FlightTaskAssignment>,
            GanttSchedulingShadowPriceMap<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft, FlightTaskAssignment>>);
        Assert.NotNull(extractorType);
    }

    [Fact]
    public void GanttSchedulingShadowPriceMap_Type_Exists() {
        var mapType = typeof(GanttSchedulingShadowPriceMap<
            Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft,
            FlightTaskAssignment>);
        Assert.NotNull(mapType);
    }

    #endregion

    #region BunchGenerationContext Tests

    [Fact]
    public void BunchGenerationContext_InitialFlightBunches_Empty() {
        var ctx = new BunchGenerationContext();
        Assert.Empty(ctx.InitialFlightBunches);
    }

    #endregion
}
