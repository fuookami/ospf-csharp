#nullable enable

using FluentAssertions;
using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Token;

/// <summary>
/// **结构变更会使索引失效，而重建后仍然正确** / A structural change invalidates the index, and the rebuild is still correct.
/// </summary>
/// <remarks>
/// **这一条是 `TokenInvalidateRebuildBenchmark` 的<b>行为前提**：
/// 那个基准假设"增删一个 token 会让索引失效、下一次读必须重建"，而重建<b>结果必须是对的**。
/// 基准本身只测时间，测不出这件事——所以由这一条判据钉住。**
///
/// **它也顺手覆盖了一个容易漏的方向：** **被移除的 token 不该还能查到，而新加的 token 必须立刻能查到。**
///
/// **This is the behavioural premise of `TokenInvalidateRebuildBenchmark` (derived from the code): that benchmark assumes
/// adding or removing one token invalidates the index and the next read rebuilds it — and the rebuilt result must be correct.** **The
/// benchmark times only; it cannot catch a wrong answer.** **It also covers both directions: a removed token must stop resolving, and a
/// newly added one must resolve immediately.**
/// </remarks>
public class TokenIndexInvalidationTests
{
    /// <summary>**增删之后索引仍然给出正确位置** / The index stays correct across add and remove.</summary>
    [Fact]
    public void TheIndex_ShouldStayCorrectAcrossStructuralChanges() {
        using var writer = new ManualTokenList<Flt64>(checkTokenExists: true);
        var variables = new List<RealVar>(64);
        for (int index = 0; index < 64; ++index) {
            variables.Add(new RealVar($"idx_{index}"));
        }
        writer.Add(variables);
        writer.TokensInSolver.Count.Should().Be(64, "all 64 variables must be registered");
        IReadOnlyList<Token<Flt64>> tokens = writer.TokensInSolver;

        // **先读一次，把索引建起来**（这就是基准的 `Setup` 里那一读）。
        // **One read first, so the index exists — the same read the benchmark's `Setup` performs.**
        writer.IndexOf(tokens[^1]).Should().Be(tokens.Count - 1, "the last registered token sits at the last index");

        var probe = new RealVar("idx_probe");
        writer.Add(probe);
        writer.TokensInSolver.Count.Should().Be(65, "the probe must really be registered");

        // **新加的 token 必须立刻能查到。**
        // **The new token must resolve immediately.**
        writer.IndexOf(probe).Should().Be(tokens.Count, "the probe registered after the initial batch, so it is last");

        // **原有 token 的位置不能被这次增删弄乱。**
        // **The existing positions must survive the change.**
        IReadOnlyList<Token<Flt64>> grown = writer.TokensInSolver;
        for (int index = 0; index < tokens.Count; ++index) {
            writer.IndexOf(grown[index]).Should().Be(index, $"token {index} kept its position after the add");
        }

        writer.Remove(probe);

        // **移除之后不该还能查到它。**
        // **After removal the probe must stop resolving.**
        writer.IndexOf(probe).Should().BeNull("a removed token has no index");
        writer.TokensInSolver.Count.Should().Be(tokens.Count, "the list is back to its original size");
    }

    /// <summary>**反复失效不会让结果漂移** / Repeated invalidation does not drift the answer.</summary>
    /// <remarks>
    /// **基准每个操作做 16 次"增删 + 读"，所以这里也做同样的次数**：**一次正确不代表第十六次还正确，
    /// 而缓存类问题最常出现在"第二次重建"上。**
    ///
    /// **The benchmark does sixteen add-remove-read cycles per operation, so this does the same: one correct answer does not imply
    /// the sixteenth is correct, and cache bugs most often appear on the second rebuild.**
    /// </remarks>
    [Fact]
    public void RepeatedInvalidation_ShouldNotDriftTheAnswer() {
        using var writer = new ManualTokenList<Flt64>(checkTokenExists: true);
        var variables = new List<RealVar>(32);
        for (int index = 0; index < 32; ++index) {
            variables.Add(new RealVar($"drift_{index}"));
        }
        writer.Add(variables);
        writer.TokensInSolver.Count.Should().Be(32, "all 32 variables must be registered");
        IReadOnlyList<Token<Flt64>> tokens = writer.TokensInSolver;

        var probes = new List<RealVar>(16);
        for (int index = 0; index < 16; ++index) {
            probes.Add(new RealVar($"drift_probe_{index}"));
        }

        for (int cycle = 0; cycle < probes.Count; ++cycle) {
            RealVar probe = probes[cycle];
            writer.Add(probe);
            writer.TokensInSolver.Count.Should().Be(tokens.Count + 1, $"cycle {cycle} registers its probe");
            writer.IndexOf(probe).Should().Be(tokens.Count, $"cycle {cycle} finds the probe at the end");
            writer.IndexOf(tokens[cycle % tokens.Count]).Should().Be(
                cycle % tokens.Count,
                $"cycle {cycle} still resolves an original token to its own position");
            writer.Remove(probe);
            writer.IndexOf(probe).Should().BeNull($"cycle {cycle} drops the probe again");
        }
    }
}