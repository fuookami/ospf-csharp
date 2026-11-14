using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// B807：`infrastructure/dto` 的重数不能只是文档里的一句话 / The recount of `infrastructure/dto` must not live only in prose.
/// </summary>
/// <remarks>
/// 清单里写着"12 个记录都有对应"，而**这句话是可以被代码推翻的**：谁删掉一个 DTO，文档不会跟着变。
/// 这里用反射把这句话钉在类型上——**文档说有的，类型必须真的在**。
///
/// The inventory claims "all 12 records have a counterpart", and **code can disprove that claim**: deleting a DTO does not update the prose. This pins the claim to
/// the types by reflection — **what the document says exists must really exist**.
/// </remarks>
public class Demo2DtoRecountTests
{
    /// <summary>
    /// 基线 12 个有引用的记录，本仓必须有同名类型 / The 12 referenced baseline records must each have a same-named type in this port.
    /// </summary>
    [Fact]
    public void EveryReferencedBaselineDto_ShouldHaveATypeInThePort() {
        Assembly example = typeof(RequestDTO).Assembly;
        var names = example.GetTypes()
            .Where(type => type.IsPublic)
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        string[] baseline = [
            "RequestDTO", "ResponseDTO", "SolvePolicy", "DiagnosticNote", "KPIResponseDTO",
            "ReportResponseDTO", "LoadingOrderResponseDTO", "BendersAdaptiveConfig",
            "BendersQualityOverrideConfig", "WeightRecommendationObjectiveConfig",
            "RunningHeartBeatDTO", "RenderDTO", "RenderPositionDTO", "RenderWeightDTO",
        ];
        foreach (string name in baseline) {
            names.Should().Contain(name, name + " is recorded as ported in the inventory, so the type has to exist");
        }
    }

    /// <summary>
    /// 13 号文件要分开看：**三个枚举在、一个枚举缺、那个记录不算缺口** / The 13th file needs splitting: three enums present, one missing, the record not a gap.
    /// </summary>
    /// <remarks>
    /// **"文件没人要"和"文件里的每一个声明都没人要"是两件事**，B807 一开始就是把它们混为一谈。
    /// 实测：`LengthUnitInput`(2)、`DeckLocationInput`(5)、`LoadingSpaceInput`(3) 在基线里都被引用且本仓都有；
    /// `WeightUnitInput`(2) 被引用但本仓**没有**——这是真缺口；`LoadingGeometryInput` 记录**零引用**，不抄是对的。
    ///
    /// **`WeightUnitInput` 这条判据会在缺口补上时变红**——那正是它该做的事：届时把它移进"必须有"的那一组。
    ///
    /// **"The file is unwanted" and "every declaration in it is unwanted" are different claims**, and B807 initially conflated them. Measured: `LengthUnitInput` (2),
    /// `DeckLocationInput` (5) and `LoadingSpaceInput` (3) are all referenced in the baseline and all present here; `WeightUnitInput` (2) is referenced but **absent**
    /// here — a real gap; the `LoadingGeometryInput` record has **zero** references, and not copying it is correct. **This criterion turns red once the gap closes** —
    /// which is its job: that is when it moves into the "must exist" group.
    /// </remarks>
    [Fact]
    public void TheThirteenthDtoFile_ShouldSplitThreeWays() {
        Assembly example = typeof(RequestDTO).Assembly;
        var names = example.GetTypes()
            .Where(type => type.IsPublic)
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string present in new[] { "LengthUnitInput", "DeckLocationInput", "LoadingSpaceInput" }) {
            names.Should().Contain(present, present + " is referenced in the baseline and is ported here");
        }
        // **B941：`WeightUnitInput` 从"必须没有"移到"必须有"。** 判据自己写着"缺口补上时它会红，届时把它移进必须存在那一组"——
        // 缺口在 B941 补上了：`LoadingContextFactory` 用 `input.WeightUnit` 建舱位承重与问题声明（基线 `LoadingContextFactory.kt:62` 就是这么做的）。
        //
        // **B941 moves `WeightUnitInput` from "must be absent" to "must be present".** The criterion itself said "it turns red once the gap closes, and that is
        // when it moves into the must-exist group" — the gap closed in B941: `LoadingContextFactory` uses `input.WeightUnit` for the position capacity and the
        // problem declaration, exactly as the baseline does at `LoadingContextFactory.kt:62`.
        names.Should().Contain("WeightUnitInput",
            "it is referenced twice in the baseline and now has a consumer here, so its absence would be the gap");
        names.Should().NotContain("LoadingGeometryInput",
            "the record has zero baseline references, so copying it would add a type with no constructor call");
    }
}
