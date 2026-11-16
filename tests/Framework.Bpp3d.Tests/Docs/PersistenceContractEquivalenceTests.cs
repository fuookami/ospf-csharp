#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **五个仓储操作在本仓有一一对应物** / The code's five repository operations map one-to-one.
/// </summary>
/// <remarks>
/// **矩阵里那格写着"是否等价<B>尚未逐项核对**"。核了，核的是契约层而不是方言层：**
///
/// | 基线（`12fdb587`，三个表达式仓储） | 本仓 `ExpressionRepository&lt;E&gt;` |
/// | --- | --- |
/// | `find` | `FindAsync` |
/// | `count` | `CountAsync` |
/// | `exists` | `ExistsAsync` |
/// | `update` | `UpdateAsync` |
/// | `delete` | `DeleteAsync` |
///
/// **读法：** **、 三个文件的公开方法集合是
/// `{find, count, exists, update, delete}`（外加各自的方言私有成员），而这些仓库<b>没有 insert**——
/// 插入不走表达式仓储。**
///
/// **所以"等价"成立的部分是明确的：** **五个操作一一对应，一个不多一个不少。**
/// **而"不等价"的部分同样明确：** **<b>四个方言后端**（H2／JDBC client／Ktorm／MyBatis）做同一件事，
/// 本仓用 EF Core 一个栈承担——** **这是"换了一条实现路"而不是"少了一个操作"。**
///
/// ### 判据钉什么
///
/// **钉本仓那五个方法必须还在。** **少一个就是真的丢能力，而被删掉时没有任何别的东西会红。**
///
/// **The matrix says "whether they are equivalent <B>has not been checked item by item</b>". The contract layer was then checked: the
/// three baseline expression repositories expose `{find, count, exists, update, delete}` and no insert, and `ExpressionRepository`
/// exposes exactly those five.** **So what holds is precise: five operations map one-to-one, no more and no less; and what differs is
/// equally precise: the code uses four dialect backends for one job while this repository uses one stack (EF Core) — a different
/// road, not a missing operation.** **The criterion pins the five methods: removing one loses a capability and nothing else would
/// redden.**
/// </remarks>
public class PersistenceContractEquivalenceTests
{
    /// <summary>**五个操作必须都还在** / All five operations must still be there.</summary>
    [Fact]
    public void TheFiveOperations_ShouldStillExist() {
        string root = RepositoryRoot();
        string contract = File.ReadAllText(System.IO.Path.Combine(
            root, "src", "Framework", "Persistence", "Expression", "ExpressionRepository.cs"));

        (string Baseline, string Csharp)[] pairs = [
            ("find", "FindAsync"),
            ("count", "CountAsync"),
            ("exists", "ExistsAsync"),
            ("update", "UpdateAsync"),
            ("delete", "DeleteAsync"),
        ];

        foreach ((string baseline, string csharp) in pairs) {
            Regex.IsMatch(contract, @"\b" + csharp + @"\s*\(").Should().BeTrue(
                $"the baseline's `{baseline}` must keep its counterpart `{csharp}`; losing one is losing a capability the baseline has");
        }

        // **不多也不少：契约里恰好这五个 Task<Result<...>> 方法。**
        // **No more, no less: exactly these five Task<Result<...>> methods.**
        // **不能用 `Task<Result<[^>]*>>` 去匹配：** **返回类型里自己嵌着 `>`（`Error<ErrorCode>>>`），所以那个模式匹配不到任何一行。** **数声明开头就够了。**
        // **`Task<Result<[^>]*>>` cannot match: the return type nests `>` inside itself (`Error<ErrorCode>>>`), so the pattern
        // matched nothing. Counting the declaration prefix is enough.**
        int declared = Regex.Matches(contract, @"Task<Result<").Count;
        declared.Should().Be(
            pairs.Length,
            "the equivalence table names five operations; a sixth would mean the table is out of date, and a fifth would mean a "
            + "capability vanished");
    }

    /// <summary>
    /// **`Repository&lt;T&gt;` 是另一层，不参与这次等价判断** / `Repository<T>` is another layer, not part of this comparison.
    /// </summary>
    /// <remarks>
    /// **本仓有两个仓储抽象：** **`Framework.Plugin.Persistence.Repository&lt;T&gt;`（六个 CRUD 操作，EF Core／Mongo／SQLite
    /// 各自实现）与 `Framework.Persistence.Expression.ExpressionRepository&lt;E&gt;`（五个表达式操作）。**
    ///
    /// **把两层的操作混在一起数会得出"多了 insert、少了 count"这种假结论**——
    /// **而它们服务的是不同的调用方：前者是对象存取的 CRUD，后者是"按谓词查／改／删"的表达式接口。**
    ///
    /// **This repository has two repository abstractions: `Repository<T>` (six CRUD operations, one implementation per backend) and
    /// `ExpressionRepository<E>` (its five expression operations).** **Counting them together yields the false conclusion
    /// "extra insert, missing count" — they serve different callers: object CRUD versus query-by-predicate.**
    /// </remarks>
    [Fact]
    public void TheTwoLayers_ShouldStaySeparate() {
        string root = RepositoryRoot();
        string plugin = File.ReadAllText(System.IO.Path.Combine(
            root, "src", "Framework.Plugin", "Persistence", "Repository.cs"));
        string expression = File.ReadAllText(System.IO.Path.Combine(
            root, "src", "Framework", "Persistence", "Expression", "ExpressionRepository.cs"));

        Regex.IsMatch(plugin, @"\bInsertAsync\s*\(").Should().BeTrue(
            "object CRUD keeps insert; the baseline's expression repositories have no insert because insert does not go through them");
        plugin.Should().NotContain(
            "CountAsync(",
            "count belongs to the expression layer; adding it here would blur the two layers this criterion keeps apart");
        expression.Should().NotContain(
            "InsertAsync(",
            "the baseline's expression repositories do not insert, and neither does this layer");
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