#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.LayerGeneration;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain.LayerGeneration;

public class LayerGenerationTests {
    private sealed class StubAdapter : IProgramCandidateAdapter<FltX> {
        public string Name => "Stub";
        private readonly IReadOnlyList<string> _candidates;
        private readonly bool _fail;

        public StubAdapter(IReadOnlyList<string>? candidates = null, bool fail = false) {
            _candidates = candidates ?? Array.Empty<string>();
            _fail = fail;
        }

        public Task<Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>>> GenerateCandidatesAsync(
            CancellationToken cancellationToken = default) {
            if (_fail) {
                return Task.FromResult(
                    (Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>>)
                    new Failed<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>>(
                        new Err<ErrorCode>(ErrorCode.Unknown, "stub failure")));
            }
            return Task.FromResult(
                Results.Ok<IReadOnlyList<string>>(_candidates));
        }
    }

    // ===== LayerGenerationContext =====

    [Fact]
    public void LayerGenerationContextShouldBeInstantiable() {
        var ctx = new LayerGenerationContext();
        ctx.Should().NotBeNull();
    }

    // ===== LayerGenerationProgramCandidateAdapters =====

    [Fact]
    public void AdaptersShouldStartEmpty() {
        var adapters = new LayerGenerationProgramCandidateAdapters<FltX>();
        adapters.Adapters.Should().BeEmpty();
    }

    [Fact]
    public void RegisterShouldAddAdapter() {
        var adapters = new LayerGenerationProgramCandidateAdapters<FltX>();
        var stub = new StubAdapter();

        adapters.Register(stub);

        adapters.Adapters.Should().HaveCount(1);
        adapters.Adapters[0].Should().Be(stub);
    }

    [Fact]
    public async Task GenerateAllCandidatesShouldReturnEmptyWhenNoAdapters() {
        var adapters = new LayerGenerationProgramCandidateAdapters<FltX>();

        Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>> result = await adapters.GenerateAllCandidatesAsync();

        result.IsFailed.Should().BeFalse();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAllCandidatesShouldCollectFromAllAdapters() {
        var adapters = new LayerGenerationProgramCandidateAdapters<FltX>();
        adapters.Register(new StubAdapter(new[] { "A", "B" }));
        adapters.Register(new StubAdapter(new[] { "C" }));

        Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>> result = await adapters.GenerateAllCandidatesAsync();

        result.IsFailed.Should().BeFalse();
        result.Value.Should().BeEquivalentTo(new[] { "A", "B", "C" });
    }

    [Fact]
    public async Task GenerateAllCandidatesShouldFailIfAnyAdapterFails() {
        var adapters = new LayerGenerationProgramCandidateAdapters<FltX>();
        adapters.Register(new StubAdapter(new[] { "A" }));
        adapters.Register(new StubAdapter(fail: true));

        Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>> result = await adapters.GenerateAllCandidatesAsync();

        result.IsFailed.Should().BeTrue();
    }

    // ===== IProgramCandidateAdapter =====

    [Fact]
    public void StubAdapterNameShouldBeStub() {
        var adapter = new StubAdapter();
        adapter.Name.Should().Be("Stub");
    }
}
