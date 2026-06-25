#nullable enable

using Gurobi;
using Fuookami.Ospf.Core.Model.Basic;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi 约束符号枚举，将内部约束关系映射为 Gurobi 约束符号。
/// Gurobi constraint sign enum, maps internal constraint relations to Gurobi constraint signs.
/// </summary>
public enum GurobiConstraintSign
{
    /// <summary>大于等于 / Greater than or equal</summary>
    GreaterEqual,
    /// <summary>等于 / Equal</summary>
    Equal,
    /// <summary>小于等于 / Less than or equal</summary>
    LessEqual
}

/// <summary>
/// GurobiConstraintSign 扩展方法 / GurobiConstraintSign extension methods.
/// </summary>
public static class GurobiConstraintSignExtensions
{
    /// <summary>
    /// 从内部约束关系创建 Gurobi 约束符号 / Create Gurobi constraint sign from internal constraint relation.
    /// </summary>
    /// <param name="sign">内部约束关系 / Internal constraint relation.</param>
    /// <returns>Gurobi 约束符号 / Gurobi constraint sign.</returns>
    public static GurobiConstraintSign From(ConstraintRelation sign)
    {
        if (sign == ConstraintRelation.GreaterEqual) return GurobiConstraintSign.GreaterEqual;
        if (sign == ConstraintRelation.Equal) return GurobiConstraintSign.Equal;
        return GurobiConstraintSign.LessEqual;
    }

    /// <summary>
    /// 转换为 Gurobi 约束符号字符 / Convert to Gurobi constraint sign character.
    /// </summary>
    /// <param name="sign">Gurobi 约束符号 / Gurobi constraint sign.</param>
    /// <returns>Gurobi 约束符号字符 / Gurobi constraint sign character.</returns>
    public static char ToGurobiChar(this GurobiConstraintSign sign) => sign switch
    {
        GurobiConstraintSign.GreaterEqual => GRB.GREATER_EQUAL,
        GurobiConstraintSign.Equal => GRB.EQUAL,
        GurobiConstraintSign.LessEqual => GRB.LESS_EQUAL,
        _ => GRB.LESS_EQUAL
    };
}
