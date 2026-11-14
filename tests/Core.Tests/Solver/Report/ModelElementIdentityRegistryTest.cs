#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Core.Solver.Report;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Report;

public class ModelElementIdentityRegistryTest {
    [Theory]
    [InlineData(0, "1h3f")]
    [InlineData(1, "1h3f")]
    [InlineData(2, "4hf09f9880")]
    public void DerivedIdentityUsesJvmUtf8Replacement(int input, string encoded) {
        string sourceId = input switch {
            0 => new string((char)0xd800, 1),
            1 => new string((char)0xdc00, 1),
            _ => "\ud83d\ude00",
        };
        ModelElementIdentity identity = DerivedModelElementIdentity.Create(
            kind: ModelElementKind.Objective,
            role: "probe",
            sourceId: sourceId,
            sourceScope: ModelElementScope.Stable,
            sourceOrigin: new ModelElementOrigin("source", "probe"),
            sourceProvenance: null,
            identityNamespace: "model",
            identitySchemaVersion: "1"
        );
        Assert.Equal($"{(input == 2 ? "artifact" : "model-local-derived")}:probe:objective:{encoded}:1h30", identity.Id.Value);
        Assert.Equal(input == 2 ? ModelElementScope.Stable : ModelElementScope.ModelLocal, identity.Scope);
    }

    [Fact]
    public void MalformedStableIdsAreRejectedWithoutReplacingFallbacks() {
        var registry = new ModelElementIdentityRegistry();
        object element = new();
        VariableId fallback = registry.VariableId(element, 0);
        string malformed = new((char)0xd800, 1);
        Assert.True(registry.RegisterVariable(element, new VariableId(malformed)).IsFailed);
        Assert.Equal(fallback, registry.VariableId(element, 0));
        Assert.Null(registry.Identity(element));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void IdentityResolutionRejectsReentrantBindingsWithoutCorruptingRegistry(int failure) {
        var registry = new ModelElementIdentityRegistry();
        object existing = new();
        object fallback = new();
        object added = new();
        var origin = new ModelElementOrigin("variable", "existing");
        var existingId = new VariableId("variable:existing");
        Assert.True(registry.RegisterVariable(existing, existingId, origin).IsOk);
        VariableId fallbackId = registry.VariableId(fallback, 0);
        bool? unchanged = null;
        bool? promoted = null;
        bool? registered = null;

        Result<int, ErrorCode, Error<ErrorCode>> result = registry.ResolveAtomically(() => {
            unchanged = registry.RegisterVariable(existing, existingId, origin).IsOk;
            promoted = registry.RegisterVariable(fallback, new VariableId("variable:promoted")).IsOk;
            Result<int, ErrorCode, Error<ErrorCode>> nested = registry.ResolveAtomically(() => {
                registered = registry.RegisterVariable(added, new VariableId("variable:added")).IsOk;
                return Results.Ok(1);
            });
            if (failure == 2) {
                throw new InvalidOperationException("identity resolution failed");
            }
            return failure == 1
                ? Results.Failed<int>(new Err<ErrorCode>(ErrorCode.ApplicationError, "failure"))
                : nested;
        });

        Assert.Equal(failure == 0, result.IsOk);
        Assert.True(unchanged);
        Assert.False(promoted);
        Assert.False(registered);
        Assert.Null(registry.Identity(fallback));
        Assert.Null(registry.Identity(added));
        Assert.Equal(fallbackId, registry.VariableId(fallback, 0));
        Assert.True(registry.Validate().IsOk);
        Assert.True(registry.RegisterVariable(fallback, new VariableId("variable:promoted")).IsOk);
        Assert.True(registry.RegisterVariable(added, new VariableId("variable:added")).IsOk);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedDerivedRoleOrDiscriminatorCannotClaimStability(bool invalidRole) {
        string malformed = new((char)0xd800, 1);
        ModelElementIdentity identity = DerivedModelElementIdentity.Create(
            kind: ModelElementKind.Objective,
            role: invalidRole ? malformed : "valid",
            sourceId: "objective:source",
            sourceScope: ModelElementScope.Stable,
            sourceOrigin: new ModelElementOrigin("source", "one"),
            sourceProvenance: null,
            identityNamespace: "model",
            identitySchemaVersion: "1",
            discriminator: invalidRole ? "0" : malformed
        );
        Assert.Equal(ModelElementScope.ModelLocal, identity.Scope);
        Assert.Empty(identity.Provenance);
    }

    [Fact]
    public void StableRegistrationPreservesCanonicalProvenanceAndRejectsMismatchedOrigin() {
        ModelElementIdentityRegistry registry = new(
            @namespace: "provenance-model",
            schemaVersion: "1.0");
        object objective = new();
        ModelElementOrigin first = new("source", "a");
        ModelElementOrigin second = new("source", "b");

        registry.RegisterObjective(
            element: objective,
            id: new ObjectiveId("objective:aggregate"),
            provenance: new[] { second, first, second })
            .IsOk.Should().BeTrue();
        registry.Identity(objective)!.Provenance.Should().Equal(first, second);
        registry.Identity(objective)!.Origin.Should().BeNull();
        registry.RegisterObjective(
            element: new object(),
            id: new ObjectiveId("objective:invalid"),
            origin: new ModelElementOrigin("source", "missing"),
            provenance: new[] { first })
            .IsFailed.Should().BeTrue();
    }

    [Fact]
    public void RegisteredProvenanceCannotBeChangedThroughInputsOrReturnedBindings() {
        ModelElementIdentityRegistry registry = new();
        object variable = new();
        ModelElementOrigin origin = new("source", "original");
        var provenance = new List<ModelElementOrigin> { origin };

        registry.RegisterVariable(
            element: variable,
            id: new VariableId("variable:immutable"),
            origin: origin,
            provenance: provenance
        ).IsOk.Should().BeTrue();
        provenance.Clear();
        ModelElementIdentity identity = registry.Identity(variable)!;
        Assert.Throws<NotSupportedException>(() => ((IList<ModelElementOrigin>)identity.Provenance).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)identity.Provenance).Add(new ModelElementOrigin("source", "other")));
        identity.Provenance.Should().Equal(origin);
        registry.Entries().Single().Provenance.Should().Equal(origin);
        registry.RegisterVariable(
            element: variable,
            id: new VariableId("variable:immutable"),
            origin: origin
        ).IsOk.Should().BeTrue();
        registry.Validate().IsOk.Should().BeTrue();
    }

    [Fact]
    public void DirectAndCopiedIdentitiesOwnTheirProvenance() {
        ModelElementOrigin first = new("source", "first");
        ModelElementOrigin second = new("source", "second");
        var originalInput = new List<ModelElementOrigin> { first };
        var copiedInput = new List<ModelElementOrigin> { second };
        var identity = new ModelElementIdentity(
            Kind: ModelElementKind.Variable,
            Id: new ModelElementId("variable:direct"),
            Scope: ModelElementScope.Stable,
            Provenance: originalInput
        );
        ModelElementIdentity copied = identity with { Provenance = copiedInput };

        originalInput.Clear();
        copiedInput.Clear();
        identity.Provenance.Should().Equal(first);
        copied.Provenance.Should().Equal(second);
        Assert.Throws<NotSupportedException>(() => ((IList<ModelElementOrigin>)identity.Provenance).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ModelElementOrigin>)copied.Provenance).Clear());

        ModelElementIdentity defaulted = new(
            Kind: ModelElementKind.Variable,
            Id: new ModelElementId("variable:defaulted"),
            Scope: ModelElementScope.Stable,
            Origin: first
        );
        defaulted.Provenance.Should().Equal(first);
        Assert.Throws<NotSupportedException>(() => ((IList<ModelElementOrigin>)defaulted.Provenance).Clear());
    }

    [Fact]
    public void InvalidProvenanceReturnsFailureWithoutChangingBindings() {
        ModelElementIdentityRegistry registry = new();
        object variable = new();
        VariableId fallback = registry.VariableId(variable, 0);

        registry.RegisterVariable(
            element: variable,
            id: new VariableId("variable:null-origin"),
            provenance: new ModelElementOrigin[] { null! }
        ).IsFailed.Should().BeTrue();
        registry.RegisterVariable(
            element: variable,
            id: new VariableId("variable:unreadable-origin"),
            provenance: new UnreadableProvenance()
        ).IsFailed.Should().BeTrue();
        registry.Identity(variable).Should().BeNull();
        registry.VariableId(variable, 0).Should().Be(fallback);
        registry.Validate().IsOk.Should().BeTrue();
    }

    [Fact]
    public void NullRegistrationElementsReturnFailures() {
        ModelElementIdentityRegistry registry = new();

        registry.RegisterVariable(null, new VariableId("variable:null")).IsFailed.Should().BeTrue();
        registry.RegisterConstraint(null, new ConstraintId("constraint:null")).IsFailed.Should().BeTrue();
        registry.RegisterObjective(null, new ObjectiveId("objective:null")).IsFailed.Should().BeTrue();
        registry.Entries().Should().BeEmpty();
        registry.Validate().IsOk.Should().BeTrue();
    }

    [Fact]
    public void DerivedIdentityReturnsReadOnlyProvenance() {
        var source = new List<ModelElementOrigin> { new("source", "a") };
        ModelElementIdentity identity = DerivedModelElementIdentity.Create(
            kind: ModelElementKind.Objective,
            role: "derived",
            sourceId: "objective:a",
            sourceScope: ModelElementScope.Stable,
            sourceOrigin: null,
            sourceProvenance: source,
            identityNamespace: "model",
            identitySchemaVersion: "1.0"
        );

        source.Clear();
        identity.Provenance.Should().Equal(new ModelElementOrigin("source", "a"));
        Assert.Throws<NotSupportedException>(() => ((IList<ModelElementOrigin>)identity.Provenance).Clear());
    }

    [Fact]
    public void IdentityEqualityUsesProvenanceContentsAndOrder() {
        ModelElementOrigin first = new("source", "first");
        ModelElementOrigin second = new("source", "second");
        var identity = new ModelElementIdentity(
            Kind: ModelElementKind.Objective,
            Id: new ModelElementId("objective:equal"),
            Scope: ModelElementScope.Stable,
            Origin: first,
            Provenance: new[] { first, second }
        );
        ModelElementIdentity same = identity with { Provenance = new List<ModelElementOrigin> { first, second } };

        Assert.Equal(identity, same);
        Assert.True(identity == same);
        Assert.Equal(identity.GetHashCode(), same.GetHashCode());
        Assert.Contains(same, new HashSet<ModelElementIdentity> { identity });
        Assert.NotEqual(identity, identity with { Provenance = new[] { second, first } });
        Assert.NotEqual(identity, identity with { Id = new ModelElementId("objective:other") });
        Assert.NotEqual(identity, identity with { Scope = ModelElementScope.ModelLocal });
        Assert.NotEqual(identity, identity with { Origin = second });
        Assert.NotEqual(identity, identity with { Kind = ModelElementKind.Variable });
    }

    private sealed class UnreadableProvenance : IReadOnlyList<ModelElementOrigin> {
        public int Count => 1;
        public ModelElementOrigin this[int index] => throw new InvalidOperationException("unreadable");
        public IEnumerator<ModelElementOrigin> GetEnumerator() => throw new InvalidOperationException("unreadable");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void TypedIdentitiesRemainDistinctAndDuplicateIdsAreRejected() {
        ModelElementIdentityRegistry registry = new();
        object variable = new();
        object constraint = new();
        object objective = new();

        registry.RegisterVariable(variable, new VariableId("variable:x")).IsOk.Should().BeTrue();
        registry.RegisterConstraint(constraint, new ConstraintId("constraint:c")).IsOk.Should().BeTrue();
        registry.RegisterObjective(objective, new ObjectiveId("objective:z")).IsOk.Should().BeTrue();

        registry.VariableId(variable, 9).Should().Be(new VariableId("variable:x"));
        registry.ConstraintId(constraint, 9).Should().Be(new ConstraintId("constraint:c"));
        registry.ObjectiveId(objective, 9).Should().Be(new ObjectiveId("objective:z"));
        registry.Entries().Should().HaveCount(3);
        registry.Validate().IsOk.Should().BeTrue();
        registry.RegisterVariable(new object(), new VariableId("variable:x"))
            .IsFailed.Should().BeTrue();
        registry.RegisterVariable(variable, new VariableId("variable:other"))
            .IsFailed.Should().BeTrue();
    }

    [Fact]
    public void FallbacksAreModelLocalUniqueAndReplaceable() {
        ModelElementIdentityRegistry registry = new();
        object first = new();
        object second = new();

        VariableId firstId = registry.VariableId(first, 0);
        VariableId secondId = registry.VariableId(second, 0);

        firstId.Value.Should().StartWith("model-local-variable:0");
        secondId.Value.Should().NotBe(firstId.Value);
        registry.Identity(first).Should().BeNull();
        registry.RegisterVariable(
            element: first,
            id: new VariableId("stable:variable"),
            origin: new ModelElementOrigin("pipeline", "variable"))
            .IsOk.Should().BeTrue();
        registry.VariableId(first, 0).Should().Be(new VariableId("stable:variable"));
        registry.Entries().Should().HaveCount(2);
        registry.Validate().IsOk.Should().BeTrue();
    }

    [Fact]
    public void FailedStableRegistrationDoesNotDiscardFallback() {
        ModelElementIdentityRegistry registry = new();
        object first = new();
        object second = new();
        VariableId fallback = registry.VariableId(first, 0);

        registry.RegisterVariable(second, new VariableId("stable:conflict"))
            .IsOk.Should().BeTrue();
        registry.RegisterVariable(first, new VariableId("stable:conflict"))
            .IsFailed.Should().BeTrue();

        registry.VariableId(first, 0).Should().Be(fallback);
        registry.Validate().IsOk.Should().BeTrue();
    }

    [Fact]
    public void ReservedPrefixesAndBlankConfigurationAreRejected() {
        ModelElementIdentityRegistry registry = new();

        registry.RegisterVariable(
            element: new object(),
            id: new VariableId("model-local-variable:0"))
            .IsFailed.Should().BeTrue();
        registry.RegisterVariable(
            element: new object(),
            id: new VariableId("artifact:user-defined"))
            .IsFailed.Should().BeTrue();
        new ModelElementIdentityRegistry(@namespace: "", schemaVersion: "1.0")
            .Validate()
            .IsFailed.Should().BeTrue();
        new ModelElementIdentityRegistry(@namespace: "model", schemaVersion: "")
            .Validate()
            .IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentFallbackResolutionRemainsUnique() {
        ModelElementIdentityRegistry registry = new();
        object[] elements = Enumerable.Range(0, 128)
            .Select(_ => new object())
            .ToArray();

        string[] ids = await Task.WhenAll(elements.Select(element =>
            Task.Run(() => registry.VariableId(element, 0).Value)));

        ids.Should().OnlyHaveUniqueItems();
        registry.Validate().IsOk.Should().BeTrue();
    }

    [Fact]
    public void DerivedIdentityEncodingDistinguishesColonBoundaries() {
        ModelElementIdentity left = DerivedModelElementIdentity.Create(
            kind: ModelElementKind.Variable,
            role: "derived",
            sourceId: "a:b",
            sourceScope: ModelElementScope.Stable,
            sourceOrigin: new ModelElementOrigin("test", "left"),
            sourceProvenance: null,
            identityNamespace: "model",
            identitySchemaVersion: "1.0",
            discriminator: "c");
        ModelElementIdentity right = DerivedModelElementIdentity.Create(
            kind: ModelElementKind.Variable,
            role: "derived",
            sourceId: "a",
            sourceScope: ModelElementScope.Stable,
            sourceOrigin: new ModelElementOrigin("test", "right"),
            sourceProvenance: null,
            identityNamespace: "model",
            identitySchemaVersion: "1.0",
            discriminator: "b:c");

        left.Id.Should().NotBe(right.Id);
        left.Scope.Should().Be(ModelElementScope.Stable);
        right.Scope.Should().Be(ModelElementScope.Stable);
        DerivedModelElementIdentity.IsGeneratedArtifactModelElementId(
            left.Id.Value,
            ModelElementKind.Variable).Should().BeTrue();
    }

    [Fact]
    public void DerivedIdentityDoesNotInventOriginForModelLocalSource() {
        IReadOnlyList<ModelElementOrigin> provenance = new[] {
            new ModelElementOrigin("constraint", "balance"),
            new ModelElementOrigin("variable", "x"),
        };

        ModelElementIdentity identity = DerivedModelElementIdentity.Create(
            kind: ModelElementKind.Objective,
            role: "aggregate",
            sourceId: null,
            sourceScope: ModelElementScope.ModelLocal,
            sourceOrigin: null,
            sourceProvenance: provenance,
            identityNamespace: "model",
            identitySchemaVersion: "1.0");

        identity.Origin.Should().BeNull();
        identity.Scope.Should().Be(ModelElementScope.ModelLocal);
        identity.Provenance.Should().BeEmpty();
        identity.Id.Value.Should().StartWith("model-local-derived:");
    }
}
