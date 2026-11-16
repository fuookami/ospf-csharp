#nullable enable

using CplexSdk = ILOG.CPLEX;
using ConcertSdk = ILOG.Concert;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Plugin.Cplex;

/// <summary>
/// CPLEX 原生分段线性写入 / Native piecewise-linear writing for CPLEX.
/// </summary>
/// <remarks>
/// 对齐基线 `CplexNativePiecewise.kt`（135 行）补写侧，与 COPT 同形。
///
/// **写出来的东西完全一样**（λ 组、`Σλ=1`、`x`／`y` 两条 linking、`SOS2`），
/// **而校验比 COPT 多两条。这不是疏忽，是<b>后端之间真实的差别</b>：**
///
/// | 校验 | COPT | CPLEX（这里） |
/// | --- | --- | --- |
/// | 断点数 ≥ 2 且与函数值等长 | ✅ | ✅ |
/// | 输入／结果键不同、结果键不重复 | ✅ | ✅ |
/// | 两个键都已映射 | ✅ | ✅ |
/// | 无穷哨兵有效 | ✅ | **无**（CPLEX 不用哨兵） |
/// | **非有限值** | **无** | ✅ |
/// | **断点严格递增** | **无** | ✅ |
///
/// **"两个后端的写入器结构相同"很容易被读成"校验也应该相同"，而实际上每一条校验都在回答
/// 那个后端<b>特有的</b>失败方式：** **COPT 需要哨兵是因为它的 λ 界由哨兵推导；
/// CPLEX 不需要哨兵，但它会直接接受非有限的断点，从而写出一行 NaN——那是 COPT 侧不会发生的失败。**
///
/// **所以合并这两个写入器是错的：它们的<b>形状</b>一样，<b>防御</b>不一样。**
///
/// **What gets written is identical (λ group, `Σλ=1`, `x`/`y` linking rows, `SOS2`), while validation carries two more
/// checks than COPT. That is not an oversight but a <b>real difference between the backends</b>.**
///
/// **"The two writers have the same shape" reads easily as "the checks should match too", yet each check answers a
/// failure mode <b>peculiar</b> to that backend: COPT needs the sentinel because its λ bounds derive from it; CPLEX needs
/// none, but it will happily accept a non-finite breakpoint and write a NaN row — a failure COPT cannot have.**
///
/// **So merging these two writers would be wrong: the <b>shape</b> matches, the <b>defences</b> do not.**
/// </remarks>
internal static class CplexNativePiecewise
{
    /// <summary>
    /// **前置校验：在写入任何东西之前跑完一整遍** / Validate up front, before anything is written.
    /// </summary>
    /// <remarks>
    /// **五条，全部来自基线，且顺序也照抄**（先数结构、再查数值、再查递增、再查映射、最后查重复）。
    ///
    /// **"断点严格递增"这一条尤其值得单独看：** **它不是"数据好看"的要求，而是 SOS2 的<b>前提</b>。**
    /// **SOS2 说"至多两个相邻 λ 非零"，而"相邻"是由断点数组的<b>下标</b>定义的；
    /// 一组乱序断点会让"相邻"指向错误的两个点，于是插值在错误的区间上进行——模型可解，折线是错的。**
    ///
    /// **Five checks, all from the baseline and in the same order: structure, finiteness, monotonicity, mapping,
    /// duplicates.**
    ///
    /// **"Strictly increasing" deserves its own note: it is not a tidiness requirement but a <b>precondition of
    /// SOS2</b>.** **SOS2 says "at most two neighbouring λ are non-zero", and "neighbouring" is defined by the
    /// <b>indices</b> of the breakpoint array; unordered breakpoints make "neighbouring" name the wrong two points, so
    /// interpolation happens over the wrong interval — the model solves, the polyline is wrong.**
    /// </remarks>
    /// <param name="variableKeys">已映射的变量键 / The mapped variable keys.</param>
    /// <param name="data">待写入的 PWL 数据 / The PWL data to write.</param>
    /// <returns>成功或错误 / Success or an error.</returns>
    internal static Try Validate(
        IReadOnlyCollection<VariableItemKey> variableKeys,
        IReadOnlyList<NativePiecewiseData> data) {
        var resultKeys = new HashSet<VariableItemKey>();
        for (int index = 0; index < data.Count; ++index) {
            NativePiecewiseData piecewise = data[index];
            if (piecewise.XPoints.Length < 2 || piecewise.YPoints.Length != piecewise.XPoints.Length) {
                return Invalid("断点和输出点数量不匹配 / breakpoint and output-point counts differ");
            }
            if (piecewise.XPoints.Any(static point => !double.IsFinite(point))
                || piecewise.YPoints.Any(static point => !double.IsFinite(point))) {
                return Invalid("存在非有限数值 / a non-finite value is present");
            }
            for (int point = 1; point < piecewise.XPoints.Length; ++point) {
                if (piecewise.XPoints[point - 1] >= piecewise.XPoints[point]) {
                    return Invalid("断点必须严格递增 / breakpoints must be strictly increasing");
                }
            }
            if (!variableKeys.Contains(piecewise.InputKey) || !variableKeys.Contains(piecewise.ResultKey)) {
                return Invalid("输入或结果变量缺失 / input or result variable is missing");
            }
            if (!resultKeys.Add(piecewise.ResultKey)) {
                return Invalid("结果变量重复 / result variable is duplicated");
            }
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// **把每条 PWL 写成 λ 组 + 三条等式 + 一条 SOS2** /
    /// Write each PWL as a λ group, three equalities and one SOS2.
    /// </summary>
    /// <param name="model">CPLEX 模型 / The CPLEX model.</param>
    /// <param name="variables">变量键到 CPLEX 变量的映射 / Variable key to CPLEX variable.</param>
    /// <param name="data">PWL 数据 / The PWL data.</param>
    /// <returns>成功或错误 / Success or an error.</returns>
    internal static Try Add(
        CplexSdk.Cplex model,
        IReadOnlyDictionary<VariableItemKey, ConcertSdk.INumVar> variables,
        IReadOnlyList<NativePiecewiseData> data) {
        Try validation = Validate(variables.Keys.ToArray(), data);
        if (validation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return validation;
        }

        for (int piecewiseIndex = 0; piecewiseIndex < data.Count; ++piecewiseIndex) {
            NativePiecewiseData piecewise = data[piecewiseIndex];
            if (!variables.TryGetValue(piecewise.InputKey, out ConcertSdk.INumVar? input)
                || !variables.TryGetValue(piecewise.ResultKey, out ConcertSdk.INumVar? result)) {
                return Invalid("输入或结果变量缺失 / input or result variable is missing");
            }

            int count = piecewise.XPoints.Length;
            string[] lambdaNames = new string[count];
            for (int index = 0; index < count; ++index) {
                lambdaNames[index] = $"{piecewise.Name}_lambda_{index}";
            }

            try {
                ConcertSdk.INumVar[] lambda = model.NumVarArray(
                    count,
                    0.0,
                    1.0,
                    ConcertSdk.NumVarType.Float,
                    lambdaNames);

                ConcertSdk.ILinearNumExpr sum = model.LinearNumExpr();
                ConcertSdk.ILinearNumExpr xLink = model.LinearNumExpr();
                ConcertSdk.ILinearNumExpr yLink = model.LinearNumExpr();
                for (int index = 0; index < count; ++index) {
                    sum.AddTerm(1.0, lambda[index]);
                    xLink.AddTerm(piecewise.XPoints[index], lambda[index]);
                    yLink.AddTerm(piecewise.YPoints[index], lambda[index]);
                }

                model.AddEq(sum, 1.0, $"{piecewise.Name}_lambda_sum");
                model.AddEq(input, xLink, $"{piecewise.Name}_x_link");
                model.AddEq(result, yLink, $"{piecewise.Name}_y_link");

                // **SOS2：至多两个相邻 λ 非零。缺了它，上面的等式描述的是断点的凸包，不是折线。**
                // **SOS2: at most two neighbouring λ non-zero. Without it the equalities above describe the convex hull
                // of the breakpoints, not the polyline.**
                model.AddSOS2(lambda, piecewise.XPoints, $"{piecewise.Name}_sos2");
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.OREngineModelingException,
                    $"CPLEX PWL 写入失败：{error.Message} / CPLEX PWL write failed"));
            }
        }

        return Results.OkInstance;
    }

    /// <summary>**构造一条非法参数的失败** / Build an invalid-argument failure.</summary>
    private static Try Invalid(string reason) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, reason));
}
