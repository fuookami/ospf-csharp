#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

public class ConcurrentTokenTableTests {
    [Fact]
    public void ConcurrentAutoTokenTable_AddAndFind() {
        var table = new ConcurrentAutoTokenTable<Flt64>(
            LinearCategory.Instance,
            new List<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());

        var var1 = new RealVar("x1");
        Result<Success, ErrorCode, Error<ErrorCode>> result = table.Add(var1);
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Ok<
            Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();

        Token<Flt64>? found = table.TokenList.Find(var1);
        found.Should().NotBeNull();
        found!.Name.Should().Be("x1");
    }

    [Fact]
    public void ConcurrentManualTokenTable_AddAndFind() {
        var table = new ConcurrentManualTokenTable<Flt64>(
            LinearCategory.Instance,
            new List<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());

        var var1 = new RealVar("y1");
        table.Add(var1);

        Token<Flt64>? found = table.TokenList.Find(var1);
        found.Should().NotBeNull();
        found!.Name.Should().Be("y1");
    }

    [Fact]
    public async Task ConcurrentTokenTable_ThreadSafety_CacheOperations() {
        var table = new ConcurrentAutoTokenTable<Flt64>(
            LinearCategory.Instance,
            new List<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());

        var errors = new ConcurrentBag<Exception>();
        var tasks = new List<Task>();

        // Concurrent cache put/get operations
        for (int i = 0; i < 10; i++) {
            int localI = i;
            tasks.Add(Task.Run(() => {
                try {
                    string key = $"key_{localI}";
                    var value = new Flt64(localI);
                    table.Cache(key, (IReadOnlyList<Flt64>?)null, value);
                    Flt64? cached = table.CachedValue(key, (IReadOnlyList<Flt64>?)null);
                    cached.Should().NotBeNull();
                }
                catch (Exception ex) {
                    errors.Add(ex);
                }
            }));
        }

        await Task.WhenAll(tasks);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ConcurrentTokenTable_Flush_ClearsAllCaches() {
        var table = new ConcurrentAutoTokenTable<Flt64>(
            LinearCategory.Instance,
            new List<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());

        table.Cache("test", (IReadOnlyList<Flt64>?)null, Flt64.One);
        table.CachedValue("test", (IReadOnlyList<Flt64>?)null).Should().Be(Flt64.One);

        table.Flush();
        table.CachedValue("test", (IReadOnlyList<Flt64>?)null).Should().BeNull();
    }

    [Fact]
    public void ConcurrentTokenTable_LinearFlattenCache() {
        var table = new ConcurrentAutoTokenTable<Flt64>(
            LinearCategory.Instance,
            new List<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());

        table.CachedLinearFlatten("test").Should().BeFalse();
        table.CacheLinearFlatten("test", null);
        table.CachedLinearFlatten("test").Should().BeTrue();

        LinearFlattenData<Flt64>? cleared = table.ClearLinearFlatten("test");
        table.CachedLinearFlatten("test").Should().BeFalse();
    }

    [Fact]
    public void ConcurrentTokenTable_Dispose_ClearsAll() {
        var table = new ConcurrentAutoTokenTable<Flt64>(
            LinearCategory.Instance,
            new List<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());

        table.Cache("key", (IReadOnlyList<Flt64>?)null, Flt64.One);
        table.Dispose();
        // After dispose, cache should be cleared
        table.CachedValue("key", (IReadOnlyList<Flt64>?)null).Should().BeNull();
    }
}
