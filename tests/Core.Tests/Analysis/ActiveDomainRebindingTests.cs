#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 取消激活必须改变派生模型 / Deactivation has to change the derived model.
/// </summary>
/// <remarks>
/// **这是 `activeDomains` + `rebindExpression` 的移植，而 C# 侧此前两者都没有。**
///
/// **`EffectiveDomain.Compute` 此后就被判据覆盖，却一个调用方都没有**——
/// **类型齐了、判据齐了，而删除复验的输入从来没被它改过。**
///
/// **This is the implementation of `activeDomains` and `rebindExpression`, neither of which existed here.** **`EffectiveDomain.Compute` had been covered by criteria since then with no caller** — **the type was ready, the criteria
/// were ready, and the deletion checks' input had never gone through it.**
/// </remarks>
public class ActiveDomainRebindingTests {
    private static readonly VariableId X = new("source:x");

    private static ConstraintProgrammingModelSnapshot Build(int lower, int upper) {
        using var model = new ConstraintProgrammingModel(name: "domains", objectCategory: ObjectCategory.Minimum);
        var variable = new IntVar("x");
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(lower), new Int64(upper)).Value;
        var expression = new ConstraintProgrammingExpression.Variable(variable, domain, X);

        model.RegisterVariable(id: X, variable: variable, domain: domain,
            scope: ModelElementScope.Stable, origin: "fixture/x").Value.Should().NotBeNull();
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        ConstraintProgrammingConstraint.IntegerComparison constraint =
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, Int64.Zero).Value;
        model.AddConstraint(constraint, "constraint:x").Value.Should().NotBeNull();
        model.Minimize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();

        return model.Snapshot().Value;
    }

    /// <summary>**不取消任何来源时，有效定义域等于原始定义域** / With nothing deactivated the domains are unchanged.</summary>
    [Fact]
    public void TheInactiveSet_ShouldLeaveEveryDomainAlone() {
        IReadOnlyDictionary<VariableId, IntegerDomain> domains =
            EffectiveDomain.ActiveDomains(Build(0, 10), activeSources: null);

        domains.Should().ContainKey(X);
        domains[X].Should().Be(Build(0, 10).Variables[0].Domain,
            "releasing no bound is the identity");
    }

    /// <summary>**重绑定把有效定义域写进 AST** / Rebinding puts the effective domain into the AST.</summary>
    /// <remarks>
    /// **值域必须写进表达式节点，不能只写在注册表里。** 求解器读的是节点上的值域；
    /// **只改注册表会让取消激活在求解时完全不起作用。**
    ///
    /// **The domain has to land on the expression node, not only in the registry.** The solver reads the node's domain; **changing only the registry makes deactivation do nothing at solve time.**
    /// </remarks>
    [Fact]
    public void TheRebind_ShouldWriteTheDomainIntoTheNode() {
        ConstraintProgrammingModelSnapshot snapshot = Build(0, 10);
        IntegerDomain narrowed = IntegerDomain.CreateInterval(Int64.Zero, new Int64(4)).Value;
        var domains = new Dictionary<VariableId, IntegerDomain> { [X] = narrowed };

        ConstraintProgrammingExpression rebound = EffectiveDomain.RebindDomain(
            snapshot.Variables[0].Domain is not null
                ? new ConstraintProgrammingExpression.Variable(
                    new IntVar("x"), snapshot.Variables[0].Domain, X)
                : throw new Xunit.Sdk.XunitException("fixture has no variable"),
            domains);

        rebound.Should().BeOfType<ConstraintProgrammingExpression.Variable>();
        ((ConstraintProgrammingExpression.Variable)rebound).Domain.Should().Be(narrowed,
            "the narrowed domain is the whole point of rebinding");
    }

    /// <summary>**不在映射里的节点原样返回** / A node missing from the map comes back unchanged.</summary>
    /// <remarks>
    /// **反向对照**：只测上一条会让"任何输入都返回窄值域"通过，
    /// **而那会让每个变量都拿到别的变量的定义域。**
    ///
    /// **The reverse control**: testing only the case above lets "return the narrow domain for anything" pass, **and that hands every variable another variable's domain.**
    /// </remarks>
    [Fact]
    public void TheUnmappedNode_ShouldComeBackUnchanged() {
        var constant = new ConstraintProgrammingExpression.Constant(new Int64(7));
        ConstraintProgrammingExpression rebound = EffectiveDomain.RebindDomain(
            constant, new Dictionary<VariableId, IntegerDomain>());

        rebound.Should().BeSameAs(constant, "a constant has no domain to rebind");
    }

    /// <summary>**`Rebuild` 必须接受活跃来源并把它用上** / Rebuild has to accept and use the active set.</summary>
    /// <remarks>
    /// **不给这个参数的 `Rebuild` 会让删除复验永远得到同一个答案**，
    /// **于是每一个边界来源都显得"必要"，最小性永远成立——一个假的 MUS。**
    ///
    /// **A `Rebuild` without this parameter makes every deletion check return the same answer**, **so every bound source looks necessary and minimality always holds — a fake MUS.**
    /// </remarks>
    [Fact]
    public void TheRebuild_ShouldProduceADifferentModelForADifferentActiveSet() {
        ConstraintProgrammingModelSnapshot snapshot = Build(0, 10);

        using ConstraintProgrammingModel wide = SnapshotModelRebuilder.Rebuild(snapshot).Value!;
        using ConstraintProgrammingModel narrow = SnapshotModelRebuilder.Rebuild(
            snapshot, objectiveFilter: null, activeSources: new HashSet<DiagnosticSource>()).Value!;

        wide.Should().NotBeNull("the wide rebuild succeeds");
        narrow.Should().NotBeNull("the narrow rebuild succeeds");

        // **比的是重建后快照里的表达式值域，不是"两个模型都存在"。**
        // **The comparison is the expression domain in the rebuilt snapshot, not "both models exist".**
        var wideExpression = (ConstraintProgrammingExpression.Variable)
            wide.Snapshot().Value!.Expressions[0].Expression;
        var narrowExpression = (ConstraintProgrammingExpression.Variable)
            narrow.Snapshot().Value!.Expressions[0].Expression;

        // **`null` 与空集合不是一回事，而这个差别正是删除复验的发动机：**
        // `null` 是"不释放任何边界"；空集合是"没有任何边界来源还活着"，**于是两个边界都放开。**
        // **`null` and the empty set are not the same thing, and that difference is the engine of deletion checks:**
        // `null` releases nothing; the empty set means no bound source is still active, **so both bounds are released.**
        wideExpression.Domain.Should().Be(snapshot.Variables[0].Domain,
            "a null active set is the identity");
        narrowExpression.Domain.Should().NotBe(wideExpression.Domain,
            "an empty active set has to release the bounds, or deactivating a source changes nothing");
        narrowExpression.Domain.Should().BeOfType<IntegerDomain.Interval>();
    }
}
