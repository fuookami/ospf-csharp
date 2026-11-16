// 方案建模半边判据 / Criteria for the modelling half of pattern assignment.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp1d.Domain.PatternAssignment;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.PatternAssignment;

/// <summary>
/// **方案建模半边** / The modelling half of pattern assignment.
/// </summary>
/// <remarks>
/// `register` / `addColumns` / `refresh`。判据点出四处要害：
///
/// | 点 | 错法 | 后果 |
/// | --- | --- | --- |
/// | **需求行是 `== amount`** | 写成 `≤` | "少装一件"也算可行，求解器把它当成更省的解 |
/// | **库存行只在声明了库存时写** | 无条件写 | 未声明库存的箱型被凭空限制成 0 |
/// | **列上界是整数除法 `需求 / 件数`** | 浮点除法 | 允许 2.5 份，取解时被整数性检查拒 |
/// | **重复键跳过而不报错** | 再建一次变量 | 同一方案被重复计费 |
///
/// /`addColumns`/`refresh`; the table lists the four failure modes.
/// </remarks>
public class PatternAssignmentModellingTests {
    /// <summary>**需求行用 `==` 且右端是货物数量** / The demand row uses `==` with the item amount.</summary>
    /// <remarks>
    /// **这里最要紧的一条。** `== 5` 与 `≤ 5` **约束名相同、项数相同**，只有不等号不同。
    /// 写成 `≤` 时"少装一件"也算可行——**而求解器非常乐意把它当成更省的解**。
    ///
    /// **The most important criterion in.** `== 5` and `&lt;= 5` share the name and the term count; only the comparison differs. With `&lt;=`, "pack one fewer"
    /// is feasible, and **the solver is happy to call that the cheaper solution**.
    /// </remarks>
    [Fact]
    public void Register_ShouldUseEqualityForTheDemandRow() {
        PatternAssignmentContext<Flt64> context = MakeContext(itemAmount: 5UL);
        LinearMetaModel<Flt64> model = new();

        Try result = context.Register(new List<PackingPattern<Flt64>>(), model);

        result.IsOk.Should().BeTrue(result.ToString());
        LinearInequality<Flt64> demand = RowOf(model, "demand_I1");
        demand.Comparison.Should().Be(Comparison.EQ, "a demand is an obligation, not an upper limit");
        demand.Rhs.Constant.ToDouble().Should().BeApproximately(5.0, 1e-12);
    }

    /// <summary>**未声明库存的箱型不写库存行** / A bin type without an inventory gets no inventory row.</summary>
    /// <remarks>
    /// 无条件写会让**未声明库存**的箱型被限制成 0——那等于把这个箱型整个禁掉，
    /// 而调用方只是**没说要限库存**。
    ///
    /// Writing the row unconditionally caps a bin type with **no declared inventory** at 0, effectively banning it, when the caller merely **did not ask for a cap**.
    /// </remarks>
    [Fact]
    public void Register_ShouldOmitTheInventoryRowWhenNoInventoryIsDeclared() {
        PatternAssignmentContext<Flt64> context = MakeContext(itemAmount: 5UL, inventory: null);
        LinearMetaModel<Flt64> model = new();

        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();

        RowExists(model, "inventory_B1").Should().BeFalse("no inventory was declared for B1");
        RowExists(model, "demand_I1").Should().BeTrue();
    }

    /// <summary>**声明了库存的箱型写 `≤` 行** / A bin type with an inventory gets a `&lt;=` row.</summary>
    [Fact]
    public void Register_ShouldWriteAnInequalityInventoryRowWhenDeclared() {
        PatternAssignmentContext<Flt64> context = MakeContext(itemAmount: 5UL, inventory: 3UL);
        LinearMetaModel<Flt64> model = new();

        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();

        LinearInequality<Flt64> inventory = RowOf(model, "inventory_B1");
        inventory.Comparison.Should().Be(Comparison.LE, "an inventory is a cap");
        inventory.Rhs.Constant.ToDouble().Should().BeApproximately(3.0, 1e-12);
    }

    /// <summary>**列上界是 `需求 / 件数` 的整数最小值** / The column bound is the integer minimum of `demand / amount`.</summary>
    /// <remarks>
    /// 需求 `5`，方案每箱 2 件 ⇒ 上界 `5 / 2 = 2`（整数除法）。
    /// 用浮点除法会得到 `2.5`——那允许求解器取 2.5 份，**一个非整数解**，
    /// 取解时才被整数性检查拒掉，而模型阶段毫无提示。
    /// 判据同时钉住**整数性**：若实现用浮点除法，右端会是 `2.5`。
    ///
    /// Demand `5` with 2 pieces per bin gives `5 / 2 = 2` in integer division; floating division yields `2.5`, allowing the solver to take 2.5 copies — **a non-integer
    /// solution** rejected only at extraction, with no warning at the model stage. The criterion pins the **integer-ness** too: floating division would leave `2.5`.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldBoundTheColumnByIntegerDivision() {
        PatternAssignmentContext<Flt64> context = MakeContext(itemAmount: 5UL);
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();

        context.AddColumns(new List<PackingPattern<Flt64>> { Pattern("B1", ("I1", 2)) }, model)
            .IsOk.Should().BeTrue();

        // **上界要看变量的区间，不能看约束行。** 曾把它写成一条约束行，并在文档里记成"有意偏离"；
        // 真实的 SCIP 一跑就拒：`UIntVar` 的缺省上界 `ulong.MaxValue` 换 `double` 是 `1.8e19`，
        // 超过 binary64 能精确表示的整数上界 `2^53`。**区间上界不设，模型根本注册不上。**
        //
        // **The bound belongs to the variable's range, not to a constraint row.** recorded a row as a "deliberate deviation"; real SCIP refused it outright, since
        // `UIntVar`'s default `ulong.MaxValue` becomes `1.8e19` as a `double` — past `2^53`. **Without the range bound the model does not even register.**
        UIntVar variable = context.Aggregation.Variables[Pattern("B1", ("I1", 2)).Key];
        variable.Range.UpperBound.Should().NotBeNull();
        variable.Range.UpperBound!.Value.ToFlt64().ToDouble().Should().BeApproximately(
            2.0, 1e-12, "5 / 2 is 2 in integer division, not 2.5");
    }

    /// <summary>**重复键跳过而不报错，也不重复建变量** / A duplicate key is skipped without error and without a second variable.</summary>
    /// <remarks>
    /// `if (pattern.key in pool) continue`——**跳过，不是失败**：
    /// 列生成每轮都会重发整池候选，把"已在池里"当成错误会让每轮都失败。
    /// 但**必须真的不建第二个变量**，否则同一方案在目标里被重复计费。
    ///
 /// This implementation does `if (pattern.key in pool) continue` — **skip, not fail**: column generation resends the whole pool each round, so treating "already in the pool"
    /// as an error would fail every round. But it **must not create a second variable**, or the same pattern is charged twice in the objective.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldSkipAnAlreadyPresentKeyWithoutDuplicatingTheVariable() {
        PatternAssignmentContext<Flt64> context = MakeContext(itemAmount: 5UL);
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();

        var columns = new List<PackingPattern<Flt64>> { Pattern("B1", ("I1", 2)) };
        context.AddColumns(columns, model).IsOk.Should().BeTrue();
        var again = new List<PackingPattern<Flt64>> { Pattern("B1", ("I1", 2)) };
        context.AddColumns(again, model).IsOk.Should().BeTrue();

        context.Aggregation.Variables.Should().HaveCount(1, "the same key must not be added twice");
        context.Aggregation.Pool.Should().HaveCount(1);
        // **必须看"建过几个列变量"，不能只看聚合表。** `Variables` 与 `Pool` 都是**按键覆盖**的字典，
        // 重复建出来的第二个变量被覆盖掉，条数仍然不变，判据看不出问题。
        // `ColumnSequence` 是**只增不减**的计数器，重复键会让它从 1 变成 2。
        //
        // **The "how many column variables were created" counter must be asserted, not just the aggregation maps.** `Variables` and `Pool` are **keyed dictionaries whose
        // entries get overwritten**, so a duplicated second variable vanishes and the counts stay the same. `ColumnSequence` only **grows**; a duplicate key moves it
        // from 1 to 2.
        context.ColumnSequence.Should().Be(1, "the duplicate key must not create a second variable");
    }

    /// <summary>**引用未知货物必须被拒** / An unknown item reference must be rejected.</summary>
    [Fact]
    public void AddColumns_ShouldRejectAnUnknownItemReference() {
        PatternAssignmentContext<Flt64> context = MakeContext(itemAmount: 5UL);
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();

        Try result = context.AddColumns(new List<PackingPattern<Flt64>> { Pattern("B1", ("NOPE", 1)) }, model);

        result.IsFailed.Should().BeTrue("NOPE is not an item");
    }

    /// <summary>**件数超过需求必须被拒** / A count above the demand must be rejected.</summary>
    [Fact]
    public void AddColumns_ShouldRejectACountAboveTheDemand() {
        PatternAssignmentContext<Flt64> context = MakeContext(itemAmount: 5UL);
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();

        Try result = context.AddColumns(new List<PackingPattern<Flt64>> { Pattern("B1", ("I1", 6)) }, model);

        result.IsFailed.Should().BeTrue("6 exceeds the demand of 5");
    }

    /// <summary>**未知箱型必须被拒** / An unknown bin type must be rejected.</summary>
    [Fact]
    public void AddColumns_ShouldRejectAnUnknownBinType() {
        PatternAssignmentContext<Flt64> context = MakeContext(itemAmount: 5UL);
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();

        Try result = context.AddColumns(new List<PackingPattern<Flt64>> { Pattern("NOPE", ("I1", 1)) }, model);

        result.IsFailed.Should().BeTrue("NOPE is not a bin type");
    }

    /// <summary>**件数为空的方案必须被拒** / A pattern with no pieces must be rejected.</summary>
    [Fact]
    public void AddColumns_ShouldRejectAnEmptyAmountMap() {
        PatternAssignmentContext<Flt64> context = MakeContext(itemAmount: 5UL);
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();

        Try result = context.AddColumns(
            new List<PackingPattern<Flt64>> { new("B1", new Dictionary<string, ulong>()) }, model);

        result.IsFailed.Should().BeTrue("a pattern that packs nothing is not a pattern");
    }

    /// <summary>**刷新把列池的件数写进需求表达式** / Refresh writes the pool's amounts into the demand expression.</summary>
    [Fact]
    public void Refresh_ShouldRebuildTheDemandExpressionFromThePool() {
        PatternAssignmentContext<Flt64> context = MakeContext(itemAmount: 5UL);
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();
        context.AddColumns(
            new List<PackingPattern<Flt64>> { Pattern("B1", ("I1", 2)), Pattern("B1", ("I1", 3)) }, model)
            .IsOk.Should().BeTrue();

        LinearExpressionSymbol demand = context.Aggregation.Demands["I1"];

        demand.Polynomial.Monomials.Should().HaveCount(2, "one term per column that carries I1");
        demand.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(2.0, 1e-12);
        demand.Polynomial.Monomials[1].Coefficient.ToDouble().Should().BeApproximately(3.0, 1e-12);
    }

    /// <summary>**重复注册必须被拒** / Registering twice must be rejected.</summary>
    [Fact]
    public void Register_ShouldRejectASecondRegistration() {
        PatternAssignmentContext<Flt64> context = MakeContext(itemAmount: 5UL);
        LinearMetaModel<Flt64> model = new();

        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();
        Try second = context.Register(new List<PackingPattern<Flt64>>(), model);

        second.IsFailed.Should().BeTrue("one context serves one registration");
    }

    /// <summary>**未注册就加列必须被拒** / Adding columns before registering must be rejected.</summary>
    [Fact]
    public void AddColumns_ShouldRejectAnUnregisteredContext() {
        PatternAssignmentContext<Flt64> context = MakeContext(itemAmount: 5UL);
        LinearMetaModel<Flt64> model = new();

        Try result = context.AddColumns(new List<PackingPattern<Flt64>> { Pattern("B1", ("I1", 1)) }, model);

        result.IsFailed.Should().BeTrue("nothing has been registered yet");
    }

    /// <summary>构造上下文 / Build a context.</summary>
    /// <param name="itemAmount">货物数量 / Item amount.</param>
    /// <param name="inventory">箱型库存，`null` 表示不声明 / Bin inventory; `null` means none declared.</param>
    /// <returns>上下文 / The context.</returns>
    private static PatternAssignmentContext<Flt64> MakeContext(ulong itemAmount, ulong? inventory = null) {
        var bins = new List<BinType<Flt64>> {
            // **箱长必须够装**：100 mm × 5 件 = 500 mm，箱长 1000 mm。
            // **The bin must be long enough**: 100 mm × 5 = 500 mm inside a 1000 mm bin.
            new("B1", Millimetres(1000.0), Kilograms(10.0), Flt64.One, inventory),
        };
        var problem = new PackingProblem<Flt64>(
            Items: new List<Item<Flt64>> { new("I1", Millimetres(100.0), Kilograms(1.0), itemAmount) },
            BinTypes: bins,
            Objective: PackingObjective.BinCount,
            Spacing: Millimetres(0.0));
        return new PatternAssignmentContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
    }

    /// <summary>按名称取出行 / Find a row by name.</summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="name">约束名 / Constraint name.</param>
    /// <returns>线性不等式 / The linear inequality.</returns>
    private static LinearInequality<Flt64> RowOf(LinearMetaModel<Flt64> model, string name) {
        foreach (LinearInequalityConstraint<Flt64> row in model.RelationConstraints) {
            if (row.Name == name) {
                return row.Inequality;
            }
        }
        throw new global::System.InvalidOperationException($"no constraint named {name}");
    }

    /// <summary>约束是否存在 / Whether a named row exists.</summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="name">约束名 / Constraint name.</param>
    /// <returns>判断结果 / The verdict.</returns>
    private static bool RowExists(LinearMetaModel<Flt64> model, string name) {
        foreach (LinearInequalityConstraint<Flt64> row in model.RelationConstraints) {
            if (row.Name == name) {
                return true;
            }
        }
        return false;
    }

    /// <summary>构造方案 / Build a pattern.</summary>
    /// <param name="binTypeId">箱型 / Bin type.</param>
    /// <param name="amounts">件数 / Amounts.</param>
    /// <returns>方案 / The pattern.</returns>
    private static PackingPattern<Flt64> Pattern(string binTypeId, params (string Item, ulong Amount)[] amounts) {
        var map = new Dictionary<string, ulong>();
        foreach ((string item, ulong amount) in amounts) {
            map[item] = amount;
        }
        return new PackingPattern<Flt64>(binTypeId, map);
    }

    /// <summary>构造毫米量 / Build a millimetre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    /// <summary>构造千克量 / Build a kilogram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) =>
        new(new Flt64(value), SIBaseUnits.Kilogram);
}
