#nullable enable

using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Hexaly.Optimizer;

namespace Fuookami.Ospf.Core.Plugin.Hexaly;

/// <summary>
/// Hexaly 变量类型枚举，将内部变量类型映射为 Hexaly 变量类型。
/// Hexaly variable type enum, maps internal variable types to Hexaly variable types.
/// </summary>
public enum HexalyVariableType {
    /// <summary>二进制变量 / Binary variable</summary>
    Binary,
    /// <summary>整数变量 / Integer variable</summary>
    Integer,
    /// <summary>连续变量 / Continuous variable</summary>
    Continuous
}

/// <summary>
/// HexalyVariable 扩展方法 / HexalyVariable extension methods.
/// </summary>
public static class HexalyVariableExtensions {
    /// <summary>
    /// 从内部变量类型创建 Hexaly 变量类型 / Create Hexaly variable type from internal variable type.
    /// </summary>
    /// <param name="type">内部变量类型 / Internal variable type kind.</param>
    /// <returns>Hexaly 变量类型 / Hexaly variable type.</returns>
    public static HexalyVariableType From(IVariableTypeKind type) {
        if (type.IsBinaryType) {
            return HexalyVariableType.Binary;
        }

        if (type.IsIntegerType) {
            return HexalyVariableType.Integer;
        }

        return HexalyVariableType.Continuous;
    }

    /// <summary>
    /// 创建 Hexaly 变量表达式 / Create Hexaly variable expression.
    /// </summary>
    /// <param name="type">Hexaly 变量类型 / Hexaly variable type.</param>
    /// <param name="model">Hexaly 模型 / Hexaly model.</param>
    /// <param name="lb">下界 / Lower bound.</param>
    /// <param name="ub">上界 / Upper bound.</param>
    /// <returns>Hexaly 变量表达式 / Hexaly variable expression.</returns>
    public static HxExpression CreateVariable(this HexalyVariableType type, HxModel model, Flt64 lb, Flt64 ub) => type switch {
        HexalyVariableType.Binary => model.Bool(),
        HexalyVariableType.Integer => model.Int(lb.ToDouble().ToLong(), ub.ToDouble().ToLong()),
        HexalyVariableType.Continuous => model.Float(lb.ToDouble(), ub.ToDouble()),
        _ => model.Float(lb.ToDouble(), ub.ToDouble())
    };
}

/// <summary>
/// 辅助扩展：double 转 long / Helper: double to long conversion for Hexaly intVar.
/// </summary>
internal static class DoubleExtensions {
    public static long ToLong(this double d) => checked((long)d);
}
