using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 请求里的目标优先级必须真的进到模型里 / The objective priorities in the request must really reach the model.
/// </summary>
public sealed class Demo2ParameterTests
{
    [Fact]
    public void ResolvedParameter_ShouldCarryTheRecommendationPriorities() {
        // `RequestDTO.parameter` 就干这一件事：把两个优先级送进 `Parameter`。
        // `RequestDTO.parameter` does exactly one thing: feed the two priorities into `Parameter`.
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            WeightRecommendationObjective = new WeightRecommendationObjectiveConfig(BalancePriority: 2.5, PayloadPriority: 7.5),
        };

        Parameter parameter = request.ResolvedParameter;

        parameter.WeightRecommendationBalance.ToDouble().Should().Be(2.5);
        parameter.WeightRecommendationPayload.ToDouble().Should().Be(7.5);
    }

    [Fact]
    public void ResolvedParameter_ShouldLeaveUnmappedParametersAtTheirDefaults() {
        // **只映射那两个**：别的参数留在默认值，是因为没从请求里取它们。
 // **Only the two this implementation maps are mapped**: the rest stay default because this implementation does not read them from the request either.
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            WeightRecommendationObjective = new WeightRecommendationObjectiveConfig(BalancePriority: 2.5, PayloadPriority: 7.5),
        };

        Parameter parameter = request.ResolvedParameter;

        parameter.MacRangeC.ToDouble().Should().Be(0.0);
        parameter.Priority.ToDouble().Should().Be(0.0);
        parameter.TrailerChange.ToDouble().Should().Be(0.0);
    }

    [Fact]
    public void ThePriorities_ShouldReachTheObjectiveCoefficients() {
        // **求解结果看不出来这件事**（示例请求在默认与极端优先级下都是 `obj = 0`、32 个非零——实测过），
        // 所以判据落在**模型的系数**上：`payloadPriority = 3` 必须在某个目标项里留下系数 3。
        // 只钉解值的话，这个缺口会一直是绿的。
        //
        // **The solve result cannot show this** (measured: the sample gives `obj = 0` and 32 non-zero entries under both default and extreme priorities), so the
        // criterion lands on **the model's coefficients**: `payloadPriority = 3` must leave a coefficient of 3 in some objective part. Pinning only the solution
        // would leave this gap green forever.
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            WeightRecommendationObjective = new WeightRecommendationObjectiveConfig(PayloadPriority: 3.0),
        };
        var pipeline = new DomainPipelineContext();
        pipeline.Init(request, StowageMode.WeightRecommendation).IsOk.Should().BeTrue();

        using var model = new LinearMetaModel<Flt64>();
        // **必须注册载荷最大化**：`WeightRecommendationPayload` 只有它读（ 实测——只调聚合的 `Register`，目标系数全是 0）。
        // **Payload maximisation has to be registered**: it is the only consumer of `WeightRecommendationPayload` (measured by — calling the aggregate
        // `Register` alone leaves every objective coefficient at zero).
        pipeline.StowageContext.Register(StowageMode.WeightRecommendation, model).IsOk.Should().BeTrue();
        pipeline.MacContext.Register(StowageMode.WeightRecommendation, model).IsOk.Should().BeTrue();
        pipeline.AirworthinessSecurityContext.Register(StowageMode.WeightRecommendation, model).IsOk.Should().BeTrue();
        var payloadMaximization = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization.PayloadMaximizationContext();
        payloadMaximization.Init(pipeline.AircraftContext, pipeline.StowageContext, request).IsOk.Should().BeTrue();
        payloadMaximization.Register(StowageMode.WeightRecommendation, request.ResolvedParameter, model).IsOk.Should().BeTrue();

        bool found = false;
        foreach (MetaSubObject<Flt64> part in model.MetaSubObjects) {
            foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in part.Polynomial.Monomials) {
                if (monomial.Coefficient.ToDouble() == 3.0) {
                    found = true;
                }
            }
        }
        found.Should().BeTrue("payloadPriority = 3 must appear as an objective coefficient, or the request field is being dropped");
    }
}
