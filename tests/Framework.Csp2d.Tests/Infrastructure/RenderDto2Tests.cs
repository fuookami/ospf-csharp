// 渲染 DTO 判据 / Criteria for the render DTOs.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure.Dto;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Infrastructure;

/// <summary>
/// **DTO 只有"可空转空表"这一条行为** / The only behaviour a DTO carries is "null becomes an empty list".
/// </summary>
/// <remarks>
/// 这几个类型是纯数据载体，**没有算法可钉**。所以判据只钉它们唯一真正影响调用方的那一格：
/// 渲染层不该为"这一项可能是 null"多写一个分支。
///
/// These types are pure data carriers and **carry no algorithm to pin**. The criteria therefore pin the only aspect that actually affects callers: the render layer should
/// not carry an extra branch for "this might be null".
/// </remarks>
public class RenderDto2Tests {
    /// <summary>**刀序缺省为空表** / A missing cut sequence becomes an empty list.</summary>
    [Fact]
    public void Pattern_ShouldDefaultTheCutSequenceToEmpty() {
        RenderCuttingPattern2Dto pattern = new(
            "PAT1", "STEEL", 4.0, 2.0, 8.0, 6.0, 2.0, 0.75, "freeform",
            new List<RenderCutPiece2Dto> { new("P1", 0.0, 0.0, 1.0, 1.0, 1.0, false) });

        pattern.Sequence.Should().BeEmpty("arbitrary nesting produces no cut sequence");
        pattern.Pieces.Should().ContainSingle();
        pattern.Pieces[0].PieceId.Should().Be("P1");
    }

    /// <summary>**关键指标缺省为空表** / A missing KPI block becomes an empty map.</summary>
    [Fact]
    public void Schema_ShouldDefaultTheIndicatorsToEmpty() {
        RenderCuttingSchema2Dto schema = new(new List<RenderCuttingPlan2Dto>());

        schema.Indicators.Should().BeEmpty();
        schema.Plans.Should().BeEmpty();
    }

    /// <summary>**显式传入的刀序会被原样保留** / Explicit data is preserved verbatim.</summary>
    [Fact]
    public void Pattern_ShouldKeepTheGivenCutSequence() {
        var sequence = new List<RenderCutLine2Dto> {
            new("vertical", 2.0, 1, "切条 / strip"),
            new("horizontal", 1.0, 2, "切件 / piece"),
        };
        RenderCuttingPattern2Dto pattern = new(
            "PAT1", "STEEL", 4.0, 2.0, 8.0, 6.0, 2.0, 0.75, "guillotine",
            new List<RenderCutPiece2Dto>(), sequence);

        pattern.Sequence.Should().HaveCount(2);
        pattern.Sequence[0].Orientation.Should().Be("vertical", "the orientation stays a string for the front end");
        pattern.Sequence[0].Position.Should().Be(2.0);
    }
}
