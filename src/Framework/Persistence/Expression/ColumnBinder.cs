#nullable enable

namespace Fuookami.Ospf.Framework.Persistence.Expression;
/// <summary>
/// 强类型列映射接口 / Strong-typed column mapping interface.
/// </summary>
public interface IHasColumnMapping {
}

/// <summary>
/// 列绑定器接口 / Column binder interface.
/// </summary>
/// <typeparam name="C">列类型 / Column type</typeparam>
public interface IColumnBinder<C> where C : notnull {
    /// <summary>解析属性路径为列 / Resolve property path to column.</summary>
    C? Resolve(string propertyPath);
}

/// <summary>
/// 列绑定器扩展方法 / Column binder extension methods.
/// </summary>
public static class ColumnBinderExtensions {
    /// <summary>将列绑定器转换为字段解析器 / Convert column binder to field resolver.</summary>
    public static PersistenceFieldResolver<C> ToResolver<C>(this IColumnBinder<C> binder) where C : notnull
        => binder.Resolve;
}
