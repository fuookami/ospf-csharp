#nullable enable
#if GUROBI11_INTEGRATION

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Gurobi11;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using BendersSolver = Fuookami.Ospf.Framework.Solver.IBendersDecompositionSolver;
using ColumnSolver = Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver;

namespace Fuookami.Ospf.Core.Plugin.Gurobi11.Tests;

/// <summary>Gurobi11 门面在已安装 Gurobi 托管程序集上的真实兼容验收。</summary>
public class Gurobi11IntegrationTest {
    [Fact]
    public async Task LinearReportUsesGurobi11IdentityAndCapabilities() {
        using LinearTriadModel model = LinearModel();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new Gurobi11LinearSolver().InvokeReportAsync(model);

        result.IsOk.Should().BeTrue(result.ToString());
        SolveReport<Flt64> report = result.Value;
        report.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        report.SolutionPresence.Should().Be(SolutionPresence.Optimal);
        report.Solution!.Values.Values.Should().ContainSingle().Which.Should().Be(Flt64.One);
        report.Solution.Objective.Should().Be(-Flt64.One);
        AssertIdentity(report, quadratic: false);
    }

    [Fact]
    public async Task QuadraticReportUsesGurobi11IdentityAndCapabilities() {
        using QuadraticTetradModel model = QuadraticModel();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new Gurobi11QuadraticSolver().InvokeReportAsync(model);

        result.IsOk.Should().BeTrue(result.ToString());
        SolveReport<Flt64> report = result.Value;
        report.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        report.SolutionPresence.Should().Be(SolutionPresence.Optimal);
        report.Solution!.Values.Values.Should().ContainSingle().Which.Should().Be(Flt64.One);
        report.Solution.Objective.Should().Be(-Flt64.One);
        AssertIdentity(report, quadratic: true);
    }

    [Fact]
    public async Task ColumnGenerationLpReturnsRowDual() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("gurobi11-column-x");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.AddConstraint(
            relation: Row(x, Comparison.LE, new Flt64(0.5)),
            group: null).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(
                [new LinearMonomial<Flt64>(-Flt64.One, x)],
                Flt64.Zero),
            name: "objective",
            displayName: null).IsOk.Should().BeTrue();

        Result<ColumnSolver.LpResult, ErrorCode, Error<ErrorCode>> result =
            await new Gurobi11ColumnGenerationSolver().SolveLPAsync("gurobi11-column", parent);

        result.IsOk.Should().BeTrue(result.ToString());
        result.Value.Solution[0].ToDouble().Should().BeApproximately(0.5, 1e-9);
        result.Value.DualSolution.Should().ContainKey(parent.RelationConstraints[0]);
        result.Value.DualSolution[parent.RelationConstraints[0]].ToDouble()
            .Should().BeApproximately(-1.0, 1e-9);
    }

    [Fact]
    public async Task BendersFeasibleSubproblemReturnsRowDual() {
        (LinearMetaModel<Flt64> parent, BinVar x) = FeasibleSubModel();
        using (parent) {
            Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
                await new Gurobi11BendersDecompositionSolver().SolveSubAsync(
                    name: "gurobi11-benders-feasible",
                    metaModel: parent,
                    objectVariable: x,
                    fixedVariables: new Dictionary<IVariableItem, Flt64>());

            result.IsOk.Should().BeTrue(result.ToString());
            BendersSolver.LinearFeasibleResult feasible =
                result.Value.Should().BeOfType<BendersSolver.LinearFeasibleResult>().Subject;
            feasible.Obj.Should().Be(-Flt64.One);
            feasible.DualSolution.Should().ContainKey(parent.RelationConstraints[0]);
            feasible.DualSolution[parent.RelationConstraints[0]].ToDouble()
                .Should().BeApproximately(-1.0, 1e-9);
        }
    }

    [Fact]
    public async Task BendersInfeasibleSubproblemReturnsFarkasCertificate() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("gurobi11-farkas-x");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.AddConstraint(Row(x, Comparison.LE, Flt64.Zero), group: null).IsOk.Should().BeTrue();
        parent.AddConstraint(Row(x, Comparison.GE, Flt64.One), group: null).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(
                [new LinearMonomial<Flt64>(-Flt64.One, x)],
                Flt64.Zero),
            name: "objective",
            displayName: null).IsOk.Should().BeTrue();

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new Gurobi11BendersDecompositionSolver().SolveSubAsync(
                name: "gurobi11-benders-farkas",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64>());

        result.IsOk.Should().BeTrue(result.ToString());
        BendersSolver.LinearInfeasibleResult infeasible =
            result.Value.Should().BeOfType<BendersSolver.LinearInfeasibleResult>().Subject;
        infeasible.FarkasDualSolution.Should().NotBeEmpty();
        infeasible.FarkasDualSolution.Values.Should().OnlyContain(value =>
            double.IsFinite(value.ToDouble()) && value != Flt64.Zero);
    }

    private static (LinearMetaModel<Flt64> Parent, BinVar Variable) FeasibleSubModel() {
        var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("gurobi11-benders-x");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.AddConstraint(Row(x, Comparison.LE, Flt64.One), group: null).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(
                [new LinearMonomial<Flt64>(-Flt64.One, x)],
                Flt64.Zero),
            name: "objective",
            displayName: null).IsOk.Should().BeTrue();
        return (parent, x);
    }

    private static LinearTriadModel LinearModel() => new(
        variables: Variables(),
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([]),
            signs: [],
            rhs: [],
            names: [],
            sources: []),
        objective: new Objective<LinearObjectiveCell>(
            ObjectCategory.Minimum,
            [new LinearObjectiveCell(0, -Flt64.One)]),
        name: "gurobi11-linear-report");

    private static QuadraticTetradModel QuadraticModel() => new(
        variables: Variables(),
        constraints: new QuadraticConstraintBatch(
            lhs: new SparseQuadraticMatrix([]),
            signs: [],
            rhs: [],
            names: [],
            sources: []),
        objective: new Objective<QuadraticObjectiveCell>(
            ObjectCategory.Minimum,
            [new QuadraticObjectiveCell(
                Col1: 0,
                Col2: -1,
                Coefficient: -Flt64.One)]),
        name: "gurobi11-quadratic-report");

    private static IReadOnlyList<ModelViewVariable> Variables() => [
        new ModelViewVariable(
            index: 0,
            lowerBound: Flt64.Zero,
            upperBound: Flt64.One,
            type: Binary.Instance,
            name: "x"),
    ];

    private static LinearInequality<Flt64> Row(
        BinVar variable,
        Comparison sign,
        Flt64 rhs) => new(
            Lhs: new LinearPolynomial<Flt64>(
                [new LinearMonomial<Flt64>(Flt64.One, variable)],
                Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>([], rhs),
            Comparison: sign);

    private static void AssertIdentity(SolveReport<Flt64> report, bool quadratic) {
        report.Provenance.Should().NotBeNull();
        report.Provenance!.Descriptor.SolverId.Should().Be("gurobi11");
        report.Provenance.Descriptor.BackendName.Should().Be("Gurobi 11");
        report.Provenance.Descriptor.Capabilities.WarmStart.Should().BeTrue();
        report.Provenance.Descriptor.Capabilities.SolutionPool.Should().BeTrue();
        report.Provenance.Descriptor.Capabilities.Callback.Should().BeTrue();
        report.Provenance.Descriptor.Capabilities.Interrupt.Should().BeTrue();
        report.Provenance.Descriptor.Capabilities.ModelTypes.Should().Contain(
            quadratic ? SolverModelType.Qp : SolverModelType.Lp);
        report.Provenance.Descriptor.Capabilities.ModelTypes.Should().Contain(SolverModelType.Mip);
        report.Fingerprints.Configuration.Should().NotBeNull();
        report.Fingerprints.Solver.Should().NotBeNull();
        report.Fingerprints.Solver!.Value.Should().HaveLength(64);
#if GUROBI11_NATIVE_SDK
        report.Provenance.Descriptor.BackendVersion.Should().StartWith("11.");
#else
        report.Provenance.Descriptor.BackendVersion.Should().StartWith("10.0.1");
#endif
    }
}

#endif
