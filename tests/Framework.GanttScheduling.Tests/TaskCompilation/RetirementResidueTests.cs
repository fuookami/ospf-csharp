// 回收后残留引用的判据 / Criteria for residual references after retirement.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **回收只摘变量，不动约束与目标项** / Retirement drops the variable only, not constraints or objective terms.
/// </summary>
/// <remarks>
/// 基线的 `remove(item)` 只有一行 `tokens.remove(item)`。所以回收之后：
/// 以它为系数的约束仍在、以它为系数的目标项仍在——**都不报错**，因为变量已被固定为 0。
///
/// 这就是"行为对、模型脏"：解不变，但任何按符号表遍历模型的下游（导出、审计、二次求解）
/// 都会看到悬空引用。本批提供的是**观测**，不是修复——清理会让模型偏离固定基线。
///
/// The baseline's `remove(item)` is one line: `tokens.remove(item)`. So after retirement the constraints using it as a coefficient remain, and so do the objective terms —
/// **neither raises anything**, because the variable is already pinned to 0.
///
/// That is "right behaviour, dirty model": the solution is unchanged, but any downstream walking the model by symbol table (export, audit, re-solve) sees a dangling reference.
/// This batch offers **observation, not repair** — cleaning up would move the model away from the pinned baseline.
/// </remarks>
public class RetirementResidueTests {
    private static (LinearMetaModel<Flt64> Model, CombinationVariableItem<UInt8, Binary> Kept, CombinationVariableItem<UInt8, Binary> Retired) MakeModelWithOneConstraintEach() {
        var model = new LinearMetaModel<Flt64>();
        var kept = new BinVariable1("x", 1);
        var retired = new BinVariable1("x", 1);
        kept[0].Name = "kept";
        retired[0].Name = "retired";
        model.Add(kept.Items).IsOk.Should().BeTrue();
        model.Add(retired.Items).IsOk.Should().BeTrue();

        model.AddConstraint(
            new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, kept[0]) }, Flt64.Zero)
                .Ge(new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, retired[0]) }, Flt64.Zero)),
            group: null,
            name: "kept_ge_retired").IsOk.Should().BeTrue();

        model.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] {
                    new LinearMonomial<Flt64>(Flt64.One, kept[0]),
                    new LinearMonomial<Flt64>(Flt64.One, retired[0]),
                },
                Flt64.Zero),
            name: "cost",
            displayName: null).IsOk.Should().BeTrue();

        return (model, kept[0], retired[0]);
    }

    /// **约束里的引用仍在** / The constraint reference is still there.
    [Fact]
    public void ConstraintsReferencing_ShouldFindTheResidualReference() {
        (LinearMetaModel<Flt64> model, CombinationVariableItem<UInt8, Binary> kept, CombinationVariableItem<UInt8, Binary> retired) = MakeModelWithOneConstraintEach();
        model.Remove(retired);

        RetirementResidue.ConstraintsReferencing(model, retired).Should().Equal(
            new[] { "kept_ge_retired" },
            "the constraint still names the retired variable");
        RetirementResidue.ConstraintsReferencing(model, kept).Should().Equal(
            new[] { "kept_ge_retired" },
            "the kept variable is in the same constraint");
    }

    /// **不在模型里的变量就没有残留** / A variable the model never saw has no residue.
    [Fact]
    public void ConstraintsReferencing_ShouldReturnEmptyForAnUnrelatedVariable() {
        (LinearMetaModel<Flt64> model, _, _) = MakeModelWithOneConstraintEach();
        var unrelated = new BinVariable1("other", 1);

        RetirementResidue.ConstraintsReferencing(model, unrelated[0]).Should().BeEmpty();
    }

    /// **目标项里的引用仍在** / The objective reference is still there.
    [Fact]
    public void ObjectiveReferences_ShouldFindTheResidualReference() {
        (LinearMetaModel<Flt64> model, _, CombinationVariableItem<UInt8, Binary> retired) = MakeModelWithOneConstraintEach();
        model.Remove(retired);

        RetirementResidue.ObjectiveReferences(model, retired).Should().BeTrue(
            "the objective term still names the retired variable");
    }

    /// **不在目标里的变量不算残留** / A variable absent from the objective is not residue.
    [Fact]
    public void ObjectiveReferences_ShouldReturnFalseForAnUnrelatedVariable() {
        (LinearMetaModel<Flt64> model, _, _) = MakeModelWithOneConstraintEach();
        var unrelated = new BinVariable1("other", 1);

        RetirementResidue.ObjectiveReferences(model, unrelated[0]).Should().BeFalse();
    }

    /// <summary>
    /// **`RemoveObjectiveTerms` 能清掉残留，但基线不调它** / `RemoveObjectiveTerms` clears the residue, but the baseline does not call it.
    /// </summary>
    /// <remarks>
    /// 这条判据的作用是钉住"工具存在、而基线选择不用"这个事实：
    /// 若哪天有人在 `RemoveColumns` 里顺手加上清理，这里会变绿，而**回收后的模型会开始偏离固定基线**——
    /// 上游的 `RetirementResidueTests` 仍然会指出偏离发生了什么。
    ///
    /// The point of this criterion is to pin the fact that "the tool exists and the baseline chooses not to use it": if someone later adds the cleanup to `RemoveColumns`, this goes
    /// green while **the post-retirement model starts diverging from the pinned baseline** — and the criteria above still say what the divergence is.
    /// </remarks>
    [Fact]
    public void RemoveObjectiveTerms_ShouldClearTheResidue() {
        (LinearMetaModel<Flt64> model, _, CombinationVariableItem<UInt8, Binary> retired) = MakeModelWithOneConstraintEach();
        model.Remove(retired);

        model.RemoveObjectiveTerms(new HashSet<IVariableItem> { retired }).IsOk.Should().BeTrue();

        RetirementResidue.ObjectiveReferences(model, retired).Should().BeFalse(
            "the tool can clear it; the baseline just does not call it");
        RetirementResidue.ConstraintsReferencing(model, retired).Should().ContainSingle(
            "`RemoveObjectiveTerms` does not touch constraints");
    }
}
