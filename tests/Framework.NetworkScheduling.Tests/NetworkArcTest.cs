// 网络弧、容量、流量、供需与容量上下界测试 / Tests for arcs, capacity, flow, balance and capacity bounds.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 网络弧、容量、流量、供需与容量上下界 / Arcs, capacity, flow, balance and capacity bounds.
/// </summary>
public sealed class NetworkArcTest {
    /// <summary>正容量合法 / A positive capacity is legal.</summary>
    [Fact]
    public void Capacity_ShouldAcceptPositive() =>
        Capacity<FltX>.Invoke(Meters(5)).IsOk.Should().BeTrue();

    /// <summary>
    /// **零容量合法**：它表示"这条边不可用"，是一个有意义的建模手段。若把零拒掉，调用方只能删边，而删边会改变拓扑、让节点
    /// 编号与下游引用错位。
    /// **A zero capacity is legal**: it means "this edge is unusable", a meaningful device. Rejecting zero would force
    /// deleting the edge, which changes topology and misaligns node numbering against downstream references.
    /// </summary>
    [Fact]
    public void Capacity_ShouldAcceptZero() =>
        Capacity<FltX>.Invoke(Meters(0)).IsOk.Should().BeTrue();

    /// <summary>负容量被拒绝 / A negative capacity is rejected.</summary>
    [Fact]
    public void Capacity_ShouldRejectNegative() =>
        Capacity<FltX>.Invoke(Meters(-1)).IsOk.Should().BeFalse();

    /// <summary>正流量合法 / A positive flow is legal.</summary>
    [Fact]
    public void Flow_ShouldAcceptPositive() =>
        Flow<FltX>.Invoke(Meters(5)).IsOk.Should().BeTrue();

    /// <summary>零流量合法（边未被使用）/ A zero flow is legal (the edge is unused).</summary>
    [Fact]
    public void Flow_ShouldAcceptZero() =>
        Flow<FltX>.Invoke(Meters(0)).IsOk.Should().BeTrue();

    /// <summary>
    /// **负流量被拒绝**：负流量属于反向边的语义，应显式建模为一条反向弧，而不是让同一条弧取负值。
    /// **A negative flow is rejected**: that belongs to a reverse edge and should be modelled as one explicitly rather than
    /// letting the same arc go negative.
    /// </summary>
    [Fact]
    public void Flow_ShouldRejectNegative() =>
        Flow<FltX>.Invoke(Meters(-1)).IsOk.Should().BeFalse();

    /// <summary>
    /// **净供需量允许任意符号**：正值供给、负值需求。拆成两个字段会让下游每次都要判断哪一侧为零。
    /// **A balance allows either sign**: positive is supply, negative is demand. Splitting into two fields would force
    /// downstream to test which side is zero every time.
    /// </summary>
    [Fact]
    public void Balance_ShouldAllowEitherSign() {
        NodeBalance<FltX>.Invoke(Meters(5)).Value.Net.Value.ToFlt64().ToDouble().Should().BeApproximately(5.0, 1e-9);
        NodeBalance<FltX>.Invoke(Meters(-5)).Value.Net.Value.ToFlt64().ToDouble().Should().BeApproximately(-5.0, 1e-9);
    }

    /// <summary>合法上下界通过 / Valid bounds pass.</summary>
    [Fact]
    public void Bounds_ShouldAcceptOrdered() =>
        CapacityBounds<FltX>.Invoke(Meters(1), Meters(5)).IsOk.Should().BeTrue();

    /// <summary>上下界相等合法 / Equal bounds are legal.</summary>
    [Fact]
    public void Bounds_ShouldAcceptEqual() =>
        CapacityBounds<FltX>.Invoke(Meters(5), Meters(5)).IsOk.Should().BeTrue();

    /// <summary>下界大于上界被拒绝 / A lower bound above the upper bound is rejected.</summary>
    [Fact]
    public void Bounds_ShouldRejectInverted() =>
        CapacityBounds<FltX>.Invoke(Meters(5), Meters(1)).IsOk.Should().BeFalse();

    /// <summary>负界被拒绝 / A negative bound is rejected.</summary>
    [Fact]
    public void Bounds_ShouldRejectNegative() =>
        CapacityBounds<FltX>.Invoke(Meters(-1), Meters(5)).IsOk.Should().BeFalse();

    /// <summary>
    /// **量纲不一致先被拦住**：若上下界量纲不同，后续所有数值比较都失去意义，先比数值会得到"看起来合理"的错误结论。
    /// **A dimension mismatch is caught first**: with mismatched dimensions every later comparison is meaningless, and
    /// comparing values first yields a plausible-looking wrong conclusion.
    /// </summary>
    [Fact]
    public void Bounds_ShouldRejectDimensionMismatch() =>
        CapacityBounds<FltX>.Invoke(Meters(1), Seconds(5)).IsOk.Should().BeFalse();

    /// <summary>
    /// **不同但可换算的单位合法，且上界按原单位保存**：若存换算后的值，调用方拿到的 `Upper` 单位会与他传入的不同。
    /// **Different yet convertible units are legal, and the upper bound keeps its original unit**: storing the converted
    /// value would hand callers an `Upper` in a unit other than what they passed.
    /// </summary>
    [Fact]
    public void Bounds_ShouldKeepOriginalUpperUnit() {
        Result<CapacityBounds<FltX>, ErrorCode, Error<ErrorCode>> result =
            CapacityBounds<FltX>.Invoke(Meters(1), Centimeters(500));

        result.IsOk.Should().BeTrue();
        result.Value.Upper.Unit.Should().Be(LengthUnits.Centimeter, "上界按原单位保存 / the upper bound keeps its unit");
        result.Value.Lower.Unit.Should().Be(SIBaseUnits.Meter);
    }

    /// <summary>弧构造成功并保留字段 / An arc constructs and keeps its fields.</summary>
    [Fact]
    public void Arc_ShouldBuild() {
        NetworkArc<FltX> arc = NetworkArc<FltX>
            .Invoke(new NetworkNodeId("a"), new NetworkNodeId("b"), Meters(3)).Value;

        arc.From.Value.Should().Be("a");
        arc.To.Value.Should().Be("b");
        arc.Cost.Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>空标识被拒绝 / A blank identifier is rejected.</summary>
    [Fact]
    public void Arc_ShouldRejectBlankId() =>
        NetworkArc<FltX>.Invoke(
            new NetworkNodeId("a"), new NetworkNodeId("b"), Meters(3),
            id: new NetworkArcId("  ")).IsOk.Should().BeFalse();

    /// <summary>
    /// **默认标识带长度前缀**：直接拼 `from + to` 会让 `("ab","c")` 与 `("a","bc")` 撞成同一个 `"abc"`，两条不同的弧在
    /// 字典里互相顶替。
    /// **The default identifier carries length prefixes**: a plain `from + to` collides `("ab","c")` with `("a","bc")` on
    /// `"abc"`, letting two different arcs overwrite each other in a dictionary.
    /// </summary>
    [Fact]
    public void Arc_ShouldDisambiguateDefaultId() {
        NetworkArc<FltX> first = NetworkArc<FltX>
            .Invoke(new NetworkNodeId("ab"), new NetworkNodeId("c"), Meters(1)).Value;
        NetworkArc<FltX> second = NetworkArc<FltX>
            .Invoke(new NetworkNodeId("a"), new NetworkNodeId("bc"), Meters(1)).Value;

        first.Id.Should().NotBe(second.Id, "长度前缀让拼接可逆 / length prefixes make the concatenation reversible");
        first.Id.Value.Should().Be("2:ab1:c");
    }

    /// <summary>显式标识覆盖默认值 / An explicit identifier overrides the default.</summary>
    [Fact]
    public void Arc_ShouldHonorExplicitId() =>
        NetworkArc<FltX>.Invoke(
            new NetworkNodeId("a"), new NetworkNodeId("b"), Meters(1),
            id: new NetworkArcId("parallel-1")).Value.Id.Value.Should().Be("parallel-1");

    /// <summary>属性默认空 / Attributes default to empty.</summary>
    [Fact]
    public void Arc_ShouldDefaultToNoAttributes() =>
        NetworkArc<FltX>.Invoke(new NetworkNodeId("a"), new NetworkNodeId("b"), Meters(1)).Value
            .Attributes.Should().BeEmpty();

    /// <summary>属性在构造时复制 / Attributes are copied at construction.</summary>
    [Fact]
    public void Arc_ShouldCopyAttributes() {
        var attributes = new Dictionary<string, Quantity<FltX>> { ["w"] = Meters(2) };
        NetworkArc<FltX> arc = NetworkArc<FltX>
            .Invoke(new NetworkNodeId("a"), new NetworkNodeId("b"), Meters(1), attributes).Value;

        attributes["w"] = Meters(9);

        arc.Attributes["w"].Value.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-9);
    }

    /// <summary>弧标识按序比较 / Arc identifiers compare ordinally.</summary>
    [Fact]
    public void ArcId_ShouldOrder() {
        var ids = new List<NetworkArcId> { new("c"), new("a") };
        ids.Sort();

        ids.Select(id => id.Value).Should().Equal(new[] { "a", "c" });
    }

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>构造以厘米为单位的量 / Build a quantity in centimetres.</summary>
    private static Quantity<FltX> Centimeters(double value) => new(new FltX(value), LengthUnits.Centimeter);

    /// <summary>构造以秒为单位的量（用于量纲不匹配用例）/ Build a quantity in seconds, for the dimension-mismatch case.</summary>
    private static Quantity<FltX> Seconds(double value) => new(new FltX(value), SIBaseUnits.Second);
}
