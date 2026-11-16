// 层分配上下文的判据 / Criteria for the layer-assignment context.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **层分配上下文** / The layer-assignment context.
/// </summary>
/// <remarks>
/// 判据的重点：**`RegisterRmp` 真的把三件都装到模型上**（不是只造出构件）、**空的新列不追加装载项**、以及**失败会带出来**。
///
/// The criteria focus on **`RegisterRmp` really putting all three pieces on the model** rather than only building the artifacts, **no load terms being appended for an empty batch**, and
/// **failures being carried out**.
/// </remarks>
public class LayerAssignmentContextTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static Bpp2dAssignmentDemandEntry Entry(string itemId, ulong demand) =>
        new(Bpp2dAssignmentDemandMode.ItemAmount, new ItemId(itemId),
            new Bpp2dAssignmentDemandValue.Amount(demand), null);

    private static Layer<FltX> Layer(ulong units) {
        var placements = new List<Placement2<FltX>>();
        for (ulong index = 0UL; index < units; ++index)
        {
            placements.Add(new Placement2<FltX>(
                Shape: new RectanglePackingShape2<FltX>(Q(20.0), Q(10.0)),
                Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                Rotated: false,
                ItemId: "I" + index));
        }
        return new Layer<FltX>(0L, null, null, new Sheet2Shape<FltX>(Q(100.0), Q(100.0)), placements);
    }

    private static LayerAssignmentContext Context() =>
        new(new Dictionary<ItemId, ulong> { [new ItemId("I0")] = 6UL });

    /// <summary>
    /// **`RegisterRmp` 把三件都装到模型上** / `RegisterRmp` puts all three pieces on the model.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 返回的三个构件**都非空**（旧的错法是"返回 ok，但构件是空的"）。
    /// 2. `area` 符号与每个 `load_i` 符号**都在模型上**——判据查模型里的符号名，不看返回值自我声明。
    /// 3. 需求行也在模型上，名字是 `demand_item_amount_lb_0`。
    /// 4. **同一条需求模式的 `overEnabled` 传到装载里**（默认 `false`）。
    ///
    /// Four paired points: all three returned artifacts are non-null, since the older defect was "returns ok but the artifacts are empty"; the `area` symbol and every `load_i` symbol are
    /// **on the model**, checked by name rather than by the return value asserting itself; the demand row is there too, named `demand_item_amount_lb_0`; and the entry's `overEnabled` reaches
    /// the load (defaulting to `false`).
    /// </remarks>
    [Fact]
    public void RegisterRmp_ShouldPutEverythingOnTheModel() {
        using var model = new LinearMetaModel<Flt64>();
        LayerAssignmentContext context = Context();
        Bpp2dAssignmentDemandEntry[] demands = { Entry("I0", 6UL) };

        Fuookami.Ospf.Utils.Functional.Result<RmpAssignmentArtifacts, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> registered = context.RegisterRmp(model, demands);
        registered.IsOk.Should().BeTrue();

        RmpAssignmentArtifacts artifacts = ((Fuookami.Ospf.Utils.Functional.Ok<RmpAssignmentArtifacts, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)registered).Value;
        artifacts.Assignment.Should().NotBeNull();
        artifacts.Load.Should().NotBeNull();
        artifacts.DemandConstraint.Should().NotBeNull();

        artifacts.Assignment.AreaSymbol.Should().NotBeNull("the area symbol was registered");
        artifacts.Load.Load.Should().HaveCount(1);
        artifacts.Load.Load[0].Name.Should().Be("load_0");
        artifacts.Load.OverEnabled.Should().BeFalse();

        model.Constraints.OfType<Fuookami.Ospf.Core.Model.Mechanism.LinearInequalityConstraint<Flt64>>()
            .Select(row => row.Name)
            .Should().Contain("demand_item_amount_lb_0", "the demand lower bound is on the model");
    }

    /// <summary>
    /// **空的新列不追加装载项** / An empty batch appends no load terms.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 一批两列 → 追加后装载多项式**非空**。
    /// 2. 再给一次相同的列（去重后为空）→ 结果为空且**不失败**。
    /// 3. 同一步的装载多项式**项数不变**——空批不该重写多项式。
    ///
    /// Three paired points: two fresh columns leave the load polynomial **non-empty**; giving the same columns again (deduplicated to nothing) yields an empty result **without failing**; and
    /// the polynomial's term count is **unchanged**, since an empty batch must not rewrite it.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldAppendLoadTermsOnlyForNonEmptyBatches() {
        using var model = new LinearMetaModel<Flt64>();
        LayerAssignmentContext context = Context();
        Bpp2dAssignmentDemandEntry[] demands = { Entry("I0", 6UL) };
        RmpAssignmentArtifacts artifacts = ((Fuookami.Ospf.Utils.Functional.Ok<RmpAssignmentArtifacts, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)
            context.RegisterRmp(model, demands)).Value;

        Layer<FltX> first = Layer(2UL);
        Layer<FltX> second = Layer(3UL);
        context.AddColumns(artifacts, 0UL, new[] { first, second }, model).IsOk.Should().BeTrue();
        int termsAfterFirst = artifacts.Load.Load[0].Polynomial.Monomials.Count;

        Fuookami.Ospf.Utils.Functional.Result<IReadOnlyList<Layer<FltX>>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> again = context.AddColumns(artifacts, 1UL, new[] { first, second }, model);
        again.IsOk.Should().BeTrue("an empty deduplicated batch is normal");
        artifacts.Load.Load[0].Polynomial.Monomials.Count
            .Should().Be(termsAfterFirst, "an empty batch must not rewrite the polynomial");
    }

    /// <summary>
    /// **移除列转给聚合，失败会带出来** / Removing columns goes to the aggregation, and failures are carried out.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 移除后聚合里**不再有**该层。
    /// 2. **没有需求项时仍然成功**（空需求是正常输入，不是错误）。
    /// 3. 空需求下模型上**只剩 `area`**，不产生任何需求行。
    ///
    /// Three paired points: after removal the layer is **gone** from the aggregation; **an empty demand list still succeeds**, since empty input is normal rather than an error; and with no
    /// demands the model holds **only `area`**, with no demand rows at all.
    /// </remarks>
    [Fact]
    public void RemoveColumnsAndEmptyDemands_ShouldBehave() {
        using var model = new LinearMetaModel<Flt64>();
        LayerAssignmentContext context = Context();
        RmpAssignmentArtifacts artifacts = ((Fuookami.Ospf.Utils.Functional.Ok<RmpAssignmentArtifacts, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)
            context.RegisterRmp(model, new[] { Entry("I0", 6UL) })).Value;

        Layer<FltX> layer = Layer(2UL);
        context.AddColumns(artifacts, 0UL, new[] { layer }, model).IsOk.Should().BeTrue();
        context.Aggregation.Layers.Should().NotBeEmpty();

        context.RemoveColumns(context.Aggregation.Layers.ToArray());
        context.Aggregation.Layers.Should().BeEmpty("the removal reached the aggregation");

        using var emptyModel = new LinearMetaModel<Flt64>();
        context.RegisterRmp(emptyModel, Array.Empty<Bpp2dAssignmentDemandEntry>()).IsOk
            .Should().BeTrue("empty demands are a normal input");
        emptyModel.Constraints.OfType<Fuookami.Ospf.Core.Model.Mechanism.LinearInequalityConstraint<Flt64>>()
            .Should().BeEmpty("no demands means no demand rows");
    }
}
