#nullable enable

using System;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Tests.Solver.Report;

/// <summary>
/// 原生诊断标识解析测试。
/// Tests for the diagnostic model-element identifier resolution used by native analyzers.
/// </summary>
/// <remarks>
/// 变量来源分支依赖真实变量项，由 SCIP 互通用例覆盖；此处覆盖显式 ID 与各级回退键。
/// The variable-origin branch needs a real variable item and is covered by the SCIP interop tests; the explicit identifier
/// and every fallback key are covered here.
/// </remarks>
public class ModelElementIdTest {
    [Fact]
    public void VariableIdentityWinsOverDerivedKeys() {
        ModelViewVariable variable = Column(0, "x", identity: new ModelElementIdentity(
            Kind: ModelElementKind.Variable,
            Id: new ModelElementId("fixture:variable:x"),
            Scope: ModelElementScope.Stable));

        Assert.Equal("fixture:variable:x", variable.DiagnosticVariableId().Value);
    }

    [Fact]
    public void AnonymousVariableFallsBackToItsModelIndex() =>
        Assert.Equal("model-local-variable:2", Column(2, "x").DiagnosticVariableId().Value);

    [Fact]
    public void ExplicitConstraintIdWins() {
        using LinearTriadModel model = Rows(
            names: ["lower-row"],
            identities: [Identity("fixture:constraint:lower", origin: null)]);

        Assert.Equal("fixture:constraint:lower", model.DiagnosticConstraintId(0).Value);
    }

    [Fact]
    public void MissingIdFallsBackToTheOriginKey() {
        using LinearTriadModel model = Rows(
            names: ["lower-row"],
            identities: [Identity("", origin: new ModelElementOrigin(Kind: "constraint", Key: "crew-manning"))]);

        Assert.Equal("origin-constraint:crew-manning:0", model.DiagnosticConstraintId(0).Value);
    }

    [Fact]
    public void MissingIdAndOriginFallBackToTheConstraintName() {
        using LinearTriadModel model = Rows(names: ["lower-row"], identities: []);

        Assert.Equal("model-local-constraint:lower-row:0", model.DiagnosticConstraintId(0).Value);
    }

    [Fact]
    public void AnonymousRowsUseTheModelLocalPrefixAndIndex() {
        using LinearTriadModel model = Rows(names: ["first", ""], identities: []);

        // 行名只是模型内消歧信息，无名行才退化为纯索引标识。 / A row name only disambiguates inside the model; anonymous rows fall back to the bare index.
        Assert.Equal("model-local-constraint:first:0", model.DiagnosticConstraintId(0).Value);
        Assert.Equal("model-local-constraint:1", model.DiagnosticConstraintId(1).Value);
    }

    private static ModelViewVariable Column(int index, string name, ModelElementIdentity? identity = null) => new(
        index: index,
        lowerBound: Flt64.Zero,
        upperBound: Flt64.One,
        type: Continuous.Instance,
        name: name,
        identity: identity);

    private static ModelElementIdentity Identity(string id, ModelElementOrigin? origin) => new(
        Kind: ModelElementKind.Constraint,
        Id: new ModelElementId(id),
        Scope: origin is null ? ModelElementScope.ModelLocal : ModelElementScope.Stable,
        Origin: origin);

    private static LinearTriadModel Rows(string[] names, ModelElementIdentity[] identities) {
        ModelViewVariable[] variables = [Column(0, "x", null)];
        var rows = new SparseVector[names.Length];
        Array.Fill(rows, new SparseVector());
        var signs = new ConstraintRelation[names.Length];
        Array.Fill(signs, ConstraintRelation.LessEqual);
        var sources = new ConstraintSource[names.Length];
        Array.Fill(sources, ConstraintSource.Origin);
        return new LinearTriadModel(
            variables: variables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(rows),
                signs: signs,
                rhs: [.. new Flt64[names.Length]],
                names: names,
                sources: sources,
                identities: identities),
            objective: new Objective<LinearObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: [],
                constant: Flt64.Zero),
            name: "identity-fixture");
    }
}
