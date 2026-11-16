// 舱位状态码的判据/ Criteria for the position status code.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **舱位状态码必须真的能分开 `Loaded` 与 `Reserved`** / The position status code must really keep `Loaded` and `Reserved` apart.
/// </summary>
/// <remarks>
/// 本仓此前只有四个布尔旗标，没有状态码，装配器还恒写 `stowageNeeded = true`。结果是：
/// **`Loaded` 与 `Reserved` 都不可用、都不需要装载决策**——两者在模型里完全一样，"已装货"和"被预留"再也分不开。
///
/// 四点成对：`Loaded` 与 `Reserved` 的码**不同**、但两者都不可用 / `Preassigned` 要求调整而 `Unloaded` 不要求 /
/// 谓词与推荐重量**只在主舱且模式匹配时**为真 / 端到端：给了 `Loaded` 的舱位在装配后确实不可用。
///
/// 第三点最容易漏：少判"主舱"或"模式"任一条，下舱或全配载下也会生成没有意义的重量变量——**那种模型不会报错，只会变钝**。
///
/// This implementation had four boolean flags and no status code, and the assembler hard-coded `stowageNeeded = true`. The result: **`Loaded` and `Reserved` are both
/// unavailable and both need no decision**, so they were identical in the model and "already loaded" could not be told from "reserved".
///
/// Four paired points: the codes of `Loaded` and `Reserved` **differ** while both are unavailable; `Preassigned` needs adjustment and `Unloaded` does not; the
/// predicate and recommended weight flags are **true only on the main deck and in the matching mode**; and end to end, a position given `Loaded` is unavailable
/// after assembly. The third point is the easiest to miss: dropping either the deck condition or the mode condition fabricates meaningless weight variables on
/// lower-deck or full-load positions — **and that model does not fail, it just gets duller**.
/// </remarks>
public sealed class Demo2PositionStatusTests {
    private static readonly PositionLocation MainDeck = new(new HashSet<PositionLocationTag> { PositionLocationTag.Main });
    private static readonly PositionLocation LowerDeck = new(new HashSet<PositionLocationTag> { PositionLocationTag.Low });

    [Fact]
    public void LoadedAndReserved_ShouldBeDistinguishableAndBothUnavailable() {
        var loaded = PositionStatus.From(MainDeck, PositionStatusCode.Loaded, StowageMode.FullLoad);
        var reserved = PositionStatus.From(MainDeck, PositionStatusCode.Reserved, StowageMode.FullLoad);

        loaded.Code.Should().Be(PositionStatusCode.Loaded);
        reserved.Code.Should().Be(PositionStatusCode.Reserved);
        loaded.Code.Should().NotBe(reserved.Code, "the whole point of a code is telling these two apart");
        loaded.Available.Should().BeFalse();
        reserved.Available.Should().BeFalse();
        loaded.StowageNeeded.Should().BeFalse("a loaded position needs no decision");
        reserved.StowageNeeded.Should().BeFalse("a reserved position needs no decision");
    }

    [Fact]
    public void Preassigned_ShouldNeedAdjustmentAndUnloadedShouldNot() {
        var preassigned = PositionStatus.From(MainDeck, PositionStatusCode.Preassigned, StowageMode.FullLoad);
        var unloaded = PositionStatus.From(MainDeck, PositionStatusCode.Unloaded, StowageMode.FullLoad);

        preassigned.AdjustmentNeeded.Should().BeTrue();
        unloaded.AdjustmentNeeded.Should().BeFalse();
        preassigned.StowageNeeded.Should().BeTrue();
        unloaded.StowageNeeded.Should().BeTrue();
        preassigned.Available.Should().BeTrue();
        unloaded.Available.Should().BeTrue();
    }

    [Fact]
    public void WeightFlags_ShouldNeedBothTheMainDeckAndTheMatchingMode() {
        foreach (StowageMode mode in Enum.GetValues<StowageMode>()) {
            var main = PositionStatus.From(MainDeck, PositionStatusCode.Unloaded, mode);
            var lower = PositionStatus.From(LowerDeck, PositionStatusCode.Unloaded, mode);

            main.PredicateWeightNeeded.Should().Be(mode == StowageMode.Predistribution);
            main.RecommendedWeightNeeded.Should().Be(mode == StowageMode.WeightRecommendation);
            lower.PredicateWeightNeeded.Should().BeFalse("a lower-deck position never carries the main-deck weight variables");
            lower.RecommendedWeightNeeded.Should().BeFalse("a lower-deck position never carries the main-deck weight variables");
        }

        // 已装载的舱位同样不该生成重量变量：**不可用 + 不需要决策，两个条件都要管住**。
        // A loaded position must not get weight variables either: **both "unavailable" and "no decision needed" have to hold**.
        var loadedInPredistribution = PositionStatus.From(MainDeck, PositionStatusCode.Loaded, StowageMode.Predistribution);
        loadedInPredistribution.PredicateWeightNeeded.Should().BeFalse();
    }

    [Fact]
    public void LegacyConstructor_ShouldCollapseReservedIntoLoaded() {
        // 旧构造只有布尔旗标，**`Reserved` 在它那里根本表达不出来**——这正是它被降级为"仅供既有测试"的原因。
        // The legacy constructor only has booleans, so **`Reserved` simply cannot be expressed through it** — which is why it is now "existing tests only".
        var legacy = new PositionStatus(stowageNeeded: false, adjustmentNeeded: false, predicateWeightNeeded: false, recommendedWeightNeeded: false);

        legacy.Code.Should().Be(PositionStatusCode.Loaded, "the legacy form cannot tell Loaded from Reserved");
        legacy.Available.Should().BeFalse();
    }

    [Fact]
    public void AircraftAssembler_ShouldDeriveTheStatusFromTheRequest() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        PositionInput first = request.Positions[0] with { StatusCode = PositionStatusCode.Loaded };
        RequestDTO withLoadedPosition = request with {
            Positions = [first, request.Positions[1]],
        };

        var pipeline = new DomainPipelineContext();
        pipeline.Init(withLoadedPosition).IsOk.Should().BeTrue();

        StowagePosition assembled = pipeline.StowageContext.Aggregation!.Positions[0];
        assembled.Status.Code.Should().Be(PositionStatusCode.Loaded);
        assembled.Status.Available.Should().BeFalse("the assembler must not hard-code stowageNeeded = true any more");
        assembled.Status.StowageNeeded.Should().BeFalse();
    }
}
