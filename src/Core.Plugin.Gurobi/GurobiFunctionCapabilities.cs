#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fuookami.Ospf.Core.Solver.Function;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi 的函数接口能力探测 / Capability detection for Gurobi's function interfaces.
/// </summary>
/// <remarks>
/// 与基线 `GurobiFunctionCapabilities.kt`（3832 B）对应。基线在 **Java 侧用反射检查 `GRBModel` 的精确方法签名**，
/// 这里在 .NET 侧用同样的思路，但**不引用 Gurobi 托管程序集**——所以这段代码在**没有 SDK 的机器上也能编译**，
/// 只是探测结果为空。
///
/// **没有 SDK 就不声明任何能力。** 这是本仓反复回到的一条：能力声明宁可少说。返回"支持 9 项"而其实一个都调不通，
/// 比返回"支持 0 项"危险得多——前者会让上层选择一条**不存在的原生路径**。
///
/// Mirrors the baseline's `GurobiFunctionCapabilities.kt`. The baseline reflects over `GRBModel`'s exact method signatures on the Java side; this does the same on the
/// .NET side but **without referencing the Gurobi managed assembly**, so this code compiles **on a machine with no SDK** — the result is simply empty.
///
/// **No SDK, no capability claims.** This is a rule this repository keeps returning to: a capability declaration may understate. Reporting "supports 9" when not one of
/// them can actually be called is far worse than reporting "supports 0" — the former lets the layer above pick **a native path that does not exist**.
/// </remarks>
public static class GurobiFunctionCapabilities
{
    /// <summary>求解器名 / The solver name.</summary>
    public const string SolverName = "gurobi";

    /// <summary>Gurobi SDK 里模型类型的全名 / The full name of the SDK's model type.</summary>
    private const string ModelTypeName = "Gurobi.GRBModel";

    /// <summary>
    /// 探测当前链接的 Gurobi SDK 暴露的函数接口 / Detect the function interfaces the linked Gurobi SDK exposes.
    /// </summary>
    /// <param name="modelType">
    /// 待检查的类型；`null` **明确表示没有 SDK**，此时一项都不声明。**这里不去搜索程序集**——
    /// 搜索是 `DetectLoaded` 的事，把两件事合成一件会让"没有 SDK"这条路径**无法被单独判据**（B855 第一次变异就因此漏检）。
    /// The type to inspect; `null` **explicitly means no SDK**, in which case nothing is claimed. **This does not search assemblies** — that is `DetectLoaded`'s job,
    /// and merging the two would make the "no SDK" path **impossible to pin on its own** (B855's first mutation slipped through exactly there).
    /// </param>
    /// <param name="version">SDK 版本；`null` 表示未探测 / SDK version; `null` means not probed.</param>
    /// <returns>能力声明 / The capability declaration.</returns>
    /// <remarks>
    /// **方法名匹配，不是签名匹配**——这是与基线的一处**较弱证据**：基线校验返回类型与参数类型，这里只校验名字存在。
    /// 记成 `ponytail:` 天花板：升级路径是等托管程序集可用时（`GurobiAvailability` 为真）再检查参数类型。
    ///
    /// **By method name, not by signature** — a **weaker form of evidence** than the baseline, which checks return and parameter types. Recorded as a `ponytail:` ceiling:
    /// the upgrade path is to check parameter types once the managed assembly is guaranteed present.
    /// </remarks>
    public static FunctionSolverCapabilities Detect(Type? modelType = null, string? version = null)
    {
        if (modelType is null)
        {
            return new FunctionSolverCapabilities(SolverName, Version: null, Supported: Empty());
        }

        var supported = new HashSet<FunctionNativeCapability>();
        if (HasMethod(modelType, "SetPWLObj"))
        {
            supported.Add(FunctionNativeCapability.PiecewiseLinearObjective);
        }
        if (HasMethod(modelType, "AddGenConstrPWL"))
        {
            supported.Add(FunctionNativeCapability.PiecewiseLinearConstraint);
        }
        if (HasMethod(modelType, "AddSOS"))
        {
            // **一个方法同时支撑两种能力**（与基线一致）：`AddSOS` 的 `type` 参数决定 1 还是 2。
            // **One method backs two capabilities** (matching the baseline): `AddSOS`'s type argument picks 1 or 2.
            supported.Add(FunctionNativeCapability.Sos1);
            supported.Add(FunctionNativeCapability.Sos2);
        }
        if (HasMethod(modelType, "AddGenConstrIndicator"))
        {
            supported.Add(FunctionNativeCapability.Indicator);
        }
        if (HasMethod(modelType, "AddGenConstrAbs"))
        {
            supported.Add(FunctionNativeCapability.GeneralAbs);
        }
        if (HasMethod(modelType, "AddGenConstrMax") && HasMethod(modelType, "AddGenConstrMin"))
        {
            // **两个都要**（与基线一致）：只有一个说明这对接口不完整，声明"支持"会让上层调用不存在的那一半。
            // **Both are required** (matching the baseline): with only one the pair is incomplete, and claiming support would let the layer above call the missing half.
            supported.Add(FunctionNativeCapability.GeneralMinMax);
        }
        if (HasMethod(modelType, "AddGenConstrAnd") && HasMethod(modelType, "AddGenConstrOr"))
        {
            supported.Add(FunctionNativeCapability.BinaryLogic);
        }

        // ponytail: 名基于前缀匹配的探测器看不见参数类型, so a same-named method with the wrong arity would still count. Add exact parameter checks when the managed
        // assembly is referenceable unconditionally.
        return new FunctionSolverCapabilities(SolverName, Version: version, Supported: supported);
    }

    /// <summary>
    /// 探测**已加载**的 Gurobi SDK / Detect the Gurobi SDK as loaded in this process.
    /// </summary>
    /// <param name="version">SDK 版本；`null` 表示未探测 / SDK version; `null` means not probed.</param>
    /// <returns>能力声明 / The capability declaration.</returns>
    /// <remarks>
    /// ponytail: 这一层只是"找类型 + 交给 `Detect`"，所以它的判据**依赖本机装没装 Gurobi**，没有独立判据。
    /// 需要独立判据时，把它拆成"按名字取类型"与"按类型判能力"两步分别钉住。
    /// 
    /// ponytail: this layer only "finds the type and hands it to `Detect`", so any criterion for it **depends on whether this machine has Gurobi** and there is none.
    /// When one is needed, split it into "find the type by name" and "judge capabilities by type" and pin each.
    /// </remarks>
    public static FunctionSolverCapabilities DetectLoaded(string? version = null) =>
        Detect(FindModelType(), version);

    /// <summary>
    /// 在已加载的程序集里找 Gurobi 的模型类型 / Look for the SDK's model type among the loaded assemblies.
    /// </summary>
    /// <returns>找到的类型，或 `null` / The type, or `null`.</returns>
    private static Type? FindModelType()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? candidate;
            try
            {
                candidate = assembly.GetType(ModelTypeName, throwOnError: false);
            }
            catch (Exception)
            {
                // 某个程序集自省失败（反射禁止、加载错误等）不该让探测整体失败——那会让"没有 SDK"和"SDK 有问题"混成一件事。
                // A single assembly failing to introspect (reflection blocked, load error) must not sink the whole probe: that would merge "no SDK" with "a broken SDK".
                continue;
            }
            if (candidate is not null)
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>类型上是否有这个公开方法名 / Whether the type has a public method of this name.</summary>
    /// <param name="type">待检查类型 / The type to inspect.</param>
    /// <param name="name">方法名 / Method name.</param>
    /// <returns>是否存在 / Whether it exists.</returns>
    private static bool HasMethod(Type type, string name)
    {
        try
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.Instance).Any(
                method => string.Equals(method.Name, name, StringComparison.Ordinal));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>空集合 / The empty set.</summary>
    /// <returns>空集合 / The empty set.</returns>
    private static IReadOnlySet<FunctionNativeCapability> Empty() =>
        new HashSet<FunctionNativeCapability>();
}
