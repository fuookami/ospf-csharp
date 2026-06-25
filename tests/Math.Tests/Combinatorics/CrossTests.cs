using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
#nullable enable

using Xunit;
using Fuookami.Ospf.Math.Combinatorics;

namespace Fuookami.Ospf.Math.Tests.Combinatorics
{
    public class CrossTests
    {
        [Fact]
        public void CrossProduct_ReturnsCorrectCount()
        {
            var input = new[] { new[] { 0, 1 }, new[] { 2, 3 } };
            Assert.Equal(4, Cross.CrossProduct(input).Count);
        }

        [Fact]
        public void CrossCount_CalculatesCorrectly()
        {
            var input = new[] { new[] { 0, 1 }, new[] { 2, 3 } };
            Assert.Equal(4L, Cross.CrossCount(input));
        }

        [Fact]
        public void Cross2_ReturnsCorrectPairs()
        {
            var result = Cross.Cross2(new[] { 0, 1 }, new[] { 2, 3 });
            Assert.Equal(4, result.Count);
            Assert.Equal((0, 2), result[0]);
            Assert.Equal((0, 3), result[1]);
            Assert.Equal((1, 2), result[2]);
            Assert.Equal((1, 3), result[3]);
        }

        [Fact]
        public void Cross3_ReturnsCorrectCount()
        {
            var result = Cross.Cross3(new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4, 5 });
            Assert.Equal(8, result.Count);
        }

        [Fact]
        public void CrossSequence_ReturnsCorrectCount()
        {
            var input = new[] { new[] { 0, 1 }, new[] { 2, 3 } };
            Assert.Equal(4, Cross.CrossSequence(input).ToList().Count);
        }

        [Fact]
        public async Task CrossAsync_ReturnsSameCountAsSync()
        {
            var input = new IReadOnlyList<int>[] { new[] { 0, 1 }, new[] { 2, 3 } };
            var syncCount = Cross.CrossProduct(input).Count;

            var asyncCount = 0;
            await foreach (var _ in Cross.CrossAsync(input).GetEnumerator())
                asyncCount++;

            Assert.Equal(syncCount, asyncCount);
        }
    }
}
