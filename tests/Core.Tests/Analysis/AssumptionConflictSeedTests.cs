#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 假设式冲突种子 / The assumption-based conflict seed.
/// </summary>
/// <remarks>
/// **这条路拿不到种子时返回 `null`，返回空集合会被读成"模型没有冲突"——完全相反的结论。**
///
/// **When this path cannot produce a seed it returns `null`; an empty list would be read as "the model has no conflict" — the opposite conclusion.**
/// </remarks>
public class AssumptionConflictSeedTests {
    private static ConstraintProgrammingModelSnapshot Fixture() {
        var model = new ConstraintProgrammingModel("seed", ObjectCategory.Maximum, identityNamespace: "seed");
        var x = new IntVar("x");
        var xId = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;
        model.RegisterVariable(id: xId, variable: x, domain: domain, scope: ModelElementScope.Stable, origin: "fixture/x")
            .Value.Should().NotBeNull();
        var expression = new ConstraintProgrammingExpression.Variable(x, domain, xId);
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        model.AddConstraint(ConstraintProgrammingConstraint.CreateLessOrEqual(expression, new Int64(4)).Value,
            id: new ConstraintId("c:le"), name: "le").Value.Should().NotBeNull();
        model.AddConstraint(ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, new Int64(6)).Value,
            id: new ConstraintId("c:ge"), name: "ge").Value.Should().NotBeNull();
        model.Maximize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();
        return model.Snapshot().Value!;
    }

    private static IReadOnlyList<DiagnosticSource> Asked() => [
        new DiagnosticSource.Constraint(new ConstraintId("c:le")),
        new DiagnosticSource.Constraint(new ConstraintId("c:ge")),
    ];

    /// <summary>**未证明的不可行不是种子** / Unproven infeasibility is not a seed.</summary>
    /// <remarks>
    /// 后端返回 `Infeasible` 但 `ProofStatus.Claimed`。**把它当成种子，报告会说"这是冲突"——而实际什么都没证。**
    ///
    /// The backend returns `Infeasible` with `ProofStatus.Claimed`. **Treating it as a seed would let the report say "this is the conflict" when nothing was established.**
    /// </remarks>
    [Fact]
    public async Task TheUnprovenInfeasibility_ShouldGiveNoSeed() {
        // **必须带一个真实的冲突**：不带冲突的话，即使去掉证明检查，重映射也会给出空结果，
        // 这条判据就分不清"因为没证明"和"因为没冲突"——**第一次写的时候正是这样，变异测不出来。**
        //
        // **It has to carry a real conflict**: without one, the remap returns empty even when the proof check is gone, and this criterion cannot tell "because unproven" from
        // "because no conflict" — **which is exactly how it was first written, and the mutation did not catch it.**
        var conflict = new ConstraintProgrammingConflict(
            constraintIds: [new ConstraintId("c:le")]);
        IReadOnlyList<DiagnosticSource>? unproven = await AssumptionConflictSeed.TrySeedAsync(
            Fixture(), Asked(),
            new SeedSolver(new ConstraintProgrammingInfeasibleOutput(
                conflict: conflict, proofStatus: ProofStatus.Claimed)));

        unproven.Should().BeNull("nothing was proven, so this path produced no seed rather than an empty one");

        // **反向对照**：同一个冲突，只把状态换成 `Verified`，就必须给出种子。
        // **The reverse control**: the same conflict with `Verified` has to yield a seed.
        IReadOnlyList<DiagnosticSource>? proven = await AssumptionConflictSeed.TrySeedAsync(
            Fixture(), Asked(),
            new SeedSolver(new ConstraintProgrammingInfeasibleOutput(
                conflict: conflict, proofStatus: ProofStatus.Verified)));
        proven.Should().NotBeNull();
    }

    /// <summary>**已证明的不可行给出种子，并带上背景证据** / A proven conflict yields a seed with its background evidence.</summary>
    /// <remarks>
    /// 边界与值域是**背景证据**：assumption 派生模型保持它们激活，
    /// 所以即使后端只报 guard 约束，种子也必须包含这些可独立复验的来源。
    ///
    /// Bounds and domains are **background evidence**: the assumption model keeps them active, so even when the backend reports only guarded constraints the seed has to carry these
    /// independently verifiable sources.
    /// </remarks>
    [Fact]
    public async Task TheProvenConflict_ShouldCarryBackgroundEvidence() {
        var boundary = new DiagnosticSource.VariableLowerBound(new VariableId("source:x"));
        var asked = new List<DiagnosticSource> {
            new DiagnosticSource.Constraint(new ConstraintId("c:le")),
            boundary,
        };
        var conflict = new ConstraintProgrammingConflict(
            constraintIds: [new ConstraintId("c:le"), new ConstraintId("c:ge")]);

        IReadOnlyList<DiagnosticSource>? seed = await AssumptionConflictSeed.TrySeedAsync(
            Fixture(), asked,
            new SeedSolver(new ConstraintProgrammingInfeasibleOutput(
                conflict: conflict, proofStatus: ProofStatus.Verified)));

        seed.Should().NotBeNull();
        seed.Should().Contain(boundary,
            "the boundary is background evidence that the assumption model keeps active");
        seed.Should().Contain(new DiagnosticSource.Constraint(new ConstraintId("c:le")));
        seed.Should().NotContain(new DiagnosticSource.Constraint(new ConstraintId("c:ge")),
            "c:ge was reported by the backend but was not part of the request");
    }

    /// <summary>**求解器建不出 session 就没有种子** / No session means no seed.</summary>
    [Fact]
    public async Task TheMissingSession_ShouldGiveNoSeed() {
        IReadOnlyList<DiagnosticSource>? seed = await AssumptionConflictSeed.TrySeedAsync(
            Fixture(), Asked(), new NoSessionSolver());

        seed.Should().BeNull("this path could not even start, which is not the same as no conflict");
    }

    /// <summary>**求解抛异常也没有种子** / A throwing solve gives no seed.</summary>
    [Fact]
    public async Task TheThrowingSolve_ShouldGiveNoSeed() {
        IReadOnlyList<DiagnosticSource>? seed = await AssumptionConflictSeed.TrySeedAsync(
            Fixture(), Asked(), new ThrowingSeedSolver());

        seed.Should().BeNull("a backend crash on this best-effort path is not evidence of anything");
    }

    /// <summary>**给每条约束都加了 guard** / Every constraint gets a guard.</summary>
    /// <remarks>
    /// **漏掉一条，那条约束就永远是"激活"的**，于是假设求解不可能通过取反它来发现冲突——
    /// **种子会少一条，而报告不会提。**
    ///
    /// **A missed guard leaves that constraint permanently active**, so the assumption solve can never find a conflict by negating it — **the seed loses a member and the report
    /// says nothing about it.**
    /// </remarks>
    [Fact]
    public async Task TheSeed_ShouldGuardEveryConstraint() {
        var solver = new RecordingSeedSolver();
        await AssumptionConflictSeed.TrySeedAsync(Fixture(), Asked(), solver);

        solver.SubmittedConstraintIds.Should().NotBeNull();
        solver.SubmittedConstraintIds!.Should().BeEquivalentTo(["c:le", "c:ge"],
            "each original constraint is replaced by a guarded version under the same id");
        solver.SubmittedVariables.Should().Be(3,
            "the one model variable plus one guard per constraint");
        solver.AssumptionCount.Should().Be(2, "both guards are assumed");
    }

    /// <summary>总是返回固定输出的求解器 / A solver returning one fixed output.</summary>
    private class SeedSolver(ConstraintProgrammingSolverOutput output) : IConstraintProgrammingSolver {
        public SolverDescriptor Descriptor => new FakeConstraintProgrammingSolver().Descriptor;

        public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Ok(output));

        public Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> CreateSession(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null) {
            IConstraintProgrammingSession session = new FixedSession(model, output);
            return Results.Ok(session);
        }
    }

    private sealed class FixedSession(
        ConstraintProgrammingModel model,
        ConstraintProgrammingSolverOutput output) : IConstraintProgrammingSession {
        public ConstraintProgrammingModel Model => model;

        public ConstraintProgrammingSolveOptions Options { get; } = new();

        public bool IsClosed { get; private set; }

        public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
            IReadOnlyList<BooleanLiteral>? assumptions = null,
            IReadOnlyDictionary<VariableId, Int64>? fixedValues = null,
            ConstraintProgrammingSolution? hints = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Ok(output));

        public void Dispose() => IsClosed = true;
    }

    private sealed class NoSessionSolver : IConstraintProgrammingSolver {
        public SolverDescriptor Descriptor => new FakeConstraintProgrammingSolver().Descriptor;

        public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<ConstraintProgrammingSolverOutput>(
                new Err<ErrorCode>(ErrorCode.Other, "not used")));

        public Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> CreateSession(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null) =>
            Results.Failed<IConstraintProgrammingSession>(new Err<ErrorCode>(ErrorCode.Other, "no sessions here"));
    }

    private sealed class ThrowingSeedSolver : IConstraintProgrammingSolver {
        public SolverDescriptor Descriptor => new FakeConstraintProgrammingSolver().Descriptor;

        public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<ConstraintProgrammingSolverOutput>(
                new Err<ErrorCode>(ErrorCode.Other, "not used")));

        public Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> CreateSession(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null) =>
            Results.Ok<IConstraintProgrammingSession>(new ThrowingSession(model));
    }

    private sealed class ThrowingSession(ConstraintProgrammingModel model) : IConstraintProgrammingSession {
        public ConstraintProgrammingModel Model => model;

        public ConstraintProgrammingSolveOptions Options { get; } = new();

        public bool IsClosed { get; private set; }

        public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
            IReadOnlyList<BooleanLiteral>? assumptions = null,
            IReadOnlyDictionary<VariableId, Int64>? fixedValues = null,
            ConstraintProgrammingSolution? hints = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("backend exploded");

        public void Dispose() => IsClosed = true;
    }

    /// <summary>记录提交内容的求解器 / A solver that records what it was handed.</summary>
    private sealed class RecordingSeedSolver : IConstraintProgrammingSolver {
        public IReadOnlyList<string>? SubmittedConstraintIds { get; private set; }

        public int SubmittedVariables { get; private set; }

        public int AssumptionCount { get; private set; }

        public SolverDescriptor Descriptor => new FakeConstraintProgrammingSolver().Descriptor;

        public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<ConstraintProgrammingSolverOutput>(
                new Err<ErrorCode>(ErrorCode.Other, "not used")));

        public Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> CreateSession(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null) {
            ConstraintProgrammingModelSnapshot snapshot = model.Snapshot().Value!;
            SubmittedConstraintIds = snapshot.Constraints.Select(entry => entry.Id.Value).ToArray();
            SubmittedVariables = snapshot.Variables.Count;
            IConstraintProgrammingSession session = new RecordingSession(model, this);
            return Results.Ok(session);
        }

        private sealed class RecordingSession(
            ConstraintProgrammingModel model,
            RecordingSeedSolver owner) : IConstraintProgrammingSession {
            public ConstraintProgrammingModel Model => model;

            public ConstraintProgrammingSolveOptions Options { get; } = new();

            public bool IsClosed { get; private set; }

            public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
                IReadOnlyList<BooleanLiteral>? assumptions = null,
                IReadOnlyDictionary<VariableId, Int64>? fixedValues = null,
                ConstraintProgrammingSolution? hints = null,
                CancellationToken cancellationToken = default) {
                owner.AssumptionCount = assumptions?.Count ?? 0;
                return Task.FromResult(Results.Failed<ConstraintProgrammingSolverOutput>(
                    new Err<ErrorCode>(ErrorCode.Other, "recorded")));
            }

            public void Dispose() => IsClosed = true;
        }
    }
}
