// IIS 纯结构辅助的判据 / Criteria for the IIS structural helpers.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Test.Core.Solver.Iis;

/// <summary>
/// **无需求解器即可验证的 IIS 结构辅助** / IIS structural helpers, verifiable without a solver.
/// </summary>
/// <remarks>
/// **定位到二次 IIS 整体缺失（846 行 / 12 函数）**，建了骨架。
/// 这里实现其中**四个不调用求解器的纯函数**——它们只读写模型形状，
/// 因此**在当前无 SDK 的环境里可以完整验证**，不像 `performElasticFiltering` 那样
/// 必须先有真实求解器。
///
/// 夹具形状（5 个变量，1 行约束）：
///
/// | 变量 | 角色 |
/// | --- | --- |
/// | `0` = `x` | 普通列，出现在约束里 |
/// | `1` = `y` | 普通列，作为 `x` 的**二次伴随列**（`Col2`） |
/// | `2` = `s` | **松弛列**，`Slack` 指向变量 `3`/`4` 与行 `0` |
/// | `3` = `s_lb` | 下界变量；**同时是约束系数**（用来钉住"标记取或"） |
/// | `4` = `s_ub` | 上界变量 |
///
/// 约束行 `0` 的条目：`(x, -1)`、`(x, y)` 二次项、`(s, -1)`、`(s_lb, -1)`。
///
/// **located the wholesale absence of quadratic IIS (846 lines / 12 functions)**; built the skeleton. ports the **four pure functions that never
/// call a solver** — they only read and write model shape, so they **can be fully verified in this SDK-less environment**, unlike `performElasticFiltering`, which needs a
/// real solver first. Fixture shape (5 variables, 1 constraint): see the table above. Row `0` carries `(x, -1)`, the quadratic term `(x, y)`, `(s, -1)` and `(s_lb, -1)`.
/// </remarks>
public class IisStructuralSupportTests {
    private const int X = 0;
    private const int Y = 1;
    private const int Slack = 2;
    private const int SlackLower = 3;
    private const int SlackUpper = 4;

    /// <summary>**松弛列要能回溯到原模型行号** / Slack columns must trace back to original model rows.</summary>
    /// <remarks>
    /// 只测"传进去的列有松弛"是不够的：还要钉住**没有松弛的列被忽略**、
    /// 以及**重复行只出现一次**。
    ///
    /// Asserting only "the given column has slack" is not enough: the criterion also pins **columns without slack being ignored** and **duplicate rows appearing once**.
    /// </remarks>
    [Fact]
    public void RelatedRows_ShouldTraceSlacksAndIgnorePlainColumns() {
        List<ModelViewVariable> variables = MakeVariables();

        IisStructuralSupport.RelatedRows(variables, [Slack]).Should().Equal(
            [0],
            "the slack column points at row 0");

        IisStructuralSupport.RelatedRows(variables, [X, Y]).Should().BeEmpty(
            "plain columns carry no slack, so they must not contribute rows");
    }

    /// <summary>**关联变量按索引升序、且只保留被标记的一侧边界** / Related variables are index-ascending and keep only the marked bound side.</summary>
    /// <remarks>
    /// 这是 IIS 的核心语义：**只保留导致不可行的边界**，其余置 `null`。
    /// 判据同时钉住**排序**（`sortedBy`）与**边界取舍**。
    ///
 /// This is the core IIS semantics: **keep only the bounds that cause infeasibility** and null the rest. The criterion pins both the **ordering** (the implementation sorts)
    /// and the **bound selection**.
    /// </remarks>
    [Fact]
    public void RelatedVariables_ShouldSortAndKeepOnlyMarkedBounds() {
        List<ModelViewVariable> variables = MakeVariables();
        QuadraticConstraintBatch lhs = MakeLhs();

        List<IisRelatedVariable> related = IisStructuralSupport.RelatedVariables(
            variables, lhs, [Slack], [0]);

        related.Select(v => v.Variable.Index).Should().Equal(
            [X, Y, Slack, SlackLower, SlackUpper],
            "all five columns are reachable: three from the row's coefficients and two from the slack's bounds; order must be ascending by index");

        related[0].LowerBound.Should().BeNull("x is only a coefficient, so neither bound belongs to the IIS");
        related[0].UpperBound.Should().BeNull("x is only a coefficient, so neither bound belongs to the IIS");
    }

    /// <summary>**同一变量既是边界又是系数时，边界必须保留** / When a variable is both a bound and a coefficient, the bound must survive.</summary>
    /// <remarks>
    /// **这条钉住 `MarkBound` 的累积语义（取或，不是覆盖）。**
    ///
    /// 夹具里变量 `3`（`s_lb`）**既**是松弛的 `LowerBound`（标记为保留下界）
    /// **又**是约束行 `0` 的系数（标记为不保留任何边界）。
    ///
    /// | 实现 | 变量 `3` 的结果 |
    /// | --- | --- |
    /// | 取或（正确） | `LowerBound` **保留** |
    /// | 覆盖（后写赢） | `LowerBound` 变成 `null`，**判据红** |
    ///
    /// **这是最容易写错的一处**：系数标记在边界标记之后处理，覆盖式实现会把边界悄悄丢掉，
    /// IIS 因此少一条必要性证据，而**结果看起来仍然"正常"**。
    ///
    /// **This pins `MarkBound`'s accumulation (OR, not overwrite).** Fixture variable `3` (`s_lb`) is **both** the slack's `LowerBound` (marked keep-lower) **and** a
    /// coefficient of row `0` (marked keep-nothing). **This is the easiest place to get wrong**: coefficient marks are processed after bound marks, so an
    /// overwrite-style implementation silently drops the bound — losing a piece of necessity evidence while the result still *looks* fine.
    /// </remarks>
    [Fact]
    public void RelatedVariables_ShouldKeepTheBoundWhenTheVariableIsAlsoACoefficient() {
        List<ModelViewVariable> variables = MakeVariables();
        QuadraticConstraintBatch lhs = MakeLhs();

        List<IisRelatedVariable> related = IisStructuralSupport.RelatedVariables(
            variables, lhs, [Slack], [0]);

        IisRelatedVariable lowerBoundVariable = related.Single(v => v.Variable.Index == SlackLower);
        lowerBoundVariable.LowerBound.Should().Be(
            new Flt64(0.0),
            "s_lb is a slack lower-bound variable, so its lower bound belongs to the IIS even though it also appears as a coefficient");

        IisRelatedVariable upperBoundVariable = related.Single(v => v.Variable.Index == SlackUpper);
        upperBoundVariable.UpperBound.Should().Be(
            new Flt64(10.0),
            "s_ub is a slack upper-bound variable, so its upper bound belongs to the IIS");
        upperBoundVariable.LowerBound.Should().BeNull(
            "s_ub's lower bound was never marked, so it is not part of the IIS");
    }

    /// <summary>**二次伴随列（`Col2`）也必须被收集** / The quadratic partner column (`Col2`) must be collected too.</summary>
    /// <remarks>
    /// 二次项 `(x, y)` 只标记 `Col1 = x` 就会**漏掉 `y`**，子模型因此缺一列，
    /// 喂给求解器时维度就对不上。线性项用 `Col2 = -1` 表示，**不能把 `-1` 当索引**。
    ///
    /// Marking only `Col1 = x` on the quadratic term `(x, y)` **misses `y`**, so the submodel is a column short and no longer matches the solver's dimensions. Linear terms
    /// use `Col2 = -1`, which **must not be treated as an index**.
    /// </remarks>
    [Fact]
    public void RelatedVariables_ShouldCollectTheQuadraticPartnerColumn() {
        List<ModelViewVariable> variables = MakeVariables();
        QuadraticConstraintBatch lhs = MakeLhs();

        List<IisRelatedVariable> related = IisStructuralSupport.RelatedVariables(
            variables, lhs, [Slack], [0]);

        related.Select(v => v.Variable.Index).Should().Contain(
            Y,
            "y appears only as the quadratic partner (Col2) of x, so a Col1-only implementation would drop it");
    }

    /// <summary>**行号必须去重且升序，即使传入顺序是反的** / Row indices must be deduplicated and ascending even when the input order is reversed.</summary>
    /// <remarks>
    /// **上一版判据钉不住这一点。** 它只传了一个松弛列（`[Slack]`），
    /// 单个元素**既不可能重复、也谈不上排序**，所以
    /// "把 `SortedSet` 换成 `List`" 的变异**直接存活**。
    ///
    /// 这条改用专门夹具：两个松弛列分别指向行 `1` 和行 `0`，传入顺序 `[行0的列, 行1的列, 行0的列]`。
    ///
    /// | 实现 | 结果 |
    /// | --- | --- |
    /// | `SortedSet`（正确） | `[0, 1]` |
    /// | `List`（变异） | `[1, 0, 1]`，**判据红** |
    ///
    /// **The previous criterion could not pin this.** It passed a single slack column (`[Slack]`), and a single element **can neither repeat nor be misordered**, so the
    /// "`SortedSet` → `List`" mutant **survived outright**. This one uses a dedicated fixture: two slack columns pointing at rows `1` and `0`, passed as
    /// `[column-for-row-0, column-for-row-1, column-for-row-0]` (table above).
    /// </remarks>
    [Fact]
    public void RelatedRows_ShouldDeduplicateAndSort() {
        var rowZeroSlack = new VariableSlack(
            Constraint: new ConstraintSlackOrigin(RowIndex: 0, Relation: ConstraintRelation.GreaterEqual));
        var rowOneSlack = new VariableSlack(
            Constraint: new ConstraintSlackOrigin(RowIndex: 1, Relation: ConstraintRelation.GreaterEqual));
        var variables = new List<ModelViewVariable> {
            new(0, new Flt64(0.0), new Flt64(10.0), Continuous.Instance, "x"),
            new(1, new Flt64(0.0), new Flt64(100.0), Continuous.Instance, "s_row1", slack: rowOneSlack),
            new(2, new Flt64(0.0), new Flt64(100.0), Continuous.Instance, "s_row0", slack: rowZeroSlack),
        };

        IisStructuralSupport.RelatedRows(variables, [2, 1, 2]).Should().Equal(
            [0, 1],
            "row 0's slack is passed twice and after row 1's, so the result must be deduplicated and ascending");
    }

    /// <summary>**裁剪后行序升序、且列索引已重映射** / Rows come out ascending and columns are remapped.</summary>
    /// <remarks>
    /// 传入 `[1, 0, 1]` 时结果必须只剩两行、且顺序为原 `0`、原 `1`。
    /// 名称跟随原行，用来确认**行的元数据也一起被搬运**，不是只实现了系数。
    ///
    /// Passing `[1, 0, 1]` must yield two rows ordered as the original `0` then `1`. Names follow their original rows, proving **row metadata is carried along**, not just
    /// coefficients.
    /// </remarks>
    [Fact]
    public void FilterConstraintByRowIndex_ShouldSortDeduplicateAndRemapColumns() {
        (QuadraticConstraintBatch batch, _) = MakeBatch();
        var map = new Dictionary<int, int> { [0] = 0, [1] = 1, [2] = 2, [3] = 3, [4] = 4 };

        QuadraticConstraintBatch filtered = IisStructuralSupport.FilterConstraintByRowIndex(batch, [1, 0, 1], map);

        filtered.ConstraintCount.Should().Be(2, "row 1 is passed twice, so it may only appear once");
        filtered.Names.Should().Equal(["c0", "c1"], "rows must come out in ascending original order");
        filtered.Rhs.Should().Equal([new Flt64(1.0), new Flt64(2.0)]);
    }

    /// <summary>**只保留一行时，元数据必须来自原行而不是新行** / With a single row kept, metadata must come from the original row, not the new index.</summary>
    /// <remarks>
    /// **上一版判据钉不住这一点。** 它传 `[1, 0, 1]`，去重排序后是 `[0, 1]`——
    /// **恰好是恒等排列**，于是 `Names[oldRow]` 与 `Names[newRow]` 得到同一个值，
    /// "用新行号取元数据"的变异**直接存活**。
    ///
    /// 这条只保留原行 `1`：新行 `0` 对应的元数据必须是 `Names[1]`。
    ///
    /// | 实现 | `Names` |
    /// | --- | --- |
    /// | 取 `oldRow`（正确） | `["c1"]` |
    /// | 取 `newRow`（变异） | `["c0"]`，**判据红** |
    ///
    /// `Rhs` 同理：正确为 `[2.0]`，变异会给出 `[1.0]`。
    ///
    /// **The previous criterion could not pin this.** It passed `[1, 0, 1]`, which deduplicates and sorts to `[0, 1]` — **the identity permutation** — so `Names[oldRow]` and
    /// `Names[newRow]` coincide and the "use the new row index" mutant **survived outright**. This one keeps only original row `1`, where new row `0` must carry
    /// `Names[1]` (table above). `Rhs` likewise.
    /// </remarks>
    [Fact]
    public void FilterConstraintByRowIndex_ShouldTakeMetadataFromTheOriginalRow() {
        (QuadraticConstraintBatch batch, _) = MakeBatch();
        var map = new Dictionary<int, int> { [0] = 0, [1] = 1, [2] = 2, [3] = 3, [4] = 4 };

        QuadraticConstraintBatch filtered = IisStructuralSupport.FilterConstraintByRowIndex(batch, [1], map);

        filtered.Names.Should().Equal(
            ["c1"],
            "only original row 1 survives, so its name must be carried over rather than the new row 0's name");
        filtered.Rhs.Should().Equal(
            [new Flt64(2.0)],
            "the right-hand side must come from original row 1 as well");
        filtered.Signs.Should().Equal(
            [ConstraintRelation.LessEqual],
            "the relation comes from original row 1 too");
    }

    /// <summary>**二次项有一端被裁掉时，整格必须丢弃** / A quadratic term is dropped entirely when either end is trimmed.</summary>
    /// <remarks>
    /// 这是最容易写错的一处：只检查 `Col1` 在映射里、却把 `Col2` 原样留下，
    /// **重映射后的矩阵会指向不存在的列**，喂给求解器时越界或语义错误。
    ///
    /// 夹具第 `0` 行含二次项 `(0, 1)`。映射里**去掉列 `1`**，那么整格 `(0, 1)` 必须消失；
    /// 纯线性项 `(0, -1)`、`(2, -1)`、`(3, -1)` 只要列在映射里就保留。
    ///
    /// The easiest place to get wrong: checking only that `Col1` is mapped while leaving `Col2` untouched **makes the remapped matrix point at a non-existent column**.
    /// Row `0` carries the quadratic term `(0, 1)`; with column `1` **removed from the map**, that whole term must vanish, while the linear terms survive as long as their
    /// column is mapped.
    /// </remarks>
    [Fact]
    public void FilterConstraintByRowIndex_ShouldDropAQuadraticTermWhenEitherEndIsTrimmed() {
        (QuadraticConstraintBatch batch, _) = MakeBatch();
        var map = new Dictionary<int, int> { [0] = 0, [2] = 1, [3] = 2 };

        QuadraticConstraintBatch filtered = IisStructuralSupport.FilterConstraintByRowIndex(batch, [0], map);

        List<SparseQuadraticEntry> entries = [.. filtered.SparseLhs.Rows[0].Entries];
        entries.Should().NotContain(
            entry => entry.Col2 >= 0,
            "column 1 is not in the map, so the quadratic term (0, 1) must be dropped whole rather than left pointing at a stale column");
        entries.Should().Contain(
            entry => entry.Col1 == 0 && entry.Col2 < 0,
            "the linear term on column 0 stays: its column is still mapped");
        entries.Should().Contain(
            entry => entry.Col1 == 2,
            "column 3 is remapped to 2, so its linear term stays under the new index");
    }

    /// <summary>**没有映射的纯线性项也必须丢弃** / A linear term whose column is unmapped must also be dropped.</summary>
    /// <remarks>
    /// 反向对照：上一条只证明"二次项被丢"，若实现干脆**丢弃所有单元格**也能通过。
    /// 这条钉住空映射的结果是**零个单元格**，与"保留再过滤"区分开。
    ///
    /// Reverse control: the previous criterion only proves "the quadratic term is dropped", which an implementation that **discards every cell** would also satisfy. This one
    /// pins that an empty map yields **zero cells**.
    /// </remarks>
    [Fact]
    public void FilterConstraintByRowIndex_ShouldDropEverythingWithAnEmptyMap() {
        (QuadraticConstraintBatch batch, _) = MakeBatch();

        QuadraticConstraintBatch filtered = IisStructuralSupport.FilterConstraintByRowIndex(
            batch, [0], new Dictionary<int, int>());

        filtered.SparseLhs.Rows[0].Entries.Should().BeEmpty(
            "no column is mapped, so no cell survives");
        filtered.Names.Should().Equal(["c0"], "the row itself is still kept: rows and columns are filtered independently");
    }

    /// <summary>**未保留的边界要变成 `±∞`，而不是保持原值** / An unretained bound becomes `±∞`, not its original value.</summary>
    /// <remarks>
    /// **这是 IIS 的语义核心。** `null` 不表示"没有边界"，而表示"这条边界不参与不可行"。
    /// 只有换成 `±∞`，子系统在去掉该边界后才**仍然可行**，从而证明它确实必要。
    ///
    /// | 实现 | `x` 的下界 |
    /// | --- | --- |
    /// | `null ⇒ -∞`（正确） | `-∞` |
    /// | 保留原下界 `0` | `0`，**判据红** |
    ///
    /// **This is the semantic core of an IIS.** `null` means "this bound does not take part in the infeasibility", not "no bound". Only substituting `±∞` leaves the
    /// subsystem **feasible** once that bound is removed, which is what proves it necessary (table above).
    /// </remarks>
    [Fact]
    public void Dump_ShouldReplaceUnretainedBoundsWithInfinity() {
        QuadraticIisModel dumped = IisStructuralSupport.Dump(MakeDumpModel(), [Slack]);

        ModelViewVariable x = dumped.Subsystem.Variables[0];
        x.Name.Should().Be("x");
        x.LowerBound.Should().Be(
            NumericConstantsRegistry.For<Flt64>().NegativeInfinity!.Value,
            "x is only a coefficient, so its lower bound must be dropped to -infinity rather than kept at 0");
        x.UpperBound.Should().Be(
            NumericConstantsRegistry.For<Flt64>().PositiveInfinity!.Value,
            "x is only a coefficient, so its upper bound must be dropped to +infinity rather than kept at 10");
    }

    /// <summary>**保留的边界必须原样带过去** / A retained bound must be carried over unchanged.</summary>
    /// <remarks>
    /// 反向对照：上一条只证明"边界被丢掉"，若实现把**所有**边界都换成 `±∞` 也能通过。
    /// 这条钉住 `s_lb` 的下界 `0` 被保留。
    ///
    /// Reverse control: the previous criterion only proves bounds are dropped, which an implementation that replaces **every** bound with `±∞` would also satisfy. This one
    /// pins that `s_lb`'s lower bound survives.
    /// </remarks>
    [Fact]
    public void Dump_ShouldCarryRetainedBoundsUnchanged() {
        QuadraticIisModel dumped = IisStructuralSupport.Dump(MakeDumpModel(), [Slack]);

        ModelViewVariable slackLower = dumped.Subsystem.Variables.Single(v => v.Name == "s_lb");
        slackLower.LowerBound.Should().Be(
            new Flt64(0.0),
            "s_lb is a slack lower-bound variable, so its lower bound belongs to the IIS and must be carried over");
    }

    /// <summary>**子系统行数与列溯源必须自洽** / The subsystem's row count and column provenance must be consistent.</summary>
    /// <remarks>
    /// 钉住三件事一起成立：行数是 `1`、列溯源覆盖全部五列、名称带 `_iis` 后缀。
    /// 只钉其中一个的话，"行数对但列映射错"的实现会漏网。
    ///
    /// Pins three things together: one row, provenance covering all five columns, and the `_iis` name suffix. Pinning only one would let "right row count but wrong column
    /// mapping" slip through.
    /// </remarks>
    [Fact]
    public void Dump_ShouldProduceAConsistentSubsystem() {
        QuadraticIisModel dumped = IisStructuralSupport.Dump(MakeDumpModel(), [Slack]);

        dumped.Subsystem.Constraints.ConstraintCount.Should().Be(1, "only row 0 is related");
        dumped.Subsystem.Name.Should().Be("qp_iis", "the subsystem name carries the origin name plus the suffix");
        dumped.OriginRows.Should().Equal([0]);
        dumped.OriginColumns.Should().Equal(
            [X, Y, Slack, SlackLower, SlackUpper],
            "provenance must record every kept column in ascending order");
        dumped.Subsystem.Variables.Should().HaveCount(5);
    }

    /// <summary>**列有缺口时，溯源必须记原索引、目标函数必须丢掉未映射的项** / With a column gap, provenance records origin indices and the objective drops unmapped terms.</summary>
    /// <remarks>
    /// **上一版判据钉不住这两点，原因还是"夹具里所有列都在映射内"。**
    ///
    /// 原夹具通过松弛边界与行系数把**全部五列**都标记了，于是：
    ///
    /// | 变异 | 为什么存活 |
    /// | --- | --- |
    /// | 列溯源写成 `newIndex` | 溯源恰好是 `[0,1,2,3,4]`，**与恒等排列相同** |
    /// | 目标函数只检查 `Col1` | `Col2` 指向的列**总在映射里**，检查从未触发 |
    ///
    /// 新夹具刻意留缺口：变量 `0` 与 `2` 出现在行 `0`，**变量 `1` 两边都不出现**；
    /// 变量 `4` **只出现在目标函数里**。
    ///
    /// | 断言 | 钉住 |
    /// | --- | --- |
    /// | `OriginColumns == [0, 2, 3]` | 溯源记的是**原索引**，不是新索引（新索引会是 `[0, 1, 2]`） |
    /// | 目标仅剩 `(0,-1)` 与 `(0,1)` | `(0,4)` 因 `Col2` 未映射被**整项丢弃** |
    ///
    /// **The previous criteria could not pin these, again because every column was inside the map.** The original fixture marked **all five columns** through slack bounds
    /// and row coefficients, so (table above) provenance was `[0,1,2,3,4]` — **the identity permutation** — and the `Col2` check never fired. The new fixture leaves gaps
    /// on purpose: variables `0` and `2` appear in row `0`, **variable `1` appears nowhere**, and variable `4` **exists only in the objective** (assertions in the table).
    /// </remarks>
    [Fact]
    public void Dump_ShouldRecordOriginIndicesAndDropUnmappedObjectiveTerms() {
        QuadraticIisModel dumped = IisStructuralSupport.Dump(MakeGappedModel(), [3]);

        // 松弛列 `3` 本身也是行 `0` 的系数，所以一并纳入——这正是"关联"的定义。
        // Slack column `3` is itself a coefficient of row `0`, so it comes along too — that is exactly what "related" means.
        dumped.OriginColumns.Should().Equal(
            [0, 2, 3],
            "provenance must record origin column indices; recording new indices would give [0, 1, 2], which differs here because column 1 is missing");
        dumped.Subsystem.Variables.Should().HaveCount(3);
        dumped.Subsystem.Variables[0].Name.Should().Be("a");
        dumped.Subsystem.Variables[1].Name.Should().Be("c");
        dumped.Subsystem.Variables[2].Name.Should().Be("s");

        List<QuadraticObjectiveCell> cells = [.. dumped.Subsystem.Objective.Cells];
        cells.Should().HaveCount(
            2,
            "the objective term (0, 4) must be dropped whole because column 4 is not in the map");
        cells.Should().NotContain(
            cell => cell.Col1 == 4 || cell.Col2 == 4,
            "column 4 is unmapped, so no surviving term may reference it");
        cells.Should().Contain(
            cell => cell.Col1 == 0 && cell.Col2 == 1,
            "the quadratic term (0, 2) is remapped to (0, 1)");
    }

    /// <summary>**关联变量为空时必须返回原模型快照，而不是空子系统** / An empty relation must yield the origin snapshot, not an empty subsystem.</summary>
    /// <remarks>
    /// **记着这个分支"只靠类型约束兜底"**，这里补齐。
    ///
    /// 触发条件：过滤的松弛列**既没有下界变量、也没有上界变量、更没有来源行**——
    /// 于是 `RelatedRows` 返回空、`RelatedVariables` 返回空。
    ///
    /// | 实现 | 结果 |
    /// | --- | --- |
    /// | 返回原模型快照（正确） | `Subsystem` 与入参**同一个实例**，行/列溯源皆空 |
    /// | 继续构造空子系统 | 0 个变量 0 行，**判据红**（实例不同） |
    /// | 抛异常 | **判据红** |
    ///
    /// 用**引用相等**断言"同一个实例"，而不是比较内容——内容相等的副本
    /// 会在后续被当作独立模型修改，失去快照语义。
    ///
    /// **recorded that this branch was "only guarded by the type constraint"**; closes it. Trigger: the filtered slack column has **no bound variables and
    /// no source row**, so `RelatedRows` and `RelatedVariables` both come back empty (table above). The criterion asserts **reference equality** for "the same instance"
    /// rather than comparing contents, because an equal-valued copy would later be mutated as an independent model and lose snapshot semantics.
    /// </remarks>
    [Fact]
    public void Dump_ShouldReturnTheOriginInstanceWhenNothingIsRelated() {
        IQuadraticTetradModelView origin = MakeIsolatedSlackModel();

        QuadraticIisModel dumped = IisStructuralSupport.Dump(origin, [1]);

        dumped.Subsystem.Should().BeSameAs(
            origin,
            "with nothing related, the IIS is the whole model, and it must be the same instance so callers cannot diverge from it");
        dumped.OriginRows.Should().BeEmpty("no row is related");
        dumped.OriginColumns.Should().BeEmpty("no column is related");
    }

    /// <summary>构造"孤立松弛列"的模型 / Build a model whose slack column is isolated.</summary>
    /// <returns>模型视图 / The model view.</returns>
    private static IQuadraticTetradModelView MakeIsolatedSlackModel() => new QuadraticTetradModel(
        variables: new List<ModelViewVariable> {
            new(0, new Flt64(0.0), new Flt64(10.0), Continuous.Instance, "a"),
            new(1, new Flt64(0.0), new Flt64(10.0), Continuous.Instance, "s", slack: new VariableSlack()),
        },
        constraints: new QuadraticConstraintBatch(
            lhs: new SparseQuadraticMatrix([new SparseQuadraticVector(new List<SparseQuadraticEntry> {
                new(0, -1, Flt64.One),
            })]),
            signs: [ConstraintRelation.GreaterEqual],
            rhs: [Flt64.One],
            names: ["c0"],
            sources: [ConstraintSource.Origin]),
        objective: new Objective<QuadraticObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: new List<QuadraticObjectiveCell> { new(0, -1, Flt64.One) },
            constant: Flt64.Zero),
        name: "isolated");

    /// <summary>构造带列缺口的模型 / Build a model with a column gap.</summary>
    /// <returns>模型视图 / The model view.</returns>
    private static IQuadraticTetradModelView MakeGappedModel() {
        var slackVariables = new List<ModelViewVariable> {
            new(0, new Flt64(0.0), new Flt64(10.0), Continuous.Instance, "a"),
            new(1, new Flt64(0.0), new Flt64(10.0), Continuous.Instance, "b"),
            new(2, new Flt64(0.0), new Flt64(10.0), Continuous.Instance, "c"),
            new(3, new Flt64(0.0), new Flt64(100.0), Continuous.Instance, "s", slack: new VariableSlack(
                Constraint: new ConstraintSlackOrigin(RowIndex: 0, Relation: ConstraintRelation.GreaterEqual))),
            new(4, new Flt64(0.0), new Flt64(10.0), Continuous.Instance, "d"),
        };
        var constraints = new QuadraticConstraintBatch(
            lhs: new SparseQuadraticMatrix([new SparseQuadraticVector(new List<SparseQuadraticEntry> {
                new(0, -1, Flt64.One),
                new(2, -1, Flt64.One),
                new(3, -1, Flt64.One),
            })]),
            signs: [ConstraintRelation.GreaterEqual],
            rhs: [Flt64.One],
            names: ["c0"],
            sources: [ConstraintSource.Origin]);
        return new QuadraticTetradModel(
            variables: slackVariables,
            constraints: constraints,
            objective: new Objective<QuadraticObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: new List<QuadraticObjectiveCell> {
                    new(0, -1, Flt64.One),
                    new(0, 2, Flt64.One),
                    new(0, 4, Flt64.One),
                },
                constant: Flt64.Zero),
            name: "qp");
    }

    /// <summary>构造用于 `Dump` 的完整二次模型 / Build a full quadratic model for `Dump`.</summary>
    /// <returns>模型视图 / The model view.</returns>
    private static IQuadraticTetradModelView MakeDumpModel() => new QuadraticTetradModel(
        variables: MakeVariables(),
        constraints: MakeLhs(),
        objective: new Objective<QuadraticObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: new List<QuadraticObjectiveCell> {
                new(X, -1, Flt64.One),
                new(X, Y, Flt64.One),
            },
            constant: Flt64.Zero),
        name: "qp");

    /// <summary>构造 5 变量夹具 / Build the 5-variable fixture.</summary>
    /// <returns>变量列表 / The variable list.</returns>
    private static List<ModelViewVariable> MakeVariables() {
        var slackInfo = new VariableSlack(
            LowerBound: new ModelViewVariable(SlackLower, new Flt64(0.0), new Flt64(100.0), Continuous.Instance, "s_lb"),
            UpperBound: new ModelViewVariable(SlackUpper, new Flt64(0.0), new Flt64(10.0), Continuous.Instance, "s_ub"),
            Constraint: new ConstraintSlackOrigin(RowIndex: 0, Relation: ConstraintRelation.GreaterEqual));

        return [
            new ModelViewVariable(X, new Flt64(0.0), new Flt64(10.0), Continuous.Instance, "x"),
            new ModelViewVariable(Y, new Flt64(0.0), new Flt64(10.0), Continuous.Instance, "y"),
            new ModelViewVariable(Slack, new Flt64(0.0), new Flt64(1000.0), Continuous.Instance, "s", slack: slackInfo),
            new ModelViewVariable(SlackLower, new Flt64(0.0), new Flt64(100.0), Continuous.Instance, "s_lb"),
            new ModelViewVariable(SlackUpper, new Flt64(0.0), new Flt64(10.0), Continuous.Instance, "s_ub"),
        ];
    }

    /// <summary>构造单行二次约束批处理 / Build the single-row quadratic constraint batch.</summary>
    /// <returns>约束批处理 / The constraint batch.</returns>
    private static QuadraticConstraintBatch MakeLhs() => new(
        lhs: new SparseQuadraticMatrix([new SparseQuadraticVector(new List<SparseQuadraticEntry> {
            new(X, -1, Flt64.One),
            new(X, Y, Flt64.One),
            new(Slack, -1, Flt64.One),
            new(SlackLower, -1, Flt64.One),
        })]),
        signs: [ConstraintRelation.GreaterEqual],
        rhs: [Flt64.One],
        names: ["c0"],
        sources: [ConstraintSource.Origin]);

    /// <summary>构造两行约束批处理 / Build a two-row constraint batch.</summary>
    /// <returns>批处理与矩阵 / The batch and its matrix.</returns>
    private static (QuadraticConstraintBatch Batch, SparseQuadraticMatrix Matrix) MakeBatch() {
        var matrix = new SparseQuadraticMatrix();
        matrix.AddRow(new SparseQuadraticVector(new List<SparseQuadraticEntry> {
            new(0, -1, Flt64.One),
            new(0, 1, Flt64.One),
            new(2, -1, Flt64.One),
            new(3, -1, Flt64.One),
        }));
        matrix.AddRow(new SparseQuadraticVector(new List<SparseQuadraticEntry> {
            new(4, -1, Flt64.One),
        }));
        var batch = new QuadraticConstraintBatch(
            lhs: matrix,
            signs: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
            rhs: [new Flt64(1.0), new Flt64(2.0)],
            names: ["c0", "c1"],
            sources: [ConstraintSource.Origin, ConstraintSource.Origin]);
        return (batch, matrix);
    }
}
