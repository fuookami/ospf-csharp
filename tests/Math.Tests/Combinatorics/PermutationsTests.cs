using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
#nullable enable

using Xunit;
using Fuookami.Ospf.Math.Combinatorics;

namespace Fuookami.Ospf.Math.Tests.Combinatorics
{
    public class PermutationsTests
    {
        [Fact]
        public void Permute_FullPermutations_ReturnsCorrectCount()
        {
            var input = new[] { 0, 1, 2 };
            Assert.Equal(6, Permutations.Permute(input).Count);
        }

        [Fact]
        public void Permute_PartialPermutations_ReturnsCorrectCount()
        {
            var input = new[] { 0, 1, 2 };
            Assert.Equal(6, Permutations.Permute(input, 2).Count);
        }

        [Fact]
        public void PermuteCount_CalculatesCorrectly()
        {
            Assert.Equal(20L, Permutations.PermuteCount(5, 2));
        }

        [Fact]
        public void PermuteSequence_ReturnsCorrectCount()
        {
            var input = new[] { 0, 1, 2 };
            Assert.Equal(6, Permutations.PermuteSequence(input, 2).ToList().Count);
        }

        [Fact]
        public async Task PermuteAsync_ReturnsSameCountAsSync()
        {
            var input = new[] { 0, 1, 2 };
            var syncCount = Permutations.Permute(input).Count;

            var asyncCount = 0;
            await foreach (var _ in Permutations.PermuteAsync(input).GetEnumerator())
                asyncCount++;

            Assert.Equal(syncCount, asyncCount);
        }
    }
}
