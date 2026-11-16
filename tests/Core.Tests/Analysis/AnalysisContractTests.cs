#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 跨语言语义契约 / The cross-language semantic contract.
/// </summary>
/// <remarks>
/// `Fixtures/analysis-contract.tsv` 是从固定基线 `12fdb587` **逐字复制**过来的，
/// **Kotlin 侧由 `AnalysisFixtureContractTest` 断言同一份文件。**
///
/// **"跨语言互通只做了一部分"这句话，在此之前没有任何可执行的东西支撑它**——
/// **现在每一节各有一条判据，而没断言到的节会被点名。**
///
/// `Fixtures/analysis-contract.tsv` is **copied verbatim** from the pinned baseline, **and the Kotlin side asserts the same file.**
/// **"Cross-language interop is only partly measured" had nothing executable behind it** — **now each section has a criterion, and any unasserted section is named.**
/// </remarks>
public class AnalysisContractTests
{
    private static string[] ContractLines()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln")))
        {
            directory = directory.Parent;
        }

        string path = Path.Combine(
            directory!.FullName, "tests", "Core.Tests", "Analysis", "Fixtures", "analysis-contract.tsv");

        return File.ReadAllLines(path)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !line.TrimStart().StartsWith("#", StringComparison.Ordinal) && line.Length > 0)
            .ToArray();
    }

    /// <summary>取一节的非注释数据行 / The data rows of one section.</summary>
    private static string[] Section(string name)
    {
        string[] lines = ContractLines();
        int start = Array.FindIndex(lines, line => line == "[" + name + "]");
        start.Should().BeGreaterThanOrEqualTo(0, "the contract has to declare [" + name + "]");

        int end = start + 1;
        while (end < lines.Length && !lines[end].StartsWith("[", StringComparison.Ordinal))
        {
            end++;
        }

        return lines[(start + 1)..end];
    }

    /// <summary>枚举成员的声明顺序 / The declared member names of an enum, in order.</summary>
    private static string[] EnumNames(Type type) => Enum.GetNames(type);

    /// <summary>构造每种具体证据所需的占位参数 / Placeholder arguments for each concrete source.</summary>
    private static object[] ProbeArguments(Type type)
    {
        ParameterInfo[] parameters = type.GetConstructors()
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .First()
            .GetParameters();

        return parameters.Select(parameter =>
        {
            Type kind = parameter.ParameterType;
            if (kind == typeof(ConstraintId))
            {
                return (object)new ConstraintId("constraint:probe");
            }
            if (kind == typeof(VariableId))
            {
                return new VariableId("source:probe");
            }
            if (kind == typeof(VariableBoundRef))
            {
                return new VariableBoundRef(new VariableId("source:probe"), BoundSide.Lower);
            }
            if (kind == typeof(ObjectiveTarget))
            {
                // **目标类来源要一个真目标，不是占位字符串。**
                // **The target source needs a real target, not a placeholder string.**
                return ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:probe"), new Fuookami.Ospf.Math.Algebra.Number.Flt64(10.0));
            }

            throw new InvalidOperationException(type.Name + " has an unmapped constructor parameter");
        }).ToArray();
    }

    /// <summary>每种具体证据实例给出的种类名 / The kind every concrete evidence instance reports.</summary>
    private static string[] ActualEvidenceKinds() =>
        typeof(DiagnosticSource)
            .Assembly
            .GetTypes()
            .Where(type => typeof(DiagnosticSource).IsAssignableFrom(type) && !type.IsAbstract)
            .Select(type => (DiagnosticSource)Activator.CreateInstance(type, ProbeArguments(type))!)
            .Select(source => source.Kind)
            .ToArray();

    /// <summary>**schema 版本必须与契约一致** / Schema versions match the contract.</summary>
    [Fact]
    public void TheSchemaVersions_ShouldMatchTheContract()
    {
        string[] rows = Section("schema");
        rows.Should().NotBeEmpty("the contract lists its report schemas");

        foreach (string row in rows)
        {
            string[] parts = row.Split('=');
            parts.Should().HaveCount(2, "'" + row + "' has to be key=value");
            parts[1].Trim().Should().Be(CriticalAnalysisSchema.Version,
                parts[0] + " is pinned to " + CriticalAnalysisSchema.Version + " on both sides");
        }
    }

    /// <summary>**状态词表与顺序必须与契约一致** / Status vocabulary and order match.</summary>
    /// <remarks>
    /// **顺序也要对**：契约注释写明"顺序必须与两端枚举声明顺序一致"。
    /// **顺序错了不会让任何比较失败，只会让按序号落盘的记录错位。**
    ///
    /// **The order matters too**: the contract says the order has to match the enum declaration on both sides.
    /// **A wrong order fails no comparison; it only shifts records that are written by index.**
    /// </remarks>
    [Fact]
    public void TheStatusVocabulary_ShouldMatchInOrder()
    {
        EnumNames(typeof(AnalysisStatus)).Should().Equal(Section("status"),
            "the status vocabulary and its order are part of the contract");
    }

    /// <summary>**"已证明"只对两个状态为真** / "Proven" holds for exactly two statuses.</summary>
    [Fact]
    public void TheProvenTable_ShouldMatchTheContract()
    {
        string[] rows = Section("proven");
        rows.Should().HaveCount(EnumNames(typeof(AnalysisStatus)).Length,
            "every status has a proven flag");

        foreach (string row in rows)
        {
            string[] parts = row.Split('=');
            bool proven = bool.Parse(parts[1].Trim());

            // **只有 Unreachable 与 Reachable 是结论；Unknown 是"没结果"，Unsupported 是"做不到"。**
            // **Only Unreachable and Reachable are conclusions; Unknown is "no result" and Unsupported is "cannot".**
            bool expected = parts[0] is "Reachable" or "Unreachable";
            proven.Should().Be(expected,
                parts[0] + " proven-ness is a semantic claim, not a formatting choice");
        }
    }

    /// <summary>**活动状态词表与顺序必须一致** / Activity-status vocabulary and order match.</summary>
    [Fact]
    public void TheActivityStatusVocabulary_ShouldMatchInOrder() =>
        EnumNames(typeof(ActivityStatus)).Should().Equal(Section("activity-status"));

    /// <summary>**证据种类必须正好是契约那五个** / Evidence kinds are exactly the contract's five.</summary>
    /// <remarks>
    /// **此前这里有第六个 `variable-bound`，契约里没有。**
    /// 它的 `StableId` 与两个具体边界**完全相同**，**于是同一份证据经中转一次 `Kind` 就变了**——
    /// **按 (stable_id, kind) 做的跨语言比对，结果取决于代码走了哪一支。**
    /// 现在 `Kind` 由边界方向派生，六个变体落到五个词上。
    ///
    /// **There used to be a sixth kind, `variable-bound`, which the contract does not list.**
    /// Its `StableId` was **identical** to the two specific bounds, **so one round trip changed the `Kind` and kept the id** —
    /// **and a comparison keyed on (stable_id, kind) depended on which branch ran.**
    /// Now `Kind` derives from the bound side, and six variants land on five words.
    /// </remarks>
    [Fact]
    public void TheEvidenceKinds_ShouldBeExactlyTheContractsFive()
    {
        ActualEvidenceKinds()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(kind => kind, StringComparer.Ordinal)
            .Should().Equal(
                Section("evidence-kind").OrderBy(kind => kind, StringComparer.Ordinal),
                "every concrete DiagnosticSource has to answer with a word the contract knows");
    }

    /// <summary>**同一份证据的 `Kind` 不因中转而改变** / A round trip may not change the kind.</summary>
    /// <remarks>
    /// **这是那条缺陷的直接判据。** `AsInfeasibilityMember` 与
    /// `FromInfeasibilityMember` 互为逆操作，**而"互逆"必须包括 `StableId` 与 `Kind` 两样。**
    ///
    /// 修复前：`VariableLowerBound` → `InfeasibilityMember` → `VariableBound`，
    /// **`StableId` 不变而 `Kind` 从 `variable-lower-bound` 变成 `variable-bound`。**
    ///
    /// **This is the direct criterion for that defect.** `AsInfeasibilityMember` and `FromInfeasibilityMember` are inverses, **and "inverse" has to cover both the `StableId` and the `Kind`.**
    /// Before the fix: `VariableLowerBound` → `InfeasibilityMember` → `VariableBound`, **same id, different kind.**
    /// </remarks>
    [Theory]
    [InlineData("constraint:probe")]
    [InlineData("source:probe")]
    public void TheRoundTrip_ShouldPreserveKind(string identifier)
    {
        DiagnosticSource[] sources =
        [
            new DiagnosticSource.Constraint(new ConstraintId(identifier)),
            new DiagnosticSource.VariableLowerBound(new VariableId(identifier)),
            new DiagnosticSource.VariableUpperBound(new VariableId(identifier)),
            new DiagnosticSource.SparseDomain(new VariableId(identifier)),
        ];

        foreach (DiagnosticSource source in sources)
        {
            var roundTripped = DiagnosticSource.FromInfeasibilityMember(
                source.AsInfeasibilityMember()!);

            roundTripped.Should().NotBeNull();
            roundTripped!.StableId.Should().Be(source.StableId, "identity has to survive the trip");
            roundTripped.Kind.Should().Be(source.Kind,
                "the kind is part of the identity, and a lower bound must not become an anonymous bound");
        }
    }

    /// <summary>**禁止出现的证据名必须真的不是证据种类** / The forbidden labels are not kinds.</summary>
    /// <remarks>
    /// 契约禁止公开证据边界出现求解器内部痕迹。**这条只守词表这一半。**
    ///
    /// The contract forbids solver internals on the public evidence boundary. **This guards the vocabulary half only.**
    /// </remarks>
    [Fact]
    public void TheForbiddenEvidence_ShouldNotBeAnEvidenceKind()
    {
        string[] kinds = ActualEvidenceKinds();

        foreach (string forbidden in Section("forbidden-evidence"))
        {
            kinds.Should().NotContain(forbidden,
                forbidden + " is a solver internal and may not be reportable evidence");
        }
    }

    /// <summary>**最小性词表与"已证明"的映射必须一致** / Minimality vocabulary and its proven map match.</summary>
    /// <remarks>
    /// **只有 Irreducible 才算已证明最小。** 超时与预算耗尽必须是 Partial——
    /// **把它们算成"已证明"会让一个没做完的删除复验看起来像结论。**
    ///
    /// **Only Irreducible counts as proven minimal.** A timeout or exhausted budget has to be Partial —
    /// **counting those as proven makes an unfinished deletion check read as a conclusion.**
    /// </remarks>
    [Fact]
    public void TheMinimalityTable_ShouldMatchTheContract()
    {
        string[] rows = Section("minimality");
        EnumNames(typeof(ConflictMinimality)).Should().Equal(rows.Select(row => row.Split('\t')[0]),
            "the minimality vocabulary and order are part of the contract");

        foreach (string row in rows)
        {
            string[] parts = row.Split('\t');
            bool.Parse(parts[1]).Should().Be(parts[0] == "Irreducible",
                "partial evidence is not a proof");
        }
    }

    /// <summary>**提取层级顺序必须与契约一致** / The extraction tiers match in order.</summary>
    /// <remarks>
    /// **层级顺序是一个关于代价的断言**：原生 core 最便宜，重复求解回退最贵。
    /// **顺序错了不会让任何一次求解失败，只会让"先试哪个"变成随机。**
    ///
    /// **The tier order is a claim about cost**: a native core is cheapest, repeated solving is dearest.
    /// **A wrong order fails no solve; it only makes "which to try first" arbitrary.**
    /// </remarks>
    [Fact]
    public void TheExtractionTiers_ShouldMatchInOrder()
    {
        string[] contracted = Section("extraction-tier")
            .Select(row => row.Split('\t')[1])
            .ToArray();
        string[] declared = EnumNames(typeof(ConflictExtractionTier));

        // **契约只钉真正的层级，不钉哨兵。** `Unavailable` 是"三级都不行"的出口，
        // **它没有"先试哪个"的含义，所以它不属于层级顺序。**
        // **The contract pins the tiers, not the sentinel.** `Unavailable` is the "none of the three worked" exit,
        // **it carries no "which to try first" meaning, so it is not part of the tier order.**
        declared.Take(contracted.Length).Should().Equal(contracted,
            "the extraction order is a claim about cost, so the contract fixes it");
        declared.Skip(contracted.Length).Should().Equal(new[] { "Unavailable" },
            "the only member after the contracted tiers is the unavailable sentinel");
    }

    /// <summary>**三层的重要性必须可区分且顺序一致** / The three criticality kinds match in order.</summary>
    [Fact]
    public void TheCriticalityKinds_ShouldMatchInOrder()
    {
        EnumNames(typeof(CriticalityKind)).Should().Equal(Section("criticality-kind"),
            "the kinds encode how much of the profile they cover, so their order is contractual");
    }

    /// <summary>**松弛策略的默认值必须与契约一致** / Relaxability defaults match the contract.</summary>
    [Fact]
    public void TheRelaxabilityDefaults_ShouldMatchTheContract()
    {
        var policy = new RelaxabilityPolicy();
        var rows = Section("relaxability-policy")
            .Select(row => row.Split('\t'))
            .ToDictionary(parts => parts[0], parts => parts[1]);

        rows["max_candidates"].Should().Be(policy.MaxCandidates.ToString(),
            "a different default silently changes how many repairs a caller is offered");
        rows["max_plans"].Should().Be(policy.MaxPlans.ToString(),
            "a different default silently changes how many plans survive");
    }
}
