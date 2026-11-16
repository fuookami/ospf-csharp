// 限度类空壳不得静默成功 / Limit stubs must not silently succeed.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **未接线就失败，不要静默成功** / Fail while unwired; never silently succeed.
/// </summary>
/// <remarks>
/// 一个返回 `Ok` 的空壳等于宣称"该约束已施加且未违反"——而实际什么都没做。
/// 这类缺陷不报错、不崩、只是结果偏掉，是最难查的一种。
/// 就是在一条约束上撞到这件事（`Missing executor compilation expression` 之后才发现空壳一直在静默通过），
/// 这里把这条规则扩到整个 `Service/Limits` 目录。
///
/// An empty stub returning `Ok` claims "this constraint was applied and is satisfied" while doing nothing at all. The defect raises nothing, crashes nothing and merely skews the
/// result — the hardest kind to find. hit exactly this on one constraint; extends the rule to the whole `Service/Limits` directory.
/// </remarks>
public class NoSilentLimitStubTests {
    /// <summary>源代码路径 / The source path.</summary>
    private static readonly string LimitsDirectory = LocateLimits();

    /// **没有任何限度类用一行 `Results.Ok` 当实现** / No limit class is implemented as a one-line `Ok`.
    [Fact]
    public void NoLimitClass_ShouldReturnOkUnconditionally() {
        List<string> files = Directory.GetFiles(LimitsDirectory, "*.cs").ToList();
        files.Should().NotBeEmpty("the directory must be found, otherwise this criterion checks nothing");

        List<string> offenders = new();
        foreach (string file in files) {
            string text = File.ReadAllText(file);
            // 只认"整个方法体就是 Ok"这一种形态；接了线的实现当然会返回 Ok。
            // Only the "the whole body is Ok" shape counts; a wired implementation naturally returns Ok too.
            if (text.Contains("public Try Invoke(object model) => Results.Ok<Success>(Results.SuccessInstance);")) {
                offenders.Add(Path.GetFileName(file));
            }
        }

        offenders.Should().BeEmpty(
            "an unwired limit must fail loudly; returning Ok claims it was applied");
    }

    /// **空壳返回的是失败而不是成功** / A stub returns a failure rather than success.
    [Fact]
    public void UnwiredLimits_ShouldFailLoudly() {
        List<string> files = Directory.GetFiles(LimitsDirectory, "*.cs").ToList();

        int failing = 0;
        foreach (string file in files) {
            string text = File.ReadAllText(file);
            if (text.Contains("尚未接线，不能当作已施加")) {
                failing++;
            }
        }

        failing.Should().BeGreaterThan(0, "at least the known stubs must fail loudly");
    }

    /// <summary>定位限度类目录 / Locate the limit-class directory.</summary>
    /// <returns>绝对路径 / The absolute path.</returns>
    private static string LocateLimits() {
        DirectoryInfo? current = new(System.AppContext.BaseDirectory);
        while (current is not null) {
            string candidate = Path.Combine(
                current.FullName, "src", "Framework.GanttScheduling", "Domain", "TaskCompilation", "Service", "Limits");
            if (Directory.Exists(candidate)) {
                return candidate;
            }
            current = current.Parent;
        }
        return Path.Combine(System.AppContext.BaseDirectory, "not-found");
    }
}
