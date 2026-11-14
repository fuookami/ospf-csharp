// 设施分区约束测试 / Tests for the facility-partition constraints.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 设施分区约束 / The facility-partition constraints.
/// </summary>
public sealed class FacilityPartitionConstraintsTest {
    /// <summary>**必选分区注册一条等式** / A required partition registers one equality.</summary>
    [Fact]
    public void Assignment_ShouldRegisterEqualityWhenRequired() {
        var model = new LinearMetaModel<Flt64>("structure");

        new FacilityPartitionAssignmentConstraint(Expression(), required: true, "p1").Invoke(model)
            .IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(1);
        model.Constraints[0].Name.Should().Be("p1");
    }

    /// <summary>
    /// **可选分区用不等式，保留"不归属"的可能** / An optional partition uses an inequality, keeping "no owner" reachable.
    /// </summary>
    [Fact]
    public void Assignment_ShouldRegisterInequalityWhenOptional() {
        var model = new LinearMetaModel<Flt64>("structure");

        new FacilityPartitionAssignmentConstraint(Expression(), required: false, "p1").Invoke(model)
            .IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(1);
    }

    /// <summary>归属约束归属自身分组 / The assignment constraint belongs to its own group.</summary>
    [Fact]
    public void Assignment_ShouldGroupItsRow() {
        var model = new LinearMetaModel<Flt64>("structure");
        var constraint = new FacilityPartitionAssignmentConstraint(Expression(), required: true, "p1");
        constraint.Invoke(model);

        model.ConstraintsOfGroup(constraint).Should().HaveCount(1);
    }

    /// <summary>最小配额为零时不注册下界 / A zero minimum registers no lower row.</summary>
    [Fact]
    public void Quota_ShouldSkipZeroMinimum() {
        var model = new LinearMetaModel<Flt64>("structure");

        new FacilityPartitionQuotaConstraint(Expression(), minimum: 0, maximum: null, "q").Invoke(model)
            .IsOk.Should().BeTrue();

        model.Constraints.Should().BeEmpty();
    }

    /// <summary>正的最小配额注册下界 / A positive minimum registers a lower row.</summary>
    [Fact]
    public void Quota_ShouldRegisterMinimum() {
        var model = new LinearMetaModel<Flt64>("structure");

        new FacilityPartitionQuotaConstraint(Expression(), minimum: 2, maximum: null, "q").Invoke(model)
            .IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(1);
        model.Constraints[0].Name.Should().Be("q_minimum");
    }

    /// <summary>**最大配额缺省时不注册"无穷大上界"** / A missing maximum registers no "infinite" row.</summary>
    [Fact]
    public void Quota_ShouldOmitMissingMaximum() {
        var model = new LinearMetaModel<Flt64>("structure");

        new FacilityPartitionQuotaConstraint(Expression(), minimum: 1, maximum: null, "q").Invoke(model)
            .IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(1, "只有下界 / only the lower row");
        model.Constraints.Any(constraint => constraint.Name.Contains("maximum", System.StringComparison.Ordinal))
            .Should().BeFalse();
    }

    /// <summary>给出最大配额时注册上界 / A given maximum registers an upper row.</summary>
    [Fact]
    public void Quota_ShouldRegisterMaximum() {
        var model = new LinearMetaModel<Flt64>("structure");

        new FacilityPartitionQuotaConstraint(Expression(), minimum: 1, maximum: 3, "q").Invoke(model)
            .IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(2);
        model.Constraints.Select(constraint => constraint.Name)
            .Should().BeEquivalentTo(new[] { "q_minimum", "q_maximum" });
    }

    /// <summary>配额行归属自身分组 / Quota rows belong to their own group.</summary>
    [Fact]
    public void Quota_ShouldGroupItsRows() {
        var model = new LinearMetaModel<Flt64>("structure");
        var constraint = new FacilityPartitionQuotaConstraint(Expression(), minimum: 1, maximum: 2, "q");
        constraint.Invoke(model);

        model.ConstraintsOfGroup(constraint).Should().HaveCount(2);
    }

    /// <summary>构造一个单变量表达式 / Build a single-variable expression.</summary>
    private static LinearPolynomial<Flt64> Expression() =>
        new(new List<LinearMonomial<Flt64>> { new(Flt64.One, new URealVar("x")) }, Flt64.Zero);
}
