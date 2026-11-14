#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Solver;

public class ColumnProjectionIndexTest {
    [Fact]
    public void SparseTerms_ShouldKeepDecisionOrderAndCaptureSourceMaps() {
        var variables = Enumerable.Range(0, 256)
            .Select(index => new BinVar($"v{index}"))
            .ToList();
        var decisions = variables.Select((variable, index) => new ColumnDecision(
            Id: $"d{index}",
            BlockId: $"b{index / 64}",
            Variable: variable)).ToList();
        var values = new CountingDictionary(
            variables[0], new Flt64(1e16),
            variables[2], new Flt64(-1e16),
            variables[1], Flt64.One);
        var row = new LinearModelRow<Flt64>(
            id: "ordered",
            sourceName: "ordered",
            coefficients: values,
            rhs: new Flt64(1e20),
            comparison: Comparison.LE);
        var empty = new LinearModelRow<Flt64>(
            id: "empty",
            sourceName: "empty",
            coefficients: new Dictionary<IVariableItem, Flt64>(),
            rhs: new Flt64(1e20),
            comparison: Comparison.LE);
        var mixed = new LinearModelRow<Flt64>(
            id: "mixed",
            sourceName: "mixed",
            coefficients: new Dictionary<IVariableItem, Flt64> {
                [variables[0]] = Flt64.One,
                [variables[65]] = Flt64.One,
            },
            rhs: new Flt64(1e20),
            comparison: Comparison.LE);
        var global = new LinearModelRow<Flt64>(
            id: "global",
            sourceName: "global",
            coefficients: new Dictionary<IVariableItem, Flt64> {
                [new RealVar("global")] = Flt64.One,
            },
            rhs: new Flt64(1e20),
            comparison: Comparison.LE);
        var snapshot = new LinearModelSnapshot<Flt64>(
            revision: new LinearModelRevision(1, 1),
            variables: Array.Empty<LinearVariableState<Flt64>>(),
            rows: new[] { row, empty, mixed, global },
            objective: values,
            objectiveConstant: Flt64.Zero,
            objectCategory: ObjectCategory.Minimum);
        var index = new ColumnProjectionIndex<Flt64>(
            decisions: decisions,
            blockIds: new[] { "b0", "b1", "b2", "b3", "unused" },
            snapshot: snapshot);
        int initialReads = values.Reads;

        index.InitializedTermIndexes().Should().BeEmpty();
        values.Clear();
        values[variables[3]] = new Flt64(2);
        for (int iteration = 0; iteration < 100; ++iteration) {
            IReadOnlyList<ColumnProjectionTerm<Flt64>> terms = index.RowTerms["ordered"]["b0"];
            terms.Select(term => term.Decision.Id).Should().Equal("d0", "d1", "d2");
            terms.Aggregate(Flt64.Zero, (sum, term) => sum.Plus(term.Coefficient))
                .Should().Be(Flt64.Zero);
            index.LocalRows["b0"].Select(localRow => localRow.Id)
                .Should().Equal("ordered", "empty");
            index.LocalRows["b1"].Select(localRow => localRow.Id)
                .Should().Equal("empty");
            index.LocalRows["unused"].Select(localRow => localRow.Id)
                .Should().Equal("empty");
        }
        index.InitializedTermIndexes().Should().Equal("rows");
        index.ObjectiveTerms["b0"].Select(term => term.Decision.Id)
            .Should().Equal("d0", "d1", "d2");
        index.InitializedTermIndexes().Should().BeEquivalentTo("rows", "objective");
        values.Reads.Should().Be(initialReads);
    }

    [Fact]
    public async Task ConcurrentFirstReaders_ShouldShareTheCapturedRevision() {
        var variable = new BinVar("v");
        var coefficients = new Dictionary<IVariableItem, Flt64> {
            [variable] = Flt64.One,
        };
        var snapshot = new LinearModelSnapshot<Flt64>(
            revision: new LinearModelRevision(11, 3),
            variables: Array.Empty<LinearVariableState<Flt64>>(),
            rows: new[] {
                new LinearModelRow<Flt64>(
                    id: "r",
                    sourceName: "r",
                    coefficients: coefficients,
                    rhs: Flt64.One,
                    comparison: Comparison.LE),
            },
            objective: coefficients,
            objectiveConstant: Flt64.Zero,
            objectCategory: ObjectCategory.Minimum);
        var index = new ColumnProjectionIndex<Flt64>(
            decisions: new[] {
                new ColumnDecision("d", "b", variable),
            },
            blockIds: new[] { "b" },
            snapshot: snapshot);
        coefficients.Clear();

        using var barrier = new Barrier(8);
        Task<(IReadOnlyDictionary<string, IReadOnlyDictionary<string,
                IReadOnlyList<ColumnProjectionTerm<Flt64>>>> Rows,
            IReadOnlyDictionary<string, IReadOnlyList<ColumnProjectionTerm<Flt64>>> Objective)>[] tasks =
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => {
                barrier.SignalAndWait();
                return (index.RowTerms, index.ObjectiveTerms);
            })).ToArray();
        (IReadOnlyDictionary<string, IReadOnlyDictionary<string,
                IReadOnlyList<ColumnProjectionTerm<Flt64>>>> Rows,
            IReadOnlyDictionary<string, IReadOnlyList<ColumnProjectionTerm<Flt64>>> Objective)[] results =
            await Task.WhenAll(tasks);

        results.Should().OnlyContain(result => ReferenceEquals(result.Rows, results[0].Rows));
        results.Should().OnlyContain(result => ReferenceEquals(result.Objective, results[0].Objective));
        index.RowTerms["r"]["b"].Single().Coefficient.Should().Be(Flt64.One);
        index.ObjectiveTerms["b"].Single().Coefficient.Should().Be(Flt64.One);
    }

    [Fact]
    public async Task SparseMaster_ShouldMatchFullDecisionFoldWithRefinedResourcesAndLateRows() {
        using var source = new LinearMetaModel<Flt64>(name: "sparse_source");
        var variables = Enumerable.Range(0, 16)
            .Select(index => new BinVar($"v{index}"))
            .ToList();
        var global = new RealVar("global");
        source.Add(variables).IsOk.Should().BeTrue();
        source.Add(global).IsOk.Should().BeTrue();
        var random = new Random(439);
        for (int row = 0; row < 12; ++row) {
            var selected = variables
                .OrderBy(_ => random.Next())
                .Take(row % 3 == 0 ? 16 : 3)
                .ToList();
            var terms = selected.Select(variable => new LinearMonomial<Flt64>(
                new Flt64(random.Next(-3, 4)),
                variable)).ToList();
            source.AddConstraint(
                relation: new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(terms, Flt64.Zero),
                    new LinearPolynomial<Flt64>(
                        Array.Empty<LinearMonomial<Flt64>>(),
                        new Flt64(100)),
                    Comparison.LE),
                group: null,
                name: $"r{row}").IsOk.Should().BeTrue();
        }
        source.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                variables.Select(variable => new LinearMonomial<Flt64>(Flt64.One, variable)).ToList(),
                new Flt64(3)),
            name: "objective",
            displayName: null).IsOk.Should().BeTrue();

        var decisions = variables.Select((variable, index) => new ColumnDecision(
            Id: $"d{index}",
            BlockId: $"b{index % 4}",
            Variable: variable)).ToList();
        var columns = Enumerable.Range(0, 4)
            .SelectMany(block => Enumerable.Range(0, 3).Select(column => new ProjectedColumn<Flt64>(
                id: $"c{block}_{column}",
                blockId: $"b{block}",
                values: decisions
                    .Where(decision => string.Equals(
                        decision.BlockId,
                        $"b{block}",
                        StringComparison.Ordinal))
                    .ToDictionary(
                        decision => decision.Id,
                        _ => new Flt64(column % 2),
                        StringComparer.Ordinal))))
            .ToList();
        var projection = new ColumnProjection<Flt64>(
            source: source,
            decisions: decisions,
            blockIds: new[] { "b0", "b1", "b2", "b3" });
        LinearModelSnapshot<Flt64> original = Success(await projection.SnapshotAsync());

        foreach (IReadOnlySet<IVariableItem>? refined in new IReadOnlySet<IVariableItem>?[] {
            null,
            new HashSet<IVariableItem> { variables[0], variables[1] },
        }) {
            using ColumnMasterRegistration<Flt64> master = Success(await projection.MasterAsync(
                columns: columns,
                phaseOne: false,
                integer: false,
                refinedResources: refined));
            LinearModelSnapshot<Flt64> actual = Success(await master.Model.State.RefreshAsync());
            foreach (LinearModelRow<Flt64> row in original.Rows) {
                LinearModelRow<Flt64> projected = actual.Rows.Single(candidate =>
                    string.Equals(candidate.SourceName, row.Id, StringComparison.Ordinal));
                foreach (ProjectedColumn<Flt64> column in columns) {
                    Flt64 expected = decisions
                        .Where(decision =>
                            string.Equals(decision.BlockId, column.BlockId, StringComparison.Ordinal) &&
                            (refined is null || !refined.Contains(decision.Variable)))
                        .Aggregate(Flt64.Zero, (sum, decision) =>
                            sum.Plus((row.Coefficients.TryGetValue(decision.Variable, out Flt64 coefficient)
                                    ? coefficient
                                    : Flt64.Zero)
                                .Times(column.Values[decision.Id])));
                    Flt64 actualCoefficient = projected.Coefficients.TryGetValue(
                        master.ColumnVariables[column.Id],
                        out Flt64 coefficientValue)
                        ? coefficientValue
                        : Flt64.Zero;
                    actualCoefficient.Should().Be(expected);
                }
            }
        }

        projection.ColumnsChanged();
        ColumnPricingRequest<Flt64> before = Success(await projection.PricingRequestAsync(
            blockId: "b0",
            phaseOne: false,
            seed: true));
        var localVariables = decisions
            .Where(decision => string.Equals(decision.BlockId, "b0", StringComparison.Ordinal))
            .Select(decision => decision.Variable)
            .ToHashSet();
        string[] expectedLocal = original.Rows
            .Where(row => row.Coefficients.Keys.All(localVariables.Contains))
            .Select(row => row.Id)
            .ToArray();
        using (LinearMetaModel<Flt64> pricing = Success(await projection.PricingModelAsync(before))) {
            LinearModelSnapshot<Flt64> local = Success(await pricing.State.RefreshAsync());
            local.Rows.Select(row => row.SourceName).Should().Equal(expectedLocal);
        }

        source.State.RegisterRow(
            new ConstraintId("late-local"),
            new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, variables[0]) },
                    Flt64.Zero),
                new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(),
                    Flt64.One),
                Comparison.LE)).IsOk.Should().BeTrue();
        (await projection.PricingModelAsync(before)).IsOk.Should().BeFalse();

        ColumnPricingRequest<Flt64> after = Success(await projection.PricingRequestAsync(
            blockId: "b0",
            phaseOne: false,
            seed: true));
        using (LinearMetaModel<Flt64> pricing = Success(await projection.PricingModelAsync(after))) {
            LinearModelSnapshot<Flt64> local = Success(await pricing.State.RefreshAsync());
            local.Rows.Should().Contain(row =>
                string.Equals(row.SourceName, "late-local", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task PricingBatch_ShouldShareSnapshotAndInvalidateAfterSourceEdits() {
        using var source = new LinearMetaModel<Flt64>(name: "pricing_batch");
        var first = new BinVar("first");
        var second = new BinVar("second");
        source.Add(first).IsOk.Should().BeTrue();
        source.Add(second).IsOk.Should().BeTrue();
        source.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                new[] {
                    new LinearMonomial<Flt64>(Flt64.One, first),
                    new LinearMonomial<Flt64>(new Flt64(2), second),
                },
                Flt64.Zero),
            name: "objective",
            displayName: null).IsOk.Should().BeTrue();
        var projection = new ColumnProjection<Flt64>(
            source: source,
            decisions: new[] {
                new ColumnDecision("first", "b0", first),
                new ColumnDecision("second", "b1", second),
            },
            blockIds: new[] { "b0", "b1" });

        using ColumnPricingBatch<Flt64> batch = Success(await projection.PreparePricingBatchAsync(
            blockIds: new[] { "b1", "b0" },
            phaseOne: false,
            seed: true));
        batch.Entries.Select(entry => entry.Request.BlockId).Should().Equal("b1", "b0");
        batch.Entries.Should().OnlyContain(entry => entry.Request.Revision == batch.Revision);
        foreach (ColumnPricingEntry<Flt64> entry in batch.Entries) {
            LinearModelSnapshot<Flt64> local = Success(await entry.Model.State.RefreshAsync());
            local.Rows.Should().BeEmpty();
        }
        (await projection.RequireCurrentAsync(batch)).IsOk.Should().BeTrue();

        ColumnPricingRequest<Flt64> scalarRequest = Success(await projection.PricingRequestAsync(
            blockId: "b0",
            phaseOne: false,
            seed: true));
        using (LinearMetaModel<Flt64> scalarModel = Success(
                   await projection.PricingModelAsync(scalarRequest))) {
            var extension = new BinVar("extension");
            scalarModel.Add(extension).IsOk.Should().BeTrue();
            scalarModel.AddConstraint(
                relation: new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(
                        new[] { new LinearMonomial<Flt64>(Flt64.One, extension) },
                        Flt64.Zero),
                    new LinearPolynomial<Flt64>(
                        Array.Empty<LinearMonomial<Flt64>>(),
                        Flt64.One),
                    Comparison.LE),
                group: null,
                name: "extension_limit").IsOk.Should().BeTrue();
            LinearModelSnapshot<Flt64> extended = Success(await scalarModel.State.RefreshAsync());
            extended.Rows.Should().Contain(row =>
                string.Equals(row.SourceName, "extension_limit", StringComparison.Ordinal));
        }

        await Task.Yield();
        source.State.RegisterRow(
            new ConstraintId("external_after_pricing"),
            new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, first) },
                    Flt64.Zero),
                new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(),
                    Flt64.One),
                Comparison.LE)).IsOk.Should().BeTrue();
        (await projection.RequireCurrentAsync(batch)).IsOk.Should().BeFalse();
    }

    private static T Success<T>(Result<T, ErrorCode, Error<ErrorCode>> result) {
        result.IsOk.Should().BeTrue(result.ToString());
        return result.Value;
    }

    private sealed class CountingDictionary : IReadOnlyDictionary<IVariableItem, Flt64> {
        private readonly Dictionary<IVariableItem, Flt64> _values;

        public CountingDictionary(params object[] values) {
            _values = new Dictionary<IVariableItem, Flt64>();
            for (int index = 0; index < values.Length; index += 2) {
                _values[(IVariableItem)values[index]] = (Flt64)values[index + 1];
            }
        }

        public int Reads { get; private set; }
        public int Count => _values.Count;
        public Flt64 this[IVariableItem key] {
            get => _values[key];
            set => _values[key] = value;
        }
        public IEnumerable<IVariableItem> Keys {
            get {
                ++Reads;
                return _values.Keys;
            }
        }
        public IEnumerable<Flt64> Values {
            get {
                ++Reads;
                return _values.Values;
            }
        }

        public void Clear() => _values.Clear();
        public bool ContainsKey(IVariableItem key) => _values.ContainsKey(key);
        public bool TryGetValue(IVariableItem key, out Flt64 value) =>
            _values.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<IVariableItem, Flt64>> GetEnumerator() {
            ++Reads;
            return _values.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
