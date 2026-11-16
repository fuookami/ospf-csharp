#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Solver.Remote.Adapter;

/// <summary>
/// OSPF 模型到远程 wire 模型的序列化适配 / Adapter serializing OSPF models into remote wire models.
/// </summary>
/// <remarks>
/// 对齐<c>OspfRemoteModelSerializer</c>。本类当前提供**身份闸门**：任何模型元素在进入
/// 远程序列化之前，其身份元数据必须先通过 <see cref="IsValidRemoteIdentity"/>；不通过即拒绝序列化，
/// 绝不"尽力序列化"一个身份不完整的模型。
///
/// 的线性／二次模型体序列化与 <c>ModelData</c> 工厂尚未实现——它们依赖的模型级身份访问器
/// 见 <c>IBasicModelView.IdentityNamespace</c>／<c>IdentitySchemaVersion</c>，这里已作为默认接口成员
/// 补齐（不破坏任何现有实现）。
///
/// Aligns with the fixed Kotlin baseline <c>OspfRemoteModelSerializer</c>. This class currently provides
/// the **identity gate**: a model element's identity metadata must pass
/// <see cref="IsValidRemoteIdentity"/> before any remote serialization, and a failure rejects
/// serialization outright rather than best-effort serializing an incomplete identity.
///
/// The code's linear/quadratic body serialization and the <c>ModelData</c> factories are not yet
/// implemented; the model-level identity accessors they need are supplied here as
/// <c>IBasicModelView.IdentityNamespace</c>/<c>IdentitySchemaVersion</c> default interface members, which
/// break no existing implementation.
/// </remarks>
public static class OspfRemoteModelSerializer {
    /// <summary>
    /// 序列化线性三元模型 / Serialize a linear triad model.
    /// </summary>
    /// <remarks>
    /// ：先做**整模型身份校验**（C# 对应 <c>ValidateIdentity</c>，它会拒绝混用多个命名空间或
    /// schema 的模型），再逐元素过身份闸门，任一失败即拒绝序列化。模型级命名空间取自约束批次——
    /// 因为整模型校验已保证全部元素共用同一套命名空间与 schema，取任一处都等价。
    /// validate the whole model's identity first (C#'s <c>ValidateIdentity</c>,
    /// which rejects a model mixing several namespaces or schemas), then gate each element; any failure
    /// rejects serialization. The model-level namespace comes from the constraint batch because the
    /// whole-model check already guarantees every element shares one namespace and schema.
    /// </remarks>
    /// <param name="model">线性三元模型视图 / Linear triad model view.</param>
    /// <returns>序列化线性模型 / Serialized linear model.</returns>
    public static Result<SerializedLinearModel, ErrorCode, Error<ErrorCode>> Serialize(
        ILinearTriadModelView model) {
        ArgumentNullException.ThrowIfNull(model);

        Try identityValidation = model.ValidateIdentity();
        if (!identityValidation.IsOk) {
            return Results.Failed<SerializedLinearModel>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "模型元素身份元数据无效，不能序列化远程模型 / "
                + "Model element identity metadata is invalid; remote serialization is rejected"));
        }

        ModelConstraint<LinearConstraintCell> constraints = model.Constraints;
        Objective<LinearObjectiveCell> objective = model.Objective;
        string? modelNamespace = constraints.IdentityNamespace;
        string? modelSchema = constraints.IdentitySchemaVersion;

        foreach (ModelViewVariable variable in model.Variables) {
            if (!IsValidRemoteIdentity(
                kind: ModelElementKind.Variable,
                id: variable.Id?.Value,
                scope: variable.IdentityScope,
                origin: variable.IdentityOrigin,
                provenance: variable.IdentityProvenance,
                identityNamespace: variable.IdentityNamespace,
                identitySchemaVersion: variable.IdentitySchemaVersion)) {
                return InvalidIdentity();
            }
        }
        for (int index = 0; index < constraints.ConstraintCount; index++) {
            if (!IsValidRemoteIdentity(
                kind: ModelElementKind.Constraint,
                id: constraints.Ids.Count > index ? constraints.Ids[index].Value : null,
                scope: constraints.IdentityScopeAt(index),
                origin: constraints.IdentityOriginAt(index),
                provenance: constraints.IdentityProvenanceAt(index),
                identityNamespace: constraints.IdentityNamespace,
                identitySchemaVersion: constraints.IdentitySchemaVersion)) {
                return InvalidIdentity();
            }
        }
        if (!IsValidRemoteIdentity(
            kind: ModelElementKind.Objective,
            id: objective.Id?.Value,
            scope: objective.IdentityScope,
            origin: objective.IdentityOrigin,
            provenance: objective.IdentityProvenance,
            identityNamespace: objective.IdentityNamespace,
            identitySchemaVersion: objective.IdentitySchemaVersion)) {
            return InvalidIdentity();
        }

        var variables = new List<SerializedVariable>(model.Variables.Count);
        for (int index = 0; index < model.Variables.Count; index++) {
            ModelViewVariable variable = model.Variables[index];
            variables.Add(new SerializedVariable(
                Name: variable.Name,
                Type: ToSerializedVariableType(variable.Type),
                LowerBound: variable.LowerBound.ToDouble(),
                UpperBound: variable.UpperBound.ToDouble()));
        }

        var serializedConstraints = new List<SerializedConstraint>(constraints.ConstraintCount);
        for (int rowIndex = 0; rowIndex < constraints.ConstraintCount; rowIndex++) {
            var cells = new List<SerializedConstraintCell>();
            if (rowIndex < constraints.Lhs.Count) {
                foreach (LinearConstraintCell cell in constraints.Lhs[rowIndex]) {
                    cells.Add(new SerializedConstraintCell(
                        VariableIndex: cell.ColIndex,
                        Coefficient: cell.Coefficient.ToDouble()));
                }
            }
            string name = rowIndex < constraints.Names.Count ? constraints.Names[rowIndex] : string.Empty;
            serializedConstraints.Add(new SerializedConstraint(
                // ：空名称回退为 cons<rowIndex>。
                // An empty name falls back to cons<rowIndex>.
                Name: string.IsNullOrEmpty(name) ? $"cons{rowIndex}" : name,
                Sign: ToSerializedSign(constraints.Signs[rowIndex]),
                Rhs: constraints.Rhs[rowIndex].ToDouble(),
                Cells: cells,
                IdentityId: constraints.Ids.Count > rowIndex ? constraints.Ids[rowIndex].Value : null,
                IdentityScope: ToRemoteIdentityScope(constraints.IdentityScopeAt(rowIndex)),
                IdentityOriginKind: constraints.IdentityOriginAt(rowIndex)?.Kind,
                IdentityOriginKey: constraints.IdentityOriginAt(rowIndex)?.Key,
                IdentityNamespace: constraints.IdentityNamespace,
                IdentitySchemaVersion: constraints.IdentitySchemaVersion,
                IdentityProvenance: ToSerializedProvenance(
                    constraints.IdentityProvenanceAt(rowIndex),
                    constraints.IdentityOriginAt(rowIndex))));
        }

        var objectiveCells = new List<SerializedObjectiveCell>(objective.Cells.Count);
        foreach (LinearObjectiveCell cell in objective.Cells) {
            objectiveCells.Add(new SerializedObjectiveCell(
                VariableIndex: cell.ColIndex,
                Coefficient: cell.Coefficient.ToDouble()));
        }

        return Results.Ok(new SerializedLinearModel(
            Name: model.Name,
            Variables: variables,
            Constraints: serializedConstraints,
            Objective: new SerializedObjective(
                Category: objective.Category == ObjectCategory.Maximum
                    ? SerializedObjectiveCategory.Maximize
                    : SerializedObjectiveCategory.Minimize,
                Cells: objectiveCells,
                Constant: objective.Constant.ToDouble(),
                IdentityId: objective.Id?.Value,
                IdentityScope: ToRemoteIdentityScope(objective.IdentityScope),
                IdentityOriginKind: objective.IdentityOrigin?.Kind,
                IdentityOriginKey: objective.IdentityOrigin?.Key,
                IdentityNamespace: objective.IdentityNamespace,
                IdentitySchemaVersion: objective.IdentitySchemaVersion,
                IdentityProvenance: ToSerializedProvenance(
                    objective.IdentityProvenance,
                    objective.IdentityOrigin)),
            IdentityNamespace: modelNamespace,
            IdentitySchemaVersion: modelSchema));
    }

    /// <summary>
    /// 身份作用域转 wire 代码 / Convert an identity scope to its wire code.
    /// </summary>
    /// <remarks>
    /// ：wire 上是大写下划线代码（<c>MODEL_LOCAL</c>／<c>STABLE</c>），不是 C# 的成员名。
    /// the wire carries upper-case underscore codes (<c>MODEL_LOCAL</c>/<c>STABLE</c>),
    /// not the C# member names.
    /// </remarks>
    /// <param name="scope">身份作用域 / Identity scope.</param>
    /// <returns>wire 代码 / Wire code.</returns>
    public static string ToRemoteIdentityScope(ModelElementScope scope) =>
        scope == ModelElementScope.Stable ? "STABLE" : "MODEL_LOCAL";

    /// <summary>
    /// 约束关系转 wire 关系符 / Convert a constraint relation to its wire sign.
    /// </summary>
    /// <param name="relation">约束关系 / Constraint relation.</param>
    /// <returns>wire 关系符 / Wire sign.</returns>
    public static SerializedConstraintSign ToSerializedSign(ConstraintRelation relation) {
        ArgumentNullException.ThrowIfNull(relation);
        // ConstraintRelation 是带单例成员的抽象记录，不是枚举，因此用相等比较而非 switch 常量。
        // ConstraintRelation is an abstract record with singleton members rather than an enum, so this
        // compares by equality instead of switching on constants.
        if (relation == ConstraintRelation.Equal) {
            return SerializedConstraintSign.Equal;
        }
        return relation == ConstraintRelation.GreaterEqual
            ? SerializedConstraintSign.GreaterEqual
            : SerializedConstraintSign.LessEqual;
    }

    private static SerializedVariableType ToSerializedVariableType(
        Fuookami.Ospf.Core.Variable.IVariableTypeKind type) {
        if (type.IsBinaryType) {
            return SerializedVariableType.Binary;
        }
        return type.IsContinuousType
            ? SerializedVariableType.Continuous
            : SerializedVariableType.Integer;
    }

    private static List<SerializedModelElementOrigin> ToSerializedProvenance(
        IReadOnlyList<ModelElementOrigin> provenance,
        ModelElementOrigin? origin) {
        var serialized = new List<SerializedModelElementOrigin>();
        if (provenance is { Count: > 0 }) {
            foreach (ModelElementOrigin entry in provenance) {
                serialized.Add(new SerializedModelElementOrigin(entry.Kind, entry.Key));
            }
        }
        else if (origin is not null) {
            serialized.Add(new SerializedModelElementOrigin(origin.Kind, origin.Key));
        }
        return serialized;
    }

    private static Result<SerializedLinearModel, ErrorCode, Error<ErrorCode>> InvalidIdentity() =>
        Results.Failed<SerializedLinearModel>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "模型元素身份元数据无效，不能序列化远程模型 / "
            + "Model element identity metadata is invalid; remote serialization is rejected"));

    /// <summary>
    /// 序列化二次四元模型 / Serialize a quadratic tetrad model.
    /// </summary>
    /// <remarks>
    /// ：整模型身份校验 → 逐元素身份闸门 → 组装。二次单元按列的**两个**索引拆分：C# 用
    /// <c>Col2 &lt; 0</c> 哨兵值表示线性项（Kotlin 用可空 <c>colIndex2</c>），因此线性项进入
    /// <c>LinearCells</c>、其余进入 <c>QuadraticCells</c>，与<c>colIndex2 == null</c> 判定等价。
    ///
    /// ：同一个约束行会**同时**出现在 <c>linearConstraints</c> 与 <c>quadraticConstraints</c>
    /// 两个列表里，前者只带线性项、后者带全部项——这是基线行为，不是重复。
    ///
    /// validate the whole model's identity, gate each element, then assemble.
    /// Quadratic cells split on the two column indices: C# uses a <c>Col2 &lt; 0</c> sentinel for linear
    /// terms (Kotlin uses a nullable <c>colIndex2</c>), so linear terms go to <c>LinearCells</c> and the rest
    /// to <c>QuadraticCells</c>, equivalent to its <c>colIndex2 == null</c> test.
    ///
    /// The same constraint row appears in **both** <c>linearConstraints</c> and
    /// <c>quadraticConstraints</c>: the former carries only its linear terms and the latter all terms. That
    /// is baseline behaviour, not duplication.
    /// </remarks>
    /// <param name="model">二次四元模型视图 / Quadratic tetrad model view.</param>
    /// <returns>序列化二次模型 / Serialized quadratic model.</returns>
    public static Result<SerializedQuadraticModel, ErrorCode, Error<ErrorCode>> Serialize(
        IQuadraticTetradModelView model) {
        ArgumentNullException.ThrowIfNull(model);

        if (!model.ValidateIdentity().IsOk) {
            return InvalidQuadraticIdentity();
        }

        ModelConstraint<QuadraticConstraintCell> constraints = model.Constraints;
        Objective<QuadraticObjectiveCell> objective = model.Objective;

        foreach (ModelViewVariable variable in model.Variables) {
            if (!IsValidRemoteIdentity(
                kind: ModelElementKind.Variable,
                id: variable.Id?.Value,
                scope: variable.IdentityScope,
                origin: variable.IdentityOrigin,
                provenance: variable.IdentityProvenance,
                identityNamespace: variable.IdentityNamespace,
                identitySchemaVersion: variable.IdentitySchemaVersion)) {
                return InvalidQuadraticIdentity();
            }
        }
        for (int index = 0; index < constraints.ConstraintCount; index++) {
            if (!IsValidRemoteIdentity(
                kind: ModelElementKind.Constraint,
                id: constraints.Ids.Count > index ? constraints.Ids[index].Value : null,
                scope: constraints.IdentityScopeAt(index),
                origin: constraints.IdentityOriginAt(index),
                provenance: constraints.IdentityProvenanceAt(index),
                identityNamespace: constraints.IdentityNamespace,
                identitySchemaVersion: constraints.IdentitySchemaVersion)) {
                return InvalidQuadraticIdentity();
            }
        }
        if (!IsValidRemoteIdentity(
            kind: ModelElementKind.Objective,
            id: objective.Id?.Value,
            scope: objective.IdentityScope,
            origin: objective.IdentityOrigin,
            provenance: objective.IdentityProvenance,
            identityNamespace: objective.IdentityNamespace,
            identitySchemaVersion: objective.IdentitySchemaVersion)) {
            return InvalidQuadraticIdentity();
        }

        var variables = new List<SerializedVariable>(model.Variables.Count);
        foreach (ModelViewVariable variable in model.Variables) {
            variables.Add(new SerializedVariable(
                Name: variable.Name,
                Type: ToSerializedVariableType(variable.Type),
                LowerBound: variable.LowerBound.ToDouble(),
                UpperBound: variable.UpperBound.ToDouble()));
        }

        var linearConstraints = new List<SerializedConstraint>(constraints.ConstraintCount);
        var quadraticConstraints = new List<SerializedQuadraticConstraint>(constraints.ConstraintCount);
        for (int rowIndex = 0; rowIndex < constraints.ConstraintCount; rowIndex++) {
            IReadOnlyList<QuadraticConstraintCell> row = rowIndex < constraints.Lhs.Count
                ? constraints.Lhs[rowIndex]
                : Array.Empty<QuadraticConstraintCell>();
            var linearCells = new List<SerializedConstraintCell>();
            var quadraticCells = new List<SerializedQuadraticConstraintCell>();
            foreach (QuadraticConstraintCell cell in row) {
                if (cell.Col2 < 0) {
                    linearCells.Add(new SerializedConstraintCell(
                        VariableIndex: cell.Col1,
                        Coefficient: cell.Coefficient.ToDouble()));
                }
                else {
                    quadraticCells.Add(new SerializedQuadraticConstraintCell(
                        RowIndex: cell.RowIndex,
                        ColIndex1: cell.Col1,
                        ColIndex2: cell.Col2,
                        Coefficient: cell.Coefficient.ToDouble()));
                }
            }
            string rawName = rowIndex < constraints.Names.Count
                ? constraints.Names[rowIndex]
                : string.Empty;
            string name = string.IsNullOrEmpty(rawName) ? $"cons{rowIndex}" : rawName;
            SerializedConstraintSign sign = ToSerializedSign(constraints.Signs[rowIndex]);
            double rhs = constraints.Rhs[rowIndex].ToDouble();
            string? identityId = constraints.Ids.Count > rowIndex
                ? constraints.Ids[rowIndex].Value
                : null;
            string identityScope = ToRemoteIdentityScope(constraints.IdentityScopeAt(rowIndex));
            string? originKind = constraints.IdentityOriginAt(rowIndex)?.Kind;
            string? originKey = constraints.IdentityOriginAt(rowIndex)?.Key;
            List<SerializedModelElementOrigin> provenance = ToSerializedProvenance(
                constraints.IdentityProvenanceAt(rowIndex),
                constraints.IdentityOriginAt(rowIndex));

            linearConstraints.Add(new SerializedConstraint(
                Name: name,
                Sign: sign,
                Rhs: rhs,
                Cells: linearCells,
                IdentityId: identityId,
                IdentityScope: identityScope,
                IdentityOriginKind: originKind,
                IdentityOriginKey: originKey,
                IdentityNamespace: constraints.IdentityNamespace,
                IdentitySchemaVersion: constraints.IdentitySchemaVersion,
                IdentityProvenance: provenance));

            quadraticConstraints.Add(new SerializedQuadraticConstraint(
                Name: name,
                Sign: sign,
                Rhs: rhs,
                LinearCells: linearCells,
                QuadraticCells: quadraticCells,
                IdentityId: identityId,
                IdentityScope: identityScope,
                IdentityOriginKind: originKind,
                IdentityOriginKey: originKey,
                IdentityNamespace: constraints.IdentityNamespace,
                IdentitySchemaVersion: constraints.IdentitySchemaVersion,
                IdentityProvenance: provenance));
        }

        var objectiveLinearCells = new List<SerializedObjectiveCell>();
        var objectiveQuadraticCells = new List<SerializedQuadraticObjectiveCell>();
        foreach (QuadraticObjectiveCell cell in objective.Cells) {
            if (cell.Col2 < 0) {
                objectiveLinearCells.Add(new SerializedObjectiveCell(
                    VariableIndex: cell.Col1,
                    Coefficient: cell.Coefficient.ToDouble()));
            }
            else {
                objectiveQuadraticCells.Add(new SerializedQuadraticObjectiveCell(
                    ColIndex1: cell.Col1,
                    ColIndex2: cell.Col2,
                    Coefficient: cell.Coefficient.ToDouble()));
            }
        }

        return Results.Ok(new SerializedQuadraticModel(
            Name: model.Name,
            Variables: variables,
            LinearConstraints: linearConstraints,
            QuadraticConstraints: quadraticConstraints,
            Objective: new SerializedQuadraticObjective(
                Category: objective.Category == ObjectCategory.Maximum
                    ? SerializedObjectiveCategory.Maximize
                    : SerializedObjectiveCategory.Minimize,
                LinearCells: objectiveLinearCells,
                QuadraticCells: objectiveQuadraticCells,
                Constant: objective.Constant.ToDouble(),
                IdentityId: objective.Id?.Value,
                IdentityScope: ToRemoteIdentityScope(objective.IdentityScope),
                IdentityOriginKind: objective.IdentityOrigin?.Kind,
                IdentityOriginKey: objective.IdentityOrigin?.Key,
                IdentityNamespace: objective.IdentityNamespace,
                IdentitySchemaVersion: objective.IdentitySchemaVersion,
                IdentityProvenance: ToSerializedProvenance(
                    objective.IdentityProvenance,
                    objective.IdentityOrigin)),
            IdentityNamespace: constraints.IdentityNamespace,
            IdentitySchemaVersion: constraints.IdentitySchemaVersion));
    }

    /// <summary>
    /// 序列化为远程模型数据 / Serialize into remote model data.
    /// </summary>
    /// <remarks>
    /// ：线性模型包成 <c>ModelData.Linear</c>、二次模型包成 <c>ModelData.Quadratic</c>、
    /// CP snapshot 包成 <c>ModelData.Raw</c>（格式 <c>ospf-cp-snapshot-json</c>）。序列化失败原样上抛，
    /// 不产出空载荷。
    /// a linear model becomes <c>ModelData.Linear</c>, a quadratic model becomes
    /// <c>ModelData.Quadratic</c> and a CP snapshot becomes <c>ModelData.Raw</c> (format
    /// <c>ospf-cp-snapshot-json</c>). A serialization failure propagates unchanged instead of producing an
    /// empty payload.
    /// </remarks>
    /// <param name="model">线性三元模型视图 / Linear triad model view.</param>
    /// <returns>远程模型数据 / Remote model data.</returns>
    public static Result<ModelData, ErrorCode, Error<ErrorCode>> ModelData(
        ILinearTriadModelView model) {
        Result<SerializedLinearModel, ErrorCode, Error<ErrorCode>> serialized = Serialize(model);
        return serialized.IsOk
            ? Results.Ok(Domain.ModelData.Linear(serialized.Value))
            : Propagate<ModelData, SerializedLinearModel>(serialized);
    }

    /// <summary>
    /// 序列化为远程模型数据 / Serialize into remote model data.
    /// </summary>
    /// <param name="model">二次四元模型视图 / Quadratic tetrad model view.</param>
    /// <returns>远程模型数据 / Remote model data.</returns>
    public static Result<ModelData, ErrorCode, Error<ErrorCode>> ModelData(
        IQuadraticTetradModelView model) {
        Result<SerializedQuadraticModel, ErrorCode, Error<ErrorCode>> serialized = Serialize(model);
        return serialized.IsOk
            ? Results.Ok(Domain.ModelData.Quadratic(serialized.Value))
            : Propagate<ModelData, SerializedQuadraticModel>(serialized);
    }

    /// <summary>
    /// 序列化 CP snapshot 为远程模型数据 / Serialize a CP snapshot into remote model data.
    /// </summary>
    /// <param name="snapshot">不含 native 句柄的 CP snapshot / CP snapshot without native handles.</param>
    /// <returns>远程模型数据 / Remote model data.</returns>
    public static Result<ModelData, ErrorCode, Error<ErrorCode>> ModelData(
        ConstraintProgrammingModelSnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);
        Result<string, ErrorCode, Error<ErrorCode>> encoded =
            ConstraintProgrammingSnapshotCodec.Encode(snapshot);
        return encoded.IsOk
            ? Results.Ok(Domain.ModelData.Raw(
                bytes: System.Text.Encoding.UTF8.GetBytes(encoded.Value),
                format: "ospf-cp-snapshot-json"))
            : Propagate<ModelData, string>(encoded);
    }

    private static Result<TTarget, ErrorCode, Error<ErrorCode>> Propagate<TTarget, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> source) =>
        source switch {
            Failed<TSource, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<TTarget>(failed.Error),
            Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<TTarget, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<TTarget>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "远程模型序列化失败 / Remote model serialization failed")),
        };

    private static Result<SerializedQuadraticModel, ErrorCode, Error<ErrorCode>>
        InvalidQuadraticIdentity() =>
        Results.Failed<SerializedQuadraticModel>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "模型元素身份元数据无效，不能序列化远程模型 / "
            + "Model element identity metadata is invalid; remote serialization is rejected"));

    /// <summary>
    /// 身份元数据是否可用于远程序列化 / Whether the identity metadata may be serialized remotely.
    /// </summary>
    /// <remarks>
    /// 规则逐条照搬：
    /// - 标识空白、origin 的 kind／key 空白、任一 provenance 的 kind／key 空白 → 无效。
    /// - 存在 origin 且 provenance 非空，但 origin **不在** provenance 里 → 无效（两者自相矛盾）。
    /// - <c>ModelLocal</c>：必须无 origin、无 provenance，且标识不得以 <c>artifact:</c> 开头——
    ///   否则一个"模型内部"元素就冒充了可跨模型追认的身份。
    /// - <c>Stable</c>：必须有非空标识、不得以 <c>model-local-</c> 开头、<c>artifact:</c> 开头的标识必须是
    ///   该类别下由框架生成的 artifact 标识、命名空间与 schema 版本都非空、并且存在自称或来源记录。
    ///
    /// The code rules are implemented one by one: blank ids, blank origin kind/key and any blank
    /// provenance kind/key are invalid; an origin that contradicts a non-empty provenance is invalid;
    /// <c>ModelLocal</c> must carry neither origin nor provenance and may not use an <c>artifact:</c> id
    /// (otherwise a model-internal element masquerades as cross-model identity); <c>Stable</c> requires a
    /// non-blank id that neither starts with <c>model-local-</c> nor borrows an <c>artifact:</c> id outside
    /// its kind, plus a namespace, a schema version and at least one self-declared or recorded origin.
    /// </remarks>
    /// <param name="kind">元素类别 / Element kind.</param>
    /// <param name="id">元素标识 / Element identifier.</param>
    /// <param name="scope">身份作用域 / Identity scope.</param>
    /// <param name="origin">自称来源 / Self-declared origin.</param>
    /// <param name="provenance">来源记录 / Origin provenance.</param>
    /// <param name="identityNamespace">身份命名空间 / Identity namespace.</param>
    /// <param name="identitySchemaVersion">身份 schema 版本 / Identity schema version.</param>
    /// <returns>是否有效 / Whether the identity is valid.</returns>
    public static bool IsValidRemoteIdentity(
        ModelElementKind kind,
        string? id,
        ModelElementScope scope,
        ModelElementOrigin? origin,
        IReadOnlyList<ModelElementOrigin>? provenance,
        string? identityNamespace,
        string? identitySchemaVersion) {
        provenance ??= Array.Empty<ModelElementOrigin>();

        if (id is not null && string.IsNullOrWhiteSpace(id)) {
            return false;
        }
        if (origin is not null &&
            (string.IsNullOrWhiteSpace(origin.Kind) || string.IsNullOrWhiteSpace(origin.Key))) {
            return false;
        }
        foreach (ModelElementOrigin entry in provenance) {
            if (string.IsNullOrWhiteSpace(entry.Kind) || string.IsNullOrWhiteSpace(entry.Key)) {
                return false;
            }
        }
        if (origin is not null && provenance.Count > 0 && !Contains(provenance, origin)) {
            // 自称来源与来源记录互相矛盾，不能只信其中一个。
            // The self-declared origin contradicts the recorded provenance; neither may be trusted alone.
            return false;
        }

        IReadOnlyList<ModelElementOrigin> completeProvenance = provenance.Count == 0
            ? origin is null
                ? Array.Empty<ModelElementOrigin>()
                : new[] { origin }
            : provenance;

        return scope switch {
            ModelElementScope.ModelLocal =>
                origin is null &&
                provenance.Count == 0 &&
                id?.StartsWith("artifact:", StringComparison.Ordinal) != true,
            ModelElementScope.Stable =>
                !string.IsNullOrWhiteSpace(id) &&
                !id!.StartsWith("model-local-", StringComparison.Ordinal) &&
                (!id.StartsWith("artifact:", StringComparison.Ordinal) ||
                 DerivedModelElementIdentity.IsGeneratedArtifactModelElementId(id, kind)) &&
                !string.IsNullOrWhiteSpace(identityNamespace) &&
                !string.IsNullOrWhiteSpace(identitySchemaVersion) &&
                completeProvenance.Count > 0 &&
                (origin is null || Contains(completeProvenance, origin)),
            _ => false,
        };
    }

    private static bool Contains(
        IReadOnlyList<ModelElementOrigin> provenance,
        ModelElementOrigin origin) {
        foreach (ModelElementOrigin entry in provenance) {
            if (string.Equals(entry.Kind, origin.Kind, StringComparison.Ordinal) &&
                string.Equals(entry.Key, origin.Key, StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }
}
