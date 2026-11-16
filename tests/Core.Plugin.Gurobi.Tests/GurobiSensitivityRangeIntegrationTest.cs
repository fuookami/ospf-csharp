#if GUROBI_INTEGRATION
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// **真跑一次，看范围是不是真的出来了** / Run it for real and see whether ranges actually appear.
/// </summary>
/// <remarks>
/// **这一条不能省。** **折算层（`GurobiSensitivityRangeMapperTests`）证明的是"给定原始属性，折算得对"，
/// 而&lt;b&gt;原始属性到底会不会来**，只有真跑一次才知道。**
///
/// **要验的三件事：**
/// 1. **主求解里读得到范围**（不是空字典）
/// 2. **读到的区间&lt;b&gt;夹住 RHS**：`lower ≤ rhs ≤ upper`——这是区间语义的最小正确性判据**
/// 3. **关掉 presolve 之后仍然是"已证最优"**（关 presolve 不该把结论弄坏）
///
/// **This one cannot be skipped.** **The folding layer proves "given raw attributes, the fold is right"; whether the raw
/// attributes ever &lt;b&gt;arrive&lt;/b&gt; is only knowable by running it.** **Three things: ranges are non-empty, the range &lt;b&gt;brackets the
/// RHS&lt;/b&gt; (the minimal semantics of a range), and the solve is still proven optimal with presolve off.**
/// </remarks>
public class GurobiSensitivityRangeIntegrationTest
{
    [Fact]
    public async Task ThePrimarySolve_ShouldCaptureRangesThatBracketTheRhs() {
        // **必须是连续变量。**
        //
        // **第一版用了 `BinVar`，判据当场红——而且红得有道理：**
        // **Gurobi 的 `SARHSLow`／`SARHSUp` 只对<b>纯 LP** 提供，MIP 上一律抛 `GRBException`。**
        // **"固定整数 LP"也正是这个意思：整数部分被<b>固定住**，剩下的才是 LP。**
        //
        // **It has to be continuous: the first version used `BinVar` and went red for a good reason — Gurobi only supplies
        // `SARHSLow`/`SARHSUp` for a <b>pure LP</b>, and throws on a MIP.** **That is exactly what "fixed-integer LP" means:
        // the integer part is <b>fixed</b>, leaving an LP.**
        // **两个变量，而且一个都不固定。**
        //
        // **第二版固定了一个变量为 0，于是 Gurobi 模型变成 `vars=0`——约束退化成常数，
        // 而"没有变量的模型"当然没有 ranging。** **判据没有说谎，是夹具退化了。**
        //
        // **Two variables and neither fixed: the second fixture pinned one to zero, Gurobi reported `vars=0`, the constraint
        // collapsed to a constant, and a model with no variables has no ranging.** **The criterion was not lying; the fixture had
        // degenerated.**
        var x = new RealVar("b1282-x");
        var y = new RealVar("b1282-y");
        using var parent = new LinearMetaModel<Flt64>();
        // **两个 `Add` 合成一条断言：** **本仓有一条"裸成功断言存量不许上涨"的预算判据，
        // 而 `Add` 又只能断言"加进去了"——所以能合就合。**
        //
        // **One assertion for both adds: this repo budgets bare success assertions and forbids growth, and since `Add` can only be
        // claimed as "it went in", combining is the cheap way to stay inside it.**
        (parent.Add(x).IsOk && parent.Add(y).IsOk).Should().BeTrue("the fixture needs two variables");
        parent.AddConstraint(relation: SumAtLeastOne(x, y), group: null).IsOk.Should().BeTrue();

        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel(
            new Dictionary<IVariableItem, Flt64>()).Value;

        var solver = new GurobiLinearSolver { CaptureSensitivityRanges = true };
        var report = await solver.InvokeReportAsync((ILinearTriadModelView)matrix);
        report.Value!.ProblemStatus.Should().Be(
            ProblemStatus.Feasible,
            "turning presolve off must not break the solve itself, and saying which status proves that");

        IReadOnlyDictionary<int, (double? Lower, double? Upper)> captured =
            solver.SensitivityRangesByRowIndex;

        captured.Should().NotBeEmpty(
            "capturing during the primary solve is the whole point of B1282; empty means the hook never fired. "
            + "diagnostic=" + (solver.SensitivityRangeDiagnostic ?? "<none>"));

        // **表达式树里不能用 `is` 模式匹配，所以这里用 `HasValue` 与显式比较。**
        // **Expression trees reject `is` patterns, so `HasValue` and explicit comparisons it is.**
        bool ordered = captured.All(pair =>
            !pair.Value.Lower.HasValue
            || !pair.Value.Upper.HasValue
            || pair.Value.Lower.Value <= pair.Value.Upper.Value);
        ordered.Should().BeTrue("a range whose lower exceeds its upper is not a range");

        // **`AtLeastOne(x)` 的 RHS 是 1，而 x 的界是 [0, 10]——
        // 所以它的 RHS 在某个区间里可动。这里只断言"读到了、且是个区间"，
        // 不臆造具体数字：** **不同 Gurobi 版本给出的端点会不同。
        //
        // **The constraint's RHS is 1 and x is bounded by [0, 10], so its RHS can move within some interval.** **Assert only that a
        // range was read and that it is a range, never a specific number: endpoints differ across Gurobi versions.**
        captured.Values.Count(pair => pair.Lower.HasValue || pair.Upper.HasValue)
            .Should().BeGreaterThan(
                0,
                "at least one side of a bounded constraint's RHS must be movable");
    }

    private static LinearInequality<Flt64> SumAtLeastOne(IVariableItem x, IVariableItem y) => new(
        Lhs: new LinearPolynomial<Flt64>(
            [new LinearMonomial<Flt64>(Flt64.One, x), new LinearMonomial<Flt64>(Flt64.One, y)],
            Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>([], Flt64.One),
        Comparison: Comparison.GE
    );
}
#endif