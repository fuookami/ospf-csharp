#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Symbol;

namespace Fuookami.Ospf.Math.Symbol.Expression
{
    /// <summary>
    /// 布尔表达式 / Boolean Expression.
    /// Kotlin sealed interface BooleanExpression; C# abstract record.
    /// 支持比较操作和完整逻辑组合 / Supports comparison and full logical combination (and/or/not).
    /// </summary>
    public abstract record BooleanExpression
    {
        /// <summary>表达式类型名 / Expression type name.</summary>
        public abstract string TypeName { get; }

        /// <summary>子表达式列表 / Child expressions list.</summary>
        public abstract IReadOnlyList<BooleanExpression> Children { get; }

        /// <summary>判断表达式是否是常量 / Check if expression is constant.</summary>
        public virtual bool IsConstant() => false;

        /// <summary>判断表达式是否是纯逻辑表达式 / Check if expression is pure logical (no comparisons etc.).</summary>
        public virtual bool IsPureLogical() => false;

        /// <summary>获取所有引用路径 / Get all reference paths.</summary>
        public IReadOnlySet<PropertyPath> CollectReferences()
        {
            var refs = new HashSet<PropertyPath>();
            CollectReferencesInto(refs);
            return refs;
        }

        /// <summary>将引用路径收集到指定集合 / Collect reference paths into specified collection.</summary>
        public virtual void CollectReferencesInto(HashSet<PropertyPath> refs) { }

        /// <summary>获取逻辑操作符数量 / Get number of logical operators.</summary>
        public virtual int LogicalOperatorCount() => 0;

        /// <summary>获取表达式深度 / Get expression depth.</summary>
        public virtual int Depth() => 1;
    }

    /// <summary>布尔常量 / Boolean Constant. 支持三值逻辑 / Supports three-valued logic.</summary>
    public sealed record BooleanConstant(Trivalent Value) : BooleanExpression
    {
        /// <inheritdoc/>
        public override string TypeName => "BooleanConstant";
        /// <inheritdoc/>
        public override IReadOnlyList<BooleanExpression> Children { get; } = Array.Empty<BooleanExpression>();
        /// <inheritdoc/>
        public override bool IsConstant() => true;
        /// <inheritdoc/>
        public override bool IsPureLogical() => true;

        /// <summary>判断是否为真 / Check if true.</summary>
        public bool IsTrue => Value is Trivalent.True;
        /// <summary>判断是否为假 / Check if false.</summary>
        public bool IsFalse => Value is Trivalent.False;
        /// <summary>判断是否为未知 / Check if unknown.</summary>
        public bool IsUnknown => Value is Trivalent.Unknown;

        /// <inheritdoc/>
        public override string ToString() => Value switch
        {
            Trivalent.True => "true",
            Trivalent.False => "false",
            _ => "unknown",
        };

        /// <summary>创建 true 常量 / Create true constant.</summary>
        public static BooleanConstant True() => new(Trivalent.Invoke(true));
        /// <summary>创建 false 常量 / Create false constant.</summary>
        public static BooleanConstant False() => new(Trivalent.Invoke(false));
        /// <summary>创建 unknown 常量 / Create unknown constant.</summary>
        public static BooleanConstant Unknown() => new(new Trivalent.Unknown());
    }

    /// <summary>比较表达式 / Comparison Expression. 两个标量值之间的比较 / Comparison between two scalar values.</summary>
    public sealed record Comparison<T>(
        ComparisonOperator Operator,
        ScalarExpression<T> Left,
        ScalarExpression<T> Right) : BooleanExpression
    {
        /// <inheritdoc/>
        public override string TypeName => "Comparison";
        /// <inheritdoc/>
        public override IReadOnlyList<BooleanExpression> Children { get; } = Array.Empty<BooleanExpression>();
        /// <inheritdoc/>
        public override bool IsConstant() => Left.IsConstant() && Right.IsConstant();
        /// <inheritdoc/>
        public override void CollectReferencesInto(HashSet<PropertyPath> refs)
        {
            if (Left is ScalarReference<T> lr) refs.Add(lr.Path);
            if (Right is ScalarReference<T> rr) refs.Add(rr.Path);
            Left.CollectReferencesInto(refs);
            Right.CollectReferencesInto(refs);
        }
        /// <inheritdoc/>
        public override string ToString() => $"{Left} {OperatorSymbols.Comparison(Operator)} {Right}";
    }

    /// <summary>集合成员判断表达式 / Set Membership (In) Expression.</summary>
    public sealed record InExpression<T>(
        ScalarExpression<T> Value,
        IReadOnlyList<ScalarExpression<T>> Candidates,
        bool Negated = false) : BooleanExpression
    {
        /// <inheritdoc/>
        public override string TypeName => Negated ? "NotIn" : "In";
        /// <inheritdoc/>
        public override IReadOnlyList<BooleanExpression> Children { get; } = Array.Empty<BooleanExpression>();
        /// <inheritdoc/>
        public override bool IsConstant() => Value.IsConstant();
        /// <inheritdoc/>
        public override void CollectReferencesInto(HashSet<PropertyPath> refs)
        {
            Value.CollectReferencesInto(refs);
            foreach (var c in Candidates) c.CollectReferencesInto(refs);
        }

        /// <summary>判断是否是否定形式 / Check if this is negated form.</summary>
        public bool IsNegated => Negated;
        /// <inheritdoc/>
        public override string ToString() => Negated
            ? $"{Value} not in ({string.Join(", ", Candidates)})"
            : $"{Value} in ({string.Join(", ", Candidates)})";
    }

    /// <summary>模式匹配表达式 / Pattern Match Expression.</summary>
    public sealed record PatternMatch<T>(
        ScalarExpression<T> Value,
        ScalarExpression<T> Pattern,
        PatternMatchMode Mode,
        bool Negated = false) : BooleanExpression
    {
        /// <inheritdoc/>
        public override string TypeName => Negated ? "NotPatternMatch" : "PatternMatch";
        /// <inheritdoc/>
        public override IReadOnlyList<BooleanExpression> Children { get; } = Array.Empty<BooleanExpression>();
        /// <inheritdoc/>
        public override bool IsConstant() => Value.IsConstant();
        /// <inheritdoc/>
        public override void CollectReferencesInto(HashSet<PropertyPath> refs)
        {
            Value.CollectReferencesInto(refs);
        }
        /// <inheritdoc/>
        public override string ToString() => Negated
            ? $"{Value} not {Mode.ToString().ToLowerInvariant()} {Pattern}"
            : $"{Value} {Mode.ToString().ToLowerInvariant()} {Pattern}";
    }

    /// <summary>空值检查表达式 / Null Check Expression.</summary>
    public sealed record NullCheck(
        PropertyPath Path,
        NullCheckType Type,
        PathSymbol? Symbol = null) : BooleanExpression
    {
        /// <summary>路径符号 / Path symbol.</summary>
        public PathSymbol Symbol { get; } = Symbol ?? Path.ToPathSymbol();
        /// <inheritdoc/>
        public override string TypeName => "NullCheck";
        /// <inheritdoc/>
        public override IReadOnlyList<BooleanExpression> Children { get; } = Array.Empty<BooleanExpression>();
        /// <inheritdoc/>
        public override void CollectReferencesInto(HashSet<PropertyPath> refs) => refs.Add(Path);

        /// <summary>判断是否检查空值 / Check if this checks for null.</summary>
        public bool IsNull => Type == NullCheckType.IsNull;
        /// <summary>判断是否检查非空 / Check if this checks for not null.</summary>
        public bool IsNotNull => Type == NullCheckType.IsNotNull;
        /// <inheritdoc/>
        public override string ToString() => $"{Path.Value} {OperatorSymbols.NullCheck(Type)}";
    }

    /// <summary>逻辑与表达式 / Logical AND Expression. 操作数至少一个 / At least one operand.</summary>
    public sealed record AndExpression : BooleanExpression
    {
        /// <summary>操作数列表 / Operands list.</summary>
        public IReadOnlyList<BooleanExpression> Operands { get; }

        /// <inheritdoc/>
        public override string TypeName => "And";
        /// <inheritdoc/>
        public override IReadOnlyList<BooleanExpression> Children => Operands;
        /// <inheritdoc/>
        public override bool IsConstant() => Operands.All(o => o.IsConstant());
        /// <inheritdoc/>
        public override bool IsPureLogical() => Operands.All(o => o.IsPureLogical());
        /// <inheritdoc/>
        public override void CollectReferencesInto(HashSet<PropertyPath> refs)
        {
            foreach (var o in Operands) o.CollectReferencesInto(refs);
        }
        /// <inheritdoc/>
        public override int LogicalOperatorCount() => Operands.Count + Operands.Sum(o => o.LogicalOperatorCount());
        /// <inheritdoc/>
        public override int Depth() => 1 + (Operands.Max(o => (int?)o.Depth()) ?? 0);
        /// <inheritdoc/>
        public override string ToString() => string.Join(" and ", Operands.Select(o => $"({o})"));

        /// <summary>构造逻辑与表达式 / Construct AND expression.</summary>
        public AndExpression(IReadOnlyList<BooleanExpression> operands)
        {
            if (operands.Count == 0) throw new ArgumentException("And expression requires at least one operand", nameof(operands));
            Operands = operands;
        }
    }

    /// <summary>逻辑或表达式 / Logical OR Expression. 操作数至少一个 / At least one operand.</summary>
    public sealed record OrExpression : BooleanExpression
    {
        /// <summary>操作数列表 / Operands list.</summary>
        public IReadOnlyList<BooleanExpression> Operands { get; }

        /// <inheritdoc/>
        public override string TypeName => "Or";
        /// <inheritdoc/>
        public override IReadOnlyList<BooleanExpression> Children => Operands;
        /// <inheritdoc/>
        public override bool IsConstant() => Operands.All(o => o.IsConstant());
        /// <inheritdoc/>
        public override bool IsPureLogical() => Operands.All(o => o.IsPureLogical());
        /// <inheritdoc/>
        public override void CollectReferencesInto(HashSet<PropertyPath> refs)
        {
            foreach (var o in Operands) o.CollectReferencesInto(refs);
        }
        /// <inheritdoc/>
        public override int LogicalOperatorCount() => Operands.Count + Operands.Sum(o => o.LogicalOperatorCount());
        /// <inheritdoc/>
        public override int Depth() => 1 + (Operands.Max(o => (int?)o.Depth()) ?? 0);
        /// <inheritdoc/>
        public override string ToString() => string.Join(" or ", Operands.Select(o => $"({o})"));

        /// <summary>构造逻辑或表达式 / Construct OR expression.</summary>
        public OrExpression(IReadOnlyList<BooleanExpression> operands)
        {
            if (operands.Count == 0) throw new ArgumentException("Or expression requires at least one operand", nameof(operands));
            Operands = operands;
        }
    }

    /// <summary>逻辑非表达式 / Logical NOT Expression.</summary>
    public sealed record NotExpression(BooleanExpression Operand) : BooleanExpression
    {
        /// <inheritdoc/>
        public override string TypeName => "Not";
        /// <inheritdoc/>
        public override IReadOnlyList<BooleanExpression> Children { get; } = new[] { Operand };
        /// <inheritdoc/>
        public override bool IsConstant() => Operand.IsConstant();
        /// <inheritdoc/>
        public override bool IsPureLogical() => Operand.IsPureLogical();
        /// <inheritdoc/>
        public override void CollectReferencesInto(HashSet<PropertyPath> refs) => Operand.CollectReferencesInto(refs);
        /// <inheritdoc/>
        public override int LogicalOperatorCount() => 1 + Operand.LogicalOperatorCount();
        /// <inheritdoc/>
        public override int Depth() => 1 + Operand.Depth();
        /// <inheritdoc/>
        public override string ToString() => $"not ({Operand})";
    }

    /// <summary>布尔自定义表达式 / Boolean Custom Expression. 用于扩展 / For extension.</summary>
    public sealed record BooleanCustom(object Value, string? Description = null) : BooleanExpression
    {
        /// <inheritdoc/>
        public override string TypeName => "Custom";
        /// <inheritdoc/>
        public override IReadOnlyList<BooleanExpression> Children { get; } = Array.Empty<BooleanExpression>();
        /// <inheritdoc/>
        public override string ToString() => Description ?? $"Custom({Value})";
    }

    /// <summary>
    /// 布尔表达式工厂 / Boolean Expression Factory.
    /// Kotlin object; C# static class.
    /// </summary>
    public static class BooleanExpressionFactory
    {
        /// <summary>创建布尔常量 / Create boolean constant.</summary>
        public static BooleanConstant Constant(Trivalent value) => new(value);
        /// <summary>创建 true 常量 / Create true constant.</summary>
        public static BooleanConstant TrueConstant() => BooleanConstant.True();
        /// <summary>创建 false 常量 / Create false constant.</summary>
        public static BooleanConstant FalseConstant() => BooleanConstant.False();
        /// <summary>创建 unknown 常量 / Create unknown constant.</summary>
        public static BooleanConstant UnknownConstant() => BooleanConstant.Unknown();
        /// <summary>创建比较表达式 / Create comparison expression.</summary>
        public static Comparison<T> Comparison<T>(ComparisonOperator op, ScalarExpression<T> left, ScalarExpression<T> right)
            => new(op, left, right);
        /// <summary>创建等于表达式 / Create equals expression.</summary>
        public static Comparison<T> Eq<T>(ScalarExpression<T> l, ScalarExpression<T> r) => Comparison(ComparisonOperator.Eq, l, r);
        /// <summary>创建不等于表达式 / Create not equals expression.</summary>
        public static Comparison<T> Ne<T>(ScalarExpression<T> l, ScalarExpression<T> r) => Comparison(ComparisonOperator.Ne, l, r);
        /// <summary>创建小于表达式 / Create less than expression.</summary>
        public static Comparison<T> Lt<T>(ScalarExpression<T> l, ScalarExpression<T> r) => Comparison(ComparisonOperator.Lt, l, r);
        /// <summary>创建小于等于表达式 / Create less than or equal expression.</summary>
        public static Comparison<T> Le<T>(ScalarExpression<T> l, ScalarExpression<T> r) => Comparison(ComparisonOperator.Le, l, r);
        /// <summary>创建大于表达式 / Create greater than expression.</summary>
        public static Comparison<T> Gt<T>(ScalarExpression<T> l, ScalarExpression<T> r) => Comparison(ComparisonOperator.Gt, l, r);
        /// <summary>创建大于等于表达式 / Create greater than or equal expression.</summary>
        public static Comparison<T> Ge<T>(ScalarExpression<T> l, ScalarExpression<T> r) => Comparison(ComparisonOperator.Ge, l, r);
        /// <summary>创建 In 表达式 / Create In expression.</summary>
        public static InExpression<T> InExpr<T>(ScalarExpression<T> value, IReadOnlyList<ScalarExpression<T>> candidates, bool negated = false)
            => new(value, candidates, negated);
        /// <summary>创建 Not In 表达式 / Create Not In expression.</summary>
        public static InExpression<T> NotIn<T>(ScalarExpression<T> value, IReadOnlyList<ScalarExpression<T>> candidates)
            => new(value, candidates, true);
        /// <summary>创建模式匹配表达式 / Create pattern match expression.</summary>
        public static PatternMatch<T> PatternMatch<T>(ScalarExpression<T> value, ScalarExpression<T> pattern, PatternMatchMode mode, bool negated = false)
            => new(value, pattern, mode, negated);
        /// <summary>创建空值检查表达式 / Create null check expression.</summary>
        public static NullCheck IsNull(PropertyPath path) => new(path, NullCheckType.IsNull);
        /// <summary>创建非空检查表达式 / Create not null check expression.</summary>
        public static NullCheck IsNotNull(PropertyPath path) => new(path, NullCheckType.IsNotNull);
        /// <summary>创建逻辑与表达式 / Create AND expression.</summary>
        public static AndExpression And(IReadOnlyList<BooleanExpression> operands) => new(operands);
        /// <summary>创建逻辑与表达式（可变参数）/ Create AND expression (vararg).</summary>
        public static AndExpression And(BooleanExpression first, BooleanExpression second, params BooleanExpression[] rest)
            => new(new[] { first, second }.Concat(rest).ToList());
        /// <summary>创建逻辑或表达式 / Create OR expression.</summary>
        public static OrExpression Or(IReadOnlyList<BooleanExpression> operands) => new(operands);
        /// <summary>创建逻辑或表达式（可变参数）/ Create OR expression (vararg).</summary>
        public static OrExpression Or(BooleanExpression first, BooleanExpression second, params BooleanExpression[] rest)
            => new(new[] { first, second }.Concat(rest).ToList());
        /// <summary>创建逻辑非表达式 / Create NOT expression.</summary>
        public static NotExpression Not(BooleanExpression operand) => new(operand);
    }
}
