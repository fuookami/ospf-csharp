#nullable enable

using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Inequality;
using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>
/// MAX 函数的结构与 fallback 快照 / Maximum structure and fallback snapshot.
/// </summary>
/// <remarks>
/// 对齐的 `MaxStructure`。
///
/// **`max` 需要每个候选一个选择列，而不是一个：**
/// **`SelectorVariables` 与 `Inputs` 一一对应，`BigMValues` 也是。**
/// **用一个"当前最大值"列无法表达"是哪一个候选取到最大"，
/// 而那正是后续按候选处理的地方。**
///
/// **`ValidateFallbackInputBounds` 的方向是"当前必须落在捕获之内"：**
///
/// | 情况 | 结论 |
/// | --- | --- |
/// | 当前范围<b>收缩</b>了 | **可以**（**Big-M 上界仍然有效**） |
/// | 当前范围<b>扩大</b>了 | **拒绝**（**Big-M 可能不再是上界**） |
///
/// **"收缩可以、扩大不行"不是对称的，所以判据必须两个方向都测——
/// 只测"扩大被拒"的话，"永远拒绝"的实现也能通过；
/// 只测"收缩可以"的话，"永远接受"的实现也能通过。**
///
/// Following its `MaxStructure`.
///
/// **`max` needs one selector column per candidate, not one overall**: **`SelectorVariables` maps one-to-one onto
/// `Inputs`, and so does `BigMValues`.** **A single "current maximum" column cannot express <i>which</i> candidate
/// attains it — and that is what candidate-dependent handling needs.**
///
/// **`ValidateFallbackInputBounds` runs in the direction "the current range must sit inside the captured one":**
///
/// | Case | Verdict |
/// | --- | --- |
/// | the current range <b>shrank</b> | **allowed** (**the Big-M upper bound still holds**) |
/// | the current range <b>grew</b> | **rejected** (**the Big-M may no longer bound it**) |
///
/// **"shrink ok, grow not" is asymmetric, so a criterion must test both directions — testing only "grow is rejected"
/// passes for an implementation that always rejects; only "shrink is ok" passes for one that always accepts.**
/// </remarks>
public sealed record MaxStructure(
    IReadOnlyList<LinearPolynomial<Flt64>> Inputs,
    IVariableItem ResultVariable,
    IReadOnlyList<IVariableItem> SelectorVariables,
    IReadOnlyList<Flt64> BigMValues,
    IIntoValue<Flt64> Converter,
    string Name = "max") : DeferredFunctionStructure
{
    /// <summary>使用语境 / Usage context.</summary>
    public FunctionUsageSummary Usage { get; init; } = new();

    /// <summary>创建快照时捕获的输入范围证明，<c>null</c> 表示未捕获 / Captured input-bound proofs.</summary>
    public IReadOnlyList<LinearPolynomialBounds<Flt64>?>? CapturedInputBounds { get; init; }

    /// <summary>是否取最小值 / Whether to compute the minimum.</summary>
    public bool Minimum { get; init; }

    /// <summary>
    /// fallback 路径需要保留的选择变量 / Selector variables retained by the fallback path.
    /// </summary>
    public IReadOnlyList<IVariableItem> HelperVariables => SelectorVariables;

    /// <summary>
    /// 校验捕获的输入范围证明是否仍然成立 / Validate that the captured input-bound proofs still hold.
    /// </summary>
    /// <remarks>
    /// **数量不匹配先检查，且不在 `try` 之内**：**否则 `Inputs[index]` 会抛下标越界，
    /// 而那个异常会被 `catch` 转成一条完全不同的消息——
    /// 调用方看到"校验失败"而真正的原因是"数量对不上"。**
    ///
    /// **`null` 条目表示"这个输入当时没有证明"，跳过它，而不是当成"证明为零"。**
    ///
    /// **The count check runs first and outside the `try`**: **otherwise `Inputs[index]` throws an out-of-range exception
    /// that the `catch` turns into a completely different message — the caller reads "validation failed" while the cause
    /// is a count mismatch.**
    ///
    /// **A `null` entry means "no proof for this input" and is skipped, not treated as a proof of zero.**
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> ValidateFallbackInputBounds() {
        IReadOnlyList<LinearPolynomialBounds<Flt64>?>? captured = CapturedInputBounds;
        if (captured is null) {
            return Ok();
        }

        if (captured.Count != Inputs.Count) {
            return Fail(
                $"the MAX snapshot has {captured.Count} input-bound proofs for {Inputs.Count} inputs");
        }

        try {
            for (int position = 0; position < captured.Count; ++position) {
                LinearPolynomialBounds<Flt64>? original = captured[position];
                if (original is null) {
                    continue;
                }

                LinearPolynomialBounds<Flt64>? current =
                    // **`BigMValues` 在这里先被解析成记录自己的属性（`IReadOnlyList<Flt64>`），
                    // 所以必须全限定到静态类。**
                    // **The record's own property shadows the static class of the same name, so the static one needs a
                    // fully-qualified reference.**
                    global::Fuookami.Ospf.Core.Symbol.Function.BigMValues.FiniteBounds(
                        Inputs[position], Converter);
                if (current is null) {
                    return Fail(
                        $"the MAX input-bound proof for input {position} is unavailable during materialization");
                }

                if (current.LowerBound.CompareTo(original.LowerBound) < 0 ||
                    current.UpperBound.CompareTo(original.UpperBound) > 0) {
                    return Fail(
                        $"the MAX input bounds for input {position} exceed the captured Big-M proof");
                }
            }

            return Ok();
        }
        catch (Exception error) {
            return Fail($"failed to validate the MAX input-bound proof: {error.Message}");
        }
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> Ok() =>
        new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());

    private static Result<Success, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        new Failed<Success, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);

    /// <summary>
    /// 生成 MAX 约束行 / Generate the MAX rows.
    /// </summary>
    /// <remarks>
    /// **`2n + 1` 行，分三族：**
    ///
    /// | 行 | 内容 | 作用 |
    /// | --- | --- | --- |
    /// | `_max_lb_i` | `result − input_i ≥ 0` | **结果不小于每个输入** |
    /// | `_max_ub_i` | `result − input_i + M_i·sel_i ≤ M_i` | **选中的那个输入给出上界** |
    /// | `_max_select_one` | `Σsel = 1` | **恰好一个选择变量为 1** |
    ///
    /// **只有下界族的话，`result` 可以任意大——所以上界族是必需的。**
    /// **而上界族只在被选中的那一支紧：`sel_i = 1` 时它化成 `result ≤ input_i`，
    /// `sel_i = 0` 时化成 `result ≤ input_i + M_i`（松的）。**
    ///
    /// **`_max_select_one` 是<b>等式</b>而不是 `≤ 1`，这个区别是决定性的：**
    /// **换成 `≤ 1`，全零选择就可行，于是所有的上界都处于松弛状态
    /// （`result ≤ input_i + M_i`），`result` 只需要 `≥ max` 就可以任意大。**
    /// **等式把"至少一个被选中"钉住，配上"至多一个"（选择变量是二值且和为 1）才给出确切的一个。**
    ///
    /// **`M_i` 必须<b>足够大</b>，而这一层不知道"足够"是多少**：
    /// **未被选中的那支要求 `result ≤ input_i + M_i`，所以 `M_i` 至少要覆盖
    /// `max − input_i`。** **`M_i` 偏小不会让模型报错，只会把真正的最大值切掉——
    /// 而那个结果仍然可行、求解成功。** **`M_i` 的来源是创建快照时捕获的输入范围，见 `ValidateFallbackInputBounds`。**
    ///
    /// **`M_i` 只要求非负，与 `AbsStructure` 一致**（**`PiecewiseBigM` 要求严格正，那里的 M 是松弛量**）。
    ///
    /// **`2n + 1` rows in three families:**
    ///
    /// | Row | Content | Role |
    /// | --- | --- | --- |
    /// | `_max_lb_i` | `result − input_i ≥ 0` | **the result is at least every input** |
    /// | `_max_ub_i` | `result − input_i + M_i·sel_i ≤ M_i` | **the selected input supplies the upper bound** |
    /// | `_max_select_one` | `Σsel = 1` | **exactly one selector is one** |
    ///
    /// **The lower bounds alone leave `result` unbounded above, so the upper family is required.** **And an upper row binds
    /// only for the selected branch: at `sel_i = 1` it becomes `result ≤ input_i`, at `sel_i = 0` it becomes
    /// `result ≤ input_i + M_i` (slack).**
    ///
    /// **`_max_select_one` is an <b>equality</b>, not `≤ 1`, and that difference decides everything**: **as `≤ 1`, all
    /// selectors zero becomes feasible, every upper row sits slack (`result ≤ input_i + M_i`), and `result` only has to
    /// be `≥ max` while being free to grow.** **The equality pins "at least one selected", and with binary selectors
    /// summing to one that gives exactly one.**
    ///
    /// **`M_i` must be <b>large enough</b>, and this layer does not know how large**: **an unselected branch demands
    /// `result ≤ input_i + M_i`, so `M_i` has to cover `max − input_i`.** **An `M_i` that is too small does not make the
    /// model error — it cuts the true maximum off, and the result is still feasible and solves fine.** **Where `M_i` comes
    /// from is the input range captured at snapshot time; see `ValidateFallbackInputBounds`.**
    ///
    /// **`M_i` need only be nonnegative, as in `AbsStructure`** (**`PiecewiseBigM` demands strict positivity there,
    /// because that M is slack**).
    /// </remarks>
    public Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        GenerateConstraints() {
        try {
            if (Inputs.Count == 0 ||
                SelectorVariables.Count != Inputs.Count ||
                BigMValues.Count != Inputs.Count) {
                return FailRows("MAX inputs, selectors and Big-M values must have matching non-empty sizes");
            }

            var keys = new List<VariableItemKey>(Inputs.Count + 1) { ResultVariable.Key };
            foreach (IVariableItem selector in SelectorVariables) {
                keys.Add(selector.Key);
            }

            if (new HashSet<VariableItemKey>(keys).Count != keys.Count) {
                return FailRows("MAX result and selector variables must have distinct keys");
            }

            foreach (LinearPolynomial<Flt64> input in Inputs) {
                if (!ConditionPolynomials.IsUsable(input)) {
                    return FailRows("MAX inputs or Big-M values contain non-finite or unrepresentable values");
                }
            }

            foreach (Flt64 bigM in BigMValues) {
 // **`BigMValues` 属性遮蔽了同名静态类，要全限定。**
                // **The `BigMValues` property shadows the static class of the same name, so qualify it.**
                if (!global::Fuookami.Ospf.Core.Symbol.Function.BigMValues.IsUsable(bigM) ||
                    bigM.CompareTo(Flt64.Zero) < 0) {
                    return FailRows("MAX inputs or Big-M values contain non-finite or unrepresentable values");
                }
            }

            Flt64 zero = Flt64.Zero;
            Flt64 one = Converter.IntoValue(Flt64.One);
            LinearMonomial<Flt64>[] empty = Array.Empty<LinearMonomial<Flt64>>();

            // **方向：`Minimum` 时把整组行<b>整体取负</b>。**
            //
            // **`Minimum` 这个属性此前<b>没有任何地方读它</b>——**
 // **声明了、投影也在填，而生成逻辑无论方向都产 MAX 行。**
            // **于是 `MinStructure` 会被当成 MAX 物化：模型合法、能解，只是答案是最大值。**
            //
            // **为什么"整体取负"是对的而不是"另写一套 MIN 行"：**
            // **MAX 的两组行是 `result − input ≥ 0` 与 `result − input ≤ M(1 − sel)`。
            // 同乘 −1 得 `input − result ≥ 0` 与 `input − result ≤ −M(1 − sel)`，
            // 也就是 `result ≤ input` 与 `result ≥ input + M(1 − sel)`——正是 MIN 需要的两组行。**
            //
            // **所以一个符号因子就够了，不需要第二套实现。**
            // **而 `generateMinConstraints` 完全可以写成<b>独立的一段</b>（它自己的消息、
            // 自己的一遍循环），本仓把它合成了一个实现。合并本身没问题，但合并的时候
            // "方向"这个参数没接上。**
            //
            // **Direction: when `Minimum`, negate the whole row set.**
            //
            // **Found that `Minimum` was <b>read by nothing</b> — it was declared, the projection even filled it in
 // , and the generation produced MAX rows either way.** **So a min would materialise as a MAX: legal,
            // solvable, merely answering with the maximum.**
            //
            // **Negating the whole set is right rather than writing a second set: MAX's two families are
            // `result - input >= 0` and `result - input <= M(1 - sel)`; multiplying by -1 gives `result <= input` and
            // `result >= input + M(1 - sel)`, exactly what MIN needs.**
            //
            // **So one sign suffices, no second implementation.** **A <b>separate</b>
            // `generateMinConstraints` with its own message and its own loop would be unnecessary; this repo merged the two, which is fine — the
            // merge just did not wire the direction through.**
            bool minimum = Minimum;
            string direction = minimum ? "min" : "max";
            // **行名保留 MAX 的旧形状（`_max_lb_`／`_max_ub_`）不动**：既有判据钉着它，
            // 而没有理由为了改名去动一条已经在看这个字符串的判据。
            // **MIN 的两个族语义是<b>对调</b>的**（第一族变成上界、第二族变成下界），
            // 所以名字也跟着对调，否则读日志的人会把上界当上下界。
            //
            // **The MAX row names keep their old shape** (`_max_lb_`/`_max_ub_`): an existing criterion pins them and there is
            // no reason to touch a criterion over a rename. **MIN's two families <b>swap</b> meaning** (the first becomes an
            // upper bound, the second a lower bound), so the labels swap too — otherwise a log reader reads an upper bound as
            // a lower one.
            string firstFamily = minimum ? "ub" : "lb";
            string secondFamily = minimum ? "lb" : "ub";
            Flt64 sign = minimum ? one.Negate() : one;
            var resultMonomial = new LinearMonomial<Flt64>(sign, ResultVariable);
            var rows = new List<LinearInequality<Flt64>>(Inputs.Count * 2 + 1);

            // **第一组：MAX 时 result ≥ input_i，MIN 时 result ≤ input_i。**
            // **First family: result >= input_i for MAX, result <= input_i for MIN.**
            for (int index = 0; index < Inputs.Count; ++index) {
                var monomials = new List<LinearMonomial<Flt64>>(Inputs[index].Monomials.Count + 1) {
                    resultMonomial,
                };

                foreach (LinearMonomial<Flt64> monomial in Inputs[index].Monomials) {
                    monomials.Add(SignedMonomial(monomial, minimum));
                }

                rows.Add(new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(monomials, Signed(Inputs[index].Constant, minimum)),
                    Rhs: new LinearPolynomial<Flt64>(empty, zero),
                    Comparison: Comparison.GE,
                    Name: $"{Name}_{direction}_{firstFamily}_{index}"));
            }

            // **第二组：被选中时收紧到 input_i，未选中时松弛 M_i。**
            // **Second family: tight to input_i when selected, slack by M_i otherwise.**
            for (int index = 0; index < Inputs.Count; ++index) {
                var monomials = new List<LinearMonomial<Flt64>>(Inputs[index].Monomials.Count + 2) {
                    resultMonomial,
                };

                foreach (LinearMonomial<Flt64> monomial in Inputs[index].Monomials) {
                    monomials.Add(SignedMonomial(monomial, minimum));
                }

                // **Big-M 项<b>两个方向都不取负</b>——这是第二处符号陷阱。**
                //
                // **第一版把 Big-M 也一起取负，得到 `input − result ≤ −M(1 − s)`：
                // 未选中的那个输入就变成 `result ≥ input + M`，直接把模型弄成无解。**
                //
                // **正确的 MIN 第二组是 `input − result ≤ M(1 − s)`：**
                // **选中时（s = 1）收紧成 `result ≥ input`，未选中时松弛到 `result ≥ input − M`。**
                // **松弛的方向是<b>往下</b>而不是往上——这就是"整体取负"这个想法错掉的地方，
                // 而它只错在 Big-M 这一项上。**
                //
                // **The Big-M term is <b>not negated in either direction</b> — the second sign trap.**
                // **The first version negated it too, yielding `input - result <= -M(1 - s)`: an unselected input then reads
                // `result >= input + M`, which is infeasible outright.**
                // **The right MIN pair is `input - result <= M(1 - s)`: tight to `result >= input` when selected, slack to
                // `result >= input - M` otherwise.** **Slack goes <b>downward</b>, not upward — which is exactly where
                // "negate everything" is wrong, and it is wrong only in the Big-M term.**
                Flt64 signedBigM = BigMValues[index];
                monomials.Add(new LinearMonomial<Flt64>(signedBigM, SelectorVariables[index]));

                rows.Add(new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(monomials, Signed(Inputs[index].Constant, minimum)),
                    Rhs: new LinearPolynomial<Flt64>(empty, signedBigM),
                    Comparison: Comparison.LE,
                    Name: $"{Name}_{direction}_{secondFamily}_{index}"));
            }

            // **恰好一个被选中——等式，不是 ≤ 1。**
            // **Exactly one selected — an equality, not ≤ 1.**
            var selectors = new List<LinearMonomial<Flt64>>(SelectorVariables.Count);
            foreach (IVariableItem selector in SelectorVariables) {
                selectors.Add(new LinearMonomial<Flt64>(one, selector));
            }

            rows.Add(new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(selectors, zero),
                Rhs: new LinearPolynomial<Flt64>(empty, one),
                Comparison: Comparison.EQ,
                Name: $"{Name}_max_select_one"));

            return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
        }
        catch (Exception error) {
            return FailRows($"failed to generate MAX constraints: {error.Message}");
        }
    }

    /// <summary>
    /// 物化 fallback 约束 / Materialize the fallback constraints.
    /// </summary>
    /// <remarks>
    /// **顺序是"先校验输入范围证明，再生成行"。**
    ///
    /// **校验在前不是形式：** **`M_i` 的正确性依赖那个证明。
    /// 先生成行再校验的话，一个证明已经失效的快照会先产出一组行，
    /// 而调用方可能已经拿它去建模型了。**
    ///
 /// **The order is "validate the input-range proof, then generate the rows".**
    ///
    /// **Checking first is not formality**: **the correctness of `M_i` rests on that proof.** **Generating first would let a
    /// snapshot with an invalidated proof produce rows that the caller may already have used to build the model.**
    /// </remarks>
    public Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
        MaterializeFallbackConstraints() {
        Result<Success, ErrorCode, Error<ErrorCode>> validation = ValidateFallbackInputBounds();
        if (validation is Failed<Success, ErrorCode, Error<ErrorCode>> invalid) {
            return new Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
                invalid.Error.Code, invalid.Error.Message);
        }

        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> rows =
            GenerateConstraints();
        if (rows is Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> failed) {
            return new Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
                failed.Error.Code, failed.Error.Message);
        }

        return new Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
            new DeferredFunctionFallbackConstraints(
                ((Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)rows)
                .Value));
    }

    private static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        FailRows(string message) =>
            new Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);

    /// <summary>
    /// **按方向给一个系数的符号 / Give a coefficient the direction's sign.**
    /// </summary>
    /// <remarks>
    /// **基础情形（MAX）是<b>取负</b>，`Minimum` 时反而不取。**
    /// **不写成 `coefficient * sign` 而是"要么原样、要么取负"，是因为乘法在这里没有可读性收益，
    /// 而取负是精确的（乘法在浮点上会引入一次额外的舍入）。**
    ///
    /// **The base case (MAX) is the <b>negation</b>, and `Minimum` is the one that does not negate.**
    /// **"Keep or negate" rather than `* sign` because multiplication buys no readability here and negation is exact.**
    ///
    /// **Not `coefficient * sign` but "keep or negate": multiplication buys no readability here and negation is exact where
    /// multiplying introduces one more rounding.**
    /// </remarks>
    /// <param name="monomial">单项式 / The monomial.</param>
    /// <param name="negative">是否取负 / Whether to negate.</param>
    /// <returns>带符号的单项式 / The signed monomial.</returns>
    private static LinearMonomial<Flt64> SignedMonomial(LinearMonomial<Flt64> monomial, bool negative) =>
        new(negative ? monomial.Coefficient : monomial.Coefficient.Negate(), monomial.Symbol);

    /// <summary>
    /// **按方向给一个数取符号 / Apply the direction's sign to a value.**
    /// </summary>
    /// <param name="value">数值 / The value.</param>
    /// <param name="negative">是否取负 / Whether to negate.</param>
    /// <returns>带符号的值 / The signed value.</returns>
    private static Flt64 Signed(Flt64 value, bool negative) =>
        negative ? value : value.Negate();
}
