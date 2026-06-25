#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Symbol.Expression.Dsl
{
    /// <summary>
    /// 布尔表达式构建器 / Boolean Expression Builder.
    /// </summary>
    public sealed class BooleanExpressionBuilder
    {
        internal BooleanExpression? _expression;

        /// <summary>构建最终的布尔表达式，未设置时返回 null / Build, or null when unset.</summary>
        public BooleanExpression? BuildOrNull() => _expression;

        /// <summary>构建最终的布尔表达式 / Build; Failed when unset.</summary>
        public Result<BooleanExpression, ErrorCode, Error<ErrorCode>> Build() =>
            _expression is { } e
                ? new Ok<BooleanExpression, ErrorCode, Error<ErrorCode>>(e)
                : new Failed<BooleanExpression, ErrorCode, Error<ErrorCode>>(ErrorCode.ApplicationError, "No expression built.");

        internal void Add(BooleanExpression expr) => _expression = expr;
    }

    /// <summary>标量表达式构建器 / Scalar Expression Builder.</summary>
    public sealed class ScalarExpressionBuilder<T>
    {
        internal ScalarExpression<T>? _expression;

        /// <summary>构建最终的标量表达式，未设置时返回 null / Build, or null when unset.</summary>
        public ScalarExpression<T>? BuildOrNull() => _expression;

        /// <summary>构建最终的标量表达式 / Build; Failed when unset.</summary>
        public Result<ScalarExpression<T>, ErrorCode, Error<ErrorCode>> Build() =>
            _expression is { } e
                ? new Ok<ScalarExpression<T>, ErrorCode, Error<ErrorCode>>(e)
                : new Failed<ScalarExpression<T>, ErrorCode, Error<ErrorCode>>(ErrorCode.ApplicationError, "No expression built.");
    }

    /// <summary>
    /// 路径引用构建器 / Path Reference Builder (string-based path).
    /// Kotlin infix eq/ne/lt/... ; C# instance methods (overloads per primitive).
    /// </summary>
    public sealed class PathBuilder
    {
        internal PropertyPath Path { get; }

        internal PathBuilder(PropertyPath path) => Path = path;

        /// <summary>转为标量引用 / Convert to scalar reference.</summary>
        public ScalarReference<object?> AsScalar() => new(Path);

        /// <summary>等于比较（String）/ Equal comparison (String).</summary>
        public Comparison<string> Eq(string value) => new(ComparisonOperator.Eq, new ScalarReference<string>(Path), new ScalarConstant<string>(value));
        /// <summary>等于比较（Int32）/ Equal comparison (Int32).</summary>
        public Comparison<int> Eq(int value) => new(ComparisonOperator.Eq, new ScalarReference<int>(Path), new ScalarConstant<int>(value));
        /// <summary>等于比较（Int64）/ Equal comparison (Int64).</summary>
        public Comparison<long> Eq(long value) => new(ComparisonOperator.Eq, new ScalarReference<long>(Path), new ScalarConstant<long>(value));
        /// <summary>等于比较（Double）/ Equal comparison (Double).</summary>
        public Comparison<double> Eq(double value) => new(ComparisonOperator.Eq, new ScalarReference<double>(Path), new ScalarConstant<double>(value));
        /// <summary>等于比较（Boolean）/ Equal comparison (Boolean).</summary>
        public Comparison<bool> Eq(bool value) => new(ComparisonOperator.Eq, new ScalarReference<bool>(Path), new ScalarConstant<bool>(value));

        /// <summary>不等于比较（String）/ Not equal comparison (String).</summary>
        public Comparison<string> Ne(string value) => new(ComparisonOperator.Ne, new ScalarReference<string>(Path), new ScalarConstant<string>(value));
        /// <summary>不等于比较（Int32）/ Not equal comparison (Int32).</summary>
        public Comparison<int> Ne(int value) => new(ComparisonOperator.Ne, new ScalarReference<int>(Path), new ScalarConstant<int>(value));
        /// <summary>不等于比较（Int64）/ Not equal comparison (Int64).</summary>
        public Comparison<long> Ne(long value) => new(ComparisonOperator.Ne, new ScalarReference<long>(Path), new ScalarConstant<long>(value));
        /// <summary>不等于比较（Double）/ Not equal comparison (Double).</summary>
        public Comparison<double> Ne(double value) => new(ComparisonOperator.Ne, new ScalarReference<double>(Path), new ScalarConstant<double>(value));

        /// <summary>小于比较（Int32）/ Less than comparison (Int32).</summary>
        public Comparison<int> Lt(int value) => new(ComparisonOperator.Lt, new ScalarReference<int>(Path), new ScalarConstant<int>(value));
        /// <summary>小于比较（Int64）/ Less than comparison (Int64).</summary>
        public Comparison<long> Lt(long value) => new(ComparisonOperator.Lt, new ScalarReference<long>(Path), new ScalarConstant<long>(value));
        /// <summary>小于比较（Double）/ Less than comparison (Double).</summary>
        public Comparison<double> Lt(double value) => new(ComparisonOperator.Lt, new ScalarReference<double>(Path), new ScalarConstant<double>(value));

        /// <summary>小于等于比较（Int32）/ Less than or equal comparison (Int32).</summary>
        public Comparison<int> Le(int value) => new(ComparisonOperator.Le, new ScalarReference<int>(Path), new ScalarConstant<int>(value));
        /// <summary>小于等于比较（Int64）/ Less than or equal comparison (Int64).</summary>
        public Comparison<long> Le(long value) => new(ComparisonOperator.Le, new ScalarReference<long>(Path), new ScalarConstant<long>(value));
        /// <summary>小于等于比较（Double）/ Less than or equal comparison (Double).</summary>
        public Comparison<double> Le(double value) => new(ComparisonOperator.Le, new ScalarReference<double>(Path), new ScalarConstant<double>(value));

        /// <summary>大于比较（Int32）/ Greater than comparison (Int32).</summary>
        public Comparison<int> Gt(int value) => new(ComparisonOperator.Gt, new ScalarReference<int>(Path), new ScalarConstant<int>(value));
        /// <summary>大于比较（Int64）/ Greater than comparison (Int64).</summary>
        public Comparison<long> Gt(long value) => new(ComparisonOperator.Gt, new ScalarReference<long>(Path), new ScalarConstant<long>(value));
        /// <summary>大于比较（Double）/ Greater than comparison (Double).</summary>
        public Comparison<double> Gt(double value) => new(ComparisonOperator.Gt, new ScalarReference<double>(Path), new ScalarConstant<double>(value));

        /// <summary>大于等于比较（Int32）/ Greater than or equal comparison (Int32).</summary>
        public Comparison<int> Ge(int value) => new(ComparisonOperator.Ge, new ScalarReference<int>(Path), new ScalarConstant<int>(value));
        /// <summary>大于等于比较（Int64）/ Greater than or equal comparison (Int64).</summary>
        public Comparison<long> Ge(long value) => new(ComparisonOperator.Ge, new ScalarReference<long>(Path), new ScalarConstant<long>(value));
        /// <summary>大于等于比较（Double）/ Greater than or equal comparison (Double).</summary>
        public Comparison<double> Ge(double value) => new(ComparisonOperator.Ge, new ScalarReference<double>(Path), new ScalarConstant<double>(value));

        /// <summary>集合成员判断（String）/ Set membership (String).</summary>
        public InExpression<string> InValues(params string[] values) => new(new ScalarReference<string>(Path), values.Select(v => (ScalarExpression<string>)new ScalarConstant<string>(v)).ToList());
        /// <summary>集合成员判断（Int32）/ Set membership (Int32).</summary>
        public InExpression<int> InValues(params int[] values) => new(new ScalarReference<int>(Path), values.Select(v => (ScalarExpression<int>)new ScalarConstant<int>(v)).ToList());
        /// <summary>集合成员判断（Int64）/ Set membership (Int64).</summary>
        public InExpression<long> InValues(params long[] values) => new(new ScalarReference<long>(Path), values.Select(v => (ScalarExpression<long>)new ScalarConstant<long>(v)).ToList());

        /// <summary>非集合成员判断（String）/ Negated set membership (String).</summary>
        public InExpression<string> NotInValues(params string[] values) => new(new ScalarReference<string>(Path), values.Select(v => (ScalarExpression<string>)new ScalarConstant<string>(v)).ToList(), true);
        /// <summary>非集合成员判断（Int32）/ Negated set membership (Int32).</summary>
        public InExpression<int> NotInValues(params int[] values) => new(new ScalarReference<int>(Path), values.Select(v => (ScalarExpression<int>)new ScalarConstant<int>(v)).ToList(), true);

        /// <summary>空值检查 / Null check.</summary>
        public NullCheck IsNull() => new(Path, NullCheckType.IsNull);
        /// <summary>非空检查 / Not null check.</summary>
        public NullCheck IsNotNull() => new(Path, NullCheckType.IsNotNull);

        /// <summary>LIKE 模式匹配 / LIKE pattern match.</summary>
        public PatternMatch<string> Like(string pattern) => new(new ScalarReference<string>(Path), new ScalarConstant<string>(pattern), PatternMatchMode.Like);
        /// <summary>精确匹配 / Exact match.</summary>
        public PatternMatch<string> LikeExact(string pattern) => new(new ScalarReference<string>(Path), new ScalarConstant<string>(pattern), PatternMatchMode.Exact);
        /// <summary>前缀匹配 / Prefix match.</summary>
        public PatternMatch<string> LikePrefix(string pattern) => new(new ScalarReference<string>(Path), new ScalarConstant<string>(pattern), PatternMatchMode.Prefix);
        /// <summary>后缀匹配 / Suffix match.</summary>
        public PatternMatch<string> LikeSuffix(string pattern) => new(new ScalarReference<string>(Path), new ScalarConstant<string>(pattern), PatternMatchMode.Suffix);
        /// <summary>包含匹配 / Contains match.</summary>
        public PatternMatch<string> LikeContains(string pattern) => new(new ScalarReference<string>(Path), new ScalarConstant<string>(pattern), PatternMatchMode.Contains);
        /// <summary>非 LIKE 模式匹配 / Negated LIKE pattern match.</summary>
        public PatternMatch<string> NotLike(string pattern) => new(new ScalarReference<string>(Path), new ScalarConstant<string>(pattern), PatternMatchMode.Like, true);
    }

    /// <summary>
    /// 成员路径提取器 / Member-path extractor.
    /// Walks an Expression&lt;Func&lt;E,T&gt;&gt; body (Parameter → Member* chain) into a PropertyPath.
    /// Replaces Kotlin KProperty1.name reflection.
    /// </summary>
    public static class MemberPathExtractor
    {
        /// <summary>命名策略 / Naming policy.</summary>
        public static IPropertyNamingPolicy NamingPolicy { get; set; } = CamelCaseNamingPolicy.Instance;

        /// <summary>从表达式树提取属性路径 / Extract property path from an expression tree.</summary>
        public static PropertyPath Extract<E, T>(Expression<Func<E, T>> lambda)
        {
            var segments = new List<string>();
            var node = lambda.Body;
            // Unwrap implicit convert (value-type boxing / numeric conversions)
            while (node is UnaryExpression { NodeType: ExpressionType.Convert } conv)
                node = conv.Operand;

            while (node is MemberExpression me)
            {
                segments.Insert(0, NamingPolicy.Convert(me.Member.Name));
                node = me.Expression;
            }

            if (node is not ParameterExpression)
                throw new ArgumentException(
                    $"Expression '{lambda}' is not a simple member path (x => x.A.B).",
                    nameof(lambda));

            return segments.Count == 0
                ? throw new ArgumentException("Empty member path.", nameof(lambda))
                : PropertyPath.Of(segments);
        }
    }

    /// <summary>属性命名策略接口 / Property naming policy interface.</summary>
    public interface IPropertyNamingPolicy
    {
        /// <summary>转换成员名 / Convert member name.</summary>
        string Convert(string memberName);
    }

    /// <summary>camelCase 命名策略 / camelCase naming policy.</summary>
    internal sealed class CamelCaseNamingPolicy : IPropertyNamingPolicy
    {
        public static readonly CamelCaseNamingPolicy Instance = new();
        public string Convert(string memberName) =>
            memberName.Length > 0 && char.IsUpper(memberName[0])
                ? char.ToLowerInvariant(memberName[0]) + memberName[1..]
                : memberName;
    }

    /// <summary>
    /// 类型化路径引用构建器 / Typed path reference builder.
    /// Kotlin TypedPathBuilder&lt;E,T&gt;(val property: KProperty1&lt;E,T&gt;).
    /// C# takes an expression tree Expression&lt;Func&lt;E,T&gt;&gt;.
    /// </summary>
    public sealed class TypedPathBuilder<E, T>
    {
        /// <summary>属性引用（表达式树）/ Property reference (expression tree).</summary>
        public Expression<Func<E, T>> Property { get; }

        /// <summary>属性路径 / Property path.</summary>
        public PropertyPath Path { get; }

        public TypedPathBuilder(Expression<Func<E, T>> property, PropertyPath? path = null)
        {
            Property = property;
            Path = path ?? MemberPathExtractor.Extract(property);
        }

        /// <summary>转为标量引用 / Convert to scalar reference.</summary>
        public ScalarReference<T> AsScalar() => new(Path);

        /// <summary>等于比较（值）/ Equal comparison (value).</summary>
        public Comparison<T> Eq(T value) => new(ComparisonOperator.Eq, AsScalar(), new ScalarConstant<T>(value));
        /// <summary>不等于比较（值）/ Not equal comparison (value).</summary>
        public Comparison<T> Ne(T value) => new(ComparisonOperator.Ne, AsScalar(), new ScalarConstant<T>(value));
        /// <summary>等于比较（列）/ Equal comparison (column).</summary>
        public Comparison<T> Eq(TypedPathBuilder<E, T> other) => new(ComparisonOperator.Eq, AsScalar(), other.AsScalar());
        /// <summary>不等于比较（列）/ Not equal comparison (column).</summary>
        public Comparison<T> Ne(TypedPathBuilder<E, T> other) => new(ComparisonOperator.Ne, AsScalar(), other.AsScalar());

        /// <summary>集合成员判断 / Set membership.</summary>
        public InExpression<T> InValues(params T[] values) => new(AsScalar(), values.Select(v => (ScalarExpression<T>)new ScalarConstant<T>(v)).ToList());
        /// <summary>非集合成员判断 / Negated set membership.</summary>
        public InExpression<T> NotInValues(params T[] values) => new(AsScalar(), values.Select(v => (ScalarExpression<T>)new ScalarConstant<T>(v)).ToList(), true);
    }

    /// <summary>
    /// 手写谓词 schema / Hand-written predicate schema.
    /// Kotlin abstract class PredicateSchema&lt;E&gt; with protected field(KProperty1).
    /// C# protected Field(Expression&lt;Func&lt;E,T&gt;&gt;) builds a TypedPathBuilder.
    /// </summary>
    public abstract class PredicateSchema<E>
    {
        /// <summary>创建类型化字段 / Create typed field.</summary>
        protected TypedPathBuilder<E, T> Field<T>(Expression<Func<E, T>> property) => ExpressionDsl.Prop(property);
    }

    /// <summary>
    /// 表达式 DSL 入口与便捷构造 / Expression DSL entry + convenience constructors.
    /// Kotlin top-level fns; C# static class.
    /// </summary>
    public static class ExpressionDsl
    {
        /// <summary>布尔表达式 DSL 入口 / Boolean expression DSL entry.</summary>
        public static Result<BooleanExpression, ErrorCode, Error<ErrorCode>> Build(Func<BooleanExpressionBuilder, BooleanExpression> block)
        {
            var builder = new BooleanExpressionBuilder();
            builder.Add(block(builder));
            return builder.Build();
        }

        /// <summary>创建路径引用 / Create path reference.</summary>
        public static PathBuilder Path(string name) => new(PropertyPath.Parse(name));

        /// <summary>创建属性引用 / Create property reference (expression-tree member path).</summary>
        public static TypedPathBuilder<E, T> Prop<E, T>(Expression<Func<E, T>> property) => new(property);

        /// <summary>创建标量引用 / Create scalar reference.</summary>
        public static ScalarReference<T> ScalarPath<T>(string name) => new(PropertyPath.Parse(name));

        /// <summary>创建布尔常量 / Create boolean constant.</summary>
        public static BooleanConstant Bool(bool value) => new(Trivalent.Invoke(value));

        /// <summary>创建布尔常量（三值逻辑）/ Create boolean constant (three-valued).</summary>
        public static BooleanConstant TrivalentValue(bool? value) => new(Math.Trivalent.Invoke(value));

        /// <summary>快速创建比较表达式 / Quick create comparison expression.</summary>
        public static Comparison<T> Compare<T>(string path, ComparisonOperator op, T value)
        {
            var reference = (ScalarExpression<T>)new ScalarReference<T>(PropertyPath.Parse(path));
            var constant = (ScalarExpression<T>)new ScalarConstant<T>(value);
            return new Comparison<T>(op, reference, constant);
        }

        /// <summary>快速等于 / Quick equals.</summary>
        public static Comparison<T> Eq<T>(string path, T value) => Compare(path, ComparisonOperator.Eq, value);
        /// <summary>快速不等于 / Quick not equals.</summary>
        public static Comparison<T> Ne<T>(string path, T value) => Compare(path, ComparisonOperator.Ne, value);
        /// <summary>快速小于 / Quick less than.</summary>
        public static Comparison<T> Lt<T>(string path, T value) => Compare(path, ComparisonOperator.Lt, value);
        /// <summary>快速小于等于 / Quick less than or equal.</summary>
        public static Comparison<T> Le<T>(string path, T value) => Compare(path, ComparisonOperator.Le, value);
        /// <summary>快速大于 / Quick greater than.</summary>
        public static Comparison<T> Gt<T>(string path, T value) => Compare(path, ComparisonOperator.Gt, value);
        /// <summary>快速大于等于 / Quick greater than or equal.</summary>
        public static Comparison<T> Ge<T>(string path, T value) => Compare(path, ComparisonOperator.Ge, value);

        /// <summary>快速 In 表达式 / Quick In expression.</summary>
        public static InExpression<T> InExpr<T>(string path, IReadOnlyList<T> values) =>
            new(new ScalarReference<T>(PropertyPath.Parse(path)), values.Select(v => (ScalarExpression<T>)new ScalarConstant<T>(v)).ToList());

        /// <summary>快速 Not In 表达式 / Quick Not In expression.</summary>
        public static InExpression<T> NotInExpr<T>(string path, IReadOnlyList<T> values) =>
            new(new ScalarReference<T>(PropertyPath.Parse(path)), values.Select(v => (ScalarExpression<T>)new ScalarConstant<T>(v)).ToList(), true);

        /// <summary>快速空值检查 / Quick null check.</summary>
        public static NullCheck IsNull(string path) => new(PropertyPath.Parse(path), NullCheckType.IsNull);

        /// <summary>快速非空检查 / Quick not null check.</summary>
        public static NullCheck IsNotNull(string path) => new(PropertyPath.Parse(path), NullCheckType.IsNotNull);

        /// <summary>快速逻辑与 / Quick AND.</summary>
        public static AndExpression And(BooleanExpression first, BooleanExpression second, params BooleanExpression[] rest) =>
            new(new[] { first, second }.Concat(rest).ToList());

        /// <summary>快速逻辑或 / Quick OR.</summary>
        public static OrExpression Or(BooleanExpression first, BooleanExpression second, params BooleanExpression[] rest) =>
            new(new[] { first, second }.Concat(rest).ToList());

        /// <summary>快速逻辑非 / Quick NOT.</summary>
        public static NotExpression NotExpr(BooleanExpression expression) => new(expression);

        /// <summary>绝对值函数 / Absolute value function.</summary>
        public static ScalarFunction<object?> Abs(ScalarExpression<object?> expr) =>
            new(ScalarFunctionNames.Abs, new[] { expr });

        /// <summary>绝对值函数（路径形式）/ Absolute value function (path form).</summary>
        public static ScalarFunction<object?> Abs(PathBuilder path) => Abs(path.AsScalar());
    }

    /// <summary>
    /// 布尔表达式逻辑运算扩展 / BooleanExpression logical-operator extensions.
    /// Kotlin infix and/or + operator fun not(); C# static extension class.
    /// </summary>
    public static class BooleanExpressionDslExtensions
    {
        /// <summary>逻辑与 / Logical AND (flattens nested And).</summary>
        public static AndExpression And(this BooleanExpression self, BooleanExpression other)
        {
            var operands = new List<BooleanExpression>();
            if (self is AndExpression sa) operands.AddRange(sa.Operands); else operands.Add(self);
            if (other is AndExpression oa) operands.AddRange(oa.Operands); else operands.Add(other);
            return new AndExpression(operands);
        }

        /// <summary>逻辑或 / Logical OR (flattens nested Or).</summary>
        public static OrExpression Or(this BooleanExpression self, BooleanExpression other)
        {
            var operands = new List<BooleanExpression>();
            if (self is OrExpression so) operands.AddRange(so.Operands); else operands.Add(self);
            if (other is OrExpression oo) operands.AddRange(oo.Operands); else operands.Add(other);
            return new OrExpression(operands);
        }

        /// <summary>逻辑非 / Logical NOT (operator).</summary>
        public static NotExpression Not(this BooleanExpression operand) => new(operand);
    }

    /// <summary>
    /// 使用具体 schema 类型构造谓词 / Build predicate with concrete schema type.
    /// </summary>
    public static class PredicateSchemaExtensions
    {
        /// <summary>使用 schema 构造谓词 / Build predicate using schema.</summary>
        public static BooleanExpression Predicate<E, S>(this S schema, Func<S, BooleanExpression> block)
            where S : PredicateSchema<E>
            => block(schema);
    }

    /// <summary>
    /// TypedPathBuilder 可比较操作扩展 / TypedPathBuilder comparable operations.
    /// </summary>
    public static class TypedPathBuilderComparableExtensions
    {
        /// <summary>小于比较（值）/ Less than (value).</summary>
        public static Comparison<T> Lt<E, T>(this TypedPathBuilder<E, T> self, T value) where T : IComparable<T>
            => new(ComparisonOperator.Lt, self.AsScalar(), new ScalarConstant<T>(value));

        /// <summary>小于等于比较（值）/ Less than or equal (value).</summary>
        public static Comparison<T> Le<E, T>(this TypedPathBuilder<E, T> self, T value) where T : IComparable<T>
            => new(ComparisonOperator.Le, self.AsScalar(), new ScalarConstant<T>(value));

        /// <summary>大于比较（值）/ Greater than (value).</summary>
        public static Comparison<T> Gt<E, T>(this TypedPathBuilder<E, T> self, T value) where T : IComparable<T>
            => new(ComparisonOperator.Gt, self.AsScalar(), new ScalarConstant<T>(value));

        /// <summary>大于等于比较（值）/ Greater than or equal (value).</summary>
        public static Comparison<T> Ge<E, T>(this TypedPathBuilder<E, T> self, T value) where T : IComparable<T>
            => new(ComparisonOperator.Ge, self.AsScalar(), new ScalarConstant<T>(value));

        /// <summary>小于比较（列）/ Less than (column).</summary>
        public static Comparison<T> Lt<E, T>(this TypedPathBuilder<E, T> self, TypedPathBuilder<E, T> other) where T : IComparable<T>
            => new(ComparisonOperator.Lt, self.AsScalar(), other.AsScalar());

        /// <summary>小于等于比较（列）/ Less than or equal (column).</summary>
        public static Comparison<T> Le<E, T>(this TypedPathBuilder<E, T> self, TypedPathBuilder<E, T> other) where T : IComparable<T>
            => new(ComparisonOperator.Le, self.AsScalar(), other.AsScalar());

        /// <summary>大于比较（列）/ Greater than (column).</summary>
        public static Comparison<T> Gt<E, T>(this TypedPathBuilder<E, T> self, TypedPathBuilder<E, T> other) where T : IComparable<T>
            => new(ComparisonOperator.Gt, self.AsScalar(), other.AsScalar());

        /// <summary>大于等于比较（列）/ Greater than or equal (column).</summary>
        public static Comparison<T> Ge<E, T>(this TypedPathBuilder<E, T> self, TypedPathBuilder<E, T> other) where T : IComparable<T>
            => new(ComparisonOperator.Ge, self.AsScalar(), other.AsScalar());

        /// <summary>空值检查 / Null check (nullable property).</summary>
        public static NullCheck IsNull<E, T>(this TypedPathBuilder<E, T?> self) where T : class
            => new(self.Path, NullCheckType.IsNull);

        /// <summary>非空检查 / Not null check (nullable property).</summary>
        public static NullCheck IsNotNull<E, T>(this TypedPathBuilder<E, T?> self) where T : class
            => new(self.Path, NullCheckType.IsNotNull);

        /// <summary>LIKE 模式匹配 / LIKE pattern match.</summary>
        public static PatternMatch<string> Like<E>(this TypedPathBuilder<E, string> self, string pattern)
            => new(self.AsScalar(), new ScalarConstant<string>(pattern), PatternMatchMode.Like);

        /// <summary>精确匹配 / Exact match.</summary>
        public static PatternMatch<string> LikeExact<E>(this TypedPathBuilder<E, string> self, string pattern)
            => new(self.AsScalar(), new ScalarConstant<string>(pattern), PatternMatchMode.Exact);

        /// <summary>前缀匹配 / Prefix match.</summary>
        public static PatternMatch<string> LikePrefix<E>(this TypedPathBuilder<E, string> self, string pattern)
            => new(self.AsScalar(), new ScalarConstant<string>(pattern), PatternMatchMode.Prefix);

        /// <summary>后缀匹配 / Suffix match.</summary>
        public static PatternMatch<string> LikeSuffix<E>(this TypedPathBuilder<E, string> self, string pattern)
            => new(self.AsScalar(), new ScalarConstant<string>(pattern), PatternMatchMode.Suffix);

        /// <summary>包含匹配 / Contains match.</summary>
        public static PatternMatch<string> LikeContains<E>(this TypedPathBuilder<E, string> self, string pattern)
            => new(self.AsScalar(), new ScalarConstant<string>(pattern), PatternMatchMode.Contains);

        /// <summary>非 LIKE 模式匹配 / Negated LIKE pattern match.</summary>
        public static PatternMatch<string> NotLike<E>(this TypedPathBuilder<E, string> self, string pattern)
            => new(self.AsScalar(), new ScalarConstant<string>(pattern), PatternMatchMode.Like, true);
    }
}
