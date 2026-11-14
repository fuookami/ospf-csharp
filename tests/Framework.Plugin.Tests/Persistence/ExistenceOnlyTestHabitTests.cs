#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence;

/// <summary>
/// **同一个习惯，出现在三个文件里** / The same habit, in three files.
/// </summary>
/// <remarks>
/// **B1266 在 `KafkaInterfaceTests.cs` 里发现 8 条判据全是成员存在性检查。B1267 顺着这个形状扫了一遍，
/// 又找到两个同类文件：**
///
/// - **`Persistence/InterfaceVerificationTests.cs`** —— **14 条**
/// - **`Persistence/RedisInterfaceTests.cs`** —— **11 条**
///
/// **三个文件、33 条判据，全部只问"某个成员在不在"。**
///
/// ### 一件事出现在一处是事故，出现在三处是习惯
///
/// **B1266 把 Kafka 那 8 条写成了"这一处的问题"。**
/// **而扫一遍之后它变成了别的东西：<b>这是一个写法习惯</b>——
/// 写测试时先想到"检查它有没有这个方法"，而不是"检查它做对了没有"。**
///
/// **习惯不能用修补单个文件解决，因为下一个文件会照同样的习惯写出来。**
///
/// **所以这一批不是再修一个文件，而是<b>把这个习惯本身钉住**：**
/// **判据断言这三个文件<b>都还是**影子检查。</b>
///
/// **它会随第一个真实行为测试出现而变红——那时该做的是把这条改成"剩下的还有哪些"。**
///
/// **B1266 found 8 existence-only criteria in `KafkaInterfaceTests.cs`; B1267 followed that shape and found two more
/// files: `Persistence/InterfaceVerificationTests.cs` (14) and `Persistence/RedisInterfaceTests.cs` (11) — 33 criteria
/// across three files, all asking only whether a member exists.**
///
/// **One occurrence is an accident; three is a habit.** **B1266 wrote Kafka up as "a problem in that place"; scanning once
/// turns it into something else — <b>a writing habit</b>: reaching for "does it have this method" before "does it do the right
/// thing".** **A habit is not fixed by patching one file, because the next file will be written the same way.** **So this batch
/// does not fix another file; it pins the habit itself: the criterion asserts all three are <b>still</b> shadow checks, and it
/// goes red as soon as the first real behavioural test appears.**
/// </remarks>
public class ExistenceOnlyTestHabitTests
{
    /// <summary>
    /// **三个文件仍然全靠成员存在性** / All three files still rely entirely on member existence.
    /// </summary>
    /// <remarks>
    /// **"影子检查"的判据是：文件里有反射（`typeof(`／`GetMethod(`），而<b>没有</b>任何一次对被测类型的构造或调用。**
    ///
    /// **这个定义刻意保守：** **它只认"完全没碰过"的文件。** **一个既做存在性检查又做真实调用的文件不会被它标出——
    /// 那种文件是好的，不该被点名。**
    ///
    /// **A shadow check means the file uses reflection and contains <b>no</b> construction or invocation of the type under test.**
    /// **The definition is deliberately conservative: it only flags files that never touch anything.** **A file doing both
    /// existence checks and real calls is not flagged — that file is fine and has no business being named.**
    /// </remarks>
    [Fact]
    public void ThreeFiles_ShouldStillBeShadowChecks() {
        string root = RepositoryRoot();

        foreach (string relative in new[] {
            "tests/Framework.Plugin.Tests/Message/KafkaInterfaceTests.cs",
            "tests/Framework.Plugin.Tests/Persistence/InterfaceVerificationTests.cs",
            "tests/Framework.Plugin.Tests/Persistence/RedisInterfaceTests.cs",
        }) {
            string source = File.ReadAllText(System.IO.Path.Combine(root, relative));

            source.Should().Contain("typeof(", $"{relative} verifies existence through reflection");
            source.Should().Contain("GetMethod(", $"{relative} looks up members by name");

            string[] invocationMarkers = [
                "new KafkaProducer(", "new KafkaConsumer(", "new RedisClient(",
                "new SqliteRepository(", "new MongoRepository(", ".Send(", "await ",
            ];
            bool touchesTheSubject = invocationMarkers.Any(marker =>
                source.Contains(marker, StringComparison.Ordinal));

            touchesTheSubject.Should().BeFalse(
                $"{relative} must never construct or call the type it verifies until it is rewritten into a behavioural "
                + "test — at which point this criterion must be updated to name what is left");
        }
    }

    /// <summary>
    /// **Redis 的代码在，而生产调用方不在** / Redis's code is present and its production callers are not.
    /// </summary>
    /// <remarks>
    /// **这与 B1260／B1261／B1265 是同一形状的第四次出现：** **代码写好了，接线没做，
    /// 而矩阵那一行没有说这件事。**
    ///
    /// **`RedisClient.cs` 203 行，只有三个测试文件提到它——而这三个文件都<b>不构造它</b>。**
    ///
    /// **所以"Redis 有客户端"这句话是对的，而"Redis 可用"这句话没有依据：
    /// 从来没有任何一段代码创建过它，也从来没有任何一次真实往返。**
    ///
    /// **This is the fourth appearance of the same shape as B1260/B1261/B1265: the code is written, the wiring is not, and the
    /// matrix row does not say so.** **`RedisClient.cs` is 203 lines, mentioned by three test files, none of which constructs
    /// it.** **So "Redis has a client" is true and "Redis works" has no basis: no code has ever created one and there has never
    /// been a real round trip.**
    /// </remarks>
    [Fact]
    public void Redis_ShouldBeImplementedButUnwired() {
        string root = RepositoryRoot();

        string client = System.IO.Path.Combine(
            root, "src/Framework.Plugin/Persistence/Redis/RedisClient.cs");
        File.Exists(client).Should().BeTrue("the Redis client is written");
        File.ReadAllText(client).Should().Contain(
            "class RedisClient",
            "and it is a real class, not a stub");

        string[] productionCallers = Directory
            .EnumerateFiles(System.IO.Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.EndsWith("RedisClient.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("RedisClient", StringComparison.Ordinal))
            .ToArray();

        productionCallers.Should().BeEmpty(
            "if a caller appears, the matrix row must change from 'implemented, unwired' to 'wired'");
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
