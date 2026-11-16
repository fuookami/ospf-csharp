// 心跳回调的判据/ Criteria for the heartbeat callbacks.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using NumUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using Fuookami.Ospf.Utils.Error;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **心跳要在成功和失败时都发** / Heartbeats must fire on success and on failure alike.
/// </summary>
/// <remarks>
/// 四点成对：分派前发一次 `RunningHeartBeat` / 收尾必发一次 `FinnishHeartBeat` / **成功时 `code == 0` 且 `message` 为空** /
/// **失败时 `code != 0` 且 `message` 带得出原因**。
///
/// 第三、四点合起来才是完整的约定：**只报成功的心跳等于没有心跳**——调用方最需要知道的恰恰是"它结束了，而且失败了"。
/// 一个永远不发的回调比一个只在成功时发的回调更难查，因为"没收到"和"还在跑"长得一样。
///
/// 另外钉住 `Id` 一路带过去：心跳不带 id 就没法和请求对上，多请求并发时那份心跳等于废纸。
///
/// Four paired points: one `RunningHeartBeat` before dispatch; always one `FinnishHeartBeat` at the end; **on success `code == 0` with an empty `message`**; and
/// **on failure `code != 0` with a message that carries the reason**. The third and fourth together are the actual contract: **heartbeats that only report success
/// are no heartbeats at all** — what a caller most needs to know is that the run **ended and failed**. A callback that never fires is harder to chase than one that
/// only fires on success, because "nothing received" looks exactly like "still running". The `Id` is pinned all the way through as well: a heartbeat without its id
/// cannot be matched to a request, and under concurrent requests that heartbeat is waste paper.
/// </remarks>
public sealed class Demo2HeartBeatTests {
    [Fact]
    public void Solve_ShouldReportAFinishedBeatWithZeroCodeOnSuccess() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        List<RunningHeartBeatDTO> running = new();
        List<FinnishHeartBeatDTO> finished = new();

        ResponseDTO response = Demo2ApplicationService.Solve(
            request,
            StowageMode.FullLoad,
            running: running.Add,
            finished: finished.Add).Response;

        response.Status.Should().Be("MilpSolverNotSupplied", "this criterion does not need a backend");
        running.Should().ContainSingle("one beat before dispatch");
        running[0].Id.Should().Be(request.Id);
        running[0].RunTime.Should().Be(TimeSpan.Zero, "nothing has run yet when the first beat fires");

        // 没带求解器算失败：**失败也要发收尾心跳**，而且必须带得出原因。
        // Without a solver this counts as a failure: **a failure emits the finishing beat too**, and it has to carry the reason.
        finished.Should().ContainSingle("exactly one beat at the end, on the failure path as well");
        finished[0].Id.Should().Be(request.Id);
        finished[0].Code.Should().NotBe(new NumUInt64(0UL), "a failure must not report the success code");
        finished[0].Message.Should().Contain("MilpSolverNotSupplied");
    }

    [Fact]
    public void Solve_ShouldMapTheFailureStatusToAnErrorCode() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        List<FinnishHeartBeatDTO> finished = new();

        Demo2ApplicationService.Solve(
            request,
            (StowageMode)99,
            finished: finished.Add);

        finished.Should().ContainSingle();
        // 未知模式是**调用方的参数错误**，不是求解引擎的问题：映射成 `IllegalArgument` 才能让调用方分清该改谁。
        // An unknown mode is **the caller's argument error**, not a solver-engine problem: mapping it to `IllegalArgument` is what lets the caller know whose bug it is.
        finished[0].Code.Should().Be(new NumUInt64((ulong)ErrorCode.IllegalArgument));
        finished[0].Message.Should().Contain("UnknownStowageMode");
    }

    [Fact]
    public void FinishedBeat_ShouldReportZeroCodeAndNoMessageWhenBuiltFromSuccess() {
        var beat = FinnishHeartBeatDTO.Finished("req-1", TimeSpan.FromSeconds(2));

        beat.Id.Should().Be("req-1");
        beat.RunTime.Should().Be(TimeSpan.FromSeconds(2));
        beat.Code.Should().Be(new NumUInt64(0UL), "zero means success, which is why the code is the ordinal and not the enum itself");
        beat.Message.Should().BeEmpty();
    }

    [Fact]
    public void FinishedBeat_ShouldCarryTheCodesOrdinalWhenBuiltFromAnError() {
        var error = new Err<ErrorCode>(ErrorCode.ORModelInfeasible, "infeasible");

        var withDuration = FinnishHeartBeatDTO.Failed("req-2", TimeSpan.FromSeconds(3), error);
        var withoutDuration = FinnishHeartBeatDTO.Failed("req-2", error);

        withDuration.Code.Should().Be(new NumUInt64((ulong)ErrorCode.ORModelInfeasible));
        withDuration.Message.Should().Be("infeasible");
        withDuration.RunTime.Should().Be(TimeSpan.FromSeconds(3));
        withoutDuration.RunTime.Should().Be(TimeSpan.Zero, "the duration is simply not known yet on that path");
        withoutDuration.Code.Should().Be(withDuration.Code);
    }
}
