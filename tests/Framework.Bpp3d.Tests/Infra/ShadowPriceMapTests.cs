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
        // **按名字拒绝**（B844）：同一个映射里 `DataNotFound` 也能让 `IsFailed` 变绿，只断言"失败"等于什么都没断言。
        // **Rejected by name** (B844): `DataNotFound` would also satisfy a bare `IsFailed`, so asserting only "failed" asserts nothing.
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Code.Should().Be(ErrorCode.IllegalArgument);
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Contain("null");
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
        ((Failed<Flt64, ErrorCode, Error<ErrorCode>>)result).Error.Code.Should().Be(ErrorCode.IllegalArgument);
    }

    [Fact]
    public void PriceOfUnregisteredKeyShouldReturnFailed() {
        var map = new TestShadowPriceMap();
        var key = new DemandShadowPriceKey.ByMaterial("M999");

        Result<Flt64, ErrorCode, Error<ErrorCode>> result = map.PriceOf(key);

        result.IsFailed.Should().BeTrue();
        // **这个键不是 null，所以这里必须是 `DataNotFound`**（B844）：换成 `IllegalArgument` 说明守卫顺序被改了。
        // **The key is not null, so this must be `DataNotFound`** (B844): an `IllegalArgument` here means the guard order changed.
        ((Failed<Flt64, ErrorCode, Error<ErrorCode>>)result).Error.Code.Should().Be(ErrorCode.DataNotFound);
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
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Code.Should().Be(ErrorCode.IllegalArgument);
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
