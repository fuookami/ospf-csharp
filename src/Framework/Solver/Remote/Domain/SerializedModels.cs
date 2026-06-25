#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Solver.Remote.Domain;
/// <summary>序列化变量类型 / Serialized variable type.</summary>
public enum SerializedVariableType { Binary, Integer, Continuous }

/// <summary>序列化约束符号 / Serialized constraint sign.</summary>
public enum SerializedConstraintSign { LessEqual, Equal, GreaterEqual }

/// <summary>序列化目标类别 / Serialized objective category.</summary>
public enum SerializedObjectiveCategory { Minimize, Maximize }

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
public sealed record SerializedConstraint(
    string Name,
    SerializedConstraintSign Sign,
    double Rhs,
    List<SerializedConstraintCell> Cells);

/// <summary>序列化目标 / Serialized objective.</summary>
public sealed record SerializedObjective(
    SerializedObjectiveCategory Category,
    List<SerializedObjectiveCell> Cells,
    double Constant = 0.0);

/// <summary>序列化线性模型 / Serialized linear model.</summary>
public sealed record SerializedLinearModel(
    string Name,
    List<SerializedVariable> Variables,
    List<SerializedConstraint> Constraints,
    SerializedObjective Objective);

/// <summary>序列化二次约束单元 / Serialized quadratic constraint cell.</summary>
public sealed record SerializedQuadraticConstraintCell(int RowIndex, int ColIndex, double Coefficient);

/// <summary>序列化二次约束 / Serialized quadratic constraint.</summary>
public sealed record SerializedQuadraticConstraint(
    string Name,
    SerializedConstraintSign Sign,
    double Rhs,
    List<SerializedConstraintCell> LinearCells,
    List<SerializedQuadraticConstraintCell> QuadraticCells);

/// <summary>序列化二次目标单元 / Serialized quadratic objective cell.</summary>
public sealed record SerializedQuadraticObjectiveCell(int RowIndex, int ColIndex, double Coefficient);

/// <summary>序列化二次目标 / Serialized quadratic objective.</summary>
public sealed record SerializedQuadraticObjective(
    SerializedObjectiveCategory Category,
    List<SerializedObjectiveCell> LinearCells,
    List<SerializedQuadraticObjectiveCell> QuadraticCells,
    double Constant = 0.0);

/// <summary>序列化二次模型 / Serialized quadratic model.</summary>
public sealed record SerializedQuadraticModel(
    string Name,
    List<SerializedVariable> Variables,
    List<SerializedQuadraticConstraint> Constraints,
    SerializedQuadraticObjective Objective);

/// <summary>序列化解 / Serialized solution.</summary>
public sealed record SerializedSolution(
    bool Feasible,
    double ObjectiveValue,
    List<double> VariableValues,
    double? Gap = null);
