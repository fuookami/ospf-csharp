using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
#nullable enable

using Xunit;
using Fuookami.Ospf.Math.Combinatorics;

namespace Fuookami.Ospf.Math.Tests.Combinatorics;

public class PermutationsTests {
    [Fact]
    public void Permute_FullPermutations_ReturnsCorrectCount() {
        int[] input = new[] { 0, 1, 2 };
        Assert.Equal(6, Permutations.Permute(input).Count);
    }

    [Fact]
    public void Permute_PartialPermutations_ReturnsCorrectCount() {
        int[] input = new[] { 0, 1, 2 };
        Assert.Equal(6, Permutations.Permute(input, 2).Count);
    }

    [Fact]
    public void PermuteCount_CalculatesCorrectly() => Assert.Equal(20L, Permutations.PermuteCount(5, 2));

    [Fact]
    public void PermuteSequence_ReturnsCorrectCount() {
        int[] input = new[] { 0, 1, 2 };
        Assert.Equal(6, Permutations.PermuteSequence(input, 2).ToList().Count);
    }

    [Fact]
    public async Task PermuteAsync_ReturnsSameCountAsSync() {
        int[] input = new[] { 0, 1, 2 };
        int syncCount = Permutations.Permute(input).Count;

        int asyncCount = 0;
        await foreach (List<int> _ in Permutations.PermuteAsync(input).GetEnumerator()) {
            asyncCount++;
        }

        Assert.Equal(syncCount, asyncCount);
    }
}
