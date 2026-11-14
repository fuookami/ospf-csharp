#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// &lt;summary>
/// 把模型的变量列与 token 列对齐成"键 → 列号" / Align a model's variable columns with its token columns into key → column.
/// &lt;/summary>
/// &lt;remarks>
/// B1208 按基线 `NativePiecewiseReport.kt` 的 `nativeColumns` 补。
///
/// **基线的签名是 `nativeColumns(model: LinearTriadModel)`，但函数体只用到两个东西：
/// `model.variables` 与 `model.tokensInSolver`。**
/// **所以这里<b>不</b>绑定具体模型类型，而是接收这两个列表——** **这不只是"更方便"：
/// 它让这一格可以被独立测，而不是必须造一个完整模型。**
///
/// **三道检查，顺序是有理由的：**
///
/// | 顺序 | 检查 | 为什么在这个位置 |
/// | --- | --- | --- |
/// | 1 | **变量数 == token 数** | **先确认两个列表可以逐位比较，否则后面全是越界** |
/// | 2 | **token 键互不相同** | **键要当字典的键用，重复会让后写的覆盖先写的** |
/// | 3 | **`origin` 非空时必须等于 token 键** | **只比"有的那些"——手工构造的模型可以没有 origin** |
///
/// **第 3 条用的是 `origin != null &amp;&amp; origin.key != key` 即拒，而不是"`origin` 必须存在"。**
/// **手工构造的模型没有 origin，那是一个<b>合法</b>情形，不是错误。**
/// **把它当成错误，会让所有手工模型都无法走 native 路径。**
///
/// **第 3 条抓的是"变量列与 token 列错位"：**
/// **两列长度相同、键各自唯一，但顺序不一样——那么按列号取到的值属于另一个变量。**
/// **这种错位不会让任何一步报错，只会让报告里的数字张冠李戴。**
///
/// **The baseline's signature is `nativeColumns(model: LinearTriadModel)` but the body uses only two things:
/// `model.variables` and `model.tokensInSolver`.** **So this does <b>not</b> bind to a concrete model type but takes those
/// two lists — not merely convenient: it lets the cell be tested on its own instead of requiring a whole model.**
///
/// **Three checks, and the order has reasons:**
///
/// | Order | Check | Why here |
/// | --- | --- | --- |
/// | 1 | **variable count == token count** | **establish that the two lists can be compared position by position** |
/// | 2 | **token keys are distinct** | **keys become dictionary keys; duplicates let a later write overwrite an earlier** |
/// | 3 | **a non-null `origin` must equal the token key** | **only the ones that exist — hand-built models may have none** |
///
/// **Check 3 rejects `origin != null &amp;&amp; origin.key != key` rather than demanding `origin` exist.** **A hand-built model
/// without origins is a <b>legal</b> case, not an error; treating it as one would lock every hand-built model out of the
/// native path.**
///
/// **What check 3 catches is column misalignment**: **two lists of equal length with individually unique keys but in
/// different order — then the value at a column belongs to another variable.** **Such misalignment raises nothing
/// anywhere; it just makes the report attribute numbers to the wrong variables.**
/// &lt;/remarks>
public static class NativeColumnProjection
{
    /// &lt;summary>把变量列与 token 列对齐 / Align variable columns with token columns.&lt;/summary>
    public static Result<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>>
        Columns(
            IReadOnlyList<ModelViewVariable> variables,
            IReadOnlyList<Token<Flt64>> tokensInSolver) {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(tokensInSolver);

        if (variables.Count != tokensInSolver.Count) {
            return Failure("native variable and token counts differ");
        }

        var keys = new List<VariableItemKey>(tokensInSolver.Count);
        foreach (Token<Flt64> token in tokensInSolver) {
            keys.Add(token.Key);
        }

        if (new HashSet<VariableItemKey>(keys).Count != keys.Count) {
            return Failure("native token keys are duplicated");
        }

        for (int index = 0; index < variables.Count; ++index) {
            IVariableItem? origin = variables[index].Origin;
            if (origin is not null && origin.Key != keys[index]) {
                return Failure("native variable and token keys differ");
            }
        }

        var columns = new Dictionary<VariableItemKey, int>(keys.Count);
        for (int index = 0; index < keys.Count; ++index) {
            columns[keys[index]] = index;
        }

        return new Ok<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>>(
            columns);
    }

    private static Result<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>>
        Failure(string message) =>
            new Failed<IReadOnlyDictionary<VariableItemKey, int>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);
}
