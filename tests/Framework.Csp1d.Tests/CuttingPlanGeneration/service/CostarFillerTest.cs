#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.Service;
/// <summary>
/// 配规填充器测试 / Costar filler tests.
/// </summary>
public class CostarFillerTest {
    [Fact]
    public void EmptyCostars_ShouldReturnOriginal() {
        var filler = new CostarFiller<Flt64>(
            subtractQuantity: (a, b) => new Quantity<Flt64>(a.Value - b.Value, a.Unit),
            isLessThan: (a, b) => a.Value < b.Value,
            isZero: a => a.Value == Flt64.Zero,
            isPositive: a => a.Value > Flt64.Zero
        );

        string plan = "original-plan";
        var restWidth = new Quantity<Flt64>(new Flt64(50.0), SIBaseUnits.Meter);

        IReadOnlyList<string> result = filler.Fill(
            plan: plan,
            restWidth: restWidth,
            costars: Array.Empty<string>(),
            getCostarWidths: _ => Array.Empty<Quantity<Flt64>>(),
            buildPlan: (p, _) => p,
            buildSlice: (_, w, a) => $"{w}-{a}"
        );

        result.Should().HaveCount(1);
        result[0].Should().Be("original-plan");
    }

    [Fact]
    public void NullRestWidth_ShouldReturnOriginal() {
        var filler = new CostarFiller<Flt64>(
            subtractQuantity: (a, b) => new Quantity<Flt64>(a.Value - b.Value, a.Unit),
            isLessThan: (a, b) => a.Value < b.Value,
            isZero: a => a.Value == Flt64.Zero,
            isPositive: a => a.Value > Flt64.Zero
        );

        IReadOnlyList<string> result = filler.Fill(
            plan: "plan",
            restWidth: null,
            costars: new[] { "costar-1" },
            getCostarWidths: _ => new[] { new Quantity<Flt64>(new Flt64(30.0), SIBaseUnits.Meter) },
            buildPlan: (p, _) => p,
            buildSlice: (_, w, a) => $"{w}-{a}"
        );

        result.Should().HaveCount(1);
    }
}
