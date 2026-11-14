#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Framework.Solver;

namespace Fuookami.Ospf.Framework.Tests.Solver;

public class ColumnGenerationBatchTest {
    private static readonly string[] Slots = {
        "aircraft-1@slot-2",
        "aircraft-2@slot-2",
    };

    [Fact]
    public void Scope_ShouldUseSetSemanticsForExecutorSlots() {
        ColumnGenerationScope scope = Scope();
        ColumnGenerationScope reordered = Scope(executorSlots: Slots.Reverse());

        reordered.Should().Be(scope);
        reordered.GetHashCode().Should().Be(scope.GetHashCode());
    }

    [Fact]
    public void ExhaustedEmptyBatch_CertifiesOnlyItsExactFullScope() {
        ColumnGenerationScope scope = Scope();
        var batch = new ColumnGenerationBatch<string>(
            columns: Array.Empty<string>(),
            completion: ColumnGenerationCompletion.Exhausted,
            scope: scope,
            noImprovingColumns: true);

        batch.Certifies(scope).Should().BeTrue();
        batch.CertifiesNoImprovingColumns(scope).Should().BeTrue();
        batch.Certifies(Scope(request: "run-7:pricing-5")).Should().BeFalse();
        batch.Certifies(Scope(model: "run-8")).Should().BeFalse();
        batch.Certifies(Scope(dual: "lp-iteration-13")).Should().BeFalse();
        batch.Certifies(Scope(branch: "root")).Should().BeFalse();
        batch.Certifies(Scope(group: "aircraft-1")).Should().BeFalse();
        batch.Certifies(Scope(executorSlots: new[] { "aircraft-1@slot-3" })).Should().BeFalse();
        batch.Certifies(Scope(global: false)).Should().BeFalse();
    }

    [Fact]
    public void MissingModelOrPricingIdentity_CannotCertifyEvenWithMatchingScopes() {
        ColumnGenerationScope[] incompleteScopes = {
            Scope(request: ""),
            Scope(model: ""),
            Scope(dual: " "),
            Scope(branch: ""),
        };

        foreach (ColumnGenerationScope incomplete in incompleteScopes) {
            var batch = new ColumnGenerationBatch<string>(
                columns: Array.Empty<string>(),
                completion: ColumnGenerationCompletion.Exhausted,
                scope: incomplete,
                noImprovingColumns: true);

            batch.Certifies(incomplete).Should().BeFalse();
            batch.CertifiesNoImprovingColumns(incomplete).Should().BeFalse();
        }
    }

    [Fact]
    public void EmptyOutput_DoesNotInferNoImprovementWithoutExplicitEvidence() {
        ColumnGenerationScope scope = Scope();
        var batch = new ColumnGenerationBatch<string>(
            columns: Array.Empty<string>(),
            completion: ColumnGenerationCompletion.Exhausted,
            scope: scope);

        batch.Certifies(scope).Should().BeTrue();
        batch.CertifiesNoImprovingColumns(scope).Should().BeFalse();
    }

    [Fact]
    public void PartialOrHeuristicBatches_CannotCertifyNoImprovingColumns() {
        ColumnGenerationScope scope = Scope();
        var budgetLimited = new ColumnGenerationBatch<string>(
            columns: Array.Empty<string>(),
            completion: ColumnGenerationCompletion.BudgetLimit,
            scope: scope,
            noImprovingColumns: true);
        var heuristic = new ColumnGenerationBatch<string>(
            columns: Array.Empty<string>(),
            completion: ColumnGenerationCompletion.Heuristic,
            scope: scope,
            noImprovingColumns: true);
        var hasCandidates = new ColumnGenerationBatch<string>(
            columns: new[] { "candidate" },
            completion: ColumnGenerationCompletion.Exhausted,
            scope: scope);
        var contradictoryOutputWithIndependentProof = new ColumnGenerationBatch<string>(
            columns: new[] { "non-improving candidate" },
            completion: ColumnGenerationCompletion.Exhausted,
            scope: scope,
            noImprovingColumns: true);

        budgetLimited.CertifiesNoImprovingColumns(scope).Should().BeFalse();
        heuristic.CertifiesNoImprovingColumns(scope).Should().BeFalse();
        hasCandidates.CertifiesNoImprovingColumns(scope).Should().BeFalse();
        contradictoryOutputWithIndependentProof
            .CertifiesNoImprovingColumns(scope)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void AggregateEvidence_CannotOverrideIncompleteExecutorOrSlotResult() {
        ColumnGenerationScope scope = Scope();
        ColumnGenerationScope firstScope = ChildScope(scope, "aircraft-1");
        ColumnGenerationScope secondScope = ChildScope(scope, "aircraft-2");
        var first = new ColumnGenerationBatch<string>(
            columns: Array.Empty<string>(),
            completion: ColumnGenerationCompletion.Exhausted,
            scope: firstScope,
            noImprovingColumns: true);
        var incomplete = new ColumnGenerationBatch<string>(
            columns: Array.Empty<string>(),
            completion: ColumnGenerationCompletion.BudgetLimit,
            scope: secondScope,
            noImprovingColumns: true);
        var aggregate = new ColumnGenerationBatch<string>(
            columns: Array.Empty<string>(),
            completion: ColumnGenerationCompletion.Exhausted,
            scope: scope,
            noImprovingColumns: true,
            children: new[] { first, incomplete });

        aggregate.Certifies(scope).Should().BeFalse();
        aggregate.CertifiesNoImprovingColumns(scope).Should().BeFalse();
    }

    [Fact]
    public void FullyCertifiedChildren_CanSupportAggregateCertificate() {
        ColumnGenerationScope scope = Scope();
        ColumnGenerationBatch<string>[] children = new[] { "aircraft-1", "aircraft-2" }
            .Select(executor => new ColumnGenerationBatch<string>(
                columns: Array.Empty<string>(),
                completion: ColumnGenerationCompletion.Exhausted,
                scope: ChildScope(scope, executor),
                noImprovingColumns: true))
            .ToArray();
        var aggregate = new ColumnGenerationBatch<string>(
            columns: Array.Empty<string>(),
            completion: ColumnGenerationCompletion.Exhausted,
            scope: scope,
            noImprovingColumns: true,
            children: children);

        aggregate.Certifies(scope).Should().BeTrue();
        aggregate.CertifiesNoImprovingColumns(scope).Should().BeTrue();
    }

    private static ColumnGenerationScope ChildScope(
        ColumnGenerationScope parent,
        string executor) =>
        new(
            request: $"{parent.Request}:{executor}",
            group: executor,
            branch: parent.Branch,
            global: parent.Global,
            model: parent.Model,
            dual: parent.Dual,
            executorSlots: new[] { $"{executor}@slot-2" });

    private static ColumnGenerationScope Scope(
        string request = "run-7:pricing-4",
        string group = "aircraft-1|aircraft-2",
        string branch = "fixed=aircraft-3|hidden=aircraft-4",
        bool global = true,
        string model = "run-7",
        string dual = "lp-iteration-12",
        IEnumerable<string>? executorSlots = null) =>
        new(
            request: request,
            group: group,
            branch: branch,
            global: global,
            model: model,
            dual: dual,
            executorSlots: executorSlots ?? Slots);
}
