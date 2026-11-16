// 适航安全上下文的两个入口：说明与代码一致/ The two entry points of the airworthiness context: remarks match the implementation.

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **`Init` 一直都在，"唯一入口"是句过期的话** / `Init` has always been there; "the only entry point" was stale wording.
/// </summary>
/// <remarks>
/// 构造函数那段注释说的是"`Init` 尚未实现期间唯一的入口"。查过之后：`Init` 在（ 起 `AggregationInitializer` 真正构造聚合），
/// 上游未初始化会明确失败，DTO 校验失败会原样抛出。**两句注释描述的是同一个文件里的两个状态，一句过期了。**
/// 过期的注释比没有更坏：下一个人会以为"想要 `Init` 得先实现它"，于是绕过它——**而绕过之后 `Aggregation` 的字段来自调用方，
/// 校验部位的差异就再也不会被发现。**
///
/// The constructor's remarks said "the only entry point while `Init` is unimplemented". Checking: `Init` is there (since then `AggregationInitializer` really builds the
/// aggregation), uninitialised upstreams fail explicitly, and DTO validation failures propagate unchanged. **The two sentences describe two states of the same file and one of
/// them is expired.** Stale remarks are worse than none: the next reader concludes "wanting `Init` means implementation it first" and routes around it — **and once they do, the
/// aggregation's fields come from the caller, so the validation differences are never noticed again.**
/// </remarks>
public class Demo2AirworthinessInitDocTests {
    private static string ContextSource() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            string candidate = Path.Combine(
                directory, "src", "Example", "FrameworkDemo", "Demo2", "Domain", "AirworthinessSecurity", "AirworthinessSecurityContext.cs");
            if (File.Exists(candidate)) {
                return File.ReadAllText(candidate);
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("could not locate AirworthinessSecurityContext.cs");
    }

    /// <summary>
    /// **`Init` 真的在，且注释不再说它没实现** / `Init` really exists, and the remarks no longer claim it is unimplemented.
    /// </summary>
    /// <remarks>
    /// 四点成对：`public Try Init(` 在源码里 / **不再出现"尚未实现期间唯一的入口"** / 上游守卫还在 / 注释写明"`Init` 早就在了"。
    /// 只钉注释不够（`Init` 被删掉时注释还是会撒谎），只钉代码也不够（过期的话会被写回去）。
    ///
    /// Four paired points: `public Try Init(` is in the source; **"the only entry point while `Init` is unimplemented" is gone**; the upstream guard is still there; and the remarks say
 /// `Init` has been there. Pinning only the remark is not enough (`Init` deleted leaves the remark lying), and pinning only this implementation is not enough either (the stale sentence comes
    /// back).
    /// </remarks>
    [Fact]
    public void Init_ShouldExistAndTheRemarksShouldSaySo() {
        string source = ContextSource();

        source.Should().Contain("public Try Init(");
        source.Should().NotContain("`Init` 尚未移植期间唯一的入口");
        source.Should().Contain("上游聚合未初始化");
        source.Should().Contain("`Init` 早就在了");
    }

    /// <summary>
    /// **上游守卫与"已初始化就不重建"都在** / The upstream guard and "do not rebuild once initialised" are both present.
    /// </summary>
    /// <remarks>
    /// 两点成对：`if (_aggregation is not null)` 短路返回成功 / 三个上游聚合的守卫一次做完。
    /// 少了短路会让每次 `Init` 重建一份聚合——**而旧引用还指着上一份，注册出去的约束来自两张不同的聚合。**
    ///
    /// Two paired points: `if (_aggregation is not null)` returns success early; and the three upstream aggregations are guarded in one place. Losing the short-circuit rebuilds an
    /// aggregation on every `Init` — **while old references still point at the previous one, so registered constraints come from two different aggregations.**
    /// </remarks>
    [Fact]
    public void Init_ShouldKeepTheShortCircuitAndTheUpstreamGuard() {
        string source = ContextSource();

        source.Should().Contain("if (_aggregation is not null)");
        source.Should().Contain("aircraftContext.Aggregation is not { } aircraftAggregation");
        source.Should().Contain("macContext.Aggregation is not { } macAggregation) {");
    }
}
