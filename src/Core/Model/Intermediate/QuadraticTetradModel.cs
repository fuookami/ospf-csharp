#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Report;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Intermediate;
// ===== Quadratic constraint cell =====

/// <summary>二次约束单元格 / Quadratic constraint cell</summary>
/// <param name="RowIndex">行索引 / Row index</param>
/// <param name="Col1">第一个列索引 / First column index</param>
/// <param name="Col2">第二个列索引 / Second column index</param>
/// <param name="Coefficient">系数 / Coefficient</param>
public sealed record QuadraticConstraintCell(int RowIndex, int Col1, int Col2, Flt64 Coefficient)
    : IConstraintCell<QuadraticConstraintCell>, ICopyable<QuadraticConstraintCell> {
    /// <inheritdoc/>
    public QuadraticConstraintCell Negate() => this with { Coefficient = -Coefficient };

    /// <inheritdoc/>
    public QuadraticConstraintCell Copy() => new(RowIndex, Col1, Col2, Coefficient);

    /// <inheritdoc/>
    public override string ToString() => $"({RowIndex},{Col1},{Col2}): {Coefficient}";
}

// ===== Quadratic constraint batch =====

/// <summary>二次约束批次（扩展 ModelConstraint，含稀疏二次矩阵 LHS）/ Quadratic constraint batch (extends ModelConstraint with sparse quadratic matrix LHS)</summary>
public sealed class QuadraticConstraintBatch : ModelConstraint<QuadraticConstraintCell> {
    private readonly SparseQuadraticMatrix _lhs;
    private readonly IReadOnlyList<IReadOnlyList<QuadraticConstraintCell>> _lhsView;

    /// <summary>稀疏二次矩阵左端 / Sparse quadratic matrix left-hand side</summary>
    public SparseQuadraticMatrix SparseLhs => _lhs;

    /// <summary>动态行视图；每次索引返回只读行快照 / Live row view; each index returns a read-only row snapshot.</summary>
    public override IReadOnlyList<IReadOnlyList<QuadraticConstraintCell>> Lhs => _lhsView;

    public QuadraticConstraintBatch(
        SparseQuadraticMatrix lhs,
        IReadOnlyList<ConstraintRelation> signs,
        IReadOnlyList<Flt64> rhs,
        IReadOnlyList<string> names,
        IReadOnlyList<ConstraintSource> sources,
        IReadOnlyList<ModelElementIdentity>? identities = null,
        string? identityNamespace = null,
        string? identitySchemaVersion = null)
        : base(
            constraintCount: lhs.RowCount,
            signs: signs,
            rhs: rhs,
            names: names,
            sources: sources,
            identities: identities,
            identityNamespace: identityNamespace,
            identitySchemaVersion: identitySchemaVersion) {
        _lhs = lhs;
        _lhsView = new SparseConstraintRows<SparseQuadraticVector, QuadraticConstraintCell>(lhs.Rows, (sparseRow, row) => {
            var cells = new List<QuadraticConstraintCell>();
            foreach (SparseQuadraticEntry entry in sparseRow.Entries) {
                cells.Add(new QuadraticConstraintCell(row, entry.Col1, entry.Col2, entry.Value));
            }
            return cells.AsReadOnly();
        });
    }

    /// <inheritdoc/>
    public override ModelConstraint<QuadraticConstraintCell> Copy() {
        var newLhs = new SparseQuadraticMatrix();
        foreach (SparseQuadraticVector row in _lhs.Rows) {
            newLhs.AddRow(new SparseQuadraticVector(new List<SparseQuadraticEntry>(row.Entries)));
        }
        return new QuadraticConstraintBatch(
            lhs: newLhs,
            signs: Signs,
            rhs: Rhs,
            names: Names,
            sources: Sources,
            identities: Identities,
            identityNamespace: IdentityNamespace,
            identitySchemaVersion: IdentitySchemaVersion);
    }
}

// ===== Quadratic objective cell =====

/// <summary>二次目标单元格 / Quadratic objective cell</summary>
/// <param name="Col1">第一个列索引 / First column index</param>
/// <param name="Col2">第二个列索引 / Second column index</param>
/// <param name="Coefficient">系数 / Coefficient</param>
public sealed record QuadraticObjectiveCell(int Col1, int Col2, Flt64 Coefficient)
    : IModelCell<QuadraticObjectiveCell>, ICopyable<QuadraticObjectiveCell> {
    /// <inheritdoc/>
    public QuadraticObjectiveCell Negate() => this with { Coefficient = -Coefficient };

    /// <inheritdoc/>
    public QuadraticObjectiveCell Copy() => new(Col1, Col2, Coefficient);

    /// <inheritdoc/>
    public override string ToString() => $"({Col1},{Col2}): {Coefficient}";
}

// ===== Type aliases =====

/// <summary>二次目标函数类型别名 / Quadratic objective type alias</summary>
public static class QuadraticObjective {
    /// <summary>创建二次目标函数 / Create quadratic objective</summary>
    public static Objective<QuadraticObjectiveCell> Create(ObjectCategory category, IReadOnlyList<QuadraticObjectiveCell> cells, Flt64? constant = null) =>
        new(category, cells, constant);
}

// ===== Basic model view =====

/// <summary>基本二次四元模型视图类型别名 / Basic quadratic tetrad model view type alias</summary>
public interface BasicQuadraticTetradModelView : IBasicModelView<QuadraticConstraintCell> {
}

/// <summary>基本二次四元模型 / Basic quadratic tetrad model</summary>
public class BasicQuadraticTetradModel : BasicQuadraticTetradModelView {
    internal bool IsDisposed { get; private set; }
    /// <inheritdoc/>
    public IReadOnlyList<ModelViewVariable> Variables { get; }
    /// <inheritdoc/>
    public ModelConstraint<QuadraticConstraintCell> Constraints { get; }
    /// <inheritdoc/>
    public string Name { get; }
    /// <inheritdoc/>
    public bool ContainsContinuous => HasVariable(variable => variable.Type.IsContinuousType);
    /// <inheritdoc/>
    public bool ContainsBinary => HasVariable(variable => variable.Type.IsBinaryType);
    /// <inheritdoc/>
    public bool ContainsInteger => HasVariable(variable => variable.Type.IsIntegerType);
    /// <inheritdoc/>
    public bool ContainsNotBinaryInteger => HasVariable(variable => variable.Type.IsNotBinaryIntegerType);

    public BasicQuadraticTetradModel(
        IReadOnlyList<ModelViewVariable> variables,
        QuadraticConstraintBatch constraints,
        string name) {
        Variables = new List<ModelViewVariable>(variables).AsReadOnly();
        Constraints = constraints;
        Name = name;
    }

    /// <inheritdoc/>
    public virtual Try ExportLP(StreamWriter? writer) {
        Try validation = LpWriterSupport.Validate(
            writer: writer,
            disposed: IsDisposed,
            variables: Variables,
            constraints: Constraints
        );
        if (!validation.IsOk || writer is null) {
            return validation;
        }
        Try cells = ValidateConstraintCells();
        if (!cells.IsOk) {
            return cells;
        }
        return LpWriterSupport.Write(writer, builder => {
            builder.Append("Subject To\n");
            for (int row = 0; row < Constraints.Lhs.Count; row++) {
                builder.Append(' ').Append(Constraints.Names[row]).Append(": ");
                LpWriterSupport.AppendQuadraticExpression(
                    builder: builder,
                    variables: Variables,
                    cells: Constraints.Lhs[row],
                    firstColumn: cell => cell.Col1,
                    secondColumn: cell => cell.Col2,
                    coefficient: cell => cell.Coefficient
                );
                builder.Append(' ')
                    .Append(Constraints.Signs[row])
                    .Append(' ')
                    .Append(LpWriterSupport.Format(Constraints.Rhs[row]))
                    .Append('\n');
            }
            builder.Append('\n');
            LpWriterSupport.AppendBounds(
                builder: builder,
                variables: Variables,
                literalInfinity: true
            );
            LpWriterSupport.AppendIntegrality(builder, Variables);
            builder.Append("End\n");
        });
    }

    /// <summary>校验全部二次约束单元格 / Validate every quadratic constraint cell.</summary>
    /// <returns>成功或参数错误 / Success or an argument error.</returns>
    protected Try ValidateConstraintCells() {
        for (int row = 0; row < Constraints.Lhs.Count; row++) {
            foreach (QuadraticConstraintCell? cell in Constraints.Lhs[row]) {
                if (cell is null) {
                    return LpWriterSupport.ValidateCell(
                        coefficient: Flt64.Zero,
                        variableCount: Variables.Count,
                        columns: [-1]
                    );
                }
                int[] columns = cell.Col2 < 0
                    ? [cell.Col1]
                    : [cell.Col1, cell.Col2];
                Try validation = LpWriterSupport.ValidateCell(
                    coefficient: cell.Coefficient,
                    variableCount: Variables.Count,
                    columns: columns
                );
                if (!validation.IsOk) {
                    return validation;
                }
            }
        }
        return Results.OkInstance;
    }

    private bool HasVariable(Func<ModelViewVariable, bool> predicate) {
        foreach (ModelViewVariable variable in Variables) {
            if (predicate(variable)) {
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (!IsDisposed) {
            IsDisposed = true;
            Constraints.Dispose();
        }
    }
}

// ===== Full model view =====

/// <summary>二次四元模型视图接口（含目标函数）/ Quadratic tetrad model view interface (with objective)</summary>
public interface IQuadraticTetradModelView : IModelView<QuadraticConstraintCell, QuadraticObjectiveCell> {
}

/// <summary>二次四元模型 / Quadratic tetrad model</summary>
public sealed class QuadraticTetradModel : BasicQuadraticTetradModel, IQuadraticTetradModelView {
    internal MechanismModelSolutionMapping? SolutionMapping { get; init; }

    /// <inheritdoc/>
    public Objective<QuadraticObjectiveCell> Objective { get; }

    public QuadraticTetradModel(
        IReadOnlyList<ModelViewVariable> variables,
        QuadraticConstraintBatch constraints,
        Objective<QuadraticObjectiveCell> objective,
        string name)
        : base(variables, constraints, name) {
        Objective = objective;
    }

    /// <summary>
    /// 构造弹性模型 / Construct the elastic model.
    /// </summary>
    /// <returns>弹性二次四元模型 / The elastic quadratic tetrad model.</returns>
    /// <remarks>
    /// **`buildElasticModel` 是挂在模型上的扩展函数，由 `QuadraticTetradModel.elastic` 调用：**
    ///
    /// ```
    /// internal fun QuadraticTetradModel.buildElasticModel: QuadraticTetradModel
    /// override fun elastic: QuadraticTetradModel { return buildElasticModel }
    /// ```
    ///
    /// C# 侧原先只有静态的 `QuadraticTetradElasticBuilder.BuildElasticModel(model, penalty)`，
    /// **模型自己没有 `Elastic`**——于是个公开入口在 C# 里**根本不存在**，
    /// 外部只能直接调 `internal` 的静态构建器（而它连 `penalty` 都要自己传）。
    ///
    /// 这里把入口补上。**惩罚系数固定为 `1.0`**，因为基线 `elastic` 无参、`buildElasticModel` 也不收惩罚，
    /// 惩罚是构建器内部的常量；C# 静态方法收 `elasticPenalty` 是为了测试能验证惩罚确实进了目标，
    /// 而 `Elastic` 必须地**不暴露它**。
    ///
    /// **In the code `buildElasticModel` is an extension function on the model, called by `QuadraticTetradModel.elastic`** (snippet above).
    ///
    /// The C# side originally had only the static `QuadraticTetradElasticBuilder.BuildElasticModel(model, penalty)` and **the model had no `Elastic`** — so
    /// its public entry point **did not exist in C# at all**, and callers had to reach an `internal` static builder (which even made them supply
    /// `penalty`).
    ///
 /// adds the entry point. **The penalty is fixed at `1.0`** because its `elastic` takes no arguments and `buildElasticModel` takes
    /// no penalty — the penalty is a builder-internal constant. The C# static method accepts `elasticPenalty` so tests can verify the penalty really reaches the
    /// objective, while `Elastic` must **not expose it**, matching the code.
    /// </remarks>
    public QuadraticTetradModel Elastic() =>
        QuadraticTetradElasticBuilder.BuildElasticModel(this, Flt64.One);

    /// <inheritdoc/>
    public override Try ExportLP(StreamWriter? writer) {
        Try validation = LpWriterSupport.Validate(
            writer: writer,
            disposed: IsDisposed,
            variables: Variables,
            constraints: Constraints
        );
        if (!validation.IsOk || writer is null) {
            return validation;
        }
        Try cells = ValidateConstraintCells();
        if (!cells.IsOk) {
            return cells;
        }
        if (!Enum.IsDefined(Objective.Category)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "LP 目标方向无效 / LP objective category is invalid"));
        }
        foreach (QuadraticObjectiveCell? cell in Objective.Cells) {
            if (cell is null) {
                return LpWriterSupport.ValidateCell(
                    coefficient: Flt64.Zero,
                    variableCount: Variables.Count,
                    columns: [-1]
                );
            }
            int[] columns = cell.Col2 < 0
                ? [cell.Col1]
                : [cell.Col1, cell.Col2];
            Try cellValidation = LpWriterSupport.ValidateCell(
                coefficient: cell.Coefficient,
                variableCount: Variables.Count,
                columns: columns
            );
            if (!cellValidation.IsOk) {
                return cellValidation;
            }
        }
        return LpWriterSupport.Write(writer, builder => {
            builder.Append(Objective.Category.ToSymbolString()).Append('\n');
            LpWriterSupport.AppendQuadraticExpression(
                builder: builder,
                variables: Variables,
                cells: Objective.Cells,
                firstColumn: cell => cell.Col1,
                secondColumn: cell => cell.Col2,
                coefficient: cell => cell.Coefficient
            );
            builder.Append("\n\n");
            builder.Append("Subject To\n");
            for (int row = 0; row < Constraints.Lhs.Count; row++) {
                builder.Append(' ').Append(Constraints.Names[row]).Append(": ");
                LpWriterSupport.AppendQuadraticExpression(
                    builder: builder,
                    variables: Variables,
                    cells: Constraints.Lhs[row],
                    firstColumn: cell => cell.Col1,
                    secondColumn: cell => cell.Col2,
                    coefficient: cell => cell.Coefficient
                );
                builder.Append(' ')
                    .Append(Constraints.Signs[row])
                    .Append(' ')
                    .Append(LpWriterSupport.Format(Constraints.Rhs[row]))
                    .Append('\n');
            }
            builder.Append('\n');
            LpWriterSupport.AppendBounds(
                builder: builder,
                variables: Variables,
                literalInfinity: true
            );
            LpWriterSupport.AppendIntegrality(builder, Variables);
            builder.Append("End\n");
        });
    }
}
