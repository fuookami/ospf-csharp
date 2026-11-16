#nullable enable

using CoptSdk = Copt;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Fuookami.Ospf.Core.Plugin.Copt;

/// <summary>
/// COPT 原生分段线性写入 / Native piecewise-linear writing for COPT.
/// </summary>
/// <remarks>
/// 对齐的 `validateCoptNativePiecewiseData` 与 `addCoptNativePiecewise`。
///
/// **这里补的是<b>写侧</b>：把 `x/y` 断点翻译成 COPT 模型里的 λ 变量组、三条行与一条 SOS2。**
/// **读侧（报告里的求值与指纹）早已有之。**
///
/// **"读得有、写不出"是一种容易被忽略的半成品：** **报告能列出 PWL 结构、能算指纹、能校验列，
/// 而求解器从来没收到过任何一条 PWL 约束——于是那些报告描述的是一份<b>从未生效</b>的模型。**
///
/// **这也是为什么它不报错：** **读侧全绿，因为它读的是自己的数据；写侧缺失不产生任何信号。**
///
/// **follows its `validateCoptNativePiecewiseData` and `addCoptNativePiecewise`.**
///
/// ** adds the <b>write</b> side: turning `x/y` breakpoints into COPT λ variables, three rows and one
/// SOS2 per structure.** **The read side (report evaluation and fingerprints) has existed since then.**
///
/// **"Readable but unwritable" is an easily missed half-finish: the report can list PWL structures, fingerprint them
/// and validate their columns, while the solver never received a single PWL constraint — so those reports describe a
/// model that <b>never took effect</b>.**
///
/// **That is also why it stays silent: the read side is green because it reads its own data, and a missing write side
/// emits no signal.**
/// </remarks>
internal static class CoptNativePiecewise
{
    /// <summary>
    /// **在写入任何东西之前批量校验所有 primitive 数据** /
    /// Validate every primitive datum before writing anything through the SDK.
    /// </summary>
    /// <remarks>
    /// **"在写入之前"这四个字是重点：** **中途失败会留下一半的模型改动，
    /// 而 COPT 的 `addVar` 没有回滚。** **所以校验必须是<b>前置</b>的一整遍，而不是边写边查。**
    ///
    /// **这里做三件事，本实现沿用：**
    ///
    /// 1. **无穷哨兵必须有限且为正**（否则 λ 的上下界会失效）
    /// 2. **输入键与结果键不得相同，且结果键不得重复**（否则两条 PWL 会抢同一个结果变量）
    /// 3. **断点至少两个、且与函数值等长**（否则凸组合退化）
    ///
    /// **"Before writing" is the point: a mid-way failure leaves the model half-modified, and COPT's `addVar` cannot roll
    /// back.** **So the check must be one <b>up-front</b> pass, not interleaved with the writing.**
    ///
 /// **This implementation does three things here, copied as-is:** **the infinity sentinel must be finite and positive; input
    /// and result keys must differ and result keys must not repeat; and there must be at least two breakpoints with the
    /// same length as the values.**
    /// </remarks>
    /// <param name="variableKeys">已映射的变量键 / The mapped variable keys.</param>
    /// <param name="data">待写入的 PWL 数据 / The PWL data to write.</param>
    /// <param name="infinity">COPT 的无穷哨兵 / COPT's infinity sentinel.</param>
    /// <returns>成功或错误 / Success or an error.</returns>
    internal static Try Validate(
        IReadOnlyCollection<VariableItemKey> variableKeys,
        IReadOnlyList<NativePiecewiseData> data,
        double infinity) {
        if (!double.IsFinite(infinity) || infinity <= 0.0) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.OREngineModelingException,
                $"COPT infinity 哨兵无效：{infinity.ToString("R", CultureInfo.InvariantCulture)} / Invalid COPT infinity sentinel"));
        }

        var resultKeys = new HashSet<VariableItemKey>();
        foreach (NativePiecewiseData piecewise in data) {
            if (piecewise.InputKey == piecewise.ResultKey || !resultKeys.Add(piecewise.ResultKey)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    $"COPT PWL 输入或结果键重复：{piecewise.Name} / COPT PWL input or result key is duplicated"));
            }
            if (piecewise.XPoints.Length < 2 || piecewise.YPoints.Length != piecewise.XPoints.Length) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    $"COPT PWL 断点与函数值数量无效：{piecewise.Name} / Invalid COPT PWL breakpoint and value counts"));
            }
            if (!variableKeys.Contains(piecewise.InputKey) || !variableKeys.Contains(piecewise.ResultKey)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    $"COPT PWL 变量映射缺失：{piecewise.Name} / Missing COPT PWL variable mapping"));
            }
        }

        return Results.OkInstance;
    }

    /// <summary>
    /// **把每条 PWL 写成 λ 变量组 + 三条行 + 一条 SOS2** /
    /// Write each PWL as a λ group, three rows and one SOS2.
    /// </summary>
    /// <remarks>
    /// **每条结构要五样东西，而它们必须同时存在才有意义：**
    ///
    /// 1. **λ 变量组**，每个断点一个，界 `[0, 1]`
    /// 2. **`Σλ = 1`** —— 让 λ 成为凸组合的权重
    /// 3. **`x = Σ λᵢ·xᵢ`** —— 输入落在断点的凸组合上
    /// 4. **`y = Σ λᵢ·yᵢ`** —— 结果落在函数值的凸组合上
    /// 5. **`SOS2(λ, x 断点)`** —— 至多两个相邻 λ 非零，这才是"在两点之间线性插值"
    ///
    /// **缺第 5 条会怎样：** **λ 可以是任意凸组合，于是 `y` 会取到断点凸包内部的任意值，
    /// 而不是折线上的值——模型仍然可解，结果却是错的。**
    ///
    /// **这就是"PWL 没有 SOS2 就只是一个凸包"这个事实，而它是这里唯一真正需要求解器配合的地方。**
    ///
    /// **Each structure needs five parts, and they only mean anything together:** **the λ group with one variable per
    /// breakpoint, `Σλ = 1`, `x = Σ λᵢ·xᵢ`, `y = Σ λᵢ·yᵢ`, and `SOS2(λ, x breakpoints)`.** **The last is what makes it
    /// interpolation between two neighbouring points rather than an arbitrary convex combination.**
    ///
    /// **Without the SOS2 the model stays solvable and the answer is wrong: `y` can sit anywhere in the convex hull of the
    /// breakpoints instead of on the polyline.** **That is the one place the write side genuinely needs the solver to
    /// cooperate.**
    /// </remarks>
    /// <param name="model">COPT 模型 / The COPT model.</param>
    /// <param name="variables">变量键到 COPT 变量的映射 / Variable key to COPT variable.</param>
    /// <param name="data">PWL 数据 / The PWL data.</param>
    /// <param name="infinity">COPT 的无穷哨兵 / COPT's infinity sentinel.</param>
    /// <returns>成功或错误 / Success or an error.</returns>
    internal static Try Add(
        CoptSdk.Model model,
        IReadOnlyDictionary<VariableItemKey, CoptSdk.Var> variables,
        IReadOnlyList<NativePiecewiseData> data,
        double infinity) {
        Try validation = Validate(variables.Keys.ToArray(), data, infinity);
        if (validation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return validation;
        }

        try {
            for (int piecewiseIndex = 0; piecewiseIndex < data.Count; ++piecewiseIndex) {
                NativePiecewiseData piecewise = data[piecewiseIndex];
                if (!variables.TryGetValue(piecewise.InputKey, out CoptSdk.Var? input)
                    || !variables.TryGetValue(piecewise.ResultKey, out CoptSdk.Var? result)) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        $"COPT PWL 变量映射缺失：{piecewise.Name} / Missing COPT PWL variable mapping"));
                }

                string prefix = $"copt-pwl-{piecewiseIndex}";
                int count = piecewise.XPoints.Length;
                var lambda = new CoptSdk.Var[count];
                for (int index = 0; index < count; ++index) {
                    lambda[index] = model.AddVar(
                        0.0,
                        1.0,
                        0.0,
                        CoptSdk.Consts.CONTINUOUS,
                        $"{prefix}-lambda-{index}");
                }

                // **Σλ = 1**
                using (var sum = new CoptSdk.Expr()) {
                    foreach (CoptSdk.Var variable in lambda) {
                        sum.AddTerm(variable, 1.0);
                    }
                    model.AddConstr(sum, CoptSdk.Consts.EQUAL, 1.0, $"{prefix}-lambda-sum");
                }

                // **x = Σ λᵢ·xᵢ**
                using (var xLink = new CoptSdk.Expr()) {
                    xLink.AddTerm(input, 1.0);
                    for (int index = 0; index < count; ++index) {
                        xLink.AddTerm(lambda[index], -piecewise.XPoints[index]);
                    }
                    model.AddConstr(xLink, CoptSdk.Consts.EQUAL, 0.0, $"{prefix}-x-link");
                }

                // **y = Σ λᵢ·yᵢ**
                using (var yLink = new CoptSdk.Expr()) {
                    yLink.AddTerm(result, 1.0);
                    for (int index = 0; index < count; ++index) {
                        yLink.AddTerm(lambda[index], -piecewise.YPoints[index]);
                    }
                    model.AddConstr(yLink, CoptSdk.Consts.EQUAL, 0.0, $"{prefix}-y-link");
                }

                // **SOS2：至多两个相邻 λ 非零。**
                // **没有这一条，上面四条只描述断点的凸包，而不是折线。**
                // **SOS2: at most two neighbouring λ non-zero. Without it the four rows above describe the convex hull
                // of the breakpoints rather than the polyline.**
                model.AddSos(lambda, piecewise.XPoints, CoptSdk.Consts.SOS_TYPE2);
            }

            return Results.OkInstance;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.OREngineModelingException,
                $"COPT PWL 写入失败：{error.Message} / COPT PWL write failed"));
        }
    }
}
