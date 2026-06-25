#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Fuookami.Ospf.Framework.Solver.Remote.Domain;
/// <summary>任务标识 / Task identifier.</summary>
public readonly record struct TaskId(string Value) { public override string ToString() => Value; }

/// <summary>切片标识 / Slice identifier.</summary>
public readonly record struct SliceId(string Value) { public override string ToString() => Value; }

/// <summary>节点标识 / Node identifier.</summary>
public readonly record struct NodeId(string Value) { public override string ToString() => Value; }

/// <summary>租户标识 / Tenant identifier.</summary>
public readonly record struct TenantId(string Value) { public override string ToString() => Value; }

/// <summary>请求标识 / Request identifier.</summary>
public readonly record struct RequestId(string Value) { public override string ToString() => Value; }

/// <summary>句柄标识 / Handle identifier.</summary>
public readonly record struct HandleId(string Value) { public override string ToString() => Value; }

/// <summary>追踪标识 / Trace identifier.</summary>
public readonly record struct TraceId(string Value) { public override string ToString() => Value; }

/// <summary>对象路径 / Object path.</summary>
public readonly record struct ObjectPath(string Value) { public override string ToString() => Value; }

/// <summary>对象版本 / Object version.</summary>
public readonly record struct ObjectVersion(string Value) { public override string ToString() => Value; }

/// <summary>对象 ETag / Object ETag.</summary>
public readonly record struct ObjectEtag(string Value) { public override string ToString() => Value; }

/// <summary>求解器类型名称 / Solver type name.</summary>
public readonly record struct SolverTypeName(string Value) { public override string ToString() => Value; }

/// <summary>目标类型名称 / Target type name.</summary>
public readonly record struct TargetTypeName(string Value) { public override string ToString() => Value; }

/// <summary>预算范围标识 / Budget scope identifier.</summary>
public readonly record struct BudgetScopeId(string Value) { public override string ToString() => Value; }

/// <summary>操作员标识 / Operator identifier.</summary>
public readonly record struct OperatorId(string Value) { public override string ToString() => Value; }

/// <summary>操作来源 / Operation source.</summary>
public readonly record struct OperationSource(string Value) { public override string ToString() => Value; }

/// <summary>原因代码 / Reason code.</summary>
public readonly record struct ReasonCode(string Value) { public override string ToString() => Value; }
