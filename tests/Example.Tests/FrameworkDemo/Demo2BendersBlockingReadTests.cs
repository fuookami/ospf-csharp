#nullable enable

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// demo2 的 Benders 结果**一次取值**，不用 `.Wait` + `.Result` / demo2 reads the Benders result **once**, not via `.Wait` + `.Result`.
/// </summary>
/// <remarks>
/// `Task.Wait` 把异常包成 `AggregateException`，于是调用方 `catch (SomeSpecificException)` 接不到，日志里也只剩一层壳；
/// 而 `.Wait` 之后再读 `.Result` 是**第二次访问同一个任务**。`GetAwaiter.GetResult` 原样抛出、只读一次。
///
/// **阻塞仍然存在，这里没有消除它**：`FullLoadApplication.Execute` 是同步入口，改 async 会波及所有调用方。
/// 判据只钉"不再用那两个成员"，不假装整链已经异步化。
///
/// `Task.Wait` wraps exceptions in `AggregateException`, so `catch (SomeSpecificException)` never matches and the log keeps only a shell; and reading `.Result` after
/// `.Wait` is **a second access to the same task**. `GetAwaiter.GetResult` rethrows the original and reads once. **The blocking remains — not remove
/// it**: `Execute` is a sync entry and making it async would ripple through every caller. The criterion pins only "those two members are gone"; it does not pretend the
/// whole chain became asynchronous.
/// </remarks>
public sealed class Demo2BendersBlockingReadTests
{
    /// <summary>**不许再用 `.Wait` + `.Result`** / The `.Wait` + `.Result` pair must not come back.</summary>
    [Fact]
    public void TheBendersRead_ShouldNotUseWaitAndResult() {
        string code = CodeLines();

        code.Should().NotContain(".Wait()", "Wait() wraps the real exception in AggregateException, so a specific catch never matches");
        code.Should().NotContain("bendersResultTask", "a task held in a local tempts the Wait-then-Result pair back in");
    }

    /// <summary>**必须一次取值** / It must read the result once.</summary>
    [Fact]
    public void TheBendersRead_ShouldTakeTheResultOnce() {
        CodeLines().Should().Contain("GetAwaiter().GetResult()",
            "the sync entry needs one blocking read that rethrows the original exception");
    }

    /// <summary>只取代码行 / Code lines only.</summary>
    /// <returns>内容 / The content.</returns>
    /// <remarks>
    /// 注释里写着 `.Wait` 与 `.Result` 这两个名字，算进来会红在自己的说明上—— / 的同一条教训。
    ///
    /// The comments name `.Wait` and `.Result` (the earlier explanation); counting them would red the criterion on its own text — /the earlier lesson, again.
    /// </remarks>
    private static string CodeLines() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            string candidate = Path.Combine(
                directory.FullName, "src", "Example", "FrameworkDemo", "Demo2", "Application", "FullLoadApplication.cs");
            if (File.Exists(candidate)) {
                var code = new System.Text.StringBuilder();
                foreach (string line in File.ReadAllLines(candidate)) {
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) {
                        continue;
                    }
                    code.AppendLine(line);
                }
                return code.ToString();
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException("could not locate FullLoadApplication.cs");
    }
}
