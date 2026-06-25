#nullable enable

namespace Fuookami.Ospf.Framework.Persistence.Expression
{
    /// <summary>
    /// 持久化字段解析器委托 / Persistence field resolver delegate.
    /// </summary>
    /// <typeparam name="C">列类型 / Column type</typeparam>
    /// <param name="propertyPath">属性路径 / Property path</param>
    /// <returns>列值，未找到返回 null / Column value, null if not found</returns>
    public delegate C? PersistenceFieldResolver<C>(string propertyPath) where C : notnull;
}
