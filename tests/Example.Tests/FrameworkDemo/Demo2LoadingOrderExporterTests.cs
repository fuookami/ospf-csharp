// 装载顺序导出器的判据/ Criteria for the loading-order exporter.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **导出器：三处照做，一处必须偏离** / The exporter: three behaviours copied from the code, one deliberate divergence.
/// </summary>
/// <remarks>
/// 四点成对：行格式 `"{id}: {spaceName} (order={order})"` / **排序按"甲板 → 顺序号 → id 的字符串字典序"**（第三级是字典序，`"10"` 排在 `"2"` 前）/
/// `status` 恒为 `"Optimal"` / **没分配顺序号的舱位明确报错**而不是崩溃或静默跳过。
///
/// 第二点最容易被"顺手改好"：看着像 bug，但它是行为。改了以后同一批输入的输出顺序就与基线不同，
/// **而顺序本身就是这个导出器的全部产出**——这类"改进"会让对拍失去意义。所以要改得先确认调用方不依赖它。
///
/// 第四点是唯一必须偏离的地方：`_loadingOrder` 是 `lateinit`，读得太早抛异常；本仓把它建成可空的，
/// 于是这里给**错误码 + 点名哪个舱位**。崩溃换不到信息，错误码能。
///
/// Four paired points: the line format; **the sort is "deck → order number → the id's string order"** (the third key is lexicographic, so `"10"` precedes `"2"`);
/// `status` is always the literal `"Optimal"`; and **a position without an order number fails loudly** instead of crashing or being skipped silently.
///
/// The second point is the easiest to "fix while at it": it looks like a bug, but it is the code behaviour, and changing it makes the same input produce a
/// different order — **and the order is this exporter's entire output**, so such an "improvement" makes cross-checking meaningless. Changing it needs a confirmed
/// promise from callers first. The fourth point is the one place a divergence is required: its `_loadingOrder` is `lateinit` and throws when read too
/// early, while this implementation models it as nullable and therefore returns **an error code naming the position**. A crash buys no information; an error code does.
/// </remarks>
public sealed class Demo2LoadingOrderExporterTests {
    private static readonly Regex Line = new(@"^\d+: \S+ \(order=\d+\)$", RegexOptions.CultureInvariant);

    [Fact]
    public void Invoke_ShouldExportOneWellFormedLinePerPosition() {
        var pipeline = new DomainPipelineContext();
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        pipeline.Init(request).IsOk.Should().BeTrue();

        LoadingOrderOutputExporter exporter = new(pipeline.AircraftContext.Aggregation!);
        Result<LoadingOrderResponseDTO, ErrorCode, Error<ErrorCode>> exported = exporter.Invoke(request);

        exported.IsOk.Should().BeTrue(exported.ToString());
        LoadingOrderResponseDTO response = ((Ok<LoadingOrderResponseDTO, ErrorCode, Error<ErrorCode>>)exported).Value;
        response.Succeed.Should().BeTrue();
        response.Status.Should().Be("Optimal", "the literal is copied from the baseline; callers match on it");
        response.Orders.Should().NotBeEmpty();

        int positionCount = pipeline.AircraftContext.Aggregation!.Decks.Sum(deck => deck.Positions.Count);
        response.Orders!.Should().HaveCount(positionCount, "every position gets exactly one line");
        foreach (string line in response.Orders!) {
            Line.IsMatch(line).Should().BeTrue("unexpected line format: " + line);
        }
    }

    [Fact]
    public void Invoke_ShouldSortByDeckThenOrderThenTheIdsStringOrder() {
        var pipeline = new DomainPipelineContext();
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        pipeline.Init(request).IsOk.Should().BeTrue();

        AircraftAggregation aggregation = pipeline.AircraftContext.Aggregation!;
        LoadingOrderOutputExporter exporter = new(aggregation);
        LoadingOrderResponseDTO response =
            ((Ok<LoadingOrderResponseDTO, ErrorCode, Error<ErrorCode>>)exporter.Invoke(request)).Value;

        // **独立重算一遍期望顺序**，不沿用被测实现：第三级键故意写成字符串比较，因为那正是行为。
        // **Recompute the expected order independently** rather than copying the implementation: the third key is deliberately a string comparison, because that is
        // exactly the code behaviour.
        var expected = aggregation.Decks
            .SelectMany(deck => deck.Positions.Select(position => (Deck: deck.Location, Position: position)))
            .OrderBy(pair => pair.Deck)
            .ThenBy(pair => pair.Position.LoadingOrder!.Order)
            .ThenBy(pair => pair.Position.Id.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .Select(pair => pair.Position.Id.ToString(CultureInfo.InvariantCulture) + ": " + pair.Position.SpaceName
                + " (order=" + pair.Position.LoadingOrder!.Order.ToString(CultureInfo.InvariantCulture) + ")")
            .ToList();

        response.Orders!.Should().Equal(expected);
    }

    [Fact]
    public void Invoke_ShouldOrderByTheOrderNumberRatherThanById() {
        // **让两个排序键真正打架**：把 P1 的顺序号设成 1、P2 设成 0。按顺序号排 P2 在前，按 id 排 P1 在前——
        // 只有这种输入才能证明第二级键真的生效。**示例请求默认 0/1 与 id 同序，那种输入下"排序对不对"是测不出来的。**
        //
        // **Make the two keys disagree**: give P1 order 1 and P2 order 0. Ordering by the order number puts P2 first, ordering by id puts P1 first — only this input
        // proves the second key is really in effect. **The default 0/1 sample agrees with id order, and under that input "is the sort right" cannot be tested at all.**
        var pipeline = new DomainPipelineContext();
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Positions = [
                sample.Positions[0] with { LoadingOrder = 1 },
                sample.Positions[1] with { LoadingOrder = 0 },
            ],
        };
        pipeline.Init(request).IsOk.Should().BeTrue();

        LoadingOrderOutputExporter exporter = new(pipeline.AircraftContext.Aggregation!);
        Result<LoadingOrderResponseDTO, ErrorCode, Error<ErrorCode>> exported = exporter.Invoke(request);

        exported.IsOk.Should().BeTrue(exported.ToString());
        LoadingOrderResponseDTO response = ((Ok<LoadingOrderResponseDTO, ErrorCode, Error<ErrorCode>>)exported).Value;
        response.Orders.Should().HaveCount(2);
        response.Orders![0].Should().EndWith("(order=0)", "the order number decides, not the id");
        response.Orders[1].Should().EndWith("(order=1)");
    }

    [Fact]
    public void Invoke_ShouldFailLoudlyWhenAnOrderIsMissing() {
        var pipeline = new DomainPipelineContext();
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        pipeline.Init(request).IsOk.Should().BeTrue();

        AircraftAggregation aggregation = pipeline.AircraftContext.Aggregation!;
        Position victim = aggregation.Decks[0].Positions[0];
        victim.LoadingOrder = null;

        LoadingOrderOutputExporter exporter = new(aggregation);
        Result<LoadingOrderResponseDTO, ErrorCode, Error<ErrorCode>> exported = exporter.Invoke(request);

        exported.IsFailed.Should().BeTrue("a position without an order number cannot be exported");
        var failed = (Failed<LoadingOrderResponseDTO, ErrorCode, Error<ErrorCode>>)exported;
        failed.Error.Code.Should().Be(ErrorCode.ApplicationFailed);
        failed.Error.Message.Should().Contain(victim.SpaceName, "the error must name the offending position");
    }
}
