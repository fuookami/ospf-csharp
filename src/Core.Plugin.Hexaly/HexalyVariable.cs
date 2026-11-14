#nullable enable

using Fuookami.Ospf.Core.Variable;

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

}
