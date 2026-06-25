#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.Model;
/// <summary>
/// 约束测试 / Constraint tests.
/// </summary>
public class ConstraintsTest {
    [Fact]
    public void MaxKnifeCountConstraint_ShouldPass_WhenUnderLimit() {
        var constraint = new MaxKnifeCountConstraint<Flt64>(new UInt64(5UL));
        var context = new CuttingPlanConstraintContext<Flt64>(
            Slices: new[]
            {
                new CuttingPlanSliceStub<Flt64>(
                    ProductionType: "product",
                    ProductionLength: null,
                    Width: new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter),
                    Amount: new UInt64(3UL))
            },
            TotalWidth: new Quantity<Flt64>(new Flt64(300.0), SIBaseUnits.Meter),
            UpperBound: new Quantity<Flt64>(new Flt64(500.0), SIBaseUnits.Meter),
            MaterialId: "mat-1"
        );

        constraint.IsSatisfied(context).Should().BeTrue();
    }

    [Fact]
    public void MaxKnifeCountConstraint_ShouldFail_WhenOverLimit() {
        var constraint = new MaxKnifeCountConstraint<Flt64>(new UInt64(2UL));
        var context = new CuttingPlanConstraintContext<Flt64>(
            Slices: new[]
            {
                new CuttingPlanSliceStub<Flt64>(
                    ProductionType: "product",
                    ProductionLength: null,
                    Width: new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter),
                    Amount: new UInt64(3UL))
            },
            TotalWidth: new Quantity<Flt64>(new Flt64(300.0), SIBaseUnits.Meter),
            UpperBound: new Quantity<Flt64>(new Flt64(500.0), SIBaseUnits.Meter),
            MaterialId: "mat-1"
        );

        constraint.IsSatisfied(context).Should().BeFalse();
    }

    [Fact]
    public void MinKnifeCountConstraint_ShouldPass_WhenAtLeastMin() {
        var constraint = new MinKnifeCountConstraint<Flt64>(new UInt64(2UL));
        var context = new CuttingPlanConstraintContext<Flt64>(
            Slices: new[]
            {
                new CuttingPlanSliceStub<Flt64>(
                    ProductionType: "product",
                    ProductionLength: null,
                    Width: new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter),
                    Amount: new UInt64(3UL))
            },
            TotalWidth: new Quantity<Flt64>(new Flt64(300.0), SIBaseUnits.Meter),
            UpperBound: new Quantity<Flt64>(new Flt64(500.0), SIBaseUnits.Meter),
            MaterialId: "mat-1"
        );

        constraint.IsSatisfied(context).Should().BeTrue();
    }

    [Fact]
    public void MinKnifeCountConstraint_IsPruning_ShouldBeFalse() {
        var constraint = new MinKnifeCountConstraint<Flt64>(new UInt64(2UL));
        constraint.IsPruning.Should().BeFalse();
    }

    [Fact]
    public void WidthUpperBoundConstraint_ShouldPass_WhenUnderBound() {
        var constraint = new WidthUpperBoundConstraint<Flt64>();
        var context = new CuttingPlanConstraintContext<Flt64>(
            Slices: System.Array.Empty<CuttingPlanSliceStub<Flt64>>(),
            TotalWidth: new Quantity<Flt64>(new Flt64(400.0), SIBaseUnits.Meter),
            UpperBound: new Quantity<Flt64>(new Flt64(500.0), SIBaseUnits.Meter),
            MaterialId: "mat-1"
        );

        constraint.IsSatisfied(context).Should().BeTrue();
    }

    [Fact]
    public void WidthUpperBoundConstraint_ShouldFail_WhenOverBound() {
        var constraint = new WidthUpperBoundConstraint<Flt64>();
        var context = new CuttingPlanConstraintContext<Flt64>(
            Slices: System.Array.Empty<CuttingPlanSliceStub<Flt64>>(),
            TotalWidth: new Quantity<Flt64>(new Flt64(600.0), SIBaseUnits.Meter),
            UpperBound: new Quantity<Flt64>(new Flt64(500.0), SIBaseUnits.Meter),
            MaterialId: "mat-1"
        );

        constraint.IsSatisfied(context).Should().BeFalse();
    }
}
