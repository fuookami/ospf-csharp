#nullable enable

using System;

namespace Fuookami.Ospf.Framework.Plugin.Persistence.EfCore;

public interface IBooleanTranslator<T> where T : class {
    global::System.Linq.Expressions.Expression<Func<T, bool>>? Translate(object predicate);
}

public class DefaultBooleanTranslator<T> : IBooleanTranslator<T> where T : class {
    public global::System.Linq.Expressions.Expression<Func<T, bool>>? Translate(object predicate) => null;
}
