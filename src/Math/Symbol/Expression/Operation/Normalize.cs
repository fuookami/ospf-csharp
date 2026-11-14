#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Symbol;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Symbol.Expression.Operation;
/// <summary>规范化配置 / Normalization Configuration.</summary>
public sealed record NormalizeConfig(
    bool Flatten = true,
    bool ConstantFolding = true,
    bool Deduplicate = true,
    bool EliminateDoubleNegation = true,
    bool ApplyDeMorgan = false,
    bool SortOperands = false) {
    /// <summary>默认配置 / Default configuration.</summary>
    public static readonly NormalizeConfig Default = new();
}

/// <summary>
/// 表达式规范化 / Expression Normalization.
/// Pure functions returning new immutable records.
/// </summary>
public static class Normalize {
    /// <summary>规范化布尔表达式 / Normalize boolean expression.</summary>
    public static BooleanExpression Apply(BooleanExpression expr, NormalizeConfig? config = null) {
        config ??= NormalizeConfig.Default;
        BooleanExpression result = expr;
        if (config.Flatten) {
            result = Flatten(result);
        }

        if (config.EliminateDoubleNegation) {
            result = EliminateDoubleNegation(result);
        }

        if (config.ApplyDeMorgan) {
            result = ApplyDeMorgan(result);
        }

        if (config.ConstantFolding) {
            result = ConstantFold(result);
        }

        if (config.Deduplicate) {
            result = Deduplicate(result);
        }

        if (config.SortOperands) {
            result = SortOperands(result);
        }

        result = NormalizeChildren(result, config);
        return SimplifySingleOperand(result);
    }

    /// <summary>扁平化 And/Or 表达式 / Flatten And/Or expressions.</summary>
    public static BooleanExpression Flatten(BooleanExpression expr) {
        if (expr is AndExpression a) {
            var flattened = new List<BooleanExpression>();
            foreach (BooleanExpression op in a.Operands) {
                BooleanExpression f = Flatten(op);
                if (f is AndExpression fa) {
                    flattened.AddRange(fa.Operands);
                }
                else {
                    flattened.Add(f);
                }
            }
            return flattened.Count == 1 ? flattened[0] : new AndExpression(flattened);
        }
        if (expr is OrExpression o) {
            var flattened = new List<BooleanExpression>();
            foreach (BooleanExpression op in o.Operands) {
                BooleanExpression f = Flatten(op);
                if (f is OrExpression fo) {
                    flattened.AddRange(fo.Operands);
                }
                else {
                    flattened.Add(f);
                }
            }
            return flattened.Count == 1 ? flattened[0] : new OrExpression(flattened);
        }
        if (expr is NotExpression n) {
            return new NotExpression(Flatten(n.Operand));
        }

        return expr;
    }

    /// <summary>常量折叠 / Constant folding.</summary>
    public static BooleanExpression ConstantFold(BooleanExpression expr) {
        if (expr is BooleanConstant) {
            return expr;
        }

        if (expr is AndExpression a) {
            var operands = a.Operands.Select(ConstantFold).ToList();
            if (operands.Any(o => o is BooleanConstant bc && bc.IsFalse)) {
                return BooleanConstant.False();
            }

            var filtered = operands.Where(o => !(o is BooleanConstant bc && bc.IsTrue)).ToList();
            if (filtered.Count == 0) {
                return BooleanConstant.True();
            }

            if (filtered.Count == 1) {
                return filtered[0];
            }

            return new AndExpression(filtered);
        }

        if (expr is OrExpression o) {
            var operands = o.Operands.Select(ConstantFold).ToList();
            if (operands.Any(x => x is BooleanConstant bc && bc.IsTrue)) {
                return BooleanConstant.True();
            }

            var filtered = operands.Where(x => !(x is BooleanConstant bc && bc.IsFalse)).ToList();
            if (filtered.Count == 0) {
                return BooleanConstant.False();
            }

            if (filtered.Count == 1) {
                return filtered[0];
            }

            return new OrExpression(filtered);
        }

        if (expr is NotExpression n) {
            BooleanExpression operand = ConstantFold(n.Operand);
            if (operand is BooleanConstant bc) {
                if (bc.Value is Trivalent.True) {
                    return BooleanConstant.False();
                }

                if (bc.Value is Trivalent.False) {
                    return BooleanConstant.True();
                }

                return BooleanConstant.Unknown();
            }
            return new NotExpression(operand);
        }

        return expr;
    }

    /// <summary>去重 / Deduplicate.</summary>
    public static BooleanExpression Deduplicate(BooleanExpression expr) {
        if (expr is AndExpression a) {
            return new AndExpression(a.Operands.Select(Deduplicate).DistinctBy(StructuralKey).ToList());
        }

        if (expr is OrExpression o) {
            return new OrExpression(o.Operands.Select(Deduplicate).DistinctBy(StructuralKey).ToList());
        }

        if (expr is NotExpression n) {
            return new NotExpression(Deduplicate(n.Operand));
        }

        return expr;
    }

    /// <summary>消除双重否定 / Eliminate double negation.</summary>
    public static BooleanExpression EliminateDoubleNegation(BooleanExpression expr) {
        if (expr is NotExpression n) {
            BooleanExpression operand = EliminateDoubleNegation(n.Operand);
            return operand is NotExpression nn ? nn.Operand : new NotExpression(operand);
        }
        if (expr is AndExpression a) {
            return new AndExpression(a.Operands.Select(EliminateDoubleNegation).ToList());
        }

        if (expr is OrExpression o) {
            return new OrExpression(o.Operands.Select(EliminateDoubleNegation).ToList());
        }

        return expr;
    }

    /// <summary>应用德摩根定律 / Apply De Morgan's laws.</summary>
    public static BooleanExpression ApplyDeMorgan(BooleanExpression expr) {
        if (expr is NotExpression n) {
            BooleanExpression operand = ApplyDeMorgan(n.Operand);
            if (operand is AndExpression innerA) {
                return new OrExpression(innerA.Operands.Select(o => (BooleanExpression)new NotExpression(o)).ToList());
            }

            if (operand is OrExpression innerO) {
                return new AndExpression(innerO.Operands.Select(x => (BooleanExpression)new NotExpression(x)).ToList());
            }

            return new NotExpression(operand);
        }
        if (expr is AndExpression a) {
            return new AndExpression(a.Operands.Select(ApplyDeMorgan).ToList());
        }

        if (expr is OrExpression o) {
            return new OrExpression(o.Operands.Select(ApplyDeMorgan).ToList());
        }

        return expr;
    }

    /// <summary>排序操作数 / Sort operands.</summary>
    public static BooleanExpression SortOperands(BooleanExpression expr) {
        if (expr is AndExpression a) {
            return new AndExpression(a.Operands.Select(SortOperands).OrderBy(StructuralKey).ToList());
        }

        if (expr is OrExpression o) {
            return new OrExpression(o.Operands.Select(SortOperands).OrderBy(StructuralKey).ToList());
        }

        if (expr is NotExpression n) {
            return new NotExpression(SortOperands(n.Operand));
        }

        return expr;
    }

    private static BooleanExpression SimplifySingleOperand(BooleanExpression expr) {
        if (expr is AndExpression a) {
            var operands = a.Operands.Select(SimplifySingleOperand).ToList();
            if (operands.Count == 0) {
                return BooleanConstant.True();
            }

            if (operands.Count == 1) {
                return operands[0];
            }

            return new AndExpression(operands);
        }
        if (expr is OrExpression o) {
            var operands = o.Operands.Select(SimplifySingleOperand).ToList();
            if (operands.Count == 0) {
                return BooleanConstant.False();
            }

            if (operands.Count == 1) {
                return operands[0];
            }

            return new OrExpression(operands);
        }
        if (expr is NotExpression n) {
            return new NotExpression(SimplifySingleOperand(n.Operand));
        }

        return expr;
    }

    private static BooleanExpression NormalizeChildren(BooleanExpression expr, NormalizeConfig config) {
        if (expr is AndExpression a) {
            return new AndExpression(a.Operands.Select(o => Apply(o, config)).ToList());
        }

        if (expr is OrExpression o) {
            return new OrExpression(o.Operands.Select(x => Apply(x, config)).ToList());
        }

        if (expr is NotExpression n) {
            return new NotExpression(Apply(n.Operand, config));
        }

        return expr;
    }

    // Internal structural key helper for dedup/sort
    private static string StructuralKey(BooleanExpression expr) =>
        StructuralKeyExtensions.StructuralKey(expr);
}

/// <summary>结构键扩展 / Structural-key extensions (for dedup + sort).</summary>
public static class StructuralKeyExtensions {
    /// <summary>获取布尔表达式的结构键 / Get structural key of boolean expression.</summary>
    public static string StructuralKey(this BooleanExpression expr) => expr switch {
        BooleanConstant c => $"Const:{(c.Value is Trivalent.True ? "True" : c.Value is Trivalent.False ? "False" : "Unknown")}",
        NullCheck n => $"Null:{n.Type}:{n.Path}",
        AndExpression a => $"And:{string.Join(",", a.Operands.Select(StructuralKey))}",
        OrExpression o => $"Or:{string.Join(",", o.Operands.Select(StructuralKey))}",
        NotExpression n => $"Not:{StructuralKey(n.Operand)}",
        BooleanCustom c => $"Custom:{c.Description}",
        _ => StructuralKeyFallback(expr),
    };

    private static string StructuralKeyFallback(BooleanExpression expr) {
        // Handle generic subtypes via runtime type checking
        System.Type type = expr.GetType();
        if (type.IsGenericType) {
            System.Type genDef = type.GetGenericTypeDefinition();
            System.Type valueType = type.GetGenericArguments()[0];
            if (genDef == typeof(Comparison<>)) {
                object op = type.GetProperty("Operator")!.GetValue(expr)!;
                object left = type.GetProperty("Left")!.GetValue(expr)!;
                object right = type.GetProperty("Right")!.GetValue(expr)!;
                return $"Cmp:{op}:{ScalarKey(left, valueType)}:{ScalarKey(right, valueType)}";
            }
            if (genDef == typeof(InExpression<>)) {
                bool negated = (bool)type.GetProperty("Negated")!.GetValue(expr)!;
                object value = type.GetProperty("Value")!.GetValue(expr)!;
                var candidates = (System.Collections.IEnumerable)type.GetProperty("Candidates")!.GetValue(expr)!;
                IEnumerable<string> cands = candidates.Cast<object>()
                    .Select(x => ScalarKey(x, valueType));
                return $"In:{negated}:{ScalarKey(value, valueType)}:{string.Join(",", cands)}";
            }
            if (genDef == typeof(PatternMatch<>)) {
                object mode = type.GetProperty("Mode")!.GetValue(expr)!;
                bool negated = (bool)type.GetProperty("Negated")!.GetValue(expr)!;
                object value = type.GetProperty("Value")!.GetValue(expr)!;
                object pattern = type.GetProperty("Pattern")!.GetValue(expr)!;
                return $"Match:{mode}:{negated}:{ScalarKey(value, valueType)}:{ScalarKey(pattern, valueType)}";
            }
        }
        return expr.TypeName;
    }

    /// <summary>
    /// 按运行时值类型取标量表达式的结构键 / Get a scalar expression's structural key using its runtime value type.
    /// </summary>
    /// <remarks>
    /// **这里必须走泛型分发，不能强转成 `ScalarExpression&lt;object?&gt;`**：`ScalarExpression&lt;T&gt;` 是不变泛型，
    /// `(ScalarExpression&lt;object?&gt;)ScalarExpression&lt;double&gt;` **在运行期必抛 `InvalidCastException`**。
    /// 该缺陷曾让任何含 `Comparison&lt;V&gt;` 的布尔表达式在规范化去重/排序时崩溃——**而它只在有比较项的配方上触发**，
    /// 所以一直没被普通多项式测试发现。
    ///
    /// **Generic dispatch is required here; casting to `ScalarExpression&lt;object?&gt;` is wrong**: `ScalarExpression&lt;T&gt;` is an invariant generic, so
    /// `(ScalarExpression&lt;object?&gt;)ScalarExpression&lt;double&gt;` **always throws `InvalidCastException` at run time**. That defect crashed normalization
    /// (de-duplication and sorting) for any boolean expression containing a `Comparison&lt;V&gt;` — **and it only triggers on fixtures that have
    /// comparisons**, which is why ordinary polynomial tests never caught it.
    /// </remarks>
    private static string ScalarKey(object expression, System.Type valueType) {
        System.Reflection.MethodInfo? method = typeof(StructuralKeyExtensions)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .FirstOrDefault(candidate => candidate.Name == nameof(StructuralKey)
                && candidate.IsGenericMethodDefinition
                && candidate.GetParameters().Length == 1
                && candidate.GetParameters()[0].ParameterType.IsGenericParameter);
        if (method is null) {
            return expression.GetType().Name;
        }
        object? key = method.MakeGenericMethod(valueType).Invoke(null, new[] { expression });
        return key as string ?? expression.GetType().Name;
    }

    /// <summary>获取标量表达式的结构键 / Get structural key of scalar expression.</summary>
    public static string StructuralKey<T>(this ScalarExpression<T> expr) => expr switch {
        ScalarConstant<T> c => $"Const:{c.Value}",
        ScalarReference<T> r => $"Ref:{r.Path}",
        ScalarSymbolReference<T> s => $"SymRef:{SymbolIdentity.Identity(s.Symbol)}",
        ScalarUnary<T> u => $"Unary:{u.Operator}:{u.Operand.StructuralKey()}",
        ScalarBinary<T> b => $"Bin:{b.Operator}:{b.Left.StructuralKey()}:{b.Right.StructuralKey()}",
        ScalarFunction<T> f => $"Func:{f.Name}:{string.Join(",", f.Arguments.Select(a => a.StructuralKey()))}",
        ScalarCustom<T> c => $"Custom:{c.Description}",
        _ => expr.TypeName,
    };
}
