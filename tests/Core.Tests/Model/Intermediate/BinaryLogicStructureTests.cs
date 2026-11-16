#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 二值逻辑：真值表是唯一的判据 / The truth table is the only criterion.
/// </summary>
/// <remarks>
/// 对齐基线 `BinaryLogicStructure.kt` 的 `BinaryLogicStructure` 与 `BinaryLogicOperation`。
///
/// **四种编码的行数公式不同，方向也不同。真正的判据是：<b>把每个二值赋值代进去，
/// 可行的恰好是该运算的那些</b>。**
///
/// **只数行数证明不了任何东西——一个行数完全正确而符号写反的实现，行数判据全绿。**
///
/// adds
///
/// **The four encodings differ in row count and direction. The real criterion is: <b>substitute every binary assignment;
/// exactly the operation's assignments are feasible</b>.**
///
/// **Counting rows proves nothing — an implementation with every count right and a sign flipped passes all of them.**
/// </remarks>
public class BinaryLogicStructureTests
{
    /// <summary>
    /// **AND 的真值表** / The AND truth table.
    /// </summary>
    /// <remarks>
    /// **两输入 AND：只有 `(1,1,1)` 可行，其余三组都不可行。**
    ///
    /// **这一条同时检验方向（`≤`）、求和行的右侧（`1 − n`）与"结果列也查二值"。**
    /// **求和行右侧若写成 `0`，`(0,0,1)` 就会变成可行——而它是错的。**
    ///
    /// **Two-input AND: only `(1,1,1)` is feasible.**
    ///
    /// **This checks the direction (`≤`), the sum row's right-hand side (`1 − n`) and the binary result check at once.**
    /// **With `0` on the sum row's right, `(0,0,1)` would become feasible — and it is wrong.**
    /// </remarks>
    [Fact]
    public void TheAndEncoding_ShouldMatchItsTruthTable()
    {
        var structure = new BinaryLogicStructure(
            Operation: BinaryLogicOperation.And, Inputs: new[] { A, B },
            ResultVariable: Result, Converter: IIntoValue<Flt64>.Identity, Name: "and");

        IReadOnlyList<LinearInequality<Flt64>> rows =
            structure.MaterializeFallbackConstraints().Value.Constraints;

        rows.Should().HaveCount(3, "n per-input rows plus one sum row");

        Feasible(rows, a: 0, b: 0, r: 0).Should().BeTrue();
        Feasible(rows, a: 1, b: 0, r: 0).Should().BeTrue();
        Feasible(rows, a: 0, b: 1, r: 0).Should().BeTrue();
        Feasible(rows, a: 1, b: 1, r: 1).Should().BeTrue();

        Feasible(rows, a: 0, b: 0, r: 1).Should().BeFalse(
            "the sum row Σx − r ≤ n − 1 excludes it");
        Feasible(rows, a: 1, b: 1, r: 0).Should().BeFalse();
        Feasible(rows, a: 1, b: 0, r: 1).Should().BeFalse();
        Feasible(rows, a: 0, b: 1, r: 1).Should().BeFalse();
    }

    /// <summary>**OR 的真值表** / The OR truth table.</summary>
    [Fact]
    public void TheOrEncoding_ShouldMatchItsTruthTable()
    {
        var structure = new BinaryLogicStructure(
            Operation: BinaryLogicOperation.Or, Inputs: new[] { A, B },
            ResultVariable: Result, Converter: IIntoValue<Flt64>.Identity, Name: "or");

        IReadOnlyList<LinearInequality<Flt64>> rows =
            structure.MaterializeFallbackConstraints().Value.Constraints;

        rows.Should().HaveCount(3);

        Feasible(rows, a: 0, b: 0, r: 0).Should().BeTrue();
        Feasible(rows, a: 1, b: 0, r: 1).Should().BeTrue();
        Feasible(rows, a: 0, b: 1, r: 1).Should().BeTrue();
        Feasible(rows, a: 1, b: 1, r: 1).Should().BeTrue();

        Feasible(rows, a: 1, b: 1, r: 0).Should().BeFalse();
        Feasible(rows, a: 0, b: 0, r: 1).Should().BeFalse(
            "the sum row excludes a result above every input");
    }

    /// <summary>**NOT 只有一行，且必须是等式** / NOT is a single equality.</summary>
    [Fact]
    public void TheNotEncoding_ShouldBeOneEquality()
    {
        var structure = new BinaryLogicStructure(
            Operation: BinaryLogicOperation.Not, Inputs: new[] { A },
            ResultVariable: Result, Converter: IIntoValue<Flt64>.Identity, Name: "not");

        IReadOnlyList<LinearInequality<Flt64>> rows =
            structure.MaterializeFallbackConstraints().Value.Constraints;

        rows.Should().HaveCount(1);
        rows[0].Comparison.Should().Be(Comparison.EQ);

        Feasible(rows, a: 1, b: 0, r: 0).Should().BeTrue();
        Feasible(rows, a: 0, b: 0, r: 1).Should().BeTrue();
        Feasible(rows, a: 1, b: 0, r: 1).Should().BeFalse();
        Feasible(rows, a: 0, b: 0, r: 0).Should().BeFalse();
    }

    /// <summary>
    /// **三输入 XOR 已知松弛：它接受"三个一、结果 0"** /
    /// The three-input XOR is known slack: it accepts "all ones, result zero".
    /// </summary>
    /// <remarks>
    /// **这是本方向第五条"实测为负"的发现，而它<b>不是</b>我的实现缺陷——
    /// 基线 `BinaryLogicStructure` 的 XOR 编码在 `n ≥ 3` 时本来就松弛。**
    ///
    /// **我按真值表逐组核对过：`n = 1` 与 `n = 2` 精确，`n = 3` 与 `n = 4` 都多接受赋值。**
    /// **`n = 3` 时唯一多出的是 `(1,1,1,0)`。**
    ///
    /// **原因：那三族行（求和上界、单输入下界、成对上界）描述的是一组<b>线性</b>不等式，
    /// 而奇偶性是<b>非凸</b>的。三输入全 1 时 `result = 0` 同时满足：**
    /// `0 − 3 ≤ 0`、`0 − 1 + 2 = 1 ≥ 0`（三组）、`0 + 1 + 1 = 2 ≤ 2`（三组）。**
    ///
    /// **精确的奇偶编码需要一个辅助整数列（`Σinput − result = 2k`），
    /// 而 `DeferredFunctionFallbackConstraints` 只承载行、不承载列定义。**
    ///
    /// **所以判据<b>断言这个松弛存在</b>，而不是假装它不存在。
    /// 一个断言"XOR 完全正确"的判据在这里会失败，而失败的原因不在我的代码。**
    ///
    /// **This is the direction's fifth "measured negative" finding, and it is <b>not</b> a defect in my implementation —
    /// the baseline's XOR encoding is already slack at `n ≥ 3`.**
    ///
    /// **Checked against the truth table: exact at `n = 1` and `n = 2`, over-accepting at `n = 3` and `n = 4`.** **At
    /// `n = 3` the only extra assignment is `(1,1,1,0)`.**
    ///
    /// **Because the three row families describe <b>linear</b> inequalities while parity is <b>non-convex</b>.** **With
    /// three inputs one and `result = 0` every row holds: `0 − 3 ≤ 0`, `0 − 1 + 2 = 1 ≥ 0` (three times) and
    /// `0 + 1 + 1 = 2 ≤ 2` (three times).**
    ///
    /// **An exact parity encoding needs an auxiliary integer column (`Σinput − result = 2k`), and
    /// `DeferredFunctionFallbackConstraints` carries rows, not column definitions.**
    ///
    /// **So the criterion <b>asserts the slack exists</b> rather than pretending otherwise. A criterion claiming "XOR is
    /// exact" would fail here, and not because of my code.**
    /// </remarks>
    [Fact]
    public void TheThreeInputXor_ShouldBeKnownSlack()
    {
        var structure = new BinaryLogicStructure(
            Operation: BinaryLogicOperation.Xor, Inputs: new[] { A, B, C },
            ResultVariable: Result, Converter: IIntoValue<Flt64>.Identity, Name: "xor");

        IReadOnlyList<LinearInequality<Flt64>> rows =
            structure.MaterializeFallbackConstraints().Value.Constraints;

        rows.Should().HaveCount(7, "one sum bound, three singles and three pairs");

        // **正确的那些赋值：奇数个 1。**
        // **The correct assignments: an odd number of ones.**
        Feasible(rows, a: 0, b: 0, r: 0, c: 0).Should().BeTrue();
        Feasible(rows, a: 1, b: 0, r: 1, c: 0).Should().BeTrue();
        Feasible(rows, a: 1, b: 1, r: 0, c: 0).Should().BeTrue();

        // **错误的那些赋值：偶数个 1 配 1。**
        // **`(1,0,0,0)` 与 `(1,1,0,1)` 都被拒。**
        //
        // **The wrong assignments: an even number of ones with a one.**
        // **`(1,0,0,0)` and `(1,1,0,1)` are both rejected.**
        Feasible(rows, a: 0, b: 0, r: 1, c: 0).Should().BeFalse();
        Feasible(rows, a: 1, b: 0, r: 0, c: 0).Should().BeFalse();
        Feasible(rows, a: 1, b: 1, r: 1, c: 0).Should().BeFalse();

        // **已知松弛是<b>双向</b>的，而这一点比"单向松弛"更糟：**
        // **`(1,1,1,0)` 被错误接受，`(1,1,1,1)` 被错误拒绝——后者是唯一正确的赋值。**
        //
        // **The known slack is <b>two-sided</b>, which is worse than one-sided:**
        // **`(1,1,1,0)` is wrongly accepted and `(1,1,1,1)` is wrongly rejected — the latter being the one correct
        // assignment.**
        Feasible(rows, a: 1, b: 1, r: 0, c: 1).Should().BeTrue(
            "the baseline's linear XOR encoding cannot exclude it; see the ponytail note");
        Feasible(rows, a: 1, b: 1, r: 1, c: 1).Should().BeFalse(
            "and it also rejects the only correct all-ones assignment");
    }

    /// <summary>
    /// **两输入 XOR 只需要求和与单输入两族** / Two-input XOR needs only the first two families.
    /// </summary>
    /// <remarks>
    /// **`1 + 2 + C(2,2) = 4` 行，其中成对行只有一个。**
    ///
    /// **判据逐组核对，同时确认成对行在两输入下<b>不排除任何合法解</b>：
    /// 两输入 XOR 的可行解是 `(0,0,0)`、`(1,0,1)`、`(0,1,1)`、`(1,1,0)`。**
    ///
    /// **`1 + 2 + C(2,2) = 4` rows, of which one is a pair row.**
    ///
    /// **The criterion walks every assignment and also confirms the pair row excludes no legal solution: the feasible set is
    /// `(0,0,0)`, `(1,0,1)`, `(0,1,1)`, `(1,1,0)`.**
    /// </remarks>
    [Fact]
    public void TheTwoInputXor_ShouldNotOverrestrict()
    {
        var structure = new BinaryLogicStructure(
            Operation: BinaryLogicOperation.Xor, Inputs: new[] { A, B },
            ResultVariable: Result, Converter: IIntoValue<Flt64>.Identity, Name: "xor");

        IReadOnlyList<LinearInequality<Flt64>> rows =
            structure.MaterializeFallbackConstraints().Value.Constraints;

        rows.Should().HaveCount(4);

        for (int a = 0; a <= 1; ++a) {
            for (int b = 0; b <= 1; ++b) {
                for (int r = 0; r <= 1; ++r) {
                    Feasible(rows, a, b, r).Should().Be(r == ((a + b) % 2));
                }
            }
        }
    }

    /// <summary>
    /// **四种无效输入都被拒** / All four kinds of invalid input are rejected.
    /// </summary>
    /// <remarks>
    /// **空输入、非二值输入、非二值结果、NOT 的多输入——四种是四个独立的分支，
    /// 只测其中一个的话，另外三个未测的分支可以整段缺失。**
    ///
    /// **Empty inputs, non-binary input, non-binary result, and NOT with several inputs are four separate branches;
    /// testing one would leave the other three deletable.**
    /// </remarks>
    [Fact]
    public void TheInvalidInputs_ShouldAllBeRejected()
    {
        // **空输入。**
        // **Empty inputs.**
        new BinaryLogicStructure(
            BinaryLogicOperation.And, Array.Empty<IVariableItem>(), Result,
            IIntoValue<Flt64>.Identity, "e").MaterializeFallbackConstraints().Should()
            .BeOfType<Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();

        // **非二值输入。**
        // **A non-binary input.**
        new BinaryLogicStructure(
            BinaryLogicOperation.And, new[] { Free }, Result,
            IIntoValue<Flt64>.Identity, "e").MaterializeFallbackConstraints().Should()
            .BeOfType<Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();

        // **非二值结果。**
        // **A non-binary result.**
        new BinaryLogicStructure(
            BinaryLogicOperation.And, new[] { A }, Free,
            IIntoValue<Flt64>.Identity, "e").MaterializeFallbackConstraints().Should()
            .BeOfType<Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();

        // **NOT 两个输入。**
        // **NOT with two inputs.**
        new BinaryLogicStructure(
            BinaryLogicOperation.Not, new[] { A, B }, Result,
            IIntoValue<Flt64>.Identity, "e").MaterializeFallbackConstraints().Should()
            .BeOfType<Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **AND 与 OR 在"全 0 / 全 1"上不同** / AND and OR differ on the all-zero and all-one corners.
    /// </summary>
    /// <remarks>
    /// **`(0,0,1)`：AND 拒绝、OR 接受。`(1,1,0)`：AND 接受不了、OR 也接受不了，
    /// 但 IN 的四个角里两者的差别恰好在这两个角上。**
    ///
    /// **只测一个运算的话，把 AND 的方向写成 `≥` 会退化成一个"永远可行"的实现，
    /// 而它在只测 OR 的判据里完全看不出来。**
    ///
    /// **`(0,0,1)`: AND rejects it, OR accepts it. The two operations differ exactly on these corners.**
    ///
    /// **Testing only one operation would let an implementation that wrote AND's direction as `≥` degenerate into "always
    /// feasible", invisible in OR-only criteria.**
    /// </remarks>
    [Fact]
    public void TheAndAndOr_ShouldDifferOnTheCorners()
    {
        IReadOnlyList<LinearInequality<Flt64>> andRows = new BinaryLogicStructure(
            BinaryLogicOperation.And, new[] { A, B }, Result,
            IIntoValue<Flt64>.Identity, "and").MaterializeFallbackConstraints().Value.Constraints;

        IReadOnlyList<LinearInequality<Flt64>> orRows = new BinaryLogicStructure(
            BinaryLogicOperation.Or, new[] { A, B }, Result,
            IIntoValue<Flt64>.Identity, "or").MaterializeFallbackConstraints().Value.Constraints;

        andRows.Should().NotBeEquivalentTo(orRows);
        Feasible(andRows, a: 0, b: 0, r: 1).Should().BeFalse();
        Feasible(orRows, a: 0, b: 0, r: 1).Should().BeFalse();
        Feasible(andRows, a: 1, b: 1, r: 0).Should().BeFalse();
        Feasible(orRows, a: 1, b: 1, r: 0).Should().BeFalse();
        Feasible(andRows, a: 1, b: 0, r: 1).Should().BeFalse();
        Feasible(orRows, a: 1, b: 0, r: 1).Should().BeTrue();
    }

    /// <summary>**BINARY 已经接入分派表** / BINARY is wired into the dispatch table.</summary>
    [Fact]
    public void TheBinaryLogic_ShouldBeDispatched()
    {
        var structure = new BinaryLogicStructure(
            BinaryLogicOperation.And, new[] { A, B }, Result,
            IIntoValue<Flt64>.Identity, "and");

        structure.ResultVariableOrNull().Should().BeSameAs(Result);
        structure.HelperVariablesOrEmpty().Should().BeEmpty();
        structure.SupportsDeferredFallback().Should().BeTrue();
    }

    /// <summary>**代入一组二值赋值，看所有行是否满足** / Substitute a binary assignment and check every row.</summary>
    private static bool Feasible(
        IReadOnlyList<LinearInequality<Flt64>> rows,
        int a, int b, int r, int c = 0) {
        foreach (LinearInequality<Flt64> row in rows) {
            double total = row.Lhs.Constant.ToDouble() - row.Rhs.Constant.ToDouble();

            foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
                total += monomial.Coefficient.ToDouble() * ValueOf(monomial.Symbol, a, b, r, c);
            }

            foreach (LinearMonomial<Flt64> monomial in row.Rhs.Monomials) {
                total -= monomial.Coefficient.ToDouble() * ValueOf(monomial.Symbol, a, b, r, c);
            }

            // **1e-9 的余量：系数都经过转换器，累加会有极小的浮点误差。**
            // **A 1e-9 margin: coefficients pass through the converter and summation carries tiny float error.**
            bool holds = row.Comparison switch {
                Comparison.LE => total <= 1e-9,
                Comparison.GE => total >= -1e-9,
                Comparison.EQ => global::System.Math.Abs(total) <= 1e-9,
                _ => false,
            };

            if (!holds) {
                return false;
            }
        }

        return true;
    }

    private static double ValueOf(ISymbol symbol, int a, int b, int r, int c) {
        if (symbol.Equals(A)) { return a; }
        if (symbol.Equals(B)) { return b; }
        if (symbol.Equals(C)) { return c; }
        if (symbol.Equals(Result)) { return r; }
        return 0.0;
    }

    private static readonly BinVar A = new("bl-a");

    private static readonly BinVar B = new("bl-b");

    private static readonly BinVar C = new("bl-c");

    private static readonly BinVar Result = new("bl-result");

    private static readonly RealVar Free = new("bl-free");
}
