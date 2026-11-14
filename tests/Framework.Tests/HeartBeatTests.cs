#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework;
using Fuookami.Ospf.Framework.Log;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using System;
using System.Text;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests;

public class HeartBeatTests {
    [Fact]
    public void LogContext_UsesKotlinNameBasedUuidContract() {
        var context = LogContext.Create(app: "a", version: "b", requestId: "c");

        context.ServiceId.Should().Be("90015098-3cd2-3fb0-9696-3f7d28e17f72");
    }

    [Fact]
    public void LogRecord_CreatesJsonPersistenceObject() {
        var record = LogRecordPo<string>.Create(
            app: "app",
            version: "1.0",
            serviceId: "service",
            step: "step",
            value: "value"
        );

        record.AvailableTime.Should().BeAfter(record.Time);
        Encoding.UTF8.GetString(record.Po.Value).Should().Contain("\"value\"");
    }

    [Fact]
    public void RunningHeartBeat_PreservesValues() {
        var heartbeat = new RunningHeartBeat(
            Id: "run-1",
            RunTime: TimeSpan.FromSeconds(5),
            EstimatedTime: TimeSpan.FromSeconds(10),
            OptimizedRate: new Flt64(0.5),
            Progress: "half",
            Message: "testing"
        );

        heartbeat.Id.Should().Be("run-1");
        heartbeat.OptimizedRate.Should().Be(new Flt64(0.5));
        heartbeat.Time.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void FinnishHeartBeat_ConstructorsMapCodeAndMessage() {
        var success = FinnishHeartBeat.Success("run-1", TimeSpan.FromSeconds(2));
        var failure = FinnishHeartBeat.FromError(
            id: "run-2",
            error: new Err<ErrorCode>(ErrorCode.ApplicationFailed, "failed")
        );

        success.Code.Should().Be(0);
        success.Message.Should().BeEmpty();
        failure.Code.Should().Be((ulong)ErrorCode.ApplicationFailed);
        failure.Message.Should().Be("failed");
        failure.RunTime.Should().Be(TimeSpan.Zero);
    }
}
