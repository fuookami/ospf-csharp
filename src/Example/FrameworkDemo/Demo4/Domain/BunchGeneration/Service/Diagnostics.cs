#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Service;

/// <summary>
/// 路线图生成的诊断计数 / Diagnostics counters for route-graph generation.
/// </summary>
/// <remarks>
/// 里两个**纯计数**类型，不打印、不写文件——诊断是一份可读的数据，输出由调用方决定。
/// 这正是不该做成"打印函数"的理由：**进 IO 的代码没法在判据里断言**。
///
/// **天花板：目前没有生产者。** `RouteGraphGenerator` 会往这些字段里累加，C# 侧的生成器还没接——
/// 门禁里能断言的是"结构在、默认是零、互不影响"，不是"生成器真的填了"。升级路径：把计数接进 `RouteGraphGenerator` 的展开循环。
///
/// Its holds two **pure counter** types — no printing, no file writing. Diagnostics are readable data; producing output is the caller's business. That is exactly
/// why this must not become a "print function": **code that reaches IO cannot be asserted on in a criterion**.
///
/// **Ceiling: there is no producer yet.** Its `RouteGraphGenerator` accumulates into these fields, while the C# generator is not wired — the gates can assert "the shape
/// exists, the defaults are zero, the counters are independent", not "the generator really fills them". Upgrade path: wire the counters into `RouteGraphGenerator`'s expansion loop.
/// </remarks>
public sealed class RouteGraphDiagnostics {
    /// <summary>候选任务数 / Candidate task count.</summary>
    public int CandidateTaskCount { get; set; }

    /// <summary>可行展开次数 / Feasible expansion count.</summary>
    public int FeasibleExpansionCount { get; set; }

    /// <summary>不可行展开次数 / Infeasible expansion count.</summary>
    public int InfeasibleExpansionCount { get; set; }
}

/// <summary>
/// 定价子问题的诊断计数 / Diagnostics counters for the pricing subproblem.
/// </summary>
/// <remarks>
/// 比路线图那一份多九项：标签的展开/被支配/触顶、负检验数标签、生成列与无效列，再加三个**成对的布尔**——
/// 触顶截断、找不到可行路径、找不到负检验数。三个布尔不是"错误标志"，是"这一轮为什么没有产出"的三个不同答案；
/// 把它们合成一个 `bool` 会让排查时说不清是"标签太少"还是"根本没有负检验数"。
///
/// Nine more than the route-graph set: label expansion, domination and the label limit, negative-reduced-cost labels, generated and invalid columns, plus three **paired booleans** —
/// truncated by the label limit, no feasible path, no negative reduced cost. Those three are not error flags but three different answers to "why did this round produce nothing";
/// merging them into one `bool` would leave no way to tell "too few labels" from "no negative reduced cost at all".
/// </remarks>
public sealed class PricingDiagnostics {
    /// <summary>候选任务数 / Candidate task count.</summary>
    public int CandidateTaskCount { get; set; }

    /// <summary>可行展开次数 / Feasible expansion count.</summary>
    public int FeasibleExpansionCount { get; set; }

    /// <summary>不可行展开次数 / Infeasible expansion count.</summary>
    public int InfeasibleExpansionCount { get; set; }

    /// <summary>展开的标签数 / Expanded label count.</summary>
    public int ExpandedLabelCount { get; set; }

    /// <summary>被支配的标签数 / Dominated label count.</summary>
    public int DominatedLabelCount { get; set; }

    /// <summary>触到标签上限的次数 / Label-limit reached count.</summary>
    public int LabelLimitReachedCount { get; set; }

    /// <summary>负检验数标签数 / Negative reduced cost label count.</summary>
    public int NegativeReducedCostLabelCount { get; set; }

    /// <summary>生成的列数 / Generated column count.</summary>
    public int GeneratedColumnCount { get; set; }

    /// <summary>无效列数 / Invalid column count.</summary>
    public int InvalidColumnCount { get; set; }

    /// <summary>搜索是否因标签上限被截断 / Whether the search was truncated by the label limit.</summary>
    public bool SearchTruncatedByLabelLimit { get; set; }

    /// <summary>是否没有可行路径 / Whether no feasible path was found.</summary>
    public bool NoFeasiblePath { get; set; }

    /// <summary>是否没有负检验数 / Whether no negative reduced cost was found.</summary>
    public bool NoNegativeReducedCost { get; set; }
}
