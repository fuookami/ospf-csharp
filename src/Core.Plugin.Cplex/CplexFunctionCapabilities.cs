#nullable enable

using Fuookami.Ospf.Core.Solver.Function;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Fuookami.Ospf.Core.Plugin.Cplex;

/// <summary>
/// CPLEX 的原生能力探测 / Native capability detection for CPLEX.
/// </summary>
/// <remarks>
/// 对齐基线 `CplexFunctionCapabilities.kt`（44 行）补，与同批的 `CoptFunctionCapabilities` 同源。
///
/// **CPLEX 的探测与 COPT 有一处<b>实质</b>差别，而它不是笔误：**
///
/// | | COPT | CPLEX |
/// | --- | --- | --- |
/// | 探测的方法名 | `AddSos` | **`AddSOS2`** |
/// | 重载数 | 三个 | 五个 |
/// | 返回类型 | `Sos` | `ISOS2` |
///
/// **CPLEX 有专用的 `AddSOS2`，所以它不需要 `type` 参数来区分 1 与 2；
/// 而它需要检查的是返回<b>接口</b>`ISOS2`，不是具体类。**
///
/// **"两个后端都支持 SOS2"这句话是对的，而"它们用同一个方法支持 SOS2"是错的——
/// 判据要能分开这两句话，否则"合写一个探测函数"会让其中一侧永远返回 `false`。**
///
/// Follows `CoptFunctionCapabilities`.
///
/// **CPLEX's probe differs from COPT's in one <b>substantive</b> way, and it is not a typo:** **the method name is
/// `AddSOS2` rather than `AddSos`, there are five overloads rather than three, and the return is the <b>interface</b>
/// `ISOS2` rather than the class `Sos`.**
///
/// **"Both backends support SOS2" is true; "they support it through the same method" is false — and a criterion has to
/// separate those two sentences, or "write one shared probe" would leave one side permanently false.**
/// </remarks>
public static class CplexFunctionCapabilities
{
    /// <summary>求解器名 / The solver name.</summary>
    public const string SolverName = "CPLEX";

    /// <summary>
    /// **探测 CPLEX 模型类型暴露的原生接口** / Probe the native interfaces the CPLEX model type exposes.
    /// </summary>
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
            return new FunctionSolverCapabilities(SolverName, Version: null, Supported: new HashSet<FunctionNativeCapability>());
        }

        var supported = new HashSet<FunctionNativeCapability>();
        if (HasSos2Creator(modelType))
        {
            // **CPLEX 的 `AddSOS2` <b>就是</b> SOS2，不是"用 type 参数选出来的 SOS2"，所以 SOS1 不在这里声明。**
            // **CPLEX's `AddSOS2` <b>is</b> SOS2 rather than a type-parameter choice, so SOS1 is not declared here.**
            supported.Add(FunctionNativeCapability.Sos2);
            supported.Add(FunctionNativeCapability.PiecewiseLinearConstraint);
        }

        return new FunctionSolverCapabilities(SolverName, version, supported);
    }

    /// <summary>
    /// **按名字 + 参数类型 + 返回接口找 `AddSOS2`** /
    /// Find `AddSOS2` by name, parameter types and return interface.
    /// </summary>
    /// <remarks>
    /// **三个参数：`INumVar[]`、`double[]`、`string`（命名重载）。**
    /// **`AddSOS2` 有五个重载，其中两个带 `(int, int)` 索引区间、三个不带——按名字会命中任意一个。**
    ///
    /// **返回类型检查用 `IsAssignableFrom` 而不是 `==`：** **`ISOS2` 是接口，
    /// 实现的返回类型可能是它本身也可能是派生接口；用相等比较会在派生实现上误判为 `false`。**
    ///
    /// **Three parameters: `INumVar[]`, `double[]`, `string` (the named overload).** **`AddSOS2` has five overloads, two
    /// with an `(int, int)` index range and three without — matching by name would hit any of them.**
    ///
    /// **The return check uses `IsAssignableFrom` rather than `==`: `ISOS2` is an interface, so an implementation may
    /// return it or a derived interface, and equality would misread a derived implementation as `false`.**
    /// </remarks>
    private static bool HasSos2Creator(Type modelType)
    {
        try
        {
            Type? numVarType = modelType.Assembly.GetType("ILOG.Concert.INumVar", throwOnError: false)
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(static assembly => assembly.GetType("ILOG.Concert.INumVar", throwOnError: false))
                    .FirstOrDefault(static type => type is not null);
            Type? sosType = modelType.Assembly.GetType("ILOG.Concert.ISOS2", throwOnError: false)
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(static assembly => assembly.GetType("ILOG.Concert.ISOS2", throwOnError: false))
                    .FirstOrDefault(static type => type is not null);
            if (numVarType is null || sosType is null)
            {
                return false;
            }

            MethodInfo? method = modelType.GetMethod(
                "AddSOS2",
                [numVarType.MakeArrayType(), typeof(double[]), typeof(string)]);
            return method is not null && sosType.IsAssignableFrom(method.ReturnType);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            return false;
        }
    }
}
