// 路径编译模型 / Path compilation model.
// 与固定 Kotlin （2629 字节）对应。

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Math.Symbol.Monomial;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation.Model;

/// <summary>
/// 注册边界规范化的单列；注册与定价共用此定义 / A normalized column shared by registration and pricing.
/// </summary>
/// <remarks>
/// **注册与定价必须用同一个规范化函数**。若注册时按行单位换算、定价时按费用单位换算，两侧的系数会在数值上一致却在量纲上不同，
/// reduced cost 因此系统性偏移——而求解器不会报错，只会收敛到更差的解。
/// **Registration and pricing must share one normalisation function.** If registration converts by row unit while pricing converts by
/// cost unit, the two coefficients agree numerically but differ in dimension, so the reduced cost drifts systematically — and the
/// solver never errors, it just converges to a worse solution.
/// </remarks>
/// <typeparam name="V">领域数值类型 / Domain numeric type.</typeparam>
/// <typeparam name="C">完整路径载荷类型 / Complete path payload type.</typeparam>
/// <param name="Column">业务列 / The business column.</param>
/// <param name="Cost">规范化费用 / The normalized cost.</param>
/// <param name="Coefficients">规范化行系数 / The normalized row coefficients.</param>
public sealed record CompiledPathColumn<V, C>(
    PathColumn<V, C> Column,
    Flt64 Cost,
    IReadOnlyDictionary<string, Flt64> Coefficients) where V : struct, IRealNumber<V>;

/// <summary>
/// 路径变量与可增量更新的行组件 / Path variables and an incrementally updatable row component.
/// </summary>
/// <remarks>
/// **行符号必须就地增量更新，而不是每次重建**。重建会让已注册进模型的旧符号失去与约束的关联，模型规模随列数线性膨胀；增量更新则让
/// 每一列的费用与行用量在同一处被记账，两者永远同步。
/// **Row symbols must be updated in place, never rebuilt.** Rebuilding detaches the symbols already registered in the model, so the
/// model grows linearly with the column count; incremental updates book each column's cost and row usage in one place, keeping the
/// two in sync.
/// **`Add` 用 `GetValue` 而非默认零**：行系数缺失意味着调用方漏填，静默按零处理会让该列在这一行上"免费"，模型会大量选用它。
/// **`Add` reads with `GetValue` rather than defaulting to zero**: a missing row coefficient means the caller forgot it, and silently
/// treating it as zero makes the column "free" in that row, so the model uses many of them.
/// </remarks>
public sealed class PathCompilation {
    private readonly Dictionary<string, LinearExpressionSymbol> _rows;
    private readonly Dictionary<string, UIntVariable1> _variables = new(StringComparer.Ordinal);

    /// <summary>
    /// 构造路径编译模型 / Construct the path compilation model.
    /// </summary>
    /// <param name="rowIds">稳定行标识 / Stable row identities.</param>
    /// <param name="name">命名空间 / The namespace.</param>
    public PathCompilation(IReadOnlyList<string> rowIds, string name) {
        ArgumentNullException.ThrowIfNull(rowIds);
        ArgumentNullException.ThrowIfNull(name);
        _rows = rowIds.ToDictionary(
            id => id,
            id => new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                name: $"{name}_row_{id}"),
            StringComparer.Ordinal);
    }

    /// <summary>当前列变量 / The current column variables.</summary>
    public IReadOnlyDictionary<string, UIntVariable1> Variables => _variables;

    /// <summary>行表达式 / The row expressions.</summary>
    public IReadOnlyDictionary<string, LinearExpressionSymbol> Rows => _rows;

    /// <summary>
    /// 添加一列并就地更新所有行 / Add a column and update every row in place.
    /// </summary>
    /// <param name="id">稳定列标识 / Stable column identity.</param>
    /// <param name="coefficients">规范化行系数 / Normalized row coefficients.</param>
    /// <param name="name">变量名 / The variable name.</param>
    /// <returns>新列变量 / The new column variable.</returns>
    internal UIntVariable1 Add(string id, IReadOnlyDictionary<string, Flt64> coefficients, string name) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(coefficients);
        ArgumentNullException.ThrowIfNull(name);
        // C# 侧没有单元素二元变量类型，用长度 1 的 `UIntVariable1` 表达同一语义（`BinVar`）。
        // The C# side has no single-element binary variable type; a length-1 `UIntVariable1` expresses the same semantics
        // (the code uses `BinVar`).
        var variable = new UIntVariable1(name, Shape1.Invoke(1));
        _variables[id] = variable;
        foreach ((string rowId, LinearExpressionSymbol symbol) in _rows) {
            symbol.Flush();
            MutableLinearPolynomial<Flt64> polynomial = symbol.AsMutable();
            Flt64 amount = coefficients.TryGetValue(rowId, out Flt64 value) ? value : Flt64.Zero;
            foreach (IVariableItem item in variable) {
                polynomial.AddMonomial(new LinearMonomial<Flt64>(amount, item));
            }
        }
        return variable;
    }

    /// <summary>
    /// 移除一列并从所有行中剔除它的单项 / Remove a column and drop its terms from every row.
    /// </summary>
    /// <param name="id">稳定列标识 / Stable column identity.</param>
    internal void Remove(string id) {
        ArgumentNullException.ThrowIfNull(id);
        if (!_variables.Remove(id, out UIntVariable1? variable)) {
            return;
        }
        foreach (LinearExpressionSymbol symbol in _rows.Values) {
            symbol.Flush();
            MutableLinearPolynomial<Flt64> polynomial = symbol.AsMutable();
            // 只删该变量的单项，保留常数项与其它列的单项。
            // Drop only this variable's terms, keeping the constant and every other column's terms.
            // 用变量名比较，而不是引用： 迭代出的是逐元素视图，与单项里存的符号不是同一对象。
            // Compare by variable name rather than by reference:  iterates element views that are not the same
            // objects as the symbols stored in the monomials.
            string prefix = variable.Name;
            var retained = polynomial.Monomials
                .Where(monomial => !monomial.Symbol.Name.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
            Flt64 constant = polynomial.Constant;
            polynomial.Clear();
            foreach (LinearMonomial<Flt64> monomial in retained) {
                polynomial.AddMonomial(monomial);
            }
            polynomial.SetConstant(constant);
        }
    }
}
