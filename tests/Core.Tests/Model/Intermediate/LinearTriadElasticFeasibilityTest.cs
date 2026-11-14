#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>线性三元模型的可行性与弹性构造测试 / Feasibility and elastic construction tests for the linear triad model.</summary>
public class LinearTriadElasticFeasibilityTest {
    private static readonly double Neg = double.NegativeInfinity;
    private static readonly double Pos = double.PositiveInfinity;
    private static readonly double Precision = NumericConstantsRegistry.For<Flt64>().DecimalPrecision!.Value.ToDouble();

    /// <summary>
    /// 可行性模型逐行核算：4 行覆盖小于等于、右端为负的小于等于、等于与大于等于；右端为负的行整体取反并反转关系，
    /// 全部改写为等式且右端取绝对值，只有人工列进入目标。
    /// Row-by-row feasibility check: the four rows cover less-equal, a less-equal with a negative right-hand side, equal
    /// and greater-equal; negative right-hand sides are negated and reversed, every row becomes an equality with an
    /// absolute right-hand side, and only artificial columns are priced.
    /// </summary>
    [Fact]
    public void Feasibility_ShouldRewriteRowsIntoEqualitiesWithSlackAndArtifactColumns() {
        using LinearTriadModel primal = Primal();
        using LinearTriadModel feasibility = Unwrap(primal.Feasibility());

        Assert.Equal("primal-feasibility", feasibility.Name);
        Assert.Equal(12, feasibility.Variables.Count);
        Assert.Equal(4, feasibility.Constraints.Size);
        Assert.Equal(Enumerable.Range(0, 12), feasibility.Variables.Select(variable => variable.Index));
        Assert.All(feasibility.Constraints.Signs, sign => Assert.Equal(ConstraintRelation.Equal, sign));
        Assert.All(feasibility.Constraints.Sources, source => Assert.Equal(ConstraintSource.Feasibility, source));
        Assert.All(feasibility.Constraints.Names, name => Assert.EndsWith("_feasibility", name));

        // 原变量的边界与类型必须原样保留，只有派生列是 [0, +inf) 的连续列。
        // Original bounds and types stay untouched; only derived columns are continuous [0, +inf).
        AssertDomain(feasibility.Variables[0], "x0", Neg, Pos);
        AssertDomain(feasibility.Variables[1], "x1", 0, Pos);
        AssertDomain(feasibility.Variables[2], "x2", Neg, 0);
        AssertDomain(feasibility.Variables[3], "x3", 2, Pos);
        AssertDomain(feasibility.Variables[4], "x4", Neg, 4);
        AssertDomain(feasibility.Variables[5], "x5", -5, 7);
        AssertDomain(feasibility.Variables[6], "c0_slack", 0, Pos);
        AssertDomain(feasibility.Variables[7], "cons1_slack", 0, Pos);
        AssertDomain(feasibility.Variables[8], "cons1_artifact", 0, Pos);
        AssertDomain(feasibility.Variables[9], "c2_artifact", 0, Pos);
        AssertDomain(feasibility.Variables[10], "c3_slack", 0, Pos);
        AssertDomain(feasibility.Variables[11], "c3_artifact", 0, Pos);
        Assert.All(feasibility.Variables.Skip(6), variable => Assert.Equal(Flt64.Zero, variable.InitialResult));
        Assert.All(feasibility.Variables.Skip(6), variable => Assert.True(variable.Type.IsContinuousType));
        // 松弛列记录来源行的原始索引与原始关系符号，人工列不携带松弛元数据。
        // Slacks record the source row's original index and relation, while artificials carry no slack metadata.
        Assert.Equal(new ConstraintSlackOrigin(0, ConstraintRelation.LessEqual), feasibility.Variables[6].Slack!.Constraint);
        Assert.Equal(new ConstraintSlackOrigin(1, ConstraintRelation.LessEqual), feasibility.Variables[7].Slack!.Constraint);
        Assert.Equal(new ConstraintSlackOrigin(3, ConstraintRelation.GreaterEqual), feasibility.Variables[10].Slack!.Constraint);
        Assert.Null(feasibility.Variables[8].Slack);
        Assert.Null(feasibility.Variables[9].Slack);
        Assert.Null(feasibility.Variables[11].Slack);
        Assert.Same(primal.Variables[5], feasibility.Variables[5]);

        AssertRow(feasibility, row: 0, relation: ConstraintRelation.Equal, rhs: 4, name: "c0_feasibility", cells: new[] { (0, 1d), (1, 2d), (6, 1d) });
        // 右端为 -3 的小于等于行取反后成为大于等于行，因此同时获得松弛列与人工列。
        // Negating the less-equal row with right-hand side -3 reverses it to greater-equal, so it gains a slack and an artificial.
        AssertRow(feasibility, row: 1, relation: ConstraintRelation.Equal, rhs: 3, name: "cons1_feasibility", cells: new[] { (2, -1d), (3, 1d), (7, -1d), (8, 1d) });
        AssertRow(feasibility, row: 2, relation: ConstraintRelation.Equal, rhs: 0, name: "c2_feasibility", cells: new[] { (4, 1d), (5, 1d), (9, 1d) });
        AssertRow(feasibility, row: 3, relation: ConstraintRelation.Equal, rhs: 5, name: "c3_feasibility", cells: new[] { (1, 1d), (5, 2d), (10, -1d), (11, 1d) });

        // 小于等于行的松弛不计价：它只吸收左端低于右端的余量。
        // The slack of a less-equal row stays unpriced: it only absorbs the leftover below the right-hand side.
        Assert.Equal(ObjectCategory.Minimum, feasibility.Objective.Category);
        Assert.Equal(Flt64.Zero, feasibility.Objective.Constant);
        Assert.Equal(
            new[] { (8, 1d), (9, 1d), (11, 1d) },
            feasibility.Objective.Cells.Select(cell => (cell.ColIndex, cell.Coefficient.ToDouble())));
    }

    /// <summary>
    /// 单列矛盾 y = 3、y &lt;= 2 的可行性模型：等式行只由人工列计价，可行解 y = 2、人工量 = 1 恰好度量出 1 的违反量。
    /// Feasibility of the contradicting pair y = 3 and y &lt;= 2: only the equality row is priced, and the feasible point
    /// y = 2 with artificial 1 measures exactly the violation of 1.
    /// </summary>
    [Fact]
    public void Feasibility_ShouldPriceExactlyTheViolationOfAnEqualityRow() {
        using LinearTriadModel primal = LeadingEquality();
        using LinearTriadModel feasibility = Unwrap(primal.Feasibility());

        Assert.Equal(3, feasibility.Variables.Count);
        Assert.Equal(2, feasibility.Constraints.Size);
        AssertDomain(feasibility.Variables[1], "eq_artifact", 0, Pos);
        AssertDomain(feasibility.Variables[2], "cap_slack", 0, Pos);
        AssertRow(feasibility, row: 0, relation: ConstraintRelation.Equal, rhs: 3, name: "eq_feasibility", cells: new[] { (0, 1d), (1, 1d) });
        AssertRow(feasibility, row: 1, relation: ConstraintRelation.Equal, rhs: 2, name: "cap_feasibility", cells: new[] { (0, 1d), (2, 1d) });
        Assert.Equal(
            new[] { (1, 1d) },
            feasibility.Objective.Cells.Select(cell => (cell.ColIndex, cell.Coefficient.ToDouble())));
    }

    [Fact]
    public void Feasibility_ShouldRejectDisposedModelAndInvalidRows() {
        using LinearTriadModel infinite = Primal(infiniteRhs: true);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(infinite.Feasibility()).Code);
        using LinearTriadModel badCell = Primal(outOfRangeCell: true);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(badCell.Feasibility()).Code);
        using LinearTriadModel badShape = Primal(mismatchedMetadata: true);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(badShape.Feasibility()).Code);

        LinearTriadModel disposed = Primal();
        disposed.Dispose();
        Assert.Equal(
            ErrorCode.ApplicationStopped,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(disposed.Feasibility()).Code);
    }

    /// <summary>
    /// 默认弹性模型：每行按关系获得松弛列，每个有界且未归一化的变量获得界松弛行，目标只求全部松弛之和。
    /// Default elastic model: every row gains slacks by relation, every bounded non-normalized variable gains bound
    /// slack rows, and the objective prices the whole slack sum.
    /// </summary>
    [Fact]
    public void Elastic_ShouldAddRowAndBoundSlacksAndPriceThem() {
        using LinearTriadModel primal = Primal();
        using LinearTriadModel elastic = Unwrap(primal.Elastic());

        Assert.Equal("primal-elastic", elastic.Name);
        Assert.Equal(15, elastic.Variables.Count);
        Assert.Equal(8, elastic.Constraints.Size);
        Assert.Equal(Enumerable.Range(0, 15), elastic.Variables.Select(variable => variable.Index));
        // 关系符号与右端保持原值，弹性只体现在追加的松弛单元上。
        // Signs and right-hand sides keep their original values; elasticity shows only in the appended slack cells.
        Assert.Equal(
            new[] { ConstraintRelation.LessEqual, ConstraintRelation.LessEqual, ConstraintRelation.Equal, ConstraintRelation.GreaterEqual },
            elastic.Constraints.Signs.Take(4));
        Assert.Equal(
            new[] { 4d, -3d, 0d, 5d },
            elastic.Constraints.Rhs.Take(4).Select(value => value.ToDouble()));
        Assert.All(elastic.Constraints.Sources.Take(4), source => Assert.Equal(ConstraintSource.Elastic, source));
        AssertRow(elastic, row: 0, relation: ConstraintRelation.LessEqual, rhs: 4, name: "c0_elastic", cells: new[] { (0, 1d), (1, 2d), (6, -1d) });
        AssertRow(elastic, row: 1, relation: ConstraintRelation.LessEqual, rhs: -3, name: "cons1_elastic", cells: new[] { (2, 1d), (3, -1d), (7, -1d) });
        AssertRow(elastic, row: 2, relation: ConstraintRelation.Equal, rhs: 0, name: "c2_elastic", cells: new[] { (4, 1d), (5, 1d), (8, -1d), (9, 1d) });
        AssertRow(elastic, row: 3, relation: ConstraintRelation.GreaterEqual, rhs: 5, name: "c3_elastic", cells: new[] { (1, 1d), (5, 2d), (10, 1d) });

        AssertDomain(elastic.Variables[6], "c0_lb_slack", 0, Pos);
        AssertDomain(elastic.Variables[7], "cons1_lb_slack", 0, Pos);
        AssertDomain(elastic.Variables[8], "c2_lb_slack", 0, Pos);
        AssertDomain(elastic.Variables[9], "c2_ub_slack", 0, Pos);
        AssertDomain(elastic.Variables[10], "c3_ub_slack", 0, Pos);
        AssertDomain(elastic.Variables[11], "x3_lb_slack", 0, Pos);
        AssertDomain(elastic.Variables[12], "x4_ub_slack", 0, Pos);
        AssertDomain(elastic.Variables[13], "x5_lb_slack", 0, Pos);
        AssertDomain(elastic.Variables[14], "x5_ub_slack", 0, Pos);
        Assert.All(elastic.Variables.Skip(6), variable => Assert.True(variable.Type.IsContinuousType));
        Assert.All(elastic.Variables.Skip(6), variable => Assert.Equal(Flt64.Zero, variable.InitialResult));
        // 行松弛记录来源行的原始索引与原始关系符号，等式行的两条松弛因此按值相等。
        // Row slacks record the source row's original index and relation, so an equality row's two slacks are value-equal.
        Assert.Equal(new ConstraintSlackOrigin(0, ConstraintRelation.LessEqual), elastic.Variables[6].Slack!.Constraint);
        Assert.Equal(new ConstraintSlackOrigin(1, ConstraintRelation.LessEqual), elastic.Variables[7].Slack!.Constraint);
        Assert.Equal(new ConstraintSlackOrigin(2, ConstraintRelation.Equal), elastic.Variables[8].Slack!.Constraint);
        Assert.Equal(elastic.Variables[8].Slack!.Constraint, elastic.Variables[9].Slack!.Constraint);
        Assert.Equal(new ConstraintSlackOrigin(3, ConstraintRelation.GreaterEqual), elastic.Variables[10].Slack!.Constraint);
        // 界松弛元数据指回原变量，且不携带来源行。
        // Bound slack metadata points back at the original column and keeps no source row.
        VariableSlack lowerSlack = elastic.Variables[11].Slack!;
        Assert.Same(primal.Variables[3], lowerSlack.LowerBound);
        Assert.Null(lowerSlack.UpperBound);
        Assert.Null(lowerSlack.Constraint);
        VariableSlack upperSlack = elastic.Variables[12].Slack!;
        Assert.Same(primal.Variables[4], upperSlack.UpperBound);
        Assert.Null(upperSlack.LowerBound);
        Assert.Same(primal.Variables[5], elastic.Variables[13].Slack!.LowerBound);
        Assert.Same(primal.Variables[5], elastic.Variables[14].Slack!.UpperBound);

        AssertRow(elastic, row: 4, relation: ConstraintRelation.GreaterEqual, rhs: 2, name: "x3_lb_slack", cells: new[] { (3, 1d), (11, 1d) });
        AssertRow(elastic, row: 5, relation: ConstraintRelation.LessEqual, rhs: 4, name: "x4_ub_slack", cells: new[] { (4, 1d), (12, -1d) });
        AssertRow(elastic, row: 6, relation: ConstraintRelation.GreaterEqual, rhs: -5, name: "x5_lb_slack", cells: new[] { (5, 1d), (13, 1d) });
        AssertRow(elastic, row: 7, relation: ConstraintRelation.LessEqual, rhs: 7, name: "x5_ub_slack", cells: new[] { (5, 1d), (14, -1d) });
        Assert.Equal(
            new[] { ConstraintSource.ElasticLowerBound, ConstraintSource.ElasticUpperBound, ConstraintSource.ElasticLowerBound, ConstraintSource.ElasticUpperBound },
            elastic.Constraints.Sources.Skip(4));
        Assert.Equal(new[] { 3, 4, 5, 5 }, elastic.Constraints.Lhs.Skip(4).Select(row => row[0].ColIndex));

        // 自由与已归一化的列保留边界，其余列的边界放开并改由界松弛行表达。
        // Free and normalized columns keep their bounds; the others open to infinity and are expressed by bound rows.
        AssertDomain(elastic.Variables[0], "x0", Neg, Pos);
        AssertDomain(elastic.Variables[1], "x1", 0, Pos);
        AssertDomain(elastic.Variables[2], "x2", Neg, 0);
        AssertDomain(elastic.Variables[3], "x3", Neg, Pos);
        AssertDomain(elastic.Variables[4], "x4", Neg, Pos);
        AssertDomain(elastic.Variables[5], "x5", Neg, Pos);
        Assert.Same(primal.Variables[0], elastic.Variables[0]);
        Assert.Same(primal.Variables[2], elastic.Variables[2]);
        Assert.Equal(ObjectCategory.Minimum, elastic.Objective.Category);
        Assert.Equal(Flt64.Zero, elastic.Objective.Constant);
        // 原目标的 5 个单元格全部丢弃，目标只剩松弛列之和。
        // All five original objective cells are dropped; the objective keeps only the slack sum.
        Assert.Equal(
            Enumerable.Range(6, 9).Select(index => (index, 1d)),
            elastic.Objective.Cells.Select(cell => (cell.ColIndex, cell.Coefficient.ToDouble())));
    }

    /// <summary>二值与最小最大松弛的行块顺序、列索引与目标必须由小到大核对。 / Row blocks, column indexes and the objective must stay ascending for binary and min-max options.</summary>
    [Theory]
    [InlineData(false, false, 15, 8)]
    [InlineData(true, false, 16, 17)]
    [InlineData(false, true, 24, 27)]
    [InlineData(true, true, 25, 36)]
    public void Elastic_ShouldCoverEveryOptionCombination(
        bool minmaxSlack,
        bool useMinSlackAmount,
        int expectedVariables,
        int expectedRows) {
        using LinearTriadModel primal = Primal();
        (ulong Count, Flt64 Threshold)? minSlackAmount = useMinSlackAmount ? (2UL, new Flt64(0.5)) : null;
        using LinearTriadModel elastic = Unwrap(primal.Elastic(
            minmaxSlack: minmaxSlack,
            minSlackAmount: minSlackAmount));

        Assert.Equal(expectedVariables, elastic.Variables.Count);
        Assert.Equal(expectedRows, elastic.Constraints.Size);
        Assert.Equal(Enumerable.Range(0, expectedVariables), elastic.Variables.Select(variable => variable.Index));
        Assert.True(elastic.Constraints.ValidateShape().IsOk);
        Assert.All(elastic.Constraints.Lhs.SelectMany(row => row), cell => Assert.True(cell.ColIndex >= 0 && cell.ColIndex < expectedVariables));
        Assert.All(elastic.Objective.Cells, cell => Assert.Equal(1d, cell.Coefficient.ToDouble()));
        Assert.All(elastic.Objective.Cells, cell => Assert.True(cell.ColIndex >= 6));
    }

    /// <summary>
    /// 最小最大松弛：每个松弛配一行 minmax - s &gt;= 0，minmax 列最后追加并计入目标。
    /// Min-max slack: every slack gains a minmax - s &gt;= 0 row, and the minmax column comes last and is priced.
    /// </summary>
    [Fact]
    public void Elastic_ShouldAddMinmaxColumnAndOneRowPerSlack() {
        using LinearTriadModel primal = Primal();
        using LinearTriadModel elastic = Unwrap(primal.Elastic(minmaxSlack: true));

        Assert.Equal(16, elastic.Variables.Count);
        Assert.Equal(17, elastic.Constraints.Size);
        AssertDomain(elastic.Variables[15], "minmax_slack", 0, Pos);
        Assert.Null(elastic.Variables[15].Slack);
        Assert.Equal(
            new[] {
                "c0_lb_slack_minmax", "cons1_lb_slack_minmax", "c2_lb_slack_minmax", "c2_ub_slack_minmax",
                "c3_ub_slack_minmax", "x3_lb_slack_minmax", "x4_ub_slack_minmax", "x5_lb_slack_minmax",
                "x5_ub_slack_minmax",
            },
            elastic.Constraints.Names.Skip(8));
        Assert.All(elastic.Constraints.Sources.Skip(8), source => Assert.Equal(ConstraintSource.ElasticSlackMinmax, source));
        Assert.All(elastic.Constraints.Signs.Skip(8), sign => Assert.Equal(ConstraintRelation.GreaterEqual, sign));
        Assert.All(elastic.Constraints.Rhs.Skip(8), value => Assert.Equal(0d, value.ToDouble()));
        AssertRow(elastic, row: 8, relation: ConstraintRelation.GreaterEqual, rhs: 0, name: "c0_lb_slack_minmax", cells: new[] { (15, 1d), (6, -1d) });
        AssertRow(elastic, row: 16, relation: ConstraintRelation.GreaterEqual, rhs: 0, name: "x5_ub_slack_minmax", cells: new[] { (15, 1d), (14, -1d) });
        Assert.Equal(
            Enumerable.Range(6, 10).Select(index => (index, 1d)),
            elastic.Objective.Cells.Select(cell => (cell.ColIndex, cell.Coefficient.ToDouble())));
    }

    /// <summary>
    /// 二值指示：每个松弛配 <c>s - (threshold + precision) * bin &gt;= 0</c> 与 <c>s - bin / precision &lt;= 0</c> 两行，
    /// 再跟一条 <c>sum(bin) &gt;= count</c> 总计行，二值列不进目标。
    /// Binary indicators: each slack gains <c>s - (threshold + precision) * bin &gt;= 0</c> and
    /// <c>s - bin / precision &lt;= 0</c>, followed by one <c>sum(bin) &gt;= count</c> total row, and binaries stay unpriced.
    /// </summary>
    [Fact]
    public void Elastic_ShouldAddBinaryIndicatorsAndTotalCountRow() {
        using LinearTriadModel primal = Primal();
        using LinearTriadModel elastic = Unwrap(primal.Elastic(
            minmaxSlack: false,
            minSlackAmount: (2UL, new Flt64(0.5))));

        Assert.Equal(24, elastic.Variables.Count);
        Assert.Equal(27, elastic.Constraints.Size);
        Assert.Equal(
            new[] { "c0_lb_slack_bin", "cons1_lb_slack_bin", "c2_ub_slack_bin", "x5_ub_slack_bin" },
            new[] { elastic.Variables[15].Name, elastic.Variables[16].Name, elastic.Variables[18].Name, elastic.Variables[23].Name });
        foreach (ModelViewVariable binary in elastic.Variables.Skip(15)) {
            Assert.True(binary.Type.IsBinaryType);
            Assert.False(binary.Type.IsContinuousType);
            Assert.Equal(0d, binary.LowerBound.ToDouble());
            Assert.Equal(1d, binary.UpperBound.ToDouble());
            Assert.Equal(Flt64.Zero, binary.InitialResult);
            Assert.Null(binary.Slack);
            Assert.EndsWith("_bin", binary.Name);
        }
        // 二值行按松弛顺序成对出现，总计行紧随其后。
        // Binary rows come in pairs following the slack order, with the total row right after them.
        AssertRow(elastic, row: 8, relation: ConstraintRelation.GreaterEqual, rhs: 0, name: "c0_lb_slack_bin_lb", cells: new[] { (6, 1d), (15, -(0.5 + Precision)) });
        AssertRow(elastic, row: 9, relation: ConstraintRelation.LessEqual, rhs: 0, name: "c0_lb_slack_bin_ub", cells: new[] { (6, 1d), (15, -(1 / Precision)) });
        AssertRow(elastic, row: 24, relation: ConstraintRelation.GreaterEqual, rhs: 0, name: "x5_ub_slack_bin_lb", cells: new[] { (14, 1d), (23, -(0.5 + Precision)) });
        AssertRow(elastic, row: 25, relation: ConstraintRelation.LessEqual, rhs: 0, name: "x5_ub_slack_bin_ub", cells: new[] { (14, 1d), (23, -(1 / Precision)) });
        AssertRow(
            elastic,
            row: 26,
            relation: ConstraintRelation.GreaterEqual,
            rhs: 2,
            name: "min_slack_amount",
            cells: Enumerable.Range(15, 9).Select(index => (index, 1d)).ToArray());
        Assert.All(elastic.Constraints.Sources.Skip(8), source => Assert.Equal(ConstraintSource.ElasticSlackBinary, source));
        Assert.Equal(
            Enumerable.Range(6, 9).Select(index => (index, 1d)),
            elastic.Objective.Cells.Select(cell => (cell.ColIndex, cell.Coefficient.ToDouble())));
    }

    /// <summary>二值与最小最大同时启用时，行块顺序是原行、界行、二值行加总计、minmax 行。 / With both options the blocks stay original rows, bound rows, binaries plus total, then minmax rows.</summary>
    [Fact]
    public void Elastic_ShouldKeepRowBlockOrderWhenBothOptionsAreEnabled() {
        using LinearTriadModel primal = Primal();
        using LinearTriadModel elastic = Unwrap(primal.Elastic(
            minmaxSlack: true,
            minSlackAmount: (1UL, new Flt64(0.25))));

        // 4 原行 + 4 界行 + 18 二值行 + 1 总计行 + 9 minmax 行；minmax 列排在全部二值列之后。
        // Four original, four bound, eighteen binary, one total and nine minmax rows; the minmax column follows every binary.
        Assert.Equal(36, elastic.Constraints.Size);
        Assert.Equal(25, elastic.Variables.Count);
        Assert.Equal("minmax_slack", elastic.Variables[24].Name);
        Assert.Equal(24, elastic.Variables[24].Index);
        Assert.Equal("x5_ub_slack_bin_lb", elastic.Constraints.Names[24]);
        Assert.Equal("x5_ub_slack_bin_ub", elastic.Constraints.Names[25]);
        Assert.Equal("min_slack_amount", elastic.Constraints.Names[26]);
        Assert.Equal("c0_lb_slack_minmax", elastic.Constraints.Names[27]);
        Assert.Equal("x5_ub_slack_minmax", elastic.Constraints.Names[35]);
        // minmax 列索引大于全部二值列，因此目标按松弛、minmax 的顺序计价。
        // The minmax index is above every binary index, so the objective prices the slacks and then the minmax column.
        Assert.Equal(
            Enumerable.Range(6, 9).Append(24).Select(index => (index, 1d)),
            elastic.Objective.Cells.Select(cell => (cell.ColIndex, cell.Coefficient.ToDouble())));
        AssertRow(
            elastic,
            row: 26,
            relation: ConstraintRelation.GreaterEqual,
            rhs: 1,
            name: "min_slack_amount",
            cells: Enumerable.Range(15, 9).Select(index => (index, 1d)).ToArray());
        AssertRow(elastic, row: 27, relation: ConstraintRelation.GreaterEqual, rhs: 0, name: "c0_lb_slack_minmax", cells: new[] { (24, 1d), (6, -1d) });
    }

    [Fact]
    public void Elastic_ShouldRejectDisposedModelAndInvalidRows() {
        using LinearTriadModel infinite = Primal(infiniteRhs: true);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(infinite.Elastic()).Code);
        using LinearTriadModel badCell = Primal(outOfRangeCell: true);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(badCell.Elastic(
                minmaxSlack: true,
                minSlackAmount: (1UL, Flt64.One))).Code);
        using LinearTriadModel nanBound = Primal(nanLowerBound: true);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(nanBound.Elastic()).Code);
        using LinearTriadModel badShape = Primal(mismatchedMetadata: true);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(badShape.Elastic()).Code);

        LinearTriadModel disposed = Primal();
        disposed.Dispose();
        Assert.Equal(
            ErrorCode.ApplicationStopped,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(disposed.Elastic()).Code);
    }

    /// <summary>
    /// 6 列 4 行的弹性与可行性夹具，覆盖全部约束关系与变量域分支。 / Six-column four-row fixture covering every relation and variable-domain branch.
    /// </summary>
    private static LinearTriadModel Primal(
        bool infiniteRhs = false,
        bool outOfRangeCell = false,
        bool mismatchedMetadata = false,
        bool nanLowerBound = false) {
        ModelViewVariable[] variables = [
            Column(0, "x0", Neg, Pos),
            Column(1, "x1", 0, Pos),
            Column(2, "x2", Neg, 0),
            Column(3, "x3", 2, Pos),
            Column(4, "x4", nanLowerBound ? double.NaN : Neg, 4),
            Column(5, "x5", -5, 7),
        ];
        SparseVector[] rows = [
            new([Cell(0, 1), Cell(1, 2)]),
            new([Cell(2, 1), Cell(3, -1)]),
            new([Cell(4, 1), Cell(5, 1)]),
            outOfRangeCell ? new([Cell(1, 1), Cell(9, 2)]) : new([Cell(1, 1), Cell(5, 2)]),
        ];
        var names = new List<string> { "c0", "", "c2", "c3" };
        if (mismatchedMetadata) {
            names.RemoveAt(3);
        }
        return new LinearTriadModel(
            variables: variables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(rows),
                signs: [
                    ConstraintRelation.LessEqual,
                    ConstraintRelation.LessEqual,
                    ConstraintRelation.Equal,
                    ConstraintRelation.GreaterEqual,
                ],
                rhs: [
                    new Flt64(4),
                    new Flt64(-3),
                    Flt64.Zero,
                    infiniteRhs ? new Flt64(Pos) : new Flt64(5),
                ],
                names: names,
                sources: [ConstraintSource.Origin, ConstraintSource.Origin, ConstraintSource.Origin, ConstraintSource.Origin]),
            objective: new Objective<LinearObjectiveCell>(
                category: ObjectCategory.Minimum,
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

    /// <summary>首行为等式的矛盾模型：y = 3 且 y &lt;= 2。 / Contradicting model whose first row is an equality: y = 3 and y &lt;= 2.</summary>
    private static LinearTriadModel LeadingEquality() => new(
        variables: [Column(0, "y", Neg, Pos)],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([
                new SparseVector([Cell(0, 1)]),
                new SparseVector([Cell(0, 1)]),
            ]),
            signs: [ConstraintRelation.Equal, ConstraintRelation.LessEqual],
            rhs: [new Flt64(3), new Flt64(2)],
            names: ["eq", "cap"],
            sources: [ConstraintSource.Origin, ConstraintSource.Origin]),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: [new LinearObjectiveCell(0, Flt64.One)]),
        name: "primal");

    private static ModelViewVariable Column(
        int index,
        string name,
        double lower,
        double upper) => new(
        index: index,
        lowerBound: new Flt64(lower),
        upperBound: new Flt64(upper),
        type: Continuous.Instance,
        name: name);

    private static SparseVectorEntry Cell(int col, double value) => new(col, new Flt64(value));

    private static void AssertDomain(
        ModelViewVariable variable,
        string name,
        double lower,
        double upper) {
        Assert.Equal(name, variable.Name);
        Assert.Equal(lower, variable.LowerBound.ToDouble());
        Assert.Equal(upper, variable.UpperBound.ToDouble());
        Assert.Null(variable.Origin);
        Assert.Null(variable.Identity);
    }

    private static void AssertRow(
        LinearTriadModel model,
        int row,
        ConstraintRelation relation,
        double rhs,
        string name,
        (int Col, double Coefficient)[] cells) {
        Assert.Equal(cells, model.Constraints.Lhs[row].Select(cell => (cell.ColIndex, cell.Coefficient.ToDouble())));
        Assert.All(model.Constraints.Lhs[row], cell => Assert.Equal(row, cell.RowIndex));
        Assert.Equal(relation, model.Constraints.Signs[row]);
        Assert.Equal(rhs, model.Constraints.Rhs[row].ToDouble());
        Assert.Equal(name, model.Constraints.Names[row]);
    }

    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        Assert.IsType<Ok<T, ErrorCode, Error<ErrorCode>>>(result).Value;
}
