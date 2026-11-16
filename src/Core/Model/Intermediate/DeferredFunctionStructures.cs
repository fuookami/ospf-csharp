#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>
/// 函数展开策略，默认 <see cref="Eager"/> / Function expansion policy, defaulting to eager.
/// </summary>
/// <remarks>
/// 对齐的 `FunctionExpansionPolicy`。
///
/// **三个值的区别只在"什么时候决定走原生接口"：**
///
/// | 值 | 含义 |
/// | --- | --- |
/// | `Eager` | **立刻展开成行，不做原生尝试** |
/// | `DeferredNativeFirst` | **先试原生接口，不行再展开** |
/// | `Auto` | **按结构自己报告的能力选** |
///
/// **`Eager` 是默认值**：**"先试原生"看起来更好，但它让每个结构都需要一份"我能不能原生"的判断，
/// 而那个判断本身就是最容易写错的地方。**
///
/// Following its `FunctionExpansionPolicy`.
///
/// **The three values differ only in <i>when</i> the native path is chosen:**
///
/// | Value | Meaning |
/// | --- | --- |
/// | `Eager` | **expand into rows immediately, no native attempt** |
/// | `DeferredNativeFirst` | **try the native interface first, expand otherwise** |
/// | `Auto` | **let the structure's own reported capability decide** |
///
/// **`Eager` is the default**: **"try native first" looks better, but it makes every structure need a "can I go
/// native" judgement, and that judgement is exactly where things go wrong.**
/// </remarks>
public enum FunctionExpansionPolicy
{
    /// <summary>立刻展开 / Expand immediately.</summary>
    Eager,

    /// <summary>先试原生，失败则展开 / Try native first, expand otherwise.</summary>
    DeferredNativeFirst,

    /// <summary>按结构报告的能力选择 / Let the structure's capability decide.</summary>
    Auto,
}

/// <summary>
/// <see cref="DeferredFunctionStructure"/> 的分派表 / The dispatch table over <see cref="DeferredFunctionStructure"/>.
/// </summary>
/// <remarks>
/// 对齐中五个 `DeferredFunctionStructure` 扩展函数。
///
/// **这五个函数是"标记接口"付出代价的地方**：**接口本身没有成员（已说明理由：
/// 它只做类型层面的集合划分），于是<b>所有具体信息都要靠一次类型分派取出来</b>。**
///
/// **分派里每个 `else` 分支都是真实的语义，不是占位：**
/// **`ResultVariableOrNull` 对不认识的结构返回 `null`，
/// 表达的正是"这个结构没有保留结果列"，而不是"我还没写完"。**
///
/// **本实现只覆盖本程序集已有的结构（`IndicatorStructure`）。**
/// **其余结构在各有分支，但它们在本仓库还不存在——
/// 等它们落地时，<b>必须同时在这里加分支</b>。**
/// **漏加不会编译失败，只会让那个结构静默地报"没有结果列"，
/// 而调用方拿到 `null` 后通常走"不需要保留"的分支，于是模型少了一列。**
/// **这是本文件唯一的真实危险，判据用"未登记的结构返回 `null`/`false`"把它钉住。**
///
/// Following its five `DeferredFunctionStructure` extensions.
///
/// **These five are where the marker interface pays its price**: **the interface has no members (it
/// only partitions types), so <b>every concrete fact must be pulled out through a type dispatch</b>.**
///
/// **Each `else` branch is real semantics, not a placeholder**: **`ResultVariableOrNull` returning `null` for an
/// unknown structure says "this structure retains no result column", not "I am unfinished".**
///
/// **This implementation covers only the structures this assembly has (`IndicatorStructure`).**
/// **The code has a branch for each of the others, but they do not exist here yet — <b>whoever lands one must add a
/// branch here too</b>.**
/// **Forgetting does not fail the build; the structure silently reports "no result column", the caller usually takes the
/// "no retention needed" path, and the model ends up short one column.**
/// **That is this file's one real hazard, and a criterion pins it by asserting an unregistered structure returns
/// `null`/`false`.**
/// </remarks>
public static class DeferredFunctionStructures
{
    /// <summary>保留的结果列，没有则 <c>null</c> / The retained result column, or <c>null</c>.</summary>
    public static IVariableItem? ResultVariableOrNull(this DeferredFunctionStructure structure) =>
        structure switch {
            IndicatorStructure indicator => indicator.ResultVariable,
            AbsStructure abs => abs.ResultVariable,
            MaxStructure max => max.ResultVariable,
            SemiStructure semi => semi.ResultVariable,
            UnivariateLinearPiecewiseStructure piecewise => piecewise.ResultVariable,
            MaskingStructure masking => masking.ResultVariable,
            BinaryLogicStructure logic => logic.ResultVariable,
            _ => null,
        };

    /// <summary>
    /// 单输入多项式，没有则 <c>null</c> / The single input polynomial, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// **`MaxStructure` 走的是 `inputs.singleOrNull`——多输入时返回 `null` 而不是取第一个。**
    /// **"取第一个"会让一个二元 `max` 被当成一元的，而调用方无从发现。**
    ///
    /// **`MaxStructure` uses `inputs.singleOrNull` — it returns `null` for several inputs rather than taking the
    /// first.** **Taking the first would treat a binary `max` as unary, with nothing telling the caller.**
    /// </remarks>
    public static LinearPolynomial<Flt64>? InputOrNull(this DeferredFunctionStructure structure) =>
        structure switch {
            IndicatorStructure indicator => indicator.Input,
            AbsStructure abs => abs.Input,
            MaxStructure max => max.Inputs.Count == 1 ? max.Inputs[0] : null,
            UnivariateLinearPiecewiseStructure piecewise => piecewise.Input,
            MaskingStructure masking => masking.Value.Input,
            _ => null,
        };

    /// <summary>全部输入多项式 / Every input polynomial.</summary>
    /// <remarks>
    /// **`IndicatorStructure` 会把自己的输入<b>连同附加条件的输入</b>一起报出来：
    /// 条件值、蕴含前件、合取条件、差值条件。**
    /// **只报主输入的话，那些附加条件的变量范围就没人收集，
    /// 而 Big-M 的自动推导依赖这份清单——于是 M 会偏小，行会切掉合法解。**
    ///
    /// **`IndicatorStructure` reports its own input <b>together with the attached conditions' inputs</b>: the
    /// conditional value, the implication antecedent, the conjunction, the difference.**
    /// **Reporting only the main input leaves those conditions' variable ranges uncollected, and the automatic Big-M
    /// derivation depends on this list — so M comes out too small and the rows cut off legal solutions.**
    /// </remarks>
    public static IReadOnlyList<LinearPolynomial<Flt64>> InputPolynomialsOrEmpty(
        this DeferredFunctionStructure structure) {
        switch (structure) {
            case IndicatorStructure indicator: {
                var inputs = new List<LinearPolynomial<Flt64>>(5) { indicator.Input };

                if (indicator.ConditionalValue is not null) {
                    inputs.Add(indicator.ConditionalValue.Input);
                }

                if (indicator.ImpliedCondition is not null) {
                    inputs.Add(indicator.ImpliedCondition.Input);
                }

                if (indicator.Conjunction is not null) {
                    inputs.Add(indicator.Conjunction.Condition.Input);
                }

                if (indicator.Difference is not null) {
                    inputs.Add(indicator.Difference.Condition.Input);
                }

                return inputs;
            }

            case AbsStructure abs:
                return new[] { abs.Input };

            case MaxStructure max:
                return max.Inputs;

            case UnivariateLinearPiecewiseStructure piecewise:
                return new[] { piecewise.Input };

            case MaskingStructure masking: {
                // **mask 的定义（若给了）也是一个输入多项式，必须报出来——
                // 否则它的变量范围没人收集，自动 Big-M 会偏小。**
                //
                // **The mask definition, when present, is also an input polynomial and must be reported — otherwise its
                // variable ranges go uncollected and the automatic Big-M comes out too small.**
                var inputs = new List<LinearPolynomial<Flt64>>(3) {
                    masking.Value.Input,
                    masking.MaskPolynomial,
                };

                if (masking.MaskDefinition is not null) {
                    inputs.Add(masking.MaskDefinition);
                }

                return inputs;
            }

            default:
                return Array.Empty<LinearPolynomial<Flt64>>();
        }
    }

    /// <summary>辅助列 / The helper columns.</summary>
    public static IReadOnlyList<IVariableItem> HelperVariablesOrEmpty(
        this DeferredFunctionStructure structure) =>
        structure switch {
            // **`IndicatorStructure` 的辅助列（零带方向列、等价列、合取/差值结果）已经由
            // `RetainedResultVariables` 报出，这里不再重复——否则同一个列会被登记两次。**
            //
            // **`IndicatorStructure`'s helper columns (the zero-band side, the equivalents, the conjunction/difference
            // results) are already reported by `RetainedResultVariables`; repeating them here would register one column
            // twice.**
            IndicatorStructure => Array.Empty<IVariableItem>(),
            AbsStructure abs => abs.HelperVariables,
            MaxStructure max => max.SelectorVariables,
            SemiStructure semi => semi.HelperVariables,
            UnivariateLinearPiecewiseStructure piecewise => piecewise.HelperVariables,
            MaskingStructure => Array.Empty<IVariableItem>(),
            BinaryLogicStructure => Array.Empty<IVariableItem>(),
            _ => Array.Empty<IVariableItem>(),
        };

    /// <summary>
    /// **把一个 `Abs` 函数符号投影成它的延迟结构快照** /
    /// Project an `Abs` function symbol into its deferred structure snapshot.
    /// </summary>
    /// <remarks>
    /// 已有的是"<b>真实函数符号 → 结构快照</b>"这条路。**在此之前，此前的所有判据都手工构造
    /// `DeferredFunctionStructure`，而生产路径上<b>没有任何地方</b>构造它。**
    ///
    /// **这个投影之所以几乎是机械的，是因为 `AbsFunction` 早就把物化需要的东西全带在身上了：**
    /// **`ResultVar`、`PositiveVar`、`NegativeVar`、`SignVar`、两个 Big-M、输入多项式。**
    /// **它们在大 M 推导时就被创建并设好了范围（`ResultVar` 与 `PositiveVar` 都是非负的）——
    /// 这正是"辅助变量必须非负"这条契约在生产路径上不会踩坑的原因。**
    ///
    /// **转换器不由函数符号提供，由调用方传入：** **它是模型级的（`metaModel.Converter`），
    /// 而一个函数符号不拥有模型。** **把它作为参数写出来，比让函数符号回去问模型更不容易出错。**
    ///
    /// **返回可空：** **`AbsStructure` 只对 `Flt64` 存在，所以这个扩展只作用于 `AbsFunction(&lt;Flt64&gt;)`。
    /// 别的数值类型没有对应的结构快照，那里的延迟展开还没有去处——这一点写在签名里而不是注释里。**
    ///
    /// **The "real function symbol → structure snapshot" road starts here.** **Before it, every
    /// criteria built `DeferredFunctionStructure` by hand while <b>nothing</b> on the production path built one.**
    ///
    /// **The projection is almost mechanical because `AbsFunction` already carries everything materialisation needs:
    /// `ResultVar`, `PositiveVar`, `NegativeVar`, `SignVar`, both Big-M values and the input polynomial.** **They are
    /// created during Big-M inference with ranges already set (`ResultVar` and `PositiveVar` are non-negative) — which is
    /// exactly why the earlier "helpers must be non-negative" contract cannot be hit on the production path.**
    ///
    /// **The converter comes from the caller, not the function symbol: it is model-level (`metaModel.Converter`) and a
    /// function symbol owns no model.** **Making it an explicit parameter is harder to get wrong than having the symbol ask
    /// its model.**
    ///
    /// **It returns non-null only for `Flt64`, because `AbsStructure` exists only there.** **Other numeric types have no
    /// structure snapshot and therefore nowhere for deferred expansion to go — stated in the signature, not in a comment.**
    /// </remarks>
    /// <param name="function">ABS 函数符号 / The ABS function symbol.</param>
    /// <param name="converter">模型级数值转换器 / The model-level value converter.</param>
    /// <returns>延迟结构快照 / The deferred structure snapshot.</returns>
    public static AbsStructure ToDeferredStructure(
        this AbsFunction<Flt64> function,
        IIntoValue<Flt64> converter) {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(converter);

        return new AbsStructure(
            Input: function.Polynomial,
            ResultVariable: function.ResultVar,
            PositiveVariable: function.PositiveVar,
            NegativeVariable: function.NegativeVar,
            SignVariable: function.SignVar,
            PositiveBigM: function.PositiveBigM,
            NegativeBigM: function.NegativeBigM,
            Converter: converter,
            Name: function.Name);
    }

    /// <summary>
    /// **把 `Max`/`Min` 函数符号投影成它的延迟结构快照** /
    /// Project a `Max`/`Min` function symbol into its deferred structure snapshot.
    /// </summary>
    /// <remarks>
    /// **与 `Abs` 不同，这里有两个地方不是机械搬运：**
    ///
    /// **① Big-M 要<b>现算</b>。** **`MaxFunction` 只留了 `ExplicitBigM`（可能为 null），
    /// 而真正用的那组是构造时算出来的、没存下来。**
    /// **于是投影必须用同一个 `BigMs` 静态方法重算一遍——而"同一个方法"是重点：
    /// 换一种算法就会得出另一组大 M，而模型依然能解，只是边界不同。**
    ///
    /// **② `Min` 与 `Max` 是同一个类的两个构造（`_minimum`），所以投影要读 `IsMinimum`，
    /// 否则 `min` 会被当成 `max` 物化。**
    /// **而 `MaxStructure` 里正好有这个开关（`Minimum`），所以这件事在结构侧是支持的、
    /// 在投影侧忘掉就会静默出错。**
    ///
    /// **Unlike `Abs`, two things here are not mechanical:**
    ///
    /// **① The Big-M values must be <b>recomputed</b>.** **`MaxFunction` keeps only `ExplicitBigM` (possibly null); the
    /// values it actually used were computed in the constructor and not stored.** **So the projection recomputes them with
    /// the same `BigMs` static — "the same method" being the point: a different formula yields different Big-M values, and
    /// the model still solves, merely with different bounds.**
    ///
    /// **② `Min` and `Max` are two constructions of one class (`_minimum`), so the projection must read `IsMinimum`, or a
    /// `min` gets materialised as a `max`.** **`MaxStructure` already has that switch (`Minimum`), so it is supported on the
    /// structure side and silently wrong if forgotten on the projection side.**
    /// </remarks>
    /// <param name="function">极值函数符号 / The extremum function symbol.</param>
    /// <param name="converter">模型级数值转换器 / The model-level value converter.</param>
    /// <returns>延迟结构快照 / The deferred structure snapshot.</returns>
    public static MaxStructure ToDeferredStructure(
        this ExtremumFunctionBase<Flt64> function,
        IIntoValue<Flt64> converter) {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(converter);

        IReadOnlyList<Flt64> bigMs = ExtremumValues.BigMs(
            polynomials: function.Polynomials,
            explicitBigM: function.ExplicitBigM,
            converter: converter,
            minimum: function.IsMinimum) ?? throw new InvalidOperationException(
            "the extremum function has no finite Big-M values; it cannot be materialised");

        return new MaxStructure(
            Inputs: function.Polynomials,
            ResultVariable: function.ResultVar,
            SelectorVariables: function.SelectorVars,
            BigMValues: bigMs,
            Converter: converter,
            Name: function.Name) {
            Minimum = function.IsMinimum,
        };
    }

    /// <summary>
    /// **把 `Semi` 函数符号投影成它的延迟结构快照** /
    /// Project a `Semi` function symbol into its deferred structure snapshot.
    /// </summary>
    /// <remarks>
    /// **这个投影确实是机械的：** **`SemiFunction` 存了 `LowerBound`、`UpperBound`、`ResultVar`、`IndicatorVar`，
    /// 与 `SemiStructure` 的五个字段一一对应。**
    ///
    /// **它值得单独写一个扩展，只因为它<b>是</b>机械的：** **如果连这一种都要在构建路径里特殊处理，
    /// 那说明投影这套东西的形状不对。**
    ///
    /// **This projection really is mechanical: `SemiFunction` stores `LowerBound`, `UpperBound`, `ResultVar` and
    /// `IndicatorVar`, matching `SemiStructure`'s five fields one for one.**
    ///
    /// **It gets its own extension precisely <b>because</b> it is mechanical: if even this one needed special handling in the
    /// build path, the shape of the projection mechanism would be wrong.**
    /// </remarks>
    /// <param name="function">半连续函数符号（非泛型）/ The semi-continuous function symbol (non-generic).</param>
    /// <param name="converter">模型级数值转换器 / The model-level value converter.</param>
    /// <returns>延迟结构快照 / The deferred structure snapshot.</returns>
    public static SemiStructure ToDeferredStructure(
        this SemiFunction function,
        IIntoValue<Flt64> converter) {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(converter);

        return new SemiStructure(
            LowerBound: function.LowerBound,
            UpperBound: function.UpperBound,
            ResultVariable: function.ResultVar,
            IndicatorVariable: function.IndicatorVar,
            Converter: converter,
            Name: function.Name);
    }

    /// <summary>
    /// **把 `And` 函数符号投影成延迟结构 / Project an `And` symbol into a deferred structure.**
    /// </summary>
    /// <remarks>
    /// **四个逻辑函数符号（`And`／`Or`／`Not`／`Xor`）是四个<b>互不相关的 sealed 类</b>，
    /// 所以没有共同的基类可用。** **四个扩展各自一行，body 共用一个 helper。
    ///
    /// **返回可空，因为 `BinaryInputs` 本身可空：** **当输入里有一个不是二元变量时，
    /// `AndFunction` 会把 `BinaryInputs` 置为 null。**
    /// **而 `BinaryLogicStructure` 只能表示二元输入的逻辑——所以这时<b>没有投影</b>，
    /// 该符号继续走急切注册。**
    ///
    /// **这个"返回 null 而不是抛"的选择是有意的：** **"这种结构表示不了它"不是错误，
    /// 它只是意味着延迟这条路由这个符号走不通，而急切那条路一直可用。**
    ///
    /// **The four logic symbols (`And`/`Or`/`Not`/`Xor`) are four <b>unrelated sealed classes</b>, so there is no common
    /// base to hook. Each extension is one line; the body is shared.**
    ///
    /// **They return null because `BinaryInputs` is itself nullable: when an input is not a binary variable,
    /// `AndFunction` sets it to null.** **`BinaryLogicStructure` can only describe binary inputs, so there is <b>no
    /// projection</b> in that case and the symbol keeps its eager registration.**
    ///
    /// **Returning null rather than throwing is deliberate: "this structure cannot represent it" is not an error, it merely
    /// means the deferred road is closed for that symbol while the eager road stays open.**
    /// </remarks>
    /// <param name="function">`And` 函数符号 / The `And` function symbol.</param>
    /// <param name="converter">模型级数值转换器 / The model-level value converter.</param>
    /// <returns>延迟结构快照，表示不了时为 `null` / The snapshot, or null when it cannot be represented.</returns>
    public static BinaryLogicStructure? ToDeferredStructure(
        this AndFunction function,
        IIntoValue<Flt64> converter) =>
        BinaryLogicDeferred(function.BinaryInputs, function.ResultVar, BinaryLogicOperation.And,
            converter, function.Name);

    /// <summary>**把 `Or` 函数符号投影成延迟结构** / Project an `Or` symbol into a deferred structure.</summary>
    /// <param name="function">`Or` 函数符号 / The `Or` function symbol.</param>
    /// <param name="converter">模型级数值转换器 / The model-level value converter.</param>
    /// <returns>延迟结构快照，表示不了时为 `null` / The snapshot, or null when it cannot be represented.</returns>
    public static BinaryLogicStructure? ToDeferredStructure(
        this OrFunction function,
        IIntoValue<Flt64> converter) =>
        BinaryLogicDeferred(function.BinaryInputs, function.ResultVar, BinaryLogicOperation.Or,
            converter, function.Name);

    /// <summary>**把 `Not` 函数符号投影成延迟结构** / Project a `Not` symbol into a deferred structure.</summary>
    /// <remarks>
    /// **`Not` 只有一个输入，而它的二元输入存的是<b>单个</b> `BinaryInput`（不是列表）。
    /// 所以这里要包一层单元素数组——而这一处差异正是"四个类不共用基类"的代价。**
    ///
    /// **`Not` has one input and stores a <b>single</b> `BinaryInput` rather than a list, so it gets wrapped in a
    /// single-element array — a difference that is exactly the price of the four classes sharing no base.**
    /// </remarks>
    /// <param name="function">`Not` 函数符号 / The `Not` function symbol.</param>
    /// <param name="converter">模型级数值转换器 / The model-level value converter.</param>
    /// <returns>延迟结构快照，表示不了时为 `null` / The snapshot, or null when it cannot be represented.</returns>
    public static BinaryLogicStructure? ToDeferredStructure(
        this NotFunction function,
        IIntoValue<Flt64> converter) =>
        BinaryLogicDeferred(
            function.BinaryInput is { } input ? new[] { input } : null,
            function.ResultVar,
            BinaryLogicOperation.Not,
            converter,
            function.Name);

    // **`Xor` 没有投影，是一个具体发现而不是遗漏：**
    // `XorFunction` 只存 `Polynomials`、`ResultVar`、`IndicatorVars`、`SideVars`、`Tolerance`、`StrictBoundary`,
    // **而 `BinaryLogicStructure` 要的是"二元输入列表"** —— `And`／`Or`／`Not` 都存了
    // （`BinaryInputs`／`BinaryInput`），**只有 `Xor` 没存。**
    //
    // **所以这条路在符号那一侧就断了：不是投影没写，是符号没保留物化需要的东西。**
    // 补它要先在 `XorFunction` 里把二元输入存下来，那是另一件事（会牵动 XOR 的急切约束生成）。
 // **而 XOR 在 n ≥ 3 时本身还有一条基线缺陷，所以这一处不急着动。**
    //
    // **`Xor` has no projection, and that is a finding rather than an omission: `XorFunction` stores only
    // `Polynomials`, `ResultVar`, `IndicatorVars`, `SideVars`, `Tolerance` and `StrictBoundary`, while
    // `BinaryLogicStructure` wants a binary-input list — which `And`/`Or`/`Not` keep and `Xor` does not.**
    //
    // **So the road is cut on the symbol side: the projection is not missing, the symbol did not keep what
    // materialisation needs.** Restoring it means storing the binary inputs in `XorFunction`, which is another change (it
    // touches XOR's eager row generation). **And XOR already carries a baseline defect at n >= 3 (recorded), so
    // this is not urgent.**

    /// <summary>四种逻辑投影共用的 body / The body shared by the four logic projections.</summary>
    /// <param name="inputs">二元输入，可能为 null / The binary inputs, possibly null.</param>
    /// <param name="result">结果列 / The result column.</param>
    /// <param name="operation">运算种类 / The operation.</param>
    /// <param name="converter">模型级数值转换器 / The model-level value converter.</param>
    /// <param name="name">名称 / The name.</param>
    /// <returns>延迟结构快照，表示不了时为 `null` / The snapshot, or null when it cannot be represented.</returns>
    private static BinaryLogicStructure? BinaryLogicDeferred(
        IReadOnlyList<BinVar>? inputs,
        IVariableItem result,
        BinaryLogicOperation operation,
        IIntoValue<Flt64> converter,
        string name) =>
        inputs is null
            ? null
            : new BinaryLogicStructure(
                Operation: operation,
                Inputs: BinaryInputsOf(inputs),
                ResultVariable: result,
                Converter: converter,
                Name: name);

    /// <summary>
    /// **把二元列列表转成变量列表 / Turn the binary-column list into a variable list.**
    /// </summary>
    /// <remarks>
    /// **不用 `Select`：本仓的 `Result` 扩展也定义了一个 `Select`，类型推断会撞上它（CS0411）。
    /// 一个显式循环既避开重载冲突，也把"这里没有查询语义"写清楚。**
    ///
    /// **Not `Select`: this repo's `Result` extensions define one too and type inference collides with it (CS0411). An
    /// explicit loop sidesteps the overload and states that no query semantics are involved.**
    /// </remarks>
    /// <param name="inputs">二元输入 / The binary inputs.</param>
    /// <returns>变量列表 / The variable list.</returns>
    private static IReadOnlyList<IVariableItem> BinaryInputsOf(IReadOnlyList<BinVar> inputs) {
        var variables = new List<IVariableItem>(inputs.Count);
        foreach (BinVar input in inputs) {
            variables.Add(input);
        }
        return variables;
    }

    /// <summary>是否支持延迟展开 / Whether deferred expansion is supported.</summary>
    public static bool SupportsDeferredFallback(this DeferredFunctionStructure structure) =>
        structure switch {
            IndicatorStructure => true,
            AbsStructure => true,
            MaxStructure => true,
            SemiStructure => true,
            UnivariateLinearPiecewiseStructure => true,
            MaskingStructure => true,
            BinaryLogicStructure => true,
            _ => false,
        };
}
