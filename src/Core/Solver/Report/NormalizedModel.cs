#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Core.Model.Basic;

namespace Fuookami.Ospf.Core.Solver.Report;

/// <summary>归一化变量 / A normalized variable.</summary>
/// <param name="Id">变量标识 / The variable identifier.</param>
/// <param name="Type">类型名 / The type name.</param>
/// <param name="LowerBound">下界文本 / The lower bound as text.</param>
/// <param name="UpperBound">上界文本 / The upper bound as text.</param>
/// <param name="Scope">标识作用域 / The identity scope.</param>
/// <param name="Origin">来源 / The origin.</param>
/// <param name="IdentityProvenance">来源链 / The provenance chain.</param>
/// <param name="IdentityNamespace">标识命名空间 / The identity namespace.</param>
/// <param name="IdentitySchemaVersion">标识 schema 版本 / The identity schema version.</param>
/// <param name="Name">名字 / The name.</param>
public sealed record NormalizedVariable(
    VariableId Id,
    string Type,
    string? LowerBound = null,
    string? UpperBound = null,
    ModelElementScope Scope = ModelElementScope.ModelLocal,
    ModelElementOrigin? Origin = null,
    IReadOnlyList<ModelElementOrigin>? IdentityProvenance = null,
    string? IdentityNamespace = null,
    string? IdentitySchemaVersion = null,
    string? Name = null);

/// <summary>归一化线性项 / A normalized linear term.</summary>
/// <param name="VariableId">变量标识 / The variable identifier.</param>
/// <param name="Coefficient">系数文本 / The coefficient as text.</param>
public sealed record NormalizedLinearTerm(VariableId VariableId, string Coefficient);

/// <summary>归一化二次项 / A normalized quadratic term.</summary>
/// <param name="FirstVariableId">第一个变量 / The first variable.</param>
/// <param name="SecondVariableId">第二个变量 / The second variable.</param>
/// <param name="Coefficient">系数文本 / The coefficient as text.</param>
public sealed record NormalizedQuadraticTerm(
    VariableId FirstVariableId,
    VariableId SecondVariableId,
    string Coefficient);

/// <summary>归一化约束 / A normalized constraint.</summary>
/// <param name="Id">约束标识 / The constraint identifier.</param>
/// <param name="Relation">关系 / The relation.</param>
/// <param name="Rhs">右端项文本 / The right-hand side as text.</param>
/// <param name="LinearTerms">线性项 / The linear terms.</param>
/// <param name="QuadraticTerms">二次项 / The quadratic terms.</param>
/// <param name="Scope">标识作用域 / The identity scope.</param>
/// <param name="Origin">来源 / The origin.</param>
/// <param name="IdentityProvenance">来源链 / The provenance chain.</param>
/// <param name="IdentityNamespace">标识命名空间 / The identity namespace.</param>
/// <param name="IdentitySchemaVersion">标识 schema 版本 / The identity schema version.</param>
/// <param name="Name">名字 / The name.</param>
/// <param name="Source">来源描述 / The source description.</param>
/// <param name="Priority">优先级 / The priority.</param>
/// <param name="Lazy">是否惰性 / Whether it is lazy.</param>
/// <param name="Group">分组 / The group.</param>
/// <param name="Args">附加参数 / Extra arguments.</param>
public sealed record NormalizedConstraint(
    ConstraintId Id,
    ConstraintRelation Relation,
    string Rhs,
    IReadOnlyList<NormalizedLinearTerm> LinearTerms,
    IReadOnlyList<NormalizedQuadraticTerm>? QuadraticTerms = null,
    ModelElementScope Scope = ModelElementScope.ModelLocal,
    ModelElementOrigin? Origin = null,
    IReadOnlyList<ModelElementOrigin>? IdentityProvenance = null,
    string? IdentityNamespace = null,
    string? IdentitySchemaVersion = null,
    string? Name = null,
    string? Source = null,
    int? Priority = null,
    bool? Lazy = null,
    string? Group = null,
    string? Args = null);

/// <summary>归一化目标 / A normalized objective.</summary>
/// <param name="Id">目标标识 / The objective identifier.</param>
/// <param name="Category">目标类别 / The objective category.</param>
/// <param name="Constant">常数项文本 / The constant as text.</param>
/// <param name="LinearTerms">线性项 / The linear terms.</param>
/// <param name="QuadraticTerms">二次项 / The quadratic terms.</param>
/// <param name="Scope">标识作用域 / The identity scope.</param>
/// <param name="Origin">来源 / The origin.</param>
/// <param name="IdentityProvenance">来源链 / The provenance chain.</param>
/// <param name="IdentityNamespace">标识命名空间 / The identity namespace.</param>
/// <param name="IdentitySchemaVersion">标识 schema 版本 / The identity schema version.</param>
public sealed record NormalizedObjective(
    ObjectiveId Id,
    string Category,
    string Constant,
    IReadOnlyList<NormalizedLinearTerm> LinearTerms,
    IReadOnlyList<NormalizedQuadraticTerm>? QuadraticTerms = null,
    ModelElementScope Scope = ModelElementScope.ModelLocal,
    ModelElementOrigin? Origin = null,
    IReadOnlyList<ModelElementOrigin>? IdentityProvenance = null,
    string? IdentityNamespace = null,
    string? IdentitySchemaVersion = null);

/// <summary>
/// 归一化数学模型 / A normalized mathematical model.
/// </summary>
/// <remarks>
/// B1175 按基线 `solver/report/NormalizedModel.kt` 的六个记录与 `validateNormalizedIdentityMetadata` 补。
///
/// **这一族记录的存在理由是"稳定指纹"**：**同一个模型重建两次，
/// 变量、约束、目标的<b>顺序可能不同</b>，而指纹必须相同。**
/// **所以所有数值都以<b>字符串</b>保存，而不是 `double`**——
/// **`double` 一旦参与往返，"同一个数"会因为格式化差异而变成两个指纹。**
///
/// **`IdentityValidation` 是构造时算出来的，不是事后调用的**：
/// **这样"不一致的模型"根本没有机会被创建出来。**
///
/// B1175, following the baseline's six records and `validateNormalizedIdentityMetadata`.
///
/// **The reason this family exists is the <b>stable fingerprint</b>**: **the same model rebuilt twice may order its
/// variables, constraints and objective differently, while the fingerprint must not change.**
/// **Every number is therefore kept as a <b>string</b> rather than a `double`** — **once a `double` takes part in a
/// round trip, "the same number" can become two fingerprints over formatting alone.**
///
/// **`IdentityValidation` is computed at construction rather than called afterwards**:
/// **so an inconsistent model never gets to exist.**
/// </remarks>
public sealed class NormalizedMathematicalModel
{
/// <param name="modelType">模型类型 / The model type.</param>
/// <param name="variables">变量 / The variables.</param>
/// <param name="constraints">约束 / The constraints.</param>
/// <param name="objective">目标 / The objective.</param>
/// <param name="identityNamespace">标识命名空间 / The identity namespace.</param>
/// <param name="identitySchemaVersion">标识 schema 版本 / The identity schema version.</param>
/// <param name="name">名字 / The name.</param>
/// <param name="schemaVersion">归一化 schema 版本 / The normalization schema version.</param>
    public NormalizedMathematicalModel(
        SolverModelType modelType,
        IReadOnlyList<NormalizedVariable> variables,
        IReadOnlyList<NormalizedConstraint> constraints,
        NormalizedObjective objective,
        string? identityNamespace = null,
        string? identitySchemaVersion = null,
        string? name = null,
        string schemaVersion = "1.1") {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(objective);

        ModelType = modelType;
        Variables = variables;
        Constraints = constraints;
        Objective = objective;
        IdentityNamespace = identityNamespace;
        IdentitySchemaVersion = identitySchemaVersion;
        Name = name;
        SchemaVersion = schemaVersion;
        IdentityValidation = NormalizedIdentity.Validate(
            identityNamespace, identitySchemaVersion, variables, constraints, objective);
    }

    public SolverModelType ModelType { get; }

    public IReadOnlyList<NormalizedVariable> Variables { get; }

    public IReadOnlyList<NormalizedConstraint> Constraints { get; }

    public NormalizedObjective Objective { get; }

    public string? IdentityNamespace { get; }

    public string? IdentitySchemaVersion { get; }

    public string? Name { get; }

    public string SchemaVersion { get; }

    /// <summary>构造时算出的标识一致性校验 / The identity consistency check computed at construction.</summary>
    public Result<Success, ErrorCode, Error<ErrorCode>> IdentityValidation { get; }

    /// <summary>标识是否一致 / Whether the identity metadata is consistent.</summary>
    public bool IsIdentityConsistent => IdentityValidation is Ok<Success, ErrorCode, Error<ErrorCode>>;
}

/// <summary>
/// 归一化模型的标识一致性校验 / Identity consistency for a normalized model.
/// </summary>
/// <remarks>
/// B1175 按基线 `validateNormalizedIdentityMetadata` 补。
///
/// **它挡的是"同一个模型里出现两个命名空间"**。
///
/// **为什么必须挡**：**命名空间会被写进指纹。** **如果变量说 `A`、某个约束说 `B`，
/// 那么指纹里就<b>同时</b>含 `A` 与 `B`——而"哪个元素说了哪个"取决于遍历顺序。
/// 于是同一个模型能产生两个指纹，取决于它是怎么重建的。**
///
/// **判定是"去重后多于一个"**：**全空、全 `A`、或只有一个元素说了 `A` 而其余为空，
/// 都是一致的。**
///
/// **空白串按"没说"处理**——**`""` 与 `null` 都表示"没有指定"，
/// 把空白串当成一个命名空间会让 `""` 与 `"A"` 被判为不一致，而那是一种无意义的失败。**
///
/// B1175, following the baseline's `validateNormalizedIdentityMetadata`.
///
/// **It blocks "two namespaces inside one model".**
///
/// **Why it must**: **the namespace goes into the fingerprint.** **If a variable says `A` and some constraint says
/// `B`, the fingerprint contains <b>both</b> — and which element said which depends on traversal order.
/// The same model can then produce two fingerprints depending on how it was rebuilt.**
///
/// **The rule is "more than one distinct value"**: **all empty, all `A`, or one element saying `A` while the rest are
/// empty are all consistent.**
///
/// **Blank strings count as "not stated"** — **`""` and `null` both mean "unspecified"; treating a blank as a
/// namespace would fail `""` against `"A"`, a meaningless failure.**
/// </remarks>
public static class NormalizedIdentity
{
    /// <summary>校验命名空间与 schema 版本的一致性 / Validate namespace and schema consistency.</summary>
    public static Result<Success, ErrorCode, Error<ErrorCode>> Validate(
        string? identityNamespace,
        string? identitySchemaVersion,
        IReadOnlyList<NormalizedVariable> variables,
        IReadOnlyList<NormalizedConstraint> constraints,
        NormalizedObjective objective) {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(objective);

        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        AddDistinct(namespaces, identityNamespace);
        foreach (NormalizedVariable variable in variables) {
            AddDistinct(namespaces, variable.IdentityNamespace);
        }

        foreach (NormalizedConstraint constraint in constraints) {
            AddDistinct(namespaces, constraint.IdentityNamespace);
        }

        AddDistinct(namespaces, objective.IdentityNamespace);

        if (namespaces.Count > 1) {
            return Fail(
                $"the normalized model namespaces disagree ({string.Join(", ", namespaces)})");
        }

        var schemaVersions = new HashSet<string>(StringComparer.Ordinal);
        AddDistinct(schemaVersions, identitySchemaVersion);
        foreach (NormalizedVariable variable in variables) {
            AddDistinct(schemaVersions, variable.IdentitySchemaVersion);
        }

        foreach (NormalizedConstraint constraint in constraints) {
            AddDistinct(schemaVersions, constraint.IdentitySchemaVersion);
        }

        AddDistinct(schemaVersions, objective.IdentitySchemaVersion);

        return schemaVersions.Count > 1
            ? Fail($"the normalized model schemas disagree ({string.Join(", ", schemaVersions)})")
            : new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }

    /// <summary>空白串按"没说"处理 / A blank string counts as "not stated".</summary>
    private static void AddDistinct(HashSet<string> target, string? value) {
        if (!string.IsNullOrWhiteSpace(value)) {
            _ = target.Add(value);
        }
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        new Failed<Success, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);
}
