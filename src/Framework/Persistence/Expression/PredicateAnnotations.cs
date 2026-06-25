#nullable enable

namespace Fuookami.Ospf.Framework.Persistence.Expression;
/// <summary>
/// 列命名策略 / Column naming strategy.
/// </summary>
public enum ColumnNamingStrategy {
    /// <summary>保持原名 / Keep original name</summary>
    Identity,
    /// <summary>转换为蛇形命名 / Convert to snake_case</summary>
    SnakeCase
}
