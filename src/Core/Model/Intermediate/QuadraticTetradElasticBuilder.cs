#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>二次四元弹性模型构建器（内部）/ Quadratic tetrad elastic model builder (internal)</summary>
/// <remarks>
/// 基线 `QuadraticTetradElasticBuilder.kt` **只有一个函数** `buildElasticModel()`：为每条约束和每个变量加松弛列，
/// 并在目标里惩罚它们。
///
/// **本类的五个方法此前全部是空壳**，而且不是无害的空壳：它们都构造一个
/// **空的约束批次**（空稀疏矩阵、空关系、空右端、空名称、空来源）和一个**空目标**，
/// 然后把原模型的变量表原样带过去，当作成功返回。
///
/// **它保留变量表，丢掉全部约束和目标，然后当作成功返回。** 调用方拿到的模型**看起来合法**
/// （有变量、有名字），但**一条约束都不剩**——求解它会得到任意可行解，而且没有任何报错。
///
/// 现在：**只保留有基线依据的 `BuildElasticModel`，并且真正实现**。
///
/// **B318 删掉了另外四个变体**（下界弹性、上界弹性、二值松弛、minmax 松弛）：基线
/// `QuadraticTetradElasticBuilder.kt` **只有 `buildElasticModel()` 一个函数**，那四个名字在基线里
/// **根本不存在**。它们是 C# 侧臆造的 API——没有基线对应、没有生产调用方，
/// **唯一的"用户"是我为它们写的那条 `VariantsWithoutBaselineBacking_ShouldThrow` 测试**。
/// 留着它们等于维护四个永远抛错的公开方法，还附一条测试去断言"它们确实抛错"。
///
/// The baseline `QuadraticTetradElasticBuilder.kt` has **exactly one function**, `buildElasticModel()`: add a slack column per constraint and per variable,
/// then penalise them in the objective.
///
/// **All five methods here used to be shells, and not harmless ones** — the snippet above **keeps the variable table, drops every constraint and the entire
/// objective, and returns as if it succeeded**. The caller receives a model that **looks legitimate** (variables, a name) but **retains not a single
/// constraint** — solving it yields an arbitrary feasible point with no error anywhere.
///
/// Now: **only `BuildElasticModel`, the one with baseline backing, remains, and it is genuinely implemented.**
///
/// **B318 deleted the other four variants** (lower-bound elasticity, upper-bound elasticity, binary slack, minmax slack): the baseline
/// `QuadraticTetradElasticBuilder.kt` has **exactly one function, `buildElasticModel()`**, and those four names **do not exist there at all**. They were invented
/// on the C# side — no baseline counterpart, no production caller, and **their only "user" was the `VariantsWithoutBaselineBacking_ShouldThrow` test I wrote for
/// them**. Keeping them meant maintaining four public methods that always throw, plus a test asserting that they do throw.
/// </remarks>
internal static class QuadraticTetradElasticBuilder {
    /// <summary>构建弹性模型 / Build elastic model</summary>
    /// <param name="model">原始二次模型 / Original quadratic model</param>
    /// <param name="elasticPenalty">弹性惩罚系数 / Elastic penalty coefficient</param>
    /// <returns>弹性二次模型 / Elastic quadratic model</returns>
    /// <remarks>
    /// 逐条约束加松弛列：
    ///
    /// | 原关系 | 追加的松弛列 | 目标系数 |
    /// | --- | --- | --- |
    /// | `LessEqual` | `_lb_slack`（行内系数 −1） | `elasticPenalty` |
    /// | `GreaterEqual` | `_ub_slack`（行内系数 +1） | `elasticPenalty` |
    /// | `Equal` | 两列 | `elasticPenalty` |
    ///
    /// 再逐变量按**自由程度**加界松弛行（与线性版本 `LinearTriadModel.BuildElastic` 完全同构）：
    /// 自由/已归一化的列**不加**；`PositiveFree` 只加下界；`NegativeFree` 只加上界；其余两界都加。
    ///
    /// **原变量的边界被改成 ±∞**，边界约束改由新增的弹性行表达——这是"弹性"的实质：
    /// 越界不再是不可行，而是有代价。
    ///
    /// `elasticPenalty` 用作松弛列的目标系数。基线用 `Flt64.one`，此处把它参数化以便按量级调权。
    ///
    /// Per-constraint slack columns (see the table above), then per-variable bound-slack rows classified by how free the variable is — structurally
    /// identical to the linear `LinearTriadModel.BuildElastic`. **Free and already-normalised columns get none**; `PositiveFree` gets only a lower bound;
    /// `NegativeFree` only an upper bound; everything else gets both.
    ///
    /// **The original variables' bounds become ±∞**, with the bound constraints expressed by the new elastic rows instead — that is the substance of
    /// "elastic": exceeding a bound is no longer infeasible, merely costly.
    ///
    /// `elasticPenalty` is used as the objective coefficient of the slack columns; the baseline uses `Flt64.one`, parameterised here so the weight can be tuned.
    /// </remarks>
    public static QuadraticTetradModel BuildElasticModel(
        BasicQuadraticTetradModel model,
        Flt64 elasticPenalty) {
        ArgumentNullException.ThrowIfNull(model);

        INumericConstants<Flt64> constants = NumericConstantsRegistry.For<Flt64>();
        Flt64 negativeInfinity = constants.NegativeInfinity!.Value;
        Flt64 positiveInfinity = constants.PositiveInfinity!.Value;
        var constraints = (QuadraticConstraintBatch)model.Constraints;
        int rows = constraints.Size;
        int columns = model.Variables.Count;
        var extraColumns = new List<ModelViewVariable>(rows * 2 + columns * 2);
        var slackColumns = new List<ModelViewVariable>(rows * 2 + columns * 2);
        var slackPairs = new List<(ModelViewVariable? Lower, ModelViewVariable? Upper)>(rows + columns);
        int colIndex = columns;

        ModelViewVariable NewSlack(string name, VariableSlack slack) {
            var column = new ModelViewVariable(
                index: colIndex,
                lowerBound: Flt64.Zero,
                upperBound: positiveInfinity,
                type: Continuous.Instance,
                name: name,
                initialResult: Flt64.Zero,
                slack: slack);
            colIndex++;
            extraColumns.Add(column);
            slackColumns.Add(column);
            return column;
        }

        for (int row = 0; row < rows; ++row) {
            string rowName = string.IsNullOrEmpty(constraints.Names[row]) ? $"cons{row}" : constraints.Names[row];
            var origin = new VariableSlack(Constraint: new ConstraintSlackOrigin(row, constraints.Signs[row]));
            if (constraints.Signs[row] == ConstraintRelation.LessEqual) {
                slackPairs.Add((NewSlack($"{rowName}_lb_slack", origin), null));
            }
            else if (constraints.Signs[row] == ConstraintRelation.GreaterEqual) {
                slackPairs.Add((null, NewSlack($"{rowName}_ub_slack", origin)));
            }
            else {
                ModelViewVariable lower = NewSlack($"{rowName}_lb_slack", origin);
                ModelViewVariable upper = NewSlack($"{rowName}_ub_slack", origin);
                slackPairs.Add((lower, upper));
            }
        }

        for (int col = 0; col < columns; ++col) {
            ModelViewVariable variable = model.Variables[col];
            if (variable.Free || variable.PositiveNormalized || variable.NegativeNormalized) {
                slackPairs.Add((null, null));
            }
            else if (variable.PositiveFree) {
                slackPairs.Add((NewSlack($"{variable.Name}_lb_slack", new VariableSlack(LowerBound: variable)), null));
            }
            else if (variable.NegativeFree) {
                slackPairs.Add((null, NewSlack($"{variable.Name}_ub_slack", new VariableSlack(UpperBound: variable))));
            }
            else {
                ModelViewVariable lower = NewSlack($"{variable.Name}_lb_slack", new VariableSlack(LowerBound: variable));
                ModelViewVariable upper = NewSlack($"{variable.Name}_ub_slack", new VariableSlack(UpperBound: variable));
                slackPairs.Add((lower, upper));
            }
        }

        var newRows = new List<SparseQuadraticVector>(rows + columns * 2);
        var signs = new List<ConstraintRelation>(rows + columns * 2);
        var rhs = new List<Flt64>(rows + columns * 2);
        var names = new List<string>(rows + columns * 2);
        var sources = new List<ConstraintSource>(rows + columns * 2);

        // 原有约束行：保留全部二次与线性项，再追加松弛列的线性项（`Col2 = -1` 表示线性项）。
        // Existing constraint rows: keep every quadratic and linear term, then append the slack columns' linear terms (`Col2 = -1` marks a linear term).
        for (int row = 0; row < rows; ++row) {
            var entries = new SparseQuadraticVector();
            foreach (QuadraticConstraintCell cell in constraints.Lhs[row]) {
                entries.Add(cell.Col1, cell.Col2, cell.Coefficient);
            }
            (ModelViewVariable? Lower, ModelViewVariable? Upper) pair = slackPairs[row];
            if (pair.Lower is { } lower) {
                entries.Add(lower.Index, -1, -Flt64.One);
            }
            if (pair.Upper is { } upper) {
                entries.Add(upper.Index, -1, Flt64.One);
            }
            newRows.Add(entries);
            signs.Add(constraints.Signs[row]);
            rhs.Add(constraints.Rhs[row]);
            names.Add($"{constraints.Names[row]}_elastic");
            sources.Add(ConstraintSource.Elastic);
        }

        for (int col = 0; col < columns; ++col) {
            (ModelViewVariable? Lower, ModelViewVariable? Upper) pair = slackPairs[rows + col];
            ModelViewVariable variable = model.Variables[col];
            if (pair.Lower is { } lower) {
                var entries = new SparseQuadraticVector();
                entries.Add(variable.Index, -1, Flt64.One);
                entries.Add(lower.Index, -1, Flt64.One);
                newRows.Add(entries);
                signs.Add(ConstraintRelation.GreaterEqual);
                rhs.Add(variable.LowerBound);
                names.Add($"{variable.Name}_lb_slack");
                sources.Add(ConstraintSource.ElasticLowerBound);
            }
            if (pair.Upper is { } upper) {
                var entries = new SparseQuadraticVector();
                entries.Add(variable.Index, -1, Flt64.One);
                entries.Add(upper.Index, -1, -Flt64.One);
                newRows.Add(entries);
                signs.Add(ConstraintRelation.LessEqual);
                rhs.Add(variable.UpperBound);
                names.Add($"{variable.Name}_ub_slack");
                sources.Add(ConstraintSource.ElasticUpperBound);
            }
        }

        var variables = new List<ModelViewVariable>(columns + extraColumns.Count);
        foreach (ModelViewVariable variable in model.Variables) {
            // 自由与已归一化的列保留原边界，其余列的边界改由弹性界行表达。
            // Free and normalised columns keep their bounds; the others are expressed by the elastic bound rows instead.
            variables.Add(
                variable.Free || variable.PositiveNormalized || variable.NegativeNormalized
                    ? variable
                    : variable.WithDomain(
                        lowerBound: negativeInfinity,
                        upperBound: positiveInfinity,
                        type: variable.Type));
        }
        variables.AddRange(extraColumns);

        var objectiveCells = new List<QuadraticObjectiveCell>(slackColumns.Count);
        foreach (ModelViewVariable slack in slackColumns) {
            objectiveCells.Add(new QuadraticObjectiveCell(slack.Index, -1, elasticPenalty));
        }

        return new QuadraticTetradModel(
            variables,
            new QuadraticConstraintBatch(
                new SparseQuadraticMatrix(newRows),
                signs,
                rhs,
                names,
                sources),
            QuadraticObjective.Create(ObjectCategory.Minimum, objectiveCells),
            $"{model.Name}_elastic");
    }
}
