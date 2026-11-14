// 调度日志往返的复现与验证 / Reproduction and verification of the schedule-log round trip.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic.Persistence;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 调度日志往返 / The schedule-log round trip.
/// </summary>
public sealed class ScheduleLogRoundTripTests {
    /// <summary>**带校验和的日志能原样往返** / A log carrying its checksum survives a serialisation round trip.</summary>
    [Fact]
    public void ScheduleLog_ShouldRoundTripThroughJson() {
        // **夹具必须满足日志的状态机**：`begin` → `dispatch` → `complete` → `yield` → `end`。
        // 用 "select"/"allocate" 这类自造事件名，`Validate` 会在"事件顺序非法"处失败——而报错位置看起来像校验和问题。
        // **The fixture must satisfy the log's state machine**: `begin` → `dispatch` → `complete` → `yield` → `end`.
        // Invented event names like "select"/"allocate" make `Validate` fail on "invalid event ordering" — which reads like a checksum problem.
        ScheduleEvent[] events = {
            new ScheduleEvent("begin"),
            new ScheduleEvent("dispatch", 0L, "a", 5L, 1L, 11L, new[] { "a", "b" }),
            new ScheduleEvent("complete", 0L, "a", 5L, 1L),
            new ScheduleEvent("yield", 1L),
            new ScheduleEvent("end", 1L, "", 5L, 1L),
        };
        ScheduleLog source = ScheduleLogs.Of("config", "problem", events);
        ScheduleLog sealedLog = source with { Checksum = source.Digest() };

        sealedLog.Validate().Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>("手工校验和应当自洽 / the hand-computed checksum must be self-consistent");

        (sealedLog with { Checksum = "0000000000000000000000000000000000000000000000000000000000000000" })
            .Validate().Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
                "校验和不符必须失败 / a wrong checksum must fail");

        string json = JsonSerializer.Serialize(sealedLog);
        ScheduleLog restored = JsonSerializer.Deserialize<ScheduleLog>(json)!;

        restored.Events.Should().HaveCount(sealedLog.Events.Count);
        restored.Digest().Should().Be(sealedLog.Checksum,
            "往返后重算的摘要必须与原来的校验和一致 / the recomputed digest must equal the original checksum after a round trip");
        restored.Validate().Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**校验和必须覆盖到每一个事件字段** / The checksum must cover every event field.</summary>
    /// <remarks>
    /// 只改一个不参与摘要的字段，`Validate` 仍然通过——**那就是"校验和形同虚设"**。
    /// 逐字段改一遍再比对摘要，是唯一能证明"覆盖住了"的做法。
    ///
    /// Change one field that the digest does not cover and `Validate` still passes — **that is a checksum in name only**. Mutating each field in turn and
    /// comparing digests is the only way to show it is actually covered.
    /// </remarks>
    [Fact]
    public void Fingerprint_ShouldCoverEveryEventField() {
        var baseEvent = new ScheduleEvent("select", 3L, "a", 5L, 1L, 7L, new[] { "a" }, false, false);
        string baseline = ScheduleLogs.Of("cfg", "prob", new[] { baseEvent }).Digest();

        string Changed(ScheduleEvent changed) =>
            ScheduleLogs.Of("cfg", "prob", new[] { changed }).Digest();

        Changed(baseEvent with { Kind = "allocate" }).Should().NotBe(baseline);
        Changed(baseEvent with { Decision = 4L }).Should().NotBe(baseline);
        Changed(baseEvent with { Action = "b" }).Should().NotBe(baseline);
        Changed(baseEvent with { Evaluations = 6L }).Should().NotBe(baseline);
        Changed(baseEvent with { Steps = 2L }).Should().NotBe(baseline);
        Changed(baseEvent with { Random = 8L }).Should().NotBe(baseline);
        Changed(baseEvent with { Available = new[] { "b" } }).Should().NotBe(baseline);
        Changed(baseEvent with { Stopped = true }).Should().NotBe(baseline);
        Changed(baseEvent with { Failed = true }).Should().NotBe(baseline);
    }

    /// <summary>**调度日志可以经存储层往返** / A schedule log survives the storage layer's round trip.</summary>
    /// <remarks>
    /// Batch 192 曾记录"往返损坏"并因此跳过这条测试。本批把那句话当作待验证的假设重新测一次：
    /// **要么它成立（那么这是真缺陷），要么它不成立（那么记录本身就是错的）**，两种结果都必须写下来。
    ///
    /// Batch 192 recorded a "broken round trip" and skipped this test because of it. This batch treats that claim as a hypothesis to re-test:
    /// **either it holds (a real defect) or it does not (the record itself was wrong)**, and both outcomes must be written down.
    /// </remarks>
    [Fact]
    public void Store_ShouldRoundTripSchedule() {
        string root = Path.Combine(Path.GetTempPath(), "ospf-schedule-" + Guid.NewGuid().ToString("N"));
        try {
            var store = new FileSearchStore(root);
            ScheduleEvent[] events = {
                new ScheduleEvent("begin"),
                new ScheduleEvent("dispatch", 0L, "a", 5L, 1L, 11L, new[] { "a" }),
                new ScheduleEvent("complete", 0L, "a", 5L, 1L),
                new ScheduleEvent("yield", 1L),
                new ScheduleEvent("end", 1L, "", 5L, 1L),
            };
            ScheduleLog source = ScheduleLogs.Of("config", "problem", events);
            ScheduleLog sealedLog = source with { Checksum = source.Digest() };

            store.SaveSchedule("one", sealedLog).Should()
                .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
            Result<ScheduleLog, ErrorCode, Error<ErrorCode>> loaded = store.LoadSchedule("one");
            loaded.Should().BeOfType<Ok<ScheduleLog, ErrorCode, Error<ErrorCode>>>();
            ScheduleLog restored = ((Ok<ScheduleLog, ErrorCode, Error<ErrorCode>>)loaded).Value;
            restored.Checksum.Should().Be(sealedLog.Checksum);
            restored.Events.Should().HaveCount(5);
        } finally {
            if (Directory.Exists(root)) {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
