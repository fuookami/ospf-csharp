#nullable enable

using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using System;
using System.Collections.Generic;
using System.IO;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Model.Intermediate
{
    // ===== Constraint cell =====

    /// <summary>线性约束单元格 / Linear constraint cell</summary>
    /// <param name="RowIndex">行索引 / Row index</param>
    /// <param name="ColIndex">列索引 / Column index</param>
    /// <param name="Coefficient">系数 / Coefficient</param>
    public sealed record LinearConstraintCell(int RowIndex, int ColIndex, Flt64 Coefficient)
        : IConstraintCell<LinearConstraintCell>, ICopyable<LinearConstraintCell>
    {
        /// <inheritdoc/>
        public LinearConstraintCell Negate() => this with { Coefficient = -Coefficient };

        /// <inheritdoc/>
        public LinearConstraintCell Copy() => new(RowIndex, ColIndex, Coefficient);

        /// <inheritdoc/>
        public override string ToString() => $"({RowIndex},{ColIndex}): {Coefficient}";
    }

    // ===== Constraint batch =====

    /// <summary>线性约束批次（扩展 ModelConstraint，含稀疏矩阵 LHS）/ Linear constraint batch (extends ModelConstraint with sparse matrix LHS)</summary>
    public sealed class LinearConstraintBatch : ModelConstraint<LinearConstraintCell>
    {
        private readonly SparseMatrix _lhs;
        private readonly List<IReadOnlyList<LinearConstraintCell>> _lhsView;

        /// <summary>稀疏矩阵左端 / Sparse matrix left-hand side</summary>
        public SparseMatrix SparseLhs => _lhs;

        /// <inheritdoc/>
        public override IReadOnlyList<IReadOnlyList<LinearConstraintCell>> Lhs => _lhsView;

        public LinearConstraintBatch(
            SparseMatrix lhs,
            IReadOnlyList<ConstraintRelation> signs,
            IReadOnlyList<Flt64> rhs,
            IReadOnlyList<string> names,
            IReadOnlyList<ConstraintSource> sources)
            : base(lhs.RowCount, signs, rhs, names, sources)
        {
            _lhs = lhs;
            _lhsView = new List<IReadOnlyList<LinearConstraintCell>>();
            for (int row = 0; row < lhs.RowCount; row++)
            {
                var cells = new List<LinearConstraintCell>();
                foreach (var entry in lhs.Rows[row].Entries)
                {
                    cells.Add(new LinearConstraintCell(row, entry.Index, entry.Value));
                }
                _lhsView.Add(cells);
            }
        }

        /// <inheritdoc/>
        public override ModelConstraint<LinearConstraintCell> Copy()
        {
            var newLhs = new SparseMatrix();
            foreach (var row in _lhs.Rows)
            {
                newLhs.AddRow(new SparseVector(new List<SparseVectorEntry>(row.Entries)));
            }
            return new LinearConstraintBatch(
                newLhs,
                new List<ConstraintRelation>(Signs),
                new List<Flt64>(Rhs),
                new List<string>(Names),
                new List<ConstraintSource>(Sources));
        }
    }

    // ===== Objective cell =====

    /// <summary>线性目标单元格 / Linear objective cell</summary>
    /// <param name="ColIndex">列索引 / Column index</param>
    /// <param name="Coefficient">系数 / Coefficient</param>
    public sealed record LinearObjectiveCell(int ColIndex, Flt64 Coefficient)
        : IModelCell<LinearObjectiveCell>, ICopyable<LinearObjectiveCell>
    {
        /// <inheritdoc/>
        public LinearObjectiveCell Negate() => this with { Coefficient = -Coefficient };

        /// <inheritdoc/>
        public LinearObjectiveCell Copy() => new(ColIndex, Coefficient);

        /// <inheritdoc/>
        public override string ToString() => $"({ColIndex}): {Coefficient}";
    }

    // ===== Type aliases =====

    /// <summary>线性目标函数类型别名 / Linear objective type alias</summary>
    public static class LinearObjective
    {
        /// <summary>创建线性目标函数 / Create linear objective</summary>
        public static Objective<LinearObjectiveCell> Create(ObjectCategory category, IReadOnlyList<LinearObjectiveCell> cells, Flt64? constant = null) =>
            new(category, cells, constant);
    }

    // ===== Basic model view =====

    /// <summary>基本线性三元模型视图类型别名 / Basic linear triad model view type alias</summary>
    public interface BasicLinearTriadModelView : IBasicModelView<LinearConstraintCell>
    {
    }

    /// <summary>基本线性三元模型 / Basic linear triad model</summary>
    public class BasicLinearTriadModel : BasicLinearTriadModelView
    {
        /// <inheritdoc/>
        public IReadOnlyList<ModelViewVariable> Variables { get; }
        /// <inheritdoc/>
        public ModelConstraint<LinearConstraintCell> Constraints { get; }
        /// <inheritdoc/>
        public string Name { get; }

        public BasicLinearTriadModel(
            IReadOnlyList<ModelViewVariable> variables,
            LinearConstraintBatch constraints,
            string name)
        {
            Variables = variables;
            Constraints = constraints;
            Name = name;
        }

        /// <inheritdoc/>
        public virtual Try ExportLP(StreamWriter writer)
        {
            // Stub: LP export to be implemented
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Constraints.Dispose();
        }
    }

    // ===== Full model view =====

    /// <summary>线性三元模型视图接口（含目标函数）/ Linear triad model view interface (with objective)</summary>
    public interface ILinearTriadModelView : IModelView<LinearConstraintCell, LinearObjectiveCell>
    {
    }

    /// <summary>线性三元模型 / Linear triad model</summary>
    public sealed class LinearTriadModel : BasicLinearTriadModel, ILinearTriadModelView
    {
        /// <inheritdoc/>
        public Objective<LinearObjectiveCell> Objective { get; }

        public LinearTriadModel(
            IReadOnlyList<ModelViewVariable> variables,
            LinearConstraintBatch constraints,
            Objective<LinearObjectiveCell> objective,
            string name)
            : base(variables, constraints, name)
        {
            Objective = objective;
        }

        /// <inheritdoc/>
        public override Try ExportLP(StreamWriter writer)
        {
            // Stub: LP export to be implemented
            return Results.Ok<Success>(Results.SuccessInstance);
        }
    }
}
