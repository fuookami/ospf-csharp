#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>线性三元模型对偶构造与整理测试 / Dual construction and tidying tests for the linear triad model.</summary>
public class LinearTriadDualTest {
    private static readonly double Neg = double.NegativeInfinity;
    private static readonly double Pos = double.PositiveInfinity;

    [Fact]
    public void Dual_ShouldBuildRowColumnsBoundColumnsAndBalanceRows() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum);
        using LinearTriadModel dual = Unwrap(primal.Dual());

        Assert.Equal("primal-dual", dual.Name);
        Assert.Equal(7, dual.Variables.Count);
        Assert.Equal(6, dual.Constraints.Size);
        Assert.Equal(Enumerable.Range(0, 7), dual.Variables.Select(variable => variable.Index));
        Assert.All(dual.Variables, variable => Assert.True(variable.Type.IsContinuousType));
        Assert.All(dual.Variables, variable => Assert.Equal(Flt64.Zero, variable.InitialResult));

        AssertRowDual(dual.Variables[0], "c0_dual", Neg, 0);
        AssertRowDual(dual.Variables[1], "cons1_dual", 0, Pos);
        AssertRowDual(dual.Variables[2], "c2_dual", Neg, Pos);
        AssertRowDual(dual.Variables[3], "x2_lb_dual", 0, Pos);
        AssertRowDual(dual.Variables[4], "x2_ub_dual", Neg, 0);
        AssertRowDual(dual.Variables[5], "x4_ub_dual", Neg, 0);
        AssertRowDual(dual.Variables[6], "x5_lb_dual", 0, Pos);

        AssertBalance(dual, row: 0, cells: new[] { (0, 1d) }, relation: ConstraintRelation.Equal, rhs: 3, name: "x0_dual");
        AssertBalance(dual, row: 1, cells: new[] { (0, 2d), (1, 1d) }, relation: ConstraintRelation.LessEqual, rhs: -1, name: "x1_dual");
        AssertBalance(dual, row: 2, cells: new[] { (1, 1d), (2, 1d), (3, 1d), (4, 1d) }, relation: ConstraintRelation.Equal, rhs: 0, name: "x2_dual");
        AssertBalance(dual, row: 3, cells: Array.Empty<(int, double)>(), relation: ConstraintRelation.GreaterEqual, rhs: 6, name: "x3_dual");
        AssertBalance(dual, row: 4, cells: new[] { (2, -1d), (5, 1d) }, relation: ConstraintRelation.Equal, rhs: 5, name: "x4_dual");
        AssertBalance(dual, row: 5, cells: new[] { (2, 3d), (6, 1d) }, relation: ConstraintRelation.Equal, rhs: 2, name: "x5_dual");
        Assert.All(dual.Constraints.Sources, source => Assert.Equal(ConstraintSource.Dual, source));

        Assert.Equal(ObjectCategory.Maximum, dual.Objective.Category);
        Assert.Equal(Flt64.Zero, dual.Objective.Constant);
        Assert.Equal(
            new[] { (0, 4d), (1, 1d), (2, 0d), (3, -5d), (4, 7d), (5, 4d), (6, 2d) },
            dual.Objective.Cells.Select(cell => (cell.ColIndex, cell.Coefficient.ToDouble())));
    }

    [Fact]
    public void Dual_ShouldFlipRowBoundsAndBalanceSignsForMaximum() {
        using LinearTriadModel primal = Primal(ObjectCategory.Maximum);
        using LinearTriadModel dual = Unwrap(primal.Dual());

        AssertRowDual(dual.Variables[0], "c0_dual", 0, Pos);
        AssertRowDual(dual.Variables[1], "cons1_dual", Neg, 0);
        AssertRowDual(dual.Variables[2], "c2_dual", Neg, Pos);
        AssertRowDual(dual.Variables[3], "x2_lb_dual", Neg, 0);
        AssertRowDual(dual.Variables[4], "x2_ub_dual", 0, Pos);
        Assert.Equal(ObjectCategory.Minimum, dual.Objective.Category);
        Assert.Equal(ConstraintRelation.GreaterEqual, dual.Constraints.Signs[1]);
        Assert.Equal(ConstraintRelation.LessEqual, dual.Constraints.Signs[3]);
        Assert.Equal(ConstraintRelation.Equal, dual.Constraints.Signs[0]);
    }

    [Fact]
    public void Dual_ShouldRejectIntegerColumns() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum, integerColumn: true);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(primal.Dual()).Code);
    }

    [Fact]
    public void Dual_ShouldRejectDisposedModel() {
        LinearTriadModel primal = Primal(ObjectCategory.Minimum);
        primal.Dispose();
        Assert.Equal(
            ErrorCode.ApplicationStopped,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(primal.Dual()).Code);
    }

    [Fact]
    public void Dual_ShouldRejectInfiniteRowRightHandSide() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum, infiniteRhs: true);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(primal.Dual()).Code);
    }

    [Fact]
    public void TidyDualSolution_ShouldKeepRowAlignedPrefixAndRejectBadInput() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum);
        IReadOnlyList<Flt64> longVector = Enumerable.Range(0, 9).Select(index => new Flt64(index)).ToArray();
        Assert.Equal(
            new[] { 0d, 1d, 2d },
            Unwrap(primal.TidyDualSolution(longVector)).Select(value => value.ToDouble()));

        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>(
                primal.TidyDualSolution(new Flt64[] { Flt64.One })).Code);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>(
                primal.TidyDualSolution(new Flt64[] { Flt64.One, new Flt64(double.NaN), Flt64.One })).Code);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>(
                primal.TidyDualSolution(null!)).Code);
        primal.Dispose();
        Assert.Equal(
            ErrorCode.ApplicationStopped,
            Assert.IsType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>(
                primal.TidyDualSolution(longVector)).Code);
    }

    [Fact]
    public async Task SolveDualAsync_ShouldSolveTheBuiltDualModelAndTidyRowDuals() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum);
        var solver = new StubSolver(model => Results.Ok(new FeasibleSolverOutput<Flt64>(
            Obj: Flt64.Zero,
            Solution: new Solution<Flt64>(Enumerable.Range(0, 7).Select(index => new Flt64(index)).ToArray()),
            Time: TimeSpan.Zero,
            PossibleBestObj: Flt64.Zero,
            Gap: Flt64.Zero)));

        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await TriadDualSolverSupport.SolveDualAsync(primal, solver);

        Assert.Equal(new[] { 0d, 1d, 2d }, Unwrap(result).Select(value => value.ToDouble()));
        Assert.Equal("primal-dual", solver.Seen!.Name);
        Assert.Equal(7, solver.Seen.Variables.Count);
        // 对偶模型在返回前已释放，因此只能在求解回调发生时记录行数。
        // The dual model is released before returning, so the row count is captured while solving.
        Assert.Equal(6, solver.SeenRowCount);
    }

    [Fact]
    public async Task SolveDualAsync_ShouldPropagateSolverFailureAndMissingArguments() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum);
        var solver = new StubSolver(_ => Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(
            ErrorCode.ORModelUnbounded,
            "stub / stub")));
        Assert.Equal(
            ErrorCode.ORModelUnbounded,
            Assert.IsType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>(
                await TriadDualSolverSupport.SolveDualAsync(primal, solver)).Code);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>(
                await TriadDualSolverSupport.SolveDualAsync(null!, solver)).Code);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>(
                await TriadDualSolverSupport.SolveDualAsync(primal, null!)).Code);

        LinearTriadModel emptyRows = Primal(ObjectCategory.Minimum);
        emptyRows.Dispose();
        Assert.Equal(
            ErrorCode.ApplicationStopped,
            Assert.IsType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>(
                await TriadDualSolverSupport.SolveDualAsync(emptyRows, solver)).Code);
    }

    [Fact]
    public void FarkasDual_ShouldBuildSignedRowColumnsSlacksAndNormalizationRow() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum);
        using LinearTriadModel farkas = Unwrap(primal.FarkasDual());

        Assert.Equal("primal-farkas-dual", farkas.Name);
        Assert.Equal(11, farkas.Variables.Count);
        Assert.Equal(8, farkas.Constraints.Size);
        Assert.Equal(Enumerable.Range(0, 11), farkas.Variables.Select(variable => variable.Index));
        Assert.All(farkas.Variables, variable => Assert.True(variable.Type.IsContinuousType));
        // 行列符号由约束方向决定，等式行再补正负松弛列；自由列不产生界对偶列。
        // Row column signs follow the constraint relation, equality rows add slack columns, and free columns add none.
        AssertFarkasColumn(farkas.Variables[0], "c0_farkas", 0, Pos);
        AssertFarkasColumn(farkas.Variables[1], "cons1_farkas", Neg, 0);
        AssertFarkasColumn(farkas.Variables[2], "c2_farkas", Neg, Pos);
        AssertFarkasColumn(farkas.Variables[3], "c2_pos_slack", 0, Pos);
        AssertFarkasColumn(farkas.Variables[4], "c2_neg_slack", 0, Pos);
        AssertFarkasColumn(farkas.Variables[5], "x1_lb_dual", Neg, 0);
        AssertFarkasColumn(farkas.Variables[7], "x2_ub_dual", 0, Pos);
        AssertFarkasColumn(farkas.Variables[8], "x3_ub_dual", 0, Pos);
        AssertFarkasColumn(farkas.Variables[10], "x5_lb_dual", Neg, 0);
        Assert.Null(farkas.Variables[6].Origin);
        Assert.All(farkas.Constraints.Sources, source => Assert.Equal(ConstraintSource.FarkasDual, source));

        AssertBalance(farkas, row: 0, cells: new[] { (0, 1d) }, relation: ConstraintRelation.Equal, rhs: 0, name: "x0_farkas_dual");
        AssertBalance(farkas, row: 1, cells: new[] { (0, 2d), (1, 1d), (5, 1d) }, relation: ConstraintRelation.Equal, rhs: 0, name: "x1_farkas_dual");
        AssertBalance(farkas, row: 2, cells: new[] { (1, 1d), (2, 1d), (6, 1d), (7, 1d) }, relation: ConstraintRelation.Equal, rhs: 0, name: "x2_farkas_dual");
        AssertBalance(farkas, row: 3, cells: new[] { (8, 1d) }, relation: ConstraintRelation.Equal, rhs: 0, name: "x3_farkas_dual");
        AssertBalance(farkas, row: 4, cells: new[] { (2, -1d), (9, 1d) }, relation: ConstraintRelation.Equal, rhs: 0, name: "x4_farkas_dual");
        AssertBalance(farkas, row: 5, cells: new[] { (2, 3d), (10, 1d) }, relation: ConstraintRelation.Equal, rhs: 0, name: "x5_farkas_dual");
        // 归一化行使用右端值与变量界，强制乘子组合等于 -1，因此证书不可能全为零。
        // The normalization row uses right-hand sides and variable bounds to force the combination to -1, so an
        // all-zero certificate is impossible.
        AssertBalance(farkas, row: 6,
            cells: new[] { (0, 4d), (1, 1d), (2, 0d), (5, 0d), (6, -5d), (7, 7d), (8, 0d), (9, 4d), (10, 2d) },
            relation: ConstraintRelation.Equal, rhs: -1, name: "normalization");
        AssertBalance(farkas, row: 7, cells: new[] { (2, 1d), (3, -1d), (4, 1d) }, relation: ConstraintRelation.Equal, rhs: 0, name: "c2_abs");

        Assert.Equal(ObjectCategory.Minimum, farkas.Objective.Category);
        Assert.Equal(Flt64.Zero, farkas.Objective.Constant);
        Assert.Equal(
            // 等式行的自由乘子列不进入目标，其幅度只由正负松弛列计价。
            // The free multiplier column of an equality row stays out of the objective; only its slacks are priced.
            new[] { (0, 1d), (1, -1d), (3, 1d), (4, 1d) },
            farkas.Objective.Cells.Select(cell => (cell.ColIndex, cell.Coefficient.ToDouble())));
    }

    [Fact]
    public void FarkasDual_ShouldRejectDisposedModelAndInfiniteRowRightHandSide() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum, infiniteRhs: true);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(primal.FarkasDual()).Code);

        LinearTriadModel disposed = Primal(ObjectCategory.Minimum);
        disposed.Dispose();
        Assert.Equal(
            ErrorCode.ApplicationStopped,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(disposed.FarkasDual()).Code);
    }

    /// <summary>
    /// 单列两行矛盾 y ≥ 2.5 与 y ≤ 2：Farkas 模型只有一行平衡等式与归一化行，手工解为 (-2, +2)。
    /// One free column with the contradicting rows y ≥ 2.5 and y ≤ 2: the Farkas model holds a single balance row plus the
    /// normalization row, whose hand solution is (-2, +2).
    /// </summary>
    [Fact]
    public async Task SolveFarkasDualAsync_ShouldReturnRowAlignedCertificateFromTheFarkasModel() {
        using LinearTriadModel primal = Contradiction();
        using LinearTriadModel farkas = Unwrap(primal.FarkasDual());
        Assert.Equal(2, farkas.Variables.Count);
        Assert.Equal(2, farkas.Constraints.Size);
        AssertBalance(farkas, row: 0, cells: new[] { (0, 1d), (1, 1d) }, relation: ConstraintRelation.Equal, rhs: 0, name: "y_farkas_dual");
        AssertBalance(farkas, row: 1, cells: new[] { (0, 2.5d), (1, 2d) }, relation: ConstraintRelation.Equal, rhs: -1, name: "normalization");

        var solver = new StubSolver(model => {
            var values = new Flt64[model.Variables.Count];
            for (int index = 0; index < values.Length; index++) {
                values[index] = index == 0 ? new Flt64(-2) : new Flt64(2);
            }
            return Results.Ok(new FeasibleSolverOutput<Flt64>(
                Obj: new Flt64(-4),
                Solution: new Solution<Flt64>(values),
                Time: TimeSpan.Zero,
                PossibleBestObj: new Flt64(-4),
                Gap: Flt64.Zero));
        });

        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await TriadDualSolverSupport.SolveFarkasDualAsync(primal, solver);

        Assert.Equal(new[] { -2d, 2d }, Unwrap(result).Select(value => value.ToDouble()));
        Assert.Equal("primal-farkas-dual", solver.Seen!.Name);
        Assert.Equal(2, solver.SeenRowCount);
    }

    /// <summary>
    /// 首行即等式的矛盾模型：行乘子列必须占满前 m 个索引，逐行证书才能与原始约束行对齐。
    /// A contradicting model whose first row is an equality: the row multiplier columns must fill the first m indexes
    /// so the per-row certificate stays aligned with the original constraint rows.
    /// </summary>
    [Fact]
    public async Task FarkasDual_ShouldKeepRowColumnsAlignedWhenEqualityRowComesFirst() {
        using LinearTriadModel primal = LeadingEquality();
        using LinearTriadModel farkas = Unwrap(primal.FarkasDual());

        // 2 个行乘子列在前，等式行的正负松弛列随后；自由列不产生界对偶列。
        // The two row multiplier columns come first, then the equality row's slacks; the free column adds no bound duals.
        Assert.Equal(4, farkas.Variables.Count);
        AssertFarkasColumn(farkas.Variables[0], "eq_farkas", Neg, Pos);
        AssertFarkasColumn(farkas.Variables[1], "cap_farkas", 0, Pos);
        AssertFarkasColumn(farkas.Variables[2], "eq_pos_slack", 0, Pos);
        AssertFarkasColumn(farkas.Variables[3], "eq_neg_slack", 0, Pos);
        AssertBalance(farkas, row: 0, cells: new[] { (0, 1d), (1, 1d) }, relation: ConstraintRelation.Equal, rhs: 0, name: "y_farkas_dual");
        AssertBalance(farkas, row: 1, cells: new[] { (0, 3d), (1, 2d) }, relation: ConstraintRelation.Equal, rhs: -1, name: "normalization");
        AssertBalance(farkas, row: 2, cells: new[] { (0, 1d), (2, -1d), (3, 1d) }, relation: ConstraintRelation.Equal, rhs: 0, name: "eq_abs");
        Assert.Equal(
            new[] { (1, 1d), (2, 1d), (3, 1d) },
            farkas.Objective.Cells.Select(cell => (cell.ColIndex, cell.Coefficient.ToDouble())));

        var solver = new StubSolver(_ => Results.Ok(new FeasibleSolverOutput<Flt64>(
            Obj: new Flt64(2),
            Solution: new Solution<Flt64>([new Flt64(-1), Flt64.One, Flt64.Zero, Flt64.One]),
            Time: TimeSpan.Zero,
            PossibleBestObj: new Flt64(2),
            Gap: Flt64.Zero)));

        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> certificate =
            await TriadDualSolverSupport.SolveFarkasDualAsync(primal, solver);

        // 手工解为等式行 -1、上界行 +1，-1·(y=3) + 1·(y≤2) 即得出 0 ≤ -1。
        // The hand solution is -1 for the equality row and +1 for the upper row, and -1·(y=3) + 1·(y≤2) gives 0 ≤ -1.
        Assert.Equal(new[] { -1d, 1d }, Unwrap(certificate).Select(value => value.ToDouble()));
    }

    [Fact]
    public async Task SolveFarkasDualAsync_ShouldPropagateBuildAndSolverFailures() {
        var solver = new StubSolver(_ => Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(
            ErrorCode.ORModelUnbounded,
            "stub / stub")));
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum);
        Assert.Equal(
            ErrorCode.ORModelUnbounded,
            Assert.IsType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>(
                await TriadDualSolverSupport.SolveFarkasDualAsync(primal, solver)).Code);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>(
                await TriadDualSolverSupport.SolveFarkasDualAsync(null!, solver)).Code);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>(
                await TriadDualSolverSupport.SolveFarkasDualAsync(primal, null!)).Code);
    }

    /// <summary>极小化/极大化模型：3 行 6 列，覆盖全部变量域分支。 / Three rows and six columns covering every variable-domain branch.</summary>
    private static LinearTriadModel Primal(
        ObjectCategory category,
        bool infiniteRhs = false,
        bool integerColumn = false,
        bool withRowIdentities = false) {
        ModelViewVariable[] variables = [
            Column(0, "x0", Neg, Pos),
            integerColumn ? IntegerColumn(1, "x1") : Column(1, "x1", 0, Pos),
            Column(2, "x2", -5, 7),
            Column(3, "x3", Neg, 0),
            Column(4, "x4", Neg, 4),
            Column(5, "x5", 2, Pos),
        ];
        SparseVector[] rows = [
            new([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, new Flt64(2))]),
            new([new SparseVectorEntry(1, Flt64.One), new SparseVectorEntry(2, Flt64.One)]),
            new([new SparseVectorEntry(2, Flt64.One), new SparseVectorEntry(4, -Flt64.One), new SparseVectorEntry(5, new Flt64(3))]),
        ];
        return new LinearTriadModel(
            variables: variables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(rows),
                signs: [ConstraintRelation.LessEqual, ConstraintRelation.GreaterEqual, ConstraintRelation.Equal],
                rhs: [new Flt64(4), new Flt64(1), infiniteRhs ? new Flt64(Pos) : Flt64.Zero],
                names: ["c0", "", "c2"],
                sources: [ConstraintSource.Origin, ConstraintSource.Origin, ConstraintSource.Origin],
                // **B1224：可选地给三行标上稳定身份。不给时行为与之前完全相同。**
                // **B1224: optionally stamp the three rows with stable identities; without them nothing changes.**
                // **`Stable` 身份必须带命名空间、schema 与非空来源——`ValidateIdentity` 会拒绝缺任何一项的稳定身份
                // （B1225 读源码确认；B1224 初版只给了 `Kind`/`Id`/`Scope`，于是 `Dual()` 在那一关失败）。**
                // **这里给的是模型<b>内</b>身份（`ModelLocal`），因为它不需要注册表，正好用于"行身份存在但不稳定"
                // 这一侧；稳定的那一侧由真实注册流程覆盖。**
                //
                // **A `Stable` identity must carry a namespace, a schema and non-empty provenance — `ValidateIdentity`
                // rejects a stable identity missing any of them (read from the source in B1225; B1224's first version
                // supplied only `Kind`/`Id`/`Scope`, so `Dual()` failed there).** **These are <b>model-local</b> identities,
                // which need no registry and are exactly the "row identity present but not stable" side; the stable side
                // is covered by the real registration flow.**
                identities: withRowIdentities
                    ? [
                        new ModelElementIdentity(
                            ModelElementKind.Constraint,
                            new ModelElementId($"model-local-derived:test-constraint-0"),
                            ModelElementScope.ModelLocal),
                        new ModelElementIdentity(
                            ModelElementKind.Constraint,
                            new ModelElementId($"model-local-derived:test-constraint-1"),
                            ModelElementScope.ModelLocal),
                        new ModelElementIdentity(
                            ModelElementKind.Constraint,
                            new ModelElementId($"model-local-derived:test-constraint-2"),
                            ModelElementScope.ModelLocal),
                      ]
                    : null),
            objective: new Objective<LinearObjectiveCell>(
                category: category,
                cells: [
                    new LinearObjectiveCell(0, new Flt64(3)),
                    new LinearObjectiveCell(1, -Flt64.One),
                    new LinearObjectiveCell(3, new Flt64(6)),
                    new LinearObjectiveCell(4, new Flt64(5)),
                    new LinearObjectiveCell(5, new Flt64(2)),
                ],
                constant: new Flt64(7)),
            name: "primal");
    }

    private static ModelViewVariable Column(int index, string name, double lower, double upper) => new(
        index: index,
        lowerBound: new Flt64(lower),
        upperBound: new Flt64(upper),
        type: Continuous.Instance,
        name: name);

    /// <summary>单自由列的矛盾模型：y ≥ 2.5 且 y ≤ 2，仅靠行乘子即可给出证书。 / One free column contradicting itself: y ≥ 2.5 and y ≤ 2, certifiable by row multipliers alone.</summary>
    private static LinearTriadModel Contradiction() => new(
        variables: [Column(0, "y", Neg, Pos)],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([
                new SparseVector([new SparseVectorEntry(0, Flt64.One)]),
                new SparseVector([new SparseVectorEntry(0, Flt64.One)]),
            ]),
            signs: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
            rhs: [new Flt64(2.5), new Flt64(2)],
            names: ["floor", "cap"],
            sources: [ConstraintSource.Origin, ConstraintSource.Origin]),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: [new LinearObjectiveCell(0, Flt64.One)]),
        name: "primal");

    /// <summary>首行为等式的矛盾模型：y = 3 且 y ≤ 2。 / Contradicting model whose first row is an equality: y = 3 and y ≤ 2.</summary>
    private static LinearTriadModel LeadingEquality() => new(
        variables: [Column(0, "y", Neg, Pos)],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([
                new SparseVector([new SparseVectorEntry(0, Flt64.One)]),
                new SparseVector([new SparseVectorEntry(0, Flt64.One)]),
            ]),
            signs: [ConstraintRelation.Equal, ConstraintRelation.LessEqual],
            rhs: [new Flt64(3), new Flt64(2)],
            names: ["eq", "cap"],
            sources: [ConstraintSource.Origin, ConstraintSource.Origin]),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: [new LinearObjectiveCell(0, Flt64.One)]),
        name: "primal");

    private static ModelViewVariable IntegerColumn(int index, string name) => new(
        index: index,
        lowerBound: Flt64.Zero,
        upperBound: new Flt64(10),
        type: Integer.Instance,
        name: name);

    /// <summary>
    /// **真实 `Dual()` 路径上，每个行对偶变量都带着来源行与关系** /
    /// On the real `Dual()` path every row-dual variable carries its origin row and relation.
    /// </summary>
    /// <remarks>
    /// **这条判据存在的唯一理由是回答"`DualOrigin` 是不是装饰"。**
    ///
    /// **一个永远为 `null` 的属性比不存在的属性更糟：不存在的属性会编译失败，
    /// 而永远为 `null` 的属性会让读者以为信息在那里。**
    ///
    /// **所以这条判据走<b>真实</b>的对偶构造，而不是手工 `new ModelViewVariable(dualOrigin: ...)`——
    /// 后者只证明"能赋值"，不证明"有人赋过"。**
    ///
    /// **同时它钉住了行号的含义：** **对偶变量表的第 `i` 个行对偶列，其 `DualOrigin.RowIndex` 必须是 `i`，
    /// 且关系必须等于原模型第 `i` 行的关系。** **如果哪天对偶列顺序变了而来源没跟着变，
    /// 这条会红——而所有数值判据仍绿，因为数值只依赖边界，不依赖来源。**
    ///
    /// **The only reason this criterion exists is to answer "is `DualOrigin` decoration".**
    ///
    /// **A property that is always `null` is worse than a missing one: a missing property fails to compile, while a
    /// permanently `null` property makes the reader believe the information is there.**
    ///
    /// **So this goes through the <b>real</b> dual construction rather than a hand-built
    /// `new ModelViewVariable(dualOrigin: ...)`, which would only prove "assignable".**
    ///
    /// **It also pins what the row index means: the `i`-th row-dual column must have `DualOrigin.RowIndex == i` and a
    /// relation equal to the primal's `i`-th row.** **If the dual column order ever changes without the origin following,
    /// this goes red while every numeric criterion stays green, because the numbers depend on bounds and not on origin.**
    /// </remarks>
    [Fact]
    public void Dual_ShouldFillDualOriginOnEveryRowDualColumn() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum);
        using LinearTriadModel dual = Unwrap(primal.Dual());

        int rows = primal.Constraints.Size;

        for (int row = 0; row < rows; ++row) {
            ModelViewVariable variable = dual.Variables[row];

            Assert.NotNull(variable.DualOrigin);
            Assert.Equal(row, variable.DualOrigin!.RowIndex);
            Assert.Equal(primal.Constraints.Signs[row], variable.DualOrigin.Relation);
        }

        // **界对偶列不是任何一行的对偶，所以它们没有来源。**
        // **Bound-dual columns are not the dual of any row, so they carry no origin.**
        for (int column = rows; column < dual.Variables.Count; ++column) {
            Assert.Null(dual.Variables[column].DualOrigin);
        }
    }

    /// <summary>
    /// **没有对偶来源的变量保持 `null`，不伪造一个** / A variable without a dual origin keeps `null`.
    /// </summary>
    [Fact]
    public void AnOrdinaryVariable_ShouldKeepItsDualOriginNull() {
        var variable = new ModelViewVariable(
            index: 0, lowerBound: Flt64.Zero, upperBound: Flt64.One,
            type: Continuous.Instance, name: "x");

        Assert.Null(variable.DualOrigin);
    }

    /// <summary>
    /// **每条 Farkas 生成列都有身份，且身份互不相同** /
    /// Every Farkas-generated column has an identity, and they are all distinct.
    /// </summary>
    /// <remarks>
    /// B1223 按基线 `LinearTriadModel.kt` 的 `withDerivedIdentity` 调用点补上三种角色：
    /// `farkas-constraint`（行乘子）、`farkas-slack`（等式行正负松弛）、`farkas-bound`（界乘子）。
    ///
    /// **底层 `DerivedModelElementIdentity.Create` 早就在，`MatrixIdentitySupport` 一直在用；
    /// 缺的只是 Farkas 这条路径没调用它。所以这不是"实现新能力"，而是"把已有能力接到漏掉的路径上"。**
    ///
    /// **这类缺口需要特定形状的判据：** **判"生成了身份"不够，还要判"每条列的身份不同"——
    /// 一个所有列共用一个身份的实现在"有身份"下全绿，而它比没有身份更糟：
    /// 下游按身份聚合时会把所有 Farkas 列合成一条。**
    ///
    /// **The underlying `DerivedModelElementIdentity.Create` already existed and `MatrixIdentitySupport` was already using
    /// it; the Farkas path simply never called it. Not a new capability, but an existing one wired onto a skipped path.**
    ///
    /// **This shape of gap needs a particular criterion: "identities were generated" is not enough — they must also
    /// differ.** **All columns sharing one identity passes "has identities" while being worse than none: a downstream
    /// aggregation by identity would fuse them.**
    /// </remarks>
    [Fact]
    public void FarkasDual_ShouldGiveEveryGeneratedColumnADistinctIdentity() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum);
        using LinearTriadModel farkas = Unwrap(primal.FarkasDual());

        ModelViewVariable[] generated = farkas.Variables
            .Where(variable => variable.Identity is not null)
            .ToArray();

        Assert.NotEmpty(generated);

        string[] ids = generated.Select(variable => variable.Identity!.Id.Value).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(generated, variable => Assert.Equal(ModelElementKind.Variable, variable.Identity!.Kind));
    }

    /// <summary>
    /// **三个 Farkas 角色都出现了** / All three Farkas roles appear.
    /// </summary>
    /// <remarks>
    /// **只判"有身份"会放过"只接了一种角色"的实现。**
    /// **基线有三种角色，分别来自三处不同的调用点，所以"三种都在"更接近这个批次真正做的事。**
    ///
    /// **"Has an identity" would let through an implementation that wired only one role.** **The three roles come from
    /// three call sites, so "all three appear" is closer to what this batch did.**
    /// </remarks>
    [Fact]
    public void FarkasDual_ShouldCarryAllThreeRoles() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum);
        using LinearTriadModel farkas = Unwrap(primal.FarkasDual());

        string[] ids = farkas.Variables
            .Where(variable => variable.Identity is not null)
            .Select(variable => variable.Identity!.Id.Value)
            .ToArray();

        Assert.Contains(ids, id => id.Contains("farkas-constraint", StringComparison.Ordinal));
        Assert.Contains(ids, id => id.Contains("farkas-slack", StringComparison.Ordinal));
        Assert.Contains(ids, id => id.Contains("farkas-bound", StringComparison.Ordinal));
    }

    /// <summary>
    /// **身份是确定性的：同一个原模型求两次得到同样的身份** / Identities are deterministic.
    /// </summary>
    /// <remarks>
    /// **确定性是派生身份的全部意义。** **一个"唯一但每次都不同"的标识（计数器、时间戳）
    /// 能通过"互不相同"那条，却无法用于任何跨运行的对账。**
    ///
    /// **Determinism is the entire point.** **A "unique but different each time" identifier (counter, timestamp) passes
    /// the distinctness criterion while being useless for cross-run reconciliation.**
    /// </remarks>
    [Fact]
    public void FarkasDual_ShouldProduceDeterministicIdentities() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum);
        using LinearTriadModel first = Unwrap(primal.FarkasDual());
        using LinearTriadModel second = Unwrap(primal.FarkasDual());

        string[] firstIds = first.Variables
            .Where(variable => variable.Identity is not null)
            .Select(variable => variable.Identity!.Id.Value)
            .ToArray();
        string[] secondIds = second.Variables
            .Where(variable => variable.Identity is not null)
            .Select(variable => variable.Identity!.Id.Value)
            .ToArray();

        Assert.Equal(firstIds, secondIds);
    }

    /// <summary>
    /// **对偶列断言：无来源、<b>无派生身份</b>** / Dual-column assertions: no origin, <b>no derived identity</b>.
    /// </summary>
    /// <remarks>
    /// **`Dual()` 路径不带派生身份（基线也不带），而 `FarkasDual()` 路径带（B1223 补的）。**
    /// **所以这条断言只能用在对偶列上——它此前被 Farkas 断言复用，B1223 让 Farkas 列有了身份之后那两处就红了。**
    ///
    /// **这正是"判据前提被有意改变"的典型情形：** **红的原因是实现按基线前进，
    /// 而不是实现退化。所以是拆分判据，不是回退实现。**
    ///
    /// **The `Dual()` path carries no derived identity (nor does the baseline), while `FarkasDual()` does (added in
    /// B1223).** **So this assertion only applies to dual columns — it was previously reused for Farkas columns, and
    /// those two went red once Farkas columns gained identities.**
    ///
    /// **This is the textbook "the criterion's premise was deliberately changed" case: the red is because the
    /// implementation moved toward the baseline, not away.** **So split the criterion; do not revert the
    /// implementation.**
    /// </remarks>
    /// <summary>
    /// **没有行身份时空表是正确答案，不是失败** / An empty table is the correct answer when rows carry no identity.
    /// </summary>
    /// <remarks>
    /// **这一条是本批<b>第一个写错方向</b>的判据，值得原样记下来。**
    ///
    /// **初版写的是"表必须覆盖每一行"，跑起来红了：期望 3，实得 0。**
    ///
    /// **而红的原因不是实现错了，是<b>判据的前提错了</b>：** **测试用的原模型没有给变量和约束设身份
    /// （`Identities` 为空 ⇒ `Ids` 为空），而基线的 `tidyDualSolution` 在 `origins` 为空时同样返回空 map。**
    ///
    /// **所以"空表"与基线一致，是正确答案。** **一个要求"表非空"的判据会逼实现去<b>伪造</b>身份，
    /// 而伪造出来的身份比没有身份更危险——它会出现在下游的账里，却指向不存在的约束。**
    ///
    /// **真正的判据要分两支：没有身份 ⇒ 空表；有身份 ⇒ 完整覆盖。** **第二支在下面两条里用<b>带身份</b>的模型测。**
    ///
    /// **The first criterion of this batch pointed the wrong way, and is worth recording verbatim.**
    ///
    /// **The first version said "the table must cover every row"; it went red, expecting 3 and getting 0.**
    ///
    /// **The red was not a wrong implementation but a <b>wrong premise</b>: the test primal assigns no identities to its
    /// variables or constraints (`Identities` empty ⇒ `Ids` empty), and the baseline's `tidyDualSolution` likewise returns
    /// an empty map when `origins` is empty.**
    ///
    /// **So "empty table" agrees with the baseline and is correct.** **A criterion demanding a non-empty table would force
    /// the implementation to <b>fabricate</b> identities — and a fabricated identity is more dangerous than none: it
    /// appears in a downstream ledger while pointing at a constraint that does not exist.**
    ///
    /// **The real criterion splits in two: no identities ⇒ empty table; identities ⇒ full coverage.** **The second branch is
    /// tested below with an identity-carrying model.**
    /// </remarks>
    [Fact]
    public void TidyDualSolutionByIdentity_ShouldBeEmptyWhenRowsCarryNoIdentity() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum);
        using LinearTriadModel dual = Unwrap(primal.Dual());

        Assert.Empty(primal.Constraints.Ids);

        double[] values = Enumerable.Repeat(1.0, dual.Variables.Count).ToArray();
        IReadOnlyDictionary<ConstraintId, Flt64> table =
            Unwrap(dual.TidyDualSolutionByIdentity(values.Select(value => new Flt64(value)).ToArray()));

        Assert.Empty(table);
    }

    /// <summary>
    /// **行对齐路径：有行身份时表覆盖每一行，键都是真实身份，且互不重复** /
    /// Row-aligned path: with row identities the table covers every row, every key is a real identity, and none repeat.
    /// </summary>
    /// <remarks>
    /// **这一条走<b>行对齐</b>路径（对原模型自己调 `TidyDualSolutionByIdentity`），而不是走 `Dual()`。**
    ///
    /// **原因是造一个"带身份的模型"比想象的难：** **初版我给三行塞了手写的 `ModelElementIdentity`，
    /// 于是 `Dual()` 在身份集合校验那一关就失败了——那些身份不在任何注册表里。**
    ///
    /// **而这件事本身是有意义的发现：** **身份不是"填个字符串"，它必须与一份注册表自洽。**
    /// **所以判据要么走真实注册流程、要么就绕开 `Dual()`。</b>本批选后者，
    /// 因为行对齐路径<b>不需要</b>派生身份，它只需要行身份，而 `Ids` 就够。**
    ///
    /// **This one takes the <b>row-aligned</b> path (calling `TidyDualSolutionByIdentity` on the primal itself), not
    /// `Dual()`.**
    ///
    /// **Because building an "identity-carrying model" is harder than it looks: the first version stamped three
    /// hand-written `ModelElementIdentity` values and `Dual()` failed at identity-set validation — those identities are
    /// in no registry.**
    ///
    /// **That is itself a useful finding: an identity is not "a string you fill in"; it must be self-consistent with a
    /// registry.** **So a criterion must either run the real registration flow or avoid `Dual()`.** **This batch takes
    /// the latter: the row-aligned path needs no derived identities, only row identities, and `Ids` suffices.**
    /// </remarks>
    [Fact]
    public void TidyDualSolutionByIdentity_ShouldCoverEveryRowWhenIdentitiesExist() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum, withRowIdentities: true);

        IReadOnlyList<ConstraintId> rowIds = primal.Constraints.Ids;
        Assert.Equal(primal.Constraints.Size, rowIds.Count);

        double[] values = Enumerable.Repeat(1.0, primal.Variables.Count).ToArray();
        IReadOnlyDictionary<ConstraintId, Flt64> table =
            Unwrap(primal.TidyDualSolutionByIdentity(values.Select(value => new Flt64(value)).ToArray()));

        Assert.Equal(rowIds.Count, table.Count);
        Assert.All(table.Keys, key => Assert.Contains(key, rowIds));
        Assert.Equal(
            table.Count,
            table.Keys.Select(key => key.Value).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// **对偶模型把原模型的行身份快照带过来了** / The dual model carries the primal's row-identity snapshot.
    /// </summary>
    /// <remarks>
    /// **这一条钉的是本批最容易被忽略的一步：** **对偶模型是<b>新建</b>的，它的 `Constraints.Ids` 是自己的行身份
    /// （构造时看不到原模型），而 `DualOrigin.RowIndex` 指向的是<b>原模型</b>的行。**
    ///
    /// **所以对偶模型必须把原模型的行身份<b>带过来</b>，否则那两半永远对不上。**
    ///
    /// **基线在这一处不需要"带"：它的 `dualOrigin` 是约束对象引用，天生就指回原约束。
    /// C# 因为用行号表达同一件事，必须补一次快照——这是表示差异带来的<b>额外一步</b>，不是额外能力。**
    ///
    /// **This pins the step most easily missed in this batch:** **a dual model is <b>newly built</b>; its own
    /// `Constraints.Ids` are its own rows (the originals are not visible at construction), while `DualOrigin.RowIndex`
    /// points at the <b>primal's</b> rows.**
    ///
    /// **So the dual model must <b>carry over</b> the primal's row identities, or the two halves never line up.**
    ///
    /// **The baseline needs no carry: its `dualOrigin` is a constraint object reference that points home by nature. C#
    /// expresses the same thing with a row index, so it must add one snapshot — an <b>extra step</b> caused by the
    /// representation difference, not an extra capability.**
    /// </remarks>
    /// <summary>
    /// **对偶模型把原模型的行身份带过来了，且整理表用的是那些身份** /
    /// The dual model carries the primal's row identities, and the tidy table uses them.
    /// </summary>
    /// <remarks>
    /// **B1224 把这条判据记为"未测"，B1225 让它可测了。**
    ///
    /// **B1224 的阻塞点是：给三行塞手写 `ModelElementIdentity` 后 `Dual()` 失败。
    /// B1225 读了 `ValidateIdentity` 的源码，发现原因很具体——`Stable` 作用域<b>必须</b>带
    /// 命名空间、schema 与非空来源，而 B1224 那三份只给了 `Kind`/`Id`/`Scope`。**
    ///
    /// **改用 `ModelLocal` 作用域后 `Dual()` 直接通过：** **模型内身份不需要注册表，
    /// 而它在这个判据里与稳定身份等价——两者都能让"行有身份"这件事成立。**
    ///
    /// **于是那条变异（"完全不读 `PrimalRowIdentities`"）现在能被抓住了，见本批的计划文档。**
    ///
    /// **这一批没有改任何产品代码是对的：** **B1224 的判断"需要更大的改动"是错的，
    /// 真正的阻碍只是测试模型少给了三个字段——而那个判断是在<b>没读校验源码</b>时下的。**
    ///
    /// **"做不到"这个结论必须先读一遍拒绝它的那段代码，否则它只是"我没找到办法"的另一种说法。**
    ///
    /// **B1224 recorded this criterion as "untested"; B1225 made it testable.**
    ///
    /// **The blocker in B1224 was: hand-written `ModelElementIdentity` values made `Dual()` fail. B1225 read
    /// `ValidateIdentity` and found the cause precisely — a `Stable` scope <b>must</b> carry a namespace, a schema and
    /// non-empty provenance, and B1224 supplied only `Kind`/`Id`/`Scope`.**
    ///
    /// **Switching to `ModelLocal` let `Dual()` pass immediately: a model-local identity needs no registry, and it is
    /// equivalent to a stable one for this criterion — both make "the row has an identity" true.**
    ///
    /// **So the mutation ("do not read `PrimalRowIdentities` at all") is now caught; see this batch's plan.**
    ///
    /// **Changing no product code was the right call: B1224's judgement "this needs a bigger change" was wrong, and
    /// the real obstacle was three missing fields on a test model — a judgement made <b>without reading the validation
    /// source</b>.**
    ///
    /// **"Cannot be done" must come after reading the code that refuses it; otherwise it is just another way of saying
    /// "I did not find a way".**
    /// </remarks>
    [Fact]
    public void Dual_ShouldCarryThePrimalRowIdentitySnapshot() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum, withRowIdentities: true);
        using LinearTriadModel dual = Unwrap(primal.Dual());

        Assert.NotNull(dual.PrimalRowIdentities);
        Assert.Equal(primal.Constraints.Ids, dual.PrimalRowIdentities!);
        Assert.NotEmpty(dual.PrimalRowIdentities!);
    }

    /// <summary>
    /// **对偶路径的表用原模型的身份做键，而不是本模型的行身份** /
    /// The dual path keys the table by the primal's identities, not its own rows.
    /// </summary>
    /// <remarks>
    /// **这一条是 B1224 那条"未测"的<b>正题</b>：** **对偶模型自己的 `Constraints.Ids` 与
    /// `PrimalRowIdentities` 是两套不同的东西，而表必须用后者。**
    ///
    /// **如果实现退回读 `Constraints.Ids`，键就会变成对偶模型自己的行身份，这条会红。**
    ///
    /// **This is the <b>point</b> of B1224's "untested": the dual model's own `Constraints.Ids` and its
    /// `PrimalRowIdentities` are two different sets, and the table must use the latter.**
    ///
    /// **If the implementation fell back to `Constraints.Ids`, the keys would become the dual model's own row
    /// identities and this goes red.**
    /// </remarks>
    [Fact]
    public void TidyDualSolutionByIdentity_OnDualPath_ShouldKeyByPrimalIdentities() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum, withRowIdentities: true);
        using LinearTriadModel dual = Unwrap(primal.Dual());

        double[] values = Enumerable.Repeat(3.0, dual.Variables.Count).ToArray();
        IReadOnlyDictionary<ConstraintId, Flt64> table =
            Unwrap(dual.TidyDualSolutionByIdentity(values.Select(value => new Flt64(value)).ToArray()));

        Assert.Equal(primal.Constraints.Ids.Count, table.Count);
        Assert.All(primal.Constraints.Ids, id => Assert.True(
            table.ContainsKey(id), $"primal row identity {id.Value} must be a key"));
        Assert.Equal(3.0, table[primal.Constraints.Ids[0]].ToDouble());
    }

    /// <summary>
    /// **普通模型没有那张快照** / An ordinary model has no such snapshot.
    /// </summary>
    /// <remarks>
    /// **快照的存在与否就是"这是不是对偶模型"的判据，而 `TidyDualSolutionByIdentity` 正是靠它分两条路。**
    /// **如果普通模型也带着一张从自己抄来的快照，那个分支就永远走不到第二条，而行对齐路径会静默地少一半。**
    ///
    /// **The presence of the snapshot is the "is this a dual model" test, and `TidyDualSolutionByIdentity` branches on
    /// it.** **If an ordinary model also carried a self-copied snapshot, the second branch would be unreachable and the
    /// row-aligned path would silently lose half its cases.**
    /// </remarks>
    [Fact]
    public void AnOrdinaryModel_ShouldNotCarryARowIdentitySnapshot() {
        using LinearTriadModel primal = Primal(ObjectCategory.Minimum);

        Assert.Null(primal.PrimalRowIdentities);
    }

    private static void AssertRowDual(ModelViewVariable variable, string name, double lower, double upper) {
        Assert.Equal(name, variable.Name);
        Assert.Equal(lower, variable.LowerBound.ToDouble());
        Assert.Equal(upper, variable.UpperBound.ToDouble());
        Assert.Null(variable.Origin);
        Assert.Null(variable.Identity);
    }

    /// <summary>
    /// **Farkas 列断言：无来源，但<b>有</b>派生身份** / Farkas-column assertions: no origin, but <b>with</b> a derived identity.
    /// </summary>
    private static void AssertFarkasColumn(ModelViewVariable variable, string name, double lower, double upper) {
        Assert.Equal(name, variable.Name);
        Assert.Equal(lower, variable.LowerBound.ToDouble());
        Assert.Equal(upper, variable.UpperBound.ToDouble());
        Assert.Null(variable.Origin);
        Assert.NotNull(variable.Identity);
        Assert.Equal(ModelElementKind.Variable, variable.Identity!.Kind);
    }

    private static void AssertBalance(
        LinearTriadModel dual,
        int row,
        (int Col, double Coefficient)[] cells,
        ConstraintRelation relation,
        double rhs,
        string name) {
        Assert.Equal(
            cells,
            dual.Constraints.Lhs[row].Select(cell => (cell.ColIndex, cell.Coefficient.ToDouble())));
        Assert.All(dual.Constraints.Lhs[row], cell => Assert.Equal(row, cell.RowIndex));
        Assert.Equal(relation, dual.Constraints.Signs[row]);
        Assert.Equal(rhs, dual.Constraints.Rhs[row].ToDouble());
        Assert.Equal(name, dual.Constraints.Names[row]);
    }

    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        Assert.IsType<Ok<T, ErrorCode, Error<ErrorCode>>>(result).Value;

    /// <summary>返回预置结果的线性求解器替身 / Linear solver stub returning a canned result.</summary>
    private sealed class StubSolver(
        Func<ILinearTriadModelView, Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> solve)
        : ILinearSolver {
        public string Name => "stub";

        public SolverConfig Config { get; } = new();

        public ILinearTriadModelView? Seen { get; private set; }

        public int? SeenRowCount { get; private set; }

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            ILinearTriadModelView model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) {
            Seen = model;
            SeenRowCount = model.Constraints.Sources.Count;
            return Task.FromResult(solve(model));
        }

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            LinearTriadModel model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            IisConfig? iisConfig = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
            ILinearTriadModelView model,
            ulong solutionAmount,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");

        public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
            LinearMechanismModel<Flt64> model,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");

        public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
            LinearMetaModel<Flt64> model,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            MechanismModelDumpingStatusCallBack? dumpingStatusCallBack = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");
    }
}
