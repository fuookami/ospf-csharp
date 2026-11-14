#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Solver;

public class ColumnProjectionTest {
    private readonly BinVar _x = new("x");
    private readonly URealVar _theta = new("theta");

    [Fact]
    public async Task NewSourceRow_ShouldReachOldColumnsPricingFinalAndResourceCompletion() {
        using LinearMetaModel<Flt64> model = Source();
        ColumnProjection<Flt64> projection = Projection(model);
        using ColumnMasterRegistration<Flt64> first = Success(await projection.MasterAsync(
            columns: Columns.Take(1),
            phaseOne: false,
            integer: false));

        model.State.RegisterRow(new ConstraintId("cut"), Cut()).IsOk.Should().BeTrue();
        (await projection.RequireCurrentAsync(first)).IsOk.Should().BeFalse();

        using ColumnMasterRegistration<Flt64> master = Success(await projection.MasterAsync(
            columns: Columns,
            phaseOne: false,
            integer: false));
        LinearModelSnapshot<Flt64> snapshot = Success(await master.Model.State.RefreshAsync());
        LinearModelRow<Flt64> cut = snapshot.Rows.Single(row =>
            string.Equals(row.SourceName, "cut", StringComparison.Ordinal));
        cut.Coefficients[master.ColumnVariables["loaded"]].Should().Be(new Flt64(-2));
        cut.Coefficients[_theta].Should().Be(Flt64.One);
        cut.Rhs.Should().Be(Flt64.One);
        snapshot.ObjectiveConstant.Should().Be(new Flt64(5));

        IReadOnlyDictionary<MathConstraint, Flt64> dual = await DualMap(
            master.Model,
            constraint => string.Equals(constraint.Name, "cut", StringComparison.Ordinal)
                ? new Flt64(3)
                : Flt64.Zero);
        (await projection.RefreshShadowPriceAsync(master, dual)).IsOk.Should().BeTrue();
        ColumnPricingRequest<Flt64> request = Success(await projection.PricingRequestAsync(
            blockId: "block",
            phaseOne: false));
        request.Coefficients["x"].Should().Be(new Flt64(7));
        Success(request.ReducedCost(new Dictionary<string, Flt64> {
            ["x"] = Flt64.One,
        })).Should().Be(new Flt64(7));
        (await projection.PricingRequestAsync("block", true)).IsOk.Should().BeFalse();
        (await projection.RefreshShadowPriceAsync(
            master,
            new Dictionary<MathConstraint, Flt64>(ReferenceEqualityComparer.Instance))).IsOk.Should().BeFalse();

        foreach (bool refine in new[] { false, true }) {
            IReadOnlySet<IVariableItem>? refined = refine
                ? new HashSet<IVariableItem> { _x }
                : null;
            using ColumnMasterRegistration<Flt64> integer = Success(await projection.MasterAsync(
                columns: Columns,
                phaseOne: false,
                integer: true,
                refinedResources: refined));
            LinearModelSnapshot<Flt64> final = Success(await integer.Model.State.RefreshAsync());
            LinearModelRow<Flt64> finalCut = final.Rows.Single(row =>
                string.Equals(row.SourceName, "cut", StringComparison.Ordinal));
            IVariableItem representedBy = refine ? _x : integer.ColumnVariables["loaded"];
            finalCut.Coefficients[representedBy].Should().Be(new Flt64(-2));
            (await projection.RequireCurrentAsync(master)).IsOk.Should().BeFalse();
        }
    }

    [Fact]
    public async Task RebuildAndDirectEdits_ShouldInvalidateOldCertificatesAndForeignOwners() {
        using LinearMetaModel<Flt64> model = Source();
        ColumnProjection<Flt64> projection = Projection(model);
        using ColumnMasterRegistration<Flt64> first = Success(await projection.MasterAsync(
            columns: Columns,
            phaseOne: true,
            integer: false));
        ColumnPricingRequest<Flt64> seed = Success(await projection.PricingRequestAsync(
            blockId: "block",
            phaseOne: true,
            seed: true));

        projection.ColumnsChanged();
        (await projection.RequireCurrentAsync(seed.Revision)).IsOk.Should().BeFalse();
        (await projection.RequireCurrentAsync(first)).IsOk.Should().BeFalse();

        using ColumnMasterRegistration<Flt64> rebuilt = Success(await projection.MasterAsync(
            columns: Columns.Take(1),
            phaseOne: false,
            integer: false));
        rebuilt.ColumnVariables.Keys.Should().Equal("empty");
        using LinearMetaModel<Flt64> foreignSource = Source();
        ColumnProjection<Flt64> foreign = Projection(foreignSource);
        (await foreign.RequireCurrentAsync(rebuilt)).IsOk.Should().BeFalse();

        rebuilt.Model.AddConstraint(
            relation: new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, _theta) },
                    Flt64.Zero),
                new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(),
                    Flt64.One),
                Comparison.LE),
            group: null,
            name: "direct_edit").IsOk.Should().BeTrue();
        (await projection.RequireCurrentAsync(rebuilt)).IsOk.Should().BeFalse();
        (await projection.PricingRequestAsync("block", false)).IsOk.Should().BeFalse();
    }

    [Fact]
    public async Task GenericProjection_ShouldPreserveExtendedPrecisionAndNormalizeMaximum() {
        var x = new BinVar("extended-x");
        using var model = new LinearMetaModel<FltX>(
            name: "extended",
            objectCategory: ObjectCategory.Maximum,
            converter: FltX.One);
        model.Add(x).IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Maximum,
            polynomial: new LinearPolynomial<FltX>(
                new[] { new LinearMonomial<FltX>(new FltX(3), x) },
                new FltX(7)),
            name: "extended_objective",
            displayName: null).IsOk.Should().BeTrue();
        var projection = new ColumnProjection<FltX>(
            source: model,
            decisions: new[] { new ColumnDecision("x", "block", x) },
            blockIds: new[] { "block" });
        ProjectedColumn<FltX>[] columns = {
            new ProjectedColumn<FltX>(
                id: "loaded",
                blockId: "block",
                values: new Dictionary<string, FltX> { ["x"] = FltX.One }),
        };

        using ColumnMasterRegistration<FltX> master = Success(await projection.MasterAsync(
            columns: columns,
            phaseOne: false,
            integer: true));
        LinearModelSnapshot<FltX> snapshot = Success(await master.Model.State.RefreshAsync());
        snapshot.ObjectiveConstant.Should().Be(new FltX(-7));
        snapshot.Objective[master.ColumnVariables["loaded"]].Should().Be(new FltX(-3));

        ColumnPricingRequest<FltX> request = Success(await projection.PricingRequestAsync(
            blockId: "block",
            phaseOne: false,
            seed: true));
        request.Coefficients["x"].Should().Be(new FltX(-3));
    }

    [Fact]
    public async Task LateLocalRowsAndTightenedBounds_ShouldRequireRemovingInvalidColumns() {
        using LinearMetaModel<Flt64> model = Source();
        ColumnProjection<Flt64> projection = Projection(model);
        model.State.RegisterRow(
            new ConstraintId("local-limit"),
            new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, _x) },
                    Flt64.Zero),
                new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(),
                    Flt64.Zero),
                Comparison.LE)).IsOk.Should().BeTrue();

        (await projection.MasterAsync(Columns, false, false)).IsOk.Should().BeFalse();
        using ColumnMasterRegistration<Flt64> accepted = Success(await projection.MasterAsync(
            columns: Columns.Take(1),
            phaseOne: false,
            integer: false));
        accepted.ColumnVariables.Keys.Should().Equal("empty");

        using LinearMetaModel<Flt64> otherSource = Source();
        ColumnProjection<Flt64> other = Projection(otherSource);
        _x.Range.Leq(false).Should().BeTrue();
        (await other.MasterAsync(Columns, false, true)).IsOk.Should().BeFalse();
    }

    [Fact]
    public async Task DualsFromIdenticallyNamedRebuiltMaster_ShouldNotBeReused() {
        using LinearMetaModel<Flt64> model = Source();
        ColumnProjection<Flt64> projection = Projection(model);
        using ColumnMasterRegistration<Flt64> first = Success(await projection.MasterAsync(
            columns: Columns,
            phaseOne: false,
            integer: false));
        IReadOnlyDictionary<MathConstraint, Flt64> dual = await DualMap(
            first.Model,
            _ => Flt64.Zero);
        using ColumnMasterRegistration<Flt64> second = Success(await projection.MasterAsync(
            columns: Columns,
            phaseOne: false,
            integer: false));

        (await projection.RefreshShadowPriceAsync(first, dual)).IsOk.Should().BeFalse();
        (await projection.RefreshShadowPriceAsync(second, dual)).IsOk.Should().BeFalse();
        (await projection.PricingRequestAsync("block", false)).IsOk.Should().BeFalse();
    }

    [Fact]
    public async Task ColumnPool_ShouldPublishAdditionsAndRemovalsAtomically() {
        using LinearMetaModel<Flt64> model = Source();
        ColumnProjection<Flt64> projection = Projection(model);
        (await projection.ReplaceColumnsAsync(Columns.Take(1))).IsOk.Should().BeTrue();
        projection.Columns.Select(column => column.Id).Should().Equal("empty");

        using ColumnMasterRegistration<Flt64> first = Success(await projection.MasterAsync(
            phaseOne: false,
            integer: false));
        ColumnPricingRequest<Flt64> seed = Success(await projection.PricingRequestAsync(
            blockId: "block",
            phaseOne: false,
            seed: true));
        using ColumnPricingBatch<Flt64> batch = Success(await projection.PreparePricingBatchAsync(
            blockIds: new[] { "block" },
            phaseOne: false,
            seed: true));

        (await projection.AddColumnsAsync(Columns.Skip(1))).IsOk.Should().BeTrue();

        projection.Columns.Select(column => column.Id).Should().Equal("empty", "loaded");
        (await projection.RequireCurrentAsync(first)).IsOk.Should().BeFalse();
        (await projection.RequireCurrentAsync(seed.Revision)).IsOk.Should().BeFalse();
        (await projection.RequireCurrentAsync(batch)).IsOk.Should().BeFalse();
        using ColumnMasterRegistration<Flt64> second = Success(await projection.MasterAsync(
            phaseOne: false,
            integer: false));
        second.ColumnVariables.Keys.Should().Equal("empty", "loaded");

        var duplicate = new ProjectedColumn<Flt64>(
            id: "loaded",
            blockId: "block",
            values: new Dictionary<string, Flt64> { ["x"] = Flt64.Zero });
        (await projection.AddColumnsAsync(new[] { duplicate })).IsOk.Should().BeFalse();
        (await projection.RequireCurrentAsync(second)).IsOk.Should().BeTrue();

        (await projection.RemoveColumnsAsync(new[] { "empty", "missing" })).IsOk.Should().BeTrue();

        projection.Columns.Select(column => column.Id).Should().Equal("loaded");
        (await projection.RequireCurrentAsync(second)).IsOk.Should().BeFalse();
        using ColumnMasterRegistration<Flt64> third = Success(await projection.MasterAsync(
            phaseOne: false,
            integer: false));
        third.ColumnVariables.Keys.Should().Equal("loaded");
    }

    [Fact]
    public async Task InvalidCandidateMaster_ShouldPreservePreviousRegistrationAndPool() {
        using LinearMetaModel<Flt64> model = Source();
        ColumnProjection<Flt64> projection = Projection(model);
        (await projection.ReplaceColumnsAsync(Columns.Take(1))).IsOk.Should().BeTrue();
        using ColumnMasterRegistration<Flt64> active = Success(await projection.MasterAsync(
            phaseOne: false,
            integer: false));
        var invalid = new ProjectedColumn<Flt64>(
            id: "invalid",
            blockId: "unknown-block",
            values: new Dictionary<string, Flt64> { ["x"] = Flt64.One });

        (await projection.MasterAsync(
            columns: new[] { invalid },
            phaseOne: false,
            integer: false)).IsOk.Should().BeFalse();
        (await projection.ReplaceColumnsAsync(new[] { invalid })).IsOk.Should().BeFalse();

        projection.Columns.Select(column => column.Id).Should().Equal("empty");
        (await projection.RequireCurrentAsync(active)).IsOk.Should().BeTrue();
    }

    [Fact]
    public async Task DynamicRowTransaction_ShouldInvalidateAndRestoreProjectionCertificates() {
        using LinearMetaModel<Flt64> model = Source();
        ColumnProjection<Flt64> projection = Projection(model);
        (await projection.ReplaceColumnsAsync(Columns.Take(1))).IsOk.Should().BeTrue();
        using ColumnMasterRegistration<Flt64> active = Success(await projection.MasterAsync(
            phaseOne: false,
            integer: false));
        ColumnPricingRequest<Flt64> seed = Success(await projection.PricingRequestAsync(
            blockId: "block",
            phaseOne: false,
            seed: true));

        IMetaModelWriteTransaction rollback = model.BeginWrite().Value;
        model.State.RegisterRow(new ConstraintId("temporary-cut"), Cut()).IsOk.Should().BeTrue();
        (await projection.RequireCurrentAsync(active)).IsOk.Should().BeFalse();
        (await projection.RequireCurrentAsync(seed.Revision)).IsOk.Should().BeFalse();
        rollback.Rollback().IsOk.Should().BeTrue();
        (await projection.RequireCurrentAsync(active)).IsOk.Should().BeTrue();

        IMetaModelWriteTransaction commit = model.BeginWrite().Value;
        model.State.RegisterRow(new ConstraintId("committed-cut"), Cut()).IsOk.Should().BeTrue();
        commit.Commit().IsOk.Should().BeTrue();

        (await projection.RequireCurrentAsync(active)).IsOk.Should().BeFalse();
        (await projection.RequireCurrentAsync(seed.Revision)).IsOk.Should().BeFalse();
        using ColumnMasterRegistration<Flt64> rebuilt = Success(await projection.MasterAsync(
            phaseOne: false,
            integer: false));
        LinearModelSnapshot<Flt64> snapshot = Success(await rebuilt.Model.State.RefreshAsync());
        snapshot.Rows.Should().Contain(row =>
            string.Equals(row.SourceName, "committed-cut", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemovedColumnVariable_ShouldRejectOldPricingAndRestoreOnRollback() {
        using LinearMetaModel<Flt64> model = Source();
        ColumnProjection<Flt64> projection = Projection(model);
        (await projection.ReplaceColumnsAsync(Columns.Take(1))).IsOk.Should().BeTrue();
        using ColumnMasterRegistration<Flt64> active = Success(await projection.MasterAsync(
            phaseOne: false,
            integer: false));
        ColumnPricingRequest<Flt64> seed = Success(await projection.PricingRequestAsync(
            blockId: "block",
            phaseOne: false,
            seed: true));

        IMetaModelWriteTransaction rollback = model.BeginWrite().Value;
        model.RemoveObjectiveTerms(new HashSet<IVariableItem> { _x }).IsOk.Should().BeTrue();
        model.Remove(_x);
        (await projection.RequireCurrentAsync(active)).IsOk.Should().BeFalse();
        (await projection.PricingModelAsync(seed)).IsOk.Should().BeFalse();
        rollback.Rollback().IsOk.Should().BeTrue();
        (await projection.RequireCurrentAsync(active)).IsOk.Should().BeTrue();

        IMetaModelWriteTransaction commit = model.BeginWrite().Value;
        model.RemoveObjectiveTerms(new HashSet<IVariableItem> { _x }).IsOk.Should().BeTrue();
        model.Remove(_x);
        commit.Commit().IsOk.Should().BeTrue();

        (await projection.RequireCurrentAsync(active)).IsOk.Should().BeFalse();
        (await projection.PricingModelAsync(seed)).IsOk.Should().BeFalse();
    }

    [Fact]
    public async Task BrokenColumnEnumerable_ShouldFailWithoutTouchingPoolOrRegistration() {
        using LinearMetaModel<Flt64> model = Source();
        ColumnProjection<Flt64> projection = Projection(model);
        (await projection.ReplaceColumnsAsync(Columns.Take(1))).IsOk.Should().BeTrue();
        using ColumnMasterRegistration<Flt64> active = Success(await projection.MasterAsync(
            phaseOne: false,
            integer: false));
        IEnumerable<ProjectedColumn<Flt64>> broken = Broken(() => Columns.Skip(1));

        (await projection.ReplaceColumnsAsync(broken)).IsFailed.Should().BeTrue();
        (await projection.AddColumnsAsync(broken)).IsFailed.Should().BeTrue();
        (await projection.MasterAsync(
            columns: broken,
            phaseOne: false,
            integer: false)).IsOk.Should().BeFalse();

        projection.Columns.Select(column => column.Id).Should().Equal("empty");
        (await projection.RequireCurrentAsync(active)).IsOk.Should().BeTrue();
    }

    private static IEnumerable<ProjectedColumn<Flt64>> Broken(
        Func<IEnumerable<ProjectedColumn<Flt64>>> source) {
        foreach (ProjectedColumn<Flt64> column in source()) {
            yield return column;
        }
        throw new InvalidOperationException("broken enumeration");
    }

    private LinearMetaModel<Flt64> Source() {
        var model = new LinearMetaModel<Flt64>(name: "source");
        model.Add(_x).IsOk.Should().BeTrue();
        model.Add(_theta).IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                new[] {
                    new LinearMonomial<Flt64>(Flt64.One, _x),
                    new LinearMonomial<Flt64>(Flt64.One, _theta),
                },
                new Flt64(5)),
            name: "source_objective",
            displayName: null).IsOk.Should().BeTrue();
        return model;
    }

    private ColumnProjection<Flt64> Projection(LinearMetaModel<Flt64> model) =>
        new(
            source: model,
            decisions: new[] { new ColumnDecision("x", "block", _x) },
            blockIds: new[] { "block" });

    private IReadOnlyList<ProjectedColumn<Flt64>> Columns => new[] {
        new ProjectedColumn<Flt64>(
            id: "empty",
            blockId: "block",
            values: new Dictionary<string, Flt64> { ["x"] = Flt64.Zero }),
        new ProjectedColumn<Flt64>(
            id: "loaded",
            blockId: "block",
            values: new Dictionary<string, Flt64> { ["x"] = Flt64.One }),
    };

    private LinearInequality<Flt64> Cut() =>
        new(
            Lhs: new LinearPolynomial<Flt64>(
                new[] {
                    new LinearMonomial<Flt64>(Flt64.One, _theta),
                    new LinearMonomial<Flt64>(new Flt64(-2), _x),
                },
                Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(),
                Flt64.One),
            Comparison: Comparison.GE);

    private static async Task<IReadOnlyDictionary<MathConstraint, Flt64>> DualMap(
        LinearMetaModel<Flt64> master,
        Func<IConstraint<Flt64, LinearCategory>, Flt64> value) {
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> expandedResult =
            await LinearMechanismModel<Flt64>.InvokeAsync(master);
        expandedResult.IsOk.Should().BeTrue(expandedResult.ToString());
        using LinearMechanismModel<Flt64> expanded = expandedResult.Value;
        var dual = new Dictionary<MathConstraint, Flt64>(ReferenceEqualityComparer.Instance);
        foreach (IConstraint<Flt64, LinearCategory> constraint in
                 ((IMechanismModel<Flt64>)expanded).Constraints) {
            constraint.Origin.Should().NotBeNull();
            dual[constraint.Origin!] = value(constraint);
        }
        return dual;
    }

    private static T Success<T>(Result<T, ErrorCode, Error<ErrorCode>> result) {
        result.IsOk.Should().BeTrue(result.ToString());
        return result.Value;
    }
}
