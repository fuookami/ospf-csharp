#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 离散情景的 CVaR（条件风险价值）/ Conditional value at risk over discrete scenarios.
/// </summary>
/// <remarks>
/// 对齐基线 Cvar.kt 的 CvarFunction。
///
/// 定义是 <c>min over t of [ t + Σ p_i · max(L_i - t, 0) / (1 - alpha) ]</c>，
/// 也就是“在最坏的 (1-alpha) 尾部上的平均损失”。
///
/// 这个函数有三处容易写错，而三处都不报错：
/// ① 分母写成 alpha 而不是 1-alpha；② 忘了 max(...,0) 直接累加差值；③ 阈值取遍所有情景，
/// 而不是只取一个“看起来对”的。
///
/// The CvarFunction from the baseline.
///
/// Defined as <c>min over t of [ t + Σ p_i · max(L_i - t, 0) / (1 - alpha) ]</c> —
/// the average loss over the worst (1-alpha) tail.
///
/// Three things are easy to get wrong here, and none throws:
/// ① dividing by alpha instead of 1-alpha; ② summing the differences without max(...,0); ③ taking the minimum over
/// a hand-picked threshold instead of every scenario.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class CvarFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IIntoValue<V> _converter;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    /// <summary>创建 CVaR 函数 / Create a CVaR function.</summary>
    /// <param name="losses">各情景的损失多项式 / Loss polynomial per scenario.</param>
    /// <param name="probabilities">与损失对应的概率 / Probabilities paired with the losses.</param>
    /// <param name="alpha">置信水平，必须属于 [0, 1) / Confidence level, must lie in [0, 1).</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="bigM">可选 Big-M / Optional Big-M.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public CvarFunction(
        IReadOnlyList<LinearPolynomial<V>> losses,
        IReadOnlyList<V> probabilities,
        V alpha,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "cvar",
        string? displayName = null) {
        if (losses is null || losses.Count == 0) {
            throw new ArgumentException("CVaR needs at least one scenario", nameof(losses));
        }

        if (probabilities is null || probabilities.Count != losses.Count) {
            throw new ArgumentException(
                "CVaR losses and probabilities must have equal, non-zero sizes",
                nameof(probabilities));
        }

        _converter = converter ?? throw new ArgumentNullException(nameof(converter));

        // alpha 必须落在 [0, 1)：等于 1 时 1-alpha 为零，尾部为空，整个式子没有定义。
        // **而这个除法不会抛异常——它会变成 inf 或 NaN，然后悄悄污染整条目标函数。**
        //
        // alpha must lie in [0, 1): at 1 the denominator is zero, the tail is empty and the expression is undefined.
        // **That division does not throw — it yields inf or NaN and quietly poisons the whole objective.**
        if (alpha.CompareTo(converter.Zero) < 0 || alpha.CompareTo(converter.One) >= 0) {
            throw new ArgumentException(
                $"CVaR alpha must lie in [0, 1) (alpha={alpha})",
                nameof(alpha));
        }

        if (probabilities.Any(probability => probability.CompareTo(converter.Zero) < 0)) {
            throw new ArgumentException(
                "CVaR probabilities must be non-negative",
                nameof(probabilities));
        }

        V total = converter.Zero;
        foreach (V probability in probabilities) {
            total = total.Plus(probability);
        }

        // 概率之和必须是 1：否则“加权平均”就不是平均，而 CVaR 的定义建立在这个前提上。
        // **和为 1.0000001 与 0.9999999 都会让结果偏离，而偏离量随 alpha 放大。**
        //
        // Probabilities must sum to one: otherwise the weighted average is not an average, and CVaR's definition
        // rests on that. **A sum of 1.0000001 or 0.9999999 skews the result, and the skew grows with alpha.**
        if (total.CompareTo(converter.One) != 0) {
            throw new ArgumentException(
                $"CVaR probabilities must sum to one (sum={total})",
                nameof(probabilities));
        }

        Losses = losses.ToArray();
        Probabilities = probabilities.ToArray();
        Alpha = alpha;
        Name = name;
        DisplayName = displayName;

        // 每个阈值一个候选式，最后取最小值——这正是 CVaR 的定义，
        // 而不是“取平均值”或“取某个分位点”。
        //
        // One candidate per threshold, then the minimum — that is the definition of CVaR,
        // not an average and not some quantile.
        var candidates = new List<LinearPolynomial<V>>();
        for (int threshold = 0; threshold < Losses.Count; ++threshold) {
            candidates.Add(Candidate(threshold));
        }

        _minimum = new MinFunction<V>(
            polynomials: candidates,
            converter: converter,
            bigM: bigM,
            name: $"{name}_minimum",
            displayName: displayName);

        _helperVariables = _minimum.HelperVariables;
    }

    private readonly MinFunction<V> _minimum;

    /// <summary>各情景的损失 / The per-scenario losses.</summary>
    public IReadOnlyList<LinearPolynomial<V>> Losses { get; }

    /// <summary>与损失对应的概率 / Probabilities paired with the losses.</summary>
    public IReadOnlyList<V> Probabilities { get; }

    /// <summary>置信水平 / Confidence level.</summary>
    public V Alpha { get; }

    /// <summary>数值转换器 / Numeric converter.</summary>
    public IIntoValue<V> Converter => _converter;

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Losses);

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => _minimum.ResultPolynomial;

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) {
        var evaluated = new List<V>(Losses.Count);
        foreach (LinearPolynomial<V> loss in Losses) {
            Result<V?, ErrorCode, Error<ErrorCode>> raw = FunctionEvaluation.Polynomial(loss, values);
            if (raw.Value is not { } value) {
                return null;
            }

            evaluated.Add(value);
        }

        return CvarValue(evaluated);
    }

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        new Ok<V?, ErrorCode, Error<ErrorCode>>(Evaluate(values));

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        _minimum.RegisterAuxiliaryTokens(tokens);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) =>
        _minimum.RegisterConstraints(model);

    /// <summary>
    /// 对给定的一组损失值求 CVaR / Compute CVaR for a concrete set of losses.
    /// </summary>
    /// <remarks>
    /// <c>min over t of [ t + Σ p_i · max(L_i - t, 0) / (1 - alpha) ]</c>，
    /// 其中 t 取遍每一个观测损失——**这正是"精确"的含义**：
    /// **不用近似分位点，而是把每个情景都当作候选阈值试一遍。**
    ///
    /// <c>min over t of [ t + Σ p_i · max(L_i - t, 0) / (1 - alpha) ]</c>,
    /// with t ranging over every observed loss — **that is what "exact" means here**:
    /// **no approximate quantile, every scenario is tried as a threshold.**
    /// </remarks>
    private V CvarValue(IReadOnlyList<V> losses) {
        V denominator = _converter.One.Minus(Alpha);
        V? minimum = null;

        foreach (V threshold in losses) {
            V candidate = threshold;
            for (int index = 0; index < losses.Count; ++index) {
                V excess = losses[index].Minus(threshold);

                // **只有超出阈值的部分计入**：这是 max(...,0)，也是"尾部损失"的定义。
                // **少了它，candidate 就变成"平均值加上阈值的倍数"，与 CVaR 无关。**
                //
                // **Only the excess over the threshold counts**: that is max(...,0) and the definition of tail loss.
                // **Without it, the candidate becomes "the mean plus a multiple of the threshold", unrelated to CVaR.**
                if (excess.CompareTo(_converter.Zero) > 0) {
                    candidate = candidate.Plus(Probabilities[index].Times(excess).Div(denominator));
                }
            }

            if (minimum is null || candidate.CompareTo(minimum.Value) < 0) {
                minimum = candidate;
            }
        }

        return minimum!.Value;
    }

    /// <summary>构造一个阈值下的候选式 / Build the candidate for one threshold.</summary>
    private LinearPolynomial<V> Candidate(int threshold) {
        V denominator = _converter.One.Minus(Alpha);
        LinearPolynomial<V> candidate = Losses[threshold];

        for (int index = 0; index < Losses.Count; ++index) {
            // max(L_i - t, 0)：**这条式子在阈值处不可微，所以必须交给 MaxFunction 编码，
            // 而不是把它展开成线性项——展开会让"超出多少"变成"差值多少"，负的一半也算进去。**
            //
            // max(L_i - t, 0): **this expression is non-differentiable at the threshold, so it goes through
            // MaxFunction rather than being expanded into linear terms — expanding would turn "the excess" into
            // "the difference", counting the negative half too.**
            var excess = new MaxFunction<V>(
                polynomials: new[] {
                    Losses[index] - Losses[threshold],
                    new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), _converter.Zero),
                },
                converter: _converter,
                name: $"{Name}_excess_{threshold}_{index}",
                displayName: DisplayName);

            V weight = Probabilities[index].Div(denominator);
            candidate += excess.ResultPolynomial * weight;
        }

        return candidate;
    }
}
