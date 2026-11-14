#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Model.ConstraintProgramming;

/// <summary>整数关系 / Integer relation.</summary>
public enum ConstraintProgrammingComparison {
    /// <summary>等于 / Equal.</summary>
    Equal,
    /// <summary>小于等于 / Less than or equal.</summary>
    LessOrEqual,
    /// <summary>大于等于 / Greater than or equal.</summary>
    GreaterOrEqual,
}

/// <summary>Reification 方向 / Reification direction.</summary>
public enum ReificationDirection {
    /// <summary>文字为真时启用约束 / The literal enables the constraint.</summary>
    Implies,
    /// <summary>约束成立时要求文字为真 / The constraint implies the literal.</summary>
    ImpliedBy,
    /// <summary>文字与约束等价 / The literal and constraint are equivalent.</summary>
    Equivalent,
}

/// <summary>
/// CP 约束统一接口，提供与求解器无关的求值契约。
/// Solver-neutral CP constraint contract with evaluation.
/// </summary>
public abstract record ConstraintProgrammingConstraint {
    /// <summary>约束引用的稳定变量 ID / Stable variable IDs referenced by the constraint.</summary>
    public abstract IReadOnlySet<VariableId> Variables { get; }

    /// <summary>使用整数赋值验证约束；布尔变量必须为 0 或 1 / Check the constraint against integer assignments; Boolean variables use 0 or 1.</summary>
    /// <param name="values">变量赋值 / Variable assignments.</param>
    /// <returns>是否满足或结构化错误 / Whether satisfied, or a structured error.</returns>
    public abstract Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values);

    /// <summary>整数等式或不等式 / Integer equality or inequality.</summary>
    public sealed record IntegerComparison : ConstraintProgrammingConstraint {
        /// <summary>创建整数比较 / Create an integer comparison.</summary>
        /// <param name="expression">左侧表达式 / Left-hand expression.</param>
        /// <param name="comparison">比较关系 / Comparison relation.</param>
        /// <param name="rhs">右侧整数 / Right-hand integer.</param>
        public IntegerComparison(
            ConstraintProgrammingExpression expression,
            ConstraintProgrammingComparison comparison,
            Int64 rhs) {
            ArgumentNullException.ThrowIfNull(expression);
            Expression = expression;
            Comparison = comparison;
            Rhs = rhs;
        }

        /// <summary>左侧表达式 / Left-hand expression.</summary>
        public ConstraintProgrammingExpression Expression { get; }

        /// <summary>比较关系 / Comparison relation.</summary>
        public ConstraintProgrammingComparison Comparison { get; }

        /// <summary>右侧整数 / Right-hand integer.</summary>
        public Int64 Rhs { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables => Expression.Variables;

        /// <inheritdoc/>
        public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values) =>
            Expression.Evaluate(values).Map(lhs => Comparison switch {
                ConstraintProgrammingComparison.Equal => lhs == Rhs,
                ConstraintProgrammingComparison.LessOrEqual => lhs <= Rhs,
                ConstraintProgrammingComparison.GreaterOrEqual => lhs >= Rhs,
                _ => false,
            });
    }

    /// <summary>布尔合取 / Boolean conjunction.</summary>
    public sealed record BoolAnd : ConstraintProgrammingConstraint {
        /// <summary>创建合取约束 / Create a conjunction constraint.</summary>
        /// <param name="literals">参与合取的文字 / Literals in the conjunction.</param>
        public BoolAnd(IEnumerable<BooleanLiteral> literals) {
            Literals = Freeze(literals);
        }

        /// <summary>参与合取的文字 / Literals in the conjunction.</summary>
        public IReadOnlyList<BooleanLiteral> Literals { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables => LiteralVariables(Literals);

        /// <inheritdoc/>
        public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values) {
            foreach (BooleanLiteral literal in Literals) {
                Result<bool, ErrorCode, Error<ErrorCode>> result = literal.EvaluateInteger(values);
                if (result.IsFailed) {
                    return result;
                }
                if (!result.Value) {
                    return Results.Ok(false);
                }
            }
            return Results.Ok(true);
        }
    }

    /// <summary>布尔析取 / Boolean disjunction.</summary>
    public sealed record BoolOr : ConstraintProgrammingConstraint {
        /// <summary>创建析取约束 / Create a disjunction constraint.</summary>
        /// <param name="literals">参与析取的文字 / Literals in the disjunction.</param>
        public BoolOr(IEnumerable<BooleanLiteral> literals) {
            Literals = Freeze(literals);
        }

        /// <summary>参与析取的文字 / Literals in the disjunction.</summary>
        public IReadOnlyList<BooleanLiteral> Literals { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables => LiteralVariables(Literals);

        /// <inheritdoc/>
        public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values) {
            foreach (BooleanLiteral literal in Literals) {
                Result<bool, ErrorCode, Error<ErrorCode>> result = literal.EvaluateInteger(values);
                if (result.IsFailed) {
                    return result;
                }
                if (result.Value) {
                    return Results.Ok(true);
                }
            }
            return Results.Ok(false);
        }
    }

    /// <summary>布尔异或，要求恰好一个文字为真 / Boolean XOR requiring exactly one true literal.</summary>
    public sealed record BoolXor : ConstraintProgrammingConstraint {
        /// <summary>创建异或约束 / Create an XOR constraint.</summary>
        /// <param name="literals">参与异或的文字 / Literals in the XOR constraint.</param>
        public BoolXor(IEnumerable<BooleanLiteral> literals) {
            Literals = Freeze(literals);
        }

        /// <summary>参与异或的文字 / Literals in the XOR constraint.</summary>
        public IReadOnlyList<BooleanLiteral> Literals { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables => LiteralVariables(Literals);

        /// <inheritdoc/>
        public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values) {
            int trueAmount = 0;
            foreach (BooleanLiteral literal in Literals) {
                Result<bool, ErrorCode, Error<ErrorCode>> result = literal.EvaluateInteger(values);
                if (result.IsFailed) {
                    return result;
                }
                if (result.Value && ++trueAmount > 1) {
                    return Results.Ok(false);
                }
            }
            return Results.Ok(trueAmount == 1);
        }
    }

    /// <summary>要求单个布尔文字为真的约束 / Constraint requiring one Boolean literal to be true.</summary>
    public sealed record Literal : ConstraintProgrammingConstraint {
        /// <summary>创建文字约束 / Create a literal constraint.</summary>
        /// <param name="literal">要求为真的文字 / Literal required to be true.</param>
        public Literal(BooleanLiteral literal) {
            ArgumentNullException.ThrowIfNull(literal);
            LiteralValue = literal;
        }

        /// <summary>要求为真的文字 / Literal required to be true.</summary>
        public BooleanLiteral LiteralValue { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables => LiteralVariables(new[] { LiteralValue });

        /// <inheritdoc/>
        public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values) =>
            LiteralValue.EvaluateInteger(values);
    }

    /// <summary>蕴含约束 / Implication constraint.</summary>
    public sealed record Implication : ConstraintProgrammingConstraint {
        /// <summary>创建蕴含约束 / Create an implication constraint.</summary>
        /// <param name="enforcement">启用文字 / Enforcement literal.</param>
        /// <param name="constraint">被蕴含的约束 / Implied constraint.</param>
        public Implication(
            BooleanLiteral enforcement,
            ConstraintProgrammingConstraint constraint) {
            ArgumentNullException.ThrowIfNull(enforcement);
            ArgumentNullException.ThrowIfNull(constraint);
            Enforcement = enforcement;
            Constraint = constraint;
        }

        /// <summary>启用文字 / Enforcement literal.</summary>
        public BooleanLiteral Enforcement { get; }

        /// <summary>被蕴含的约束 / Implied constraint.</summary>
        public ConstraintProgrammingConstraint Constraint { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables =>
            UnionVariables(LiteralVariables(new[] { Enforcement }), Constraint.Variables);

        /// <inheritdoc/>
        public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values) =>
            Enforcement.EvaluateInteger(values)
                .Bind(enabled => enabled ? Constraint.IsSatisfied(values) : Results.Ok(true));
    }

    /// <summary>Reified 约束 / Reified constraint.</summary>
    public sealed record Reified : ConstraintProgrammingConstraint {
        /// <summary>创建 reified 约束 / Create a reified constraint.</summary>
        /// <param name="literal">关联文字 / Associated literal.</param>
        /// <param name="constraint">关联约束 / Associated constraint.</param>
        /// <param name="direction">Reification 方向 / Reification direction.</param>
        public Reified(
            BooleanLiteral literal,
            ConstraintProgrammingConstraint constraint,
            ReificationDirection direction = ReificationDirection.Equivalent) {
            ArgumentNullException.ThrowIfNull(literal);
            ArgumentNullException.ThrowIfNull(constraint);
            LiteralValue = literal;
            Constraint = constraint;
            Direction = direction;
        }

        /// <summary>关联文字 / Associated literal.</summary>
        public BooleanLiteral LiteralValue { get; }

        /// <summary>关联约束 / Associated constraint.</summary>
        public ConstraintProgrammingConstraint Constraint { get; }

        /// <summary>Reification 方向 / Reification direction.</summary>
        public ReificationDirection Direction { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables =>
            UnionVariables(LiteralVariables(new[] { LiteralValue }), Constraint.Variables);

        /// <inheritdoc/>
        public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values) {
            return Direction switch {
                ReificationDirection.Implies => LiteralValue.EvaluateInteger(values)
                    .Bind(literalValue => literalValue
                        ? Constraint.IsSatisfied(values)
                        : Results.Ok(true)),
                ReificationDirection.ImpliedBy => Constraint.IsSatisfied(values)
                    .Bind(constraintValue => constraintValue
                        ? LiteralValue.EvaluateInteger(values)
                        : Results.Ok(true)),
                ReificationDirection.Equivalent => LiteralValue.EvaluateInteger(values)
                    .Bind(literalValue => Constraint.IsSatisfied(values)
                        .Map(constraintValue => literalValue == constraintValue)),
                _ => Results.Ok(false),
            };
        }
    }

    /// <summary>全异约束 / All-different constraint.</summary>
    public sealed record AllDifferent : ConstraintProgrammingConstraint {
        /// <summary>创建全异约束 / Create an all-different constraint.</summary>
        /// <param name="expressions">待比较表达式 / Expressions to compare.</param>
        public AllDifferent(IEnumerable<ConstraintProgrammingExpression> expressions) {
            Expressions = Freeze(expressions);
        }

        /// <summary>待比较表达式 / Expressions to compare.</summary>
        public IReadOnlyList<ConstraintProgrammingExpression> Expressions { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables =>
            UnionVariables(Expressions.Select(expression => expression.Variables));

        /// <inheritdoc/>
        public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values) {
            var seen = new HashSet<Int64>();
            foreach (ConstraintProgrammingExpression expression in Expressions) {
                Result<Int64, ErrorCode, Error<ErrorCode>> result = expression.Evaluate(values);
                if (result.IsFailed) {
                    return result.Map(_ => false);
                }
                if (!seen.Add(result.Value)) {
                    return Results.Ok(false);
                }
            }
            return Results.Ok(true);
        }
    }

    /// <summary>Element 约束：目标等于候选数组在索引处的值 / Element constraint: target equals values[index].</summary>
    public sealed record Element : ConstraintProgrammingConstraint {
        /// <summary>创建 element 约束 / Create an element constraint.</summary>
        /// <param name="index">索引表达式 / Index expression.</param>
        /// <param name="values">候选表达式 / Candidate expressions.</param>
        /// <param name="target">目标表达式 / Target expression.</param>
        public Element(
            ConstraintProgrammingExpression index,
            IEnumerable<ConstraintProgrammingExpression> values,
            ConstraintProgrammingExpression target) {
            ArgumentNullException.ThrowIfNull(index);
            ArgumentNullException.ThrowIfNull(values);
            ArgumentNullException.ThrowIfNull(target);
            Index = index;
            Values = Freeze(values);
            Target = target;
        }

        /// <summary>索引表达式 / Index expression.</summary>
        public ConstraintProgrammingExpression Index { get; }

        /// <summary>候选表达式 / Candidate expressions.</summary>
        public IReadOnlyList<ConstraintProgrammingExpression> Values { get; }

        /// <summary>目标表达式 / Target expression.</summary>
        public ConstraintProgrammingExpression Target { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables =>
            UnionVariables(
                new[] { Index.Variables, Target.Variables }
                    .Concat(Values.Select(value => value.Variables)));

        /// <inheritdoc/>
        public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values) {
            Result<Int64, ErrorCode, Error<ErrorCode>> index = Index.Evaluate(values);
            if (index.IsFailed) {
                return index.Map(_ => false);
            }
            long position = index.Value.ToLong();
            if (position < 0 || position >= Values.Count) {
                return Results.Ok(false);
            }
            return Values[(int)position].Evaluate(values)
                .Bind(selected => Target.Evaluate(values)
                    .Map(target => target == selected));
        }
    }

    /// <summary>允许赋值表约束 / Allowed-assignment table constraint.</summary>
    public sealed record AllowedAssignments : ConstraintProgrammingConstraint {
        /// <summary>创建允许赋值表 / Create an allowed-assignment table.</summary>
        /// <param name="expressions">表达式列 / Expression columns.</param>
        /// <param name="tuples">允许元组 / Allowed tuples.</param>
        public AllowedAssignments(
            IEnumerable<ConstraintProgrammingExpression> expressions,
            IEnumerable<IEnumerable<Int64>> tuples) {
            Expressions = Freeze(expressions);
            Tuples = FreezeTuples(tuples);
        }

        /// <summary>表达式列 / Expression columns.</summary>
        public IReadOnlyList<ConstraintProgrammingExpression> Expressions { get; }

        /// <summary>允许元组 / Allowed tuples.</summary>
        public IReadOnlyList<IReadOnlyList<Int64>> Tuples { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables =>
            UnionVariables(Expressions.Select(expression => expression.Variables));

        /// <inheritdoc/>
        public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values) =>
            TableSatisfied(Expressions, Tuples, values, allowed: true);
    }

    /// <summary>禁止赋值表约束 / Forbidden-assignment table constraint.</summary>
    public sealed record ForbiddenAssignments : ConstraintProgrammingConstraint {
        /// <summary>创建禁止赋值表 / Create a forbidden-assignment table.</summary>
        /// <param name="expressions">表达式列 / Expression columns.</param>
        /// <param name="tuples">禁止元组 / Forbidden tuples.</param>
        public ForbiddenAssignments(
            IEnumerable<ConstraintProgrammingExpression> expressions,
            IEnumerable<IEnumerable<Int64>> tuples) {
            Expressions = Freeze(expressions);
            Tuples = FreezeTuples(tuples);
        }

        /// <summary>表达式列 / Expression columns.</summary>
        public IReadOnlyList<ConstraintProgrammingExpression> Expressions { get; }

        /// <summary>禁止元组 / Forbidden tuples.</summary>
        public IReadOnlyList<IReadOnlyList<Int64>> Tuples { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables =>
            UnionVariables(Expressions.Select(expression => expression.Variables));

        /// <inheritdoc/>
        public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values) =>
            TableSatisfied(Expressions, Tuples, values, allowed: false);
    }

    /// <summary>基于后继表达式的单回路约束 / Single-circuit constraint over successor expressions.</summary>
    public sealed record Circuit : ConstraintProgrammingConstraint {
        /// <summary>创建回路约束 / Create a circuit constraint.</summary>
        /// <param name="successors">每个节点的后继表达式 / Successor expression for each node.</param>
        public Circuit(IEnumerable<ConstraintProgrammingExpression> successors) {
            Successors = Freeze(successors);
        }

        /// <summary>每个节点的后继表达式 / Successor expression for each node.</summary>
        public IReadOnlyList<ConstraintProgrammingExpression> Successors { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables =>
            UnionVariables(Successors.Select(expression => expression.Variables));

        /// <inheritdoc/>
        public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values) {
            var next = new List<int>(Successors.Count);
            foreach (ConstraintProgrammingExpression successor in Successors) {
                Result<Int64, ErrorCode, Error<ErrorCode>> result = successor.Evaluate(values);
                if (result.IsFailed) {
                    return result.Map(_ => false);
                }
                long value = result.Value.ToLong();
                if (value < 0 || value >= Successors.Count) {
                    return Results.Ok(false);
                }
                next.Add((int)value);
            }
            if (next.Distinct().Count() != next.Count) {
                return Results.Ok(false);
            }

            int current = 0;
            var visited = new HashSet<int>();
            for (int index = 0; index < next.Count; index++) {
                if (!visited.Add(current)) {
                    return Results.Ok(false);
                }
                current = next[current];
            }
            return Results.Ok(current == 0 && visited.Count == next.Count);
        }
    }

    /// <summary>确定性自动机转移 / Deterministic automaton transition.</summary>
    public sealed record AutomatonTransition {
        /// <summary>创建确定性转移 / Create a deterministic transition.</summary>
        /// <param name="fromState">起始状态 / Source state.</param>
        /// <param name="value">匹配值 / Matched value.</param>
        /// <param name="toState">目标状态 / Target state.</param>
        public AutomatonTransition(
            int fromState,
            Int64 value,
            int toState) {
            FromState = fromState;
            Value = value;
            ToState = toState;
        }

        /// <summary>起始状态 / Source state.</summary>
        public int FromState { get; }

        /// <summary>匹配值 / Matched value.</summary>
        public Int64 Value { get; }

        /// <summary>目标状态 / Target state.</summary>
        public int ToState { get; }
    }

    /// <summary>整数序列自动机约束 / Integer-sequence automaton constraint.</summary>
    public sealed record Automaton : ConstraintProgrammingConstraint {
        /// <summary>创建自动机约束 / Create an automaton constraint.</summary>
        /// <param name="expressions">输入序列表达式 / Input-sequence expressions.</param>
        /// <param name="initialState">初始状态 / Initial state.</param>
        /// <param name="finalStates">可接受终态 / Accepting final states.</param>
        /// <param name="transitions">确定性转移 / Deterministic transitions.</param>
        public Automaton(
            IEnumerable<ConstraintProgrammingExpression> expressions,
            int initialState,
            IEnumerable<int> finalStates,
            IEnumerable<AutomatonTransition> transitions) {
            Expressions = Freeze(expressions);
            InitialState = initialState;
            FinalStates = finalStates.ToFrozenSet();
            Transitions = Freeze(transitions);
        }

        /// <summary>输入序列表达式 / Input-sequence expressions.</summary>
        public IReadOnlyList<ConstraintProgrammingExpression> Expressions { get; }

        /// <summary>初始状态 / Initial state.</summary>
        public int InitialState { get; }

        /// <summary>可接受终态 / Accepting final states.</summary>
        public IReadOnlySet<int> FinalStates { get; }

        /// <summary>确定性转移 / Deterministic transitions.</summary>
        public IReadOnlyList<AutomatonTransition> Transitions { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables =>
            UnionVariables(Expressions.Select(expression => expression.Variables));

        /// <inheritdoc/>
        public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values) {
            int state = InitialState;
            foreach (ConstraintProgrammingExpression expression in Expressions) {
                Result<Int64, ErrorCode, Error<ErrorCode>> result = expression.Evaluate(values);
                if (result.IsFailed) {
                    return result.Map(_ => false);
                }
                AutomatonTransition? transition = Transitions.FirstOrDefault(candidate =>
                    candidate.FromState == state && candidate.Value == result.Value);
                if (transition is null) {
                    return Results.Ok(false);
                }
                state = transition.ToState;
            }
            return Results.Ok(FinalStates.Contains(state));
        }
    }

    /// <summary>液位容量约束 / Reservoir-level constraint.</summary>
    public sealed record Reservoir : ConstraintProgrammingConstraint {
        /// <summary>整数时间点上的液位变化 / Level change at an integer time.</summary>
        /// <param name="Time">事件时间表达式 / Event-time expression.</param>
        /// <param name="LevelChange">液位变化表达式 / Level-change expression.</param>
        public sealed record Event(
            ConstraintProgrammingExpression Time,
            ConstraintProgrammingExpression LevelChange);

        /// <summary>创建液位约束 / Create a reservoir constraint.</summary>
        /// <param name="events">液位变化事件 / Level-change events.</param>
        /// <param name="initialLevel">初始液位 / Initial level.</param>
        /// <param name="minimumLevel">最低液位 / Minimum level.</param>
        /// <param name="maximumLevel">最高液位 / Maximum level.</param>
        public Reservoir(
            IEnumerable<Event> events,
            Int64 initialLevel,
            Int64 minimumLevel,
            Int64 maximumLevel) {
            Events = Freeze(events);
            InitialLevel = initialLevel;
            MinimumLevel = minimumLevel;
            MaximumLevel = maximumLevel;
        }

        /// <summary>液位变化事件 / Level-change events.</summary>
        public IReadOnlyList<Event> Events { get; }

        /// <summary>初始液位 / Initial level.</summary>
        public Int64 InitialLevel { get; }

        /// <summary>最低液位 / Minimum level.</summary>
        public Int64 MinimumLevel { get; }

        /// <summary>最高液位 / Maximum level.</summary>
        public Int64 MaximumLevel { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables =>
            UnionVariables(Events.SelectMany(evt => new[] { evt.Time.Variables, evt.LevelChange.Variables }));

        /// <inheritdoc/>
        public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(IReadOnlyDictionary<VariableId, Int64> values) {
            if (MinimumLevel > MaximumLevel ||
                InitialLevel < MinimumLevel ||
                InitialLevel > MaximumLevel) {
                return Results.Ok(false);
            }

            var evaluated = new List<(long Time, long Change)>(Events.Count);
            foreach (Event evt in Events) {
                Result<Int64, ErrorCode, Error<ErrorCode>> time = evt.Time.Evaluate(values);
                if (time.IsFailed) {
                    return time.Map(_ => false);
                }
                Result<Int64, ErrorCode, Error<ErrorCode>> change = evt.LevelChange.Evaluate(values);
                if (change.IsFailed) {
                    return change.Map(_ => false);
                }
                evaluated.Add((time.Value.ToLong(), change.Value.ToLong()));
            }
            evaluated.Sort((left, right) => left.Time.CompareTo(right.Time));

            BigInteger level = new(InitialLevel.ToLong());
            BigInteger minimum = new(MinimumLevel.ToLong());
            BigInteger maximum = new(MaximumLevel.ToLong());
            int index = 0;
            while (index < evaluated.Count) {
                long time = evaluated[index].Time;
                BigInteger totalChange = BigInteger.Zero;
                while (index < evaluated.Count && evaluated[index].Time == time) {
                    totalChange += new BigInteger(evaluated[index].Change);
                    ++index;
                }
                level += totalChange;
                if (level < minimum || level > maximum) {
                    return Results.Ok(false);
                }
            }
            return Results.Ok(true);
        }
    }

    /// <summary>创建整数等式 / Create an integer equality.</summary>
    /// <param name="expression">左侧表达式 / Left-hand expression.</param>
    /// <param name="rhs">右侧整数 / Right-hand integer.</param>
    /// <returns>整数等式 / Integer equality.</returns>
    public static Result<IntegerComparison, ErrorCode, Error<ErrorCode>> CreateEqual(
        ConstraintProgrammingExpression expression,
        Int64 rhs) =>
        Results.Ok(new IntegerComparison(
            expression: expression,
            comparison: ConstraintProgrammingComparison.Equal,
            rhs: rhs));

    /// <summary>创建整数小于等于约束 / Create an integer less-than-or-equal constraint.</summary>
    /// <param name="expression">左侧表达式 / Left-hand expression.</param>
    /// <param name="rhs">右侧整数 / Right-hand integer.</param>
    /// <returns>整数约束 / Integer constraint.</returns>
    public static Result<IntegerComparison, ErrorCode, Error<ErrorCode>> CreateLessOrEqual(
        ConstraintProgrammingExpression expression,
        Int64 rhs) =>
        Results.Ok(new IntegerComparison(
            expression: expression,
            comparison: ConstraintProgrammingComparison.LessOrEqual,
            rhs: rhs));

    /// <summary>创建整数大于等于约束 / Create an integer greater-than-or-equal constraint.</summary>
    /// <param name="expression">左侧表达式 / Left-hand expression.</param>
    /// <param name="rhs">右侧整数 / Right-hand integer.</param>
    /// <returns>整数约束 / Integer constraint.</returns>
    public static Result<IntegerComparison, ErrorCode, Error<ErrorCode>> CreateGreaterOrEqual(
        ConstraintProgrammingExpression expression,
        Int64 rhs) =>
        Results.Ok(new IntegerComparison(
            expression: expression,
            comparison: ConstraintProgrammingComparison.GreaterOrEqual,
            rhs: rhs));

    /// <summary>创建合取约束 / Create an AND constraint.</summary>
    /// <param name="literals">参与合取的文字 / Literals in the conjunction.</param>
    /// <returns>合取约束 / AND constraint.</returns>
    public static Result<BoolAnd, ErrorCode, Error<ErrorCode>> CreateBoolAnd(IEnumerable<BooleanLiteral> literals) =>
        Results.Ok(new BoolAnd(literals));

    /// <summary>创建析取约束 / Create an OR constraint.</summary>
    /// <param name="literals">参与析取的文字 / Literals in the disjunction.</param>
    /// <returns>析取约束 / OR constraint.</returns>
    public static Result<BoolOr, ErrorCode, Error<ErrorCode>> CreateBoolOr(IEnumerable<BooleanLiteral> literals) =>
        Results.Ok(new BoolOr(literals));

    /// <summary>创建异或约束 / Create an XOR constraint.</summary>
    /// <param name="literals">参与异或的文字 / Literals in the XOR constraint.</param>
    /// <returns>异或约束 / XOR constraint.</returns>
    public static Result<BoolXor, ErrorCode, Error<ErrorCode>> CreateBoolXor(IEnumerable<BooleanLiteral> literals) =>
        Results.Ok(new BoolXor(literals));

    /// <summary>创建文字约束 / Create a literal constraint.</summary>
    /// <param name="literal">要求为真的文字 / Literal required to be true.</param>
    /// <returns>文字约束 / Literal constraint.</returns>
    public static Result<Literal, ErrorCode, Error<ErrorCode>> CreateLiteral(BooleanLiteral literal) =>
        Results.Ok(new Literal(literal));

    /// <summary>创建蕴含约束 / Create an implication constraint.</summary>
    /// <param name="enforcement">启用文字 / Enforcement literal.</param>
    /// <param name="constraint">被蕴含的约束 / Implied constraint.</param>
    /// <returns>蕴含约束 / Implication constraint.</returns>
    public static Result<Implication, ErrorCode, Error<ErrorCode>> CreateImplication(
        BooleanLiteral enforcement,
        ConstraintProgrammingConstraint constraint) =>
        Results.Ok(new Implication(enforcement, constraint));

    /// <summary>创建文字到文字的蕴含 / Create a literal-to-literal implication.</summary>
    /// <param name="enforcement">启用文字 / Enforcement literal.</param>
    /// <param name="consequence">结果文字 / Consequence literal.</param>
    /// <returns>蕴含约束 / Implication constraint.</returns>
    public static Result<Implication, ErrorCode, Error<ErrorCode>> CreateImplication(
        BooleanLiteral enforcement,
        BooleanLiteral consequence) =>
        CreateImplication(enforcement, new Literal(consequence));

    /// <summary>创建 reified 约束 / Create a reified constraint.</summary>
    /// <param name="literal">关联文字 / Associated literal.</param>
    /// <param name="constraint">关联约束 / Associated constraint.</param>
    /// <param name="direction">Reification 方向 / Reification direction.</param>
    /// <returns>Reified 约束 / Reified constraint.</returns>
    public static Result<Reified, ErrorCode, Error<ErrorCode>> CreateReified(
        BooleanLiteral literal,
        ConstraintProgrammingConstraint constraint,
        ReificationDirection direction = ReificationDirection.Equivalent) =>
        Results.Ok(new Reified(
            literal: literal,
            constraint: constraint,
            direction: direction));

    /// <summary>创建非空全异约束 / Create a non-empty all-different constraint.</summary>
    /// <param name="expressions">待比较表达式 / Expressions to compare.</param>
    /// <returns>全异约束或结构化错误 / All-different constraint or a structured error.</returns>
    public static Result<AllDifferent, ErrorCode, Error<ErrorCode>> CreateAllDifferent(
        IEnumerable<ConstraintProgrammingExpression> expressions) {
        var list = expressions.ToList();
        return list.Count == 0
            ? Failure<AllDifferent>(
                ErrorCode.IllegalArgument,
                "AllDifferent 表达式不能为空 / AllDifferent expressions must not be empty")
            : Results.Ok(new AllDifferent(list));
    }

    /// <summary>创建 element 约束 / Create an element constraint.</summary>
    /// <param name="index">索引表达式 / Index expression.</param>
    /// <param name="values">候选表达式 / Candidate expressions.</param>
    /// <param name="target">目标表达式 / Target expression.</param>
    /// <returns>Element 约束或结构化错误 / Element constraint or a structured error.</returns>
    public static Result<Element, ErrorCode, Error<ErrorCode>> CreateElement(
        ConstraintProgrammingExpression index,
        IEnumerable<ConstraintProgrammingExpression> values,
        ConstraintProgrammingExpression target) {
        var list = values.ToList();
        return list.Count == 0
            ? Failure<Element>(
                ErrorCode.IllegalArgument,
                "Element 数组不能为空 / Element array must not be empty")
            : Results.Ok(new Element(
                index: index,
                values: list,
                target: target));
    }

    /// <summary>创建允许赋值表 / Create an allowed-assignment table.</summary>
    /// <param name="expressions">表达式列 / Expression columns.</param>
    /// <param name="tuples">允许元组 / Allowed tuples.</param>
    /// <returns>允许赋值表或结构化错误 / Allowed table or a structured error.</returns>
    public static Result<AllowedAssignments, ErrorCode, Error<ErrorCode>> CreateAllowedAssignments(
        IEnumerable<ConstraintProgrammingExpression> expressions,
        IEnumerable<IEnumerable<Int64>> tuples) {
        Result<(List<ConstraintProgrammingExpression> Expressions, List<List<Int64>> Tuples), ErrorCode, Error<ErrorCode>> validated =
            ValidateTable(expressions, tuples);
        return validated.IsOk
            ? Results.Ok(new AllowedAssignments(validated.Value.Expressions, validated.Value.Tuples))
            : Failure<AllowedAssignments>(
                ErrorCode.IllegalArgument,
                ErrorMessage<(
                    List<ConstraintProgrammingExpression> Expressions,
                    List<List<Int64>> Tuples)>(validated));
    }

    /// <summary>创建禁止赋值表 / Create a forbidden-assignment table.</summary>
    /// <param name="expressions">表达式列 / Expression columns.</param>
    /// <param name="tuples">禁止元组 / Forbidden tuples.</param>
    /// <returns>禁止赋值表或结构化错误 / Forbidden table or a structured error.</returns>
    public static Result<ForbiddenAssignments, ErrorCode, Error<ErrorCode>> CreateForbiddenAssignments(
        IEnumerable<ConstraintProgrammingExpression> expressions,
        IEnumerable<IEnumerable<Int64>> tuples) {
        Result<(List<ConstraintProgrammingExpression> Expressions, List<List<Int64>> Tuples), ErrorCode, Error<ErrorCode>> validated =
            ValidateTable(expressions, tuples);
        return validated.IsOk
            ? Results.Ok(new ForbiddenAssignments(validated.Value.Expressions, validated.Value.Tuples))
            : Failure<ForbiddenAssignments>(
                ErrorCode.IllegalArgument,
                ErrorMessage<(
                    List<ConstraintProgrammingExpression> Expressions,
                    List<List<Int64>> Tuples)>(validated));
    }

    /// <summary>创建非空回路约束 / Create a non-empty circuit constraint.</summary>
    /// <param name="successors">后继表达式 / Successor expressions.</param>
    /// <returns>回路约束或结构化错误 / Circuit constraint or a structured error.</returns>
    public static Result<Circuit, ErrorCode, Error<ErrorCode>> CreateCircuit(
        IEnumerable<ConstraintProgrammingExpression> successors) {
        var list = successors.ToList();
        return list.Count == 0
            ? Failure<Circuit>(
                ErrorCode.IllegalArgument,
                "Circuit 后继表达式不能为空 / Circuit successor expressions must not be empty")
            : Results.Ok(new Circuit(list));
    }

    /// <summary>创建自动机约束 / Create an automaton constraint.</summary>
    /// <param name="expressions">输入序列表达式 / Input-sequence expressions.</param>
    /// <param name="initialState">初始状态 / Initial state.</param>
    /// <param name="finalStates">可接受终态 / Accepting final states.</param>
    /// <param name="transitions">确定性转移 / Deterministic transitions.</param>
    /// <returns>自动机约束或结构化错误 / Automaton constraint or a structured error.</returns>
    public static Result<Automaton, ErrorCode, Error<ErrorCode>> CreateAutomaton(
        IEnumerable<ConstraintProgrammingExpression> expressions,
        int initialState,
        IEnumerable<int> finalStates,
        IEnumerable<AutomatonTransition> transitions) {
        var expressionList = expressions.ToList();
        var finalStateList = finalStates.ToList();
        var transitionList = transitions.ToList();
        if (expressionList.Count == 0 ||
            finalStateList.Count == 0 ||
            transitionList.Count == 0) {
            return Failure<Automaton>(
                ErrorCode.IllegalArgument,
                "Automaton 输入不能为空 / Automaton expressions, final states, and transitions must not be empty");
        }

        var keys = new HashSet<(int FromState, Int64 Value)>();
        foreach (AutomatonTransition transition in transitionList) {
            if (!keys.Add((transition.FromState, transition.Value))) {
                return Failure<Automaton>(
                    ErrorCode.IllegalArgument,
                    "Automaton 转移必须对 (fromState, value) 确定 / " +
                    "Automaton transitions must be deterministic for (fromState, value)");
            }
        }
        return Results.Ok(new Automaton(
            expressions: expressionList,
            initialState: initialState,
            finalStates: finalStateList,
            transitions: transitionList));
    }

    /// <summary>创建液位约束 / Create a reservoir constraint.</summary>
    /// <param name="events">液位变化事件 / Level-change events.</param>
    /// <param name="initialLevel">初始液位 / Initial level.</param>
    /// <param name="minimumLevel">最低液位 / Minimum level.</param>
    /// <param name="maximumLevel">最高液位 / Maximum level.</param>
    /// <returns>液位约束或结构化错误 / Reservoir constraint or a structured error.</returns>
    public static Result<Reservoir, ErrorCode, Error<ErrorCode>> CreateReservoir(
        IEnumerable<Reservoir.Event> events,
        Int64 initialLevel,
        Int64 minimumLevel,
        Int64 maximumLevel) {
        var eventList = events.ToList();
        if (eventList.Count == 0) {
            return Failure<Reservoir>(
                ErrorCode.IllegalArgument,
                "Reservoir 事件不能为空 / Reservoir events must not be empty");
        }
        if (minimumLevel > maximumLevel ||
            initialLevel < minimumLevel ||
            initialLevel > maximumLevel) {
            return Failure<Reservoir>(
                ErrorCode.IllegalArgument,
                "Reservoir 液位范围无效 / Reservoir level bounds are invalid");
        }
        return Results.Ok(new Reservoir(
            events: eventList,
            initialLevel: initialLevel,
            minimumLevel: minimumLevel,
            maximumLevel: maximumLevel));
    }

    private static Result<bool, ErrorCode, Error<ErrorCode>> TableSatisfied(
        IReadOnlyList<ConstraintProgrammingExpression> expressions,
        IReadOnlyList<IReadOnlyList<Int64>> tuples,
        IReadOnlyDictionary<VariableId, Int64> values,
        bool allowed) {
        var assignment = new List<Int64>(expressions.Count);
        foreach (ConstraintProgrammingExpression expression in expressions) {
            Result<Int64, ErrorCode, Error<ErrorCode>> result = expression.Evaluate(values);
            if (result.IsFailed) {
                return result.Map(_ => false);
            }
            assignment.Add(result.Value);
        }
        bool present = tuples.Any(tuple => tuple.SequenceEqual(assignment));
        return Results.Ok(allowed ? present : !present);
    }

    private static Result<(List<ConstraintProgrammingExpression> Expressions, List<List<Int64>> Tuples), ErrorCode, Error<ErrorCode>>
        ValidateTable(
            IEnumerable<ConstraintProgrammingExpression> expressions,
            IEnumerable<IEnumerable<Int64>> tuples) {
        var expressionList = expressions.ToList();
        if (expressionList.Count == 0) {
            return Failure<(List<ConstraintProgrammingExpression>, List<List<Int64>>)>(
                ErrorCode.IllegalArgument,
                "Table 表达式不能为空 / Table expressions must not be empty");
        }

        var tupleList = new List<List<Int64>>();
        foreach (IEnumerable<Int64> tuple in tuples) {
            var values = tuple.ToList();
            if (values.Count != expressionList.Count) {
                return Failure<(List<ConstraintProgrammingExpression>, List<List<Int64>>)>(
                    ErrorCode.IllegalArgument,
                    "Table 元组长度与表达式数量不一致 / Table tuple arity does not match expression count");
            }
            tupleList.Add(values);
        }
        return tupleList.Count == 0
            ? Failure<(List<ConstraintProgrammingExpression>, List<List<Int64>>)>(
                ErrorCode.IllegalArgument,
                "Table 元组不能为空 / Table tuples must not be empty")
            : Results.Ok((expressionList, tupleList));
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<T>(values.ToList());
    }

    private static IReadOnlyList<IReadOnlyList<Int64>> FreezeTuples(
        IEnumerable<IEnumerable<Int64>> tuples) {
        ArgumentNullException.ThrowIfNull(tuples);
        return new ReadOnlyCollection<IReadOnlyList<Int64>>(
            tuples.Select(tuple => (IReadOnlyList<Int64>)new ReadOnlyCollection<Int64>(tuple.ToList()))
                .ToList());
    }

    private static FrozenSet<VariableId> LiteralVariables(IEnumerable<BooleanLiteral> literals) =>
        literals
            .Select(literal => literal.VariableId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToFrozenSet();

    private static FrozenSet<VariableId> UnionVariables(params IEnumerable<VariableId>[] sources) {
        var variables = new HashSet<VariableId>();
        foreach (IEnumerable<VariableId> source in sources) {
            variables.UnionWith(source);
        }
        return variables.ToFrozenSet();
    }

    private static FrozenSet<VariableId> UnionVariables(IEnumerable<IReadOnlySet<VariableId>> sources) {
        var variables = new HashSet<VariableId>();
        foreach (IReadOnlySet<VariableId> source in sources) {
            variables.UnionWith(source);
        }
        return variables.ToFrozenSet();
    }

    private static string ErrorMessage<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result is Failed<T, ErrorCode, Error<ErrorCode>> failed
            ? failed.Error.Message
            : "CP 约束构造失败 / CP constraint construction failed";

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(ErrorCode code, string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));
}
