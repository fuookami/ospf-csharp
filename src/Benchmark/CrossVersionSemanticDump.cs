#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System.Threading.Tasks;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Ordinary;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// 跨版本共有的输出语义转储（不计时） / A cross-version dump of shared output semantics (untimed).
/// </summary>
/// <remarks>
/// 与基线 `CrossVersionSemanticDump.kt`（335 行，`19c72455` 新增）对应。 它的用途不是计时，而是让两端各跑一次、
/// 逐行对比：网络可达与强连通分量、反向视图一致性、token 索引、素性缓存——每行都是"生产入口算出来的确定值"。
///
/// 本批只转储三段：`NET`（网络）、`TOK`（token）、`PRM`（素性）。 其余三段是待办，
/// 不是"没有"——`Pending` 常量逐个写明为什么还没搬，并由判据 `CrossVersionSemanticDumpTests` 钉住
/// "待办清单必须与已实现段不重叠、且每段都写了理由"。
///
/// 一个必须写明的可比性限制： 基线的 `orderDigest`／`sortedDigest` 用的是 Kotlin 的 `String.hashCode()`，
/// 那不是跨语言可比的值。 所以本转储不输出 `hashCode` 摘要；可逐行对比的字段是计数、排序后原样拼接的取值、
/// 以及布尔量。 含 `hashCode` 的行在跨语言 diff 里只能各自记录、不能互比——这一点写在这里，而不是留给下一个人发现。
///
/// Corresponds to the 335-line baseline. Its purpose is not timing but a line-by-line diff of two runs: network reachability and
/// strongly connected components, reverse-view consistency, token indices, the primality cache — every line a deterministic value from a production
/// entry point. This batch dumps three sections: `NET`, `TOK`, `PRM`. The other three are *pending*, not absent: the `Pending` constant states
/// why each is not ported yet, and a criterion pins that the pending list never overlaps what is implemented and always carries a reason.
/// One comparability limit must be stated: the baseline's digests use Kotlin's `String.hashCode()`, which is not comparable across languages, so
/// this dump emits no `hashCode` digest; the diffable fields are counts, sorted raw values and booleans. Lines built on `hashCode` can only be
/// recorded per side, never compared across — written here instead of left for the next reader to discover.
/// </remarks>
public static class CrossVersionSemanticDump
{
    /// <summary>
    /// 还没搬的三段，以及为什么 / The three sections not ported yet, and why.
    /// </summary>
    /// <remarks>
    /// 判据要求这份清单与已实现段不重叠。 清单里每一条都必须带理由： "还没做"单独一句会一直挂着，
    /// 而"缺的是哪个入口"是可以被安排掉的。
    ///
    /// A criterion requires this list to never overlap the implemented sections. Every entry carries a reason: "not done yet" on its own hangs
    /// forever, while "the missing entry is X" can be scheduled.
    /// </remarks>
    // **B1309：三段补齐之后这份清单是空的。** **它保留下来，是因为"清单为空"本身是一个可断言的结论：**
    // **判据要求它与 `Implemented` 不重叠，一旦再有人把某段退回去，必须同时把理由写进这里，而不是悄悄少几行。**
    //
    // **B1309: with the last three sections ported this list is empty.** **It stays because "the list is empty" is itself an assertable conclusion: a
    // criterion requires it never to overlap `Implemented`, so anyone deferring a section again has to write the reason here rather than quietly
    // emitting fewer lines.**
    public static readonly IReadOnlyList<(string Section, string Reason)> Pending
        = Array.Empty<(string Section, string Reason)>();

    /// <summary>已实现的三段 / The three sections that are implemented.</summary>
    public static readonly IReadOnlyList<string> Implemented
        = new[] { "NET", "PRJ", "GEN", "TRUNC", "TOK", "PRM" };

    /// <summary>转储全部已实现段 / Dump every implemented section.</summary>
    /// <returns>逐行文本 / The line-oriented text</returns>
    public static string Run() {
        var text = new StringBuilder();
        DumpNetwork(text);
        DumpProjection(text);
        DumpGeneration(text, maxPlans: 100_000L, prefix: "GEN");
        DumpGeneration(text, maxPlans: 200L, prefix: "TRUNC");
        DumpToken(text);
        DumpPrime(text);
        return text.ToString();
    }

    /// <summary>弧 / An arc.</summary>
    /// <param name="Id">弧标识 / Arc identity.</param>
    /// <param name="From">起点 / Tail.</param>
    /// <param name="To">终点 / Head.</param>
    internal sealed record Arc(NetworkArcId Id, NetworkNodeId From, NetworkNodeId To);

    // ==================== Network ====================

    /// <summary>网络段：可达、强连通分量、反向视图一致性 / Network: reachability, SCC, reverse-view consistency.</summary>
    /// <param name="text">输出 / The output.</param>
    private static void DumpNetwork(StringBuilder text) {
        var access = new NetworkGraphAccess<NetworkNodeId, Arc>(
            NodeId: node => node,
            ArcId: arc => arc.Id,
            From: arc => arc.From,
            To: arc => arc.To);

        foreach (int size in new[] { 256, 1_024 }) {
            var nodes = new List<NetworkNodeId>(size);
            for (int index = 0; index < size; ++index) {
                nodes.Add(new NetworkNodeId("n" + index));
            }
            var arcs = new List<Arc>(size * 4);
            for (int from = 0; from < size; ++from) {
                for (int step = 1; step <= 4; ++step) {
                    arcs.Add(new Arc(
                        new NetworkArcId("a" + from + "_" + step),
                        nodes[from],
                        nodes[(from + step) % size]));
                }
            }

            NetworkGraph<NetworkNodeId, Arc> graph = BuildGraph(nodes, arcs, access);
            var topology = new NetworkTopology<NetworkNodeId, Arc>(graph);
            IReadOnlySet<NetworkNodeId> reachable = Reach(topology, nodes[0]);
            Line(text, "NET|" + size + "|reachableFirst|count=" + reachable.Count
                + "|sortedDigest=" + SortedDigest(reachable.Select(id => id.Value)));

            IReadOnlyList<IReadOnlySet<NetworkNodeId>> components = topology.StronglyConnectedComponents();
            Line(text, "NET|" + size + "|scc|count=" + components.Count
                + "|sizes=" + string.Join(",", components.Select(component => component.Count).OrderBy(count => count)));

            NetworkGraph<NetworkNodeId, Arc> reverse = graph.Reverse();
            IReadOnlyList<Arc> reverseRead = reverse.Outgoing(nodes[0]);
            IReadOnlyList<Arc> incoming = graph.Incoming(nodes[0]);
            Line(text, "NET|" + size + "|reverseOutgoing|ids=" + string.Join(",", reverseRead.Select(arc => arc.Id.Value)));
            Line(text, "NET|" + size + "|reverseEqualsIncoming|same="
                + Flag(Same(reverseRead.Select(arc => arc.Id.Value), incoming.Select(arc => arc.Id.Value))));

            // **反向视图必须在全节点上逐项一致：** **这是"反向读不需要第二套索引"的设计前提。**
            // **The reverse view must agree node by node on <b>every</b> node: that is the premise behind "reverse reads need no second index".**
            int reverseMismatch = 0;
            int reverseIncomingMismatch = 0;
            foreach (NetworkNodeId node in nodes) {
                if (!Same(reverse.Outgoing(node).Select(arc => arc.Id.Value), graph.Incoming(node).Select(arc => arc.Id.Value))) {
                    ++reverseMismatch;
                }
                if (!Same(reverse.Incoming(node).Select(arc => arc.Id.Value), graph.Outgoing(node).Select(arc => arc.Id.Value))) {
                    ++reverseIncomingMismatch;
                }
            }
            Line(text, "NET|" + size + "|reverseExhaustiveMismatch|outgoing=" + reverseMismatch
                + "|incoming=" + reverseIncomingMismatch);

            IReadOnlySet<NetworkNodeId> reverseReachable =
                Reach(new NetworkTopology<NetworkNodeId, Arc>(reverse), nodes[0]);
            Line(text, "NET|" + size + "|reverseReachable|count=" + reverseReachable.Count
                + "|sortedDigest=" + SortedDigest(reverseReachable.Select(id => id.Value)));

            IReadOnlySet<NetworkNodeId> warm = Reach(new NetworkTopology<NetworkNodeId, Arc>(graph), nodes[7]);
            Line(text, "NET|" + size + "|warmReachable|count=" + warm.Count
                + "|sortedDigest=" + SortedDigest(warm.Select(id => id.Value)));

            NetworkTopology<NetworkNodeId, Arc> filteredTopology = new NetworkTopology<NetworkNodeId, Arc>(graph)
                .Filtered(
                    allowedArc: arc => arc.To != nodes[^1],
                    allowedNode: node => node != nodes[^1]);
            IReadOnlySet<NetworkNodeId> filtered = Reach(filteredTopology, nodes[0]);
            Line(text, "NET|" + size + "|filteredReachable|count=" + filtered.Count
                + "|sortedDigest=" + SortedDigest(filtered.Select(id => id.Value)));

            IReadOnlySet<NetworkNodeId> cold = Reach(new NetworkTopology<NetworkNodeId, Arc>(BuildGraph(nodes, arcs, access)), nodes[0]);
            Line(text, "NET|" + size + "|coldEqualsWarm|same=" + Flag(reachable.SetEquals(cold)));
        }
    }

    // ==================== Token ====================

    /// <summary>token 段：冷/热索引校验和、缺失查不到、按变量项查 / Token: cold and warm checksums, misses, lookups by item.</summary>
    /// <param name="text">输出 / The output.</param>
    private static void DumpToken(StringBuilder text) {
        foreach (int size in new[] { 1_024, 4_096 }) {
            var variables = new List<RealVar>(size);
            for (int index = 0; index < size; ++index) {
                variables.Add(new RealVar("tok" + index));
            }

            using var writer = new ManualTokenList<Flt64>(checkTokenExists: true);
            writer.Add(variables);
            var map = writer.Tokens.ToDictionary(token => token.Key, token => token);
            var list = new TokenList<Flt64>(map);
            try {
                IReadOnlyList<Token<Flt64>> ordered = list.TokensInSolver;
                long checksum = 0L;
                foreach (Token<Flt64> token in map.Values) {
                    checksum += list.IndexOf(token) ?? -1;
                }
                Line(text, "TOK|" + size + "|coldChecksum|" + checksum
                    + "|orderedFirst=" + ordered[0].SolverIndex + "|orderedLast=" + ordered[^1].SolverIndex);

                long warmChecksum = 0L;
                foreach (Token<Flt64> token in map.Values) {
                    warmChecksum += list.IndexOf(token) ?? -1;
                }
                Line(text, "TOK|" + size + "|warmEqualsCold|same=" + Flag(warmChecksum == checksum));

                int? miss = list.IndexOf(new RealVar("tok" + size));
                Line(text, "TOK|" + size + "|missIsNull|" + Flag(miss is null));

                var byItem = new List<string>(4);
                foreach (Token<Flt64> token in map.Values.Take(4)) {
                    byItem.Add((list.IndexOf(token.Variable) ?? -1).ToString());
                }
                Line(text, "TOK|" + size + "|indexOfItem|" + string.Join(",", byItem));
            }
            finally {
                list.Dispose();
            }
        }
    }

    // ==================== Prime ====================

    /// <summary>素性段：小范围逐值、区间取值、越界、边界、重复读 / Primality: per-value range, list, beyond sieve, boundaries, repeats.</summary>
    /// <param name="text">输出 / The output.</param>
    private static void DumpPrime(StringBuilder text) {
        var cache = new PrimeCache();
        int count = 0;
        long digest = 0L;
        for (int value = 0; value <= 20_000; ++value) {
            if (cache.IsPrime(new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)value))) {
                ++count;
                digest = digest * 31 + value;
            }
        }
        Line(text, "PRM|isPrime0To20000|count=" + count + "|digest=" + digest);

        IReadOnlyList<Fuookami.Ospf.Math.Algebra.Number.UInt64> primes =
            cache.GetPrimes(new Fuookami.Ospf.Math.Algebra.Number.UInt64(20_000UL));
        Line(text, "PRM|getPrimes|count=" + primes.Count
            + "|head=" + string.Join(",", primes.Take(10).Select(prime => prime.ToString()))
            + "|tail=" + string.Join(",", primes.Skip(System.Math.Max(0, primes.Count - 5)).Select(prime => prime.ToString())));

        var beyond = new List<string>(4);
        foreach (ulong value in new ulong[] { 1_000_003UL, 1_999_999UL, 999_999_937UL, 1_000_000_007UL }) {
            beyond.Add(value + "=" + Flag(cache.IsPrime(new Fuookami.Ospf.Math.Algebra.Number.UInt64(value))));
        }
        Line(text, "PRM|beyondSieve|" + string.Join(",", beyond));

        Line(text, "PRM|boundaries|0=" + Flag(cache.IsPrime(new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL)))
            + ",1=" + Flag(cache.IsPrime(new Fuookami.Ospf.Math.Algebra.Number.UInt64(1UL)))
            + ",2=" + Flag(cache.IsPrime(new Fuookami.Ospf.Math.Algebra.Number.UInt64(2UL))));

        int repeatCount = 0;
        for (int value = 0; value <= 5_000; ++value) {
            if (cache.IsPrime(new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)value))) {
                ++repeatCount;
            }
        }
        Line(text, "PRM|warmSameAsCold|count=" + repeatCount);
    }

    /// <summary>追加一行 / Append one line.</summary>
    /// <param name="text">输出 / The output.</param>
    /// <param name="line">行内容 / The line.</param>
    private static void Line(StringBuilder text, string line) => text.Append(line).Append('\n');

    /// <summary>排序后原样拼接——这是跨语言唯一可比的"摘要"形态 / Sorted raw join: the only cross-language comparable digest.</summary>
    /// <param name="values">取值 / Values.</param>
    /// <returns>拼接结果 / The joined text</returns>
    private static string SortedDigest(IEnumerable<string> values) =>
        string.Join(";", values.OrderBy(value => value, StringComparer.Ordinal));

    /// <summary>布尔量渲染成小写：基线输出 `true`／`false`，而 `bool.ToString()` 给的是 `True`／`False`——
    /// 逐行 diff 会把每一行都算成差异。
    ///
    /// Booleans render lowercase: the baseline prints `true`/`false` while `bool.ToString()` gives `True`/`False`, which would make every
    /// line look different in a line-by-line diff.</summary>
    ///
    /// <param name="value">布尔值 / The boolean.</param>
    /// <returns>`true` 或 `false` / `true` or `false`</returns>
    private static string Flag(bool value) => value ? "true" : "false";

    /// <summary>逐项相同（含顺序）/ Item-by-item equality, order included.</summary>
    /// <param name="left">左侧 / Left.</param>
    /// <param name="right">右侧 / Right.</param>
    /// <returns>是否完全相同 / Whether they are identical</returns>
    private static bool Same(IEnumerable<string> left, IEnumerable<string> right) =>
        left.SequenceEqual(right, StringComparer.Ordinal);

    /// <summary>建图，失败即抛 / Build the graph, throwing on failure.</summary>
    /// <param name="nodes">节点 / Nodes.</param>
    /// <param name="arcs">弧 / Arcs.</param>
    /// <param name="access">拓扑约定 / Topology access.</param>
    /// <returns>图 / The graph</returns>
    private static NetworkGraph<NetworkNodeId, Arc> BuildGraph(
        IReadOnlyList<NetworkNodeId> nodes,
        IReadOnlyList<Arc> arcs,
        NetworkGraphAccess<NetworkNodeId, Arc> access) {
        Result<NetworkGraph<NetworkNodeId, Arc>, ErrorCode, Error<ErrorCode>> result =
            NetworkGraph<NetworkNodeId, Arc>.Invoke(nodes, arcs, access);
        return result is Ok<NetworkGraph<NetworkNodeId, Arc>, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("转储图必须可构造 / the dump graph must build: " + result);
    }

    /// <summary>求可达集，失败即抛 / Compute reachability, throwing on failure.</summary>
    /// <param name="topology">拓扑 / The topology.</param>
    /// <param name="source">起点 / The source.</param>
    /// <returns>可达节点 / Reachable nodes</returns>
    private static IReadOnlySet<NetworkNodeId> Reach(NetworkTopology<NetworkNodeId, Arc> topology, NetworkNodeId source) {
        Result<IReadOnlySet<NetworkNodeId>, ErrorCode, Error<ErrorCode>> result = topology.Reachable([source]);
        return result is Ok<IReadOnlySet<NetworkNodeId>, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("转储可达集必须可算 / the dump reachability must compute: " + result);
    }

    // ==================== Projection (B1309) ====================

    /// <summary>
    /// 投影段：快照、定价、主问题登记 / The projection section: snapshot, pricing, master registration.
    /// </summary>
    /// <remarks>
    /// 这一段的待办理由原来是"快照类型名不同、字段需对齐"，而实测下来需要的东西全在：
    /// `ColumnProjection.SnapshotAsync`／`PricingRequestAsync`／`MasterAsync`、`LinearModelSnapshot`（Rows/Variables/Objective/ObjectiveConstant）、
    /// `ColumnPricingRequest.ReducedCost` 一个不缺。所以那段理由和 `PreciseAssignment`、和"环境所限"是同一个错法：把"我没找到"写成了"没有"。
    ///
    /// 先记一次改错： 第一版把 `rows` 写成 `registration.Model.Constraints.Count`、把 `convexities` 写成 `RefinedResources.Count`，
    /// 理由是"C# 侧没有同名集合"——而 `Rows` 与 `Convexities` 都在登记类上，我只是只看了半边。 实测数字会立刻露馅：行数 132 而凸组合 0，
    /// 一个"登记了四列却零凸组合"的主问题说不通。 现在两边逐字段对齐。
    ///
    /// The reason this section was pending used to be "the snapshot type name differs and the fields need aligning", while measurement shows everything
    /// needed is present. So that reason has the same defect as `PreciseAssignment` and "limited by the environment": "I did not find it" written as
    /// "it does not exist". A correction is recorded too: the first version reported rows from `Model.Constraints` and convexities from
    /// `RefinedResources`, justified by "C# has no same-named collections" — both `Rows` and `Convexities` are on the registration, and only my partial
    /// reading said otherwise. The numbers exposed it at once: 132 rows against zero convexities is not a master problem that just registered four
    /// columns. Both sides now align field by field.
    /// </remarks>
    /// <param name="text">输出 / The output.</param>
    private static void DumpProjection(StringBuilder text) {
        foreach (int size in new[] { 256, 1024 }) {
            var source = new LinearMetaModel<Flt64>(name: "dump_projection", objectCategory: ObjectCategory.Minimum);

            var variables = new List<BinVar>(size);
            for (int index = 0; index < size; ++index) {
                variables.Add(new BinVar($"v{index}"));
            }
            MustSucceed(source.Add(variables), "投影变量必须可加入 / the projection variables must be addable");

            for (int row = 0; row < size / 2; ++row) {
                var monomials = new List<LinearMonomial<Flt64>>(3);
                for (int offset = 0; offset <= 2; ++offset) {
                    monomials.Add(new LinearMonomial<Flt64>(Flt64.One, variables[(row + offset * 4) % size]));
                }
                var lhs = new LinearPolynomial<Flt64>(monomials, Flt64.Zero);
                var rhs = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(2));
                MustSucceed(
                    source.AddConstraint(
                        new LinearInequality<Flt64>(lhs, rhs, Comparison.LE, Name: $"r{row}"),
                        group: null,
                        name: $"r{row}"),
                    "投影行必须可加入 / the projection rows must be addable");
            }

            MustSucceed(
                source.AddObject(
                    ObjectCategory.Minimum,
                    new LinearPolynomial<Flt64>(
                        variables.Select(variable => new LinearMonomial<Flt64>(Flt64.One, variable)).ToList(),
                        Flt64.Zero),
                    "objective",
                    displayName: null),
                "投影目标必须可加入 / the projection objective must be addable");

            string[] blockIds = { "b0", "b1", "b2", "b3" };
            var decisions = variables
                .Select((variable, index) => new ColumnDecision(
                    Id: $"d{index}",
                    BlockId: blockIds[index % blockIds.Length],
                    Variable: variable))
                .ToList();
            var projection = new ColumnProjection<Flt64>(source: source, decisions: decisions, blockIds: blockIds);

            LinearModelSnapshot<Flt64> snapshot = Await(projection.SnapshotAsync(), "投影快照 / the projection snapshot");
            Line(text, $"PRJ|{size}|snapshot|rows={snapshot.Rows.Count}|vars={snapshot.Variables.Count}");
            // **不打印行 `Id`：** **C# 侧它是 `model-local-row:<进程级计数器>:<序号>`，同进程第二次运行必然不同**
            // **（判据第一次跑就抓到这一点）。** **改用 `SourceName`（`r0`），它既是稳定身份，也比基线那个 id 更可跨语言比对。**
            //
            // **The row `Id` is not printed: on the C# side it is `model-local-row:<process counter>:<index>`, which necessarily differs on a second run
            // in the same process (the criterion caught this on its first run).** **`SourceName` (`r0`) is used instead: a stable identity, and more
            // comparable across languages than the baseline's id.**
            LinearModelRow<Flt64> firstRow = snapshot.Rows
                .OrderBy(row => row.SourceName, StringComparer.Ordinal)
                .First();
            Line(text, $"PRJ|{size}|snapshotRow0|source={firstRow.SourceName}|rhs={Num(firstRow.Rhs)}|coeffs="
                + string.Join(",", firstRow.Coefficients
                    .OrderBy(pair => pair.Key.Name, StringComparer.Ordinal)
                    .Take(8)
                    .Select(pair => pair.Key.Name + ":" + Num(pair.Value))));
            Line(text, $"PRJ|{size}|snapshotObjective|constant={Num(snapshot.ObjectiveConstant)}|terms="
                + string.Join(",", snapshot.Objective
                    .OrderBy(pair => pair.Key.Name, StringComparer.Ordinal)
                    .Take(8)
                    .Select(pair => pair.Key.Name + ":" + Num(pair.Value))));

            ColumnPricingRequest<Flt64> pricing = Await(
                projection.PricingRequestAsync(blockId: "b0", phaseOne: false, seed: true),
                "投影定价请求 / the projection pricing request");
            Line(text, $"PRJ|{size}|pricing|block={pricing.BlockId}|coeffCount={pricing.Coefficients.Count}"
                + $"|convexity={Num(pricing.ConvexityPrice)}|head="
                + string.Join(",", pricing.Coefficients
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Take(4)
                    .Select(pair => Num(pair.Value))));
            Line(text, $"PRJ|{size}|pricingRowOrder|keys="
                + string.Join(",", pricing.RowPrices.Keys.Take(6))
                + $"|dualHead={string.Join(",", pricing.RowPrices.Values.Take(4).Select(value => Num(value)))}");

            var ones = pricing.Coefficients.Keys
                .ToDictionary(key => key, _ => Flt64.One, StringComparer.Ordinal);
            Flt64 reduced = Unwrap(pricing.ReducedCost(ones), "投影简约成本 / the projection reduced cost");
            Line(text, $"PRJ|{size}|pricingReducedCost|value={Num(reduced)}");

            var columns = blockIds.Select(block => new ProjectedColumn<Flt64>(
                    id: block,
                    blockId: block,
                    values: decisions
                        .Where(decision => decision.BlockId == block)
                        .ToDictionary(decision => decision.Id, _ => Flt64.Zero, StringComparer.Ordinal)))
                .ToList();
            using (ColumnMasterRegistration<Flt64> registration = Await(
                       projection.MasterAsync(columns: columns, phaseOne: false, integer: false),
                       "投影主问题登记 / the projection master registration")) {
                Line(text, $"PRJ|{size}|master|rows={registration.Rows.Count}"
                    + $"|convexities={registration.Convexities.Count}"
                    + $"|columnVars={registration.ColumnVariables.Count}");
            }

            ColumnPricingRequest<Flt64> warm = Await(
                projection.PricingRequestAsync(blockId: "b0", phaseOne: false, seed: true),
                "投影预热定价 / the warm projection pricing request");
            Line(text, $"PRJ|{size}|warmSameAsCold|coeffs="
                + Flag(Same(pricing.Coefficients.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + Num(p.Value)),
                    warm.Coefficients.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + Num(p.Value))))
                + $"|convexity={Flag(warm.ConvexityPrice.Equals(pricing.ConvexityPrice))}");
        }
    }

    // ==================== Generation (B1309) ====================

    /// <summary>
    /// 生成段：三个场景、三个生成器，方案数、终止原因与规范键顺序 /
    /// The generation section: three scenarios and three generators, with plan counts, stop reasons and canonical-key ordering.
    /// </summary>
    /// <remarks>
    /// 待办理由原来是"`canonicalKey()` 是服务层的东西，基准层拿不到"。 实测：`CuttingPlanCanonicalKey.From(plan)` 就在
    /// `Domain/CuttingPlanGeneration/model/`，`CuttingPlan&lt;V&gt;` 直接实现 `ICuttingPlanForCanonicalKey`——
    /// 拿不到的是服务层那层包装，不是键本身。 第三次同一种错法。
    ///
    /// 两处有意偏离基线，都写在这里：
    /// ① 基线的 `hashCode()` 是 Java/Kotlin 的字符串哈希，跨语言不可比，这里改用规范键文本的排序后原样拼接（与 `NET`／`TOK`／`PRM` 同一个选择）；
    /// ② 基线的 `orderDigest` 对顺序敏感，这里给的是"按到达顺序拼接后的摘要"，同样只用于同语言两次运行比对。
    ///
    /// The pending reason used to be "`canonicalKey()` is a service-layer thing the benchmark cannot reach". Measured: `CuttingPlanCanonicalKey.From(plan)`
    /// lives in `Domain/CuttingPlanGeneration/model/` and `CuttingPlan&lt;V&gt;` implements `ICuttingPlanForCanonicalKey` directly — what was unreachable was
    /// the service-layer wrapper, not the key. The third time for this same defect. Two deliberate divergences from the baseline are stated here:
    /// the baseline's `hashCode()` is a JVM string hash that cannot be compared across languages, so this uses the sorted raw join of canonical-key text
    /// (the same choice as `NET`/`TOK`/`PRM`), and the order-sensitive digest is likewise a same-language comparison only.
    /// </remarks>
    /// <param name="text">输出 / The output.</param>
    /// <param name="maxPlans">方案预算 / The plan budget.</param>
    /// <param name="prefix">行前缀（`GEN` 或 `TRUNC`）/ The line prefix.</param>
    private static void DumpGeneration(StringBuilder text, long maxPlans, string prefix) {
        foreach (string scenario in new[] { "equivalent", "distinct", "custom" }) {
            GenerationInput<Flt64> input = GenerationInputOf(scenario);
            foreach ((string name, ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> generator)
                     in GeneratorsOf(scenario, maxPlans)) {
                CuttingPlanGenerationReport<CuttingPlan<Flt64>> report = generator.GenerateWithReport(input);
                var keys = report.Plans.Select(CanonicalKeyText).ToList();

                Line(text, $"{prefix}|{scenario}|{name}|plans={keys.Count}|stop={report.Statistics.StopReason}");
                Line(text, $"{prefix}|{scenario}|{name}|orderHead="
                    + string.Join("|", keys.Take(3).Select(OrdinalDigest)));
                Line(text, $"{prefix}|{scenario}|{name}|sortedDigest=" + OrdinalDigest(string.Join(";", keys.OrderBy(k => k, StringComparer.Ordinal))));

                string stats = CuttingPlanGenerationBenchmarkSnapshot.From(name, report.Statistics).ToStableLine();
                Line(text, $"{prefix}|{scenario}|{name}|stats={stats}");
            }
        }
    }

    /// <summary>固定生成夹具：8 个商品、12 种物料（与基线逐项相同）/ The fixed generation fixture: eight products, twelve materials, identical to the baseline.</summary>
    /// <param name="scenario">场景 / The scenario.</param>
    /// <returns>生成输入 / The generation input.</returns>
    private static GenerationInput<Flt64> GenerationInputOf(string scenario) {
        var products = new List<Product<Flt64>>(8);
        var demands = new List<ProductDemand<Flt64>>(8);
        for (int index = 0; index < 8; ++index) {
            var product = new Product<Flt64>(
                "p" + index,
                "p" + index,
                [new Quantity<Flt64>(new Flt64(0.17 + index * 0.03), SIBaseUnits.Meter)]);
            products.Add(product);
            demands.Add(ProductDemand<Flt64>.Roll(product, new Quantity<Flt64>(new Flt64(8), RollCountUnit.Instance)));
        }

        var materials = new List<Material<Flt64>>(12);
        for (int index = 0; index < 12; ++index) {
            double upperBound = scenario == "distinct" ? 1.2 + index * 0.01 : 1.2;
            materials.Add(new Material<Flt64>(
                "m" + index,
                "m" + index,
                new WidthRange<Flt64>(
                    Width: new QuantityRange<Flt64>(
                        LowerBound: new Quantity<Flt64>(Flt64.Zero, SIBaseUnits.Meter),
                        UpperBound: new Quantity<Flt64>(new Flt64(upperBound), SIBaseUnits.Meter)),
                    Step: new Quantity<Flt64>(new Flt64(0.01), SIBaseUnits.Meter))));
        }

        return new GenerationInput<Flt64> {
            Materials = materials,
            Demands = demands,
            Costars = [],
        };
    }

    /// <summary>三个生成器，单 worker（截断路径要可确定复现）/ The three generators, single worker so truncation stays reproducible.</summary>
    /// <param name="scenario">场景 / The scenario.</param>
    /// <param name="maxPlans">方案预算 / The plan budget.</param>
    /// <returns>名称与生成器 / Names and generators.</returns>
    private static IReadOnlyList<(string Name, ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> Generator)>
        GeneratorsOf(string scenario, long maxPlans) {
        var constraints = new List<ICuttingPlanConstraint<Flt64>> {
            new MaxKnifeCountConstraint<Flt64>(new Fuookami.Ospf.Math.Algebra.Number.UInt64(5UL)),
        };
        if (scenario == "custom") {
            constraints.Add(new AlwaysSatisfiedConstraint());
        }

        return new[] {
            ("dfs", (ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>>)
                new DFSGenerator<Flt64>(constraints, maxPlans: maxPlans, parallelism: 1)),
            ("nsum", new NSumGenerator<Flt64>(constraints, maxPlans: maxPlans, parallelism: 1)),
            ("fullsum", new FullSumGenerator<Flt64>(constraints, maxPlans: maxPlans, parallelism: 1)),
        };
    }

    /// <summary>恒真的自定义策略（`custom` 场景用它，与基线一致）/ The always-true custom policy used by the `custom` scenario.</summary>
    private sealed class AlwaysSatisfiedConstraint : ICuttingPlanConstraint<Flt64> {
        /// <summary>恒真 / Always true.</summary>
        /// <param name="context">约束上下文 / The constraint context.</param>
        /// <returns>恒为 true / Always true.</returns>
        public bool IsSatisfied(CuttingPlanConstraintContext<Flt64> context) => true;
    }

    /// <summary>
    /// 规范键的稳定文本 / The stable text of a canonical key.
    /// </summary>
    /// <remarks>
    /// 不能用 `record.ToString()`： `CuttingPlanCanonicalKey` 的两个集合成员是 `IReadOnlyList`，`record` 生成的
    /// `ToString` 打印的是集合的类型名而不是内容——第一版就是这样，于是前三个方案的摘要全都是同一串十六进制，
    /// 一个"看起来有摘要、实际没有信息"的结果。 这里逐项展开，顺序由 `CuttingPlanCanonicalKey` 自己保证（内部用了有序字典）。
    ///
    /// `record.ToString()` cannot be used: the key's two collection members are `IReadOnlyList`, and the generated `ToString` prints the
    /// collection's type name rather than its contents — the first version did exactly that, so the first three plans hashed to the same
    /// hexadecimal string: a digest that looks informative and carries none. The text is expanded item by item here, and the ordering is the key's own
    /// job (it uses sorted dictionaries internally).
    /// </remarks>
    /// <param name="plan">切割方案 / The cutting plan.</param>
    /// <returns>文本 / The text.</returns>
    private static string CanonicalKeyText(CuttingPlan<Flt64> plan) {
        var key = CuttingPlanCanonicalKey.From(plan);
        return key.MaterialId + "|" + (key.MachineId ?? "-") + "|" + (key.CapacityConsumption ?? "-")
            + "|slices=" + string.Join(",", key.Slices.Select(slice =>
                slice.ProductionType + ":" + slice.ProductionId + ":" + slice.Width + ":" + slice.Amount))
            + "|demands=" + string.Join(",", key.DemandContributions.Select(contribution =>
                contribution.ProductId + ":" + contribution.Unit + ":" + contribution.QuantityValue));
    }

    /// <summary>序数摘要：排序后拼接的 FNV-1a 十六进制，只用于同语言两次运行比对 / Ordinal digest: FNV-1a over the text, for same-language comparisons only.</summary>
    /// <param name="value">文本 / The text.</param>
    /// <returns>十六进制摘要 / The hexadecimal digest.</returns>
    private static string OrdinalDigest(string value) {
        ulong hash = 14695981039346656037UL;
        foreach (char character in value) {
            hash ^= character;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x");
    }

    /// <summary>等待异步结果，失败即抛 / Await the result, throwing on failure.</summary>
    /// <typeparam name="TValue">结果类型 / The result type.</typeparam>
    /// <param name="task">异步结果 / The pending result.</param>
    /// <param name="consumer">出错时的说明 / What was being consumed.</param>
    /// <returns>值 / The value.</returns>
    private static TValue Await<TValue>(Task<Result<TValue, ErrorCode, Error<ErrorCode>>> task, string consumer) =>
        Unwrap(task.GetAwaiter().GetResult(), consumer);

    /// <summary>同步结果取值，失败即抛 / Take the value of a synchronous result, throwing on failure.</summary>
    /// <typeparam name="TValue">结果类型 / The result type.</typeparam>
    /// <param name="result">结果 / The result.</param>
    /// <param name="consumer">出错时的说明 / What was being consumed.</param>
    /// <returns>值 / The value.</returns>
    private static TValue Unwrap<TValue>(Result<TValue, ErrorCode, Error<ErrorCode>> result, string consumer) =>
        result is Ok<TValue, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException(consumer + ": " + result);

    /// <summary>断言成功，否则抛 / Require success, otherwise throw.</summary>
    /// <param name="result">结果 / The result.</param>
    /// <param name="consumer">出错时的说明 / What was being built.</param>
    private static void MustSucceed(Try result, string consumer) {
        if (result is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            throw new InvalidOperationException(consumer + ": " + result);
        }
    }

    /// <summary>数值渲染成与基线一致的文本（整数值不带小数点）/ Render a number the way the baseline does (integers without a decimal point).</summary>
    /// <param name="value">数值 / The number.</param>
    /// <returns>文本 / The text.</returns>
    private static string Num(object? value) => value switch {
        null => "",
        Flt64 number => number.ToFlt64().ToDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };
}