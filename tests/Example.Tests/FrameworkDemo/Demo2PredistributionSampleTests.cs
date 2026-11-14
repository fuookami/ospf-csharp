using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// B813：预配载示例请求，以及"两种模式真的产出不同方案" / The predistribution sample and "the two modes really produce different plans".
/// </summary>
public sealed class Demo2PredistributionSampleTests
{
    [Fact]
    public void ThePredistributionSample_ShouldDifferOnlyInTheSpecification() {
        // **隔离住"唯一差别"这个说法**：如果谁以后往预配载示例里加了别的字段，这条会红，
        // 而那条"两种模式应当产出不同方案"的判据会把原因归错。
        //
        // **Pin the "only difference" claim**: if someone later adds another field to the predistribution sample this turns red, and the criterion about "the two
        // modes should produce different plans" would otherwise blame the wrong cause.
        RequestDTO full = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO predicate = AircraftLoadingDemo.CreatePredistributionSampleRequest();

        full.Positions.Should().OnlyContain(position => position.PredicateLoadWeight == null,
            "the full-load sample must not carry a spec, or every existing criterion would measure a different model");
        predicate.Positions.Should().OnlyContain(position => position.PredicateLoadWeight != null,
            "predistribution cannot assemble without the spec");

        // **不比整条记录**：`RequestDTO` 里有数组字段，两次调用各自建一个，引用相等永远不成立——
        // 第一版就是这么红的。**比字段本身**，而且只比"应该相同"的那些。
        //
        // **Not whole-record equality**: `RequestDTO` carries array fields and each call builds a new one, so reference equality never holds — the first version went
        // red exactly there. Compare the fields, and only the ones that are supposed to match.
        predicate.PayloadUpperBound.Should().Be(full.PayloadUpperBound);
        predicate.MinPayloadRatio.Should().Be(full.MinPayloadRatio);
        predicate.LengthUnit.Should().Be(full.LengthUnit);
        System.Linq.Enumerable.Select(predicate.Cargos, cargo => cargo.Name)
            .Should().Equal(System.Linq.Enumerable.Select(full.Cargos, cargo => cargo.Name));
        System.Linq.Enumerable.Select(predicate.Positions, position => position.Name)
            .Should().Equal(System.Linq.Enumerable.Select(full.Positions, position => position.Name), "the positions themselves are the same");
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **预配载的应用路径有八个域，含冗余** / The predistribution application path has eight domains, redundancy included.
    /// </summary>
    /// <remarks>
    /// 基线的 `PredistributionPipelineContext.rest` 是"全配载七域 + 余度"，**八个**。本仓走过两次弯路：
    ///
    /// 1. B814 之前只注册**四个**，`rows49`，SCIP 直接报不可行——**漏域不报错，只是少约束**。
    /// 2. B814 补到七个，`rows52`；B815 查明差的 2 行是 `StowageContext.Init` 没收到模式。七个域 + 模式 = `rows54`。
    /// 3. B817：余度域写完了（799 行）却**从来没有消费者**，所以七个域其实一直是七个。接上之后是
    ///    `rows54,symbols82,objective10`——**行数不变，多六符号两目标**，因为冗余那两条管线加的是**目标**而不是约束。
    ///
    /// **行数因此不是这条判据的锚点**（接不接余度都是 54），锚点必须是整串形状。
    ///
    /// The baseline's register list is "full load's seven + redundancy", **eight**. The port took three wrong turns: four domains until B814 (`rows49`, SCIP called it infeasible,
    /// because **a missing domain never errors**); seven domains at B814 (`rows52`), where B815 found the missing two rows were `StowageContext.Init` not receiving the mode;
    /// and B817, where redundancy was written (799 lines) but **never had a consumer**, so "seven" was really seven plus nothing. Wired up it is
    /// `rows54,symbols82,objective10` — **the row count does not move, six symbols and two objectives join**, because redundancy's two pipelines add **objectives**, not
    /// constraints. **So the row count cannot be this criterion's anchor** (54 either way); the anchor has to be the whole shape string.
    /// </remarks>
    [Fact]
    public void TheApplicationPath_ShouldRegisterTheRedundancyDomain() {
        RequestDTO request = AircraftLoadingDemo.CreatePredistributionSampleRequest();

        (ResponseDTO Response, RenderDTO? Render) executed =
            new PredistributionApplication().Execute(request, milpSolver: new Fuookami.Ospf.Core.Plugin.Scip.ScipLinearSolver());

        executed.Response.Notes.Should().Contain(note => note.Contains("direct_model=rows54,symbols82,objective10"),
            "eight domains: seven plus redundancy, whose two pipelines add symbols and objectives rather than rows");
        executed.Response.Succeed.Should().BeTrue(
            "notes: " + (executed.Response.Notes is null ? "<no notes>" : string.Join(" | ", System.Linq.Enumerable.ToArray(executed.Response.Notes))));
        executed.Response.Status.Should().Be("Optimal");
        executed.Response.Objective.Should().Be(1.0);
    }

    [Fact]
    public async Task TheTwoModes_ShouldProduceDifferentPlans() {
        // **指纹不同不等于方案不同**：B799/B801 钉的是模型的形状，这里钉的是**解出来的结果**。
        // 实测（B817 之后）：全配载 38 个非零赋值、预配载 42 个——同一条请求，两种模式给出不同的装载方案。
        //
        // **A different fingerprint is not a different plan**: B799/B801 pinned the model's shape, this pins **the computed result**. Measured after B817: full load has 38
        // non-zero assignments and predistribution 42 — the same request, two modes, two different loading plans.
        (int NonZero, double Objective) full = await Solve(StowageMode.FullLoad);
        (int NonZero, double Objective) predicate = await Solve(StowageMode.Predistribution);

        full.NonZero.Should().BeGreaterThan(0);
        predicate.NonZero.Should().BeGreaterThan(0);
        predicate.NonZero.Should().NotBe(full.NonZero,
            "if the two modes produced the same plan, the request would only have been decoded differently");
    }

    private static async Task<(int NonZero, double Objective)> Solve(StowageMode mode) {
        RequestDTO request = AircraftLoadingDemo.CreatePredistributionSampleRequest();
        var pipeline = new DomainPipelineContext();
        pipeline.Init(request, mode).IsOk.Should().BeTrue();
        using var model = new LinearMetaModel<Flt64>();
        pipeline.Register(mode, request.ResolvedParameter, model).IsOk.Should().BeTrue();
        Result<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> built =
            await model.BuildLinearTriadModelAsync<Flt64>();
        built.IsOk.Should().BeTrue(built.ToString());

        var solver = new Fuookami.Ospf.Core.Plugin.Scip.ScipLinearSolver();
        Result<Fuookami.Ospf.Core.Solver.Output.SolverOutput, ErrorCode, Error<ErrorCode>> solved =
            await solver.InvokeAsync(built.Value.Matrix, iisConfig: new Fuookami.Ospf.Core.Solver.Iis.IisConfig());
        solved.IsOk.Should().BeTrue(solved.ToString());
        var output = (Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>)
            ((Ok<Fuookami.Ospf.Core.Solver.Output.SolverOutput, ErrorCode, Error<ErrorCode>>)solved).Value;

        int nonZero = 0;
        foreach (Flt64 value in output.Solution.Values) {
            if (value.ToDouble() != 0.0) {
                ++nonZero;
            }
        }
        return (nonZero, output.Obj.ToDouble());
    }
#endif
}
