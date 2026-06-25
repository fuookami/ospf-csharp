#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Log;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Framework.Tests;
// ===== FrameworkSolveOptions Builder Tests =====

public class FrameworkSolveOptionsTests {
    [Fact]
    public void Builder_DefaultValues_AreNull() {
        FrameworkSolveOptions options = new FrameworkSolveOptions.Builder().Build();

        options.Name.Should().BeNull();
        options.ToLogModel.Should().BeFalse();
        options.SolutionAmount.Should().BeNull();
        options.RegistrationStatusCallBack.Should().BeNull();
        options.SolvingStatusCallBack.Should().BeNull();
        options.ValueConversionPolicy.Should().BeNull();
        options.BendersIterationLimit.Should().BeNull();
        options.BendersStallIterationLimit.Should().BeNull();
    }

    [Fact]
    public void Builder_SetProperties_Propagated() {
        FrameworkSolveOptions options = new FrameworkSolveOptions.Builder {
            Name = "test-solve",
            ToLogModel = true,
            SolutionAmount = 10,
            BendersIterationLimit = 100,
            BendersStallIterationLimit = 50
        }.Build();

        options.Name.Should().Be("test-solve");
        options.ToLogModel.Should().BeTrue();
        options.SolutionAmount.Should().Be(10UL);
        options.BendersIterationLimit.Should().Be(100UL);
        options.BendersStallIterationLimit.Should().Be(50UL);
    }

    [Fact]
    public void Build_ViaDelegate_Works() {
        var options = FrameworkSolveOptions.Build(b => {
            b.Name = "delegate-build";
            b.ToLogModel = true;
        });

        options.Name.Should().Be("delegate-build");
        options.ToLogModel.Should().BeTrue();
    }

    [Fact]
    public void SolveName_ReturnsDefault_WhenNameIsNull() {
        FrameworkSolveOptions options = new FrameworkSolveOptions.Builder().Build();
        options.SolveName("fallback").Should().Be("fallback");
    }

    [Fact]
    public void SolveName_ReturnsName_WhenSet() {
        FrameworkSolveOptions options = new FrameworkSolveOptions.Builder { Name = "custom" }.Build();
        options.SolveName("fallback").Should().Be("custom");
    }

    [Fact]
    public void EffectiveValueConversionPolicy_DefaultsToStrict() {
        FrameworkSolveOptions options = new FrameworkSolveOptions.Builder().Build();
        options.EffectiveValueConversionPolicy.Should().Be(SolveValueConversionPolicy.Strict);
    }

    [Fact]
    public void ToCoreSolveOptions_MapsFields() {
        FrameworkSolveOptions options = new FrameworkSolveOptions.Builder {
            SolutionAmount = 5,
            ValueConversionPolicy = SolveValueConversionPolicy.AllowRounding
        }.Build();

        Core.Solver.SolveOptions core = options.ToCoreSolveOptions();
        core.SolutionAmount.Should().Be(5UL);
        core.ValueConversionPolicy.Should().Be(SolveValueConversionPolicy.AllowRounding);
    }
}

// ===== ShadowPriceMap Tests =====

public class TestShadowPriceMap : AbstractShadowPriceMap<string, TestShadowPriceMap> {
}

public class ShadowPriceMapTests {
    [Fact]
    public void Put_And_Get_ShadowPrice() {
        var map = new TestShadowPriceMap();
        var key = new ShadowPriceKey(typeof(string));
        var price = new ShadowPrice(key, new Flt64(3.14));

        map.Put(price);

        map[key].Should().NotBeNull();
        map[key]!.Price.Should().Be(new Flt64(3.14));
    }

    [Fact]
    public void PutOrAdd_AccumulatesPrices() {
        var map = new TestShadowPriceMap();
        var key = new ShadowPriceKey(typeof(int));

        map.PutOrAdd(new ShadowPrice(key, new Flt64(1.0)));
        map.PutOrAdd(new ShadowPrice(key, new Flt64(2.5)));

        map[key]!.Price.Should().Be(new Flt64(3.5));
    }

    [Fact]
    public void Remove_RemovesKey() {
        var map = new TestShadowPriceMap();
        var key = new ShadowPriceKey(typeof(double));
        map.Put(new ShadowPrice(key, new Flt64(1.0)));

        map.Remove(key);

        map[key].Should().BeNull();
    }

    [Fact]
    public void Shrink_RemovesZeroPrices() {
        var map = new TestShadowPriceMap();
        var key1 = new ShadowPriceKey(typeof(string));
        var key2 = new ShadowPriceKey(typeof(int));

        map.Put(new ShadowPrice(key1, Flt64.Zero));
        map.Put(new ShadowPrice(key2, new Flt64(5.0)));

        map.Shrink();

        map[key1].Should().BeNull();
        map[key2].Should().NotBeNull();
    }

    [Fact]
    public void Invoke_SumsExtractorResults() {
        var map = new TestShadowPriceMap();
        var key = new ShadowPriceKey(typeof(string));
        map.Put(new ShadowPrice(key, new Flt64(10.0)));

        map.Put((m, arg) => new Flt64(20.0));
        map.Put((m, arg) => new Flt64(30.0));

        Flt64 result = map.Invoke("test");
        result.Should().Be(new Flt64(50.0));
    }

    [Fact]
    public void Set_OverwritesExistingPrice() {
        var map = new TestShadowPriceMap();
        var key = new ShadowPriceKey(typeof(string));

        map.Set(key, new ShadowPrice(key, new Flt64(1.0)));
        map.Set(key, new ShadowPrice(key, new Flt64(2.0)));

        map[key]!.Price.Should().Be(new Flt64(2.0));
    }
}

// ===== LogContext Tests =====

public class LogContextTests {
    [Fact]
    public void Create_GeneratesServiceId() {
        var ctx = LogContext.Create(app: "test-app", version: "1.0", requestId: "req-1");

        ctx.App.Should().Be("test-app");
        ctx.Version.Should().Be("1.0");
        ctx.ServiceId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Create_SameInputs_ProducesSameServiceId() {
        var ctx1 = LogContext.Create(app: "a", version: "b", requestId: "c");
        var ctx2 = LogContext.Create(app: "a", version: "b", requestId: "c");

        ctx1.ServiceId.Should().Be(ctx2.ServiceId);
    }

    [Fact]
    public void Build_ViaBuilder_Works() {
        var ctx = LogContext.Build(b => {
            b.App = "builder-app";
            b.Version = "2.0";
            b.RequestId = "req-builder";
        });

        ctx.App.Should().Be("builder-app");
        ctx.Version.Should().Be("2.0");
    }

    [Fact]
    public async Task LogContextScope_AsyncLocal_Flows() {
        var ctx = LogContext.Create(app: "scope-test", version: "1.0", requestId: "r1");
        LogContextScope.Current = ctx;

        LogContextScope.Current.App.Should().Be("scope-test");

        // AsyncLocal flows to child async tasks
        string? childApp = null;
        var task = Task.Run(() => {
            childApp = LogContextScope.Current.App;
        });
        await task;

        childApp.Should().Be("scope-test");
    }

    [Fact]
    public async Task LogContextScope_DifferentAsyncContexts_AreIndependent() {
        var ctx1 = LogContext.Create(app: "ctx1", version: "1.0", requestId: "r1");
        var ctx2 = LogContext.Create(app: "ctx2", version: "1.0", requestId: "r2");

        string? result1 = null;
        string? result2 = null;

        var t1 = Task.Run(() => {
            LogContextScope.Current = ctx1;
            result1 = LogContextScope.Current.App;
        });

        var t2 = Task.Run(() => {
            LogContextScope.Current = ctx2;
            result2 = LogContextScope.Current.App;
        });

        await Task.WhenAll(t1, t2);

        result1.Should().Be("ctx1");
        result2.Should().Be("ctx2");
    }

    [Fact]
    public void Push_WithNullPushing_DoesNotThrow() {
        var ctx = LogContext.Create(app: "test", version: "1.0", requestId: "r1");
        // No pushing configured — should be a no-op
        Action action = () => ctx.Push("step1", new { Data = "test" });
        action.Should().NotThrow();
    }

    [Fact]
    public void Save_WithNullSaving_DoesNotThrow() {
        var ctx = LogContext.Create(app: "test", version: "1.0", requestId: "r1");
        Action action = () => ctx.Save("step1", new { Data = "test" });
        action.Should().NotThrow();
    }

    [Fact]
    public void Push_WithMockPushing_ReceivesRecord() {
        LogRecordPo<object>? received = null;
        var mockPushing = new MockPushing(po => { received = po; return Results.Ok<Success>(Results.SuccessInstance); });

        var ctx = LogContext.Create(app: "push-test", version: "1.0", requestId: "r1", pushing: mockPushing);
        ctx.Push("step1", new { Value = 42 });

        received.Should().NotBeNull();
        received!.App.Should().Be("push-test");
        received.Step.Should().Be("step1");
    }

    [Fact]
    public void Save_WithMockSaving_ReceivesRecord() {
        LogRecordPo<object>? received = null;
        var mockSaving = new MockSaving(po => { received = po; return Results.Ok<Success>(Results.SuccessInstance); });

        var ctx = LogContext.Create(app: "save-test", version: "1.0", requestId: "r1", saving: mockSaving);
        ctx.Save("step2", new { Value = 99 });

        received.Should().NotBeNull();
        received!.App.Should().Be("save-test");
        received.Step.Should().Be("step2");
    }
}

// ===== Pipeline Tests =====

public class PipelineTests {
    [Fact]
    public void Pipeline_Invoke_ReturnsOk() {
        IPipeline<string> pipeline = new TestPipeline();
        Try result = pipeline.Invoke("test-model");

        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void Pipeline_Invoke_ReturnsFailed() {
        IPipeline<string> pipeline = new FailingPipeline();
        Try result = pipeline.Invoke("test-model");

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void PipelineListInvoker_Invoke_AllSucceed() {
        var pipelines = new List<IPipeline<string>>
        {
            new TestPipeline(),
            new TestPipeline()
        };

        Try result = PipelineListInvoker.Invoke(pipelines, "model");
        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void PipelineListInvoker_Invoke_StopsOnFailure() {
        var pipelines = new List<IPipeline<string>>
        {
            new TestPipeline(),
            new FailingPipeline(),
            new TestPipeline() // Should not be reached
        };

        Try result = PipelineListInvoker.Invoke(pipelines, "model");
        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }
}

// ===== CGPipeline Tests =====

public class TestShadowPriceMapForCG : AbstractShadowPriceMap<string, TestShadowPriceMapForCG> {
}

public class CGPipelineTests {
    [Fact]
    public void CGPipeline_Refresh_WithShadowPriceKey() {
        ICGPipeline<string, string, TestShadowPriceMapForCG> pipeline = new TestCGPipeline();
        var map = new TestShadowPriceMapForCG();
        MetaDualSolution dualSolution = MetaDualSolution.Empty;

        Try result = pipeline.Refresh(map, "model", dualSolution);
        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void CGPipeline_Extractor_ReturnsNull() {
        ICGPipeline<string, string, TestShadowPriceMapForCG> pipeline = new TestCGPipeline();
        pipeline.Extractor().Should().BeNull();
    }
}

// ===== ParallelCombinatorialMode Tests =====

public class ParallelCombinatorialModeTests {
    [Fact]
    public void Enum_HasExpectedValues() {
        ((int)Fuookami.Ospf.Framework.Solver.ParallelCombinatorialMode.First).Should().Be(0);
        ((int)Fuookami.Ospf.Framework.Solver.ParallelCombinatorialMode.Best).Should().Be(1);
    }
}

// ===== Mock helpers =====

internal class TestPipeline : IPipeline<string> {
    bool IMetaConstraintGroup.Lazy => false;
    string IMetaConstraintGroup.Name => "TestPipeline";
    void IPipeline<string>.Register(string model) { }
    Try IPipeline<string>.Invoke(string model) => Results.Ok<Success>(Results.SuccessInstance);
}

internal class FailingPipeline : IPipeline<string> {
    bool IMetaConstraintGroup.Lazy => false;
    string IMetaConstraintGroup.Name => "FailingPipeline";
    void IPipeline<string>.Register(string model) { }
    Try IPipeline<string>.Invoke(string model) => new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, "intentional failure"));
}

internal class TestCGPipeline : ICGPipeline<string, string, TestShadowPriceMapForCG> {
    bool IMetaConstraintGroup.Lazy => false;
    string IMetaConstraintGroup.Name => "TestCGPipeline";
    void IPipeline<string>.Register(string model) { }
    Try IPipeline<string>.Invoke(string model) => Results.Ok<Success>(Results.SuccessInstance);
    ShadowPriceExtractor<string, TestShadowPriceMapForCG>? ICGPipeline<string, string, TestShadowPriceMapForCG>.Extractor() => null;
}

internal class MockPushing : IPushing {
    private readonly Func<LogRecordPo<object>, Try> _handler;
    public MockPushing(Func<LogRecordPo<object>, Try> handler) => _handler = handler;
    public Try Push<T>(LogRecordPo<T> value, Func<LogRecordPo<T>, string> serializer) where T : class
        => _handler(new LogRecordPo<object> {
            App = value.App,
            Version = value.Version,
            ServiceId = value.ServiceId,
            Step = value.Step,
            Type = value.Type,
            Time = value.Time,
            AvailableTime = value.AvailableTime,
            Value = value.Value
        });
}

internal class MockSaving : ISaving {
    private readonly Func<LogRecordPo<object>, Try> _handler;
    public MockSaving(Func<LogRecordPo<object>, Try> handler) => _handler = handler;
    public Try Save<T>(LogRecordPo<T> value, Func<LogRecordPo<T>, string> serializer) where T : class
        => _handler(new LogRecordPo<object> {
            App = value.App,
            Version = value.Version,
            ServiceId = value.ServiceId,
            Step = value.Step,
            Type = value.Type,
            Time = value.Time,
            AvailableTime = value.AvailableTime,
            Value = value.Value
        });
    public void Dispose() { }
}
