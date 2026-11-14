// 连续半径选择装配（B682）的判据 / Criteria for the continuous radius selection assembly (B682).

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain;

/// <summary>
/// **连续半径选择装配（B682）** / The continuous radius selection assembly (B682).
/// </summary>
/// <remarks>
/// 判据的重点：**缺半径跳过、缺误差按 0**（两类缺失处理不同）、**求解结果里没有的变量名跳过**、
/// **结果的键取自原型的 `RadiusWeightFunctionKey`**、**没有键就抛**。
///
/// The criteria focus on **a missing radius skipping while missing errors default to zero** (the two kinds of absence differing), on **names absent from the solver results being skipped**, on **the result
/// key coming from the prototype's `RadiusWeightFunctionKey`**, and on **throwing when that key is absent**.
/// </remarks>
public class ContinuousRadiusSelectionExtractorTests {
    private static FltX F(double value) => new(value);
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static ContinuousCylinderRadiusSolverPrototype Prototype(string variable, string? key = "w1") =>
        new(Source: "src", VariableName: variable, Axis: Axis3.Z,
            RadiusMin: Q(1.0), RadiusMax: Q(20.0), RadiusWeightFunctionKey: key);

    /// <summary>
    /// **原生路径：有结果才产出，键取自原型** / Native path: a result is produced only when the solver has one, keyed from the prototype.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 两个原型里只有一个在求解结果中 → **只产出一条**（另一个跳过，而不是造零半径结果）。
    /// 2. 键是原型的 `RadiusWeightFunctionKey`（`w1`），不是变量名。
    /// 3. 半径与上下界原样带出。
    /// 4. 求解结果为空 → 空列表，不抛。
    ///
    /// Four paired points: of two prototypes only one appearing in the solver results yields **exactly one selection** (the other skipped rather than given a zero radius); the key is the prototype's
    /// `RadiusWeightFunctionKey` (`w1`) rather than the variable name; the radius and bounds pass through; and empty solver results yield an empty list rather than throwing.
    /// </remarks>
    [Fact]
    public void BuildNative_ShouldSkipMissingAndKeyFromThePrototype() {
        var prototypes = new List<ContinuousCylinderRadiusSolverPrototype> {
            Prototype("r1", key: "w1"),
            Prototype("r2", key: "w2"),
        };
        var results = new Dictionary<string, FltX> { ["r1"] = F(7.5) };

        IReadOnlyList<CylinderRadiusSelectionResult> selections =
            ContinuousRadiusSelectionExtractor.BuildNativeResults(prototypes, results);

        selections.Should().ContainSingle("r2 has no solver result and must be skipped, not defaulted to zero");
        selections[0].Key.Should().Be("w1", "the key comes from the prototype's weight-function key");
        selections[0].VariableName.Should().Be("r1");
        selections[0].SelectedRadius.Value.ToFlt64().ToDouble().Should().BeApproximately(7.5, 1e-9);
        selections[0].PwlMetadata.Should().BeNull("the native path carries no piecewise metadata");

        ContinuousRadiusSelectionExtractor
            .BuildNativeResults(prototypes, new Dictionary<string, FltX>())
            .Should().BeEmpty();
    }

    /// <summary>
    /// **PWL 路径：缺半径跳过，缺误差按 0** / PWL path: a missing radius skips, missing errors default to zero.
    /// </summary>
    /// <remarks>
    /// 五点成对：
    ///
    /// 1. 缺 `solverRadius` 的条目**跳过**（没有半径就没有结果）。
    /// 2. 缺 `pwlAbsoluteError` / `pwlRelativeError` / `maxPWLRelativeError` / `numSegments` 的条目**照样产出**，
    ///    误差为 **0**、段数为 **1**——误差可以缺省为零，半径不能。
    /// 3. `isWithinEnvelope` 用 **> 0.5** 判定：`0.6` → true、`0.4` → false。
    /// 4. 原型里没有的变量名**跳过**。
    /// 5. 空输入 → 空列表。
    ///
    /// Five paired points: an entry lacking `solverRadius` **is skipped**, since no radius means no result; an entry lacking the error fields and `numSegments` **still yields a result** with
    /// **zero** errors and **one** segment, because an error may default but a radius may not; `isWithinEnvelope` is judged by **> 0.5**, so `0.6` is true and `0.4` false; names with no matching
    /// prototype **are skipped**; and empty input yields an empty list.
    /// </remarks>
    [Fact]
    public void BuildPwl_ShouldSkipMissingRadiusButDefaultTheErrors() {
        var prototypes = new List<ContinuousCylinderRadiusSolverPrototype> {
            Prototype("r1"), Prototype("r2"), Prototype("r3"), Prototype("r4"), Prototype("r5"),
        };
        var none = new Dictionary<string, FltX>();
        var pwl = new Dictionary<string, IReadOnlyDictionary<string, FltX>> {
            ["r1"] = none,
            ["r2"] = new Dictionary<string, FltX> {
                ["solverRadius"] = F(3.0),
                ["solverRadiusSquared"] = F(9.0),
                ["actualRadiusSquared"] = F(9.5),
            },
            ["r3"] = new Dictionary<string, FltX> {
                ["solverRadius"] = F(4.0),
                ["solverRadiusSquared"] = F(16.0),
                ["actualRadiusSquared"] = F(16.0),
                ["isWithinEnvelope"] = F(0.6),
                ["numSegments"] = F(4.0),
            },
            ["r4"] = new Dictionary<string, FltX> {
                ["solverRadiusSquared"] = F(1.0),
                ["actualRadiusSquared"] = F(1.0),
            },
            ["r5"] = new Dictionary<string, FltX> {
                ["solverRadius"] = F(6.0),
                ["solverRadiusSquared"] = F(36.0),
                ["actualRadiusSquared"] = F(36.0),
                ["isWithinEnvelope"] = F(0.4),
                ["numSegments"] = F(2.0),
            },
            ["ghost"] = new Dictionary<string, FltX> {
                ["solverRadius"] = F(5.0),
                ["solverRadiusSquared"] = F(25.0),
                ["actualRadiusSquared"] = F(25.0),
            },
        };

        IReadOnlyList<CylinderRadiusSelectionResult> selections =
            ContinuousRadiusSelectionExtractor.BuildPwlResults(prototypes, pwl);

        selections.Should().HaveCount(3,
            "r1 and r4 lack a radius and ghost has no prototype; all three are skipped");
        CylinderRadiusSelectionResult r2 = selections[0];
        r2.VariableName.Should().Be("r2");
        r2.PwlMetadata!.PwlAbsoluteError.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-12,
            "an absent error defaults to zero while an absent radius would have skipped the entry");
        r2.PwlMetadata.NumSegments.Should().Be(1, "an absent segment count defaults to one");
        r2.PwlMetadata.IsWithinEnvelope.Should().BeFalse("no envelope flag means not within");

        CylinderRadiusSelectionResult r3 = selections[1];
        r3.PwlMetadata!.IsWithinEnvelope.Should().BeTrue("0.6 is above the 0.5 threshold");
        r3.PwlMetadata.NumSegments.Should().Be(4);

        selections[2].VariableName.Should().Be("r5");
        selections[2].PwlMetadata!.IsWithinEnvelope.Should().BeFalse("0.4 is below the 0.5 threshold");

        ContinuousRadiusSelectionExtractor
            .BuildPwlResults(prototypes, new Dictionary<string, IReadOnlyDictionary<string, FltX>>())
            .Should().BeEmpty();
        ContinuousRadiusSelectionExtractor
            .BuildPwlResultsFromExtracted(prototypes, Array.Empty<PwlExtractedRadius>())
            .Should().BeEmpty();
    }

    /// <summary>
    /// **没有 `RadiusWeightFunctionKey` 就抛** / Throwing when the weight-function key is absent.
    /// </summary>
    /// <remarks>
    /// 键是结果的回指依据，不允许为空——**抛而不是造一个空键结果**。
    ///
    /// The key is how a result is traced back and must not be blank, so it **throws rather than producing a blank-keyed result**.
    /// </remarks>
    [Fact]
    public void BuildNative_ShouldThrowWithoutTheWeightFunctionKey() {
        var prototypes = new List<ContinuousCylinderRadiusSolverPrototype> { Prototype("r1", key: null) };
        var results = new Dictionary<string, FltX> { ["r1"] = F(2.0) };

        Action call = () => ContinuousRadiusSelectionExtractor.BuildNativeResults(prototypes, results);
        call.Should().Throw<InvalidOperationException>();
    }
}
