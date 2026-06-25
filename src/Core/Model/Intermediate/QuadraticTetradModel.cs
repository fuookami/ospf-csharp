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
    // ===== Quadratic constraint cell =====

    /// <summary>二次约束单元格 / Quadratic constraint cell</summary>
    /// <param name="RowIndex">行索引 / Row index</param>
    /// <param name="Col1">第一个列索引 / First column index</param>
    /// <param name="Col2">第二个列索引 / Second column index</param>
    /// <param name="Coefficient">系数 / Coefficient</param>
    public sealed record QuadraticConstraintCell(int RowIndex, int Col1, int Col2, Flt64 Coefficient)
        : IConstraintCell<QuadraticConstraintCell>, ICopyable<QuadraticConstraintCell>
    {
        /// <inheritdoc/>
        public QuadraticConstraintCell Negate() => this with { Coefficient = -Coefficient };

        /// <inheritdoc/>
        public QuadraticConstraintCell Copy() => new(RowIndex, Col1, Col2, Coefficient);

        /// <inheritdoc/>
        public override string ToString() => $"({RowIndex},{Col1},{Col2}): {Coefficient}";
    }

    // ===== Quadratic constraint batch =====

    /// <summary>二次约束批次（扩展 ModelConstraint，含稀疏二次矩阵 LHS）/ Quadratic constraint batch (extends ModelConstraint with sparse quadratic matrix LHS)</summary>
    public sealed class QuadraticConstraintBatch : ModelConstraint<QuadraticConstraintCell>
    {
        private readonly SparseQuadraticMatrix _lhs;
        private readonly List<IReadOnlyList<QuadraticConstraintCell>> _lhsView;

        /// <summary>稀疏二次矩阵左端 / Sparse quadratic matrix left-hand side</summary>
        public SparseQuadraticMatrix SparseLhs => _lhs;

        /// <inheritdoc/>
        public override IReadOnlyList<IReadOnlyList<QuadraticConstraintCell>> Lhs => _lhsView;

        public QuadraticConstraintBatch(
            SparseQuadraticMatrix lhs,
            IReadOnlyList<ConstraintRelation> signs,
            IReadOnlyList<Flt64> rhs,
            IReadOnlyList<string> names,
            IReadOnlyList<ConstraintSource> sources)
            : base(lhs.RowCount, signs, rhs, names, sources)
        {
            _lhs = lhs;
            _lhsView = new List<IReadOnlyList<QuadraticConstraintCell>>();
            for (int row = 0; row < lhs.RowCount; row++)
            {
                var cells = new List<QuadraticConstraintCell>();
                foreach (var entry in lhs.Rows[row].Entries)
                {
                    cells.Add(new QuadraticConstraintCell(row, entry.Col1, entry.Col2, entry.Value));
                }
                _lhsView.Add(cells);
            }
        }

        /// <inheritdoc/>
        public override ModelConstraint<QuadraticConstraintCell> Copy()
        {
            var newLhs = new SparseQuadraticMatrix();
            foreach (var row in _lhs.Rows)
            {
                newLhs.AddRow(new SparseQuadraticVector(new List<SparseQuadraticEntry>(row.Entries)));
            }
            return new QuadraticConstraintBatch(
                newLhs,
                new List<ConstraintRelation>(Signs),
                new List<Flt64>(Rhs),
                new List<string>(Names),
                new List<ConstraintSource>(Sources));
        }
    }

    // ===== Quadratic objective cell =====

    /// <summary>二次目标单元格 / Quadratic objective cell</summary>
    /// <param name="Col1">第一个列索引 / First column index</param>
    /// <param name="Col2">第二个列索引 / Second column index</param>
    /// <param name="Coefficient">系数 / Coefficient</param>
    public sealed record QuadraticObjectiveCell(int Col1, int Col2, Flt64 Coefficient)
        : IModelCell<QuadraticObjectiveCell>, ICopyable<QuadraticObjectiveCell>
    {
        /// <inheritdoc/>
        public QuadraticObjectiveCell Negate() => this with { Coefficient = -Coefficient };

        /// <inheritdoc/>
        public QuadraticObjectiveCell Copy() => new(Col1, Col2, Coefficient);

        /// <inheritdoc/>
        public override string ToString() => $"({Col1},{Col2}): {Coefficient}";
    }

    // ===== Type aliases =====

    /// <summary>二次目标函数类型别名 / Quadratic objective type alias</summary>
    public static class QuadraticObjective
    {
        /// <summary>创建二次目标函数 / Create quadratic objective</summary>
        public static Objective<QuadraticObjectiveCell> Create(ObjectCategory category, IReadOnlyList<QuadraticObjectiveCell> cells, Flt64? constant = null) =>
            new(category, cells, constant);
    }

    // ===== Basic model view =====

    /// <summary>基本二次四元模型视图类型别名 / Basic quadratic tetrad model view type alias</summary>
    public interface BasicQuadraticTetradModelView : IBasicModelView<QuadraticConstraintCell>
    {
    }

    /// <summary>基本二次四元模型 / Basic quadratic tetrad model</summary>
    public class BasicQuadraticTetradModel : BasicQuadraticTetradModelView
    {
        /// <inheritdoc/>
        public IReadOnlyList<ModelViewVariable> Variables { get; }
        /// <inheritdoc/>
        public ModelConstraint<QuadraticConstraintCell> Constraints { get; }
        /// <inheritdoc/>
        public string Name { get; }

        public BasicQuadraticTetradModel(
            IReadOnlyList<ModelViewVariable> variables,
            QuadraticConstraintBatch constraints,
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

    /// <summary>二次四元模型视图接口（含目标函数）/ Quadratic tetrad model view interface (with objective)</summary>
    public interface IQuadraticTetradModelView : IModelView<QuadraticConstraintCell, QuadraticObjectiveCell>
    {
    }

    /// <summary>二次四元模型 / Quadratic tetrad model</summary>
    public sealed class QuadraticTetradModel : BasicQuadraticTetradModel, IQuadraticTetradModelView
    {
        /// <inheritdoc/>
        public Objective<QuadraticObjectiveCell> Objective { get; }

        public QuadraticTetradModel(
            IReadOnlyList<ModelViewVariable> variables,
            QuadraticConstraintBatch constraints,
            Objective<QuadraticObjectiveCell> objective,
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
