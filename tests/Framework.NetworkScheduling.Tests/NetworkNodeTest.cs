// 网络调度基础设施测试 / Tests for network-scheduling infrastructure.

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
/// 网络调度基础设施 / Network-scheduling infrastructure.
/// </summary>
public sealed class NetworkNodeTest {
    /// <summary>有效标识构造成功 / A valid identifier constructs successfully.</summary>
    [Fact]
    public void Invoke_ShouldBuildNode() =>
        NetworkNode<FltX>.Invoke(new NetworkNodeId("n1")).IsOk.Should().BeTrue();

    /// <summary>
    /// **空标识被拒绝**：节点标识是图的寻址键，空标识会让多个节点互相顶替，且错误在后续求最短路时表现为"路径凭空缩短"。
    /// **A blank identifier is rejected**: the identifier is the graph's addressing key, and a blank one lets nodes overwrite
    /// each other — an error surfacing later as "paths that are mysteriously shorter".
    /// </summary>
    [Fact]
    public void Invoke_ShouldRejectBlankId() {
        NetworkNode<FltX>.Invoke(new NetworkNodeId("   ")).IsOk.Should().BeFalse();
        NetworkNode<FltX>.Invoke(new NetworkNodeId(string.Empty)).IsOk.Should().BeFalse();
    }

    /// <summary>属性可为空 / Attributes may be omitted.</summary>
    [Fact]
    public void Invoke_ShouldDefaultToNoAttributes() =>
        NetworkNode<FltX>.Invoke(new NetworkNodeId("n1")).Value.Attributes.Should().BeEmpty();

    /// <summary>属性被保留 / Attributes are kept.</summary>
    [Fact]
    public void Invoke_ShouldKeepAttributes() {
        var attributes = new Dictionary<string, Quantity<FltX>> { ["weight"] = Meters(3) };

        NetworkNode<FltX>.Invoke(new NetworkNodeId("n1"), attributes).Value
            .Attributes["weight"].Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>
    /// **属性在构造时复制**：调用方后续修改传入字典不会悄悄改掉节点属性。
    /// **Attributes are copied at construction**: later mutation of the caller's dictionary cannot silently change them.
    /// </summary>
    [Fact]
    public void Invoke_ShouldCopyAttributes() {
        var attributes = new Dictionary<string, Quantity<FltX>> { ["weight"] = Meters(3) };
        NetworkNode<FltX> node = NetworkNode<FltX>.Invoke(new NetworkNodeId("n1"), attributes).Value;

        attributes["weight"] = Meters(9);

        node.Attributes["weight"].Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>节点保留标识 / The node keeps its identifier.</summary>
    [Fact]
    public void Node_ShouldKeepId() =>
        NetworkNode<FltX>.Invoke(new NetworkNodeId("n1")).Value.Id.Value.Should().Be("n1");

    /// <summary>节点标识按序比较 / Node identifiers compare ordinally.</summary>
    [Fact]
    public void NodeId_ShouldOrder() {
        var ids = new List<NetworkNodeId> { new("c"), new("a"), new("b") };
        ids.Sort();

        ids.Select(id => id.Value).Should().Equal(new[] { "a", "b", "c" });
    }

    /// <summary>同值标识相等 / Identifiers with equal values are equal.</summary>
    [Fact]
    public void NodeId_ShouldEqualOnValue() =>
        new NetworkNodeId("a").Equals(new NetworkNodeId("a")).Should().BeTrue();

    /// <summary>
    /// **负成本是合法语义**：补贴、回收、逆向流量都表现为负成本，把负数当错误会让这类建模无法表达。
    /// **A negative cost is legitimate**: subsidies, recycling and reverse flows all show up as negative costs, and rejecting
    /// them would make such models inexpressible.
    /// </summary>
    [Fact]
    public void Cost_ShouldAllowNegative() =>
        NetworkCost<FltX>.Invoke(Meters(-5)).Value.Quantity.Value.ToFlt64().ToDouble()
            .Should().BeApproximately(-5.0, 1e-9);

    /// <summary>成本保留量值 / The cost keeps its quantity.</summary>
    [Fact]
    public void Cost_ShouldKeepQuantity() =>
        NetworkCost<FltX>.Invoke(Meters(5)).Value.Quantity.Value.ToFlt64().ToDouble()
            .Should().BeApproximately(5.0, 1e-9);

    /// <summary>零成本合法 / A zero cost is legal.</summary>
    [Fact]
    public void Cost_ShouldAllowZero() =>
        NetworkCost<FltX>.Invoke(Meters(0)).IsOk.Should().BeTrue();

    /// <summary>失败构造带非法参数错误码 / The failure builder uses the illegal-argument code.</summary>
    [Fact]
    public void Failure_ShouldCarryIllegalArgument() {
        Result<int, ErrorCode, Error<ErrorCode>> result = NetworkSchedulingErrors.Failure<int>("boom");

        result.IsOk.Should().BeFalse();
        ((Failed<int, ErrorCode, Error<ErrorCode>>)result).Error.Code.Should().Be(ErrorCode.IllegalArgument);
        ((Failed<int, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Be("boom");
    }

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
