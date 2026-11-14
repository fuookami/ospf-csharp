#nullable enable

using Fuookami.Ospf.Core.Solver.Function;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Fuookami.Ospf.Core.Plugin.Copt;

/// <summary>
/// COPT 的原生能力探测 / Native capability detection for COPT.
/// </summary>
/// <remarks>
/// B1234 按基线 `CoptFunctionCapabilities.kt`（30 行）补，与 B855 起的 `GurobiFunctionCapabilities` 同源。
///
/// **这一批的实际动机不是"补一个探测函数"，而是把 B1232／B1233 留下的那层区分写进用户能读到的地方。**
///
/// **那层区分是：** **PWL 的写侧已实现（我们的代码会调用 `addSos`），而"COPT 真的接受了它"从未实测。**
///
/// **`Detect` 回答的正好是前者**：**它问的是"这个 SDK 有没有这个签名"，
/// 不是"我们写进去的那条约束有没有被求解器采纳"。**
/// **两者相差一次真跑，而这一批不声称跨过它。**
///
/// **B1234 follows the baseline's 30-line `CoptFunctionCapabilities.kt`, the same family as `GurobiFunctionCapabilities`
/// since B855.**
///
/// **The real motive is not "add a probe function" but to put the distinction B1232/B1233 left behind somewhere the user
/// can read it.**
///
/// **That distinction: PWL's write side is implemented (our code calls `addSos`), while "COPT really accepted it" has never
/// been measured.**
///
/// **`Detect` answers exactly the former: it asks whether <b>this SDK has that signature</b>, not whether <b>the constraint
/// we wrote was adopted by the solver</b>.** **The two differ by one real run, and this batch does not claim to cross
/// it.**
/// </remarks>
public static class CoptFunctionCapabilities
{
    /// <summary>求解器名 / The solver name.</summary>
    public const string SolverName = "COPT";

    /// <summary>
    /// **探测 COPT 模型类型暴露的原生接口** / Probe the native interfaces the COPT model type exposes.
    /// </summary>
    /// <remarks>
    /// **只探测一条：`AddSos(Var[], double[], int)` 返回 `Sos`。**
    /// **基线在这一点上比 Gurobi 那一侧<b>严格</b>：它校验返回类型与参数类型，
    /// 而 `GurobiFunctionCapabilities` 只校验名字（那里的 `ponytail:` 天花板）。**
    ///
    /// **本实现照基线的严格程度做**：**名字 + 三个参数 + 返回类型。**
    /// **理由：这里的判断决定"我们的 PWL 写入会不会在运行时抛 `MissingMethodException`"，
    /// 而一个同名但签名不同的方法会让那个判断出错。**
    ///
    /// **One capability only: `AddSos(Var[], double[], int)` returning `Sos`.** **The baseline is <b>stricter</b> here than
    /// on the Gurobi side: it checks return and parameter types, while `GurobiFunctionCapabilities` checks names only (the
    /// `ponytail:` ceiling there).**
    ///
    /// **This implementation matches the baseline's strictness: name + three parameter types + return type.** **Because this
    /// decision governs whether our PWL write throws `MissingMethodException` at run time, and a same-named method with a
    /// different signature would make that decision wrong.**
    /// </remarks>
    /// <param name="modelType">
    /// 待检查的类型；`null` **明确表示没有 SDK**，此时一项都不声明。
    /// The type to inspect; `null` **explicitly means no SDK**, in which case nothing is claimed.
    /// </param>
    /// <param name="version">SDK 版本；`null` 表示未探测 / SDK version; `null` means not probed.</param>
    /// <returns>能力声明 / The capability declaration.</returns>
    public static FunctionSolverCapabilities Detect(Type? modelType = null, string? version = null)
    {
        if (modelType is null)
        {
            // **没有 SDK 时"支持 0 项"与"支持 0 项、版本 ''"是两件事，此处保留 `null`。**
            // **With no SDK, "supports 0" and "supports 0, version ''" are different; `null` is preserved here.**
            return new FunctionSolverCapabilities(SolverName, Version: null, Supported: new HashSet<FunctionNativeCapability>());
        }

        var supported = new HashSet<FunctionNativeCapability>();
        if (HasSos2Creator(modelType))
        {
            // **一个方法同时支撑 SOS1 与 SOS2（与基线一致）：`addSos` 的 type 参数决定 1 还是 2。**
            // **One method backs SOS1 and SOS2 alike (matching the baseline): the type argument picks 1 or 2.**
            supported.Add(FunctionNativeCapability.Sos1);
            supported.Add(FunctionNativeCapability.Sos2);
            // **PWL 的约束侧依赖 SOS2，所以这里一并声明。**
            // **PWL's constraint side depends on SOS2, so it is declared here too.**
            supported.Add(FunctionNativeCapability.PiecewiseLinearConstraint);
        }

        return new FunctionSolverCapabilities(SolverName, version, supported);
    }

    /// <summary>
    /// **按名字 + 参数类型 + 返回类型找 `AddSos`** /
    /// Find `AddSos` by name, parameter types and return type.
    /// </summary>
    /// <remarks>
    /// **`GetMethod` 只返回签名完全匹配的那一个，不返回"名字像"的那一个**——
    /// 这正是它与 `HasMethod` 的区别，也是基线选择它的理由。
    ///
    /// **三个参数类型从 SDK 自己的类型取，而不是硬编码 `typeof(Array)`：**
    /// **`AddSos` 有三个重载（`SosBuilder`、`Var[]`、`VarArray`），只按名字会命中任意一个。**
    ///
    /// **`GetMethod` returns only the exact signature, never a look-alike name** — that is precisely how it differs from
    /// `HasMethod` and why the baseline chose it.
    ///
    /// **The three parameter types come from the SDK's own types rather than a hard-coded `typeof(Array)`: `AddSos` has
    /// three overloads (`SosBuilder`, `Var[]`, `VarArray`), and matching by name alone would hit any of them.**
    /// </remarks>
    private static bool HasSos2Creator(Type modelType)
    {
        try
        {
            // **`Var[]` 的运行时类型要用 `MakeArrayType` 现算，因为 SDK 可能不在。**
            // **The runtime type of `Var[]` is computed with `MakeArrayType` because the SDK may be absent.**
            Type? varType = modelType.Assembly.GetType("Copt.Var", throwOnError: false);
            Type? sosType = modelType.Assembly.GetType("Copt.Sos", throwOnError: false);
            if (varType is null || sosType is null)
            {
                return false;
            }

            MethodInfo? method = modelType.GetMethod(
                "AddSos",
                [varType.MakeArrayType(), typeof(double[]), typeof(int)]);
            return method is not null && method.ReturnType == sosType;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            // **反射失败一律读作"没有这项能力"，而不是让探测本身抛出去。**
            // **A reflection failure reads as "the capability is absent" rather than throwing out of detection.**
            return false;
        }
    }
}
