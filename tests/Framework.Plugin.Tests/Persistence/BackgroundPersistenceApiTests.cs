#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence;
using MongoDB.Driver;
using System;
using System.Threading;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence;

/// <summary>
/// **后台处理重载** / The background-processing overload.
/// </summary>
/// <remarks>
/// 判据的重点：**同步响应立即返回且与处理结果类型不同**、**处理在后台跑、不阻塞返回**、
/// **Mongo 未配置时持久化静默跳过但处理与回调照常**。
///
/// The criteria focus on **the synchronous response returning at once with a type distinct from the processed result**, on **processing running in the background without blocking the return**, and
/// on **persistence being skipped silently when Mongo is unconfigured while processing and the callback proceed**.
/// </remarks>
public class BackgroundPersistenceApiTests {
    /// <summary>处理结果 / The processed result.</summary>
    public sealed record Processed(int Length);

    /// <summary>同步响应 / The synchronous response.</summary>
    public sealed record Accepted(string Tag);

    private sealed class Controller : IPersistenceApiController {
        // Mongo 未配置：整条持久化路径必须静默跳过。
        // Mongo is unconfigured, so the whole persistence path must be skipped silently.
        public IMongoDatabase? MongoClient => null;
    }

    /// <summary>
    /// **同步响应立即返回，处理在后台完成** / The synchronous response returns at once while processing finishes in the background.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 返回值**就是**传入的 `syncResponse`（不是处理结果）。
    /// 2. 返回时 `process` **还没跑**（后台排队中）。
    /// 3. 稍后 `process` 跑完，`asyncResponse` 收到**处理结果**而不是同步响应。
    /// 4. `syncResponse` 与处理结果**类型不同**：一个 `string`，一个 `int`。
    ///
    /// Four paired points: the return value **is** the `syncResponse` passed in rather than the processed result; `process` **has not run yet** at the moment of return, still queued in the background;
    /// later `process` completes and `asyncResponse` receives the **processed result**, not the synchronous one; and the two are of **different types**, a `string` and an `int`.
    /// </remarks>
    [Fact]
    public void PersistenceApiImplAsync_ShouldReturnAtOnceAndProcessInTheBackground() {
        IPersistenceApiController controller = new Controller();
        int processed = 0;
        Processed? callbackValue = null;
        var completed = new ManualResetEventSlim(false);
        using var gate = new ManualResetEventSlim(false);

        Accepted returned = controller.PersistenceApiImplAsync<string, Processed, Accepted>(
            api: "api", app: "app", requester: "me", version: "1",
            request: "payload",
            process: request => {
                gate.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
                Interlocked.Increment(ref processed);
                return new Processed(request.Length);
            },
            asyncResponse: value => {
                Volatile.Write(ref callbackValue, value);
                completed.Set();
            },
            syncResponse: new Accepted("accepted"));

        returned.Tag.Should().Be("accepted", "the caller gets the synchronous response, not the processed result");
        Volatile.Read(ref processed).Should().Be(0,
            "processing sits off the return path, so it cannot have run while the gate is shut");

        gate.Set();
        completed.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("the background task must eventually finish");
        Volatile.Read(ref callbackValue).Should().Be(new Processed(7), "the callback receives the processed result");
        Volatile.Read(ref processed).Should().Be(1);
    }

    /// <summary>
    /// **同步处理重载仍返回处理结果** / The synchronous-processing overload still returns the processed result.
    /// </summary>
    /// <remarks>
    /// 两点成对：同步重载返回的是 **`process` 的返回值**；且 `process` 在返回**之前**就已经跑过——
    /// 两个重载的差别正是"处理在不在返回路径上"，不是"名字差一个 Async"。
    ///
    /// Two paired points: the synchronous overload returns **`process`'s value**, and `process` has already run **before** the return — the difference between the two overloads is precisely whether
    /// processing sits on the return path, not a suffix in the name.
    /// </remarks>
    [Fact]
    public void PersistenceApiImpl_ShouldReturnTheProcessedValue() {
        IPersistenceApiController controller = new Controller();
        int processed = 0;

        Processed returned = controller.PersistenceApiImpl<string, Processed>(
            api: "api", app: "app", requester: "me", version: "1",
            request: "payload",
            process: request => {
                Interlocked.Increment(ref processed);
                return new Processed(request.Length);
            });

        returned.Length.Should().Be(7);
        Volatile.Read(ref processed).Should().Be(1, "the synchronous overload processes on the return path");
    }
}
