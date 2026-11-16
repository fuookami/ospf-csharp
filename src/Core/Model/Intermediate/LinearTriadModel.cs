#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Intermediate;
// ===== Constraint cell =====

/// <summary>线性约束单元格 / Linear constraint cell</summary>
/// <param name="RowIndex">行索引 / Row index</param>
/// <param name="ColIndex">列索引 / Column index</param>
/// <param name="Coefficient">系数 / Coefficient</param>
public sealed record LinearConstraintCell(int RowIndex, int ColIndex, Flt64 Coefficient)
    : IConstraintCell<LinearConstraintCell>, ICopyable<LinearConstraintCell> {
    /// <inheritdoc/>
    public LinearConstraintCell Negate() => this with { Coefficient = -Coefficient };

    /// <inheritdoc/>
    public LinearConstraintCell Copy() => new(RowIndex, ColIndex, Coefficient);

    /// <inheritdoc/>
    public override string ToString() => $"({RowIndex},{ColIndex}): {Coefficient}";
}

// ===== Constraint batch =====

/// <summary>线性约束批次（扩展 ModelConstraint，含稀疏矩阵 LHS）/ Linear constraint batch (extends ModelConstraint with sparse matrix LHS)</summary>
public sealed class LinearConstraintBatch : ModelConstraint<LinearConstraintCell> {
    private readonly SparseMatrix _lhs;
    private readonly IReadOnlyList<IReadOnlyList<LinearConstraintCell>> _lhsView;

    /// <summary>稀疏矩阵左端 / Sparse matrix left-hand side</summary>
    public SparseMatrix SparseLhs => _lhs;

    /// <summary>动态行视图；每次索引返回只读行快照 / Live row view; each index returns a read-only row snapshot.</summary>
    public override IReadOnlyList<IReadOnlyList<LinearConstraintCell>> Lhs => _lhsView;

    public LinearConstraintBatch(
        SparseMatrix lhs,
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
        _lhsView = new SparseConstraintRows<SparseVector, LinearConstraintCell>(lhs.Rows, (sparseRow, row) => {
            var cells = new List<LinearConstraintCell>();
            foreach (SparseVectorEntry entry in sparseRow.Entries) {
                cells.Add(new LinearConstraintCell(row, entry.Index, entry.Value));
            }
            return cells.AsReadOnly();
        });
    }

    /// <inheritdoc/>
    public override ModelConstraint<LinearConstraintCell> Copy() {
        var newLhs = new SparseMatrix();
        foreach (SparseVector row in _lhs.Rows) {
            newLhs.AddRow(new SparseVector(new List<SparseVectorEntry>(row.Entries)));
        }
        return new LinearConstraintBatch(
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

// ===== Objective cell =====

/// <summary>线性目标单元格 / Linear objective cell</summary>
/// <param name="ColIndex">列索引 / Column index</param>
/// <param name="Coefficient">系数 / Coefficient</param>
public sealed record LinearObjectiveCell(int ColIndex, Flt64 Coefficient)
    : IModelCell<LinearObjectiveCell>, ICopyable<LinearObjectiveCell> {
    /// <inheritdoc/>
    public LinearObjectiveCell Negate() => this with { Coefficient = -Coefficient };

    /// <inheritdoc/>
    public LinearObjectiveCell Copy() => new(ColIndex, Coefficient);

    /// <inheritdoc/>
    public override string ToString() => $"({ColIndex}): {Coefficient}";
}

// ===== Type aliases =====

/// <summary>线性目标函数类型别名 / Linear objective type alias</summary>
public static class LinearObjective {
    /// <summary>创建线性目标函数 / Create linear objective</summary>
    public static Objective<LinearObjectiveCell> Create(ObjectCategory category, IReadOnlyList<LinearObjectiveCell> cells, Flt64? constant = null) =>
        new(category, cells, constant);
}

// ===== Basic model view =====

/// <summary>基本线性三元模型视图类型别名 / Basic linear triad model view type alias</summary>
public interface BasicLinearTriadModelView : IBasicModelView<LinearConstraintCell> {
}

/// <summary>基本线性三元模型 / Basic linear triad model</summary>
public class BasicLinearTriadModel : BasicLinearTriadModelView {
    internal bool IsDisposed { get; private set; }
    /// <inheritdoc/>
    public IReadOnlyList<ModelViewVariable> Variables { get; }
    /// <inheritdoc/>
    public ModelConstraint<LinearConstraintCell> Constraints { get; }
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

    /// <summary>创建持有指定约束批次的模型 / Create a model that owns the supplied constraint batch.</summary>
    /// <param name="variables">模型列 / Model columns.</param>
    /// <param name="constraints">由模型释放的约束批次；不同模型不应共享该批次 / Constraint batch disposed by this model; do not share it between models.</param>
    /// <param name="name">模型名称 / Model name.</param>
    public BasicLinearTriadModel(
        IReadOnlyList<ModelViewVariable> variables,
        LinearConstraintBatch constraints,
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
                string name = string.IsNullOrEmpty(Constraints.Names[row])
                    ? $"cons{row}"
                    : Constraints.Names[row];
                builder.Append(' ').Append(name).Append(": ");
                bool hasTerm = false;
                foreach (LinearConstraintCell cell in Constraints.Lhs[row]) {
                    LpWriterSupport.AppendTerm(
                        builder: builder,
                        hasTerm: ref hasTerm,
                        coefficient: cell.Coefficient,
                        expression: Variables[cell.ColIndex].Name
                    );
                }
                if (!hasTerm) {
                    builder.Append('0');
                }
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
                literalInfinity: false
            );
            LpWriterSupport.AppendIntegrality(builder, Variables);
            builder.Append("End\n");
        });
    }

    /// <summary>校验全部约束单元格 / Validate every constraint cell.</summary>
    /// <returns>成功或参数错误 / Success or an argument error.</returns>
    protected Try ValidateConstraintCells() {
        for (int row = 0; row < Constraints.Lhs.Count; row++) {
            foreach (LinearConstraintCell? cell in Constraints.Lhs[row]) {
                if (cell is null) {
                    return LpWriterSupport.ValidateCell(
                        coefficient: Flt64.Zero,
                        variableCount: Variables.Count,
                        columns: [-1]
                    );
                }
                Try validation = LpWriterSupport.ValidateCell(
                    coefficient: cell.Coefficient,
                    variableCount: Variables.Count,
                    columns: [cell.ColIndex]
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

/// <summary>线性三元模型视图接口（含目标函数）/ Linear triad model view interface (with objective)</summary>
public interface ILinearTriadModelView : IModelView<LinearConstraintCell, LinearObjectiveCell> {
}

/// <summary>线性三元模型 / Linear triad model</summary>
public sealed class LinearTriadModel : BasicLinearTriadModel, ILinearTriadModelView {
    internal MechanismModelSolutionMapping? SolutionMapping { get; init; }
    private LinearTriadModel? _relaxationSource;
    private MechanismMatrixSnapshot? _relaxationSnapshot;
    // **对偶模型保存原模型的行身份快照。**
    //
    // **这一处是"把约束对象引用塞进 `dualOrigin`"，而对偶模型的行就是原模型的行，
    // 所以那把 key 天生就是原约束。C# 的对偶模型是<b>新建</b>的约束（构造时看不到原身份），
    // 于是必须把原身份<b>带过来</b>，否则 `DualOrigin.RowIndex` 在对偶模型里指向的是一个
    // 它自己都没有的身份集合。**
    //
    // **快照而不是引用：视图的全部意义是可复制的快照，而持有一个活的原模型会让对偶模型的生命周期
    // 绑在原模型上——原模型被 `Dispose` 之后，对偶模型的对偶整理就会失败。**
    //
    // **a dual model keeps a snapshot of the primal's row identities.** **The code puts a constraint object
    // reference into `dualOrigin` and the dual's rows <b>are</b> the primal's, so that key is naturally the primal
    // constraint. C#'s dual model builds <b>new</b> constraints (the originals are not visible at construction), so the
    // original identities must be <b>carried over</b> — otherwise `DualOrigin.RowIndex` points into an identity set the
    // dual model does not have.**
    //
    // **A snapshot rather than a reference: a view is a copyable snapshot by definition, and holding a live primal would
    // tie the dual's lifetime to it — disposing the primal would break the dual's tidy step.**
    internal IReadOnlyList<ConstraintId>? PrimalRowIdentities { get; init; }

    /// <inheritdoc/>
    public Objective<LinearObjectiveCell> Objective { get; }

    /// <summary>创建持有指定约束批次的线性模型 / Create a linear model that owns the supplied constraint batch.</summary>
    /// <param name="variables">模型列 / Model columns.</param>
    /// <param name="constraints">由模型释放的约束批次；不同模型不应共享该批次 / Constraint batch disposed by this model; do not share it between models.</param>
    /// <param name="objective">目标函数 / Objective function.</param>
    /// <param name="name">模型名称 / Model name.</param>
    public LinearTriadModel(
        IReadOnlyList<ModelViewVariable> variables,
        LinearConstraintBatch constraints,
        Objective<LinearObjectiveCell> objective,
        string name)
        : base(variables, constraints, name) {
        Objective = objective;
    }

    /// <summary>保留列边界并将变量类型松弛为连续类型，约束批次独立持有。 / Replace column types with continuous types while preserving bounds and owning an independent constraint batch.</summary>
    /// <remarks>仅转换变量类型，不证明其他变量类型的凸包；求解期间不得并发修改模型。 / Only column types are changed; this does not prove a convex hull for other variable types. Do not mutate the model concurrently with a solve.</remarks>
    /// <returns>松弛模型或形状、生命周期错误 / Relaxed model or a shape/lifecycle error.</returns>
    public Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> RelaxToContinuous() {
        if (IsDisposed) {
            return Results.Failed<LinearTriadModel>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "模型已关闭 / Model is closed"));
        }
        Try shape = this.ValidateIdentity();
        if (!shape.IsOk) {
            return shape.Bind<LinearTriadModel>(_ => RelaxationFailure("约束形状无效 / Invalid constraint shape"));
        }

        LinearTriadModel? relaxed = null;
        try {
            var constraints = (LinearConstraintBatch)Constraints;
            var sourceSnapshot = MechanismMatrixSnapshot.Capture(constraints, Objective);
            var variables = new List<ModelViewVariable>(Variables.Count);
            foreach (ModelViewVariable variable in Variables) {
                variables.Add(variable.WithDomain(
                    lowerBound: variable.LowerBound,
                    upperBound: variable.UpperBound,
                    type: Continuous.Instance));
            }
            relaxed = new LinearTriadModel(
                variables: variables,
                constraints: (LinearConstraintBatch)constraints.Copy(),
                objective: Objective,
                name: Name
            );
            relaxed._relaxationSource = this;
            relaxed._relaxationSnapshot = MechanismMatrixSnapshot.Capture((LinearConstraintBatch)relaxed.Constraints, relaxed.Objective);
            if (!sourceSnapshot.Matches(this) || !relaxed._relaxationSnapshot.Matches(relaxed)) {
                relaxed.Dispose();
                return RelaxationFailure("创建松弛模型时矩阵已变更 / Matrix changed while creating the relaxation");
            }
            return Results.Ok(relaxed);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            relaxed?.Dispose();
            return RelaxationFailure($"无法创建松弛模型：{error.Message} / Cannot create relaxation: {error.Message}");
        }
    }

    internal Try ValidateRelaxation(LinearTriadModel? source) {
        try {
            if (source is not null && ReferenceEquals(_relaxationSource, source)
                && _relaxationSnapshot is not null && _relaxationSnapshot.Matches(this)) {
                return Results.OkInstance;
            }
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "松弛矩阵无效或已变更，请重新求解 / Relaxed matrix is invalid or changed; solve again"));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"无法检查松弛矩阵：{error.Message} / Cannot inspect relaxed matrix: {error.Message}"));
        }
    }

    private static Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> RelaxationFailure(string message) =>
        Results.Failed<LinearTriadModel>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    /// <summary>构建线性规划对偶模型 / Build the linear-programming dual model.</summary>
    /// <remarks>
    /// 要求全部列为连续类型；整数列须先调用 <see cref="RelaxToContinuous"/>。对偶模型的前 m 个变量按原约束行顺序排列，
    /// 其后是每个原变量界对应的对偶列；对偶目标方向取反、不含原目标常数，也不含原变量的界（界以显式列进入对偶目标）。
    /// 对偶元素使用匿名 model-local 身份，未派生 Kotlin 式的稳定身份。
    /// All columns must be continuous; call <see cref="RelaxToContinuous"/> first for integer columns. The first m dual
    /// variables follow the primal row order, followed by dual columns for each primal variable bound. The dual objective
    /// reverses the category and drops the primal constant. Dual elements use anonymous model-local identities; the
    /// Kotlin-style derived stable identities are not generated.
    /// </remarks>
    /// <returns>对偶模型或形状、生命周期错误 / The dual model or a shape/lifecycle error.</returns>
    public Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> Dual() {
        if (IsDisposed) {
            return Results.Failed<LinearTriadModel>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "模型已关闭 / Model is closed"));
        }
        Try shape = this.ValidateIdentity();
        if (!shape.IsOk) {
            return shape.Bind<LinearTriadModel>(_ => DualFailure("约束形状无效 / Invalid constraint shape"));
        }
        if (Objective.Category is not ObjectCategory.Minimum and not ObjectCategory.Maximum) {
            return DualFailure("目标方向无效 / Invalid objective direction");
        }
        foreach (ModelViewVariable variable in Variables) {
            if (!variable.Type.IsContinuousType) {
                return DualFailure("对偶模型要求连续列，请先松弛 / The dual model requires continuous columns; relax the model first");
            }
        }
        try {
            return BuildDual();
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return DualFailure($"无法构建对偶模型：{error.Message} / Cannot build the dual model: {error.Message}");
        }
    }

    /// <summary>整理行对偶向量 / Tidy a row-dual vector.</summary>
    /// <remarks>
    /// 保留前 m 个与约束行同序的对偶值；行到机制约束的映射由调用方按行索引完成，因此原生逐行对偶与对偶模型解共用此入口。
    /// Keeps the first m values aligned with the constraint rows; mapping rows back to mechanism constraints is done by the
    /// caller through row indexes, so native per-row duals and dual-model solutions share this entry.
    /// </remarks>
    /// <param name="values">行对偶向量或对偶模型的解向量 / Row dual vector or the dual model's solution vector.</param>
    /// <returns>与约束行同序的对偶值或错误 / Row-aligned dual values or an error.</returns>
    // <summary>对偶解按约束身份整理成表 / Tidy the dual solution into a table keyed by constraint identity.</summary>
    // <remarks>
    // **基线 `tidyDualSolution` 返回 `Map&lt;Constraint, Flt64>`：对偶模型走 `dualOrigin`，普通模型走
    // `constraints.origins`。**
    //
 // **C# 的视图不持有约束对象引用，所以能给的 key 是
    // `ConstraintId`——而在<b>有</b> `ConstraintId` 的情况下，它就是把 key 的身份层内容：
    // 一个对象引用表达"哪一行"，C# 用一个身份字符串表达同一件事。**
    //
    // **行对偶（`Dual` 模型）用 `DualOrigin.RowIndex` 回查**，因为那个模型的行序是原模型的列序，
    // 而它的对偶变量的 `DualOrigin` 恰好记着"我来自哪一行"——**这正是该字段的用途第一次被真正用上。**
    //
    // **普通模型（行对齐向量）直接用行号取 `Ids[row]`。**
    //
    // **与 `TidyDualSolution` 的分工：** **那个返回按行对齐的 `IReadOnlyList&lt;Flt64>`（供按行序消费的调用方），
    // 这个返回带身份的字典（供按身份对账的调用方）。两者都保留，因为"数第几行"与"这是哪条约束"
    // 是两种不同的消费方式，而强行统一会让其中一方要么丢信息、要么多做一次映射。**
    //
    // **The code returns `Map&lt;Constraint, Flt64>`: the dual model goes through `dualOrigin`, an ordinary model
    // through `constraints.origins`.**
    //
    // **A C# view holds no constraint object references (a recorded representation difference), so the best key
    // available is `ConstraintId` — which, when present, is exactly the identity content of its key: the
    // baseline expresses "which row" with an object reference, C# expresses the same thing with an identity string.**
    //
    // **A dual model (`Dual`) resolves through `DualOrigin.RowIndex`, because its rows are the primal's columns and its
    // dual variables record "which row I came from" — the first real use of the field.**
    //
    // **An ordinary model is row-aligned, so `Ids[row]` applies directly.**
    //
    // **Division of labour with `TidyDualSolution`: that one returns a row-aligned `IReadOnlyList&lt;Flt64>` for callers
    // consuming by row order, this one returns an identity-keyed dictionary for callers reconciling by identity. Both
    // stay, because "the n-th row" and "which constraint this is" are different ways to consume, and unifying them would
    // force one side to lose information or add a mapping pass.**
    // </remarks>
    // <param name="values">对偶向量 / The dual vector.</param>
    // <returns>身份 → 对偶值 / Identity to dual value.</returns>
    public Result<IReadOnlyDictionary<ConstraintId, Flt64>, ErrorCode, Error<ErrorCode>> TidyDualSolutionByIdentity(
        IReadOnlyList<Flt64> values) {
        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> tidy = TidyDualSolution(values);
        if (tidy is Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<IReadOnlyDictionary<ConstraintId, Flt64>>(failed.Error);
        }
        IReadOnlyList<Flt64> aligned = ((Ok<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>)tidy).Value;
        var table = new Dictionary<ConstraintId, Flt64>(aligned.Count, ConstraintIdComparer.Instance);

        // **两种走法在这里合流成一个：**
        //
        // - **对偶模型**：`dualOrigin` 记着"这个变量是第几行的对偶"——行号直接取它。
        // - **普通（行对齐）模型**：第 `index` 个值就是第 `index` 行的对偶——行号就是 `index`。
        //
        // **合流的依据是：两者都在回答"这个值属于哪一行"，只是前者的行号存在变量上、后者存在位置上。**
        // **所以 `DualOrigin` 有空就用它，没有就退化为 `index`，不需要第三个分支。**
        //
        // **界对偶列与松弛列不是任何一行的对偶，它们本来就不该进这张表——
        // 而它们之所以不会进，是因为它们的对偶值不在前 `rows` 个位置上（`TidyDualSolution` 只截前 `rows` 个）。**
        //
        // **The code's two branches converge here into one:** **on a dual model `dualOrigin` records which row a
        // variable is the dual of, while on a row-aligned model position `index` *is* the row.** **They converge because
        // both answer "which row does this value belong to", differing only in where the row number is stored — on the
        // variable, or in the position.** **So use `DualOrigin` when present and fall back to `index`; no third
        // branch.**
        //
        // **Bound-dual and slack columns are not the dual of any row and must not enter this table — they do not, because
        // their values sit past the first `rows` positions and `TidyDualSolution` keeps only the first `rows`.**
        // **身份来源分两种：**
        //
        // - **对偶/Farkas 模型**：`PrimalRowIdentities` 是原模型的行身份快照，行号取 `DualOrigin.RowIndex`。
        // - **普通（行对齐）模型**：第 `index` 个值就是第 `index` 行的对偶，身份取本模型的 `Ids[index]`。
        //
        // **没有快照就说明这是普通模型——而不是"对偶模型但忘了带快照"，因为 `BuildDual` 一定会带。
        // 所以这个分支不是在猜，是在读一个由构造决定的属性。**
        //
        // **Two identity sources:**
        //
        // **On a dual/Farkas model `PrimalRowIdentities` holds the primal's row identities and the row comes from
        // `DualOrigin.RowIndex`; on an ordinary row-aligned model position `index` is the row and the identity comes
        // from this model's `Ids[index]`.**
        //
        // **No snapshot means an ordinary model — not "a dual model that forgot its snapshot", because `BuildDual`
        // always sets one.** **So the branch reads a construction-determined property rather than guessing.**
        IReadOnlyList<ConstraintId>? primalIdentities = PrimalRowIdentities;
        int rows = Constraints.Size;
        int identityCount = (primalIdentities ?? Constraints.Ids).Count;

        for (int index = 0; index < aligned.Count && index < rows; ++index) {
            int row = primalIdentities is not null && index < Variables.Count
                ? Variables[index].DualOrigin?.RowIndex ?? index
                : index;
            if (row < 0 || row >= identityCount) {
                continue;
            }
            ConstraintId key = primalIdentities is not null
                ? primalIdentities[row]
                : Constraints.Ids[row];
            table[key] = aligned[index];
        }

        return Results.Ok<IReadOnlyDictionary<ConstraintId, Flt64>>(table);
    }

    /// <summary>按值比较的 `ConstraintId` 比较器 / A value-comparing `ConstraintId` comparer.</summary>
    /// <remarks>
    /// **`ConstraintId` 是 `readonly record struct`，默认的 `EqualityComparer` 走生成的结构相等，已经可用；
    /// 这里只是把它固定成一个单例，避免每次构造字典都分配。**
    ///
    /// **`ConstraintId` is a `readonly record struct`; the default comparer already uses the generated structural
    /// equality. This only pins it to a singleton so building a dictionary does not allocate one.**
    /// </remarks>
    private sealed class ConstraintIdComparer : IEqualityComparer<ConstraintId> {
        public static readonly ConstraintIdComparer Instance = new();
        public bool Equals(ConstraintId x, ConstraintId y) =>
            string.Equals(x.Value, y.Value, StringComparison.Ordinal);
        public int GetHashCode(ConstraintId obj) =>
            obj.Value is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Value);
    }

    public Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> TidyDualSolution(IReadOnlyList<Flt64> values) {
        if (IsDisposed) {
            return Results.Failed<IReadOnlyList<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "模型已关闭 / Model is closed"));
        }
        if (values is null) {
            return Results.Failed<IReadOnlyList<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "对偶向量为空 / Dual vector is null"));
        }
        int rows = Constraints.Size;
        if (values.Count < rows) {
            return Results.Failed<IReadOnlyList<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"对偶向量长度 {values.Count} 小于约束行数 {rows} / Dual vector length {values.Count} is below the row count {rows}"));
        }
        var tidy = new Flt64[rows];
        for (int row = 0; row < rows; row++) {
            if (!double.IsFinite(values[row].ToDouble())) {
                return Results.Failed<IReadOnlyList<Flt64>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    $"第 {row} 行对偶值无效 / The dual value for row {row} is not finite"));
            }
            tidy[row] = values[row];
        }
        return Results.Ok<IReadOnlyList<Flt64>>(Array.AsReadOnly(tidy));
    }

    private Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> BuildDual() {
        IReadOnlyList<ConstraintId> primalRowIdentities = Constraints.Ids;
        INumericConstants<Flt64> constants = NumericConstantsRegistry.For<Flt64>();
        Flt64 negativeInfinity = constants.NegativeInfinity!.Value;
        Flt64 positiveInfinity = constants.PositiveInfinity!.Value;
        bool maximum = Objective.Category == ObjectCategory.Maximum;
        var constraints = (LinearConstraintBatch)Constraints;
        int rows = constraints.Size;
        // 下界对偶列在极大化时非正、极小化时非负；上界对偶列符号相反。
        // Lower-bound dual columns are non-positive for a maximum and non-negative for a minimum; upper-bound ones flip.
        Flt64 lowerDualUpper = maximum ? Flt64.Zero : positiveInfinity;
        Flt64 lowerDualLower = maximum ? negativeInfinity : Flt64.Zero;
        Flt64 upperDualLower = maximum ? Flt64.Zero : negativeInfinity;
        Flt64 upperDualUpper = maximum ? positiveInfinity : Flt64.Zero;
        var dualVariables = new List<ModelViewVariable>(rows + Variables.Count * 2);
        for (int row = 0; row < rows; row++) {
            Flt64 lower = negativeInfinity;
            Flt64 upper = positiveInfinity;
            if (constraints.Signs[row] == ConstraintRelation.LessEqual) {
                lower = maximum ? Flt64.Zero : negativeInfinity;
                upper = maximum ? positiveInfinity : Flt64.Zero;
            }
            else if (constraints.Signs[row] == ConstraintRelation.GreaterEqual) {
                lower = maximum ? negativeInfinity : Flt64.Zero;
                upper = maximum ? Flt64.Zero : positiveInfinity;
            }
            string rowName = constraints.Names[row];
            // **对偶变量记录它的来源行与那一行的关系。**
            // **这正是基线 `dualOrigin` 承载的信息，只是在扁平视图里用行号表达而不是对象引用。**
            // **a dual variable records its origin row and that row's relation — exactly what its
            // `dualOrigin` carries, expressed as a row index rather than an object reference in a flat view.**
            dualVariables.Add(new ModelViewVariable(
                index: row,
                lowerBound: lower,
                upperBound: upper,
                type: Continuous.Instance,
                name: $"{(string.IsNullOrEmpty(rowName) ? $"cons{row}" : rowName)}_dual",
                initialResult: Flt64.Zero,
                dualOrigin: new ConstraintSlackOrigin(row, constraints.Signs[row])));
        }
        var lowerDuals = new ModelViewVariable?[Variables.Count];
        var upperDuals = new ModelViewVariable?[Variables.Count];
        int colIndex = rows;
        // 界对偶列按原变量顺序追加，保证变量表索引与位置一致。
        // Bound dual columns are appended in primal-variable order so the variable list stays index-aligned.
        ModelViewVariable NewBoundDual(string name, Flt64 lower, Flt64 upper) {
            var dual = new ModelViewVariable(
                index: colIndex,
                lowerBound: lower,
                upperBound: upper,
                type: Continuous.Instance,
                name: name,
                initialResult: Flt64.Zero);
            colIndex++;
            dualVariables.Add(dual);
            return dual;
        }
        for (int col = 0; col < Variables.Count; col++) {
            ModelViewVariable variable = Variables[col];
            if (variable.NegativeNormalized || variable.PositiveNormalized || variable.Free) {
                continue;
            }
            if (variable.PositiveFree) {
                lowerDuals[col] = NewBoundDual($"{variable.Name}_lb_dual", lowerDualLower, lowerDualUpper);
            }
            else if (variable.NegativeFree) {
                upperDuals[col] = NewBoundDual($"{variable.Name}_ub_dual", upperDualLower, upperDualUpper);
            }
            else {
                lowerDuals[col] = NewBoundDual($"{variable.Name}_lb_dual", lowerDualLower, lowerDualUpper);
                upperDuals[col] = NewBoundDual($"{variable.Name}_ub_dual", upperDualLower, upperDualUpper);
            }
        }
        var transposed = new List<SparseVectorEntry>[Variables.Count];
        for (int col = 0; col < transposed.Length; col++) {
            transposed[col] = new List<SparseVectorEntry>();
        }
        for (int row = 0; row < rows; row++) {
            foreach (LinearConstraintCell cell in constraints.Lhs[row]) {
                if (cell.ColIndex < 0 || cell.ColIndex >= Variables.Count
                    || !double.IsFinite(cell.Coefficient.ToDouble())) {
                    return DualFailure("约束单元格无效 / Invalid constraint cell");
                }
                transposed[cell.ColIndex].Add(new SparseVectorEntry(row, cell.Coefficient));
            }
        }
        double[] objectiveCoefficients = new double[Variables.Count];
        foreach (LinearObjectiveCell cell in Objective.Cells) {
            if (cell.ColIndex < 0 || cell.ColIndex >= Variables.Count
                || !double.IsFinite(cell.Coefficient.ToDouble())) {
                return DualFailure("目标单元格无效 / Invalid objective cell");
            }
            objectiveCoefficients[cell.ColIndex] += cell.Coefficient.ToDouble();
            if (!double.IsFinite(objectiveCoefficients[cell.ColIndex])) {
                return DualFailure("目标系数溢出 / Objective coefficient overflow");
            }
        }
        var dualRows = new List<SparseVector>(Variables.Count);
        var signs = new List<ConstraintRelation>(Variables.Count);
        var rhs = new List<Flt64>(Variables.Count);
        var names = new List<string>(Variables.Count);
        var sources = new List<ConstraintSource>(Variables.Count);
        for (int col = 0; col < Variables.Count; col++) {
            ModelViewVariable variable = Variables[col];
            var entries = new List<SparseVectorEntry>(transposed[col].Count + 2);
            entries.AddRange(transposed[col]);
            if (lowerDuals[col] is { } lowerDual) {
                entries.Add(new SparseVectorEntry(lowerDual.Index, Flt64.One));
            }
            if (upperDuals[col] is { } upperDual) {
                entries.Add(new SparseVectorEntry(upperDual.Index, Flt64.One));
            }
            dualRows.Add(new SparseVector(entries));
            signs.Add(
                !variable.Normalized ? ConstraintRelation.Equal
                : variable.NegativeNormalized ? (maximum ? ConstraintRelation.LessEqual : ConstraintRelation.GreaterEqual)
                : maximum ? ConstraintRelation.GreaterEqual : ConstraintRelation.LessEqual);
            rhs.Add(new Flt64(objectiveCoefficients[col]));
            names.Add($"{variable.Name}_dual");
            sources.Add(ConstraintSource.Dual);
        }
        var objectiveCells = new List<LinearObjectiveCell>(colIndex);
        for (int row = 0; row < rows; row++) {
            if (!double.IsFinite(constraints.Rhs[row].ToDouble())) {
                return DualFailure("约束右端值无效 / Invalid constraint right-hand side");
            }
            objectiveCells.Add(new LinearObjectiveCell(row, constraints.Rhs[row]));
        }
        for (int col = 0; col < Variables.Count; col++) {
            if (lowerDuals[col] is { } lowerDual) {
                objectiveCells.Add(new LinearObjectiveCell(lowerDual.Index, Variables[col].LowerBound));
            }
            if (upperDuals[col] is { } upperDual) {
                objectiveCells.Add(new LinearObjectiveCell(upperDual.Index, Variables[col].UpperBound));
            }
        }
        return Results.Ok(new LinearTriadModel(
            variables: dualVariables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(dualRows),
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources),
            objective: new Objective<LinearObjectiveCell>(
                category: Objective.Category.Reverse(),
                cells: objectiveCells),
            name: $"{Name}-dual") {
            PrimalRowIdentities = primalRowIdentities
        });
    }

    private static Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> DualFailure(string message) =>
        Results.Failed<LinearTriadModel>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    /// <summary>构建 Farkas 对偶模型 / Build the Farkas dual model.</summary>
    /// <remarks>
    /// 与 Kotlin <c>farkasDual</c> 一致：每个原约束行生成一个带符号的 Farkas 列（<c>&lt;=</c> 非负、<c>&gt;=</c> 非正、
    /// <c>=</c> 自由），等式行再补正负松弛列把乘子绝对值化；每个有界原列生成上下界对偶列。归一化行强制
    /// Σ rhs·y + Σ lb·μ&lt;sub>lb&lt;/sub> + Σ ub·μ&lt;sub>ub&lt;/sub> = -1，因此可行基不存在时乘子必非零；目标极小化乘子幅度。
    /// 前 m 个列与原约束行同序，可交给 <see cref="TidyDualSolution"/> 取回逐行乘子；Farkas 模型不含原目标，
    /// 因此与 <see cref="Dual"/> 一样只描述不可行性证据。
    /// Mirrors Kotlin <c>farkasDual</c>: one signed Farkas column per primal row (<c>&lt;=</c> non-negative,
    /// <c>&gt;=</c> non-positive, <c>=</c> free), plus positive/negative slack columns that absolute the multipliers of
    /// equality rows, and bound dual columns for every bounded primal column. The normalization row forces
    /// Σ rhs·y + Σ lb·μ&lt;sub>lb&lt;/sub> + Σ ub·μ&lt;sub>ub&lt;/sub> = -1, so the multipliers cannot all vanish when no feasible
    /// point exists, while the minimum objective shrinks their magnitude. The first m columns stay row-aligned so
    /// <see cref="TidyDualSolution"/> recovers per-row multipliers. Like <see cref="Dual"/> the model ignores the primal
    /// objective and only describes infeasibility evidence.
    /// </remarks>
    /// <returns>Farkas 对偶模型或形状、生命周期错误 / The Farkas dual model or a shape/lifecycle error.</returns>
    public Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> FarkasDual() {
        if (IsDisposed) {
            return Results.Failed<LinearTriadModel>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "模型已关闭 / Model is closed"));
        }
        Try shape = this.ValidateIdentity();
        if (!shape.IsOk) {
            return DualFailure("约束形状无效 / Invalid constraint shape");
        }
        try {
            return BuildFarkasDual();
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return DualFailure($"无法构建 Farkas 对偶模型：{error.Message} / Cannot build the Farkas dual model: {error.Message}");
        }
    }

    private Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> BuildFarkasDual() {
        INumericConstants<Flt64> constants = NumericConstantsRegistry.For<Flt64>();
        Flt64 negativeInfinity = constants.NegativeInfinity!.Value;
        Flt64 positiveInfinity = constants.PositiveInfinity!.Value;
        var constraints = (LinearConstraintBatch)Constraints;
        int rows = constraints.Size;
        int columns = Variables.Count;
        var dualVariables = new List<ModelViewVariable>(rows * 3 + columns * 2);
        var farkas = new ModelViewVariable[rows];
        var positiveSlacks = new ModelViewVariable?[rows];
        var negativeSlacks = new ModelViewVariable?[rows];
        var objectiveCells = new List<LinearObjectiveCell>(rows * 3 + columns * 2);
        int colIndex = 0;
        // **Farkas 列带上派生身份。**
        //
        // **`role` 与 `discriminator` 来自的 `withDerivedIdentity` 调用点，
        // 其余六个参数（源 ID、作用域、主来源、完整来源、命名空间、schema）由 `FarkasSource(...)` 从被指向的
        // 那个源元素（约束行或原变量）取出。**
        //
        // **`FarkasSource` 是一个统一的取值函数而不是六个参数：** **因为"源是谁"取决于这一列指向哪一行/哪一列，
        // 而"源的哪几个身份字段"在两处完全相同——把后者提出去，前者的调用点就只剩一个索引。**
        //
        // **Farkas columns carry derived identities.** **`role` and `discriminator` come from its
        // `withDerivedIdentity` call sites; the other six inputs (source id, scope, origin, provenance, namespace,
        // schemaVersion) come from whichever element this column points at, through `FarkasSource(...)`.**
        //
        // **`FarkasSource` is one lookup rather than six parameters: which element is the source depends on the
        // row/column, while which identity fields to take from it is identical in both places — lifting the latter out
        // leaves a single index at each call site.**
        ModelViewVariable NewColumn(
            string name,
            Flt64 lower,
            Flt64 upper,
            string? role = null,
            Func<(ModelElementIdentity? Identity, string? Namespace, string? SchemaVersion)>? getSource = null,
            string discriminator = "0") {
            (ModelElementIdentity? sourceIdentity, string? sourceNamespace, string? sourceSchemaVersion) =
                getSource?.Invoke() ?? (null, null, null);
            var column = new ModelViewVariable(
                index: colIndex,
                lowerBound: lower,
                upperBound: upper,
                type: Continuous.Instance,
                name: name,
                initialResult: Flt64.Zero,
                identity: role is null
                    ? null
                    : DerivedModelElementIdentity.Create(
                        kind: ModelElementKind.Variable,
                        role: role,
                        sourceId: sourceIdentity?.Id.Value,
                        sourceScope: sourceIdentity?.Scope ?? ModelElementScope.ModelLocal,
                        sourceOrigin: sourceIdentity?.Origin,
                        sourceProvenance: sourceIdentity?.Provenance,
                        identityNamespace: sourceNamespace,
                        identitySchemaVersion: sourceSchemaVersion,
                        discriminator: discriminator),
                identityNamespace: role is null ? null : sourceNamespace,
                identitySchemaVersion: role is null ? null : sourceSchemaVersion);
            colIndex++;
            dualVariables.Add(column);
            return column;
        }
        // **取源元素身份。基线 `derivedDiscriminator` 的语义是
        // "源 ID 为空则用 model-local 判别串，否则用稳定判别串"，这里沿用。**
        //
        // **`RowSource` 取第 `row` 行约束的身份，`ColumnSource` 取第 `col` 个原变量的身份。**
        // **两者都返回 `(身份, 命名空间, schema)` 三元组，因为 C# 把后两个字段放在视图变量/约束上而不是身份上，
        // 与`identityNamespace`／`identitySchemaVersion` 放在 `Variable` 上是同一层次。**
        //
        // **`RowSource` 越界时退化为 `null` 而不是伪造：源行没有身份时不该造一个。**
        //
        // **read the source element's identity. The code's `derivedDiscriminator` means "model-local
        // discriminator when the source id is null, stable one otherwise", copied here.** **Both lookups return
        // `(identity, namespace, schema)`, because C# keeps the last two on the view variable/constraint rather than on
        // the identity, the same layer where the code keeps them on `Variable`.** **`RowSource` degrades to `null`
        // out of range rather than inventing an identity for a row that has none.**
        (ModelElementIdentity? Identity, string? Namespace, string? SchemaVersion) RowSource(int row) {
            if (row < 0 || row >= Constraints.Identities.Count) {
                return (null, Constraints.IdentityNamespace, Constraints.IdentitySchemaVersion);
            }
            return (Constraints.Identities[row], Constraints.IdentityNamespace, Constraints.IdentitySchemaVersion);
        }
        (ModelElementIdentity? Identity, string? Namespace, string? SchemaVersion) ColumnSource(int col) {
            ModelViewVariable primal = Variables[col];
            return (primal.Identity, primal.IdentityNamespace, primal.IdentitySchemaVersion);
        }
        // **基线 `derivedDiscriminator(sourceId, stable, modelLocal)`：源 ID 为空时用 model-local 那个。**
        // **The code's `derivedDiscriminator(sourceId, stable, modelLocal)`: model-local when the source id is null.**
        string Discriminator(string? sourceId, string stable, string modelLocal) =>
            sourceId is null ? modelLocal : stable;

        // 行 Farkas 列先占满前 m 个索引，与 Dual 一样让 TidyDualSolution 能按行序直接读取。
        // Row Farkas columns fill the first m indexes, like Dual, so TidyDualSolution reads them in row order.
        for (int row = 0; row < rows; row++) {
            ConstraintRelation sign = constraints.Signs[row];
            farkas[row] = NewColumn(
                name: $"{RowName(constraints.Names[row], row)}_farkas",
                lower: sign == ConstraintRelation.LessEqual ? Flt64.Zero : negativeInfinity,
                upper: sign == ConstraintRelation.GreaterEqual ? Flt64.Zero : positiveInfinity,
                role: "farkas-constraint",
                getSource: () => RowSource(row),
                discriminator: Discriminator(
                    RowSource(row).Identity?.Id.Value, "0", row.ToString(CultureInfo.InvariantCulture)));
            // 非正乘子取负系数，使目标始终是乘子幅度之和；等式行的幅度只由正负松弛列计量。
            // Non-positive multipliers flip sign so the objective sums magnitudes; equality rows are priced by their slacks only.
            if (sign == ConstraintRelation.LessEqual) {
                objectiveCells.Add(new LinearObjectiveCell(farkas[row].Index, Flt64.One));
            }
            else if (sign == ConstraintRelation.GreaterEqual) {
                objectiveCells.Add(new LinearObjectiveCell(farkas[row].Index, -Flt64.One));
            }
        }
        for (int row = 0; row < rows; row++) {
            if (constraints.Signs[row] != ConstraintRelation.Equal) {
                continue;
            }
            string name = RowName(constraints.Names[row], row);
            // **基线：等式行的正负松弛按 `index / 2` 找到它所属的那一行，判别串为 `row:positive`／`row:negative`。**
            // **Baseline: an equality row's positive/negative slack maps back through `index / 2` to its row, with
            // discriminator `row:positive` / `row:negative`.**
            string? rowSourceId = RowSource(row).Identity?.Id.Value;
            positiveSlacks[row] = NewColumn(
                $"{name}_pos_slack", Flt64.Zero, positiveInfinity,
                role: "farkas-slack",
                getSource: () => RowSource(row),
                discriminator: Discriminator(
                    rowSourceId, "positive",
                    $"{row.ToString(CultureInfo.InvariantCulture)}:positive"));
            negativeSlacks[row] = NewColumn(
                $"{name}_neg_slack", Flt64.Zero, positiveInfinity,
                role: "farkas-slack",
                getSource: () => RowSource(row),
                discriminator: Discriminator(
                    rowSourceId, "negative",
                    $"{row.ToString(CultureInfo.InvariantCulture)}:negative"));
            objectiveCells.Add(new LinearObjectiveCell(positiveSlacks[row]!.Index, Flt64.One));
            objectiveCells.Add(new LinearObjectiveCell(negativeSlacks[row]!.Index, Flt64.One));
        }
        var lowerDuals = new ModelViewVariable?[columns];
        var upperDuals = new ModelViewVariable?[columns];
        // 界对偶列按原列顺序追加，自由列不参与。 / Bound dual columns follow the primal column order; free columns are skipped.
        for (int col = 0; col < columns; col++) {
            ModelViewVariable variable = Variables[col];
            if (variable.Free) {
                continue;
            }
            // **基线：界 Farkas 列的源是<b>原变量</b>，判别串为 `col:lower`／`col:upper`。**
            // **Baseline: a bound Farkas column's source is the <b>primal variable</b>, with discriminator
            // `col:lower` / `col:upper`.**
            string? colSourceId = ColumnSource(col).Identity?.Id.Value;
            string colText = col.ToString(CultureInfo.InvariantCulture);
            if (variable.PositiveFree) {
                lowerDuals[col] = NewColumn(
                    $"{variable.Name}_lb_dual", negativeInfinity, Flt64.Zero,
                    role: "farkas-bound",
                    getSource: () => ColumnSource(col),
                    discriminator: Discriminator(colSourceId, "lower", $"{colText}:lower"));
            }
            else if (variable.NegativeFree) {
                upperDuals[col] = NewColumn(
                    $"{variable.Name}_ub_dual", Flt64.Zero, positiveInfinity,
                    role: "farkas-bound",
                    getSource: () => ColumnSource(col),
                    discriminator: Discriminator(colSourceId, "upper", $"{colText}:upper"));
            }
            else {
                lowerDuals[col] = NewColumn(
                    $"{variable.Name}_lb_dual", negativeInfinity, Flt64.Zero,
                    role: "farkas-bound",
                    getSource: () => ColumnSource(col),
                    discriminator: Discriminator(colSourceId, "lower", $"{colText}:lower"));
                upperDuals[col] = NewColumn(
                    $"{variable.Name}_ub_dual", Flt64.Zero, positiveInfinity,
                    role: "farkas-bound",
                    getSource: () => ColumnSource(col),
                    discriminator: Discriminator(colSourceId, "upper", $"{colText}:upper"));
            }
        }
        var transposed = new List<SparseVectorEntry>[columns];
        for (int col = 0; col < columns; col++) {
            transposed[col] = new List<SparseVectorEntry>();
        }
        for (int row = 0; row < rows; row++) {
            if (!double.IsFinite(constraints.Rhs[row].ToDouble())) {
                return DualFailure($"第 {row} 行右端值无效 / Invalid right-hand side for row {row}");
            }
            foreach (LinearConstraintCell cell in constraints.Lhs[row]) {
                if (cell.ColIndex < 0 || cell.ColIndex >= columns
                    || !double.IsFinite(cell.Coefficient.ToDouble())) {
                    return DualFailure("约束单元格无效 / Invalid constraint cell");
                }
                transposed[cell.ColIndex].Add(new SparseVectorEntry(row, cell.Coefficient));
            }
        }
        var farkasRows = new List<SparseVector>(columns + 1 + rows);
        var signs = new List<ConstraintRelation>(columns + 1 + rows);
        var rhs = new List<Flt64>(columns + 1 + rows);
        var names = new List<string>(columns + 1 + rows);
        var sources = new List<ConstraintSource>(columns + 1 + rows);
        for (int col = 0; col < columns; col++) {
            var entries = new List<SparseVectorEntry>(transposed[col].Count + 2);
            entries.AddRange(transposed[col]);
            if (lowerDuals[col] is { } lowerDual) {
                entries.Add(new SparseVectorEntry(lowerDual.Index, Flt64.One));
            }
            if (upperDuals[col] is { } upperDual) {
                entries.Add(new SparseVectorEntry(upperDual.Index, Flt64.One));
            }
            farkasRows.Add(new SparseVector(entries));
            signs.Add(ConstraintRelation.Equal);
            rhs.Add(Flt64.Zero);
            names.Add($"{Variables[col].Name}_farkas_dual");
            sources.Add(ConstraintSource.FarkasDual);
        }
        // 归一化行：Σ rhs·y + Σ lb·μ<sub>lb</sub> + Σ ub·μ<sub>ub</sub> = -1。 / Normalization row.
        var normalization = new List<SparseVectorEntry>(rows + columns * 2);
        for (int row = 0; row < rows; row++) {
            normalization.Add(new SparseVectorEntry(farkas[row].Index, constraints.Rhs[row]));
        }
        for (int col = 0; col < columns; col++) {
            if (lowerDuals[col] is { } lowerDual) {
                if (!double.IsFinite(Variables[col].LowerBound.ToDouble())) {
                    return DualFailure($"第 {col} 列下界无效 / Invalid lower bound for column {col}");
                }
                normalization.Add(new SparseVectorEntry(lowerDual.Index, Variables[col].LowerBound));
            }
            if (upperDuals[col] is { } upperDual) {
                if (!double.IsFinite(Variables[col].UpperBound.ToDouble())) {
                    return DualFailure($"第 {col} 列上界无效 / Invalid upper bound for column {col}");
                }
                normalization.Add(new SparseVectorEntry(upperDual.Index, Variables[col].UpperBound));
            }
        }
        farkasRows.Add(new SparseVector(normalization));
        signs.Add(ConstraintRelation.Equal);
        rhs.Add(-Flt64.One);
        names.Add("normalization");
        sources.Add(ConstraintSource.FarkasDual);
        for (int row = 0; row < rows; row++) {
            if (constraints.Signs[row] != ConstraintRelation.Equal) {
                continue;
            }
            farkasRows.Add(new SparseVector([
                new SparseVectorEntry(farkas[row].Index, Flt64.One),
                new SparseVectorEntry(positiveSlacks[row]!.Index, -Flt64.One),
                new SparseVectorEntry(negativeSlacks[row]!.Index, Flt64.One),
            ]));
            signs.Add(ConstraintRelation.Equal);
            rhs.Add(Flt64.Zero);
            names.Add($"{RowName(constraints.Names[row], row)}_abs");
            sources.Add(ConstraintSource.FarkasDual);
        }
        return Results.Ok(new LinearTriadModel(
            variables: dualVariables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(farkasRows),
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources),
            objective: new Objective<LinearObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: objectiveCells),
            name: $"{Name}-farkas-dual"));
    }

    private static string RowName(string name, int row) => string.IsNullOrEmpty(name) ? $"cons{row}" : name;

    // 行松弛只记录来源行的原始索引与原始关系；Kotlin 用 constraints.origins[row] 的 ConstraintInfo 表达同一件事。
    // A row slack records the original index and relation of its source row; Kotlin carries it as the ConstraintInfo
    // stored in constraints.origins[row].
    private static VariableSlack RowSlack(ModelConstraint<LinearConstraintCell> constraints, int row) =>
        new(Constraint: new ConstraintSlackOrigin(row, constraints.Signs[row]));

    /// <summary>构建可行性模型（最小化人工变量）/ Build the feasibility model (minimize artificial variables).</summary>
    /// <remarks>
    /// 与 Kotlin <c>feasibility</c> 一致：右端为负的整行取反并反转关系符号，每行改写为等式且右端取绝对值；
    /// 小于等于行新增系数 +1 的松弛列，大于等于行新增系数 -1 的松弛列与系数 +1 的人工列，等式行只新增系数 +1
    /// 的人工列；目标极小化人工列之和，因此最优值为 0 当且仅当原约束系统可行。松弛列与人工列都是连续
    /// [0, +inf)，原变量的边界与类型保持不变。派生行与派生列使用匿名 model-local 身份，且约束批次不承载
    /// origins、froms 与 priorities，因此这些元数据未复制。与 Kotlin 一致，松弛列会在
    /// <see cref="VariableSlack.Constraint"/> 中记录来源行的原始索引与原始关系符号，人工列不携带松弛元数据。
    /// Mirrors Kotlin <c>feasibility</c>: rows with a negative right-hand side are negated and their relation reversed,
    /// every row becomes an equality with an absolute right-hand side, a less-equal row gains a slack with coefficient
    /// +1, a greater-equal row gains a slack with -1 plus an artificial with +1, an equal row only gains an artificial
    /// with +1, and the objective minimizes the artificial sum, so an optimal value of 0 is exactly feasibility. Slacks
    /// and artificials are continuous [0, +inf) while the original bounds and types stay. Derived rows and columns use
    /// anonymous model-local identities, and the constraint batch carries no origins, froms or priorities, so that
    /// metadata is not copied. Like Kotlin, a slack column records its source row in
    /// <see cref="VariableSlack.Constraint"/> as the original index and relation, while an artificial column carries
    /// no slack metadata.
    /// </remarks>
    /// <returns>可行性模型或形状、生命周期错误 / The feasibility model or a shape/lifecycle error.</returns>
    public Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> Feasibility() {
        if (IsDisposed) {
            return Results.Failed<LinearTriadModel>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "模型已关闭 / Model is closed"));
        }
        Try shape = this.ValidateIdentity();
        if (!shape.IsOk) {
            return shape.Bind<LinearTriadModel>(_ => FeasibilityFailure("约束形状无效 / Invalid constraint shape"));
        }
        try {
            return BuildFeasibility();
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return FeasibilityFailure($"无法构建可行性模型：{error.Message} / Cannot build the feasibility model: {error.Message}");
        }
    }

    private Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> BuildFeasibility() {
        INumericConstants<Flt64> constants = NumericConstantsRegistry.For<Flt64>();
        Flt64 positiveInfinity = constants.PositiveInfinity!.Value;
        var constraints = (LinearConstraintBatch)Constraints;
        int rows = constraints.Size;
        int columns = Variables.Count;
        var variables = new List<ModelViewVariable>(columns + rows * 2);
        variables.AddRange(Variables);
        var newRows = new List<SparseVector>(rows);
        var signs = new List<ConstraintRelation>(rows);
        var rhs = new List<Flt64>(rows);
        var names = new List<string>(rows);
        var sources = new List<ConstraintSource>(rows);
        var objectiveCells = new List<LinearObjectiveCell>(rows);
        int colIndex = columns;
        // 松弛列与人工列都按索引递增追加，因此变量表的位置与索引保持一致。
        // Slacks and artificials are appended in ascending index order so the variable list stays index-aligned.
        ModelViewVariable NewColumn(string name, VariableSlack? slack = null) {
            var column = new ModelViewVariable(
                index: colIndex,
                lowerBound: Flt64.Zero,
                upperBound: positiveInfinity,
                type: Continuous.Instance,
                name: name,
                initialResult: Flt64.Zero,
                slack: slack);
            colIndex++;
            variables.Add(column);
            return column;
        }
        for (int row = 0; row < rows; row++) {
            if (!double.IsFinite(constraints.Rhs[row].ToDouble())) {
                return FeasibilityFailure($"第 {row} 行右端值无效 / Invalid right-hand side for row {row}");
            }
            bool negate = constraints.Rhs[row].Ls(Flt64.Zero);
            ConstraintRelation relation = negate ? constraints.Signs[row].Reverse : constraints.Signs[row];
            string rowName = RowName(constraints.Names[row], row);
            var entries = new List<SparseVectorEntry>();
            foreach (LinearConstraintCell cell in constraints.Lhs[row]) {
                if (cell.ColIndex < 0 || cell.ColIndex >= columns || !double.IsFinite(cell.Coefficient.ToDouble())) {
                    return FeasibilityFailure("约束单元格无效 / Invalid constraint cell");
                }
                entries.Add(new SparseVectorEntry(cell.ColIndex, negate ? -cell.Coefficient : cell.Coefficient));
            }
            if (relation == ConstraintRelation.LessEqual) {
                ModelViewVariable slack = NewColumn(
                    name: $"{rowName}_slack",
                    slack: RowSlack(constraints, row));
                entries.Add(new SparseVectorEntry(slack.Index, Flt64.One));
            }
            else {
                if (relation == ConstraintRelation.GreaterEqual) {
                    ModelViewVariable slack = NewColumn(
                        name: $"{rowName}_slack",
                        slack: RowSlack(constraints, row));
                    entries.Add(new SparseVectorEntry(slack.Index, -Flt64.One));
                }
                ModelViewVariable artifact = NewColumn(name: $"{rowName}_artifact");
                entries.Add(new SparseVectorEntry(artifact.Index, Flt64.One));
                objectiveCells.Add(new LinearObjectiveCell(artifact.Index, Flt64.One));
            }
            newRows.Add(new SparseVector(entries));
            signs.Add(ConstraintRelation.Equal);
            rhs.Add(constraints.Rhs[row].Abs());
            names.Add($"{rowName}_feasibility");
            sources.Add(ConstraintSource.Feasibility);
        }
        return Results.Ok(new LinearTriadModel(
            variables: variables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(newRows),
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources),
            objective: new Objective<LinearObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: objectiveCells),
            name: $"{Name}-feasibility"));
    }

    private static Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> FeasibilityFailure(string message) =>
        Results.Failed<LinearTriadModel>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    /// <summary>构建弹性模型（允许约束与边界被松弛）/ Build the elastic model (allow constraints and bounds to relax).</summary>
    /// <remarks>
    /// 与 Kotlin <c>elastic</c> 一致：每行按关系获得 <c>_lb_slack</c>、<c>_ub_slack</c> 或两者，关系符号与右端保持原值，
    /// 松弛列以 -1、+1 进入该行；每个非自由且未归一化的变量获得界松弛行 <c>x + s &gt;= lb</c> 与
    /// <c>x - s &lt;= ub</c>，其自身边界放开到无穷并改由这些行表达；目标改为极小化全部松弛之和（含可选 minmax 列），
    /// 原目标单元格全部丢弃。<paramref name="minSlackAmount"/> 为每个松弛建立二值指示列，并加入
    /// <c>s - (threshold + precision) * bin &gt;= 0</c>、<c>s - bin / precision &lt;= 0</c> 与
    /// <c>sum(bin) &gt;= count</c> 的总计行；<paramref name="minmaxSlack"/> 为每个松弛加入 <c>minmax - s &gt;= 0</c>。
    /// 派生行与派生列使用匿名 model-local 身份，且约束批次不承载 origins、froms 与 priorities，因此这些元数据未复制；
    /// 行松弛列改用 <see cref="VariableSlack.Constraint"/> 记录来源行的原始索引与原始关系，界松弛列记录被松弛的变量，
    /// IIS 过滤因此可以把松弛回溯到原行或原边界。
    /// Mirrors Kotlin <c>elastic</c>: every row gets a lower slack, an upper slack or both by relation while keeping its
    /// original sign and right-hand side, with the slacks entering at -1 and +1; every bounded non-normalized variable
    /// gains the bound rows <c>x + s &gt;= lb</c> and <c>x - s &lt;= ub</c> and has its own bounds opened to infinity and
    /// expressed by those rows instead; the objective becomes the minimum of all slacks (plus the optional minmax
    /// column) and drops every original objective cell. <paramref name="minSlackAmount"/> creates a binary indicator per
    /// slack and adds <c>s - (threshold + precision) * bin &gt;= 0</c>, <c>s - bin / precision &lt;= 0</c> and the
    /// <c>sum(bin) &gt;= count</c> total row; <paramref name="minmaxSlack"/> adds <c>minmax - s &gt;= 0</c> per slack.
    /// Derived rows and columns use anonymous model-local identities, and the constraint batch carries no origins,
    /// froms or priorities, so that metadata is not copied. A row slack records its source-row index and original
    /// relation in <see cref="VariableSlack.Constraint"/> and a bound slack records the relaxed variable, so IIS
    /// filtering can trace a slack back to the row or bound it came from.
    /// </remarks>
    /// <param name="minmaxSlack">是否加入最小-最大松弛列 / Whether to add the min-max slack column.</param>
    /// <param name="minSlackAmount">非零松弛数量下限与单个非零松弛的量级下限 / Minimum number of non-zero slacks and the minimum magnitude of each non-zero slack.</param>
    /// <returns>弹性模型或形状、生命周期错误 / The elastic model or a shape/lifecycle error.</returns>
    public Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> Elastic(
        bool minmaxSlack = false,
        (ulong Count, Flt64 Threshold)? minSlackAmount = null) {
        if (IsDisposed) {
            return Results.Failed<LinearTriadModel>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "模型已关闭 / Model is closed"));
        }
        Try shape = this.ValidateIdentity();
        if (!shape.IsOk) {
            return shape.Bind<LinearTriadModel>(_ => ElasticFailure("约束形状无效 / Invalid constraint shape"));
        }
        try {
            return BuildElastic(
                minmaxSlack: minmaxSlack,
                minSlackAmount: minSlackAmount);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return ElasticFailure($"无法构建弹性模型：{error.Message} / Cannot build the elastic model: {error.Message}");
        }
    }

    private Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> BuildElastic(
        bool minmaxSlack,
        (ulong Count, Flt64 Threshold)? minSlackAmount) {
        INumericConstants<Flt64> constants = NumericConstantsRegistry.For<Flt64>();
        Flt64 negativeInfinity = constants.NegativeInfinity!.Value;
        Flt64 positiveInfinity = constants.PositiveInfinity!.Value;
        Flt64 precision = constants.DecimalPrecision!.Value;
        var constraints = (LinearConstraintBatch)Constraints;
        int rows = constraints.Size;
        int columns = Variables.Count;
        var extraColumns = new List<ModelViewVariable>(rows * 2 + columns * 2 + 1);
        var slackColumns = new List<ModelViewVariable>(rows * 2 + columns * 2);
        var binaryColumns = new List<ModelViewVariable>();
        var slackPairs = new List<(ModelViewVariable? Lower, ModelViewVariable? Upper)>(rows + columns);
        int colIndex = columns;
        // 松弛、二值与 minmax 列都按索引递增追加，最终变量表因此保持位置与索引一致。
        // Slack, binary and minmax columns are appended in ascending index order so the final table stays index-aligned.
        ModelViewVariable NewColumn(
            string name,
            Flt64 lower,
            Flt64 upper,
            IVariableTypeKind type,
            VariableSlack? slack) {
            var column = new ModelViewVariable(
                index: colIndex,
                lowerBound: lower,
                upperBound: upper,
                type: type,
                name: name,
                initialResult: Flt64.Zero,
                slack: slack);
            colIndex++;
            extraColumns.Add(column);
            return column;
        }
        ModelViewVariable NewSlack(string name, VariableSlack? slack) {
            ModelViewVariable column = NewColumn(
                name: name,
                lower: Flt64.Zero,
                upper: positiveInfinity,
                type: Continuous.Instance,
                slack: slack);
            slackColumns.Add(column);
            return column;
        }
        for (int row = 0; row < rows; row++) {
            string rowName = RowName(constraints.Names[row], row);
            if (constraints.Signs[row] == ConstraintRelation.LessEqual) {
                ModelViewVariable lower = NewSlack($"{rowName}_lb_slack", RowSlack(constraints, row));
                slackPairs.Add((lower, null));
            }
            else if (constraints.Signs[row] == ConstraintRelation.GreaterEqual) {
                ModelViewVariable upper = NewSlack($"{rowName}_ub_slack", RowSlack(constraints, row));
                slackPairs.Add((null, upper));
            }
            else {
                ModelViewVariable lower = NewSlack($"{rowName}_lb_slack", RowSlack(constraints, row));
                ModelViewVariable upper = NewSlack($"{rowName}_ub_slack", RowSlack(constraints, row));
                slackPairs.Add((lower, upper));
            }
        }
        for (int col = 0; col < columns; col++) {
            ModelViewVariable variable = Variables[col];
            if (variable.Free || variable.PositiveNormalized || variable.NegativeNormalized) {
                slackPairs.Add((null, null));
            }
            else if (variable.PositiveFree) {
                ModelViewVariable lower = NewSlack($"{variable.Name}_lb_slack", new VariableSlack(LowerBound: variable));
                slackPairs.Add((lower, null));
            }
            else if (variable.NegativeFree) {
                ModelViewVariable upper = NewSlack($"{variable.Name}_ub_slack", new VariableSlack(UpperBound: variable));
                slackPairs.Add((null, upper));
            }
            else {
                ModelViewVariable lower = NewSlack($"{variable.Name}_lb_slack", new VariableSlack(LowerBound: variable));
                ModelViewVariable upper = NewSlack($"{variable.Name}_ub_slack", new VariableSlack(UpperBound: variable));
                slackPairs.Add((lower, upper));
            }
        }
        if (minSlackAmount is not null) {
            int slackCount = slackColumns.Count;
            for (int ordinal = 0; ordinal < slackCount; ordinal++) {
                ModelViewVariable slack = slackColumns[ordinal];
                ModelViewVariable binary = NewColumn(
                    name: $"{slack.Name}_bin",
                    lower: Flt64.Zero,
                    upper: Flt64.One,
                    type: Binary.Instance,
                    slack: null);
                binaryColumns.Add(binary);
            }
        }
        // minmax 列最后创建，并同时进入目标列集合，与 Kotlin 的目标顺序一致。
        // The minmax column is created last and joins the priced columns, matching Kotlin's objective order.
        ModelViewVariable? minmax = minmaxSlack
            ? NewSlack("minmax_slack", null)
            : null;
        var newRows = new List<SparseVector>(rows + columns * 2);
        var signs = new List<ConstraintRelation>(rows + columns * 2);
        var rhs = new List<Flt64>(rows + columns * 2);
        var names = new List<string>(rows + columns * 2);
        var sources = new List<ConstraintSource>(rows + columns * 2);
        for (int row = 0; row < rows; row++) {
            if (!double.IsFinite(constraints.Rhs[row].ToDouble())) {
                return ElasticFailure($"第 {row} 行右端值无效 / Invalid right-hand side for row {row}");
            }
            (ModelViewVariable? Lower, ModelViewVariable? Upper) pair = slackPairs[row];
            var entries = new List<SparseVectorEntry>();
            foreach (LinearConstraintCell cell in constraints.Lhs[row]) {
                if (cell.ColIndex < 0 || cell.ColIndex >= columns || !double.IsFinite(cell.Coefficient.ToDouble())) {
                    return ElasticFailure("约束单元格无效 / Invalid constraint cell");
                }
                entries.Add(new SparseVectorEntry(cell.ColIndex, cell.Coefficient));
            }
            if (pair.Lower is { } lower) {
                entries.Add(new SparseVectorEntry(lower.Index, -Flt64.One));
            }
            if (pair.Upper is { } upper) {
                entries.Add(new SparseVectorEntry(upper.Index, Flt64.One));
            }
            newRows.Add(new SparseVector(entries));
            signs.Add(constraints.Signs[row]);
            rhs.Add(constraints.Rhs[row]);
            names.Add($"{RowName(constraints.Names[row], row)}_elastic");
            sources.Add(ConstraintSource.Elastic);
        }
        for (int col = 0; col < columns; col++) {
            (ModelViewVariable? Lower, ModelViewVariable? Upper) pair = slackPairs[rows + col];
            ModelViewVariable variable = Variables[col];
            if (pair.Lower is { } lower) {
                if (!double.IsFinite(variable.LowerBound.ToDouble())) {
                    return ElasticFailure($"第 {col} 列下界无效 / Invalid lower bound for column {col}");
                }
                newRows.Add(new SparseVector([
                    new SparseVectorEntry(variable.Index, Flt64.One),
                    new SparseVectorEntry(lower.Index, Flt64.One),
                ]));
                signs.Add(ConstraintRelation.GreaterEqual);
                rhs.Add(variable.LowerBound);
                names.Add($"{variable.Name}_lb_slack");
                sources.Add(ConstraintSource.ElasticLowerBound);
            }
            if (pair.Upper is { } upper) {
                if (!double.IsFinite(variable.UpperBound.ToDouble())) {
                    return ElasticFailure($"第 {col} 列上界无效 / Invalid upper bound for column {col}");
                }
                newRows.Add(new SparseVector([
                    new SparseVectorEntry(variable.Index, Flt64.One),
                    new SparseVectorEntry(upper.Index, -Flt64.One),
                ]));
                signs.Add(ConstraintRelation.LessEqual);
                rhs.Add(variable.UpperBound);
                names.Add($"{variable.Name}_ub_slack");
                sources.Add(ConstraintSource.ElasticUpperBound);
            }
        }
        if (minSlackAmount is { } limit) {
            for (int ordinal = 0; ordinal < binaryColumns.Count; ordinal++) {
                ModelViewVariable slack = slackColumns[ordinal];
                ModelViewVariable binary = binaryColumns[ordinal];
                newRows.Add(new SparseVector([
                    new SparseVectorEntry(slack.Index, Flt64.One),
                    new SparseVectorEntry(binary.Index, -(limit.Threshold + precision)),
                ]));
                signs.Add(ConstraintRelation.GreaterEqual);
                rhs.Add(Flt64.Zero);
                names.Add($"{slack.Name}_bin_lb");
                sources.Add(ConstraintSource.ElasticSlackBinary);
                newRows.Add(new SparseVector([
                    new SparseVectorEntry(slack.Index, Flt64.One),
                    new SparseVectorEntry(binary.Index, -precision.Reciprocal()),
                ]));
                signs.Add(ConstraintRelation.LessEqual);
                rhs.Add(Flt64.Zero);
                names.Add($"{slack.Name}_bin_ub");
                sources.Add(ConstraintSource.ElasticSlackBinary);
            }
            var total = new List<SparseVectorEntry>(binaryColumns.Count);
            foreach (ModelViewVariable binary in binaryColumns) {
                total.Add(new SparseVectorEntry(binary.Index, Flt64.One));
            }
            newRows.Add(new SparseVector(total));
            signs.Add(ConstraintRelation.GreaterEqual);
            rhs.Add(new Flt64(limit.Count));
            names.Add("min_slack_amount");
            sources.Add(ConstraintSource.ElasticSlackBinary);
        }
        if (minmax is { } max) {
            foreach ((ModelViewVariable? Lower, ModelViewVariable? Upper) pair in slackPairs) {
                if (pair.Lower is { } lower) {
                    newRows.Add(new SparseVector([
                        new SparseVectorEntry(max.Index, Flt64.One),
                        new SparseVectorEntry(lower.Index, -Flt64.One),
                    ]));
                    signs.Add(ConstraintRelation.GreaterEqual);
                    rhs.Add(Flt64.Zero);
                    names.Add($"{lower.Name}_minmax");
                    sources.Add(ConstraintSource.ElasticSlackMinmax);
                }
                if (pair.Upper is { } upper) {
                    newRows.Add(new SparseVector([
                        new SparseVectorEntry(max.Index, Flt64.One),
                        new SparseVectorEntry(upper.Index, -Flt64.One),
                    ]));
                    signs.Add(ConstraintRelation.GreaterEqual);
                    rhs.Add(Flt64.Zero);
                    names.Add($"{upper.Name}_minmax");
                    sources.Add(ConstraintSource.ElasticSlackMinmax);
                }
            }
        }
        var variables = new List<ModelViewVariable>(columns + extraColumns.Count);
        foreach (ModelViewVariable variable in Variables) {
            // 自由与已归一化的列保留原边界，其余列的边界改由弹性界行表达。
            // Free and normalized columns keep their bounds; the others are expressed by the elastic bound rows instead.
            variables.Add(
                variable.Free || variable.PositiveNormalized || variable.NegativeNormalized
                    ? variable
                    : variable.WithDomain(
                        lowerBound: negativeInfinity,
                        upperBound: positiveInfinity,
                        type: variable.Type));
        }
        variables.AddRange(extraColumns);
        var objectiveCells = new List<LinearObjectiveCell>(slackColumns.Count);
        foreach (ModelViewVariable slack in slackColumns) {
            objectiveCells.Add(new LinearObjectiveCell(slack.Index, Flt64.One));
        }
        return Results.Ok(new LinearTriadModel(
            variables: variables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(newRows),
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources),
            objective: new Objective<LinearObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: objectiveCells),
            name: $"{Name}-elastic"));
    }

    private static Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> ElasticFailure(string message) =>
        Results.Failed<LinearTriadModel>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

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
        foreach (LinearObjectiveCell? cell in Objective.Cells) {
            if (cell is null) {
                return LpWriterSupport.ValidateCell(
                    coefficient: Flt64.Zero,
                    variableCount: Variables.Count,
                    columns: [-1]
                );
            }
            Try cellValidation = LpWriterSupport.ValidateCell(
                coefficient: cell.Coefficient,
                variableCount: Variables.Count,
                columns: [cell.ColIndex]
            );
            if (!cellValidation.IsOk) {
                return cellValidation;
            }
        }
        return LpWriterSupport.Write(writer, builder => {
            builder.Append(Objective.Category.ToSymbolString()).Append('\n');
            bool hasTerm = false;
            foreach (LinearObjectiveCell cell in Objective.Cells) {
                LpWriterSupport.AppendTerm(
                    builder: builder,
                    hasTerm: ref hasTerm,
                    coefficient: cell.Coefficient,
                    expression: Variables[cell.ColIndex].Name
                );
            }
            builder.Append("\n\n");
            builder.Append("Subject To\n");
            for (int row = 0; row < Constraints.Lhs.Count; row++) {
                string name = string.IsNullOrEmpty(Constraints.Names[row])
                    ? $"cons{row}"
                    : Constraints.Names[row];
                builder.Append(' ').Append(name).Append(": ");
                bool hasConstraintTerm = false;
                foreach (LinearConstraintCell cell in Constraints.Lhs[row]) {
                    LpWriterSupport.AppendTerm(
                        builder: builder,
                        hasTerm: ref hasConstraintTerm,
                        coefficient: cell.Coefficient,
                        expression: Variables[cell.ColIndex].Name
                    );
                }
                if (!hasConstraintTerm) {
                    builder.Append('0');
                }
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
                literalInfinity: false
            );
            LpWriterSupport.AppendIntegrality(builder, Variables);
            builder.Append("End\n");
        });
    }
}
