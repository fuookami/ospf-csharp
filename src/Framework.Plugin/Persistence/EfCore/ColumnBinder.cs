#nullable enable

using System;
using System.Linq.Expressions;

namespace Fuookami.Ospf.Framework.Plugin.Persistence.EfCore;

public interface IColumnBinder<T> where T : class {
    global::System.Linq.Expressions.Expression<Func<T, object?>>? BindColumn(string propertyName);
}

public class DefaultColumnBinder<T> : IColumnBinder<T> where T : class {
    public global::System.Linq.Expressions.Expression<Func<T, object?>>? BindColumn(string propertyName) {
        ParameterExpression param = global::System.Linq.Expressions.Expression.Parameter(typeof(T), "x");
        MemberExpression prop = global::System.Linq.Expressions.Expression.Property(param, propertyName);
        UnaryExpression convert = global::System.Linq.Expressions.Expression.Convert(prop, typeof(object));
        return global::System.Linq.Expressions.Expression.Lambda<Func<T, object?>>(convert, param);
    }
}
