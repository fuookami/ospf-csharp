using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
#nullable enable

using Xunit;
using Fuookami.Ospf.Math.Combinatorics;

namespace Fuookami.Ospf.Math.Tests.Combinatorics
{
    public class CombinationsTests
    {
        [Fact]
        public void Combine_AllSubsets_ReturnsCorrectCount()
        {
            var input = new[] { 0, 1, 2 };
            Assert.Equal(7, Combinations.Combine(input).Count);
        }

        [Fact]
        public void Combine_ChooseTwo_ReturnsCorrectCount()
        {
            var input = new[] { 0, 1, 2 };
            Assert.Equal(3, Combinations.Combine(input, 2).Count);
        }

        [Fact]
        public void CombineCount_CalculatesCorrectly()
        {
            Assert.Equal(10L, Combinations.CombineCount(5, 2));
        }

        [Fact]
        public void CombineSequence_ChooseTwo_ReturnsCorrectElements()
        {
            var input = new[] { 0, 1, 2 };
            var result = Combinations.CombineSequence(input, 2).ToList();
            Assert.Equal(3, result.Count);
            Assert.Equal(new List<int> { 0, 1 }, result[0]);
            Assert.Equal(new List<int> { 0, 2 }, result[1]);
            Assert.Equal(new List<int> { 1, 2 }, result[2]);
        }

        [Fact]
        public async Task CombineAsync_ReturnsSameCountAsSync()
        {
            var input = new[] { 0, 1, 2 };
            var syncCount = Combinations.Combine(input).Count;

            var asyncCount = 0;
            await foreach (var _ in Combinations.CombineAsync(input).GetEnumerator())
                asyncCount++;

            Assert.Equal(syncCount, asyncCount);
        }
    }
}
