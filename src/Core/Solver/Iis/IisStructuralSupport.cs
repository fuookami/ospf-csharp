// IIS 的纯结构辅助：不依赖求解器 / Pure structural helpers for IIS: solver-free.

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Iis;

/// <summary>
/// **IIS 中"与过滤变量关联的变量"及其保留的边界** / A variable related to the filtered set, plus the bounds kept from it.
/// </summary>
/// <param name="Variable">关联变量 / The related variable.</param>
/// <param name="LowerBound">保留下界；`null` 表示下界不属于 IIS / Retained lower bound; `null` means it is not part of the IIS.</param>
/// <param name="UpperBound">保留上界；`null` 表示上界不属于 IIS / Retained upper bound; `null` means it is not part of the IIS.</param>
/// <remarks>
/// 对应的 `RelatedVariable`。**两个边界都可空是核心语义**：
/// IIS 只保留"导致不可行"的那些边界，其余边界要被丢弃。
///
/// Mirrors `RelatedVariable` in the implementation. **Both bounds being nullable is the core semantics**: an IIS keeps only the bounds that cause infeasibility and drops the
/// rest.
/// </remarks>
internal sealed record IisRelatedVariable(
    ModelViewVariable Variable,
    Flt64? LowerBound,
    Flt64? UpperBound);

/// <summary>
/// **IIS 的纯结构辅助** / Pure structural helpers for IIS.
/// </summary>
/// <remarks>
/// 这些函数**不调用求解器**，只做模型形状的读写，因此**可以在无 SDK 的环境里完整验证**。
/// 里它们是 `performElasticFiltering` / `performDeletionFiltering`
/// 的基础设施——解析出关联行和关联变量，再把子模型喂给求解器。
///
/// **`RelatedRows` 原本写在 `LinearLegacyIisCompute` 里，但它的签名和实现都与线性无关**
/// （只要变量列表与松弛行号），所以这里**提取到共享位置**，让二次侧直接复用，
/// 而不是复制一份。
///
/// These functions **never call a solver** — they only read and write model shape — so they **can be fully verified without an SDK**. In the implementation they are the
/// infrastructure the elastic and deletion filters build on: resolve related rows and variables, then hand the submodel to the solver. **`RelatedRows` used to live in
/// `LinearLegacyIisCompute`, but neither its signature nor its body is linear-specific** (it only needs a variable list and slack row indices), so
/// **extracts it to a shared location** for the quadratic side to reuse instead of copying it.
/// </remarks>
internal static class IisStructuralSupport {
    /// <summary>从松弛列回溯到原模型行号 / Trace slack columns back to original model rows.</summary>
    /// <param name="elasticVariables">弹性模型变量 / Elastic-model variables.</param>
    /// <param name="slackIndices">松弛列索引 / Slack column indices.</param>
    /// <returns>去重升序的行号 / Deduplicated ascending row indices.</returns>
    public static List<int> RelatedRows(
        IReadOnlyList<ModelViewVariable> elasticVariables,
        IEnumerable<int> slackIndices) {
        var rows = new SortedSet<int>();
        foreach (int index in slackIndices) {
            if (elasticVariables[index].Slack?.Constraint is { } constraint) {
                rows.Add(constraint.RowIndex);
            }
        }
        return [.. rows];
    }

    /// <summary>变量是否有松弛信息 / Whether a variable carries slack information.</summary>
    /// <param name="variable">变量 / Variable.</param>
    /// <returns>有松弛信息则为 `true` / `true` when slack information is present.</returns>
    public static bool HasSlack(ModelViewVariable variable) => variable.Slack is not null;

    /// <summary>
    /// **收集与过滤松弛变量及关联行有关的变量，并标记要保留哪一侧边界** / Collect variables related to the filtered slacks and rows, marking which bounds to keep.
    /// </summary>
    /// <param name="variables">模型变量 / Model variables.</param>
    /// <param name="constraints">二次约束批处理 / Quadratic constraint batch.</param>
    /// <param name="filterSlackIndices">被过滤的松弛列索引 / Filtered slack column indices.</param>
    /// <param name="relatedRows">关联约束行 / Related constraint rows.</param>
    /// <returns>按变量索引升序的关联变量 / Related variables in ascending index order.</returns>
    /// <remarks>
    /// **标记规则**：
    ///
    /// | 来源 | 下界标记 | 上界标记 |
    /// | --- | --- | --- |
    /// | 过滤变量的 `slack.LowerBound` | **true** | false |
    /// | 过滤变量的 `slack.UpperBound` | false | **true** |
    /// | 关联行的任一系数（含二次项两列） | false | false |
    ///
    /// 同一变量被多次标记时**取或**（`MarkBound` 的累积语义），所以"既是边界又是系数"时边界会保留。
    /// 最终 `LowerBound`/`UpperBound` 为 `null` 表示**该侧边界不属于 IIS**。
    ///
 /// **Marking rules (. Repeated marks on one variable **OR together** (the accumulation in `MarkBound`), so a
    /// variable that is both a bound and a coefficient keeps its bound. A `null` bound in the result means **that side is not part of the IIS**.
    /// </remarks>
    public static List<IisRelatedVariable> RelatedVariables(
        IReadOnlyList<ModelViewVariable> variables,
        ModelConstraint<QuadraticConstraintCell> constraints,
        IEnumerable<int> filterSlackIndices,
        IReadOnlyList<int> relatedRows) {
        var marks = new SortedDictionary<int, (bool KeepLower, bool KeepUpper)>();

        foreach (int slackIndex in filterSlackIndices) {
            ModelViewVariable? slack = variables[slackIndex];
            if (slack.Slack is { } info) {
                if (info.LowerBound is { } lower) {
                    MarkBound(marks, lower.Index, keepLower: true, keepUpper: false);
                }
                if (info.UpperBound is { } upper) {
                    MarkBound(marks, upper.Index, keepLower: false, keepUpper: true);
                }
            }
        }

        foreach (int row in relatedRows) {
            foreach (QuadraticConstraintCell cell in constraints.Lhs[row]) {
                MarkBound(marks, cell.Col1, keepLower: false, keepUpper: false);
                if (cell.Col2 >= 0) {
                    MarkBound(marks, cell.Col2, keepLower: false, keepUpper: false);
                }
            }
        }

        var result = new List<IisRelatedVariable>(marks.Count);
        foreach (KeyValuePair<int, (bool KeepLower, bool KeepUpper)> mark in marks) {
            ModelViewVariable variable = variables[mark.Key];
            result.Add(new IisRelatedVariable(
                Variable: variable,
                LowerBound: mark.Value.KeepLower ? variable.LowerBound : null,
                UpperBound: mark.Value.KeepUpper ? variable.UpperBound : null));
        }
        return result;
    }

    /// <summary>
    /// **按行索引裁剪约束，并按需重映射列索引** / Keep only the given rows and remap column indices.
    /// </summary>
    /// <param name="constraints">原约束批处理 / Source constraint batch.</param>
    /// <param name="rows">要保留的行索引（可重复、可乱序）/ Row indices to keep (may repeat or be unordered).</param>
    /// <param name="oldToNewVariableIndexMap">旧列索引到新列索引的映射 / Old-to-new column index map.</param>
    /// <returns>裁剪后的约束批处理 / The trimmed constraint batch.</returns>
    /// <remarks>
    /// 对应`filterConstraintByRowIndex`。**两条丢弃规则**：
    ///
    /// 1. 行去重后**升序**排列——`distinct.sorted`；
    /// 2. 单元格的 `Col1`（以及存在的 `Col2`）**必须在映射表里**，否则整格丢弃。
    ///
    /// 第 2 条是**成对丢弃**：二次项 `(i, j)` 只要有一端被裁掉，整个二次项都不能留——
    /// 留下半截会让重映射后的矩阵指向不存在的列。
    ///
    /// **有意差异**：不必拆成五条并行列表
    /// （`ids` / `identityScopes` / `identityOrigins` / `identityProvenance` 那样的五份数据），
    /// C# 的 `ModelElementIdentity` 把 Kind / Id / Scope / Origin / Provenance
    /// **打包在一个记录里**，所以这里只需**按行索引过滤一个列表**，
    /// 不需要逐条对齐五份数据。
    ///
    /// With **two discard rules** (above). Rule 2 is a **paired discard**: if either end of a quadratic term `(i, j)` is
    /// trimmed, the whole term must go — keeping half would leave the remapped matrix pointing at a non-existent column. **Deliberate difference**: identity need not be
    /// split into five parallel lists, because C#'s `ModelElementIdentity` **bundles** Kind / Id / Scope / Origin / Provenance, so this only has to
    /// **filter one list by row index** instead of aligning five.
    /// </remarks>
    public static QuadraticConstraintBatch FilterConstraintByRowIndex(
        QuadraticConstraintBatch constraints,
        IEnumerable<int> rows,
        IReadOnlyDictionary<int, int> oldToNewVariableIndexMap) {
        List<int> relatedRows = [.. rows.Distinct().Order()];

        var lhs = new SparseQuadraticMatrix();
        var signs = new List<ConstraintRelation>(relatedRows.Count);
        var rhs = new List<Flt64>(relatedRows.Count);
        var names = new List<string>(relatedRows.Count);
        var sources = new List<ConstraintSource>(relatedRows.Count);
        var identities = new List<ModelElementIdentity>(relatedRows.Count);

        for (int newRow = 0; newRow < relatedRows.Count; newRow++) {
            int oldRow = relatedRows[newRow];
            var keptEntries = new List<SparseQuadraticEntry>();
            foreach (SparseQuadraticEntry entry in constraints.SparseLhs.Rows[oldRow].Entries) {
                if (!oldToNewVariableIndexMap.TryGetValue(entry.Col1, out int newCol1)) {
                    continue;
                }
                int newCol2 = -1;
                if (entry.Col2 >= 0) {
                    if (!oldToNewVariableIndexMap.TryGetValue(entry.Col2, out newCol2)) {
                        continue;
                    }
                }
                keptEntries.Add(new SparseQuadraticEntry(newCol1, newCol2, entry.Value));
            }
            lhs.AddRow(new SparseQuadraticVector(keptEntries));

            signs.Add(constraints.Signs[oldRow]);
            rhs.Add(constraints.Rhs[oldRow]);
            names.Add(constraints.Names[oldRow]);
            sources.Add(constraints.Sources[oldRow]);
            if (oldRow < constraints.Identities.Count) {
                identities.Add(constraints.Identities[oldRow]);
            }
        }

        return new QuadraticConstraintBatch(
            lhs: lhs,
            signs: signs,
            rhs: rhs,
            names: names,
            sources: sources,
            identities: identities.Count == relatedRows.Count ? identities : null,
            identityNamespace: constraints.IdentityNamespace,
            identitySchemaVersion: constraints.IdentitySchemaVersion);
    }

    /// <summary>
    /// **把"哪些松弛变量构成不可行子集"落成一个可独立求解的子系统** / Turn "these slacks form the infeasible subset" into a standalone submodel.
    /// </summary>
    /// <param name="model">原模型视图 / Origin model view.</param>
    /// <param name="filterSlackIndices">构成 IIS 的松弛列索引 / Slack column indices forming the IIS.</param>
    /// <returns>子系统与行/列溯源 / The subsystem plus row/column provenance.</returns>
    /// <remarks>
    /// 对应`dump(model, slackVariables)`。流程：
    ///
    /// 1. 由松弛列回溯关联行（`RelatedRows`）；
    /// 2. 由关联行与松弛边界收集关联变量及其保留边界（`RelatedVariables`）；
    /// 3. **关联变量为空 ⇒ 返回原模型快照**——没有可裁剪的东西；
    /// 4. 建旧到新的列映射，裁剪约束（`FilterConstraintByRowIndex`）；
    /// 5. 目标函数按同一映射裁剪——**两端都要在映射里**，否则整项丢弃；
    /// 6. 未保留的边界替换成 `±∞`，即"这条边界不属于 IIS"。
    ///
    /// **第 6 步是 IIS 的语义核心**：`null` 不是"没有边界"，而是"这条边界不参与不可行"。
    /// 用 `±∞` 表示才让子系统**在去掉该边界后仍然可行**，从而证明它确实必要。
    ///
    /// **复用**：变量重建走 `LinearIisCompute.ProjectColumn`（与线性版同一份实现，
    /// 变量的字段集合与线性/二次无关），不复制第二份。
    ///
    /// ; the six steps are listed above. **Step 6 is the semantic core of an IIS**: `null` does not mean "no bound"
    /// but "this bound does not take part in the infeasibility", and substituting `±∞` is what lets the subsystem stay feasible once that bound is gone — proving it was
    /// necessary. **Reuse**: variable rebuilding goes through `LinearIisCompute.ProjectColumn` (the same implementation as the linear side; a variable's field set is
    /// unrelated to linear vs quadratic), so no second copy is written.
    /// </remarks>
    public static QuadraticIisModel Dump(
        IQuadraticTetradModelView model,
        IEnumerable<int> filterSlackIndices) {
        List<int> filter = [.. filterSlackIndices];
        List<int> relatedRows = RelatedRows(model.Variables, filter);
        List<IisRelatedVariable> relatedVariables = RelatedVariables(
            model.Variables, model.Constraints, filter, relatedRows);

        if (relatedVariables.Count == 0) {
            return new QuadraticIisModel(
                Subsystem: model as QuadraticTetradModel
                    ?? throw new global::System.InvalidOperationException(
                        "an empty IIS can only be snapshotted from a concrete QuadraticTetradModel"),
                OriginRows: [],
                OriginColumns: []);
        }

        var map = new Dictionary<int, int>(relatedVariables.Count);
        var originColumns = new List<int>(relatedVariables.Count);
        var variables = new List<ModelViewVariable>(relatedVariables.Count);
        INumericConstants<Flt64> constants = NumericConstantsRegistry.For<Flt64>();
        Flt64 negativeInfinity = constants.NegativeInfinity!.Value;
        Flt64 positiveInfinity = constants.PositiveInfinity!.Value;

        for (int newIndex = 0; newIndex < relatedVariables.Count; newIndex++) {
            IisRelatedVariable related = relatedVariables[newIndex];
            map[related.Variable.Index] = newIndex;
            originColumns.Add(related.Variable.Index);
            variables.Add(LinearIisCompute.ProjectColumn(
                source: related.Variable,
                index: newIndex,
                lowerBound: related.LowerBound ?? negativeInfinity,
                upperBound: related.UpperBound ?? positiveInfinity));
        }

        QuadraticConstraintBatch constraints = FilterConstraintByRowIndex(
            (QuadraticConstraintBatch)model.Constraints, relatedRows, map);

        var cells = new List<QuadraticObjectiveCell>();
        foreach (QuadraticObjectiveCell cell in model.Objective.Cells) {
            if (!map.TryGetValue(cell.Col1, out int newCol1)) {
                continue;
            }
            int newCol2 = -1;
            if (cell.Col2 >= 0 && !map.TryGetValue(cell.Col2, out newCol2)) {
                continue;
            }
            cells.Add(new QuadraticObjectiveCell(newCol1, newCol2, cell.Coefficient));
        }

        var objective = new Objective<QuadraticObjectiveCell>(
            category: model.Objective.Category,
            cells: cells,
            constant: model.Objective.Constant,
            identity: model.Objective.Identity,
            identityNamespace: model.Objective.IdentityNamespace,
            identitySchemaVersion: model.Objective.IdentitySchemaVersion);

        return new QuadraticIisModel(
            Subsystem: new QuadraticTetradModel(variables, constraints, objective, $"{model.Name}_iis"),
            OriginRows: relatedRows,
            OriginColumns: originColumns);
    }

    /// <summary>累积标记某个变量的边界归属 / Accumulate a variable's bound membership.</summary>
    /// <param name="marks">标记表 / Mark table.</param>
    /// <param name="index">变量索引 / Variable index.</param>
    /// <param name="keepLower">是否保留下界 / Whether to keep the lower bound.</param>
    /// <param name="keepUpper">是否保留上界 / Whether to keep the upper bound.</param>
    /// <remarks>
    /// **必须是"或"而不是"覆盖"**：同一个变量可能先作为边界被标记、再作为系数被标记，
    /// 覆盖会把边界丢掉，IIS 就少了一条必要性证据。
    ///
    /// **It must OR, not overwrite**: a variable can be marked first as a bound and later as a coefficient, and overwriting would drop the bound — losing one piece of
    /// necessity evidence for the IIS.
    /// </remarks>
    private static void MarkBound(
        SortedDictionary<int, (bool KeepLower, bool KeepUpper)> marks,
        int index,
        bool keepLower,
        bool keepUpper) {
        if (marks.TryGetValue(index, out (bool KeepLower, bool KeepUpper) previous)) {
            marks[index] = (previous.KeepLower || keepLower, previous.KeepUpper || keepUpper);
        } else {
            marks[index] = (keepLower, keepUpper);
        }
    }
}
