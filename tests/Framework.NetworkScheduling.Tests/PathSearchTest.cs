// 路径标签搜索测试 / Tests for the resource-label path search.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Path;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using SearchResult = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Framework.NetworkScheduling.Domain.Path.PathSearchResult<Fuookami.Ospf.Framework.NetworkScheduling.Tests.PathSearchTest.State, Fuookami.Ospf.Framework.NetworkScheduling.Tests.PathSearchTest.TestArc>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 路径标签搜索 / The resource-label path search.
/// </summary>
public sealed class PathSearchTest {
    /// <summary>线性图找到唯一路径 / A line graph yields exactly one path.</summary>
    [Fact]
    public void Search_ShouldFindLinePath() {
        SearchResult result = Run(LineGraph(), "a", "c");

        result.Value.Paths.Should().ContainSingle();
        result.Value.Completion.Should().Be(PathSearchCompletion.Exhausted);
        result.Value.Paths[0].Node.Value.Should().Be("c");
    }

    /// <summary>不可达时穷尽且无路径 / An unreachable target exhausts with no path.</summary>
    [Fact]
    public void Search_ShouldExhaustWhenUnreachable() {
        SearchResult result = Run(LineGraph(), "c", "a");

        result.Value.Paths.Should().BeEmpty();
        result.Value.Completion.Should().Be(PathSearchCompletion.Exhausted);
    }

    /// <summary>起点不存在时报错 / A missing source is an error.</summary>
    [Fact]
    public void Search_ShouldRejectMissingSource() =>
        Run(LineGraph(), "ghost", "c").IsOk.Should().BeFalse();

    /// <summary>负结果上限时报错 / A negative result limit is an error.</summary>
    [Fact]
    public void Search_ShouldRejectNegativeResultLimit() =>
        new PathSearchContext()
            .Search(LineProblem("a"), maximumResults: -1).IsOk.Should().BeFalse();

    /// <summary>零标签预算时报错 / A zero label budget is an error.</summary>
    [Fact]
    public void Search_ShouldRejectZeroBudget() =>
        new PathSearchContext()
            .Search(LineProblem("a"), new PathSearchBudget(MaximumLabels: 0)).IsOk.Should().BeFalse();

    /// <summary>起点被过滤时穷尽且无路径 / A filtered-out source exhausts with no path.</summary>
    [Fact]
    public void Search_ShouldRespectNodeFilterOnSource() {
        var problem = new PathProblem<TestNode, TestArc, State>(
            LineGraph(),
            new NetworkNodeId("a"),
            new State(0, new HashSet<NetworkNodeId>()),
            (label, arc) => Results.Ok<State?>(null),
            (node, state) => node.Value == "c",
            AllowedNode: node => node.Value != "a");

        SearchResult result = new PathSearchContext().Search(problem);

        result.Value.Paths.Should().BeEmpty();
        result.Value.Completion.Should().Be(PathSearchCompletion.Exhausted);
    }

    /// <summary>
    /// **标签总预算耗尽时终态是 BudgetLimit，不是 Exhausted**：预算耗尽不能冒充穷尽。
    /// **Reaching the label budget reports BudgetLimit, never Exhausted**: an exhausted budget must not masquerade as exhaustion.
    /// </summary>
    [Fact]
    public void Search_ShouldReportBudgetLimit() {
        var problem = new PathProblem<TestNode, TestArc, State>(
            WideGraph(),
            new NetworkNodeId("a"),
            new State(0, new HashSet<NetworkNodeId>()),
            (label, arc) => Results.Ok<State?>(new State(0, new HashSet<NetworkNodeId>())),
            (node, state) => node.Value == "z");

        SearchResult result = new PathSearchContext()
            .Search(problem, new PathSearchBudget(MaximumLabels: 2));

        result.Value.Completion.Should().Be(PathSearchCompletion.BudgetLimit);
        result.Value.Complete.Should().BeFalse();
    }

    /// <summary>
    /// **外部中断时终态是 Interrupted**：中断不能冒充穷尽。
    /// **An external interruption reports Interrupted**: an interruption must not masquerade as exhaustion.
    /// </summary>
    [Fact]
    public void Search_ShouldReportInterruption() {
        SearchResult result = new PathSearchContext().Search(
            LineProblem("a"), new PathSearchBudget(ShouldStop: () => true));

        result.Value.Completion.Should().Be(PathSearchCompletion.Interrupted);
        result.Value.Complete.Should().BeFalse();
    }

    /// <summary>
    /// **路径复核拒绝时终态是 UncertifiedValidation**：完整路径校验没用过，不能算证明。
    /// **A rejected trace reports UncertifiedValidation**: validation that rejected a path is no proof.
    /// </summary>
    [Fact]
    public void Search_ShouldReportValidationRejection() {
        var problem = new PathProblem<TestNode, TestArc, State>(
            LineGraph(),
            new NetworkNodeId("a"),
            new State(0, new HashSet<NetworkNodeId>()),
            (label, arc) => Results.Ok<State?>(new State(label.State.Cost + 1, new HashSet<NetworkNodeId>())),
            (node, state) => node.Value == "c",
            Validate: _ => Results.Ok(false));

        SearchResult result = new PathSearchContext().Search(problem);

        result.Value.Paths.Should().BeEmpty();
        result.Value.Completion.Should().Be(PathSearchCompletion.UncertifiedValidation);
        result.Value.TerminalCount.Should().Be(1);
    }

    /// <summary>
    /// **未认证支配剪枝后终态是 UncertifiedDominance**：剪枝本身正确与否未经证明。
    /// **Uncertified dominance pruning reports UncertifiedDominance**: whether the pruning is sound is unproven.
    /// </summary>
    [Fact]
    public void Search_ShouldReportUncertifiedDominance() {
        var problem = new PathProblem<TestNode, TestArc, State>(
            ConvergingGraph(),
            new NetworkNodeId("a"),
            new State(0, new HashSet<NetworkNodeId>()),
            (label, arc) => Results.Ok<State?>(new State(label.State.Cost + 1, new HashSet<NetworkNodeId>())),
            (node, state) => node.Value == "d",
            Dominates: (left, right) => left.State.Cost <= right.State.Cost,
            DominanceIsExact: false);

        SearchResult result = new PathSearchContext().Search(problem);

        result.Value.Completion.Should().Be(PathSearchCompletion.UncertifiedDominance);
        result.Value.HeuristicDominanceUsed.Should().BeTrue();
    }

    /// <summary>认证支配不污染终态 / Certified dominance leaves the completion state clean.</summary>
    [Fact]
    public void Search_ShouldKeepExhaustedWithCertifiedDominance() {
        var problem = new PathProblem<TestNode, TestArc, State>(
            ConvergingGraph(),
            new NetworkNodeId("a"),
            new State(0, new HashSet<NetworkNodeId>()),
            (label, arc) => Results.Ok<State?>(new State(label.State.Cost + 1, new HashSet<NetworkNodeId>())),
            (node, state) => node.Value == "d",
            Dominates: (left, right) => left.State.Cost <= right.State.Cost,
            DominanceIsExact: true);

        SearchResult result = new PathSearchContext().Search(problem);

        result.Value.Completion.Should().Be(PathSearchCompletion.Exhausted);
        result.Value.HeuristicDominanceUsed.Should().BeFalse();
    }

    /// <summary>
    /// **终止标签与可扩展标签不能互相支配**：否则一条已经到达终点的路径会被中途标签剪掉。
    /// **Terminal and expandable labels cannot dominate each other**: otherwise a completed path gets pruned by a mid-route label.
    /// </summary>
    [Fact]
    public void Search_ShouldNotLetTerminalDominateExpandable() {
        var problem = new PathProblem<TestNode, TestArc, State>(
            DiamondGraph(),
            new NetworkNodeId("a"),
            new State(0, new HashSet<NetworkNodeId>()),
            (label, arc) => Results.Ok<State?>(new State(label.State.Cost + 1, new HashSet<NetworkNodeId>())),
            (node, state) => node.Value == "d",
            Dominates: (left, right) => true,
            DominanceIsExact: true);

        SearchResult result = new PathSearchContext().Search(problem);

        result.Value.Paths.Should().NotBeEmpty();
    }

    /// <summary>前驱链可完整回溯 / The predecessor chain traces back completely.</summary>
    [Fact]
    public void Search_ShouldTraceFullChain() {
        SearchResult result = Run(LineGraph(), "a", "c");
        PathLabel<State, TestArc> terminal = result.Value.Paths[0];

        IReadOnlyList<PathLabel<State, TestArc>> chain = result.Value.Trace(terminal);

        chain.Should().HaveCount(3);
        chain[0].Node.Value.Should().Be("a");
        chain[^1].Node.Value.Should().Be("c");
    }

    /// <summary>
    /// **前驱链与标签数组不符时返回空链**：半截路径会让上游以为找到了一条更短路线。
    /// **A chain disagreeing with the label array yields an empty trace**: a truncated path would look like a shorter route.
    /// </summary>
    [Fact]
    public void Trace_ShouldRejectMismatchedChain() {
        var result = new PathSearchResult<State, TestArc>(
            Array.Empty<PathLabel<State, TestArc>>(),
            Array.Empty<PathLabel<State, TestArc>>(),
            PathSearchCompletion.Exhausted, 0, 0, 0);
        var stray = new PathLabel<State, TestArc>(
            0, new NetworkNodeId("a"), new State(0, new HashSet<NetworkNodeId>()),
            null, null, new HashSet<NetworkNodeId>(), 0);

        result.Trace(stray).Should().BeEmpty();
    }

    /// <summary>
    /// **输出上限不改变搜索本身**：只限制返回条数，`Completion` 仍反映真实穷尽状态。
    /// **The output limit does not alter the search**: it only bounds the returned count while `Completion` still reflects the
    /// real exhaustion state.
    /// </summary>
    [Fact]
    public void Search_ShouldNotTruncateSearchForOutputLimit() {
        SearchResult limited = new PathSearchContext().Search(
            LineProblem("a"), maximumResults: 1);
        SearchResult unlimited = Run(LineGraph(), "a", "c");

        limited.Value.Paths.Should().HaveCount(1);
        limited.Value.Completion.Should().Be(PathSearchCompletion.Exhausted);
        limited.Value.ExpandedLabels.Should().Be(unlimited.Value.ExpandedLabels);
    }

    /// <summary>循环图未声明有限域时报错 / A cyclic graph without a finite bound is an error.</summary>
    [Fact]
    public void Search_ShouldRejectCyclicWalkWithoutBound() {
        var problem = new PathProblem<TestNode, TestArc, State>(
            CycleGraph(),
            new NetworkNodeId("a"),
            new State(0, new HashSet<NetworkNodeId>()),
            (label, arc) => Results.Ok<State?>(new State(label.State.Cost + 1, new HashSet<NetworkNodeId>())),
            (node, state) => node.Value == "z",
            Elementary: false);

        new PathSearchContext().Search(problem).IsOk.Should().BeFalse();
    }

    /// <summary>循环图声明有限步数后可搜索 / A cyclic graph searches once a finite step bound is declared.</summary>
    [Fact]
    public void Search_ShouldAcceptCyclicWalkWithBound() {
        var problem = new PathProblem<TestNode, TestArc, State>(
            CycleGraph(),
            new NetworkNodeId("a"),
            new State(0, new HashSet<NetworkNodeId>()),
            (label, arc) => Results.Ok<State?>(new State(label.State.Cost + 1, new HashSet<NetworkNodeId>())),
            (node, state) => node.Value == "z",
            Elementary: false,
            MaximumSteps: 3);

        new PathSearchContext().Search(problem).IsOk.Should().BeTrue();
    }

    /// <summary>执行一次线性图搜索 / Run a line-graph search.</summary>
    private static SearchResult Run(NetworkGraph<TestNode, TestArc> graph, string source, string target) =>
        new PathSearchContext().Search(new PathProblem<TestNode, TestArc, State>(
            graph,
            new NetworkNodeId(source),
            new State(0, new HashSet<NetworkNodeId>()),
            (label, arc) => Results.Ok<State?>(new State(label.State.Cost + 1, new HashSet<NetworkNodeId>())),
            (node, state) => node.Value == target));

    /// <summary>构造线性图的问题 / Build a line-graph problem.</summary>
    private static PathProblem<TestNode, TestArc, State> LineProblem(string source) =>
        new(
            LineGraph(),
            new NetworkNodeId(source),
            new State(0, new HashSet<NetworkNodeId>()),
            (label, arc) => Results.Ok<State?>(new State(label.State.Cost + 1, new HashSet<NetworkNodeId>())),
            (node, state) => node.Value == "c");

    /// <summary>a → b → c / a → b → c.</summary>
    private static NetworkGraph<TestNode, TestArc> GraphOf(
        string[] nodes, (string Id, string From, string To)[] arcs) =>
        NetworkGraph<TestNode, TestArc>.Invoke(
            nodes.Select(id => new TestNode(id)).ToList(),
            arcs.Select(arc => new TestArc(arc.Id, arc.From, arc.To)).ToList(),
            new NetworkGraphAccess<TestNode, TestArc>(
                NodeId: node => new NetworkNodeId(node.Id),
                ArcId: arc => new NetworkArcId(arc.Id),
                From: arc => new NetworkNodeId(arc.From),
                To: arc => new NetworkNodeId(arc.To))).Value;

    /// <summary>线性图 / A line graph.</summary>
    private static NetworkGraph<TestNode, TestArc> LineGraph() =>
        GraphOf(new[] { "a", "b", "c" }, new[] { ("e1", "a", "b"), ("e2", "b", "c") });

    /// <summary>菱形图 / A diamond graph.</summary>
    private static NetworkGraph<TestNode, TestArc> DiamondGraph() =>
        GraphOf(
            new[] { "a", "b", "c", "d" },
            new[] { ("e1", "a", "b"), ("e2", "a", "c"), ("e3", "b", "d"), ("e4", "c", "d") });

    /// <summary>
    /// 两条路径汇聚到同一中间节点的图：a→b（价 1）与 a→c→b（价 2），再从 b 到 d。
    /// 只有同节点的标签才会互相支配，因此支配剪枝必须在这种图上才发生。
    /// A graph whose two paths converge on one intermediate node: a→b (cost 1) and a→c→b (cost 2), then b→d. Only labels at the
    /// same node compete, so dominance pruning requires this shape.
    /// </summary>
    private static NetworkGraph<TestNode, TestArc> ConvergingGraph() =>
        GraphOf(
            new[] { "a", "b", "c", "d" },
            new[] { ("e1", "a", "b"), ("e2", "a", "c"), ("e3", "c", "b"), ("e4", "b", "d") });

    /// <summary>宽图，用于预算耗尽 / A wide graph used to exhaust the budget.</summary>
    private static NetworkGraph<TestNode, TestArc> WideGraph() =>
        GraphOf(
            new[] { "a", "b", "c", "y", "z" },
            new[] { ("e1", "a", "b"), ("e2", "a", "c"), ("e3", "b", "y"), ("e4", "c", "y"), ("e5", "y", "z") });

    /// <summary>有环图 / A cyclic graph.</summary>
    private static NetworkGraph<TestNode, TestArc> CycleGraph() =>
        GraphOf(new[] { "a", "b" }, new[] { ("e1", "a", "b"), ("e2", "b", "a") });

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
    /// <param name="Extra">附加集合占位 / Placeholder set.</param>
    internal sealed record State(int Cost, IReadOnlySet<NetworkNodeId> Extra);

}
