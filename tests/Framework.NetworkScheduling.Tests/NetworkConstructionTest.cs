// 建设变量组件与容量约束测试 / Tests for the construction component and capacity constraint.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Service.Limits;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 建设变量组件与容量约束 / The construction component and capacity constraint.
/// </summary>
public sealed class NetworkConstructionTest {
    /// <summary>主路径：一个可选设施可注册 / The main path registers: one optional facility.</summary>
    [Fact]
    public void Register_ShouldSucceedOnMainPath() =>
        Construction().Register(new LinearMetaModel<Flt64>("design")).IsOk.Should().BeTrue();

    /// <summary>可选设施产生一个二值变量 / An optional facility yields one binary variable.</summary>
    [Fact]
    public void Register_ShouldCreateFacilityVariable() {
        NetworkConstruction<FltX> construction = Construction();
        construction.Register(new LinearMetaModel<Flt64>("design"));

        construction.Facilities.Should().HaveCount(1);
        construction.Facilities[new NetworkNodeId("n1")].Should().BeOfType<BinVar>();
    }

    /// <summary>
    /// **必选设施被强制为一**：必须有等式约束锁定。
    /// **A required facility is pinned to one**: an equality must lock it.
    /// </summary>
    [Fact]
    public void Register_ShouldPinRequiredFacility() {
        var model = new LinearMetaModel<Flt64>("design");
        Construction(required: true).Register(model);

        model.Constraints.Should().HaveCount(1);
    }

    /// <summary>可选设施不产生等式，只进目标 / An optional facility adds no row.</summary>
    [Fact]
    public void Register_ShouldNotPinOptionalFacility() {
        var model = new LinearMetaModel<Flt64>("design");
        Construction().Register(model);

        model.Constraints.Should().BeEmpty();
    }

    /// <summary>建设费用随设施与等级累计 / Construction cost accumulates over facilities and levels.</summary>
    [Fact]
    public void Register_ShouldAccumulateConstructionCost() {
        NetworkConstruction<FltX> construction = Construction(withService: true);
        construction.Register(new LinearMetaModel<Flt64>("design"));

        construction.ConstructionCost.Monomials.Should().HaveCount(3, "1 设施 + 2 等级 / 1 facility + 2 levels");
    }

    /// <summary>
    /// **未声明的通道默认完全可用（常数 1），不是不可用（常数 0）**。
    /// **An undeclared service defaults to fully available (one), not unavailable (zero).**
    /// </summary>
    [Fact]
    public void Availability_ShouldDefaultToOneForUndeclaredService() {
        NetworkConstruction<FltX> construction = Construction();
        construction.Register(new LinearMetaModel<Flt64>("design"));

        LinearPolynomial<Flt64> availability = construction.Availability(new NetworkArcId("a1"));

        availability.Monomials.Should().BeEmpty();
        availability.Constant.ToDouble().Should().BeApproximately(1.0, 1e-9);
    }

    /// <summary>未声明的设施启用量也默认为一 / An undeclared facility defaults to one.</summary>
    [Fact]
    public void FacilityAvailability_ShouldDefaultToOne() {
        NetworkConstruction<FltX> construction = Construction();
        construction.Register(new LinearMetaModel<Flt64>("design"));

        LinearPolynomial<Flt64> availability = construction.FacilityAvailability(new NetworkNodeId("ghost"));

        availability.Monomials.Should().BeEmpty();
        availability.Constant.ToDouble().Should().BeApproximately(1.0, 1e-9);
    }

    /// <summary>已声明通道的启用量是等级变量之和 / A declared service's activation is the sum of level variables.</summary>
    [Fact]
    public void Availability_ShouldSumLevelVariables() {
        NetworkConstruction<FltX> construction = Construction(withService: true);
        construction.Register(new LinearMetaModel<Flt64>("design"));

        construction.Availability(new NetworkArcId("a1")).Monomials.Should().HaveCount(2);
    }

    /// <summary>**可选通道只禁止并存（≤ 1），不强制选择** / An optional service only forbids coexistence (≤ 1).</summary>
    [Fact]
    public void Register_ShouldUseInequalityForOptionalService() {
        var model = new LinearMetaModel<Flt64>("design");
        Construction(withService: true).Register(model);

        model.Constraints.Should().HaveCount(1);
    }

    /// <summary>**必选通道强制选一个等级（= 1）** / A required service must pick one level (= 1).</summary>
    [Fact]
    public void Register_ShouldUseEqualityForRequiredService() {
        var model = new LinearMetaModel<Flt64>("design");
        Construction(withService: true, serviceRequired: true).Register(model);

        model.Constraints.Should().HaveCount(1);
    }

    /// <summary>预算产生一条上界约束 / A budget adds one upper-bound row.</summary>
    [Fact]
    public void Register_ShouldRegisterBudget() {
        var model = new LinearMetaModel<Flt64>("design");
        Construction(withBudget: true).Register(model);

        model.Constraints.Should().HaveCount(1);
    }

    /// <summary>
    /// **等级容量超出声明包络时被拒**：那会让"建了也用不上"的容量进模型，费用照计而容量约束永远不起作用。
    /// **A level beyond the declared envelope is rejected**: it puts unusable capacity into the model, charging for it while the
    /// capacity row never binds.
    /// </summary>
    [Fact]
    public void Register_ShouldRejectLevelBeyondEnvelope() =>
        Construction(withService: true, levelCapacity: 1000.0)
            .Register(new LinearMetaModel<Flt64>("design")).IsOk.Should().BeFalse();

    /// <summary>负设施成本被拒 / A negative facility cost is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectNegativeFacilityCost() =>
        Construction(facilityCost: -1.0)
            .Register(new LinearMetaModel<Flt64>("design")).IsOk.Should().BeFalse();

    /// <summary>**重复设施被拒** / A duplicate facility is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectDuplicateFacility() {
        NetworkDesignProblem<FltX> problem = DesignProblem(
            facilities: new[] {
                new NetworkFacility<FltX>(new NetworkNodeId("n1"), Cost(1.0)),
                new NetworkFacility<FltX>(new NetworkNodeId("n1"), Cost(2.0)),
            });

        new NetworkConstruction<FltX>(problem, FltXNetworkSchedulingSolverValueAdapter.Instance, "design")
            .Register(new LinearMetaModel<Flt64>("design")).IsOk.Should().BeFalse();
    }

    /// <summary>**空等级列表被拒** / An empty level list is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectEmptyLevels() {
        NetworkDesignProblem<FltX> problem = DesignProblem(services: new[] { new NetworkService<FltX>(new NetworkArcId("a1")) });

        new NetworkConstruction<FltX>(problem, FltXNetworkSchedulingSolverValueAdapter.Instance, "design")
            .Register(new LinearMetaModel<Flt64>("design")).IsOk.Should().BeFalse();
    }

    /// <summary>**重复等级标识被拒** / Duplicate level identities are rejected.</summary>
    [Fact]
    public void Register_ShouldRejectDuplicateLevelIds() {
        var levels = new List<CapacityLevel<FltX>> {
            new("low", Meters(10.0), Cost(1.0)),
            new("low", Meters(20.0), Cost(2.0)),
        };
        NetworkDesignProblem<FltX> problem = DesignProblem(
            services: new[] { new NetworkService<FltX>(new NetworkArcId("a1"), levels) });

        new NetworkConstruction<FltX>(problem, FltXNetworkSchedulingSolverValueAdapter.Instance, "design")
            .Register(new LinearMetaModel<Flt64>("design")).IsOk.Should().BeFalse();
    }

    /// <summary>**不存在的通道被拒** / A non-existent service is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectUnknownService() {
        NetworkDesignProblem<FltX> problem = DesignProblem(
            services: new[] {
                new NetworkService<FltX>(
                    new NetworkArcId("ghost"),
                    new[] { new CapacityLevel<FltX>("low", Meters(1.0), Cost(1.0)) }),
            });

        new NetworkConstruction<FltX>(problem, FltXNetworkSchedulingSolverValueAdapter.Instance, "design")
            .Register(new LinearMetaModel<Flt64>("design")).IsOk.Should().BeFalse();
    }

    /// <summary>**不存在的设施节点被拒** / A non-existent facility node is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectUnknownFacilityNode() {
        NetworkDesignProblem<FltX> problem = DesignProblem(
            facilities: new[] { new NetworkFacility<FltX>(new NetworkNodeId("ghost"), Cost(1.0)) });

        new NetworkConstruction<FltX>(problem, FltXNetworkSchedulingSolverValueAdapter.Instance, "design")
            .Register(new LinearMetaModel<Flt64>("design")).IsOk.Should().BeFalse();
    }

    /// <summary>同模型重复注册幂等 / Re-registering the same model is idempotent.</summary>
    [Fact]
    public void Register_ShouldBeIdempotent() {
        var model = new LinearMetaModel<Flt64>("design");
        NetworkConstruction<FltX> construction = Construction();

        construction.Register(model).IsOk.Should().BeTrue();
        construction.Register(model).IsOk.Should().BeTrue();
    }

    /// <summary>**不可跨模型复用** / No reuse across models.</summary>
    [Fact]
    public void Register_ShouldRejectSecondModel() {
        NetworkConstruction<FltX> construction = Construction();

        construction.Register(new LinearMetaModel<Flt64>("a")).IsOk.Should().BeTrue();
        construction.Register(new LinearMetaModel<Flt64>("b")).IsOk.Should().BeFalse();
    }

    /// <summary>
    /// **容量约束注册上下两条表达式行**：界是表达式才让未建设时占用归零。
    /// **The capacity constraint registers two expression rows**: expression bounds are what drive the load to zero when nothing is
    /// built.
    /// </summary>
    [Fact]
    public void CapacityConstraint_ShouldRegisterBothBounds() {
        var model = new LinearMetaModel<Flt64>("design");
        var pipeline = new NetworkCapacityConstraint(Expression(1.0), Expression(0.0), Expression(10.0), "cap");

        pipeline.Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(2);
        model.ConstraintsOfGroup(pipeline).Should().HaveCount(2);
    }

    /// <summary>行名带上下界后缀 / Row names carry the bound suffix.</summary>
    [Fact]
    public void CapacityConstraint_ShouldNameRows() {
        var model = new LinearMetaModel<Flt64>("design");
        new NetworkCapacityConstraint(Expression(1.0), Expression(0.0), Expression(10.0), "cap").Invoke(model);

        model.Constraints.Select(constraint => constraint.Name)
            .Should().BeEquivalentTo(new[] { "cap_lower", "cap_upper" });
    }

    /// <summary>构造一个单变量表达式 / Build a single-variable expression.</summary>
    private static LinearPolynomial<Flt64> Expression(double coefficient) =>
        new(new List<LinearMonomial<Flt64>> { new(new Flt64(coefficient), new URealVar("x")) }, Flt64.Zero);

    /// <summary>构造一个建设组件 / Build a construction component.</summary>
    private static NetworkConstruction<FltX> Construction(
        bool required = false,
        bool withService = false,
        bool serviceRequired = false,
        bool withBudget = false,
        double levelCapacity = 10.0,
        double facilityCost = 5.0) =>
        new(
            DesignProblem(
                new[] { new NetworkFacility<FltX>(new NetworkNodeId("n1"), Cost(facilityCost), required) },
                withService
                    ? new[] {
                        new NetworkService<FltX>(
                            new NetworkArcId("a1"),
                            new[] {
                                new CapacityLevel<FltX>("low", Meters(levelCapacity), Cost(1.0)),
                                new CapacityLevel<FltX>("high", Meters(levelCapacity), Cost(2.0)),
                            },
                            serviceRequired),
                    }
                    : Array.Empty<NetworkService<FltX>>(),
                withBudget ? Cost(50.0) : null),
            FltXNetworkSchedulingSolverValueAdapter.Instance,
            "design");

    /// <summary>构造一个建设问题 / Build a design problem.</summary>
    private static NetworkDesignProblem<FltX> DesignProblem(
        IReadOnlyList<NetworkFacility<FltX>>? facilities = null,
        IReadOnlyList<NetworkService<FltX>>? services = null,
        Quantity<FltX>? budget = null) =>
        new(FlowProblem(), services, facilities, budget);

    /// <summary>构造一个带单弧的流问题 / Build a flow problem with a single arc.</summary>
    private static FlowProblem<FltX> FlowProblem() {
        NetworkArc<FltX> networkArc = NetworkArc<FltX>.Invoke(
            new NetworkNodeId("n0"), new NetworkNodeId("n1"),
            new Quantity<FltX>(new FltX(2.0), NoneUnit.Instance), null, new NetworkArcId("a1")).Value;
        FlowArc<FltX> arc = FlowArc<FltX>.Invoke(
            networkArc, CapacityBounds<FltX>.Invoke(Meters(0.0), Meters(10.0)).Value).Value;

        NetworkNode<FltX>[] nodes = {
            NetworkNode<FltX>.Invoke(new NetworkNodeId("n0"), new Dictionary<string, Quantity<FltX>>()).Value,
            NetworkNode<FltX>.Invoke(new NetworkNodeId("n1"), new Dictionary<string, Quantity<FltX>>()).Value,
        };
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph =
            NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>>.Invoke(
                nodes, new[] { networkArc },
                new NetworkGraphAccess<NetworkNode<FltX>, NetworkArc<FltX>>(
                    NodeId: node => node.Id,
                    ArcId: candidate => candidate.Id,
                    From: candidate => candidate.From,
                    To: candidate => candidate.To)).Value;

        return new FlowProblem<FltX>(
            graph, new[] { arc }, Array.Empty<CommodityFlow<FltX>>(), new FltX(0.0),
            costUnit: NoneUnit.Instance);
    }

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>无量纲费用（与流的费用单位一致）/ A dimensionless cost matching the flow's cost unit.</summary>
    private static Quantity<FltX> Cost(double value) => new(new FltX(value), NoneUnit.Instance);
}
