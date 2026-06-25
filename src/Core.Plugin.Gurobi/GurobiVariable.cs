#nullable enable

using Gurobi;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi 变量类型枚举，将内部变量类型映射为 Gurobi 变量类型。
/// Gurobi variable type enum, maps internal variable types to Gurobi variable types.
/// </summary>
public enum GurobiVariable
{
    /// <summary>二进制变量 / Binary variable</summary>
    Binary,
    /// <summary>整数变量 / Integer variable</summary>
    Integer,
    /// <summary>连续变量 / Continuous variable</summary>
    Continuous
}

/// <summary>
/// GurobiVariable 扩展方法 / GurobiVariable extension methods.
/// </summary>
public static class GurobiVariableExtensions
{
    /// <summary>
    /// 从内部变量类型创建 Gurobi 变量类型 / Create Gurobi variable type from internal variable type.
    /// </summary>
    /// <param name="type">内部变量类型 / Internal variable type kind.</param>
    /// <returns>Gurobi 变量类型 / Gurobi variable type.</returns>
    public static GurobiVariable From(IVariableTypeKind type)
    {
        if (type.IsBinaryType) return GurobiVariable.Binary;
        if (type.IsIntegerType) return GurobiVariable.Integer;
        return GurobiVariable.Continuous;
    }

    /// <summary>
    /// 转换为 Gurobi 变量类型字符 / Convert to Gurobi variable type character.
    /// </summary>
    /// <param name="variable">Gurobi 变量类型 / Gurobi variable type.</param>
    /// <returns>Gurobi 变量类型字符 / Gurobi variable type character.</returns>
    public static char ToGurobiChar(this GurobiVariable variable) => variable switch
    {
        GurobiVariable.Binary => GRB.BINARY,
        GurobiVariable.Integer => GRB.INTEGER,
        GurobiVariable.Continuous => GRB.CONTINUOUS,
        _ => GRB.CONTINUOUS
    };
}
