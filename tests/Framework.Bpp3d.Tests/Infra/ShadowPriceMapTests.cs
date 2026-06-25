#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Infra;

public class ShadowPriceMapTests {
    // Concrete subclass for testing the abstract base
    private sealed class TestShadowPriceMap : AbstractBpp3dShadowPriceMap { }

    [Fact]
    public void RegisterShouldAddPriceSuccessfully() {
        var map = new TestShadowPriceMap();
        var key = new DemandShadowPriceKey.ByMaterial("M001");

        Result<Success, ErrorCode, Error<ErrorCode>> result = map.Register(key, new Flt64(3.14));

        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        map.Prices.Should().ContainKey(key);
        map.Prices[key].ToDouble().Should().BeApproximately(3.14, 1e-9);
    }

    [Fact]
    public void RegisterNullKeyShouldReturnFailed() {
        var map = new TestShadowPriceMap();

        Result<Success, ErrorCode, Error<ErrorCode>> result = map.Register(null!, new Flt64(1.0));

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void PriceOfShouldReturnRegisteredPrice() {
        var map = new TestShadowPriceMap();
        var key = new DemandShadowPriceKey.ByMaterial("M001");
        map.Register(key, new Flt64(2.5));

        Result<Flt64, ErrorCode, Error<ErrorCode>> result = map.PriceOf(key);

        result.Should().BeOfType<Ok<Flt64, ErrorCode, Error<ErrorCode>>>();
        result.Value.ToDouble().Should().BeApproximately(2.5, 1e-9);
    }

    [Fact]
    public void PriceOfNullKeyShouldReturnFailed() {
        var map = new TestShadowPriceMap();

        Result<Flt64, ErrorCode, Error<ErrorCode>> result = map.PriceOf(null!);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void PriceOfUnregisteredKeyShouldReturnFailed() {
        var map = new TestShadowPriceMap();
        var key = new DemandShadowPriceKey.ByMaterial("M999");

        Result<Flt64, ErrorCode, Error<ErrorCode>> result = map.PriceOf(key);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void RegisterShouldOverwriteExistingPrice() {
        var map = new TestShadowPriceMap();
        var key = new DemandShadowPriceKey.ByMaterial("M001");
        map.Register(key, new Flt64(1.0));
        map.Register(key, new Flt64(2.0));

        map.PriceOf(key).Value.ToDouble().Should().BeApproximately(2.0, 1e-9);
    }

    [Fact]
    public void RegisterRangeShouldAddMultiplePrices() {
        var map = new TestShadowPriceMap();
        KeyValuePair<DemandShadowPriceKey, Flt64>[] entries = new[] {
            KeyValuePair.Create<DemandShadowPriceKey, Flt64>(
                new DemandShadowPriceKey.ByMaterial("M001"), new Flt64(1.0)),
            KeyValuePair.Create<DemandShadowPriceKey, Flt64>(
                new DemandShadowPriceKey.ByMaterial("M002"), new Flt64(2.0)),
        };

        Result<Success, ErrorCode, Error<ErrorCode>> result = map.RegisterRange(entries);

        result.IsFailed.Should().BeFalse();
        map.Prices.Should().HaveCount(2);
    }

    [Fact]
    public void RegisterRangeShouldFailIfAnyKeyIsNull() {
        var map = new TestShadowPriceMap();
        var entries = new KeyValuePair<DemandShadowPriceKey, Flt64>[] {
            KeyValuePair.Create<DemandShadowPriceKey, Flt64>(
                new DemandShadowPriceKey.ByMaterial("M001"), new Flt64(1.0)),
            KeyValuePair.Create<DemandShadowPriceKey, Flt64>(null!, new Flt64(2.0)),
        };

        Result<Success, ErrorCode, Error<ErrorCode>> result = map.RegisterRange(entries);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void ClearShouldRemoveAllPrices() {
        var map = new TestShadowPriceMap();
        map.Register(new DemandShadowPriceKey.ByMaterial("M001"), new Flt64(1.0));
        map.Register(new DemandShadowPriceKey.ByMaterial("M002"), new Flt64(2.0));

        map.Clear();

        map.Prices.Should().BeEmpty();
    }

    // ===== DemandShadowPriceKey =====

    [Fact]
    public void ByMaterialShouldStoreMaterialNo() {
        var key = new DemandShadowPriceKey.ByMaterial("M001");
        key.MaterialNo.Should().Be("M001");
    }

    [Fact]
    public void ByPackageShouldStoreMaterialNoAndPackageKey() {
        var key = new DemandShadowPriceKey.ByPackage("M001", "PKG-A");
        key.MaterialNo.Should().Be("M001");
        key.PackageAttributeKey.Should().Be("PKG-A");
    }

    [Fact]
    public void ByMaterialAndByPackageShouldNotBeEqual() {
        var k1 = new DemandShadowPriceKey.ByMaterial("M001");
        var k2 = new DemandShadowPriceKey.ByPackage("M001", "PKG-A");
        k1.Should().NotBe(k2);
    }
}
