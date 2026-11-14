// 人工覆盖变量测试 / Tests for the artificial coverage variables.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 人工覆盖变量 / The artificial coverage variables.
/// </summary>
public sealed class ArtificialCoverageTest {
    /// <summary>主路径：两个客户可注册 / The main path registers: two customers.</summary>
    [Fact]
    public void Register_ShouldSucceedOnMainPath() =>
        Coverage(2).Register(Model()).IsOk.Should().BeTrue();

    /// <summary>**每个客户一个非负人工变量** / One non-negative artificial variable per customer.</summary>
    [Fact]
    public void Register_ShouldCreateOneVariablePerCustomer() {
        ArtificialCoverage<Flt64> coverage = Coverage(3);
        coverage.Register(Model());

        coverage.Variables.Should().NotBeNull();
        coverage.Variables!.Items.Should().HaveCount(3);
    }

    /// <summary>人工覆盖符号按客户建组 / The coverage symbols are grouped per customer.</summary>
    [Fact]
    public void Register_ShouldCreateCoverageSymbols() {
        ArtificialCoverage<Flt64> coverage = Coverage(2);
        coverage.Register(Model());

        coverage.Coverage.Should().NotBeNull();
        coverage.Coverage!.Count.Should().Be(2);
    }

    /// <summary>
    /// **人工成本是全部人工变量之和**：Phase I 的最小化对象就是它。
    /// **The artificial cost is the sum of every artificial variable**: that is exactly what Phase I minimises.
    /// </summary>
    [Fact]
    public void Register_ShouldSumEveryArtificialIntoCost() {
        ArtificialCoverage<Flt64> coverage = Coverage(3);
        coverage.Register(Model());

        coverage.Cost.Should().NotBeNull();
        coverage.Cost!.Polynomial.Monomials.Should().HaveCount(3);
        coverage.Cost.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>人工变量进入模型符号表 / The variables reach the model token table.</summary>
    [Fact]
    public void Register_ShouldRegisterVariablesIntoModel() {
        LinearMetaModel<Flt64> model = Model();
        Coverage(2).Register(model);

        model.Tokens.Tokens.Should().HaveCount(2);
    }

    /// <summary>**重复注册幂等** / Repeated registration is idempotent.</summary>
    [Fact]
    public void Register_ShouldBeIdempotent() {
        LinearMetaModel<Flt64> model = Model();
        ArtificialCoverage<Flt64> coverage = Coverage(2);

        coverage.Register(model).IsOk.Should().BeTrue();
        coverage.Register(model).IsOk.Should().BeTrue();
        model.Tokens.Tokens.Should().HaveCount(2, "第二次注册不再新增变量 / the second call adds no variable");
    }

    /// <summary>**未注册时切换到 Phase II 失败** / Switching to Phase II before registration fails.</summary>
    [Fact]
    public void SwitchToPhaseTwo_ShouldRejectBeforeRegistration() =>
        Coverage(2).SwitchToPhaseTwo(Model()).IsOk.Should().BeFalse();

    /// <summary>
    /// **Phase II 为每个客户注册一条 `a = 0` 等式行**：把人工作为缺失记账的成本抹掉。
    /// **Phase II registers one `a = 0` equality per customer**: it removes the cost of booking coverage as missing.
    /// </summary>
    [Fact]
    public void SwitchToPhaseTwo_ShouldFixEveryArtificialAtZero() {
        LinearMetaModel<Flt64> model = Model();
        ArtificialCoverage<Flt64> coverage = Coverage(3);
        coverage.Register(model);

        coverage.SwitchToPhaseTwo(model).IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(3);
        model.Constraints.Should().OnlyContain(constraint =>
            constraint.Name.StartsWith("artificial_phase_two_", StringComparison.Ordinal));
        coverage.IsPhaseTwo.Should().BeTrue();
    }

    /// <summary>**重复切换到 Phase II 幂等** / Switching to Phase II twice is idempotent.</summary>
    [Fact]
    public void SwitchToPhaseTwo_ShouldBeIdempotent() {
        LinearMetaModel<Flt64> model = Model();
        ArtificialCoverage<Flt64> coverage = Coverage(2);
        coverage.Register(model);

        coverage.SwitchToPhaseTwo(model).IsOk.Should().BeTrue();
        coverage.SwitchToPhaseTwo(model).IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(2, "第二次调用不再加行 / the second call adds no row");
    }

    /// <summary>未注册时收敛检查失败 / The convergence check fails before registration.</summary>
    [Fact]
    public void IsPhaseOneConverged_ShouldRejectBeforeRegistration() =>
        Coverage(2).IsPhaseOneConverged(Model(), new Flt64(1e-9)).IsOk.Should().BeFalse();

    /// <summary>
    /// **未求解时收敛检查失败，绝不把缺值当 0**：否则"人工总和 ≤ 容差"恒真，Phase I 被误判收敛。
    /// **The convergence check fails when unsolved and never treats a missing value as zero**: otherwise "sum ≤ tolerance" is always
    /// true and Phase I is wrongly judged converged.
    /// </summary>
    [Fact]
    public void IsPhaseOneConverged_ShouldRejectUnsolvedModel() {
        LinearMetaModel<Flt64> model = Model();
        ArtificialCoverage<Flt64> coverage = Coverage(2);
        coverage.Register(model);

        coverage.IsPhaseOneConverged(model, new Flt64(1e-9)).IsOk.Should().BeFalse();
    }

    /// <summary>构造人工覆盖 / Build the artificial coverage.</summary>
    private static ArtificialCoverage<Flt64> Coverage(int customers) =>
        new(Enumerable.Range(0, customers).Select(index => Customer(index)).ToList());

    /// <summary>构造模型 / Build a model.</summary>
    private static LinearMetaModel<Flt64> Model() => new("route_compilation");

    /// <summary>构造一个客户 / Build a customer.</summary>
    private static Customer<Flt64> Customer(int index) =>
        new(
            new CustomerId($"c{index}"),
            NetworkNode<Flt64>.Invoke(new NetworkNodeId($"n{index}"), new Dictionary<string, Quantity<Flt64>>()).Value,
            Quantity(1.0, SIBaseUnits.Meter),
            new ServiceTimeWindow(
                new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 1, 18, 0, 0, TimeSpan.Zero)),
            TimeSpan.FromMinutes(30));

    /// <summary>构造物理量 / Build a quantity.</summary>
    private static Quantity<Flt64> Quantity(double value, Fuookami.Ospf.Quantities.Unit.PhysicalUnit unit) =>
        new(new Flt64(value), unit);
}
