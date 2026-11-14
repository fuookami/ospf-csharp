#nullable enable
#if HEXALY_INTEGRATION

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Plugin.Hexaly.Tests;

/// <summary>
/// 显式启用的 Hexaly 真机集成测试 / Opt-in Hexaly native integration tests.
/// </summary>
public class HexalyIntegrationTest {
    [Fact]
    [Trait("Solver", "Hexaly")]
    public async Task SolvesLinearMipToOptimality() {
        using LinearTriadModel model = new(
            variables: [
                new ModelViewVariable(
                    index: 0,
                    lowerBound: Flt64.Zero,
                    upperBound: Flt64.One,
                    type: Binary.Instance,
                    name: "x"),
                new ModelViewVariable(
                    index: 1,
                    lowerBound: Flt64.Zero,
                    upperBound: Flt64.One,
                    type: Continuous.Instance,
                    name: "y"),
            ],
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix([
                    new SparseVector([
                        new SparseVectorEntry(0, Flt64.One),
                        new SparseVectorEntry(1, Flt64.One),
                    ]),
                ]),
                signs: [ConstraintRelation.GreaterEqual],
                rhs: [Flt64.One],
                names: ["demand"],
                sources: [ConstraintSource.Origin]),
            objective: new Objective<LinearObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: [
                    new LinearObjectiveCell(0, Flt64.One),
                    new LinearObjectiveCell(1, Flt64.One),
                ]),
            name: "hexaly-linear-mip");

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new HexalyLinearSolver().InvokeReportAsync(model);

        result.IsOk.Should().BeTrue(result.ToString());
        result.Value.SolutionPresence.Should().Be(SolutionPresence.Optimal);
        result.Value.Proof.Status.Should().Be(ProofStatus.Claimed);
        result.Value.Solution!.Objective!.Value.ToDouble().Should().BeApproximately(1, 1e-6);
    }

    [Fact]
    [Trait("Solver", "Hexaly")]
    public async Task SolvesConvexQpToOptimality() {
        using QuadraticTetradModel model = new(
            variables: [
                new ModelViewVariable(
                    index: 0,
                    lowerBound: Flt64.Zero,
                    upperBound: new Flt64(10),
                    type: Continuous.Instance,
                    name: "x"),
                new ModelViewVariable(
                    index: 1,
                    lowerBound: Flt64.Zero,
                    upperBound: new Flt64(10),
                    type: Continuous.Instance,
                    name: "y"),
            ],
            constraints: new QuadraticConstraintBatch(
                lhs: new SparseQuadraticMatrix([
                    new SparseQuadraticVector([
                        new SparseQuadraticEntry(0, -1, Flt64.One),
                        new SparseQuadraticEntry(1, -1, Flt64.One),
                    ]),
                ]),
                signs: [ConstraintRelation.GreaterEqual],
                rhs: [Flt64.One],
                names: ["demand"],
                sources: [ConstraintSource.Origin]),
            objective: new Objective<QuadraticObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: [
                    new QuadraticObjectiveCell(0, 0, Flt64.One),
                    new QuadraticObjectiveCell(1, 1, Flt64.One),
                ]),
            name: "hexaly-convex-qp");

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new HexalyQuadraticSolver().InvokeReportAsync(model);

        result.IsOk.Should().BeTrue(result.ToString());
        result.Value.SolutionPresence.Should().Be(SolutionPresence.Optimal);
        result.Value.Solution!.Objective!.Value.ToDouble().Should().BeApproximately(0.5, 1e-5);
        result.Value.Solution.Values[0].ToDouble().Should().BeApproximately(0.5, 1e-5);
        result.Value.Solution.Values[1].ToDouble().Should().BeApproximately(0.5, 1e-5);
    }

    [Fact]
    [Trait("Solver", "Hexaly")]
    public async Task ColumnGenerationLpReturnsRowDual() {
        using var parent = new LinearMetaModel<Flt64>(
            name: "hexaly-column",
            objectCategory: ObjectCategory.Minimum);
        var x = new RealVar("hexaly-column-x");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.AddConstraint(
            relation: new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(
                    Monomials: [new LinearMonomial<Flt64>(Flt64.One, x)],
                    Constant: Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>(
                    Monomials: [],
                    Constant: new Flt64(0.5)),
                Comparison: Comparison.LE),
            group: null,
            name: "upper").IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(
                Monomials: [new LinearMonomial<Flt64>(-Flt64.One, x)],
                Constant: Flt64.Zero),
            name: "objective",
            displayName: null).IsOk.Should().BeTrue();

        Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> result =
            await new HexalyColumnGenerationSolver().SolveLPAsync(
                name: "hexaly-column",
                metaModel: parent);

        result.IsOk.Should().BeTrue(result.ToString());
        result.Value.Solution[0].ToDouble().Should().BeApproximately(0.5, 1e-9);
        result.Value.DualSolution.Should().ContainKey(parent.RelationConstraints[0]);
        result.Value.DualSolution[parent.RelationConstraints[0]].ToDouble()
            .Should().BeApproximately(-1, 1e-9);
    }
}
#endif
