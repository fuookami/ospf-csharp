#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Report;

/// <summary>
/// 一个模型里出现两个命名空间，指纹就不唯一 / Two namespaces in one model make the fingerprint ambiguous.
/// </summary>
/// <remarks>
/// 对齐基线 `solver/report/NormalizedModel.kt` 的六个记录与 `validateNormalizedIdentityMetadata`。
///
/// **这一族记录存在的理由是稳定指纹**：**同一个模型重建两次，元素顺序可能不同，而指纹必须相同。**
/// **所以所有数值以字符串保存——`double` 一旦参与往返，"同一个数"会因为格式化差异变成两个指纹。**
///
/// adds
///
/// **The family exists for a stable fingerprint**: **rebuilt twice, the same model may order its elements differently
/// while the fingerprint must not change.** **Numbers are therefore strings — once a `double` round-trips, "the same
/// number" can become two fingerprints over formatting alone.**
/// </remarks>
public class NormalizedModelTests
{
    /// <summary>
    /// **变量说 `A`、约束说 `B`，模型必须被拒绝** / A variable saying `A` and a constraint saying `B` is rejected.
    /// </summary>
    /// <remarks>
    /// **这是本批的核心判据。**
    ///
    /// **命名空间进指纹。两个都出现时，指纹里<b>同时</b>含 `A` 与 `B`，
    /// 而"哪个元素说了哪个"取决于遍历顺序——于是同一个模型能产生两个指纹。**
    ///
    /// **判据断言消息里点了 "namespace"，而不只是"返回了失败"**：
    /// **命名空间与 schema 是两条独立的检查，混淆它们会让失败指向错误的原因。**
    ///
    /// **This is the batch's core criterion.**
    ///
    /// **The namespace goes into the fingerprint. When both appear, it contains <b>both</b>, and which element said
    /// which depends on traversal order — so one model can produce two fingerprints.**
    ///
    /// **The criterion asserts the message says "namespace" rather than merely "it failed"**: **namespace and schema
    /// are independent checks, and conflating them points a failure at the wrong cause.**
    /// </remarks>
    [Fact]
    public void TheDisagreeingNamespaces_ShouldBeRejected() {
        NormalizedMathematicalModel model = Model(
            variableNamespace: "A",
            constraintNamespace: "B");

        model.IsIdentityConsistent.Should().BeFalse("the namespaces disagree");
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)model.IdentityValidation).Error.Message.Should()
            .Contain("namespace", "the failure must name the axis that actually disagreed");
    }

    /// <summary>**schema 版本不一致被单独报告** / A schema disagreement is reported on its own axis.</summary>
    [Fact]
    public void TheDisagreeingSchemas_ShouldBeRejected() {
        NormalizedMathematicalModel model = Model(
            variableSchema: "1",
            constraintSchema: "2");

        model.IsIdentityConsistent.Should().BeFalse("the schema versions disagree");
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)model.IdentityValidation).Error.Message.Should()
            .Contain("schema", "the namespace axis is untouched here, so the message must name the schema");
    }

    /// <summary>**一致时通过** / Consistent metadata passes.</summary>
    [Fact]
    public void TheConsistentMetadata_ShouldPass() {
        NormalizedMathematicalModel model = Model(
            variableNamespace: "A",
            constraintNamespace: "A");

        model.IsIdentityConsistent.Should().BeTrue("both sides name the same namespace");
    }

    /// <summary>
    /// **空白串按"没说"处理** / A blank string counts as "not stated".
    /// </summary>
    /// <remarks>
    /// **`""` 与 `null` 都表示"没有指定"。**
    /// **把空白串当成一个命名空间，会让 `""` 与 `"A"` 被判为不一致——
    /// 而那是一种无意义的失败：用户没有说过任何冲突的话。**
    ///
    /// **`""` and `null` both mean "unspecified".**
    /// **Treating a blank as a namespace would fail `""` against `"A"` — a meaningless failure, since the user never
    /// stated anything conflicting.**
    /// </remarks>
    [Fact]
    public void TheBlankNamespace_ShouldCountAsUnstated() {
        Model(variableNamespace: "", constraintNamespace: "A").IsIdentityConsistent.Should().BeTrue(
            "a blank string is not a namespace");
        Model(variableNamespace: "   ", constraintNamespace: "A").IsIdentityConsistent.Should().BeTrue(
            "whitespace is not a namespace either");
        Model(variableNamespace: null, constraintNamespace: "A").IsIdentityConsistent.Should().BeTrue();
        Model(variableNamespace: null, constraintNamespace: null).IsIdentityConsistent.Should().BeTrue(
            "a model with no identity metadata at all is consistent");
    }

    /// <summary>
    /// **只有一个元素说了命名空间，也算一致** / One element naming a namespace is consistent.
    /// </summary>
    /// <remarks>
    /// **判定是"去重后多于一个"，不是"全都有值"。**
    /// **要求所有元素都填，会让"只标了模型级命名空间"这种正常情形被拒绝。**
    ///
    /// **The rule is "more than one distinct", not "all present".**
    /// **Demanding a value everywhere would reject the normal case of a model-level namespace only.**
    /// </remarks>
    [Fact]
    public void TheSingleNamespace_ShouldBeConsistent() {
        var model = new NormalizedMathematicalModel(
            SolverModelType.Lp,
            new[] { Variable("x") },
            new[] { Constraint("c") },
            Objective(),
            identityNamespace: "A");

        model.IsIdentityConsistent.Should().BeTrue(
            "only the model-level namespace is stated, which is not a disagreement");
    }

    /// <summary>
    /// **不一致的模型被创建出来了，但校验结果随之而定** / The model exists; its verdict is fixed at construction.
    /// </summary>
    /// <remarks>
    /// **`IdentityValidation` 是构造时算出的，不是事后调用的。**
    ///
    /// **这带来一个必须说清的性质**：**构造<b>不会</b>抛异常——它把结论存进 `IdentityValidation`。**
    /// **"不一致的模型根本没有机会被创建"是我原本想写的说法，而那是不准确的：
    /// 它被创建了，只是带着一个失败标记。**
    ///
    /// **这个区别重要**：**拒绝构造会让调用方无法<b>报告</b>问题（他们拿不到那个对象来诊断），
    /// 而带标记的构造让他们能既拿到模型、又拿到结论。**
    ///
    /// **`IdentityValidation` is computed at construction rather than called afterwards.**
    ///
    /// **That brings a property worth stating precisely**: **construction does <b>not</b> throw — it stores the verdict
    /// in `IdentityValidation`.** **"An inconsistent model never gets to exist" was the phrasing I first reached for,
    /// and it is inaccurate: the model exists, carrying a failure.**
    ///
    /// **The distinction matters**: **refusing to construct leaves callers unable to <b>report</b> the problem — they
    /// cannot get the object to diagnose it — while a tagged construction hands them both the model and the verdict.**
    /// </remarks>
    [Fact]
    public void TheInconsistentModel_ShouldCarryItsVerdictWithoutThrowing() {
        NormalizedMathematicalModel model = Model(
            variableNamespace: "A",
            constraintNamespace: "B");

        model.Variables.Should().ContainSingle("the model is still usable for diagnosis");
        model.IdentityValidation.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
            "the verdict rides along with the model");
    }

    /// <summary>**数值以字符串保存，不经过 `double`** / Numbers are strings, never `double`.</summary>
    /// <remarks>
    /// **这条判据检查的是"保真"而不是"能存"**：**一个高于 `double` 精度的常量
    /// 必须原样往返，而任何 `double` 中转都会截断它。**
    ///
    /// **The criterion checks fidelity rather than capacity**: **a constant beyond `double` precision must survive a
    /// round trip unchanged, and any `double` hop would truncate it.**
    /// </remarks>
    [Fact]
    public void TheNumbers_ShouldRoundTripExactly() {
        const string precise = "0.123456789012345678901234567890";
        var objective = new NormalizedObjective(
            new ObjectiveId("obj"),
            "Min",
            precise,
            Array.Empty<NormalizedLinearTerm>());

        objective.Constant.Should().Be(precise, "the text must not pass through a double");
        objective.Constant.Should().NotBe(
            precise.Length > 17 ? precise[..17] : precise, "a truncated value would be a different fingerprint");
    }

    /// <summary>**二次项缺省为空** / Quadratic terms default to empty.</summary>
    [Fact]
    public void TheQuadraticTerms_ShouldDefaultToAbsent() {
        NormalizedConstraint constraint = Constraint("c");
        constraint.QuadraticTerms.Should().BeNull("a linear-only constraint states no quadratic terms");
        new NormalizedObjective(
            new ObjectiveId("obj"), "Min", "0", Array.Empty<NormalizedLinearTerm>())
            .QuadraticTerms.Should().BeNull();
    }

    private static NormalizedMathematicalModel Model(
        string? variableNamespace = null,
        string? constraintNamespace = null,
        string? variableSchema = null,
        string? constraintSchema = null) =>
        new(
            SolverModelType.Lp,
            new[] { Variable("x", variableNamespace, variableSchema) },
            new[] { Constraint("c", constraintNamespace, constraintSchema) },
            Objective());

    private static NormalizedVariable Variable(
        string name,
        string? identityNamespace = null,
        string? identitySchemaVersion = null) =>
        new(
            new VariableId(name),
            "Continuous",
            IdentityNamespace: identityNamespace,
            IdentitySchemaVersion: identitySchemaVersion,
            Name: name);

    private static NormalizedConstraint Constraint(
        string name,
        string? identityNamespace = null,
        string? identitySchemaVersion = null) =>
        new(
            new ConstraintId(name),
            ConstraintRelation.LessEqual,
            "0",
            Array.Empty<NormalizedLinearTerm>(),
            IdentityNamespace: identityNamespace,
            IdentitySchemaVersion: identitySchemaVersion,
            Name: name);

    private static NormalizedObjective Objective() =>
        new(new ObjectiveId("obj"), "Min", "0", Array.Empty<NormalizedLinearTerm>());
}
