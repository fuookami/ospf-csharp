#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Function;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence;

/// <summary>
/// **一份被检测、被记录、却从未被读取的声明** /
/// A declaration that is detected, recorded, and never read.
/// </summary>
/// <remarks>
/// **发现 CPLEX／COPT 的 `PiecewiseLinearConstraint` 申报超过实际。顺着这个形状查了<b>全部</b>申报，
/// 结果比那一处严重得多：**
///
/// 1. **三个插件合计申报 <b>13 项</b>原生能力**（Gurobi 8、CPLEX 2、COPT 3）
/// 2. **`src/` 里<b>没有任何一处</b>调用过原生构造方法**（`AddGenConstrAbs`／`AddGenConstrPWL`／
///    `AddSOS`／`AddGenConstrIndicator`／`AddGenConstrMax`／`AddGenConstrAnd` —— 全部零命中）
/// 3. **`FunctionSolverCapabilities.Supports(...)` 的生产调用方<b>为零</b>，只有测试在调它**
///
/// ### 于是那份声明问的不是同一个问题
///
/// **它问的是"这个 SDK 有没有这个方法"，而读它的人会理解成"这个后端会不会用原生形式"。**
///
/// **这两句话差别很大：** **前者对每个装了新版 Gurobi 的环境都为真，
/// 而后者在本仓<b>对所有环境都为假</b>。**
///
/// ### 三段各自为真，合起来是假的
///
/// **"探测对了"为真、"申报写全了"为真、"没人用它"也为真——**
/// **而这三段合起来的意思是：那块能力只存在于声明里。**
///
/// **它不是 bug：没有任何一个解因此算错。它是<b>一块镜子</b>——
/// 照着 SDK 的样子，报告一份从不被使用、也从不被验证的能力清单。**
///
/// **Found CPLEX/COPT's `PiecewiseLinearConstraint` declaring beyond reality; a follow-up checked <b>every</b> declaration
/// and found something worse: 1) three plugins declare <b>13</b> native capabilities; 2) <b>not one</b> native constructor
/// call exists anywhere in `src/`; 3) `Supports(...)` has <b>zero</b> production callers — only tests call it.**
///
/// **So the declaration asks a different question than the reader answers: it asks "does this SDK have the method" while a
/// reader understands "does this backend use the native form".** **The first is true for every environment with a recent
/// Gurobi; the second is false here for <b>all</b> of them.**
///
/// **Three true parts summing to a false whole: the detection is right, the declaration is complete, and nobody reads it — and
/// together that means the capability exists only in the declaration.** **It is not a bug: no solution is wrong because of it.
/// It is <b>a mirror</b>, reflecting the SDK and reporting a list of capabilities that is never used and never verified.**
/// </remarks>
public class FunctionCapabilityDeclarationTests
{
    /// <summary>
    /// **三段同时成立：申报在、写入不在、读取不在** /
    /// All three hold at once: declared, not written, not read.
    /// </summary>
    /// <remarks>
    /// **这一条必须三段一起断言，因为任何一段单独看都是一个正常状态：**
    /// **只看到"申报在"，会以为能力可用；只看到"写入不在"，会以为只是没接上；
    /// 只看到"读取不在"，会以为那份表只是文档。**
    ///
    /// **三段一起才是那个结论：这份声明没有任何一端连着实际。**
    ///
    /// **All three must be asserted together, because each alone is a normal state: seeing only "declared" suggests the
    /// capability works; only "not written" suggests mere missing wiring; only "not read" suggests the table is documentation.
    /// Together they are the conclusion: this declaration touches reality at neither end.**
    /// </remarks>
    [Fact]
    public void TheNativeCapabilityTable_ShouldBeDetectedUnwrittenAndUnread() {
        string root = RepositoryRoot();
        string source = ReadAllSources(System.IO.Path.Combine(root, "src"));

        // ① **申报在**：三个插件都在。
        // Declared: all three plugins are.
        source.Should().Contain("FunctionNativeCapability.BinaryLogic");
        source.Should().Contain("FunctionNativeCapability.PiecewiseLinearConstraint");
        source.Should().Contain("FunctionNativeCapability.Sos1");

        // ② **原生写入不在**：一个原生构造方法都没被调用过。
        // Not written: not one native constructor is ever called.
        string[] nativeConstructors = [
            "AddGenConstrAbs(", "AddGenConstrPWL(", "AddGenConstrIndicator(",
            "AddGenConstrMax(", "AddGenConstrAnd(", "AddGenConstrOr(",
        ];
        foreach (string constructor in nativeConstructors) {
            int callSites = Directory
                .EnumerateFiles(System.IO.Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith("FunctionCapabilities.cs", StringComparison.Ordinal))
                .Count(path => File.ReadAllText(path).Contains(constructor, StringComparison.Ordinal));
            callSites.Should().Be(
                0,
                $"{constructor} has no call site, so the declaration has nothing behind it — "
                + "the first real writer must make this criterion red and force the matrix to say 'wired'");
        }

        // ③ **读取不在**：`Supports(...)` 只在测试里被调。
        // Not read: `Supports(...)` is only called from tests.
        string[] productionReaders = Directory
            .EnumerateFiles(System.IO.Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("FunctionSolverCapabilities.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(".Supports(FunctionNativeCapability", StringComparison.Ordinal))
            .ToArray();
        productionReaders.Should().BeEmpty(
            "nothing decides anything from this table; if a decision starts consulting it, the matrix must say so");
    }

    /// <summary>
    /// **枚举与基线的对应关系必须说准确** / The enum's relation to the baseline must be stated exactly.
    /// </summary>
    /// <remarks>
    /// **这一条防的是"顺手把它删掉"这种修法。** **那份表之所以值得留，是因为它记录了
    /// "基线认为这些能力可以被原生表达"，这个事实本身有用。** **要修的是<b>它的意思被读错</b>，不是把表删了。**
    ///
    /// ### 而"逐一对应"这句话本身也不准确
    ///
    /// **基线那个枚举有 <b>15</b> 个成员：9 个是能力，6 个是<b>位置修饰</b>**
    /// （`ObjectiveOnly`／`ConstraintOnly`／`ObjectiveAndConstraint`／`Nested`／`External`／`Unused`）。
    ///
    /// **本仓的 `FunctionNativeCapability` 只有那 9 个能力——而它的文档注释写着"逐一对应，成员名与顺序一致"。**
    ///
    /// **那 6 个修饰并没有丢，它们在 `FunctionUsageSummary` 里，这是合理的拆分。**
    ///
    /// **但"逐一对应"这句话会让下一个读者以为两边都是 15 个——
    /// 而他会用这句话去核一个<b>永远不会相等的集合</b>。**
    ///
    /// **所以这条判据现在钉的是<b>准确的对应关系</b>：9 个能力在这里、6 个修饰在那里。**
    /// **一句需要打折的"一致"，比一句明确的分工更糟。**
    ///
    /// **This guards against the fix being "just delete it".** **The table is worth keeping because it records that the
    /// baseline considers these capabilities natively expressible.**
    ///
    /// **But "one member at a time" is itself inexact: the baseline enum has <b>15</b> members — 9 capabilities and 6
    /// <b>positional qualifiers</b> — while this repo's `FunctionNativeCapability` has only the 9, with a doc comment claiming
    /// names and order match one-to-one.** **The 6 qualifiers are not lost, they live in `FunctionUsageSummary`, which is a
    /// reasonable split.** **But "one member at a time" invites the next reader to verify a set that can <b>never</b> be equal.**
    /// **So the criterion now pins the <b>accurate</b> relation: 9 capabilities here, 6 qualifiers there.** **A claim that needs
    /// discounting is worse than a statement of the split.**
    /// </remarks>
    [Fact]
    public void TheEnum_ShouldKeepMirroringTheBaseline() {
        FunctionNativeCapability[] members = Enum.GetValues<FunctionNativeCapability>();

        members.Should().Contain(FunctionNativeCapability.Sos1);
        members.Should().Contain(FunctionNativeCapability.Sos2);
        members.Should().Contain(FunctionNativeCapability.Indicator);
        members.Should().Contain(FunctionNativeCapability.GeneralAbs);
        members.Should().Contain(FunctionNativeCapability.GeneralMinMax);
        members.Should().Contain(FunctionNativeCapability.BinaryLogic);
        members.Should().Contain(FunctionNativeCapability.PiecewiseLinearConstraint);
        members.Should().Contain(FunctionNativeCapability.PiecewiseLinearObjective);
        members.Should().Contain(FunctionNativeCapability.SemiContinuous);

        string[] qualifiers = Enum.GetNames<Fuookami.Ospf.Core.Model.Intermediate.FunctionUsageLocation>();
        qualifiers.Should().BeEquivalentTo(
            ["ObjectiveOnly", "ConstraintOnly", "ObjectiveAndConstraint", "Nested", "External", "Unused"],
            "the six positional qualifiers from the baseline enum live here, not in the capability table");

        members.Should().HaveCount(
            9,
            "the nine <b>capability</b> members mirror the baseline; the six remaining baseline members are positional "
            + "qualifiers that live in FunctionUsageSummary, and the doc comment must say so rather than claim a "
            + "one-to-one enum mirror");
    }

    private static string ReadAllSources(string root) {
        var text = new System.Text.StringBuilder();
        foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)) {
            text.Append(File.ReadAllText(path)).Append('\n');
        }
        return text.ToString();
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
