#nullable enable

using System;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

public class MatrixIdentityValidationTest {
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(9, false)]
    [InlineData(10, false)]
    [InlineData(11, false)]
    [InlineData(12, false)]
    [InlineData(13, false)]
    [InlineData(14, false)]
    [InlineData(15, false)]
    [InlineData(16, false)]
    public void ValidationRejectsMalformedOrConflictingIdentities(int change, bool valid) {
        var origin = new ModelElementOrigin("source", "x");
        var variable = new ModelElementIdentity(
            Kind: ModelElementKind.Variable,
            Id: new ModelElementId("variable:x"),
            Scope: ModelElementScope.Stable,
            Origin: origin
        );
        var row = new ModelElementIdentity(
            Kind: ModelElementKind.Constraint,
            Id: new ModelElementId("constraint:row"),
            Scope: ModelElementScope.Stable,
            Origin: new ModelElementOrigin("source", "row")
        );
        var objective = new ModelElementIdentity(
            Kind: ModelElementKind.Objective,
            Id: new ModelElementId("objective:one"),
            Scope: ModelElementScope.Stable,
            Origin: new ModelElementOrigin("source", "objective")
        );
        variable = change switch {
            1 => variable with { Kind = ModelElementKind.Constraint },
            2 => variable with { Id = new ModelElementId("") },
            3 => variable with { Scope = (ModelElementScope)42 },
            4 => variable with { Origin = null, Provenance = Array.Empty<ModelElementOrigin>() },
            5 => variable with { Scope = ModelElementScope.ModelLocal },
            6 => variable with { Id = new ModelElementId("model-local-variable:0") },
            7 => variable with { Id = new ModelElementId("artifact:invalid") },
            8 => variable with { Provenance = [new ModelElementOrigin("source", "different")] },
            9 => variable with { Origin = new ModelElementOrigin("", "bad"), Provenance = [new ModelElementOrigin("", "bad")] },
            16 => variable with { Id = new ModelElementId(new string((char)0xd800, 1)) },
            _ => variable,
        };
        if (change == 10) { row = row with { Id = variable.Id }; }
        if (change == 11) { objective = objective with { Id = row.Id }; }
        string? variableNamespace = change switch { 12 => null, 13 => "other", _ => "model" };
        string? objectiveSchema = change == 14 ? "other" : "1";
        ModelElementIdentity[] identities = change == 15 ? [row, row] : [row];
        using var matrix = new LinearTriadModel(
            variables: [new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Continuous.Instance,
                name: "x",
                identity: variable,
                identityNamespace: variableNamespace,
                identitySchemaVersion: "1"
            )],
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix([new SparseVector()]),
                signs: [ConstraintRelation.Equal],
                rhs: [Flt64.Zero],
                names: ["row"],
                sources: [ConstraintSource.Origin],
                identities: identities,
                identityNamespace: "model",
                identitySchemaVersion: "1"
            ),
            objective: new Objective<LinearObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: [],
                identity: objective,
                identityNamespace: "model",
                identitySchemaVersion: objectiveSchema
            ),
            name: "identity"
        );
        Assert.Equal(valid, matrix.ValidateIdentity().IsOk);
        using ModelConstraint<LinearConstraintCell> copy = matrix.Constraints.Copy();
        Assert.Equal(change != 15, copy.ValidateShape().IsOk);
    }

    [Fact]
    public void AnonymousAndValidGeneratedIdentitiesRemainAccepted() {
        ModelElementIdentity generated = DerivedModelElementIdentity.Create(
            kind: ModelElementKind.Objective,
            role: "aggregate-objective",
            sourceId: "source",
            sourceScope: ModelElementScope.Stable,
            sourceOrigin: new ModelElementOrigin("source", "one"),
            sourceProvenance: null,
            identityNamespace: "model",
            identitySchemaVersion: "1"
        );
        using var matrix = new QuadraticTetradModel(
            variables: [],
            constraints: new QuadraticConstraintBatch(
                lhs: new SparseQuadraticMatrix(),
                signs: [],
                rhs: [],
                names: [],
                sources: []
            ),
            objective: new Objective<QuadraticObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: [],
                identity: generated,
                identityNamespace: "model",
                identitySchemaVersion: "1"
            ),
            name: "identity"
        );
        Assert.True(matrix.ValidateIdentity().IsOk);
        matrix.Dispose();
        Assert.True(matrix.ValidateIdentity().IsFailed);
    }
}
