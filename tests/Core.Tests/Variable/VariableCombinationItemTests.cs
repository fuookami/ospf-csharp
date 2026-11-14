#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Variable;

public class VariableCombinationItemTests {
    [Fact]
    public void BinVariable1_CanBeCreated() {
        var var = new BinVariable1("x", 5);

        var.Should().NotBeNull();
        var.Name.Should().Be("x");
    }

    [Fact]
    public void BinVariable2_CanBeCreated() {
        var var = new BinVariable2("y", 3, 4);

        var.Should().NotBeNull();
        var.Name.Should().Be("y");
    }

    /// <summary>
    /// **`UIntVariable2` 存在、二维可取、元素是 `UInt64` 且上界可收紧** / `UIntVariable2` exists, indexes in two dimensions, holds `UInt64` and accepts a tightened upper bound.
    /// </summary>
    /// <remarks>
    /// B630 之前 Core 里只有 `UIntVariable1`，二维那张"箱 × 层"矩阵无处落脚。判据钉四件事：
    ///
    /// 1. 按 `[i, j]` 取得到元素，且维度顺序是 `(d1, d2)` 而不是反过来。
    /// 2. 元素个数等于 `d1 * d2`。
    /// 3. 元素的值类型是 `UInt64`（无符号，不是 `Int64`）。
    /// 4. **上界可收紧**：`PreciseAssignment` 靠 `x[i, j].Range.leq(容量)` 把装载量压到容量以下，上界不生效这条类型就没用。
    ///
    /// Before B630 Core only had `UIntVariable1`, leaving the two-dimensional (bin × layer) matrix nowhere to live. The criterion pins four things: indexing by `[i, j]` with the
    /// dimension order `(d1, d2)` rather than reversed; an element count of `d1 * d2`; a `UInt64` element type (unsigned, not `Int64`); and **a tighten-able upper bound**, since
    /// `PreciseAssignment` clamps loads with `x[i, j].Range.leq(capacity)` and the type is useless if that does not take effect.
    /// </remarks>
    [Fact]
    public void UIntVariable2_CanBeCreatedAndBounded() {
        var variable = new UIntVariable2("load", 3, 4);

        variable.Name.Should().Be("load");
        variable.Items.Length.Should().Be(12, "3 by 4 gives twelve entries");
        // **维度顺序必须钉住**：`(3, 4)` 与 `(4, 3)` 的元素个数一样，只有越界访问才分得开——
        // 下标 `[0, 4]` 在 `(3, 4)` 下合法、在 `(4, 3)` 下越界。
        // **The dimension order must be pinned**: `(3, 4)` and `(4, 3)` hold the same number of entries, and only an out-of-range access tells them apart — index `[0, 4]` is valid under
        // `(3, 4)` and out of range under `(4, 3)`.
        variable[0, 4].Should().NotBeNull("the second dimension has length 4");
        variable[2, 3].Should().NotBeNull();
        // 值类型由类声明钉住（`VariableCombination<UInt64, UInteger, Shape2>`），运行期只需盯上界生效。
        // The element type is pinned by the class declaration (`VariableCombination<UInt64, UInteger, Shape2>`); at runtime only the bound taking effect matters.
        // 无符号变量天生有上界（`UInt64` 的最大值），所以判据比的是**收紧后的上界与相邻那格不同**，
        // 而不是"从无界变成有界"。
        // An unsigned variable is bounded by construction (the `UInt64` maximum), so the criterion compares the tightened bound **against the neighbouring entry** rather than
        // asserting "unbounded becomes bounded".
        string? before = variable[1, 1].UpperBound?.ToString();
        variable[1, 1].Range.Leq(new Fuookami.Ospf.Math.Algebra.Number.UInt64(7UL));
        variable[1, 1].UpperBound.Should().NotBeNull(
            "PreciseAssignment clamps the load matrix with exactly this call");
        string after = variable[1, 1].UpperBound!.ToString();
        after.Should().NotBe(before, "the bound must actually tighten");
        variable[0, 0].UpperBound!.ToString().Should().NotBe(after,
            "the bound must not leak onto the whole combination");
    }

    [Fact]
    public void BinVariable3_CanBeCreated() {
        var var = new BinVariable3("z", 2, 3, 4);

        var.Should().NotBeNull();
        var.Name.Should().Be("z");
    }

    [Fact]
    public void BinVariable4_CanBeCreated() {
        var var = new BinVariable4("w", 2, 3, 4, 5);

        var.Should().NotBeNull();
        var.Name.Should().Be("w");
    }

    [Fact]
    public void IntVariable1_CanBeCreated() {
        var var = new IntVariable1("i", 10);

        var.Should().NotBeNull();
        var.Name.Should().Be("i");
    }

    [Fact]
    public void IntVariable2_CanBeCreated() {
        var var = new IntVariable2("j", 5, 6);

        var.Should().NotBeNull();
        var.Name.Should().Be("j");
    }

    [Fact]
    public void RealVariable1_CanBeCreated() {
        var var = new RealVariable1("r", 8);

        var.Should().NotBeNull();
        var.Name.Should().Be("r");
    }

    [Fact]
    public void RealVariable2_CanBeCreated() {
        var var = new RealVariable2("s", 3, 4);

        var.Should().NotBeNull();
        var.Name.Should().Be("s");
    }

    [Fact]
    public void TerVariable1_CanBeCreated() {
        var var = new TerVariable1("t", 6);

        var.Should().NotBeNull();
        var.Name.Should().Be("t");
    }

    [Fact]
    public void UIntVar1_CanBeCreated() {
        var var = new UIntVar1("u", 7);

        var.Should().NotBeNull();
        var.Name.Should().Be("u");
    }

    [Fact]
    public void PctVariable1_CanBeCreated() {
        var var = new PctVariable1("p", 4);

        var.Should().NotBeNull();
        var.Name.Should().Be("p");
    }

    [Fact]
    public void URealVariable1_CanBeCreated() {
        var var = new URealVariable1("ur", 5);

        var.Should().NotBeNull();
        var.Name.Should().Be("ur");
    }

    [Fact]
    public void BTerVariable1_CanBeCreated() {
        var var = new BTerVariable1("bt", 3);

        var.Should().NotBeNull();
        var.Name.Should().Be("bt");
    }

    [Fact]
    public void Variable1_HasCorrectType() {
        var var = new BinVariable1("bin", 5);

        var.Should().BeAssignableTo<Variable1<UInt8, Binary>>();
    }

    [Fact]
    public void Variable1_IntType_HasCorrectType() {
        var var = new IntVariable1("intVar", 5);

        var.Should().BeAssignableTo<Variable1<Int64, Integer>>();
    }

    [Fact]
    public void Variable1_RealType_HasCorrectType() {
        var var = new RealVariable1("realVar", 5);

        var.Should().BeAssignableTo<Variable1<Flt64, Continuous>>();
    }
}
