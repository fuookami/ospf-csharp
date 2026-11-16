// yield 提取的空列表判据 / Criteria for the empty-list semantics of yield extraction.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Domain.Yield;

/// <summary>
/// **"没有欠产"是空列表，不是 null** / "No deficit" is an empty list, not null.
/// </summary>
/// <remarks>
/// 发现的偏离**：`YieldModelingResult` 的两个字段应当是 `List<...> = emptyList`——
/// **永不为 null**。这里原本是 `Count > 0 ? list : null`，把空列表变成了 null。
///
/// 后果：调用方拿到 null 只能猜是"没有"还是"没算"。
/// **这正是 在余料代理那里刚批评过的同一类混淆**（"没有数据"与"零浪费"必须分开），
/// **方向相反，错误相同**。
///
/// **the earlier deviation**: its two fields are `List<...> = emptyList` — **never null**. C# used `Count > 0 ? list : null`. Callers receiving null can only guess
/// between "none" and "not computed" — **the same conflation criticised one batch earlier** in the rest-material proxy. **Opposite direction, same mistake.**
/// </remarks>
public class YieldEmptyListTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Product<Flt64> MakeProduct(string id) =>
        new(id: id, name: id, width: new List<Quantity<Flt64>> { Millimetres(60.0) });

    private static (YieldAggregation Aggregation, LinearMetaModel<Flt64> Model, IReadOnlyList<ProductDemand<Flt64>> Demands) MakeYield() {
        var demands = new List<ProductDemand<Flt64>> {
            new(MakeProduct("A"), Millimetres(60.0)),
        };
        var config = new YieldModelingConfig<Flt64>(
            UnderProductionPenalty: new Dictionary<ProductDemandShadowPriceKey, Flt64> {
                [new ProductDemandShadowPriceKey("A", "mm")] = new Flt64(1.0),
            },
            OverProductionPenalty: new Dictionary<ProductDemandShadowPriceKey, Flt64> {
                [new ProductDemandShadowPriceKey("A", "mm")] = new Flt64(1.0),
            });
        var aggregation = new YieldAggregation(
            Config: config,
            Demands: demands,
            NeedsOverSlackForOverArea: false);
        var model = new LinearMetaModel<Flt64>();
        aggregation.Register(model);
        return (aggregation, model, demands);
    }

    /// <summary>**解全为零时两个列表都非 null，且为空** / With an all-zero solution both lists are non-null and empty.</summary>
    /// <remarks>
    /// 断言**非 null 且空**，而不是"没有欠产"。前者能区分 `[]` 与 `null`，后者不能。
    ///
    /// Asserts **non-null and empty**, not "no deficit" — the former distinguishes `[]` from `null`, the latter cannot.
    /// </remarks>
    [Fact]
    public void ExtractResult_ShouldReturnEmptyListsRatherThanNull() {
        (YieldAggregation aggregation, LinearMetaModel<Flt64> model, _) = MakeYield();
        // 不给任何变量设值：`DoubleResult` 为 null，两个循环都不追加。
        // Assign no variable values: `DoubleResult` stays null and neither loop appends.

        YieldModelingResult<Flt64>? result = aggregation.ExtractResult(model);

        result.Should().NotBeNull();
        result!.UnderProductions.Should().NotBeNull(
            "the baseline defaults to emptyList(), so 'no deficit' is [] and never null");
        result.OverProductions.Should().NotBeNull();
        result.UnderProductions.Should().BeEmpty();
        result.OverProductions.Should().BeEmpty();
    }

    /// <summary>**两个字段的静态类型就是非空**——这条钉住"契约"，不是"运行时行为" / Both fields are statically non-null — this pins the contract, not the runtime behaviour.</summary>
    /// <remarks>
    /// 只改了**运行时行为**（返回列表本身），字段类型仍是 `IReadOnlyList<...>?`，
    /// 所以下游必须写 `?? []`。**类型签名允许 null 就是允许下一个人再写出那个 bug。**
    ///
    /// 这里把类型改成非空。这条判据用**反射**确认字段的静态类型不可为空——
    /// 若有人改回 `?`，它会红，而运行时判据**不会**。
    ///
    /// fixed only the **runtime behaviour** (return the lists themselves) while the field types stayed `IReadOnlyList<...>?`, so downstream had to write `?? []`.
 /// **A nullable signature is an invitation for the next person to reintroduce the bug.** makes the types non-null, and this criterion uses **reflection**
    /// to confirm the static type is non-nullable — a revert to `?` fails here while the runtime criteria stay green.
    /// </remarks>
    [Fact]
    public void YieldModelingResult_ShouldDeclareNonNullListFields() {
        global::System.Reflection.PropertyInfo[] properties =
            global::System.Array.FindAll(
                typeof(YieldModelingResult<Flt64>).GetProperties(),
                p => p.Name is "UnderProductions" or "OverProductions");

        properties.Should().HaveCount(2);
        foreach (global::System.Reflection.PropertyInfo property in properties) {
            global::System.Type propertyType = property.PropertyType;
            bool isNullableList = !propertyType.IsValueType
                && new global::System.Reflection.NullabilityInfoContext()
                    .Create(property).ReadState
                    == global::System.Reflection.NullabilityState.Nullable;
            isNullableList.Should().BeFalse(
                $"{property.Name} must be declared non-nullable so callers need no `?? []`");
        }
    }

    /// <summary>**有欠产时列表非空且金额正确**——反向对照 / With a deficit the list is non-empty and the amount is right — the reverse control.</summary>
    /// <remarks>
    /// 少了这条，"**永远返回空列表**"的实现也会绿。
    ///
    /// Without this, an implementation that **always** returns empty lists would also pass.
    /// </remarks>
    [Fact]
    public void ExtractResult_ShouldPopulateTheUnderProductionList() {
        (YieldAggregation aggregation, LinearMetaModel<Flt64> model, IReadOnlyList<ProductDemand<Flt64>> demands) = MakeYield();
        URealVar underVar = aggregation.UnderProduction.First()!;
        model.Tokens.Find(underVar)!.SetResult(new Flt64(7.0));

        YieldModelingResult<Flt64>? result = aggregation.ExtractResult(model);

        result.Should().NotBeNull();
        result!.UnderProductions.Should().HaveCount(1);
        result.UnderProductions![0].ProductId.Should().Be(demands[0].Product.Id);
        result.UnderProductions[0].Amount.ToDouble().Should().BeApproximately(
            7.0,
            1e-9,
            "the under-production variable carries 7");
        result.OverProductions.Should().NotBeNull();
        result.OverProductions.Should().BeEmpty("nothing set the over-production variable");
    }
}
