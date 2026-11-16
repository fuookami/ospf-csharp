// 变量适配器的判据 / Criteria for the variable adapter.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Framework.Model.Symbol;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Model.Symbol;

/// <summary>
/// **变量适配器** / The variable adapter.
/// </summary>
/// <remarks>
/// 判据的重点：**按引用相等判定**——两只"值相等"的变量（同类型、同名、同上下界）必须被区分开。
/// 这一条是这个类的全部意义，按值相等做就等于把别人的别名认成自己的。
///
/// The criteria focus on **identity comparison**: two "value-equal" variables (same type, same name, same bounds) must stay distinguishable. That is this class's whole point, and value equality would
/// mistake someone else's alias for one's own.
/// </remarks>
public class VariableAdapterTests {
    /// <summary>
    /// **拉出内层/外层视角，且带着适配器的引用** / The inner and outer views come out carrying the adapter's reference.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. `InnerVariable` 的变量**就是内层那只**（引用相等），不是副本。
    /// 2. `OuterVariable` 同理。
    /// 3. 两个视角带的 `Adapter` **是同一个适配器实例**——不然视角与适配器会在下游被配错。
    ///
    /// Three paired points: `InnerVariable`'s variable **is the inner one** (reference-equal) rather than a copy, `OuterVariable` likewise, and both views carry **the same adapter instance**,
    /// which stops a view and its adapter from being mismatched downstream.
    /// </remarks>
    [Fact]
    public void Views_ShouldCarryTheSameAdapter() {
        var inner = new PctVar("x");
        var outer = new PctVar("y");
        var adapter = new VariableAdapter<Flt64, Percentage>(inner, outer);

        VariableAdapter<Flt64, Percentage>.InternalVariable innerView = adapter.InnerVariable();
        VariableAdapter<Flt64, Percentage>.AdapterVariable outerView = adapter.OuterVariable();

        ReferenceEquals(innerView.Variable, inner).Should().BeTrue("the inner view wraps the inner variable itself");
        ReferenceEquals(outerView.Variable, outer).Should().BeTrue("the outer view wraps the outer variable itself");
        ReferenceEquals(innerView.Adapter, adapter).Should().BeTrue();
        ReferenceEquals(outerView.Adapter, adapter).Should().BeTrue();
    }

    /// <summary>
    /// **按引用相等找适配器，值相等不算** / Find an adapter by identity, never by value equality.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 内层变量查得到自己的适配器（引用相等）。
    /// 2. 外层变量也查得到**同一个**。
    /// 3. **同名同类型的另一只变量查不到**——它的名字与内层完全一样，值相等会误命中；引用相等不会。
    /// 4. 候选为空时返回 `null`，不是抛异常。
    ///
    /// Four paired points: the inner variable finds its own adapter (by identity); the outer one finds **the same** adapter; **a different variable with the same name and type finds nothing**, since its
    /// name matches the inner one exactly and value equality would falsely hit while identity does not; and an empty candidate set returns `null` rather than throwing.
    /// </remarks>
    [Fact]
    public void Lookup_ShouldUseIdentityNotValueEquality() {
        var inner = new PctVar("x");
        var outer = new PctVar("y");
        var adapter = new VariableAdapter<Flt64, Percentage>(inner, outer);

        ReferenceEquals(inner.Adapter(adapter), adapter).Should().BeTrue("the inner variable belongs to it");
        ReferenceEquals(outer.Adapter(adapter), adapter).Should().BeTrue("and so does the outer one");

        var lookalike = new PctVar("x");
        lookalike.Adapter(adapter).Should().BeNull(
            "an identically named, identically typed variable is a different variable, so it must not match");

        // **记档**：`AbstractVariableItem` 是 **class** 而不是 record，所以 `==` 本身就是引用相等——
        // 这条"必须按引用相等"的判据在变异上不可观测（把 `ReferenceEquals` 改成 `==` 是 no-op）。
        // 它的价值在于把意图钉在文档里：**一旦这些类型改成 record，这条就会立刻生效**。
        // **Recorded**: `AbstractVariableItem` is a **class**, not a record, so `==` already means reference equality; this criterion is therefore unobservable under mutation (swapping
        // `ReferenceEquals` for `==` is a no-op). Its value is pinning the intent in the documentation: **the moment these types become records, it starts biting**.

        // **列表重载也要真找**：只断言"空列表返回 null"测不到它有没有在看候选。
        // **The list overload must actually search**: asserting only that an empty list returns null cannot tell whether it looks at the candidates at all.
        ReferenceEquals(inner.Adapter(new List<VariableAdapter<Flt64, Percentage>> { adapter }), adapter)
            .Should().BeTrue("the list overload searches the candidates rather than returning null outright");
        inner.Adapter(new List<VariableAdapter<Flt64, Percentage>>()).Should().BeNull("no candidates means no answer");
    }
}
