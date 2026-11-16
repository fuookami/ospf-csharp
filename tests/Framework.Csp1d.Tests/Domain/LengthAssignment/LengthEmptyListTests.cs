// length 提取的空列表判据 / Criteria for the empty-list semantics of length extraction.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Domain.LengthAssignment;

/// <summary>
/// **"没有分配"是空列表，不是 null** / "Nothing assigned" is an empty list, not null.
/// </summary>
/// <remarks>
/// 发现的偏离**：`YieldAggregation` 的同一处问题已经修好了，
/// **漏了 `LengthAggregation`**——那里是**一模一样的 `Count > 0 ? list : null`**。
///
/// `LengthAssignmentModelingResult` 的两个字段默认是 `= emptyList`，
/// 返回处直接传列表本身。
///
/// **这是第二次"修一处等于没修"**（第一次是 `SolutionAmount`）。
/// 两次都是**同一个模式在另一个类里**。规律：**修完一处必须全仓搜该模式**。
///
/// **the earlier deviation**: fixed this in `YieldAggregation` and **missed `LengthAggregation`**, which had **the identical `Count > 0 ? list : null`**. Its two
/// fields are `= emptyList` and the return passes the lists through. **This is the second "fixing one place changes nothing"** in this session (the first was
/// `SolutionAmount`). Both were **the same pattern in another class**. The rule: **after fixing one site, search the whole repo for the pattern**.
/// </remarks>
public class LengthEmptyListTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Product<Flt64> MakeProduct(string id) =>
        new(id: id, name: id, width: new List<Quantity<Flt64>> { Millimetres(60.0) });

    private static (LengthAggregation Aggregation, LinearMetaModel<Flt64> Model) MakeLength() {
        var demands = new List<ProductDemand<Flt64>> {
            new(MakeProduct("A"), Millimetres(60.0)),
        };
        var config = new LengthAssignmentModelingConfig<Flt64>(
            DynamicProductIds: new HashSet<string> { "A" },
            AssignedLengthLowerBound: new Dictionary<string, Flt64> { ["A"] = new Flt64(0.0) },
            AssignedLengthUpperBound: new Dictionary<string, Flt64> { ["A"] = new Flt64(1000.0) },
            OverLengthPenalty: new Dictionary<string, Flt64> { ["A"] = new Flt64(1.0) });
        var aggregation = new LengthAggregation(config, demands);
        var model = new LinearMetaModel<Flt64>();
        aggregation.HasAny.Should().BeTrue("the config declares a dynamic product, so variables exist");
        // **必须 `Register`**：变量不注册进模型，`Tokens.Find` 返回 null，
        // 解值设不进去——第一次跑就是 `NullReferenceException`。
        // **`Register` is required**: unregistered variables make `Tokens.Find` return null and the value cannot be set — the first run threw `NullReferenceException`.
        aggregation.Register(model).IsOk.Should().BeTrue();
        return (aggregation, model);
    }

    /// <summary>**解全为零时两个列表都非 null** / With an all-zero solution both lists are non-null.</summary>
    /// <remarks>
    /// **注意 `AssignedLength` 的判据是 `>= 0.0`**（不是 `> 0.0`）——所以把解设成 0
    /// 会让它**进入列表**。这条要验证的正是"列表本身不是 null"，不是"列表为空"。
    ///
    /// **Note `AssignedLength` uses `>= 0.0`** (not `> 0.0`), so setting the solution to 0 makes it **enter the list**. This criterion verifies "the list is not null",
    /// not "the list is empty".
    /// </remarks>
    [Fact]
    public void ExtractResult_ShouldReturnNonNullLists() {
        (LengthAggregation aggregation, LinearMetaModel<Flt64> model) = MakeLength();
        // 不给任何变量设值：`DoubleResult` 为 null，两个循环都不追加。
        // Assign no variable values: `DoubleResult` stays null and neither loop appends.

        LengthAssignmentModelingResult<Flt64>? result = aggregation.ExtractResult(model);

        result.Should().NotBeNull();
        result!.AssignedLengths.Should().NotBeNull(
            "the baseline defaults to emptyList(), so 'nothing assigned' is [] and never null");
        result.OverLengths.Should().NotBeNull();
        result.AssignedLengths.Should().BeEmpty();
        result.OverLengths.Should().BeEmpty();
    }

    /// <summary>**已分配长度为 0 也必须进入列表**——`>=` 与 `>` 的分界 / An assigned length of 0 must enter the list — the `>=` versus `>` boundary.</summary>
    /// <remarks>
    /// `assignedLength` 用 `>= 0.0`、`overLength` 用 `> 0.0`。**两个边界不同**：
    ///
    /// | 变量 | 解 = 0 | 解 = 5 |
    /// | --- | --- | --- |
    /// | `assignedLength` | **进入列表** | 进入列表 |
    /// | `overLength` | **不进入** | 进入列表 |
    ///
    /// 若实现把两者都写成 `> 0.0`，"长度为 0 的动态产品"会**从结果里消失**——
    /// 下游会以为那个产品根本没有动态卷长。
    ///
 /// This implementation uses `>= 0.0` for `assignedLength` and `> 0.0` for `overLength` — **the two boundaries differ** (table above). If both were `> 0.0`, a dynamic product
    /// with length 0 would **vanish from the result**, and downstream would believe it has no dynamic length at all.
    /// </remarks>
    [Fact]
    public void ExtractResult_ShouldIncludeAZeroAssignedLengthButNotAZeroOverLength() {
        (LengthAggregation aggregation, LinearMetaModel<Flt64> model) = MakeLength();
        // **列表里可能有 null 占位**（按 demand 索引），必须挑出非 null 的那个。
        // **The lists may hold null placeholders** (indexed by demand), so pick the non-null entry.
        URealVar assignedVar = aggregation.AssignedLength.First(v => v is not null)!;
        URealVar overVar = aggregation.OverLength.First(v => v is not null)!;
        model.Tokens.Find(assignedVar)!.SetResult(Flt64.Zero);
        model.Tokens.Find(overVar)!.SetResult(Flt64.Zero);

        LengthAssignmentModelingResult<Flt64>? result = aggregation.ExtractResult(model);

        result.Should().NotBeNull();
        result!.AssignedLengths.Should().HaveCount(
            1,
            "an assigned length of 0 still counts, because the baseline uses >= 0.0");
        result.AssignedLengths![0].AssignedLength.ToDouble().Should().BeApproximately(0.0, 1e-9);
        result.OverLengths.Should().NotBeNull();
        result.OverLengths.Should().BeEmpty(
            "an over length of 0 does not count, because the baseline uses > 0.0");
    }
}
