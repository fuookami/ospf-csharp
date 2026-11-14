// 一维无符号整数变量组合测试 / Tests for the 1D unsigned integer variable combination.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.MultiArray;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 一维无符号整数变量组合 / The 1D unsigned integer variable combination.
/// </summary>
/// <remarks>
/// 本测试类位于 `Framework.NetworkScheduling.Tests`，刻意不引用 `Framework.Csp1d`：它同时充当"该类型归 Core 所有"的可执行证据——
/// 任何需要的域都能通过 Core 拿到它，而不必反向依赖某个应用模块。
/// This test class lives in `Framework.NetworkScheduling.Tests` and deliberately does not reference `Framework.Csp1d`: it doubles as
/// executable evidence that Core owns the type, so any domain that needs it reaches it through Core instead of depending backwards on
/// an application module.
/// </remarks>
public sealed class UIntVariable1Test {
    /// <summary>按形状创建出对应数量的变量 / The declared shape yields that many variables.</summary>
    [Fact]
    public void Shape_ShouldDecideVariableCount() {
        var group = new UIntVariable1("x", Shape1.Invoke(3));

        group.Items.Should().HaveCount(3);
    }

    /// <summary>索引访问取到同一批变量 / Indexer access reaches the same variables.</summary>
    [Fact]
    public void Indexer_ShouldReachTheSameVariables() {
        var group = new UIntVariable1("x", Shape1.Invoke(2));

        group[0].Should().BeSameAs(group.Items[0]);
        group[1].Should().BeSameAs(group.Items[1]);
    }

    /// <summary>可枚举全部变量 / All variables are enumerable.</summary>
    [Fact]
    public void Enumerator_ShouldYieldEveryVariable() {
        var group = new UIntVariable1("x", Shape1.Invoke(4));

        var enumerated = new List<IVariableItem>();
        foreach (IVariableItem item in group) {
            enumerated.Add(item);
        }

        enumerated.Should().HaveCount(4);
        enumerated.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.Name));
    }

    /// <summary>
    /// **整组可一次性注册进元模型**：逐个添加会漏掉任何一个，而变量组合存在的意义正是"一组变量一起进模型"。
    /// **The whole group registers into a meta model at once**: adding one by one can miss one, and registering as a group is exactly
    /// why the combination type exists.
    /// </summary>
    [Fact]
    public void Add_ShouldRegisterEveryVariableAtOnce() {
        var model = new LinearMetaModel<Flt64>("variables");
        var group = new UIntVariable1("x", Shape1.Invoke(3));

        model.Add((IEnumerable<IVariableItem>)group).IsOk.Should().BeTrue();

        model.Tokens.Tokens.Should().HaveCount(3);
    }

    /// <summary>变量名来自组合前缀与下标 / Variable names come from the group prefix and index.</summary>
    [Fact]
    public void Variables_ShouldCarryGroupPrefix() {
        var group = new UIntVariable1("plan", Shape1.Invoke(2));

        group.Items.Select(item => item.Name).Should().OnlyContain(name => name.Contains("plan", System.StringComparison.Ordinal));
    }
}
