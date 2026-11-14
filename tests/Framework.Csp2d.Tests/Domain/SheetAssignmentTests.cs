// 板长分配判据 / Criteria for sheet assignment.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp2d.Domain.SheetAssignment;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Domain;

/// <summary>
/// **板长分配的算法与两条守卫** / The sheet assignment arithmetic and its two guards.
/// </summary>
/// <remarks>
/// 对应基线 `SheetAssignmentContext.kt`。`AssignmentOf` 是**纯函数**，判据不需要求解器。
///
/// Mirrors the baseline `SheetAssignmentContext.kt`. `AssignmentOf` is a **pure function**, so the criteria need no solver.
/// </remarks>
public class SheetAssignmentTests {
    /// <summary>**方案数与使用次数必须一一对应** / The plan and usage counts must line up.</summary>
    [Fact]
    public void AssignmentOf_ShouldRejectAMismatchedUsageList() {
        Result<Csp2dSheetAssignmentResult, ErrorCode, Error<ErrorCode>> result =
            Csp2dSheetAssignmentFactory.AssignmentOf(
                Fixture().Plans, new List<int> { 2 }, new Csp2dSheetAssignmentConfig());

        result.IsFailed.Should().BeTrue();
        ((Failed<Csp2dSheetAssignmentResult, ErrorCode, Error<ErrorCode>>)result).Error.Message
            .Should().Contain("不一致");
    }

    /// <summary>**只有卷材计入消耗长度，但所有使用次数都计入张数** / Only coils contribute to the consumed length, while every usage counts as a sheet.</summary>
    /// <remarks>
    /// 定尺板材的"长度"就是板高，把它算进消耗长度会让"最大消耗长度"变成一个既管卷长又管板数的混合口径。
    ///
    /// A fixed sheet's "length" is its height, and counting it in the consumed length would turn the maximum into a mixed measure governing both.
    /// </remarks>
    [Fact]
    public void AssignmentOf_ShouldCountOnlyCoilLengthButEverySheet() {
        PlanSet fixture = Fixture();

        Csp2dSheetAssignmentResult result = Csp2dSheetAssignmentFactory.AssignmentOf(
            fixture.Plans, new List<int> { 2, 3, 0 }, new Csp2dSheetAssignmentConfig()).ValueOrThrow();

        result.ConsumedLength.ToDouble().Should().BeApproximately(2.0 * 2.0, 1e-12,
            "2 uses of a 2 metre coil segment");
        result.SheetCount.Should().Be(5, "2 coils plus 3 sheets, the zero usage contributes nothing");
        result.OverLengthDetected.Should().BeFalse("no limit was configured");
    }

    /// <summary>**超长只在超过上限时出现** / Over-length appears only beyond the limit.</summary>
    [Fact]
    public void AssignmentOf_ShouldReportOverLengthAgainstTheLimit() {
        PlanSet fixture = Fixture();

        Csp2dSheetAssignmentResult within = Csp2dSheetAssignmentFactory.AssignmentOf(
            fixture.Plans, new List<int> { 2, 0, 0 },
            new Csp2dSheetAssignmentConfig(MaxConsumedLength: new Flt64(5.0))).ValueOrThrow();
        within.OverLength.ToDouble().Should().BeApproximately(0.0, 1e-12);
        within.OverLengthDetected.Should().BeFalse();

        Csp2dSheetAssignmentResult beyond = Csp2dSheetAssignmentFactory.AssignmentOf(
            fixture.Plans, new List<int> { 3, 0, 0 },
            new Csp2dSheetAssignmentConfig(MaxConsumedLength: new Flt64(5.0))).ValueOrThrow();
        beyond.OverLength.ToDouble().Should().BeApproximately(1.0, 1e-12,
            "6 metres used against a 5 metre limit");
        beyond.OverLengthDetected.Should().BeTrue();
    }

    /// <summary>**消耗段高约束只对卷材注册** / The consumed length constraint is registered for coils only.</summary>
    [Fact]
    public void ConsumedLengthConstraint_ShouldSkipWithoutALimitOrWithoutCoils() {
        PlanSet fixture = Fixture();

        using (var model = new LinearMetaModel<Flt64>()) {
            Csp2dPatternAssignment assignment = new(fixture.Plans);
            assignment.Register(model, 10UL).IsOk.Should().BeTrue();
            int before = model.Constraints.Count;

            new Csp2dConsumedLengthConstraint(fixture.Plans, assignment, new Csp2dSheetAssignmentConfig())
                .Invoke(model).IsOk.Should().BeTrue();
            model.Constraints.Count.Should().Be(before, "no limit means no constraint");
        }

        using (var model = new LinearMetaModel<Flt64>()) {
            IReadOnlyList<CuttingPlan2<Flt64>> sheetsOnly = new List<CuttingPlan2<Flt64>> {
                PlanOf(MaterialOf("MAT1", coil: false), height: 2.0),
            };
            Csp2dPatternAssignment assignment = new(sheetsOnly);
            assignment.Register(model, 10UL).IsOk.Should().BeTrue();
            int before = model.Constraints.Count;

            new Csp2dConsumedLengthConstraint(
                sheetsOnly, assignment, new Csp2dSheetAssignmentConfig(MaxConsumedLength: new Flt64(5.0)))
                .Invoke(model).IsOk.Should().BeTrue();
            model.Constraints.Count.Should().Be(before, "fixed sheets do not consume coil length");
        }

        using (var model = new LinearMetaModel<Flt64>()) {
            IReadOnlyList<CuttingPlan2<Flt64>> coilsOnly = new List<CuttingPlan2<Flt64>> {
                PlanOf(MaterialOf("MAT2", coil: true), height: 2.0),
            };
            Csp2dPatternAssignment assignment = new(coilsOnly);
            assignment.Register(model, 10UL).IsOk.Should().BeTrue();
            int before = model.Constraints.Count;

            new Csp2dConsumedLengthConstraint(
                coilsOnly, assignment, new Csp2dSheetAssignmentConfig(MaxConsumedLength: new Flt64(5.0)))
                .Invoke(model).IsOk.Should().BeTrue();
            model.Constraints.Count.Should().Be(before + 1, "a coil plan consumes coil length");
        }
    }

    /// **两个经典目标都能注册，且只认卷材的段高** / Both classic objectives register, and only coils contribute length.
    [Fact]
    public void Objectives_ShouldRegisterAndRespectTheCoilMode() {
        PlanSet fixture = Fixture();
        using var model = new LinearMetaModel<Flt64>();
        Csp2dPatternAssignment assignment = new(fixture.Plans);
        assignment.Register(model, 10UL).IsOk.Should().BeTrue();

        new Csp2dConsumedLengthObjective(fixture.Plans, assignment).Invoke(model).IsOk.Should().BeTrue();
        new Csp2dSheetCountObjective(fixture.Plans, assignment).Invoke(model).IsOk.Should().BeTrue();

        SheetAssignmentModes.IsCoilMode(MaterialOf("MAT2", coil: true)).Should().BeTrue(
            "a coil with a given length is still a coil");
        SheetAssignmentModes.IsCoilMode(MaterialOf("MAT1", coil: false)).Should().BeFalse();
    }

    /// <summary>卷材、定尺与一块不使用的方案 / A coil plan, a sheet plan and an unused one.</summary>
    private sealed record PlanSet(IReadOnlyList<CuttingPlan2<Flt64>> Plans);

    /// <summary>构造三个方案 / Build the three plans.</summary>
    /// <returns>方案集合 / The plan set.</returns>
    private static PlanSet Fixture() =>
        new(new List<CuttingPlan2<Flt64>> {
            PlanOf(MaterialOf("MAT-COIL", coil: true), height: 2.0),
            PlanOf(MaterialOf("MAT-SHEET", coil: false), height: 1.0),
            PlanOf(MaterialOf("MAT-UNUSED", coil: true), height: 9.0),
        });

    /// <summary>构造一个方案 / Build one plan.</summary>
    /// <param name="material">物料 / The material.</param>
    /// <param name="height">料域高度 / The region height.</param>
    /// <returns>方案 / The plan.</returns>
    private static CuttingPlan2<Flt64> PlanOf(Material2<Flt64> material, double height) {
        var pattern = new Pattern2<Flt64>(
            Csp2dIds.PatternId2Of("PAT-" + material.Id.Value),
            material,
            Region2Factory.Region2Of(Metres(0.0), Metres(0.0), Metres(4.0), Metres(height)),
            new List<Placement2<Flt64>> {
                new(new RectanglePackingShape2<Flt64>(Metres(1.0), Metres(1.0)),
                    new QuantityPoint2<Flt64>(Metres(0.0), Metres(0.0)), false, "P1"),
            },
            new List<Placement2<Flt64>>());
        return new(Csp2dIds.PlanId2Of("PLAN-" + material.Id.Value), material, pattern, null, null);
    }

    /// <summary>构造物料 / Build a material.</summary>
    /// <param name="materialId">物料编号 / The material identifier.</param>
    /// <param name="coil">是否为卷材 / Whether it is a coil.</param>
    /// <returns>物料 / The material.</returns>
    private static Material2<Flt64> MaterialOf(string materialId, bool coil) =>
        new(Csp2dIds.MaterialId2Of(materialId), "mat",
            Metres(4.0), coil ? null : Metres(2.0));

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
