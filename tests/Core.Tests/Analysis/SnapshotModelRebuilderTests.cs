#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Linq;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 快照重建器 / The snapshot rebuilder.
/// </summary>
/// <remarks>
/// **这里实现的是 `ConstraintProgrammingSnapshotBuilder.buildModel` 的普通路径**（全部成员激活）。
/// 选择性停用证据的值域重绑、区间变量、目标过滤**没有实现，而且遇到就报错**。
///
/// **This implements the ordinary path of `buildModel`** (every member active). Domain rebinding when evidence is deactivated, interval variables and objective filtering are
/// **not implemented, and each one is an error rather than a silent omission**.
/// </remarks>
public class SnapshotModelRebuilderTests {
    /// <summary>搭一个两变量、一约束、一目标的小模型 / A small model with two variables, one constraint and one objective.</summary>
    private static ConstraintProgrammingModel BuildSmallModel() {
        var model = new ConstraintProgrammingModel("fixture", identityNamespace: "fixture");
        var x = new IntVar("x");
        var y = new IntVar("y");
        var xId = new VariableId("source:x");
        var yId = new VariableId("source:y");
        IntegerDomain xDomain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;
        IntegerDomain yDomain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;

        model.RegisterVariable(id: xId, variable: x, domain: xDomain, scope: ModelElementScope.Stable, origin: "fixture/x")
            .Value.Should().NotBeNull();
        model.RegisterVariable(id: yId, variable: y, domain: yDomain, scope: ModelElementScope.Stable, origin: "fixture/y")
            .Value.Should().NotBeNull();

        var xExpression = new ConstraintProgrammingExpression.Variable(x, xDomain, xId);
        var yExpression = new ConstraintProgrammingExpression.Variable(y, yDomain, yId);
        model.RegisterExpression("x", xExpression).Value.Should().NotBeNull();
        model.RegisterExpression("y", yExpression).Value.Should().NotBeNull();

        model.AddConstraint(
                ConstraintProgrammingConstraint.CreateLessOrEqual(xExpression, new Int64(6)).Value,
                id: new ConstraintId("constraint:cap"), name: "cap")
            .Value.Should().NotBeNull();
        model.Minimize(expression: yExpression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();
        return model;
    }

    /// <summary>**重建出来的模型要与原模型结构一致** / The rebuilt model matches the original structurally.</summary>
    [Fact]
    public void TheRebuild_ShouldReproduceTheModel() {
        ConstraintProgrammingModel original = BuildSmallModel();
        ConstraintProgrammingModelSnapshot snapshot = original.Snapshot().Value!;

        Result<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> rebuilt =
            SnapshotModelRebuilder.Rebuild(snapshot);
        rebuilt.IsOk.Should().BeTrue(rebuilt.ToString());

        ConstraintProgrammingModelSnapshot again = rebuilt.Value!.Snapshot().Value!;
        again.Variables.Select(entry => entry.Id.Value).Should().Equal(
            snapshot.Variables.Select(entry => entry.Id.Value));
        again.Expressions.Select(entry => entry.Name).Should().Equal(
            snapshot.Expressions.Select(entry => entry.Name));
        again.Constraints.Select(entry => entry.Id.Value).Should().Equal(
            snapshot.Constraints.Select(entry => entry.Id.Value));
        again.Objectives.Select(entry => entry.Id.Value).Should().Equal(
            snapshot.Objectives.Select(entry => entry.Id.Value));
        again.Name.Should().Be("fixture", "the model identity is carried over");
    }

    /// <summary>**变量域要被带过来** / The domains have to come across.</summary>
    /// <remarks>
    /// 结构一致不等于语义一致：**域丢了重建出来的模型会更宽松**，解出来的值原模型根本不允许。
    ///
    /// Structural equality is not semantic equality: **a dropped domain makes the rebuilt model looser**, and it would solve to values the original forbade.
    /// </remarks>
    [Fact]
    public void TheRebuild_ShouldCarryTheDomains() {
        ConstraintProgrammingModel original = BuildSmallModel();
        ConstraintProgrammingModelSnapshot snapshot = original.Snapshot().Value!;

        ConstraintProgrammingModelSnapshot again = SnapshotModelRebuilder.Rebuild(snapshot).Value!.Snapshot().Value!;

        foreach (ConstraintProgrammingVariableSnapshot entry in snapshot.Variables) {
            ConstraintProgrammingVariableSnapshot copy = again.Variables
                .Single(candidate => candidate.Id == entry.Id);
            copy.Domain.Should().Be(entry.Domain, $"the domain of {entry.Id.Value} has to survive the rebuild");
        }
    }

    /// <summary>**拿不回变量就报错，不要像样跳过** / A variable that cannot be recovered is an error, not a skip.</summary>
    /// <remarks>
    /// `continue` 的依据是"该变量完全没被引用"。**而这里"找不到"是"我们没能从 AST 里收出来"**——
    /// 两件事，跳过就是把后者当成前者。
    ///
    /// 构造方式：注册一个变量、**不为它注册任何表达式**。它是合法的快照成员，
    /// 但重建器没有任何地方能把它找回来。
    ///
    /// It `continue`s on "referenced nowhere". **A miss here means "we could not recover it from the AST"** — different facts. Constructed by registering a
    /// variable and **no expression for it**: a legitimate snapshot member the rebuilder has nowhere to find.
    /// </remarks>
    [Fact]
    public void TheUnrecoverableVariable_ShouldBeAnError() {
        ConstraintProgrammingModel model = BuildSmallModel();
        var orphan = new IntVar("orphan");
        model.RegisterVariable(
                id: new VariableId("source:orphan"),
                variable: orphan,
                domain: IntegerDomain.CreateInterval(new Int64(0), new Int64(3)).Value,
                scope: ModelElementScope.Stable,
                origin: "fixture/orphan")
            .Value.Should().NotBeNull("the fixture registers a variable that no expression mentions");

        ConstraintProgrammingModelSnapshot snapshot = model.Snapshot().Value!;
        Result<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> rebuilt =
            SnapshotModelRebuilder.Rebuild(snapshot);

        rebuilt.IsOk.Should().BeFalse("without an expression there is no way to recover the variable");
        rebuilt.ToString().Should().Contain("source:orphan",
            "the error has to name the variable it could not recover, or the caller cannot fix it");
    }
}
