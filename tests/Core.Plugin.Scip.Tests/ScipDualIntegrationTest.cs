#nullable enable
#if SCIP_INTEGRATION

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using BendersSolver = Fuookami.Ospf.Framework.Solver.IBendersDecompositionSolver;
using ColumnsSolver = Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

/// <summary>SCIP 原生 LP 对偶与 Farkas 证书验收 / Native SCIP LP dual and Farkas certificate acceptance.</summary>
public class ScipDualIntegrationTest {
    /// <summary>min -x，x ≤ 0.5，x 松弛到 [0,1]：行对偶为 -1 且 Σ rhs·y 等于目标值。 / min -x with x ≤ 0.5 relaxed to [0,1]: the row dual is -1 and Σ rhs·y equals the objective.</summary>
    [Fact]
    public async Task SolveLP_ShouldReturnRowDualForUnboundedRelaxation() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("dual-simple-x");
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.AddConstraint(relation: Row(x, 1, Comparison.LE, 0.5), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>([new LinearMonomial<Flt64>(-Flt64.One, x)], Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);

        Result<ColumnsSolver.LpResult, ErrorCode, Error<ErrorCode>> result =
            await new ScipColumnGenerationSolver().SolveLPAsync("dual-simple", parent);
        Assert.True(result.IsOk, result.ToString());
        Assert.InRange(result.Value.Solution[0].ToDouble(), 0.5 - 1e-9, 0.5 + 1e-9);
        Assert.InRange(
            result.Value.DualSolution[parent.RelationConstraints[0]].ToDouble(),
            -1.0 - 1e-6,
            -1.0 + 1e-6);
        Assert.InRange(parent.Tokens.Find(x)!.Result!.Value.ToDouble(), 0.5 - 1e-9, 0.5 + 1e-9);
    }

    /// <summary>min x + 2y，x + y ≥ 1.5，界 x ≤ 1 生效：Σ rhs·y 不等于目标值，仍须给出正确行价格。 / min x + 2y with x + y ≥ 1.5 and the active bound x ≤ 1: Σ rhs·y differs from the objective, yet the row price must stay correct.</summary>
    [Fact]
    public async Task SolveLP_ShouldReturnRowDualWhenColumnBoundsBind() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("dual-bound-x");
        var y = new BinVar("dual-bound-y");
        Assert.True(parent.Add(new IVariableItem[] { x, y }).IsOk);
        Assert.True(parent.AddConstraint(relation: Sum(x, y, Comparison.GE, 1.5), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>([
                new LinearMonomial<Flt64>(Flt64.One, x),
                new LinearMonomial<Flt64>(new Flt64(2), y),
            ], Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);

        Result<ColumnsSolver.LpResult, ErrorCode, Error<ErrorCode>> result =
            await new ScipColumnGenerationSolver().SolveLPAsync("dual-bound", parent);
        Assert.True(result.IsOk, result.ToString());
        Assert.InRange(result.Value.Result.Obj.ToDouble(), 2.0 - 1e-6, 2.0 + 1e-6);
        Assert.Single(result.Value.DualSolution);
        Assert.InRange(
            result.Value.DualSolution[parent.RelationConstraints[0]].ToDouble(),
            2.0 - 1e-6,
            2.0 + 1e-6);
        // 界生效时 Σ rhs·y = 3 与目标值 2 不等，因此该结果来自对偶模型回退分支。
        // With the bound active, Σ rhs·y = 3 differs from the objective 2, so this result comes from the fallback.
        Assert.True(System.Math.Abs(1.5 * result.Value.DualSolution[parent.RelationConstraints[0]].ToDouble()
            - result.Value.Result.Obj.ToDouble()) > 1e-6);
    }

    /// <summary>固定 x=1 后子问题为 min 2 + 3y 受 y ≥ 0.5 与 y ≤ 1 约束，行价格应为 3 与 0。 / With x fixed to 1 the sub-problem is min 2 + 3y under y ≥ 0.5 and y ≤ 1, so the row prices are 3 and 0.</summary>
    [Fact]
    public async Task BendersSubproblem_ShouldReturnRowDuals() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("dual-benders-x");
        var y = new BinVar("dual-benders-y");
        Assert.True(parent.Add(new IVariableItem[] { x, y }).IsOk);
        Assert.True(parent.AddConstraint(relation: Sum(x, y, Comparison.GE, 1.5), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Row(y, 1, Comparison.LE, 1), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>([
                new LinearMonomial<Flt64>(new Flt64(2), x),
                new LinearMonomial<Flt64>(new Flt64(3), y),
            ], Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new ScipBendersDecompositionSolver().SolveSubAsync(
                name: "dual-benders",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One });
        Assert.True(result.IsOk, result.ToString());
        BendersSolver.LinearFeasibleResult feasible = Assert.IsType<BendersSolver.LinearFeasibleResult>(result.Value);
        Assert.Equal(2, feasible.DualSolution.Count);
        // 固定 x=1 后子问题为 y ≥ 0.5，故第一条行价格为 3，未生效的上界行价格为 0。
        // With x fixed to 1 the sub-problem is y ≥ 0.5, so the first row price is 3 and the slack upper row is 0.
        Assert.InRange(feasible.DualSolution[parent.RelationConstraints[0]].ToDouble(), 3.0 - 1e-6, 3.0 + 1e-6);
        Assert.InRange(feasible.DualSolution[parent.RelationConstraints[1]].ToDouble(), -1e-6, 1e-6);
        Assert.InRange(feasible.Obj.ToDouble(), 3.5 - 1e-6, 3.5 + 1e-6);
    }

    /// <summary>
    /// 自由列 x=0 固定后子问题只剩 y ≥ 2.5 与 y ≤ 2，Farkas 归一化行把乘子钉在 (-2, +2)，可手工核算。
    /// With the free column x fixed to 0 the sub-problem keeps only y ≥ 2.5 and y ≤ 2, and the Farkas normalization row
    /// pins the multipliers to (-2, +2), which is checkable by hand.
    /// </summary>
    [Fact]
    public async Task BendersSubproblem_ShouldReturnFarkasCertificateWhenInfeasible() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("farkas-benders-x");
        var y = new RealVar("farkas-benders-y");
        Assert.True(parent.Add(new IVariableItem[] { x, y }).IsOk);
        Assert.True(parent.AddConstraint(relation: Sum(x, y, Comparison.GE, 2.5), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Sum(x, y, Comparison.LE, 2), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>([
                new LinearMonomial<Flt64>(new Flt64(2), x),
                new LinearMonomial<Flt64>(new Flt64(3), y),
            ], Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new ScipBendersDecompositionSolver().SolveSubAsync(
                name: "farkas-benders",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero });
        Assert.True(result.IsOk, result.ToString());
        BendersSolver.LinearInfeasibleResult infeasible =
            Assert.IsType<BendersSolver.LinearInfeasibleResult>(result.Value);
        Assert.Equal(2, infeasible.FarkasDualSolution.Count);
        double first = infeasible.FarkasDualSolution[parent.RelationConstraints[0]].ToDouble();
        double second = infeasible.FarkasDualSolution[parent.RelationConstraints[1]].ToDouble();
        // >= 行乘子非正、<= 行乘子非负，且反号等模，证伪 2.5 ≤ y ≤ 2。
        // The >= row multiplier is non-positive, the <= row one non-negative, they are opposite and equal in magnitude,
        // refuting 2.5 ≤ y ≤ 2.
        Assert.InRange(first, -2.0 - 1e-6, -2.0 + 1e-6);
        Assert.InRange(second, 2.0 - 1e-6, 2.0 + 1e-6);
        // 归一化行使 Σ rhs·λ = -1，因此证书幅度被唯一确定。 / The normalization row forces Σ rhs·λ = -1, which pins the magnitude.
        Assert.InRange(2.5 * first + 2.0 * second, -1.0 - 1e-6, -1.0 + 1e-6);
    }

    /// <summary>
    /// 首行即等式的不可行子问题：固定 x=0 后 y = 3 与 y ≤ 2 矛盾，等式行的松弛列不得挤占逐行乘子的索引。
    /// An infeasible sub-problem whose first row is an equality: fixing x to 0 leaves y = 3 against y ≤ 2, and the
    /// equality row's slack columns must not push the row multiplier indexes out of alignment.
    /// </summary>
    [Fact]
    public async Task BendersSubproblem_ShouldKeepFarkasCertificateAlignedWithLeadingEqualityRow() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("farkas-eq-x");
        var y = new RealVar("farkas-eq-y");
        Assert.True(parent.Add(new IVariableItem[] { x, y }).IsOk);
        Assert.True(parent.AddConstraint(relation: Sum(x, y, Comparison.EQ, 3), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Sum(x, y, Comparison.LE, 2), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>([
                new LinearMonomial<Flt64>(new Flt64(2), x),
                new LinearMonomial<Flt64>(new Flt64(3), y),
            ], Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new ScipBendersDecompositionSolver().SolveSubAsync(
                name: "farkas-eq",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero });
        Assert.True(result.IsOk, result.ToString());
        BendersSolver.LinearInfeasibleResult infeasible =
            Assert.IsType<BendersSolver.LinearInfeasibleResult>(result.Value);
        Assert.Equal(2, infeasible.FarkasDualSolution.Count);
        double equalityRow = infeasible.FarkasDualSolution[parent.RelationConstraints[0]].ToDouble();
        double upperRow = infeasible.FarkasDualSolution[parent.RelationConstraints[1]].ToDouble();
        // 手工解唯一：等式行 -1、上界行 +1，两者组合即 -y = -3 加 y ≤ 2 得出 0 ≤ -1。
        // The hand solution is unique: -1 for the equality row and +1 for the upper row, so -y = -3 plus y ≤ 2 gives 0 ≤ -1.
        Assert.InRange(equalityRow, -1.0 - 1e-6, -1.0 + 1e-6);
        Assert.InRange(upperRow, 1.0 - 1e-6, 1.0 + 1e-6);
        Assert.InRange(3.0 * equalityRow + 2.0 * upperRow, -1.0 - 1e-6, -1.0 + 1e-6);
    }

    /// <summary>不可行 LP 不得伪造解或对偶，只能报告缺少 incumbent。 / An infeasible LP must not fabricate a solution or duals; it only reports the missing incumbent.</summary>
    [Fact]
    public async Task SolveLP_ShouldRejectMissingIncumbentWithoutFabricatedDuals() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("dual-infeasible-x");
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.AddConstraint(relation: Row(x, 1, Comparison.GE, 2), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Row(x, 1, Comparison.LE, 1), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>([new LinearMonomial<Flt64>(Flt64.One, x)], Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);

        Result<ColumnsSolver.LpResult, ErrorCode, Error<ErrorCode>> result =
            await new ScipColumnGenerationSolver().SolveLPAsync("dual-infeasible", parent);
        // 列生成的 LP 入口只声明“没有 incumbent”，问题结论保留在统一报告里。
        // The column-generation LP entry only reports "no incumbent"; the problem
        // conclusion stays in the unified report.
        Failed<ColumnsSolver.LpResult, ErrorCode, Error<ErrorCode>> failed =
            Assert.IsType<Failed<ColumnsSolver.LpResult, ErrorCode, Error<ErrorCode>>>(result);
        Assert.Equal(ErrorCode.Other, failed.Code);
        Assert.Contains("no incumbent", failed.Message);
        Assert.Contains("Infeasible", failed.Message);
    }

    private static LinearInequality<Flt64> Row(IVariableItem variable, double coefficient, Comparison sign, double rhs) =>
        new(
            Lhs: new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(new Flt64(coefficient), variable)], Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>([], new Flt64(rhs)),
            Comparison: sign
        );

    private static LinearInequality<Flt64> Sum(IVariableItem first, IVariableItem second, Comparison sign, double rhs) =>
        new(
            Lhs: new LinearPolynomial<Flt64>([
                new LinearMonomial<Flt64>(Flt64.One, first),
                new LinearMonomial<Flt64>(Flt64.One, second),
            ], Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>([], new Flt64(rhs)),
            Comparison: sign
        );
}
#endif
