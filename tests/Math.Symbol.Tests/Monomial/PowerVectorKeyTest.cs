#nullable enable

using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Tests
{
    using Monomial;

    /// <summary>
    /// 测试幂向量键 / Tests for PowerVectorKey.
    /// </summary>
    public class PowerVectorKeyTest
    {
        private static readonly ISymbol X = new OwnedSymbol(new SymbolId("x"), "x");
        private static readonly ISymbol Y = new OwnedSymbol(new SymbolId("y"), "y");
        private static readonly ISymbol Z = new OwnedSymbol(new SymbolId("z"), "z");

        [Fact]
        public void DenseModeShouldCreateCorrectKey()
        {
            var key = PowerVectorKey.Dense(new[] { 1, 2, 0 });
            Assert.True(key.IsDense);
            Assert.False(key.IsSparse);
            Assert.NotEqual(0, key.Hash);
        }

        [Fact]
        public void SparseModeShouldCreateCorrectKey()
        {
            var key = PowerVectorKey.Sparse(new[] { 0, 2 }, new[] { 1, 3 });
            Assert.False(key.IsDense);
            Assert.True(key.IsSparse);
            Assert.NotEqual(0, key.Hash);
        }

        [Fact]
        public void DenseKeysWithSamePowersShouldBeEqual()
        {
            var a = PowerVectorKey.Dense(new[] { 1, 2, 3 });
            var b = PowerVectorKey.Dense(new[] { 1, 2, 3 });
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void SparseKeysWithSamePowersShouldBeEqual()
        {
            var a = PowerVectorKey.Sparse(new[] { 0, 2 }, new[] { 1, 3 });
            var b = PowerVectorKey.Sparse(new[] { 0, 2 }, new[] { 1, 3 });
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void KeysWithDifferentPowersShouldNotBeEqual()
        {
            var a = PowerVectorKey.Dense(new[] { 1, 2, 3 });
            var b = PowerVectorKey.Dense(new[] { 1, 2, 4 });
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void EmptyPowersShouldWork()
        {
            var key = PowerVectorKey.Dense(Array.Empty<int>());
            Assert.True(key.IsDense);
        }

        [Fact]
        public void SingleSymbolShouldWork()
        {
            var key = PowerVectorKey.Sparse(new[] { 0 }, new[] { 2 });
            Assert.True(key.IsSparse);
        }

        [Fact]
        public void SparseSizeMismatchShouldThrow()
        {
            Assert.Throws<ArgumentException>(() =>
                PowerVectorKey.Sparse(new[] { 0, 1 }, new[] { 2 }));
        }

        [Fact]
        public void ModeSelectionShouldFollowThreshold()
        {
            var symbols = new Dictionary<ISymbol, int> { [X] = 0, [Y] = 1, [Z] = 2 };
            var powers = new Dictionary<ISymbol, int> { [X] = 1, [Y] = 2 };
            // 2 out of 3 symbols = 67% > 50% threshold => dense
            var key = PowerVectorKey.Create(powers, symbols, 3);
            Assert.True(key.IsDense);
        }

        [Fact]
        public void SmallSymbolSetShouldAlwaysUseDenseMode()
        {
            var symbols = new Dictionary<ISymbol, int> { [X] = 0, [Y] = 1 };
            var powers = new Dictionary<ISymbol, int> { [X] = 1 };
            // totalSymbols <= 5 => always dense
            var key = PowerVectorKey.Create(powers, symbols, 2);
            Assert.True(key.IsDense);
        }

        [Fact]
        public void ToStringShouldShowMode()
        {
            var dense = PowerVectorKey.Dense(new[] { 1, 2 });
            Assert.Contains("dense", dense.ToString());

            var sparse = PowerVectorKey.Sparse(new[] { 0 }, new[] { 3 });
            Assert.Contains("sparse", sparse.ToString());
        }
    }
}
