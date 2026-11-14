// 网络路径 → 业务任务束的桥接测试 / Tests for the network-path to business-bunch bridge.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttNetworkScheduling.Domain.Network;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Path;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttNetworkScheduling.Tests.Domain.Network;

/// <summary>
/// 网络路径 → 业务任务束的桥接 / The network-path to business-bunch bridge.
/// </summary>
/// <remarks>
/// 这段代码的**真正内容是完成状态的翻译**，不是形式上的适配器。测试也围绕这一点：
/// "搜索跑完了"不等于"证明了没有更好的列"，"没证明"也不等于"证明没有"。
///
/// The **real content of this code is the completion translation**, not the adapter formality. These tests centre on that:
/// "the search finished" is not "no better column was proved", and "not proved" is not "proved none".
/// </remarks>
public sealed class NetworkBunchGenerationTests {
    private static ColumnGenerationScope Scope() =>
        new(request: "r1", group: "g1", branch: "b1");

    /// <summary>a → b → c / a → b → c.</summary>
    private static NetworkGraph<TestNode, TestArc> LineGraph() =>
        NetworkGraph<TestNode, TestArc>.Invoke(
            new[] { "a", "b", "c" }.Select(id => new TestNode(id)).ToList(),
            new[] { ("e1", "a", "b"), ("e2", "b", "c") }
                .Select(arc => new TestArc(arc.Item1, arc.Item2, arc.Item3)).ToList(),
            new NetworkGraphAccess<TestNode, TestArc>(
                NodeId: node => new NetworkNodeId(node.Id),
                ArcId: arc => new NetworkArcId(arc.Id),
                From: arc => new NetworkNodeId(arc.From),
                To: arc => new NetworkNodeId(arc.To))).Value;

    /// <summary>宽图，用于预算耗尽 / A wide graph used to exhaust the budget.</summary>
    private static NetworkGraph<TestNode, TestArc> WideGraph() =>
        NetworkGraph<TestNode, TestArc>.Invoke(
            new[] { "a", "b", "c", "y", "z" }.Select(id => new TestNode(id)).ToList(),
            new[] { ("e1", "a", "b"), ("e2", "a", "c"), ("e3", "b", "y"), ("e4", "c", "y"), ("e5", "y", "z") }
                .Select(arc => new TestArc(arc.Item1, arc.Item2, arc.Item3)).ToList(),
            new NetworkGraphAccess<TestNode, TestArc>(
                NodeId: node => new NetworkNodeId(node.Id),
                ArcId: arc => new NetworkArcId(arc.Id),
                From: arc => new NetworkNodeId(arc.From),
                To: arc => new NetworkNodeId(arc.To))).Value;

    private static PathProblem<TestNode, TestArc, State> WideProblem() =>
        new(
            WideGraph(),
            new NetworkNodeId("a"),
            new State(0),
            (label, arc) => Results.Ok<State?>(new State(label.State.Cost + 1)),
            (node, state) => node.Value == "z");

    private static PathProblem<TestNode, TestArc, State> LineProblem() =>
        new(
            LineGraph(),
            new NetworkNodeId("a"),
            new State(0),
            (label, arc) => Results.Ok<State?>(new State(label.State.Cost + 1)),
            (node, state) => node.Value == "c");

    private static Result<ColumnGenerationBatch<Bunch>, ErrorCode, Error<ErrorCode>> Run(
        Func<PathLabel<State, TestArc>, PathSearchResult<State, TestArc>, Result<Bunch?, ErrorCode, Error<ErrorCode>>>? extract = null,
        Func<PathSearchResult<State, TestArc>, bool>? evidence = null,
        PathSearchBudget? budget = null,
        Action<PathSearchResult<State, TestArc>>? onCompleted = null) =>
        new NetworkBunchGenerationContext().Generate<TestNode, TestArc, State, Bunch>(
            LineProblem(),
            budget ?? new PathSearchBudget(),
            Scope(),
            noImprovementEvidence: evidence,
            onSearchCompleted: onCompleted,
            extract: extract ?? ((path, result) => Results.Ok<Bunch?>(new Bunch(path.Node.Value))));

    /// <summary>**穷尽且无拒绝时映射为 Exhausted** / Exhausted with no rejection maps to Exhausted.</summary>
    [Fact]
    public void Generate_ShouldTranslateExhaustion() {
        ColumnGenerationBatch<Bunch> batch = Run().Value;

        batch.Completion.Should().Be(ColumnGenerationCompletion.Exhausted);
        batch.Columns.Should().ContainSingle().Which.Id.Should().Be("c");
    }

    /// <summary>**没有独立证据时 `NoImprovingColumns` 是 null，不是 false** / Without independent evidence it is null, not false.</summary>
    /// <remarks>
    /// "没证明"与"证明没有"是两件事。若实现把 `null` 写成 `false`，上游会把一次**可能还有更好列**的搜索
    /// 当成"确定没有"，这正是最危险的那种错误。
    ///
    /// "Not proved" and "proved none" differ. If the implementation wrote `false` where it means `null`, the caller would read a search that **might
    /// still have better columns** as "definitely none" — the most dangerous kind of mistake.
    /// </remarks>
    [Fact]
    public void Generate_ShouldLeaveTheProofNullWithoutEvidence() =>
        Run().Value.NoImprovingColumns.Should().BeNull();

    /// <summary>**有独立证据且穷尽时才给出证明** / The proof appears only with evidence and exhaustion.</summary>
    [Fact]
    public void Generate_ShouldClaimTheProofWithEvidence() =>
        Run(evidence: _ => true).Value.NoImprovingColumns.Should().BeTrue();

    /// <summary>**证据为假时不给证明** / A false verdict yields no proof.</summary>
    [Fact]
    public void Generate_ShouldNotClaimTheProofWhenEvidenceIsFalse() =>
        Run(evidence: _ => false).Value.NoImprovingColumns.Should().BeNull();

    /// <summary>**预算耗尽时即使证据为真也不给证明** / With a budget limit even true evidence yields no proof.</summary>
    /// <remarks>
    /// 只测"穷尽 + 证据 → true"会放过一个**不看完成状态**的实现。这里让证据恒为真、但搜索停在预算上。
    ///
    /// Asserting only "exhausted + evidence → true" would let an implementation that **ignores the completion status** pass. Here the evidence is
    /// always true while the search stops at a budget limit.
    /// </remarks>
    [Fact]
    public void Generate_ShouldNotClaimTheProofWhenTheSearchIsNotExhausted() {
        ColumnGenerationBatch<Bunch> batch =
            new NetworkBunchGenerationContext().Generate<TestNode, TestArc, State, Bunch>(
                WideProblem(),
                new PathSearchBudget(MaximumLabels: 2),
                Scope(),
                noImprovementEvidence: _ => true,
                extract: (path, result) => Results.Ok<Bunch?>(new Bunch(path.Node.Value))).Value;

        batch.Completion.Should().NotBe(ColumnGenerationCompletion.Exhausted);
        batch.NoImprovingColumns.Should().BeNull();
    }

    /// <summary>**一条路径被拒绝就降级为 Heuristic** / One rejected path degrades the whole batch to Heuristic.</summary>
    /// <remarks>
    /// 抽取器返回 `null` 表示这条路径**不可用**，而不是"没有路径"。若实现忽略它，一批"大部分可用"的结果会被当成穷尽搜索。
    ///
    /// An extractor returning `null` means the path was **unusable**, not that there were no paths. An implementation that ignores it would treat a
    /// mostly-usable batch as an exhaustive search.
    /// </remarks>
    [Fact]
    public void Generate_ShouldDegradeWhenAPathIsRejected() {
        // 抽取器对**每一条**路径都返回 null：路径都被拒绝，批次里一条列也没有。
        // The extractor returns null for **every** path: all are rejected and the batch carries no column.
        ColumnGenerationBatch<Bunch> batch = Run(
            extract: (path, result) => Results.Ok<Bunch?>(null)).Value;

        batch.Completion.Should().Be(ColumnGenerationCompletion.Heuristic);
        batch.Columns.Should().BeEmpty();
    }

    /// <summary>**抽取器失败直接向上返回** / An extractor failure propagates.</summary>
    [Fact]
    public void Generate_ShouldPropagateExtractorFailure() {
        Result<ColumnGenerationBatch<Bunch>, ErrorCode, Error<ErrorCode>> result = Run(
            extract: (path, res) => Results.Failed<Bunch?>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, "no bunch for this path")));

        result.IsOk.Should().BeFalse();
    }

    /// <summary>**搜索失败直接向上返回** / A search failure propagates.</summary>
    [Fact]
    public void Generate_ShouldPropagateSearchFailure() {
        Result<ColumnGenerationBatch<Bunch>, ErrorCode, Error<ErrorCode>> result =
            new NetworkBunchGenerationContext().Generate<TestNode, TestArc, State, Bunch>(
                LineProblem(),
                new PathSearchBudget(MaximumLabels: 0),
                Scope(),
                extract: (path, res) => Results.Ok<Bunch?>(new Bunch(path.Node.Value)));

        result.IsOk.Should().BeFalse();
    }

    /// <summary>**作用域原样带出** / The scope is carried through unchanged.</summary>
    [Fact]
    public void Generate_ShouldCarryTheScope() {
        ColumnGenerationBatch<Bunch> batch = Run().Value;

        batch.Scope.Should().NotBeNull();
        batch.Scope!.Request.Should().Be("r1");
    }

    /// <summary>**搜索完成后回调被调用一次** / The completion callback fires once.</summary>
    [Fact]
    public void Generate_ShouldInvokeTheCompletionCallbackOnce() {
        int calls = 0;
        Run(onCompleted: _ => calls++).Value.Columns.Should().HaveCount(1);

        calls.Should().Be(1);
    }

    /// <summary>**未认证的支配按启发式，不是穷尽** / Uncertified dominance counts as heuristic, not exhaustion.</summary>
    [Theory]
    [InlineData(PathSearchCompletion.Exhausted, ColumnGenerationCompletion.Exhausted)]
    [InlineData(PathSearchCompletion.BudgetLimit, ColumnGenerationCompletion.BudgetLimit)]
    [InlineData(PathSearchCompletion.Interrupted, ColumnGenerationCompletion.Interrupted)]
    [InlineData(PathSearchCompletion.UncertifiedDominance, ColumnGenerationCompletion.Heuristic)]
    [InlineData(PathSearchCompletion.UncertifiedValidation, ColumnGenerationCompletion.Heuristic)]
    public void Translate_ShouldMapEachCompletion(PathSearchCompletion from, ColumnGenerationCompletion to) =>
        NetworkBunchGenerationContext.Translate(from, rejected: false).Should().Be(to);

    /// <summary>**任何完成状态遇上拒绝都降级** / Any completion degrades when a path was rejected.</summary>
    [Theory]
    [InlineData(PathSearchCompletion.Exhausted)]
    [InlineData(PathSearchCompletion.BudgetLimit)]
    [InlineData(PathSearchCompletion.Interrupted)]
    public void Translate_ShouldDegradeOnRejection(PathSearchCompletion from) =>
        NetworkBunchGenerationContext.Translate(from, rejected: true)
            .Should().Be(ColumnGenerationCompletion.Heuristic);

    /// <summary>测试节点载荷 / A test node payload.</summary>
    /// <param name="Id">标识 / Identity.</param>
    internal sealed record TestNode(string Id);

    /// <summary>测试弧载荷 / A test arc payload.</summary>
    /// <param name="Id">标识 / Identity.</param>
    /// <param name="From">起点 / Origin.</param>
    /// <param name="To">终点 / Destination.</param>
    internal sealed record TestArc(string Id, string From, string To);

    /// <summary>测试资源状态 / A test resource state.</summary>
    /// <param name="Cost">累计成本 / Accumulated cost.</param>
    internal sealed record State(int Cost);

    /// <summary>业务任务束 / A business bunch.</summary>
    /// <param name="Id">标识 / Identity.</param>
    internal sealed record Bunch(string Id);
}
