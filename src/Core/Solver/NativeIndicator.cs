#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// <summary>互斥条件差值 / Exclusive condition difference.</summary>
/// <param name="Condition">减数条件 / The subtracted condition.</param>
/// <param name="ResultKey">保留结果键 / Retained result key.</param>
/// <param name="Name">链接名称 / Link name.</param>
public sealed record NativeDifferenceIndicatorData(
    NativeIndicatorData Condition,
    VariableItemKey ResultKey,
    string Name);

/// <summary>两个重化条件的合取结果 / Conjunction of two reified conditions.</summary>
/// <param name="Condition">第二个条件 / The second condition.</param>
/// <param name="ResultKey">保留的合取结果键 / Retained conjunction result key.</param>
/// <param name="Name">链接约束名称 / Link constraint name.</param>
public sealed record NativeConjoinedIndicatorData(
    NativeIndicatorData Condition,
    VariableItemKey ResultKey,
    string Name);

/// <summary>已验证的零容差带 / Validated zero-tolerance band.</summary>
/// <param name="Tolerance">带内绝对值上界 / Inside-band absolute-value limit.</param>
/// <param name="SideKey">保留方向列的键 / Retained side column key.</param>
public sealed record NativeZeroBandData(double Tolerance, VariableItemKey SideKey);

/// <summary>已验证的双向正数指示 / Validated bidirectional positive indicator.</summary>
/// <param name="Terms">仿射系数 / Affine coefficients.</param>
/// <param name="Constant">仿射常数 / Affine constant.</param>
/// <param name="Tolerance">真分支阈值 / True-branch threshold.</param>
/// <param name="BigM">原 fallback 的范围参数 / Original fallback range parameter.</param>
/// <param name="ResultKey">保留的二值结果键 / Retained binary result key.</param>
/// <param name="Name">名称 / Name.</param>
/// <param name="PositiveOnZero">由结果 0 激活正分支 / Activate the positive branch on result zero.</param>
/// <param name="LowerBound">原 fallback 输入下界 / Original fallback input lower bound.</param>
/// <param name="UpperBound">原 fallback 输入上界 / Original fallback input upper bound.</param>
/// <param name="EquivalentResultKeys">保留的等价结果列键 / Retained equivalent result column keys.</param>
/// <param name="ConditionalValue">条件数值结果 / Conditional numeric result.</param>
/// <param name="ImpliedCondition">仅由前件激活的后件数据 / Consequent data activated only by the antecedent.</param>
/// <param name="ZeroBand">零容差带及保留方向列 / Zero band and retained side column.</param>
/// <param name="Conjunction">第二个条件及保留的 AND 结果 / Second condition and retained AND result.</param>
/// <param name="Difference">第二个条件及互斥差值结果 / Second condition and exclusive difference result.</param>
public sealed record NativeIndicatorData(
    IReadOnlyDictionary<VariableItemKey, double> Terms,
    double Constant,
    double Tolerance,
    double BigM,
    VariableItemKey ResultKey,
    string Name,
    bool PositiveOnZero = false,
    double LowerBound = 0.0,
    double UpperBound = 0.0,
    IReadOnlyList<VariableItemKey>? EquivalentResultKeys = null,
    NativeConditionalValueData? ConditionalValue = null,
    NativeConditionalValueData? ImpliedCondition = null,
    NativeZeroBandData? ZeroBand = null,
    NativeConjoinedIndicatorData? Conjunction = null,
    NativeDifferenceIndicatorData? Difference = null);

/// <summary>
/// 指示结构的原生准备 / Native preparation of an indicator structure.
/// </summary>
/// <remarks>
/// 对齐的 `prepareNativeIndicator` / `prepareIndicator`。
///
/// **这一格的核心是一张<b>保留列集合</b>，而它必须在递归<b>之前</b>算出来。**
///
/// **`resultKeys` 收集的是"这一族里必须被保留、且不能出现在输入侧的列"：**
///
/// | 来源 | 收进 `resultKeys` 的 |
/// | --- | --- |
/// | **等价结果列** | **每个被声明的等价结果** |
/// | **结果列本身** | **`ResultVariable`** |
/// | **零带的侧列** | **`ZeroBand.SideVariable`** |
/// | **合取的第二个条件结果** | **`Conjunction.Condition.ResultVariable`** |
/// | **合取结果** | **`Conjunction.ResultVariable`** |
/// | **差值的第二个条件结果** | **`Difference.Condition.ResultVariable`** |
/// | **差值结果** | **`Difference.ResultVariable`** |
///
/// **然后约束是双向的：**
///
/// - **递归的子条件，其 `terms` 不能碰到 `resultKeys`**（否则子条件在读自己的输出）
/// - **本结构的输入项，其变量不能是 `resultKeys`、`conditionalValue.ResultKey`、
///   或 `impliedCondition.ResultKey`**（否则主条件在读它自己这一族的输出）
///
/// **为什么必须<b>先</b>算 `resultKeys` 再递归：** **子条件的合法性取决于
/// "它有没有碰到本族的保留列"，而那个集合只有在看完了合取/差值/零带的声明之后才完整。**
/// **先递归再收集，就会让一个子条件在还不知道自己不许碰哪些列的情况下通过检查。**
///
/// **两张集合的区别很细但很实际：** **`conditionalValue.ResultKey` 与
/// `impliedCondition.ResultKey` <b>不在</b> `resultKeys` 里**
/// **（它们不参与子条件与合取/差值那一族的检查），
/// 但它们被<b>单独</b>列进主输入的禁碰名单。**
///
/// **所以一个变量可以同时是"条件数值结果"与"某一族的等价结果"而不冲突——
/// 而它如果是主输入的项就会被拒。** **这条不对称照做。**
///
/// **The heart of this cell is a <b>retained-column set</b>, and it must be computed <b>before</b> recursing.**
///
/// **`resultKeys` collects the columns of this family that must be retained and must not appear on an input side** (see
/// the table above).
///
/// **Then the constraint is two-way: a recursive sub-condition's `terms` must not touch `resultKeys` (a sub-condition
/// reading its own output), and this structure's input variables must not be any of `resultKeys`,
/// `conditionalValue.ResultKey` or `impliedCondition.ResultKey` (the main condition reading its own family's output).**
///
/// **Why `resultKeys` must be complete <b>before</b> the recursion: a sub-condition's legality depends on whether it
/// touches this family's retained columns, and that set is only complete after the conjunction/difference/zero-band
/// declarations are read.** **Recursing first would let a sub-condition pass before knowing which columns it must not
/// touch.**
///
/// **The difference between the two sets is subtle but real: `conditionalValue.ResultKey` and
/// `impliedCondition.ResultKey` are <b>not</b> in `resultKeys` (they take no part in the sub-condition or
/// conjunction/difference checks) but are listed <b>separately</b> in the main input's forbidden list.**
///
/// **So a variable can be both a conditional-value result and some family's equivalent result without conflict — while
/// the same variable as a main-input term is refused.** **This asymmetry is copied from the code.**
/// </remarks>
public static class NativeIndicator
{
    /// <summary>准备一个指示结构 / Prepare one indicator structure.</summary>
    public static Result<NativeIndicatorData, ErrorCode, Error<ErrorCode>> Prepare(
        IndicatorStructure structure,
        double maximumMagnitude = double.PositiveInfinity) {
        ArgumentNullException.ThrowIfNull(structure);

        // **先让结构自己生成约束。**
        // **Let the structure emit its own rows first.**
        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> constraints =
            structure.GenerateConstraints();
        if (constraints is Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> bad) {
            return new Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>(
                bad.Error.Code, bad.Error.Message);
        }

        IIntoValue<Flt64> converter = structure.Converter;

        double constant = ToSolverDouble(converter.FromValue(structure.Input.Constant));
        double tolerance = ToSolverDouble(converter.FromValue(structure.Tolerance));
        double bigM = ToSolverDouble(converter.FromValue(structure.BigM));

        double allowedLower = structure.ConditionBounds is null
            ? -bigM
            : ToSolverDouble(converter.FromValue(structure.ConditionBounds.Lower));
        double allowedUpper = structure.ConditionBounds is null
            ? bigM
            : ToSolverDouble(converter.FromValue(structure.ConditionBounds.Upper));

        if (!Usable(allowedLower, maximumMagnitude) ||
            !Usable(allowedUpper, maximumMagnitude) || allowedLower > allowedUpper) {
            return Fail();
        }

        // **`tolerance <= 0` 与 `bigM <= 0` 都拒：** **一个非正的阈值让"真分支"无从定义，
        // 一个非正的 M 让 fallback 无从松弛。**
        //
        // **`tolerance <= 0` and `bigM <= 0` are both refused: a non-positive threshold leaves the true branch undefined,
        // and a non-positive M cannot relax the fallback.**
        if (!Usable(constant, maximumMagnitude) || !Usable(tolerance, maximumMagnitude) ||
            !Usable(bigM, maximumMagnitude) || tolerance <= 0.0 || bigM <= 0.0 ||
            !Usable(tolerance - constant, maximumMagnitude) ||
            !Usable(-constant, maximumMagnitude)) {
            return Fail();
        }

        NativeZeroBandData? zeroBand = null;
        if (structure.ZeroBand is not null) {
            double bandTolerance = ToSolverDouble(
                converter.FromValue(structure.ZeroBand.Tolerance));

            // **带容差必须 `0 <= band < tolerance`（严格小于）。**
            // **`band == tolerance` 会让"带内"与"真分支"重合，于是那条边界既属于带内又属于真分支。**
            //
            // **The band tolerance must satisfy `0 <= band < tolerance`, strictly.** **`band == tolerance` makes the band
            // and the true branch coincide, so the boundary belongs to both.**
            if (!Usable(bandTolerance, maximumMagnitude) || bandTolerance < 0.0 ||
                bandTolerance >= tolerance ||
                !Usable(tolerance + bandTolerance, maximumMagnitude) ||
                !Usable(-tolerance - constant, maximumMagnitude) ||
                !Usable(bandTolerance - constant, maximumMagnitude) ||
                !Usable(-bandTolerance - constant, maximumMagnitude)) {
                return Fail();
            }

            zeroBand = new NativeZeroBandData(bandTolerance, structure.ZeroBand.SideVariable.Key);
        }

        // **保留列集合：必须在递归之前完整。**
        // **The retained-column set must be complete before recursing.**
        var resultKeys = new HashSet<VariableItemKey>();
        foreach (IVariableItem equivalent in structure.EquivalentResults) {
            resultKeys.Add(equivalent.Key);
        }

        resultKeys.Add(structure.ResultVariable.Key);

        if (zeroBand is not null) {
            resultKeys.Add(zeroBand.SideKey);
        }

        if (structure.Conjunction is not null) {
            resultKeys.Add(structure.Conjunction.Condition.ResultVariable.Key);
            resultKeys.Add(structure.Conjunction.ResultVariable.Key);
        }

        if (structure.Difference is not null) {
            resultKeys.Add(structure.Difference.Condition.ResultVariable.Key);
            resultKeys.Add(structure.Difference.ResultVariable.Key);
        }

        NativeDifferenceIndicatorData? difference = null;
        if (structure.Difference is not null) {
            Result<NativeIndicatorData, ErrorCode, Error<ErrorCode>> condition =
                Prepare(structure.Difference.Condition, maximumMagnitude);
            if (condition is Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>> badDifference) {
                return new Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>(
                    badDifference.Error.Code, badDifference.Error.Message);
            }

            NativeIndicatorData prepared =
                ((Ok<NativeIndicatorData, ErrorCode, Error<ErrorCode>>)condition).Value;

            if (TouchesAny(prepared.Terms, resultKeys)) {
                return Fail();
            }

            difference = new NativeDifferenceIndicatorData(
                prepared, structure.Difference.ResultVariable.Key, structure.Difference.Name);
        }

        NativeConjoinedIndicatorData? conjunction = null;
        if (structure.Conjunction is not null) {
            Result<NativeIndicatorData, ErrorCode, Error<ErrorCode>> condition =
                Prepare(structure.Conjunction.Condition, maximumMagnitude);
            if (condition is Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>> badConjunction) {
                return new Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>(
                    badConjunction.Error.Code, badConjunction.Error.Message);
            }

            NativeIndicatorData prepared =
                ((Ok<NativeIndicatorData, ErrorCode, Error<ErrorCode>>)condition).Value;

            if (TouchesAny(prepared.Terms, resultKeys)) {
                return Fail();
            }

            conjunction = new NativeConjoinedIndicatorData(
                prepared, structure.Conjunction.ResultVariable.Key, structure.Conjunction.Name);
        }

        NativeConditionalValueData? conditionalValue = null;
        if (structure.ConditionalValue is not null) {
            if (resultKeys.Contains(structure.ConditionalValue.ResultVariable.Key)) {
                return Fail();
            }

            Result<NativeConditionalValueData, ErrorCode, Error<ErrorCode>> prepared =
                NativeMasking.PrepareConditionalValue(
                    structure.ConditionalValue, converter, maximumMagnitude);
            if (prepared is Failed<NativeConditionalValueData, ErrorCode, Error<ErrorCode>> badValue) {
                return new Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>(
                    badValue.Error.Code, badValue.Error.Message);
            }

            conditionalValue =
                ((Ok<NativeConditionalValueData, ErrorCode, Error<ErrorCode>>)prepared).Value;
        }

        NativeConditionalValueData? impliedCondition = null;
        if (structure.ImpliedCondition is not null) {
            // **`positiveOnZero` 与蕴含后件不能并存：** **前者让结果 0 激活正分支，
            // 后者让前件激活后件——两者对同一个 0 给出相反的约定。**
            //
            // **`positiveOnZero` and an implied consequent cannot coexist: the former activates the positive branch on
            // result zero, the latter activates the consequent — two opposite conventions for the same zero.**
            if (structure.PositiveOnZero ||
                resultKeys.Contains(structure.ImpliedCondition.IndicatorVariable.Key) ||
                structure.ImpliedCondition.IndicatorVariable.Key == conditionalValue?.ResultKey) {
                return Fail();
            }

            // **后件复用 `ConditionalValue` 的准备函数，只是把结果变量换成指示变量。**
            // **The consequent reuses the conditional-value preparation with the indicator as its result.**
            Result<NativeConditionalValueData, ErrorCode, Error<ErrorCode>> prepared =
                NativeMasking.PrepareConditionalValue(
                    new ConditionalValue(
                        Input: structure.ImpliedCondition.Input,
                        ResultVariable: structure.ImpliedCondition.IndicatorVariable,
                        Bounds: structure.ImpliedCondition.Bounds,
                        Name: structure.ImpliedCondition.Name),
                    converter, maximumMagnitude);
            if (prepared is Failed<NativeConditionalValueData, ErrorCode, Error<ErrorCode>> badImplied) {
                return new Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>(
                    badImplied.Error.Code, badImplied.Error.Message);
            }

            impliedCondition =
                ((Ok<NativeConditionalValueData, ErrorCode, Error<ErrorCode>>)prepared).Value;
        }

        // **后件的容差与保留列检查。**
        // **`tolerance − impliedCondition.Constant` 必须可用：那是后件常数与阈值的关系，
        // 它决定了"前件成立时"那条分支是否还有余量。**
        //
        // **The consequent's tolerance and retained-column checks.**
        // **`tolerance - impliedCondition.Constant` must be usable: it is the relation between the consequent's constant
        // and the threshold, and decides whether the "antecedent holds" branch still has room.**
        if (impliedCondition is not null) {
            if (!Usable(tolerance - impliedCondition.Constant, maximumMagnitude) ||
                TouchesAny(impliedCondition.Terms, resultKeys)) {
                return Fail();
            }

            if (conditionalValue is not null &&
                impliedCondition.Terms.ContainsKey(conditionalValue.ResultKey)) {
                return Fail();
            }
        }

        var terms = new Dictionary<VariableItemKey, double>();
        var variables = new Dictionary<VariableItemKey, IVariableItem>();

        foreach (LinearMonomial<Flt64> monomial in structure.Input.Monomials) {
            if (monomial.Symbol is not IVariableItem variable) {
                return Fail();
            }

            if (resultKeys.Contains(variable.Key) ||
                variable.Key == conditionalValue?.ResultKey ||
                variable.Key == impliedCondition?.ResultKey) {
                return Fail();
            }

            double coefficient = ToSolverDouble(converter.FromValue(monomial.Coefficient));
            double combined = (terms.TryGetValue(variable.Key, out double existing) ? existing : 0.0)
                + coefficient;

            if (!Usable(coefficient, maximumMagnitude) || !Usable(combined, maximumMagnitude)) {
                return Fail();
            }

            terms[variable.Key] = combined;
            variables[variable.Key] = variable;
        }

        double lower = constant;
        double upper = constant;

        foreach (KeyValuePair<VariableItemKey, double> term in terms) {
            if (term.Value == 0.0) {
                continue;
            }

            IVariableItem variable = variables[term.Key];

            if (variable.LowerBound is null || variable.UpperBound is null) {
                return Fail();
            }

            double variableLower = ToSolverDouble(variable.LowerBound.Value.ToFlt64());
            double variableUpper = ToSolverDouble(variable.UpperBound.Value.ToFlt64());

            if (!Usable(variableLower, maximumMagnitude) ||
                !Usable(variableUpper, maximumMagnitude) ||
                variableLower > variableUpper) {
                return Fail();
            }

            lower += term.Value * (term.Value > 0 ? variableLower : variableUpper);
            upper += term.Value * (term.Value > 0 ? variableUpper : variableLower);

            if (!Usable(lower, maximumMagnitude) || !Usable(upper, maximumMagnitude)) {
                return Fail();
            }
        }

        if (lower < allowedLower || upper > allowedUpper) {
            return Fail();
        }

        var equivalents = new List<VariableItemKey>(structure.EquivalentResults.Count);
        foreach (IVariableItem equivalent in structure.EquivalentResults) {
            equivalents.Add(equivalent.Key);
        }

        return new Ok<NativeIndicatorData, ErrorCode, Error<ErrorCode>>(
            new NativeIndicatorData(
                Terms: NonZero(terms),
                Constant: constant,
                Tolerance: tolerance,
                BigM: bigM,
                ResultKey: structure.ResultVariable.Key,
                Name: structure.Name,
                PositiveOnZero: structure.PositiveOnZero,
                LowerBound: allowedLower,
                UpperBound: allowedUpper,
                EquivalentResultKeys: equivalents,
                ConditionalValue: conditionalValue,
                ImpliedCondition: impliedCondition,
                ZeroBand: zeroBand,
                Conjunction: conjunction,
                Difference: difference));
    }

    private static bool TouchesAny(
        IReadOnlyDictionary<VariableItemKey, double> terms, HashSet<VariableItemKey> keys) {
        foreach (VariableItemKey key in terms.Keys) {
            if (keys.Contains(key)) {
                return true;
            }
        }

        return false;
    }

    private static double ToSolverDouble(Flt64 value) => value.ToSolverDouble();

    private static bool Usable(double value, double maximumMagnitude) =>
        double.IsFinite(value) &&
        global::System.Math.Abs(value) <
            global::System.Math.Min(maximumMagnitude, double.MaxValue);

    private static IReadOnlyDictionary<VariableItemKey, double> NonZero(
        Dictionary<VariableItemKey, double> terms) {
        var kept = new Dictionary<VariableItemKey, double>(terms.Count);
        foreach (KeyValuePair<VariableItemKey, double> term in terms) {
            if (term.Value != 0.0) {
                kept[term.Key] = term.Value;
            }
        }

        return kept;
    }

    private static Result<NativeIndicatorData, ErrorCode, Error<ErrorCode>> Fail() =>
        new Failed<NativeIndicatorData, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, "invalid native indicator values or bounds");
}
