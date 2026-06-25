#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Persistence.Expression
{
    /// <summary>
    /// 更新赋值集合 / Update assignment collection.
    /// </summary>
    public sealed record UpdateAssignments
    {
        private readonly List<UpdateAssignment> _assignments = new();

        /// <summary>赋值列表 / Assignment list.</summary>
        public IReadOnlyList<UpdateAssignment> Items => _assignments;

        /// <summary>设置值 / Set value.</summary>
        public UpdateAssignments Set(string field, object? value)
        {
            _assignments.Add(new UpdateAssignment.SetValue(field, value));
            return this;
        }

        /// <summary>设置为 null / Set to null.</summary>
        public UpdateAssignments SetNull(string field)
        {
            _assignments.Add(new UpdateAssignment.SetNull(field));
            return this;
        }

        /// <summary>设置为表达式 / Set from expression.</summary>
        public UpdateAssignments SetExpr(string field, string expression)
        {
            _assignments.Add(new UpdateAssignment.SetFromExpression(field, expression));
            return this;
        }

        /// <summary>追加赋值 / Append assignment.</summary>
        public UpdateAssignments ThenSet(string field, object? value)
        {
            _assignments.Add(new UpdateAssignment.SetValue(field, value));
            return this;
        }

        /// <summary>追加 null 赋值 / Append null assignment.</summary>
        public UpdateAssignments ThenSetNull(string field)
        {
            _assignments.Add(new UpdateAssignment.SetNull(field));
            return this;
        }

        /// <summary>追加表达式赋值 / Append expression assignment.</summary>
        public UpdateAssignments ThenSetExpr(string field, string expression)
        {
            _assignments.Add(new UpdateAssignment.SetFromExpression(field, expression));
            return this;
        }
    }

    /// <summary>
    /// 更新赋值（密封）/ Update assignment (sealed).
    /// </summary>
    public abstract record UpdateAssignment
    {
        /// <summary>字段名 / Field name</summary>
        public string Field { get; init; } = "";

        /// <summary>设置值赋值 / Set value assignment.</summary>
        public sealed record SetValue : UpdateAssignment
        {
            /// <summary>值 / Value</summary>
            public object? Value { get; }
            public SetValue(string field, object? value) { Field = field; Value = value; }
        }

        /// <summary>设置 null 赋值 / Set null assignment.</summary>
        public sealed record SetNull : UpdateAssignment
        {
            public SetNull(string field) { Field = field; }
        }

        /// <summary>设置表达式赋值 / Set from expression assignment.</summary>
        public sealed record SetFromExpression : UpdateAssignment
        {
            /// <summary>表达式 / Expression</summary>
            public string Expression { get; }
            public SetFromExpression(string field, string expression) { Field = field; Expression = expression; }
        }
    }
}
