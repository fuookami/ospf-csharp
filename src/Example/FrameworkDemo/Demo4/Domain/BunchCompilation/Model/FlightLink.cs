#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;

/// <summary>
/// 航班链接的松弛层 / The flight-link slack layer.
/// </summary>
/// <remarks>
/// ：每条链接一个表达式 `link_k`，再加一个松弛
/// `link_slack_k = exampleThresholdSlack(link_k + 0.5·y[前驱] + 0.5·y[后继], 阈值 1, 只有负侧)`。
///
/// 直觉上"链接"该是一条约束，实际是**松弛**：链接是否成立交给目标去压，而不是硬性禁止。两条系数都是 **0.5**——
/// 一条链接由两个任务共同承担，把它们各算一半，正是基线避免"同一对任务被重复计数"的写法。
///
/// **`AddColumns` 找的是"同时含前后两个任务的束"**，系数是 1——分割成本不进这里（它在目标里），
/// 这里数的只是"这一轮有几个束把这两个任务接在一起"。与基线一样**先清再加**，否则每轮都会把上一轮的项留着。
///
/// The code's: one expression `link_k` per link plus a slack
/// `link_slack_k = exampleThresholdSlack(link_k + 0.5·y[prev] + 0.5·y[succ], threshold 1, negative side only)`. Intuitively a link is a constraint, but it is actually a **slack**:
/// whether a link holds is left to the objective rather than forbidden outright. Both coefficients are **0.5** — a link is shared by two tasks, counting each as half is how the
/// baseline avoids **counting the same pair twice**.
///
/// **`AddColumns` looks for the bunches holding *both* tasks**, with coefficient 1 — the split cost does not enter here (it belongs in the objective); what is counted is "how many bunches
/// of this round stitch these two tasks together". Like the code it **clears before adding**, otherwise every round keeps the previous round's terms.
/// </remarks>
public sealed class FlightLink {
    private readonly IReadOnlyList<Link> _links;

    private readonly BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> _compilation;

    private readonly Dictionary<int, LinearExpressionSymbol> _link = new();

    private readonly Dictionary<int, ILinearIntermediateSymbol<Flt64>> _slack = new();

    private readonly Dictionary<int, LinearExpressionSymbol> _source = new();

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="links">链接列表 / The links.</param>
    /// <param name="compilation">束编译 / The bunch compilation.</param>
    public FlightLink(
        IReadOnlyList<Link> links,
        BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> compilation) {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(compilation);
        _links = links;
        _compilation = compilation;
    }

    /// <summary>链接列表 / The links, in index order.</summary>
    /// <remarks>
    /// 限制层（`FlightLinkLimit`）要按**链接本身**建影子价格键，所以需要拿到原始列表；只靠 `Links` 那张按序号索引的表拿不到 `Link`。
    ///
    /// The limit layer (`FlightLinkLimit`) needs the original list to key shadow prices by **the link itself**; the index-keyed `Links` table alone cannot yield a `Link`.
    /// </remarks>
    public IReadOnlyList<Link> Items => _links;

    /// <summary>链接表达式 / The link expressions, keyed by link index.</summary>
    public IReadOnlyDictionary<int, LinearExpressionSymbol> Links => _link;

    /// <summary>松弛符号 / The slack symbols, keyed by link index.</summary>
    public IReadOnlyDictionary<int, ILinearIntermediateSymbol<Flt64>> Slacks => _slack;

    /// <summary>
    /// 松弛的源多项式 / The slack's source polynomial.
    /// </summary>
    /// <remarks>
    /// **源多项式要能读**：`link_k + 0.5·y[前驱] + 0.5·y[后继]` 里的两个 0.5 是这条规则的全部内容，
    /// 而松弛符号本身是适配器、看不到系数。判据如果不读源，就只剩"符号建出来了"这一条恒真断言。
    ///
    /// **The source must be readable**: the two 0.5 factors in `link_k + 0.5·y[prev] + 0.5·y[succ]` *are* this rule, and the slack symbol itself is an adapter whose coefficients are
    /// invisible. Without reading the source, the only criterion left is the always-true "a symbol was built".
    /// </remarks>
    public IReadOnlyDictionary<int, LinearExpressionSymbol> Sources => _source;

    /// <summary>
    /// 注册到模型 / Register into the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    /// <remarks>
    /// **没有链接时什么都不建**（的提前返回）。构建一次之后不再重建：重建会让同一个链接在模型里
    /// 出现两个符号，求解时它们各自取值、彼此无关。
    ///
    /// **No links build nothing** (an early return matching the code). The tables are built once and never rebuilt: rebuilding would put two symbols for the same link into the model,
    /// each taking its own value at solve time.
    /// </remarks>
    public Try Register(IAbstractLinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (false) { }
        if (_link.Count == 0) {
            for (int index = 0; index < _links.Count; ++index) {
                _link[index] = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                    name: "link_" + index);
            }
            Try addedLinks = model.Add(_link.Values);
            if (!addedLinks.IsOk) {
                return addedLinks;
            }
        }

        if (_slack.Count == 0) {
            for (int index = 0; index < _links.Count; ++index) {
                var monomials = new List<LinearMonomial<Flt64>> {
                    new(Flt64.One, _link[index]),
                };
                if (CancellationOf(_links[index].PrevTask) is { } prev) {
                    monomials.Add(new LinearMonomial<Flt64>(new Flt64(0.5), prev));
                }
                if (CancellationOf(_links[index].SuccTask) is { } succ) {
                    monomials.Add(new LinearMonomial<Flt64>(new Flt64(0.5), succ));
                }

                var poly = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(monomials, Flt64.Zero),
                    name: "link_slack_source_" + index);
                _source[index] = poly;
                _slack[index] = ProduceSlackHelper.Build(
                    poly,
                    threshold: Flt64.One,
                    withNegative: true,
                    withPositive: false,
                    constraint: false,
                    name: "link_slack_" + index);
            }
            Try addedSlacks = model.Add(_slack.Values);
            if (!addedSlacks.IsOk) {
                return addedSlacks;
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 把这一轮的列变量换进链接表达式 / Swap this round's column variables into the link expressions.
    /// </summary>
    /// <param name="iteration">迭代轮次 / The iteration round.</param>
    /// <param name="bunches">本轮任务束 / The bunches of this round.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    /// <remarks>
    /// 命中的判据是**同一个束里同时含有前后两个任务**：只含一个说明这两个任务没被接在一起，链接不成立。
    /// 系数是 **1**（一个束算一次），分割成本在目标里乘，不在这里。
    ///
    /// The match requires **one bunch holding both tasks**: holding only one means they were not stitched together and the link does not hold. The coefficient is **1** (one bunch counts
    /// once); the split cost multiplies in the objective, not here.
    /// </remarks>
    public Try AddColumns(ulong iteration, IReadOnlyList<FlightTaskBunch> bunches) {
        ArgumentNullException.ThrowIfNull(bunches);

        for (int index = 0; index < _links.Count; ++index) {
            if (!_link.TryGetValue(index, out LinearExpressionSymbol? expression)) {
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.ApplicationError, "链接还没注册 / the links have not been registered"));
            }
            var monomials = new List<LinearMonomial<Flt64>>();
            foreach (FlightTaskBunch bunch in bunches) {
                if (bunch.Get(_links[index].PrevTask) is null || bunch.Get(_links[index].SuccTask) is null) {
                    continue;
                }
                if (_compilation.ColumnItem(iteration, bunch) is { } column) {
                    monomials.Add(new LinearMonomial<Flt64>(Flt64.One, column));
                }
            }
            if (monomials.Count == 0) {
                continue;
            }
            // 先清再加，等价于`flush` / Clear, then add — the equivalent of its `flush`.
            MutableLinearPolynomial<Flt64> mutable = expression.AsMutable();
            mutable.Clear();
            mutable.Add(new LinearPolynomial<Flt64>(monomials.ToArray(), Flt64.Zero));
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <remarks>
    /// **取消变量是从编译对象的表里取的**：取不到就不加那一项（而不是加一个系数 0.5 的空符号）。`y[task]` 就在这里。
    ///
    /// 表里存的是**变量项**（`CombinationVariableItem`），不是中间符号——按中间符号去转，会一条都转不中，
    /// 于是两个 0.5 项**静默消失**，松弛退化成"只有链接本身"。这类"转不中就跳过"的写法最容易被当成正确的容错。
    ///
    /// **The cancellation variable comes from the compilation's table**: when it is absent that term is simply not added (rather than adding a 0.5 monomial over an empty symbol).
    /// That is its `y[task]`. The table holds **variable items** (`CombinationVariableItem`), not intermediate symbols — casting to the latter matches nothing, so both 0.5
    /// terms **silently vanish** and the slack degenerates to "the link alone". This "skip when the cast fails" shape is the easiest thing to mistake for correct fault tolerance.
    /// </remarks>
    private Fuookami.Ospf.Math.Symbol.ISymbol? CancellationOf(FlightTask task) =>
        _compilation.Cancellation.TryGetValue(task, out object? symbol) && symbol is Fuookami.Ospf.Math.Symbol.ISymbol typed
            ? typed
            : null;
}
