// 圆柱半径选择结果的判据 / Criteria for the cylinder radius selection.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain;

/// <summary>
/// **圆柱半径选择结果** / The cylinder radius selection.
/// </summary>
/// <remarks>
/// 判据的重点：**两个体积各用各自半径平方**、**四条不变量真的会拒**（不是只写了一行 `require` 没人验）。
///
/// The criteria focus on **each volume helper using its own squared radius**, and on **the four invariants actually rejecting bad input** rather than being `require` lines nobody checks.
/// </remarks>
public class CylinderRadiusSelectionTests {
    private static FltX F(double value) => new(value);
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static PWLRadiusSelectionMetadata Metadata(
        double solverSquared = 100.0,
        double actualSquared = 110.0,
        int segments = 4,
        string source = "pwl") =>
        new(F(solverSquared), F(actualSquared), F(10.0), F(0.05), F(0.08), segments, true, source);

    /// <summary>
    /// **真实体积与模型以为的体积分开** / The actual volume and the model's belief stay apart.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. `ActualVolume(2, 3)` 用**实际**半径平方 `110` → `3 × 110 × 2 = 660`。
    /// 2. `PwlVolume(2, 3)` 用**求解器**那个 `100` → `600`。
    /// 3. 两者之差正是逼近带来的偏差（`60`），判据钉住"它们不是同一个数"。
    ///
    /// Three paired points: `ActualVolume(2, 3)` uses the **actual** squared radius `110`, giving `3 × 110 × 2 = 660`; `PwlVolume(2, 3)` uses the **solver's** `100`, giving `600`; and their
    /// difference is exactly the approximation bias (`60`), the criterion pinning that they are not the same number.
    /// </remarks>
    [Fact]
    public void Volumes_ShouldUseTheirOwnRadiusSquared() {
        PWLRadiusSelectionMetadata metadata = Metadata();
        metadata.ActualVolume(F(2.0), F(3.0)).ToFlt64().ToDouble().Should().BeApproximately(660.0, 1e-9);
        metadata.PwlVolume(F(2.0), F(3.0)).ToFlt64().ToDouble().Should().BeApproximately(600.0, 1e-9);
    }

    /// <summary>
    /// **不变量真的会拒** / The invariants genuinely reject.
    /// </summary>
    /// <remarks>
    /// 五点成对：
    ///
    /// 1. 合法的元数据**通过**校验。
    /// 2. 段数 `0` → 拒。
    /// 3. 来源空白 → 拒。
    /// 4. 结果键为空白 → 拒；选中半径为 `0` → 拒。
    /// 5. 合法的结果（带元数据）**通过**，且元数据的前置校验会被连带执行——元数据段数为 0 时结果也拒。
    ///
    /// Five paired points: valid metadata **passes**; a zero segment count **is rejected**; a blank source **is rejected**; a blank result key and a zero selected radius **are rejected**; and a
    /// valid result carrying metadata **passes**, with the metadata's own validation running through it — a zero-segment metadata makes the result reject too.
    /// </remarks>
    [Fact]
    public void Invariants_ShouldReject() {
        Metadata().Invoking(m => m.Validate()).Should().NotThrow();

        Metadata(segments: 0).Invoking(m => m.Validate()).Should().Throw<ArgumentOutOfRangeException>();
        Metadata(source: "  ").Invoking(m => m.Validate()).Should().Throw<ArgumentException>();

        var valid = new CylinderRadiusSelectionResult("k1", Q(10.0), Axis3.Z, Q(5.0), Q(20.0), "r1", "pwl", Metadata());
        valid.Invoking(r => r.Validate()).Should().NotThrow();

        CylinderRadiusSelectionResult blankKey = valid with { Key = " " };
        blankKey.Invoking(r => r.Validate()).Should().Throw<ArgumentException>();

        CylinderRadiusSelectionResult zeroRadius = valid with { SelectedRadius = Q(0.0) };
        zeroRadius.Invoking(r => r.Validate()).Should().Throw<ArgumentOutOfRangeException>();

        CylinderRadiusSelectionResult badMetadata = valid with { PwlMetadata = Metadata(segments: 0) };
        badMetadata.Invoking(r => r.Validate()).Should().Throw<ArgumentOutOfRangeException>();
    }
}
