// 种子列构造（B665）的判据 / Criteria for seed-column construction (B665).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **种子列构造（B665）** / Seed-column construction (B665).
/// </summary>
/// <remarks>
/// 判据的重点：**种子的状态是"迭代 0 + 空列"**、**上限用调用方的值而不是默认值**、**结果按值去重**。
///
/// The criteria focus on **the seed state being "iteration 0 with empty columns"**, on **the limits coming from the caller rather than the defaults**, and on **results deduplicating by value**.
/// </remarks>
public class SeedColumnsTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static Layer<FltX> Layer(string itemId, double width = 20.0) =>
        new(0L, null,
            new SheetType<FltX>(Q(100.0), Q(100.0), Q(1000.0), new SheetTypeId("S"), true),
            new Sheet2Shape<FltX>(Q(100.0), Q(100.0)),
            new[] {
                new Placement2<FltX>(
                    Shape: new RectanglePackingShape2<FltX>(Q(width), Q(10.0)),
                    Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                    Rotated: false,
                    ItemId: itemId),
            });

    /// <summary>
    /// 记录收到的状态与配置的请求构造器 / A request builder that records the state and config it receives.
    /// </summary>
    /// <remarks>
    /// **它只能观测，不计算**：构造请求时把状态与配置抄下来，返回一个空请求。
    ///
    /// **It only observes**: it copies the state and config it receives and returns an empty request.
    /// </remarks>
    private sealed class RecordingBuilder : IColumnGenerationLayerRequestBuilder
    {
        public int? CapturedIteration { get; private set; }

        public int? CapturedColumnCount { get; private set; }

        public ColumnGenerationConfig? CapturedConfig { get; private set; }

        public Bpp2dLayerGenerationRequest<FltX> Build(
            ColumnGenerationState state, ColumnGenerationConfig config) {
            CapturedIteration = state.Iteration;
            CapturedColumnCount = state.Columns.Count;
            CapturedConfig = config;
            return new Bpp2dLayerGenerationRequest<FltX>(
                Iteration: state.Iteration,
                Sheet: null,
                Sheets: Array.Empty<SheetType<FltX>>(),
                Items: Array.Empty<(ActualItem<FltX> Item, int Amount)>());
        }
    }

    /// <summary>按序返回给定结果的生成器 / A generator returning the given results in order.</summary>
    private sealed class ListGenerator : IBpp2dLayerGenerator<FltX>
    {
        private readonly IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> _results;

        public ListGenerator(IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results) => _results = results;

        public IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> Generate(
            Bpp2dLayerGenerationRequest<FltX> request) => _results;
    }

    /// <summary>
    /// **种子状态是迭代 0 + 空列，上限来自调用方** / The seed state is iteration 0 with no columns, and the limits come from the caller.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 构造器收到的**迭代号是 0**，即使是第 7 轮请求种子。
    /// 2. 构造器收到的**列数是 0**：种子与已有列无关。
    /// 3. 配置里的**两个上限是调用方给的**（`20`/`7`），不是默认的 `64`/`32`。
    /// 4. 生成器返回的列原样成为种子。
    ///
    /// Four paired points: the builder sees **iteration 0** even when seeding for round seven; it sees **no columns**, since seeding is independent of existing ones; the config's **two limits are
    /// the caller's** (`20` and `7`), not the defaults `64`/`32`; and the generator's layers become the seed unchanged.
    /// </remarks>
    [Fact]
    public void Build_ShouldSeedFromIterationZeroAndEmptyColumns() {
        var builder = new RecordingBuilder();
        var generator = new ListGenerator(new[] {
            new Bpp2dLayerGenerationResult<FltX>(Layer("I1")),
        });

        IReadOnlyList<Layer<FltX>> seeds = SeedColumns.Build(generator, builder, 20, 7);

        builder.CapturedIteration.Should().Be(0, "seeding always starts at iteration zero");
        builder.CapturedColumnCount.Should().Be(0, "seeding does not inherit existing columns");
        builder.CapturedConfig!.IterationLimit.Should().Be(20, "the caller's iteration limit is used");
        builder.CapturedConfig!.MaxColumnsPerIteration.Should().Be(7, "the caller's column cap is used");
        seeds.Should().HaveCount(1);
    }

    /// <summary>
    /// **结果按值去重，等价列只留一份** / Results deduplicate by value, keeping one of each equivalent layer.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 生成器给出两个**形状相同、实例不同**的列 → 种子**只留一份**（`Layer` 是值相等对象）。
    /// 2. 生成器给出两个**形状不同**的列 → 种子留两份。
    /// 3. 生成器给空 → 种子为空，不抛。
    ///
    /// Three paired points: two columns with **identical shape but distinct instances** leave **one seed**, since `Layer` has value equality; two columns with **different shapes** leave two; and an
    /// empty generation leaves an empty seed without throwing.
    /// </remarks>
    [Fact]
    public void Build_ShouldDeduplicateByValue() {
        var builder = new RecordingBuilder();

        IReadOnlyList<Layer<FltX>> duplicated = SeedColumns.Build(
            new ListGenerator(new[] {
                new Bpp2dLayerGenerationResult<FltX>(Layer("I1")),
                new Bpp2dLayerGenerationResult<FltX>(Layer("I1")),
            }),
            builder, 10, 5);
        duplicated.Should().HaveCount(1, "two equivalent layers collapse into one seed");

        IReadOnlyList<Layer<FltX>> distinctLayers = SeedColumns.Build(
            new ListGenerator(new[] {
                new Bpp2dLayerGenerationResult<FltX>(Layer("I1", 20.0)),
                new Bpp2dLayerGenerationResult<FltX>(Layer("I1", 30.0)),
            }),
            builder, 10, 5);
        distinctLayers.Should().HaveCount(2, "different shapes stay distinct");

        SeedColumns.Build(new ListGenerator(Array.Empty<Bpp2dLayerGenerationResult<FltX>>()), builder, 10, 5)
            .Should().BeEmpty("a generator with nothing to offer is not an error");
    }
}
