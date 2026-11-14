#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

public class MatrixIdentityPropagationTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinearDumpPreservesRegisteredIdentitiesAndFixedColumns(bool fixFirst) {
        var registry = new ModelElementIdentityRegistry(@namespace: "identity-test", schemaVersion: "1.2");
        var x = new RealVar("identity-x");
        var y = new RealVar("identity-y");
        using LinearMetaModel<Flt64> parent = CreateLinear(
            registry: registry,
            x: x,
            y: y
        );
        RegisterIdentities(
            registry: registry,
            x: x,
            y: y,
            row: parent.Constraints[0],
            objective: parent.MetaSubObjects[0]
        );
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Dictionary<IVariableItem, Flt64>? fixedVariables = fixFirst ? new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One } : null;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel(fixedVariables).Value;

        Assert.Equal(fixFirst ? new[] { "variable:y" } : new[] { "variable:x", "variable:y" },
            matrix.Variables.Select(variable => variable.Id?.Value));
        Assert.Equal("constraint:row", Assert.Single(matrix.Constraints.Ids).Value);
        Assert.Equal(ModelElementScope.Stable, matrix.Constraints.IdentityScopeAt(0));
        Assert.Equal(new[] { new ModelElementOrigin("row", "first") }, matrix.Constraints.IdentityProvenanceAt(0));
        Assert.Equal("objective:one", matrix.Objective.Id?.Value);
        Assert.Equal("identity-test", matrix.Constraints.IdentityNamespace);
        Assert.Equal("1.2", matrix.Objective.IdentitySchemaVersion);
        Assert.True(matrix.ValidateIdentity().IsOk);

        using LinearTriadModel relaxed = matrix.RelaxToContinuous().Value;
        Assert.Equal(matrix.Variables.Select(variable => variable.Identity), relaxed.Variables.Select(variable => variable.Identity));
        Assert.Equal(matrix.Constraints.Identities, relaxed.Constraints.Identities);
        Assert.Equal(matrix.Objective.Identity, relaxed.Objective.Identity);
        Assert.Equal(matrix.Variables[0].Identity, matrix.Variables[0].Copy().Identity);
        Assert.True(mechanism.ValidateSolverTokenSnapshot(matrix, relaxed).IsOk);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuadraticDumpPreservesRegisteredIdentitiesAndFixedColumns(bool fixFirst) {
        var registry = new ModelElementIdentityRegistry(@namespace: "identity-test", schemaVersion: "1.2");
        var x = new RealVar("identity-x");
        var y = new RealVar("identity-y");
        using QuadraticMetaModel<Flt64> parent = CreateQuadratic(
            registry: registry,
            x: x,
            y: y
        );
        RegisterIdentities(
            registry: registry,
            x: x,
            y: y,
            row: parent.Constraints[0],
            objective: parent.FlattenSubObjects[0]
        );
        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Dictionary<IVariableItem, Flt64>? fixedVariables = fixFirst ? new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One } : null;
        using QuadraticTetradModel matrix = mechanism.ToQuadraticTetradModel(fixedVariables).Value;

        Assert.Equal(fixFirst ? new[] { "variable:y" } : new[] { "variable:x", "variable:y" },
            matrix.Variables.Select(variable => variable.Id?.Value));
        Assert.Equal("constraint:row", Assert.Single(matrix.Constraints.Ids).Value);
        Assert.Equal(ModelElementScope.Stable, matrix.Constraints.IdentityScopeAt(0));
        Assert.Equal(new[] { new ModelElementOrigin("row", "first") }, matrix.Constraints.IdentityProvenanceAt(0));
        Assert.Equal("objective:one", matrix.Objective.Id?.Value);
        Assert.Equal("identity-test", matrix.Constraints.IdentityNamespace);
        Assert.Equal("1.2", matrix.Objective.IdentitySchemaVersion);
        Assert.True(matrix.ValidateIdentity().IsOk);

        using ModelConstraint<QuadraticConstraintCell> copied = matrix.Constraints.Copy();
        Assert.Equal(matrix.Constraints.Identities, copied.Identities);
        Assert.Equal(matrix.Objective.Identity, matrix.Objective.Copy().Identity);
        Assert.True(mechanism.ValidateSolverTokenSnapshot(matrix).IsOk);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PromotingAnIdentityInvalidatesAnExistingMatrix(bool quadratic) {
        var registry = new ModelElementIdentityRegistry();
        var x = new RealVar("promotion-x");
        var y = new RealVar("promotion-y");
        if (quadratic) {
            using QuadraticMetaModel<Flt64> parent = CreateQuadratic(
                registry: registry,
                x: x,
                y: y
            );
            using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            using QuadraticTetradModel matrix = mechanism.ToQuadraticTetradModel().Value;
            Assert.Equal(ModelElementScope.ModelLocal, matrix.Variables[0].IdentityScope);
            Assert.True(registry.RegisterVariable(
                element: x,
                id: new VariableId("variable:promoted"),
                origin: new ModelElementOrigin("variable", "promoted")
            ).IsOk);
            Assert.True(mechanism.ValidateSolverTokenSnapshot(matrix).IsFailed);
            using QuadraticTetradModel fresh = mechanism.ToQuadraticTetradModel().Value;
            Assert.Equal("variable:promoted", fresh.Variables[0].Id?.Value);
            Assert.True(mechanism.ValidateSolverTokenSnapshot(fresh).IsOk);
        }
        else {
            using LinearMetaModel<Flt64> parent = CreateLinear(
                registry: registry,
                x: x,
                y: y
            );
            using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;
            Assert.Equal(ModelElementScope.ModelLocal, matrix.Variables[0].IdentityScope);
            Assert.True(registry.RegisterVariable(
                element: x,
                id: new VariableId("variable:promoted"),
                origin: new ModelElementOrigin("variable", "promoted")
            ).IsOk);
            Assert.True(mechanism.ValidateSolverTokenSnapshot(matrix).IsFailed);
            using LinearTriadModel fresh = mechanism.ToLinearTriadModel().Value;
            Assert.Equal("variable:promoted", fresh.Variables[0].Id?.Value);
            Assert.True(mechanism.ValidateSolverTokenSnapshot(fresh).IsOk);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AggregateObjectiveIsStableOnlyWithCompleteSources(bool quadratic, bool complete) {
        var registry = new ModelElementIdentityRegistry();
        var x = new RealVar("aggregate-x");
        var y = new RealVar("aggregate-y");
        if (quadratic) {
            using QuadraticMetaModel<Flt64> parent = CreateQuadratic(
                registry: registry,
                x: x,
                y: y
            );
            Assert.True(parent.AddObject(
                category: ObjectCategory.Minimum,
                flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>([], Flt64.Zero),
                name: "second",
                displayName: null
            ).IsOk);
            RegisterAggregate(
                registry: registry,
                first: parent.FlattenSubObjects[0],
                second: parent.FlattenSubObjects[1],
                complete: complete
            );
            using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            using QuadraticTetradModel matrix = mechanism.ToQuadraticTetradModel().Value;
            AssertAggregate(matrix.Objective.Identity, complete);
            Assert.True(matrix.ValidateIdentity().IsOk);
        }
        else {
            using LinearMetaModel<Flt64> parent = CreateLinear(
                registry: registry,
                x: x,
                y: y
            );
            Assert.True(parent.AddObject(
                category: ObjectCategory.Minimum,
                flattenData: new SymbolFlatten.LinearFlattenData<Flt64>([], Flt64.Zero),
                name: "second",
                displayName: null
            ).IsOk);
            RegisterAggregate(
                registry: registry,
                first: parent.MetaSubObjects[0],
                second: parent.MetaSubObjects[1],
                complete: complete
            );
            using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;
            AssertAggregate(matrix.Objective.Identity, complete);
            Assert.True(matrix.ValidateIdentity().IsOk);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDumpRollsBackFallbackIdentities(bool quadratic) {
        var registry = new ModelElementIdentityRegistry();
        var x = new RealVar("invalid-identity-x");
        var y = new RealVar("invalid-identity-y");
        Assert.True(registry.RegisterVariable(x, new VariableId("variable:no-origin")).IsOk);
        ModelElementIdentity[] before = registry.Entries().ToArray();
        if (quadratic) {
            using QuadraticMetaModel<Flt64> parent = CreateQuadratic(
                registry: registry,
                x: x,
                y: y
            );
            using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            Assert.True(mechanism.ToQuadraticTetradModel().IsFailed);
        }
        else {
            using LinearMetaModel<Flt64> parent = CreateLinear(
                registry: registry,
                x: x,
                y: y
            );
            using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            Assert.True(mechanism.ToLinearTriadModel().IsFailed);
        }
        Assert.Equal(before, registry.Entries());
        Assert.Equal("model-local-variable:1", registry.VariableId(y, 1).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingRegistryLeavesElementsAnonymous(bool quadratic) {
        var x = new RealVar("anonymous-x");
        var y = new RealVar("anonymous-y");
        if (quadratic) {
            using QuadraticMetaModel<Flt64> parent = CreateQuadratic(
                registry: null,
                x: x,
                y: y
            );
            using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            using QuadraticTetradModel matrix = mechanism.ToQuadraticTetradModel().Value;
            Assert.All(matrix.Variables, variable => Assert.Null(variable.Identity));
            Assert.Empty(matrix.Constraints.Identities);
            Assert.Null(matrix.Objective.Identity);
            Assert.True(matrix.ValidateIdentity().IsOk);
        }
        else {
            using LinearMetaModel<Flt64> parent = CreateLinear(
                registry: null,
                x: x,
                y: y
            );
            using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;
            Assert.All(matrix.Variables, variable => Assert.Null(variable.Identity));
            Assert.Empty(matrix.Constraints.Identities);
            Assert.Null(matrix.Objective.Identity);
            Assert.True(matrix.ValidateIdentity().IsOk);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateRowOriginFailsWithoutRetainingNewFallbacks(bool quadratic) {
        var registry = new ModelElementIdentityRegistry();
        var x = new RealVar("duplicate-x");
        var y = new RealVar("duplicate-y");
        if (quadratic) {
            using QuadraticMetaModel<Flt64> parent = CreateQuadratic(
                registry: registry,
                x: x,
                y: y
            );
            using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            var rows = (List<QuadraticConstraintImpl<Flt64>>)mechanism.QuadraticConstraints;
            rows.Add(rows[0]);
            Assert.True(mechanism.ToQuadraticTetradModel().IsFailed);
        }
        else {
            using LinearMetaModel<Flt64> parent = CreateLinear(
                registry: registry,
                x: x,
                y: y
            );
            using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            var rows = (List<LinearConstraintImpl<Flt64>>)mechanism.LinearConstraints;
            rows.Add(rows[0]);
            Assert.True(mechanism.ToLinearTriadModel().IsFailed);
        }
        Assert.Empty(registry.Entries());
        Assert.Equal("model-local-variable:0", registry.VariableId(x, 0).Value);
    }

    [Fact]
    public async Task IdempotentRegistrationAndFallbackLookupsDoNotInvalidateButNewBindingsDo() {
        var registry = new ModelElementIdentityRegistry();
        var x = new RealVar("revision-x");
        var y = new RealVar("revision-y");
        var origin = new ModelElementOrigin("variable", "x");
        Assert.True(registry.RegisterVariable(
            element: x,
            id: new VariableId("variable:x"),
            origin: origin
        ).IsOk);
        using LinearMetaModel<Flt64> parent = CreateLinear(
            registry: registry,
            x: x,
            y: y
        );
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;
        Assert.True(registry.RegisterVariable(
            element: x,
            id: new VariableId("variable:x"),
            origin: origin
        ).IsOk);
        _ = registry.VariableId(new object(), 10);
        Assert.True(mechanism.ValidateSolverTokenSnapshot(matrix).IsOk);
        Assert.True(registry.RegisterVariable(
            element: new object(),
            id: new VariableId("variable:unrelated"),
            origin: new ModelElementOrigin("variable", "unrelated")
        ).IsOk);
        Assert.True(mechanism.ValidateSolverTokenSnapshot(matrix).IsFailed);
    }

    private static void RegisterAggregate(ModelElementIdentityRegistry registry, object first, object second, bool complete) {
        Assert.True(registry.RegisterObjective(
            element: first,
            id: new ObjectiveId("objective:a"),
            origin: new ModelElementOrigin("source", "a")
        ).IsOk);
        Assert.True(registry.RegisterObjective(
            element: second,
            id: new ObjectiveId("objective:b"),
            origin: complete ? new ModelElementOrigin("source", "b") : null
        ).IsOk);
    }

    private static void AssertAggregate(ModelElementIdentity? identity, bool complete) {
        Assert.NotNull(identity);
        Assert.Equal(complete ? ModelElementScope.Stable : ModelElementScope.ModelLocal, identity.Scope);
        Assert.Equal(complete
            ? "artifact:aggregate-objective:objective:60h6167677265676174653a3131683666363236613635363337343639373636353361363131316836663632366136353633373436393736363533613632:1h30"
            : "model-local-objective:0", identity.Id.Value);
        Assert.Equal(complete
            ? new[] { new ModelElementOrigin("source", "a"), new ModelElementOrigin("source", "b") }
            : Array.Empty<ModelElementOrigin>(), identity.Provenance);
    }

    private static LinearMetaModel<Flt64> CreateLinear(ModelElementIdentityRegistry? registry, RealVar x, RealVar y) {
        var parent = new LinearMetaModel<Flt64>(identityRegistry: registry);
        Assert.True(parent.Add(new IVariableItem[] { x, y }).IsOk);
        var row = new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(3)),
            Comparison: Comparison.LE
        );
        Assert.True(parent.AddConstraint(relation: row, group: null, name: "row").IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, y) }, Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);
        return parent;
    }

    private static QuadraticMetaModel<Flt64> CreateQuadratic(ModelElementIdentityRegistry? registry, RealVar x, RealVar y) {
        var parent = new QuadraticMetaModel<Flt64>(identityRegistry: registry);
        Assert.True(parent.Add(new IVariableItem[] { x, y }).IsOk);
        var row = new QuadraticInequalityOf<Flt64>(
            Lhs: new QuadraticPolynomial<Flt64>(new[] { new QuadraticMonomial<Flt64>(
                Coefficient: Flt64.One,
                Symbol1: x,
                Symbol2: null
            ) }, Flt64.Zero),
            Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), new Flt64(3)),
            Comparison: Comparison.LE
        );
        Assert.True(parent.AddConstraint(relation: row, group: null, name: "row").IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(new[] { new QuadraticMonomial<Flt64>(
                Coefficient: Flt64.One,
                Symbol1: y,
                Symbol2: null
            ) }, Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);
        return parent;
    }

    private static void RegisterIdentities(
        ModelElementIdentityRegistry registry,
        RealVar x,
        RealVar y,
        object row,
        object objective) {
        Assert.True(registry.RegisterVariable(
            element: x,
            id: new VariableId("variable:x"),
            origin: new ModelElementOrigin("variable", "x")
        ).IsOk);
        Assert.True(registry.RegisterVariable(
            element: y,
            id: new VariableId("variable:y"),
            origin: new ModelElementOrigin("variable", "y")
        ).IsOk);
        Assert.True(registry.RegisterConstraint(
            element: row,
            id: new ConstraintId("constraint:row"),
            origin: new ModelElementOrigin("row", "first")
        ).IsOk);
        Assert.True(registry.RegisterObjective(
            element: objective,
            id: new ObjectiveId("objective:one"),
            origin: new ModelElementOrigin("objective", "one")
        ).IsOk);
    }
}
