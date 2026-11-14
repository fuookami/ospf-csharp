#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 固定整数 LP 后端接口 / The fixed-integer LP backend interface.
/// </summary>
/// <remarks>
/// **它是"需要真实后端"与"只需要一个假后端"之间的分界线。**
/// 有了这个接口，编排逻辑可以用一个假后端跑通；**没有它，唯一能测的路径必须挂一个真求解器。**
///
/// **It is the line between "needs a real backend" and "needs a fake one".** With this interface the orchestration runs against a stub; **without it, the only testable path needs a live solver.**
/// </remarks>
public class FixedIntegerLpBackendTests {
    /// <summary>一个只记录调用次数的假后端 / A stub backend that only counts its calls.</summary>
    private sealed class StubBackend : IFixedIntegerLpBackend {
        public int Calls { get; private set; }

        public FixedIntegerLpRequest? LastRequest { get; private set; }

        public Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>> Next { get; set; } =
            Results.Failed<FixedIntegerLpSolveResult>(new Err<ErrorCode>(ErrorCode.ApplicationError, "stub"));

        public Task<Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
            FixedIntegerLpRequest request,
            CancellationToken cancellationToken = default) {
            ++Calls;
            LastRequest = request;
            return Task.FromResult(Next);
        }
    }

    /// <summary>**接口能被一个假后端实现，且调用真的到达它** / A stub implements it and calls really arrive.</summary>
    /// <remarks>
    /// **这条判据的意义在于"不需要真求解器"**：如果这个接口不存在，这一段根本写不出来，
    /// **而上层的编排就永远只能靠真后端来验。**
    ///
    /// **The point is "no real solver needed"**: without this interface this snippet cannot be written at all, **and the orchestration above can only ever be verified against a live backend.**
    /// </remarks>
    [Fact]
    public async Task TheStubBackend_ShouldReceiveTheCall() {
        var backend = new StubBackend();

        backend.Calls.Should().Be(0, "nothing has been asked yet");

        // **一个空请求就够**：接口只承诺"收到请求、给出结果或错误"。
        // **An empty request suffices**: the interface only promises "take a request, give a result or an error".
        Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>> result =
            await backend.SolveAsync(null!);

        backend.Calls.Should().Be(1, "the call reached the implementation, which is the whole contract");
        result.Should().BeOfType<Failed<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**失败被原样带回，不被改写成成功** / A failure comes back as a failure.</summary>
    /// <remarks>
    /// 一个把后端失败吞成"空敏感性报告"的实现**会让调用方以为敏感性为零**——
    /// **而"零"是一个结论，后端根本没给出来。**
    ///
    /// An implementation that swallows a backend failure into "an empty sensitivity report" **makes the caller think sensitivity is zero** — **and zero is a conclusion the backend never gave.**
    /// </remarks>
    [Fact]
    public async Task TheBackendFailure_ShouldNotBecomeASuccess() {
        var backend = new StubBackend {
            Next = Results.Failed<FixedIntegerLpSolveResult>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, "no duals")),
        };

        Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>> result =
            await backend.SolveAsync(null!);

        result.Should().NotBeOfType<Ok<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>>>(
            "a backend that failed has not answered the question");
    }
}
