#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **四个 Mongo 翻译器里，三个接了、一个没接** /
/// Three of the four Mongo translators are wired; one is not.
/// </summary>
/// <remarks>
/// **B1288 核矩阵里那句"B1122 把它们接进真实查询路径……在此之前四个翻译器没有被任何生产路径调用过"。**
///
/// **我先把它读成<b>当前状态的断言**，于是认为它与 `MongoRepository.cs` 里那次调用矛盾，准备去改文档。
/// 再读一遍才发现它写的是"在此之前"——<b>那是一句历史叙述，而且是对的**。
///
/// **同一件事，我在这个会话里已经第三次读错：** **B1285（把用例数读成方法数）、B1286（把 `24 条` 的所在行读错）、
/// B1288（把历史读成现在）。** **三次都不是文档错，而是<b>我读得太快**。
///
/// ### 而 B1293 发现：我给这条判据编了一个不存在的理由
///
/// **B1288 写的是"**同一段还写着‘明确的局限’：Mongo 这条路没有完全接上**"——**而矩阵里那句“明确的局限”说的是判据没连真实 Mongo 实例，以及 H2／Ktorm／MyBatis／JDBC 四个后端未移植，与 update 无关。**
///
/// **而 `MongoRepository.UpdateAsync` 本来就存在，它做的是整文档 `ReplaceOneAsync`——所以字段级的 `MongoUpdateTranslator` 不被调用是设计差异，不是接口未接。**
///
/// **所以这条判据的真正价值不是"钉住一个缺口"，而是钉住三个译文器确实被生产路径用到——以及第四个确实没被用到，而后者是因为仓储用整文档替换。**
/// **如果有人把它接上去"补缺口"，那他就是按一句不存在的缺口去做工作——而这正是本仓库记过的"过期的 cannot 比过期的 done 更贵"。**
///
/// **B1293 found that this criterion was written around a reason I invented.** **B1288 said "the same paragraph also states the
/// documented limitation that the Mongo path is not fully wired" — but the limitation in the matrix is about the criterion not
/// connecting to a real Mongo instance plus four unported backends (H2/Ktorm/MyBatis/JDBC), and has nothing to do with updates.**
/// **`MongoRepository.UpdateAsync` has always existed and performs a whole-document `ReplaceOneAsync`, so the field-level
/// `MongoUpdateTranslator` going uncalled is a design difference, not an unwired interface.** **The criterion is still worth keeping —
/// it pins that three translators really are reached from production and the fourth really is not — but the reason is now the real
/// one.** **Wiring it "to close the gap" would be work done against a gap that does not exist, which is the cost this repository
/// already recorded: a stale cannot is more expensive than a stale done.**
///
/// **这是本会话第五次读错（B1285、B1286、B1288、B1290）——而这一次比前四次更重：** **前四次是读错文字，这一次是把文字读成我预期的样子。**
/// </remarks>
public class MongoTranslatorWiringTests
{
    /// <summary>**接线状态：三个已接、一个未接** / Wiring state: three wired, one not.</summary>
    [Fact]
    public void TheWiringState_ShouldMatchTheDocumentedLimitation() {
        string root = RepositoryRoot();
        string mongo = System.IO.Path.Combine(root, "src", "Framework.Plugin", "Persistence", "MongoDB");
        Directory.Exists(mongo).Should().BeTrue("the Mongo persistence layer exists");

        string[] sources = Directory
            .EnumerateFiles(mongo, "*.cs", SearchOption.AllDirectories)
            .ToArray();

        // **调用方必须在<b>别的**文件里：** **自己文件里那次调用不算"接进生产路径"。**
        // **The caller must be in <b>another</b> file: a call inside the translator's own file is not production wiring.**
        bool WiredByOtherFile(string type) => sources.Any(path =>
            System.IO.Path.GetFileNameWithoutExtension(path) != type
            && File.ReadAllText(path).Contains(type + ".", StringComparison.Ordinal));

        WiredByOtherFile("MongoBooleanTranslator").Should().BeTrue(
            "MongoRepository translates predicates through it");
        WiredByOtherFile("MongoOrderByTranslator").Should().BeTrue(
            "MongoRepository translates sorts through it");
        WiredByOtherFile("MongoScalarTranslator").Should().BeTrue(
            "the boolean translator delegates comparisons to it, so it is reached from the repository as well");

        WiredByOtherFile("MongoUpdateTranslator").Should().BeFalse(
            "UpdateAsync replaces whole documents by design, so the field-level translator is not needed; "
            + "the criterion records that as a difference, not as an unwired gap");
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