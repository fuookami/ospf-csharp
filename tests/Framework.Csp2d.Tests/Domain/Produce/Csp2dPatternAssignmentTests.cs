// 主问题使用量变量判据 / Criteria for the master-problem usage variables.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Domain.Produce;

/// <summary>
/// **使用量变量的上界与聚合表达式** / The usage bounds and aggregate expressions.
/// </summary>
/// <remarks>
/// 对应的 `Csp2dPatternAssignment` / `safeCsp2dPolynomial`。这里只落变量与四个聚合表达式，
/// 三个约束与目标留给后续。
///
/// Mirrors `Csp2dPatternAssignment` / `safeCsp2dPolynomial`. lands the variables and the four aggregate expressions;
/// the three constraints and the objective come next.
/// </remarks>
public class Csp2dPatternAssignmentTests {
    /// <summary>**没有候选方案时必须失败** / A master problem without candidate plans must fail.</summary>
    [Fact]
    public void Register_ShouldRejectAnEmptyPlanSet() {
        using var model = new LinearMetaModel<Flt64>();
        Csp2dPatternAssignment assignment = new(new List<CuttingPlan2<Flt64>>());

        Result<Success, ErrorCode, Error<ErrorCode>> result = assignment.Register(model, 10UL);

        result.IsFailed.Should().BeTrue();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error
            .Should().BeOfType<Csp2dValidationError>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Contain("没有候选方案");
    }

    /// <summary>**上界取需求件数，零上界退化为 1** / The bound is the demand piece count, and a zero bound becomes 1.</summary>
    /// <remarks>
    /// 零上界会把**所有方案钉死成不可用**，那不是"没有需求"，而是"模型写错了"。
    ///
    /// A zero upper bound pins **every plan to unusable**, which is not "no demand" but "the model is broken".
    /// </remarks>
    [Fact]
    public void Register_ShouldTightenTheUsageBoundAndNeverUseZero() {
        using (var model = new LinearMetaModel<Flt64>()) {
            Csp2dPatternAssignment assignment = new(Plans());
            assignment.Register(model, 7UL).IsOk.Should().BeTrue();

            UpperBound(assignment, 0).Should().BeApproximately(7.0, 1e-9);
            UpperBound(assignment, 1).Should().BeApproximately(7.0, 1e-9);
        }

        using (var model = new LinearMetaModel<Flt64>()) {
            Csp2dPatternAssignment assignment = new(Plans());
            assignment.Register(model, 0UL).IsOk.Should().BeTrue();

            UpperBound(assignment, 0).Should().BeApproximately(1.0, 1e-9,
                "a zero bound would make every plan unusable");
        }
    }

    /// <summary>**四个聚合表达式都建立并挂到模型上** / The four aggregate expressions are built and attached to the model.</summary>
    [Fact]
    public void Register_ShouldBuildAllFourAggregateExpressions() {
        using var model = new LinearMetaModel<Flt64>();
        Csp2dPatternAssignment assignment = new(Plans());

        assignment.Register(model, 5UL).IsOk.Should().BeTrue();

        assignment.Usage.Should().NotBeNull();
        assignment.UsedArea.Should().NotBeNull("used_area is part of the model");
        assignment.WasteArea.Should().NotBeNull("waste_area is part of the model");
        assignment.ConsumedArea.Should().NotBeNull("consumed_area is part of the model");
        assignment.ConsumedHeight.Should().NotBeNull("consumed_height is part of the model");

        // 两个方案、成品面积 2 m² 与 1 m²：表达式应当有两个单项式。
        // Two plans with 2 m² and 1 m² of finished area: the expression carries two monomials.
        assignment.UsedArea!.Polynomial.Monomials.Should().HaveCount(2);
        assignment.UsedArea!.Polynomial.Constant.Should().Be(Flt64.Zero,
            "합산 starts from the zero constant");
    }

    /// <summary>**读取未注册的变量必须立刻暴露** / Reading an unregistered variable must fail loudly.</summary>
    /// <remarks>
    /// 没有上界的无符号变量不可能被求解器接受，所以不能返回一个"看起来能用"的空变量。
    ///
    /// An unsigned variable without an upper bound cannot be accepted by the solver, so it must not be handed out as a plausible-looking empty one.
    /// </remarks>
    [Fact]
    public void Usage_ShouldThrowBeforeRegistration() {
        Csp2dPatternAssignment assignment = new(Plans());

        System.Action act = () => _ = assignment.Usage;

        act.Should().Throw<System.InvalidOperationException>();
    }

    /// <summary>取某个变量的上界 / Read the upper bound of one variable.</summary>
    /// <param name="assignment">使用量集合 / The usage set.</param>
    /// <param name="index">下标 / The index.</param>
    /// <returns>上界数值 / The upper bound value.</returns>
    private static double UpperBound(Csp2dPatternAssignment assignment, int index) {
        AbstractVariableItem<UInt64, UInteger> variable = assignment.Usage[index];
        Bound<Flt64>? bound = variable.UpperBound;
        bound.Should().NotBeNull("the exactness of the model depends on an explicit bound");
        return bound!.Value.ToFlt64().ToDouble();
    }

    /// <summary>两个候选方案 / Two candidate plans.</summary>
    /// <returns>方案列表 / The plan list.</returns>
    private static IReadOnlyList<CuttingPlan2<Flt64>> Plans() {
        Material2<Flt64> material = new(Csp2dIds.MaterialId2Of("MAT1"), "steel", Metres(4.0), Metres(2.0));
        return new List<CuttingPlan2<Flt64>> {
            Plan("PLAN1", material, 2, 2.0),
            Plan("PLAN2", material, 1, 1.0),
        };
    }

    /// <summary>构造一个方案 / Build one plan.</summary>
    /// <param name="planId">方案编号 / The plan identifier.</param>
    /// <param name="material">物料 / The material.</param>
    /// <param name="pieces">放置件数 / The placement count.</param>
    /// <param name="usedArea">成品占用的**面积数值**，仅用于区分两个方案 / The finished area of the first piece, used only to tell the plans apart.</param>
    /// <returns>方案 / The plan.</returns>
    private static CuttingPlan2<Flt64> Plan(
        string planId,
        Material2<Flt64> material,
        int pieces,
        double usedArea) {
        var placements = new List<Placement2<Flt64>>();
        for (int index = 0; index < pieces; index++) {
            placements.Add(new Placement2<Flt64>(
                new RectanglePackingShape2<Flt64>(Metres(usedArea), Metres(1.0)),
                new QuantityPoint2<Flt64>(Metres(index * usedArea), Metres(0.0)),
                false,
                "P1"));
        }
        var pattern = new Pattern2<Flt64>(
            Csp2dIds.PatternId2Of(planId), material, Region(4.0, 2.0), placements,
            new List<Placement2<Flt64>>());
        return new CuttingPlan2<Flt64>(Csp2dIds.PlanId2Of(planId), material, pattern, null, null);
    }

    /// <summary>以原点构造区域 / Build a region anchored at the origin.</summary>
    /// <param name="width">宽度 / The width.</param>
    /// <param name="height">高度 / The height.</param>
    /// <returns>区域 / The region.</returns>
    private static Region2<Flt64> Region(double width, double height) =>
        Region2Factory.Region2Of(Metres(0.0), Metres(0.0), Metres(width), Metres(height));

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
