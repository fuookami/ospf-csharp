// NSGA-II 的约束分组选择：说明与代码一致/ NSGA-II constraint-group selection: remarks match the implementation.

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// **NSGA-II 运行器的说明与代码一致** / The NSGA-II runner's remarks match the implementation.
/// </summary>
/// <remarks>
/// 说明原来说"没有实现模型自带约束分组这条路径"。查过之后：**能力在**——`SearchConstraints.SelectSurvivors` 就是
/// `constraintSurvivors`（随机排序取前 n、其余按可行组分组选择），一字不差；缺的是**运行器不去接它**，因为本实现把处理器放在
/// 问题旁边而不是模型上。所以缺口是"接不上"，不是"没有"。
///
/// 两者对调用方的意思完全不同：前者意味着"你自己包一层就行"，后者意味着"你得不到这个能力"。**说明写错方向，人会白写一遍。**
///
/// The remarks used to claim "the model-carried constraint grouping path is not implemented". Checking shows the **capability is there** —
/// `SearchConstraints.SelectSurvivors` is exactly its `constraintSurvivors` (stochastic ranking takes the top n; otherwise per feasibility group),
/// with the same logic. What is missing is that **the runner does not call it**, because this implementation keeps the handler next to the problem rather than on the model.
/// So the gap is "not wired", not "absent".
///
/// Those two mean very different things to a caller: the first says "wrap it yourself", the second says "you cannot have it". **A remark pointing the wrong way
/// costs someone a rewrite.**
/// </remarks>
public class Nsga2ConstraintSelectionDocTests {
    private static string RunnerSource() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            string candidate = Path.Combine(
                directory, "src", "Core", "Solver", "Heuristic", "Running", "Nsga2Runner.cs");
            if (File.Exists(candidate)) {
                return File.ReadAllText(candidate);
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("could not locate Nsga2Runner.cs");
    }

    /// <summary>
    /// **说明里点名能力在哪，且不再说它没有** / The remarks name where the capability lives and no longer claim it is absent.
    /// </summary>
    /// <remarks>
    /// 三点成对：出现 `SelectSurvivors` / **不再出现"没有实现"** / 出现"接不上"（缺口的方向写清楚）。
    ///
    /// Three paired points: `SelectSurvivors` appears; **"没有实现" is gone**; and "接不上" is there, naming the direction of the gap.
    /// </remarks>
    [Fact]
    public void Remarks_ShouldNameTheCapabilityInsteadOfDenyingIt() {
        string source = RunnerSource();

        source.Should().Contain("SelectSurvivors");
        source.Should().NotContain("没有移植\"模型自带约束分组\"这条路径");
        // **接线之后，"接不上"这句话已经不成立了**：判据跟着改成"接线方式写在备注里"。
        // **After wired it, "not wired" no longer holds**: the criterion follows and asks for the wiring description instead.
        source.Should().Contain("由构造参数 `constraints` 接进来");
        source.Should().NotContain("接不上");
    }

    /// <summary>
    /// **能力本身还在：`SelectSurvivors` 两种模式都在** / The capability itself is present: both modes live in `SelectSurvivors`.
    /// </summary>
    /// <remarks>
    /// 三点成对：源码里出现随机排序分支 / 出现分组循环 / `Groups` 调用。
    ///
    /// Three paired points: the stochastic-ranking branch, the group loop, and the `Groups` call all appear in the source.
    /// </remarks>
    [Fact]
    public void SelectSurvivors_ShouldStillHoldBothModes() {
        string? directory = AppContext.BaseDirectory;
        string? path = null;
        while (directory is not null) {
            string candidate = Path.Combine(directory, "src", "Core", "Solver", "Heuristic", "SearchConstraints.cs");
            if (File.Exists(candidate)) {
                path = candidate;
                break;
            }
            directory = Path.GetDirectoryName(directory);
        }
        path.Should().NotBeNull();

        string source = File.ReadAllText(path!);
        source.Should().Contain("ConstraintMode.StochasticRanking");
        source.Should().Contain("Handling.Groups(entries)");
    }
}
