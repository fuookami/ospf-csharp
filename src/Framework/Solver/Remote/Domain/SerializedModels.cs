#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Solver.Remote.Domain;
/// <summary>序列化变量类型 / Serialized variable type.</summary>
public enum SerializedVariableType { Binary, Integer, Continuous }

/// <summary>序列化约束符号 / Serialized constraint sign.</summary>
public enum SerializedConstraintSign { LessEqual, Equal, GreaterEqual }

/// <summary>序列化目标类别 / Serialized objective category.</summary>
public enum SerializedObjectiveCategory { Minimize, Maximize }

/// <summary>序列化模型元素来源 / Serialized model-element origin.</summary>
/// <param name="Kind">来源类型 / Origin kind.</param>
/// <param name="Key">来源键 / Origin key.</param>
public sealed record SerializedModelElementOrigin(string Kind, string Key);

/// <summary>序列化约束单元 / Serialized constraint cell.</summary>
public sealed record SerializedConstraintCell(int VariableIndex, double Coefficient);

/// <summary>序列化目标单元 / Serialized objective cell.</summary>
public sealed record SerializedObjectiveCell(int VariableIndex, double Coefficient);

/// <summary>序列化变量 / Serialized variable.</summary>
public sealed record SerializedVariable(
    string Name,
    SerializedVariableType Type,
    double LowerBound,
    double UpperBound);

/// <summary>序列化约束 / Serialized constraint.</summary>
/// <remarks>
/// 身份字段与逐字一致：<c>identityScope</c> 默认 <c>MODEL_LOCAL</c>（字符串枚举代码），
/// <c>identityProvenance</c> 默认空列表。缺少这些字段时远端会丢失元素身份溯源。
/// Identity fields match the reference verbatim: <c>identityScope</c> defaults to
/// <c>MODEL_LOCAL</c> (a string enum code) and <c>identityProvenance</c> defaults to an empty list.
/// Without them the remote side loses element-identity provenance.
/// </remarks>
/// <param name="Name">约束名称 / Constraint name.</param>
/// <param name="Sign">关系符 / Constraint relation.</param>
/// <param name="Rhs">右端项 / Right-hand side.</param>
/// <param name="Cells">约束单元 / Constraint cells.</param>
/// <param name="IdentityId">稳定身份标识 / Stable identity identifier.</param>
/// <param name="IdentityScope">身份作用域代码 / Identity scope code.</param>
/// <param name="IdentityOriginKind">来源类型 / Origin kind.</param>
/// <param name="IdentityOriginKey">来源键 / Origin key.</param>
/// <param name="IdentityNamespace">身份命名空间 / Identity namespace.</param>
/// <param name="IdentitySchemaVersion">身份 schema 版本 / Identity schema version.</param>
/// <param name="IdentityProvenance">来源记录 / Origin provenance.</param>
public sealed record SerializedConstraint(
    string Name,
    SerializedConstraintSign Sign,
    double Rhs,
    List<SerializedConstraintCell> Cells,
    string? IdentityId = null,
    string IdentityScope = "MODEL_LOCAL",
    string? IdentityOriginKind = null,
    string? IdentityOriginKey = null,
    string? IdentityNamespace = null,
    string? IdentitySchemaVersion = null,
    List<SerializedModelElementOrigin>? IdentityProvenance = null);

/// <summary>序列化目标 / Serialized objective.</summary>
/// <param name="Category">目标类别 / Objective category.</param>
/// <param name="Cells">目标单元 / Objective cells.</param>
/// <param name="Constant">常数项 / Constant term.</param>
/// <param name="IdentityId">稳定身份标识 / Stable identity identifier.</param>
/// <param name="IdentityScope">身份作用域代码 / Identity scope code.</param>
/// <param name="IdentityOriginKind">来源类型 / Origin kind.</param>
/// <param name="IdentityOriginKey">来源键 / Origin key.</param>
/// <param name="IdentityNamespace">身份命名空间 / Identity namespace.</param>
/// <param name="IdentitySchemaVersion">身份 schema 版本 / Identity schema version.</param>
/// <param name="IdentityProvenance">来源记录 / Origin provenance.</param>
public sealed record SerializedObjective(
    SerializedObjectiveCategory Category,
    List<SerializedObjectiveCell> Cells,
    double Constant = 0.0,
    string? IdentityId = null,
    string IdentityScope = "MODEL_LOCAL",
    string? IdentityOriginKind = null,
    string? IdentityOriginKey = null,
    string? IdentityNamespace = null,
    string? IdentitySchemaVersion = null,
    List<SerializedModelElementOrigin>? IdentityProvenance = null);

/// <summary>序列化线性模型 / Serialized linear model.</summary>
/// <param name="Name">模型名称 / Model name.</param>
/// <param name="Variables">变量列表 / Variables.</param>
/// <param name="Constraints">约束列表 / Constraints.</param>
/// <param name="Objective">目标函数 / Objective.</param>
/// <param name="IdentityNamespace">身份命名空间 / Identity namespace.</param>
/// <param name="IdentitySchemaVersion">身份 schema 版本 / Identity schema version.</param>
public sealed record SerializedLinearModel(
    string Name,
    List<SerializedVariable> Variables,
    List<SerializedConstraint> Constraints,
    SerializedObjective Objective,
    string? IdentityNamespace = null,
    string? IdentitySchemaVersion = null);

/// <summary>序列化二次约束单元 / Serialized quadratic constraint cell.</summary>
/// <remarks>
/// 与一致：一个二次单元需要**两个**列索引（<c>colIndex1</c>／<c>colIndex2</c>）。此前
/// C# 只有一个 <c>ColIndex</c>，根本无法表达二次项，与wire 形状不符。
/// Matches the reference: a quadratic cell needs **two** column indices
/// (<c>colIndex1</c>/<c>colIndex2</c>). C# previously had a single <c>ColIndex</c>, which cannot express a
/// quadratic term at all and did not match the implementation wire shape.
/// </remarks>
/// <param name="RowIndex">行索引 / Row index.</param>
/// <param name="ColIndex1">第一个列索引 / First column index.</param>
/// <param name="ColIndex2">第二个列索引 / Second column index.</param>
/// <param name="Coefficient">系数 / Coefficient.</param>
public sealed record SerializedQuadraticConstraintCell(
    int RowIndex,
    int ColIndex1,
    int ColIndex2,
    double Coefficient);

/// <summary>序列化二次约束 / Serialized quadratic constraint.</summary>
/// <param name="Name">约束名称 / Constraint name.</param>
/// <param name="Sign">关系符 / Constraint relation.</param>
/// <param name="Rhs">右端项 / Right-hand side.</param>
/// <param name="LinearCells">线性单元 / Linear cells.</param>
/// <param name="QuadraticCells">二次单元 / Quadratic cells.</param>
/// <param name="IdentityId">稳定身份标识 / Stable identity identifier.</param>
/// <param name="IdentityScope">身份作用域代码 / Identity scope code.</param>
/// <param name="IdentityOriginKind">来源类型 / Origin kind.</param>
/// <param name="IdentityOriginKey">来源键 / Origin key.</param>
/// <param name="IdentityNamespace">身份命名空间 / Identity namespace.</param>
/// <param name="IdentitySchemaVersion">身份 schema 版本 / Identity schema version.</param>
/// <param name="IdentityProvenance">来源记录 / Origin provenance.</param>
public sealed record SerializedQuadraticConstraint(
    string Name,
    SerializedConstraintSign Sign,
    double Rhs,
    List<SerializedConstraintCell> LinearCells,
    List<SerializedQuadraticConstraintCell> QuadraticCells,
    string? IdentityId = null,
    string IdentityScope = "MODEL_LOCAL",
    string? IdentityOriginKind = null,
    string? IdentityOriginKey = null,
    string? IdentityNamespace = null,
    string? IdentitySchemaVersion = null,
    List<SerializedModelElementOrigin>? IdentityProvenance = null);

/// <summary>序列化二次目标单元 / Serialized quadratic objective cell.</summary>
/// <remarks>
/// ：目标单元只需两个列索引，**没有**行索引——目标是单行结构。
/// An objective cell carries two column indices and **no** row index, because the
/// objective is a single row.
/// </remarks>
/// <param name="ColIndex1">第一个列索引 / First column index.</param>
/// <param name="ColIndex2">第二个列索引 / Second column index.</param>
/// <param name="Coefficient">系数 / Coefficient.</param>
public sealed record SerializedQuadraticObjectiveCell(
    int ColIndex1,
    int ColIndex2,
    double Coefficient);

/// <summary>序列化二次目标 / Serialized quadratic objective.</summary>
/// <param name="Category">目标类别 / Objective category.</param>
/// <param name="LinearCells">线性目标单元 / Linear objective cells.</param>
/// <param name="QuadraticCells">二次目标单元 / Quadratic objective cells.</param>
/// <param name="Constant">常数项 / Constant term.</param>
/// <param name="IdentityId">稳定身份标识 / Stable identity identifier.</param>
/// <param name="IdentityScope">身份作用域代码 / Identity scope code.</param>
/// <param name="IdentityOriginKind">来源类型 / Origin kind.</param>
/// <param name="IdentityOriginKey">来源键 / Origin key.</param>
/// <param name="IdentityNamespace">身份命名空间 / Identity namespace.</param>
/// <param name="IdentitySchemaVersion">身份 schema 版本 / Identity schema version.</param>
/// <param name="IdentityProvenance">来源记录 / Origin provenance.</param>
public sealed record SerializedQuadraticObjective(
    SerializedObjectiveCategory Category,
    List<SerializedObjectiveCell> LinearCells,
    List<SerializedQuadraticObjectiveCell> QuadraticCells,
    double Constant = 0.0,
    string? IdentityId = null,
    string IdentityScope = "MODEL_LOCAL",
    string? IdentityOriginKind = null,
    string? IdentityOriginKey = null,
    string? IdentityNamespace = null,
    string? IdentitySchemaVersion = null,
    List<SerializedModelElementOrigin>? IdentityProvenance = null);

/// <summary>序列化二次模型 / Serialized quadratic model.</summary>
/// <remarks>
/// 与一致：线性约束与二次约束是**两个独立列表**，不是合并后的一个列表——两者的单元
/// 结构不同，合并会让线性约束丢失其本来的形状。
/// Matches the reference: linear and quadratic constraints are **two separate lists**, not one
/// merged list — their cell shapes differ, and merging loses the linear constraints' original shape.
/// </remarks>
/// <param name="Name">模型名称 / Model name.</param>
/// <param name="Variables">变量列表 / Variables.</param>
/// <param name="LinearConstraints">线性约束 / Linear constraints.</param>
/// <param name="QuadraticConstraints">二次约束 / Quadratic constraints.</param>
/// <param name="Objective">目标函数 / Objective.</param>
/// <param name="IdentityNamespace">身份命名空间 / Identity namespace.</param>
/// <param name="IdentitySchemaVersion">身份 schema 版本 / Identity schema version.</param>
public sealed record SerializedQuadraticModel(
    string Name,
    List<SerializedVariable> Variables,
    List<SerializedConstraint> LinearConstraints,
    List<SerializedQuadraticConstraint> QuadraticConstraints,
    SerializedQuadraticObjective Objective,
    string? IdentityNamespace = null,
    string? IdentitySchemaVersion = null);
