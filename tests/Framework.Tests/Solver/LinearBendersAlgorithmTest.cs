#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Tests.Solver;

public class LinearBendersAlgorithmTest {
    [Fact]
    public async Task Algorithm_ShouldMergeTokenOrdersAndReturnProvenResult() {
        var x = new BinVar("decision");
        var y = new URealVar("recourse");
        y.Range.Leq(new Flt64(2)).Should().BeTrue();
        using var source = new LinearMetaModel<Flt64>(name: "full");
        source.Add(y).IsOk.Should().BeTrue();
        source.Add(x).IsOk.Should().BeTrue();
        source.AddConstraint(
            relation: Recourse(x, y, new Flt64(3)),
            group: null,
            name: "recourse"
        ).IsOk.Should().BeTrue();
        source.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: Linear(x),
            name: "objective",
            displayName: null
        ).IsOk.Should().BeTrue();

        using LinearBendersModels models = await BuildAsync(source);
        models.Bindings.Values.Should().Equal(x);
        var backend = new FeasibilityBackend(x, y);

        Result<LinearBendersResult, ErrorCode, Error<ErrorCode>> result =
            await new LinearBendersAlgorithm(backend).SolveAsync(models);

        result.IsOk.Should().BeTrue(result.ToString());
        result.Value.Objective.Should().Be(Flt64.One);
        result.Value.Values[y].Should().Be(new Flt64(2));
        result.Value.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        result.Value.TerminationReason.Should().Be(TerminationReason.Completed);
        result.Value.SolutionPresence.Should().Be(SolutionPresence.Optimal);
        result.Value.ProofStatus.Should().Be(ProofStatus.Claimed);
        result.Value.Iterations.Sum(iteration => iteration.Cuts).Should().Be(1);
        backend.FixedHistory.Should().Equal(Flt64.Zero, Flt64.One);
        models.Verify(result.Value.Values).IsOk.Should().BeTrue();

        var invalid = new Dictionary<IVariableItem, Flt64>(result.Value.Values) {
            [y] = new Flt64(3),
        };
        models.Verify(invalid).IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task ProvenInfeasibleMaster_ShouldReturnModelInfeasible() {
        var x = new BinVar("infeasible-decision");
        var y = new URealVar("unused-recourse");
        using var source = new LinearMetaModel<Flt64>(name: "infeasible-master");
        source.Add(x).IsOk.Should().BeTrue();
        source.Add(y).IsOk.Should().BeTrue();
        source.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: Linear(x),
            name: "objective",
            displayName: null
        ).IsOk.Should().BeTrue();

        using LinearBendersModels models = await BuildAsync(source);
        var backend = new InfeasibleMasterBackend();

        Result<LinearBendersResult, ErrorCode, Error<ErrorCode>> result =
            await new LinearBendersAlgorithm(backend).SolveAsync(models);

        result.Should()
            .BeOfType<Failed<LinearBendersResult, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ORModelInfeasible);
        backend.MasterCalls.Should().Be(1);
        backend.SubCalls.Should().Be(0);
    }

    [Fact]
    public async Task RepeatedFarkasCertificates_ShouldDeduplicateAndStall() {
        var x = new BinVar("decision");
        var y = new URealVar("recourse");
        y.Range.Leq(new Flt64(2)).Should().BeTrue();
        using var source = new LinearMetaModel<Flt64>(name: "stall");
        source.Add(x).IsOk.Should().BeTrue();
        source.Add(y).IsOk.Should().BeTrue();
        source.AddConstraint(
            relation: Recourse(x, y, new Flt64(3)),
            group: null,
            name: "recourse"
        ).IsOk.Should().BeTrue();
        source.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: Linear(x),
            name: "objective",
            displayName: null
        ).IsOk.Should().BeTrue();

        using LinearBendersModels models = await BuildAsync(source);
        var backend = new FeasibilityBackend(x, y, repeatInfeasible: true);

        Result<LinearBendersResult, ErrorCode, Error<ErrorCode>> result =
            await new LinearBendersAlgorithm(backend).SolveAsync(
                models: models,
                config: new LinearBendersConfig { StallLimit = 2 }
            );

        result.IsFailed.Should().BeTrue();
        backend.MasterCalls.Should().Be(3);
        models.Master.RelationConstraints
            .Count(constraint => constraint.Name.StartsWith("benders_cut_", StringComparison.Ordinal))
            .Should().Be(1);
    }

    [Fact]
    public async Task TimeLimitedMaster_ShouldPreserveFeasibleIncumbentWithoutOptimalClaim() {
        var x = new BinVar("decision");
        var y = new URealVar("recourse");
        y.Range.Leq(new Flt64(2)).Should().BeTrue();
        using var source = new LinearMetaModel<Flt64>(name: "limited");
        source.Add(x).IsOk.Should().BeTrue();
        source.Add(y).IsOk.Should().BeTrue();
        source.AddConstraint(
            relation: Recourse(x, y, new Flt64(3)),
            group: null,
            name: "recourse"
        ).IsOk.Should().BeTrue();
        source.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: Linear(x),
            name: "objective",
            displayName: null
        ).IsOk.Should().BeTrue();

        using LinearBendersModels models = await BuildAsync(source);
        var backend = new FeasibilityBackend(x, y, limited: true);

        Result<LinearBendersResult, ErrorCode, Error<ErrorCode>> result =
            await new LinearBendersAlgorithm(backend).SolveAsync(models);

        result.IsOk.Should().BeTrue(result.ToString());
        result.Value.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        result.Value.TerminationReason.Should().Be(TerminationReason.TimeLimit);
        result.Value.SolutionPresence.Should().Be(SolutionPresence.Incumbent);
        result.Value.ProofStatus.Should().Be(ProofStatus.None);
        result.Value.Gap.Should().Be(1.0);
    }

    [Fact]
    public async Task NonoptimalRecourse_ShouldKeepVerifiedIncumbentWithoutOptimalityCut() {
        var x = new BinVar("decision");
        var y = new URealVar("recourse");
        y.Range.Leq(new Flt64(2)).Should().BeTrue();
        using var source = new LinearMetaModel<Flt64>(name: "limited-recourse");
        source.Add(x).IsOk.Should().BeTrue();
        source.Add(y).IsOk.Should().BeTrue();
        source.AddConstraint(
            relation: Recourse(x, y, new Flt64(3)),
            group: null,
            name: "recourse"
        ).IsOk.Should().BeTrue();
        source.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: Linear(x),
            name: "objective",
            displayName: null
        ).IsOk.Should().BeTrue();

        using LinearBendersModels models = await BuildAsync(source);
        var backend = new FeasibilityBackend(x, y, limitedRecourse: true);

        Result<LinearBendersResult, ErrorCode, Error<ErrorCode>> result =
            await new LinearBendersAlgorithm(backend).SolveAsync(models);

        result.IsOk.Should().BeTrue(result.ToString());
        result.Value.TerminationReason.Should().Be(TerminationReason.TimeLimit);
        result.Value.SolutionPresence.Should().Be(SolutionPresence.Incumbent);
        result.Value.TerminationDetail.Should().Be("nonoptimal recourse termination");
        result.Value.Iterations[^1].Cuts.Should().Be(0);
        models.Verify(result.Value.Values).IsOk.Should().BeTrue();
    }

    [Fact]
    public async Task FeasibleSubproblemWithoutUnifiedReport_ShouldFail() {
        var x = new BinVar("decision");
        var y = new URealVar("recourse");
        y.Range.Leq(new Flt64(2)).Should().BeTrue();
        using var source = new LinearMetaModel<Flt64>(name: "missing-report");
        source.Add(x).IsOk.Should().BeTrue();
        source.Add(y).IsOk.Should().BeTrue();
        source.AddConstraint(
            relation: Recourse(x, y, new Flt64(3)),
            group: null,
            name: "recourse"
        ).IsOk.Should().BeTrue();
        source.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: Linear(x),
            name: "objective",
            displayName: null
        ).IsOk.Should().BeTrue();

        using LinearBendersModels models = await BuildAsync(source);
        var backend = new FeasibilityBackend(
            x: x,
            y: y,
            limited: true,
            missingSubReport: true
        );

        Result<LinearBendersResult, ErrorCode, Error<ErrorCode>> result =
            await new LinearBendersAlgorithm(backend).SolveAsync(models);

        result.Should()
            .BeOfType<Failed<LinearBendersResult, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.OREngineSolvingException);
        result.Should()
            .BeOfType<Failed<LinearBendersResult, ErrorCode, Error<ErrorCode>>>()
            .Which.Error.Message.Should().Contain("missing its unified report");
    }

    [Fact]
    public async Task InvalidBindings_ShouldFailBeforeBackendInvocation() {
        var backend = new ThrowingBackend();

        async Task AssertInvalidAsync(
            Func<BinVar, URealVar, IReadOnlyDictionary<string, IVariableItem>> bindingsFactory,
            bool addThetaToSub = false) {
            var x = new BinVar("binding-x");
            var theta = new URealVar("binding-theta");
            var master = new LinearMetaModel<Flt64>();
            var sub = new LinearMetaModel<Flt64>();
            master.Add(x).IsOk.Should().BeTrue();
            sub.Add(x).IsOk.Should().BeTrue();
            master.Add(theta).IsOk.Should().BeTrue();
            if (addThetaToSub) {
                sub.Add(theta).IsOk.Should().BeTrue();
            }
            using var models = new LinearBendersModels(
                master: master,
                sub: sub,
                bindings: bindingsFactory(x, theta),
                theta: theta,
                variables: new IVariableItem[] { x },
                feasibilityOnly: false,
                verify: _ => Results.OkInstance
            );

            Result<LinearBendersResult, ErrorCode, Error<ErrorCode>> result =
                await new LinearBendersAlgorithm(backend).SolveAsync(models);
            result.IsFailed.Should().BeTrue();
        }

        await AssertInvalidAsync((x, _) =>
            new Dictionary<string, IVariableItem> { [""] = x });
        await AssertInvalidAsync((x, _) =>
            new Dictionary<string, IVariableItem> { ["first"] = x, ["second"] = x });
        await AssertInvalidAsync((_, theta) =>
            new Dictionary<string, IVariableItem> { ["theta"] = theta },
            addThetaToSub: true);
        await AssertInvalidAsync((x, _) => {
            var unknown = new URealVar("unknown-binding");
            return new Dictionary<string, IVariableItem> { ["unknown"] = unknown };
        });

        backend.MasterCalls.Should().Be(0);
        backend.SubCalls.Should().Be(0);
    }

    [Fact]
    public async Task UnfixedIntegerInSubproblem_ShouldFailBeforeBackendInvocation() {
        var x = new BinVar("unfixed-integer");
        var theta = new URealVar("theta");
        var master = new LinearMetaModel<Flt64>();
        var sub = new LinearMetaModel<Flt64>();
        master.Add(theta).IsOk.Should().BeTrue();
        sub.Add(x).IsOk.Should().BeTrue();
        using var models = new LinearBendersModels(
            master: master,
            sub: sub,
            bindings: new Dictionary<string, IVariableItem>(),
            theta: theta,
            variables: new IVariableItem[] { x },
            feasibilityOnly: false,
            verify: _ => Results.OkInstance
        );
        var backend = new ThrowingBackend();

        Result<LinearBendersResult, ErrorCode, Error<ErrorCode>> result =
            await new LinearBendersAlgorithm(backend).SolveAsync(models);

        result.IsFailed.Should().BeTrue();
        result.Should()
            .BeOfType<Failed<LinearBendersResult, ErrorCode, Error<ErrorCode>>>()
            .Which.Error.Message.Should().Contain("unfixed integer");
        backend.MasterCalls.Should().Be(0);
        backend.SubCalls.Should().Be(0);
    }

    [Fact]
    public async Task Partition_ShouldPlaceStagedContinuousObjectiveInRecourse() {
        var x = new BinVar("master-extension");
        var y = new URealVar("recourse-extension");
        y.Range.Leq(new Flt64(3)).Should().BeTrue();
        using var source = new LinearMetaModel<Flt64>(name: "staged-extension");
        source.Add(y).IsOk.Should().BeTrue();
        source.Add(x).IsOk.Should().BeTrue();
        source.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                new[]
                {
                    new LinearMonomial<Flt64>(Flt64.One, x),
                    new LinearMonomial<Flt64>(new Flt64(-2), y),
                },
                new Flt64(5)
            ),
            name: "objective",
            displayName: null
        ).IsOk.Should().BeTrue();

        var stages = new Dictionary<IVariableItem, LinearBendersStage> {
            [y] = LinearBendersStage.ContinuousRecourse,
        };
        using LinearBendersModels models = await BuildAsync(source, stages);

        models.FeasibilityOnly.Should().BeFalse();
        models.Bindings.Values.Should().Equal(x);
        models.Theta.LowerBound!.Value.Unwrap().Should().Be(new Flt64(-6));
        models.Master.MetaSubObjects.Single().Polynomial.Constant.Should().Be(new Flt64(5));
        models.Master.MetaSubObjects.Single().Polynomial.Monomials
            .Should().ContainSingle(term => term.Symbol == x);
        models.Sub.MetaSubObjects.Single().Polynomial.Monomials
            .Should().ContainSingle(term =>
                term.Symbol == y && term.Coefficient.Eq(new Flt64(-2)));

        Result<LinearBendersModels, ErrorCode, Error<ErrorCode>> invalid =
            await LinearBendersPartition.BuildAsync(
                source: source,
                stages: new Dictionary<IVariableItem, LinearBendersStage> {
                    [x] = LinearBendersStage.ContinuousRecourse,
                }
            );
        invalid.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task LegacyMasterAdapter_ShouldExposeIncumbentWithoutOptimalProof() {
        IBendersDecompositionSolver backend = new LegacyOnlyBackend();
        using var model = new LinearMetaModel<Flt64>(name: "legacy-master");

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await backend.SolveMasterReportAsync("legacy-master", model);

        result.IsOk.Should().BeTrue(result.ToString());
        result.Value.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        result.Value.TerminationReason.Should().Be(TerminationReason.Completed);
        result.Value.SolutionPresence.Should().Be(SolutionPresence.Incumbent);
        result.Value.Proof.Status.Should().Be(ProofStatus.None);
    }

    private static async Task<LinearBendersModels> BuildAsync(
        LinearMetaModel<Flt64> source,
        IReadOnlyDictionary<IVariableItem, LinearBendersStage>? stages = null) {
        Result<LinearBendersModels, ErrorCode, Error<ErrorCode>> result =
            await LinearBendersPartition.BuildAsync(source, stages);
        result.IsOk.Should().BeTrue(result.ToString());
        return result.Value;
    }

    private static LinearPolynomial<Flt64> Linear(IVariableItem variable) =>
        new(
            new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
            Flt64.Zero
        );

    private static LinearInequality<Flt64> Recourse(
        IVariableItem x,
        IVariableItem y,
        Flt64 rhs) =>
        new LinearPolynomial<Flt64>(
            new[]
            {
                new LinearMonomial<Flt64>(Flt64.One, x),
                new LinearMonomial<Flt64>(Flt64.One, y),
            },
            Flt64.Zero
        ).Ge(rhs);

    private static SolveReport<Flt64> Report(
        ProblemStatus problemStatus,
        TerminationReason terminationReason,
        SolutionPresence solutionPresence,
        IReadOnlyList<Flt64>? values = null,
        Flt64? objective = null,
        Flt64? bestBound = null,
        Flt64? gap = null,
        bool proofClaimed = false) {
        return new SolveReport<Flt64> {
            ProblemStatus = problemStatus,
            TerminationReason = terminationReason,
            SolutionPresence = solutionPresence,
            Solution = values is null
                ? null
                : new SolveSolution<Flt64>(
                    Values: new Solution<Flt64>(values),
                    Objective: objective
                ),
            Proof = proofClaimed
                ? new SolveProof(ProofStatus.Claimed, "test-backend")
                : new SolveProof(ProofStatus.None),
            Statistics = new SolveStatistics(
                SolveTime: TimeSpan.Zero,
                BestBound: bestBound,
                Gap: gap
            ),
        };
    }

    private abstract class BackendBase : IBendersDecompositionSolver {
        public abstract string Name { get; }
        public int MasterCalls { get; protected set; }
        public int SubCalls { get; protected set; }

        public virtual Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("Legacy master output is not used by this test backend.");

        public abstract Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>
            SolveMasterReportAsync(
                string name,
                LinearMetaModel<Flt64> metaModel,
                bool toLogModel = false,
                RegistrationStatusCallBack? registrationStatusCallBack = null,
                SolvingStatusCallBack? solvingStatusCallBack = null);

        public virtual Task<Result<IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>>
            SolveSubAsync(
                string name,
                LinearMetaModel<Flt64> metaModel,
                IVariableItem objectVariable,
                IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
                bool toLogModel = false,
                RegistrationStatusCallBack? registrationStatusCallBack = null,
                SolvingStatusCallBack? solvingStatusCallBack = null) {
            SubCalls++;
            throw new NotSupportedException("This test backend does not solve subproblems.");
        }

        public virtual Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>>
            SolveMasterAsAsync<V>(
                string name,
                LinearMetaModel<Flt64> metaModel,
                IIntoValue<V> converter,
                bool toLogModel = false,
                RegistrationStatusCallBack? registrationStatusCallBack = null,
                SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("Converted master output is not used by this test backend.");

        public virtual Task<Result<IBendersDecompositionSolver.LinearSubResultOf<V>, ErrorCode, Error<ErrorCode>>>
            SolveSubAsAsync<V>(
                LinearMetaModel<Flt64> metaModel,
                IVariableItem objectVariable,
                IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
                FrameworkSolveOptions options)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("Converted subproblem output is not used by this test backend.");
    }

    private sealed class ThrowingBackend : BackendBase {
        public override string Name => "throwing-backend";

        public override Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>
            SolveMasterReportAsync(
                string name,
                LinearMetaModel<Flt64> metaModel,
                bool toLogModel = false,
                RegistrationStatusCallBack? registrationStatusCallBack = null,
                SolvingStatusCallBack? solvingStatusCallBack = null) {
            MasterCalls++;
            throw new InvalidOperationException("Validation must happen before backend invocation.");
        }
    }

    private sealed class InfeasibleMasterBackend : BackendBase {
        public override string Name => "infeasible-master-backend";

        public override Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>
            SolveMasterReportAsync(
                string name,
                LinearMetaModel<Flt64> metaModel,
                bool toLogModel = false,
                RegistrationStatusCallBack? registrationStatusCallBack = null,
                SolvingStatusCallBack? solvingStatusCallBack = null) {
            MasterCalls++;
            return Task.FromResult(Results.Ok(Report(
                problemStatus: ProblemStatus.Infeasible,
                terminationReason: TerminationReason.Completed,
                solutionPresence: SolutionPresence.None
            )));
        }
    }

    private sealed class LegacyOnlyBackend : IBendersDecompositionSolver {
        public string Name => "legacy-only-backend";

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            Task.FromResult(Results.Ok<SolverOutput>(new FeasibleSolverOutput<Flt64>(
                Obj: Flt64.Zero,
                Solution: new Solution<Flt64>(Array.Empty<Flt64>()),
                Time: TimeSpan.Zero,
                PossibleBestObj: Flt64.Zero,
                Gap: Flt64.Zero)));

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException();

        public Task<Result<IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>> SolveSubAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IVariableItem objectVariable,
            IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException();

        public Task<Result<IBendersDecompositionSolver.LinearSubResultOf<V>, ErrorCode, Error<ErrorCode>>>
            SolveSubAsAsync<V>(
                LinearMetaModel<Flt64> metaModel,
                IVariableItem objectVariable,
                IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
                FrameworkSolveOptions options)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException();
    }

    private sealed class FeasibilityBackend : BackendBase {
        private readonly BinVar _x;
        private readonly bool _repeatInfeasible;
        private readonly bool _limited;
        private readonly bool _limitedRecourse;
        private readonly bool _missingSubReport;

        public FeasibilityBackend(
            BinVar x,
            URealVar y,
            bool repeatInfeasible = false,
            bool limited = false,
            bool limitedRecourse = false,
            bool missingSubReport = false) {
            _x = x;
            _repeatInfeasible = repeatInfeasible;
            _limited = limited;
            _limitedRecourse = limitedRecourse;
            _missingSubReport = missingSubReport;
        }

        public override string Name => "feasibility-backend";
        public List<Flt64> FixedHistory { get; } = new();

        public override Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>
            SolveMasterReportAsync(
                string name,
                LinearMetaModel<Flt64> metaModel,
                bool toLogModel = false,
                RegistrationStatusCallBack? registrationStatusCallBack = null,
                SolvingStatusCallBack? solvingStatusCallBack = null) {
            MasterCalls++;
            Flt64 value = _limited ||
                (!_repeatInfeasible && MasterCalls > 1)
                ? Flt64.One
                : Flt64.Zero;
            Flt64[] values = metaModel.Tokens.TokensInSolver
                .Select(token => token.Variable == _x ? value : Flt64.Zero)
                .ToArray();
            return Task.FromResult(Results.Ok(Report(
                problemStatus: ProblemStatus.Feasible,
                terminationReason: _limited
                    ? TerminationReason.TimeLimit
                    : TerminationReason.Completed,
                solutionPresence: _limited
                    ? SolutionPresence.Incumbent
                    : SolutionPresence.Optimal,
                values: values,
                objective: value,
                bestBound: _limited ? Flt64.Zero : value,
                gap: _limited ? Flt64.One : Flt64.Zero,
                proofClaimed: !_limited
            )));
        }

        public override async Task<Result<IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>>
            SolveSubAsync(
                string name,
                LinearMetaModel<Flt64> metaModel,
                IVariableItem objectVariable,
                IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
                bool toLogModel = false,
                RegistrationStatusCallBack? registrationStatusCallBack = null,
                SolvingStatusCallBack? solvingStatusCallBack = null) {
            SubCalls++;
            Flt64 current = fixedVariables[_x];
            FixedHistory.Add(current);
            if (_repeatInfeasible || current.Eq(Flt64.Zero)) {
                Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> expandedResult =
                    await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);
                expandedResult.IsOk.Should().BeTrue(expandedResult.ToString());
                using LinearMechanismModel<Flt64> expanded = expandedResult.Value;
                MathConstraint origin = ((IMechanismModel<Flt64>)expanded).Constraints
                    .Single(constraint => constraint.Name == "recourse")
                    .Origin!;
                return Results.Ok<IBendersDecompositionSolver.LinearSubResult>(
                    new IBendersDecompositionSolver.LinearInfeasibleResult(
                        FarkasDualSolution: new Dictionary<MathConstraint, Flt64> {
                            [origin] = -Flt64.One,
                        },
                        Cuts: null
                    )
                );
            }

            SolveReport<Flt64> subReport = Report(
                problemStatus: ProblemStatus.Feasible,
                terminationReason: _limitedRecourse
                    ? TerminationReason.TimeLimit
                    : TerminationReason.Completed,
                solutionPresence: _limitedRecourse
                    ? SolutionPresence.Incumbent
                    : SolutionPresence.Optimal,
                values: new[] { new Flt64(2) },
                objective: Flt64.Zero,
                bestBound: Flt64.Zero,
                gap: Flt64.Zero,
                proofClaimed: !_limitedRecourse
            );
            var feasible = new IBendersDecompositionSolver.LinearFeasibleResult(
                Result: new FeasibleSolverOutput<Flt64>(
                    Obj: Flt64.Zero,
                    Solution: new Solution<Flt64>(new[] { new Flt64(2) }),
                    Time: TimeSpan.Zero,
                    PossibleBestObj: Flt64.Zero,
                    Gap: Flt64.Zero
                ),
                DualSolution: new Dictionary<MathConstraint, Flt64>(),
                Cuts: null
            ) {
                Report = _missingSubReport ? null : subReport,
            };
            return Results.Ok<IBendersDecompositionSolver.LinearSubResult>(feasible);
        }
    }
}
