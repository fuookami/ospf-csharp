#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// "特性不支持"的目标报告 / The unsupported-feature target report.
/// </summary>
/// <remarks>
/// **`TargetFixed` 必须是 `false`。** 这个字段的含义是"目标值已经被固定下来了"，
/// **而这里恰恰相反：根本没算。**
///
/// **`TargetFixed` has to be `false`.** The field means "the objective value has been pinned down", **and here the opposite is true: nothing was computed.**
/// </remarks>
public class UnsupportedTargetFeaturesTests {
    private static ObjectiveTarget Target() =>
        ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(1.0));

    /// <summary>**状态是 `Unsupported`，且目标值未被固定** / The status is Unsupported and the objective is not fixed.</summary>
    /// <remarks>
    /// **把 `TargetFixed` 留成默认的 `true` 会让下游以为有一个已经确定的结论**，
    /// 而实际上一个数都没有。
    ///
    /// **Leaving `TargetFixed` at the default `true` lets downstream believe there is a settled conclusion**, when there is not a single number.
    /// </remarks>
    [Fact]
    public void TheUnsupportedFeatures_ShouldNotClaimAFixedTarget() {
        TargetFeasibilityReport report = UnsupportedTargetFeatures.Report(
            Target(),
            new HashSet<ConstraintProgrammingFeature> { ConstraintProgrammingFeature.BooleanLogic });

        report.Status.Should().Be(AnalysisStatus.Unsupported);
        report.TargetFixed.Should().BeFalse("nothing was computed, so nothing was fixed");
    }

    /// <summary>**特性名字必须进消息** / The feature names have to be in the message.</summary>
    /// <remarks>
    /// "当前求解器不支持"这句话对调用方没有用——**他要知道的是"哪一条不支持"**，
    /// 否则唯一的动作就是把整个后端换掉。
    ///
    /// "The current solver does not support this" is useless — **they need to know which one**, or their only move is to swap the entire backend.
    /// </remarks>
    [Fact]
    public void TheMessage_ShouldNameEveryUnsupportedFeature() {
        var features = new HashSet<ConstraintProgrammingFeature> {
            ConstraintProgrammingFeature.BooleanLogic,
            ConstraintProgrammingFeature.AllDifferent,
        };

        string? message = UnsupportedTargetFeatures.Report(Target(), features).Message;
        message.Should().NotBeNull("the report always carries an explanation");

        message.Should().Contain(nameof(ConstraintProgrammingFeature.BooleanLogic), "one of the two was named");
        message.Should().Contain(nameof(ConstraintProgrammingFeature.AllDifferent), "and the other one too");
    }

    /// <summary>**排序稳定** / The order is stable.</summary>
    /// <remarks>
    /// **两份等价的消息才会被日志和 diff 当成同一条。** 集合的迭代顺序在 .NET 里不保证，
    /// **不排序的话同一次失败会在两次运行里产生两条不同的消息。**
    ///
    /// **Two equivalent messages are what logs and diffs treat as the same event.** Set iteration order is not guaranteed in .NET, **so without sorting one failure produces two different messages across two
    /// runs.**
    /// </remarks>
    [Fact]
    public void TheMessage_ShouldBeIndependentOfSetOrder() {
        var first = new HashSet<ConstraintProgrammingFeature> {
            ConstraintProgrammingFeature.BooleanLogic,
            ConstraintProgrammingFeature.AllDifferent,
            ConstraintProgrammingFeature.Cumulative,
        };
        var second = new HashSet<ConstraintProgrammingFeature> {
            ConstraintProgrammingFeature.Cumulative,
            ConstraintProgrammingFeature.BooleanLogic,
            ConstraintProgrammingFeature.AllDifferent,
        };

        UnsupportedTargetFeatures.Report(Target(), first).Message
            .Should().Be(UnsupportedTargetFeatures.Report(Target(), second).Message,
                "the same set of features has to produce the same message");
    }

    /// <summary>**没有不同时放行** / No unsupported feature means proceed.</summary>
    /// <remarks>
    /// **反向对照**：只测"有特性时给报告"会让"恒给报告"通过，
    /// **而恒给报告等于这个后端什么都算不了。**
    ///
    /// **The reverse control**: testing only "features present gives a report" lets "always report" pass, **and always reporting means this backend can never compute anything.**
    /// </remarks>
    [Fact]
    public void TheGate_ShouldPassWhenNothingIsUnsupported() {
        TargetFeasibilityReport? gate = UnsupportedTargetFeatures.Gate(
            Matrix(),
            BuildSnapshot(),
            Target(),
            new HashSet<DiagnosticSource>());

        gate.Should().BeNull("an empty declaration against a plain model forbids nothing");
    }

    private static CapabilityMatrix Matrix() =>
        new(ModelTypes: new HashSet<SolverModelType> { SolverModelType.Cp });

    private static ConstraintProgrammingModelSnapshot BuildSnapshot() {
        using var model = new ConstraintProgrammingModel(
            name: "features", objectCategory: Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
        var variable = new Fuookami.Ospf.Core.Variable.IntVar("x");
        var id = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(Int64.Zero, new Int64(3)).Value;
        var expression = new ConstraintProgrammingExpression.Variable(variable, domain, id);

        model.RegisterVariable(id: id, variable: variable, domain: domain,
            scope: ModelElementScope.Stable, origin: "fixture/x").Value.Should().NotBeNull();
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        ConstraintProgrammingConstraint.IntegerComparison constraint =
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, Int64.Zero).Value;
        model.AddConstraint(constraint, "constraint:x").Value.Should().NotBeNull();
        model.Minimize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();

        return model.Snapshot().Value;
    }
}
