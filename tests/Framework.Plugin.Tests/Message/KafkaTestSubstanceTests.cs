#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Message;

/// <summary>
/// **"有方法"与"能用"之间的距离** / The distance between "has a method" and "works".
/// </summary>
/// <remarks>
/// **B1266 查的是矩阵上 Kafka 那一行："有生产者/消费者，<b>没有真实 broker 往返测试</b>"。**
///
/// **查完之后那句话是对的，但它漏掉了更糟的一层：**
///
/// **整个 `KafkaInterfaceTests.cs` 的 <b>8 条判据全部只检查"某个成员在不在"</b>**
/// （`IDisposable` 实现了没有、`Send` 方法存不存在、`SendJson` 方法存不存在……）。
///
/// **没有一条会调用任何一个方法，因此没有一条会因为行为错误而变红。**
///
/// ### 为什么这与"没有 broker 往返测试"不是同一件事
///
/// **"没有往返测试"是一个<b>已知的、被写下来的</b>缺口——矩阵上标着 ◐，读者知道要打折扣。**
///
/// **而"8 条判据全是成员存在性检查"是一个<b>看起来有覆盖</b>的缺口：
/// 文件名是 `KafkaInterfaceTests`，8 条全绿，而它们合起来对行为一无所知。**
///
/// **后者更危险，因为它给出的是"这块测过了"的印象。**
///
/// **一条只会因为<b>删掉一个方法</b>而变红的判据，本质上是一份编译期检查的副本——
/// 而编译器本来就在做那件事。**
///
/// **B1266 checks the matrix's Kafka row: "a producer and a consumer exist, <b>no real broker round trip</b>".**
///
/// **After checking, that sentence is true but misses a worse layer: all <b>8 criteria in `KafkaInterfaceTests.cs` only
/// check whether a member exists</b> (`IDisposable` implemented, `Send` present, `SendJson` present...).** **Not one invokes
/// any method, so not one can go red on wrong behaviour.**
///
/// **Why that differs from "no round-trip test": the missing round trip is a <b>known, recorded</b> gap — the matrix says ◐
/// and the reader knows to discount it.** **"Eight criteria are all member-existence checks" is a gap that <b>looks like
/// coverage</b>: the file is called `KafkaInterfaceTests`, all eight are green, and together they know nothing about
/// behaviour.** **The latter is more dangerous because it gives the impression this area is tested.**
///
/// **A criterion that can only go red when a method is <b>deleted</b> is a copy of a compile-time check — and the compiler
/// already does that.**
/// </remarks>
public class KafkaTestSubstanceTests
{
    /// <summary>
    /// **那 8 条判据里没有一条调用被测类型** / None of those 8 criteria invokes the type under test.
    /// </summary>
    /// <remarks>
    /// **判据的做法是"在测试文件里找有没有一次调用"，而不是读断言的名字。**
    ///
    /// **它现在必须为真（因为没有调用），而它<b>在有人补上一条真实调用时会红</b>。**
    /// **那时该做的是把这条改写成"至少 N 条调用"——缺口判据的正确形态是<b>闭合时提醒改写</b>，
    /// 而不是闭合后仍然绿。**
    ///
    /// **The criterion looks for an actual invocation in the test file rather than reading assertion names.**
    ///
    /// **It must be true now (there is none) and it <b>goes red the moment someone adds a real call</b>.** **Then the job is to
    /// rewrite it as "at least N invocations" — a gap criterion's correct shape is to <b>demand its own rewrite upon
    /// closing</b>, not to stay green after.**
    /// </remarks>
    [Fact]
    public void TheInterfaceTests_ShouldStillContainNoRealInvocation() {
        string path = System.IO.Path.Combine(
            RepositoryRoot(), "tests/Framework.Plugin.Tests/Message/KafkaInterfaceTests.cs");
        string source = File.ReadAllText(path);

        // **"调用"的判据：出现 `new KafkaProducer(` / `new KafkaConsumer(` / `.Send(` / `.SendJson(` / `await `。**
        // **成员存在性检查只会出现 `typeof(...)`、`GetMethod(...)`、`NotBeNull`，不会出现这些。**
        //
        // **The marker of an invocation: `new KafkaProducer(`, `new KafkaConsumer(`, `.Send(`, `.SendJson(`, `await `.
        // Existence checks only ever contain `typeof(...)`, `GetMethod(...)`, `NotBeNull`.**
        string[] invocations = [
            "new KafkaProducer(",
            "new KafkaConsumer(",
            ".Send(",
            ".SendJson(",
            "await ",
        ];

        bool hasInvocation = invocations.Any(marker =>
            source.Contains(marker, StringComparison.Ordinal));

        hasInvocation.Should().BeFalse(
            "all 8 criteria are member-existence checks; the first real invocation must make this criterion red "
            + "so that it gets rewritten into a behavioural one");
    }

    /// <summary>
    /// **矩阵那一行必须同时说出这两层** / The matrix row must name both layers.
    /// </summary>
    /// <remarks>
    /// **"没有 broker 往返测试"与"8 条判据全是成员存在性检查"是两层不同的缺口，
    /// 而后者才是让前者看起来已经补上的原因。**
    ///
    /// **这一条同时钉住矩阵、测试文件与判据三者的对应关系——
    /// 因为一旦三者只剩两者，读者就无法判断自己看到的绿灯意味着什么。**
    ///
    /// **"No broker round trip" and "all 8 criteria are existence checks" are two different gaps, and the second is what makes
    /// the first look covered.** **This pins the correspondence among the matrix, the test file and the criterion — with only
    /// two of the three, a reader cannot tell what a green light means.**
    /// </remarks>
    [Fact]
    public void TheMatrixRow_ShouldNameBothLayers() {
        string row = File
            .ReadAllLines(System.IO.Path.Combine(RepositoryRoot(), "docs/kotlin-capability-matrix.md"))
            .First(static line => line.StartsWith("| 消息插件（Kafka）", StringComparison.Ordinal));

        row.Should().Contain(
            "没有真实 broker 往返测试",
            "the known gap must stay stated");
        row.Should().Contain(
            "成员存在性",
            "and the worse gap — that the existing 8 criteria prove nothing about behaviour — must be stated too");
    }

    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(System.IO.Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}
