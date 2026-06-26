#nullable enable

using System;
using System.Linq.Expressions;

namespace Fuookami.Ospf.Framework.Plugin.Persistence.EfCore;

public interface IScalarTranslator<T> where T : class {
    global::System.Linq.Expressions.Expression<Func<T, TValue>>? Translate<TValue>(string propertyName);
}

public class DefaultScalarTranslator<T> : IScalarTranslator<T> where T : class {
    public global::System.Linq.Expressions.Expression<Func<T, TValue>>? Translate<TValue>(string propertyName) {
        ParameterExpression param = global::System.Linq.Expressions.Expression.Parameter(typeof(T), "x");
        MemberExpression prop = global::System.Linq.Expressions.Expression.Property(param, propertyName);
        UnaryExpression convert = global::System.Linq.Expressions.Expression.Convert(prop, typeof(TValue));
        return global::System.Linq.Expressions.Expression.Lambda<Func<T, TValue>>(convert, param);
    }
}
