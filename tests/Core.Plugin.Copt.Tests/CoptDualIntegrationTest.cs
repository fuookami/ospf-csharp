#nullable enable
#if COPT_INTEGRATION

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Copt.Tests;

/// <summary>
/// COPT 原生 LP 对偶与 Farkas 证书验收。
/// Native COPT LP dual and Farkas certificate acceptance.
/// </summary>
public class CoptDualIntegrationTest {
    [Fact]
    [Trait("Solver", "COPT")]
    public async Task SolveLpReturnsRowDual() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("copt-dual-simple-x");
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.AddConstraint(relation: Row(x, 1, Comparison.LE, 0.5), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                [new LinearMonomial<Flt64>(-Flt64.One, x)],
                Flt64.Zero),
            name: "objective",
            displayName: null).IsOk);

        Result<ColumnsSolver.LpResult, ErrorCode, Error<ErrorCode>> result =
            await new CoptColumnGenerationSolver().SolveLPAsync("copt-dual-simple", parent);

        Assert.True(result.IsOk, result.ToString());
        Assert.InRange(result.Value.Solution[0].ToDouble(), 0.5 - 1e-9, 0.5 + 1e-9);
        Assert.InRange(
            result.Value.DualSolution[parent.RelationConstraints[0]].ToDouble(),
            -1.0 - 1e-6,
            -1.0 + 1e-6);
    }

    [Fact]
    [Trait("Solver", "COPT")]
    public async Task BendersSubproblemReturnsRowDuals() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("copt-dual-benders-x");
        var y = new BinVar("copt-dual-benders-y");
        Assert.True(parent.Add(new IVariableItem[] { x, y }).IsOk);
        Assert.True(parent.AddConstraint(relation: Sum(x, y, Comparison.GE, 1.5), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Row(y, 1, Comparison.LE, 1), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                [
                    new LinearMonomial<Flt64>(new Flt64(2), x),
                    new LinearMonomial<Flt64>(new Flt64(3), y),
                ],
                Flt64.Zero),
            name: "objective",
            displayName: null).IsOk);

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new CoptBendersDecompositionSolver().SolveSubAsync(
                name: "copt-dual-benders",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One });

        Assert.True(result.IsOk, result.ToString());
        BendersSolver.LinearFeasibleResult feasible =
            Assert.IsType<BendersSolver.LinearFeasibleResult>(result.Value);
        Assert.Equal(2, feasible.DualSolution.Count);
        Assert.InRange(
            feasible.DualSolution[parent.RelationConstraints[0]].ToDouble(),
            3.0 - 1e-6,
            3.0 + 1e-6);
        Assert.InRange(
            feasible.DualSolution[parent.RelationConstraints[1]].ToDouble(),
            -1e-6,
            1e-6);
        Assert.InRange(feasible.Obj.ToDouble(), 3.5 - 1e-6, 3.5 + 1e-6);
    }

    [Fact]
    [Trait("Solver", "COPT")]
    public async Task BendersSubproblemReturnsFarkasCertificate() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("copt-farkas-benders-x");
        var y = new RealVar("copt-farkas-benders-y");
        Assert.True(parent.Add(new IVariableItem[] { x, y }).IsOk);
        Assert.True(parent.AddConstraint(relation: Sum(x, y, Comparison.GE, 2.5), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Sum(x, y, Comparison.LE, 2), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                [
                    new LinearMonomial<Flt64>(new Flt64(2), x),
                    new LinearMonomial<Flt64>(new Flt64(3), y),
                ],
                Flt64.Zero),
            name: "objective",
            displayName: null).IsOk);

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new CoptBendersDecompositionSolver().SolveSubAsync(
                name: "copt-farkas-benders",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero });

        Assert.True(result.IsOk, result.ToString());
        BendersSolver.LinearInfeasibleResult infeasible =
            Assert.IsType<BendersSolver.LinearInfeasibleResult>(result.Value);
        Assert.Equal(2, infeasible.FarkasDualSolution.Count);
        double first = infeasible.FarkasDualSolution[parent.RelationConstraints[0]].ToDouble();
        double second = infeasible.FarkasDualSolution[parent.RelationConstraints[1]].ToDouble();
        // COPT 原生 Farkas 乘子采用 >= 规范：>= 行取正、<= 行取负；两者合成 0 >= 0.5 即证伪。
        // COPT's raw Farkas multipliers use >= form: positive for >= rows and negative for <= rows;
        // their combination gives 0 >= 0.5, proving infeasibility.
        Assert.True(first > 0);
        Assert.True(second < 0);
        Assert.True(2.5 * first + 2.0 * second > 0);
    }

    [Fact]
    [Trait("Solver", "COPT")]
    public async Task SolveLpRejectsInfeasiblePricingProblemWithoutFabricatedDuals() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("copt-dual-infeasible-x");
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.AddConstraint(relation: Row(x, 1, Comparison.GE, 2), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Row(x, 1, Comparison.LE, 1), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                [new LinearMonomial<Flt64>(Flt64.One, x)],
                Flt64.Zero),
            name: "objective",
            displayName: null).IsOk);

        Result<ColumnsSolver.LpResult, ErrorCode, Error<ErrorCode>> result =
            await new CoptColumnGenerationSolver().SolveLPAsync("copt-dual-infeasible", parent);

        Failed<ColumnsSolver.LpResult, ErrorCode, Error<ErrorCode>> failed =
            Assert.IsType<Failed<ColumnsSolver.LpResult, ErrorCode, Error<ErrorCode>>>(result);
        Assert.Equal(ErrorCode.Other, failed.Code);
        Assert.Contains("Infeasible", failed.Message);
    }

    private static LinearInequality<Flt64> Row(
        IVariableItem variable,
        double coefficient,
        Comparison sign,
        double rhs) =>
        new(
            Lhs: new LinearPolynomial<Flt64>(
                [new LinearMonomial<Flt64>(new Flt64(coefficient), variable)],
                Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>([], new Flt64(rhs)),
            Comparison: sign);

    private static LinearInequality<Flt64> Sum(
        IVariableItem first,
        IVariableItem second,
        Comparison sign,
        double rhs) =>
        new(
            Lhs: new LinearPolynomial<Flt64>(
                [
                    new LinearMonomial<Flt64>(Flt64.One, first),
                    new LinearMonomial<Flt64>(Flt64.One, second),
                ],
                Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>([], new Flt64(rhs)),
            Comparison: sign);
}
#endif
