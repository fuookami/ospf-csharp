#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Variable;

/// <summary>
/// 变量类型与其稳定名称之间的对照 / The catalog between variable types and their stable names.
/// </summary>
/// <remarks>
/// **更正**： 说这个目录是"重建器的前置件"，**那个判断是错的。**
///
/// `buildModel` **不从 `TypeName` 重建变量**：它遍历快照的表达式 AST，把里面的
/// `IVariableItem` 引用**原样收出来**再用。所以重建器不需要"造变量"，需要的是"找回变量"
/// ——**而这条路上没有类型名。**
///
/// 这个目录仍然有用（报告、校验、跨语言对照），但**它不是重建器的前置件**。
///
/// **correction**: claimed this catalog is the rebuilder's prerequisite. **That was wrong.** Its `buildModel` **does not rebuild variables from
/// `TypeName`**: it walks the snapshot's expression AST and reuses the `IVariableItem` references found there. A rebuilder therefore does not *create* variables, it *finds*
/// them — **and no type name appears on that path.** The catalog stays useful for reporting, verification and cross-language comparison, but **it is not the rebuilder's
/// prerequisite.**
///
/// **`TryResolve` 不提供兜底**：认不出的名字返回 `false`。**兜底一个"最像的"类型，
/// 正是那种不报错、只是答案不对的失效。**
///
/// adds this catalog because it is the prerequisite for `ConstraintProgrammingSnapshotBuilder`.** The snapshot stores a variable's type as `TypeKind.ToString` —
/// **a class name** — and nothing in C# parses it back: `TypeName` is encoded, carried and decoded, **and never once used as the basis for constructing a variable**. Without
/// this catalog, rebuilding a mutable model from a snapshot could only **guess** a type, and a wrong default (continuous for everything, say) would let **an integer model be
/// solved as an LP** — no error, just a wrong answer. **`TryResolve` offers no fallback**: an unrecognised name returns `false`. **Falling back to the "closest" type is exactly
/// that no-error-wrong-answer failure.**
/// </remarks>
public static class VariableTypeCatalog
{
    /// <summary>
    /// 名称到下标的对照表 / The name table.
    /// </summary>
    /// <remarks>
    /// **键是类型的稳定名称，不是显示名。** 每一项都必须在 <see cref="StableName"/> 与 <see cref="TryResolve"/> 两侧一致，
    /// 否则"存得进去、读不出来"。
    ///
    /// **The keys are the types' stable names, not display names.** Every entry has to agree between <see cref="StableName"/> and <see cref="TryResolve"/>, or values get
    /// **written but not read back.**
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, IVariableTypeKind> ByName =
        new Dictionary<string, IVariableTypeKind>(StringComparer.Ordinal) {
            ["Binary"] = Binary.Instance,
            ["Ternary"] = Ternary.Instance,
            ["BalancedTernary"] = BalancedTernary.Instance,
            ["Percentage"] = Percentage.Instance,
            ["Integer"] = Integer.Instance,
            ["UInteger"] = UInteger.Instance,
            ["Continuous"] = Continuous.Instance,
            ["UContinuous"] = UContinuous.Instance,
        };

    /// <summary>已知稳定名称 / The known stable names.</summary>
    public static IReadOnlyCollection<string> Names => (IReadOnlyCollection<string>)ByName.Keys;

    /// <summary>
    /// 取类型的稳定名称 / The stable name of a type.
    /// </summary>
    /// <param name="kind">变量类型 / Variable type.</param>
    /// <returns>稳定名称，未知类型时为 <c>null</c> / The stable name, or <c>null</c> for an unknown type.</returns>
    public static string? StableName(IVariableTypeKind kind) {
        ArgumentNullException.ThrowIfNull(kind);

        foreach (KeyValuePair<string, IVariableTypeKind> entry in ByName) {
            if (entry.Value.GetType() == kind.GetType()) {
                return entry.Key;
            }
        }
        return null;
    }

    /// <summary>
    /// 按稳定名称造一个标量变量 / Create a scalar variable from its stable name.
    /// </summary>
    /// <param name="name">稳定名称 / Stable name.</param>
    /// <param name="variableName">变量显示名 / Display name for the variable.</param>
    /// <param name="variable">造出的变量 / The created variable.</param>
    /// <returns>是否造出 / Whether the name resolved to a variable.</returns>
    /// <remarks>
    /// **`RegisterVariable` 要的是变量实例，不是一个类型对象**；这个工厂能造出一个，
    /// **但做法不是造，是找回**：新建的对象 `Identifier` / `Key` 都不同，注册进去就是另一个变量。
    /// **更正见类型说明。**
    ///
    /// **认不出就返回 `false`，不兜底造一个连续型**：一个错的变量类型不报错，只是让整数模型按 LP 求解，
    /// **解出来了、值不对、没有一行日志说它换过类型。**
    ///
 /// **`RegisterVariable` takes a variable instance, not a type object**, and this factory can supply one — **but that is not what this implementation does**: the implementation *finds* the original variable objects in the expression AST, and a new object is not the same variable (`Identifier` / `Key` differ, so it registers as another one). **See the type remarks for the correction.** **An unrecognised name returns
    /// `false` with no fallback**: a wrong variable type raises nothing, it just lets an integer model be solved as an LP — **solved, wrong, and with no log line saying the type
    /// changed.**
    /// </remarks>
    public static bool TryCreate(string? name, string variableName, out IVariableItem variable) {
        if (name is not null) {
            switch (name) {
                case "Binary": variable = new BinVar(variableName); return true;
                case "Ternary": variable = new TerVar(variableName); return true;
                case "BalancedTernary": variable = new BTerVar(variableName); return true;
                case "Percentage": variable = new PctVar(variableName); return true;
                case "Integer": variable = new IntVar(variableName); return true;
                case "UInteger": variable = new UIntVar(variableName); return true;
                case "Continuous": variable = new RealVar(variableName); return true;
                case "UContinuous": variable = new URealVar(variableName); return true;
                default: break;
            }
        }
        variable = new RealVar("");
        return false;
    }

    /// <summary>
    /// 由稳定名称解析类型 / Resolve a type from its stable name.
    /// </summary>
    /// <param name="name">稳定名称 / Stable name.</param>
    /// <param name="kind">解析结果 / The resolved type.</param>
    /// <returns>是否解析成功 / Whether the name resolved.</returns>
    /// <remarks>
    /// **没有兜底。** 认不出就是 <c>false</c>，由调用方决定是拒掉整次重建还是跳过这个变量——
    /// **但绝不能替它选一个类型。**
    ///
    /// **No fallback.** An unrecognised name is `false`, leaving it to the caller to refuse the rebuild or skip the variable — **but never to pick a type in its place.**
    /// </remarks>
    public static bool TryResolve(string? name, out IVariableTypeKind kind) {
        if (name is not null && ByName.TryGetValue(name, out IVariableTypeKind? resolved)) {
            kind = resolved;
            return true;
        }
        kind = Continuous.Instance;
        return false;
    }
}
