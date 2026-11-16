#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Mechanism;
// ===== IMetaConstraintGroup =====

/// <summary>
/// 元约束组接口 / Meta constraint group interface.
/// </summary>
public interface IMetaConstraintGroup {
    bool Lazy { get; }
    string Name { get; }
}

// ===== MetaConstraintGroupExtensions =====

/// <summary>
/// IMetaConstraintGroup / IMetaModel DSL 扩展方法 /
/// DSL extension methods on IMetaModel&lt;V&gt;.
/// </summary>
public static class MetaConstraintGroupExtensions {
    public static void RegisterConstraintGroup<V>(this IMetaModel<V> model, IMetaConstraintGroup group)
        where V : struct, IRealNumber<V>, INumberField<V> => model.RegisterConstraintGroup(group);

    public static Range? IndicesOfConstraintGroup<V>(this IMetaModel<V> model, IMetaConstraintGroup group)
        where V : struct, IRealNumber<V>, INumberField<V> => model.IndicesOfConstraintGroup(group);

    public static IReadOnlyList<MathConstraint> ConstraintsOfGroup<V>(this IMetaModel<V> model, IMetaConstraintGroup group)
        where V : struct, IRealNumber<V>, INumberField<V> => model.ConstraintsOfGroup(group);

    // group-scoped AddConstraint overloads for linear

    public static Try AddConstraint<V>(
        this IAbstractLinearMetaModel<V> model,
        IMetaConstraintGroup group,
        IVariableItem constraint,
        bool? lazy = null,
        string? name = null,
        string? displayName = null,
        object? args = null,
        bool? withRangeSet = false)
        where V : struct, IRealNumber<V>, INumberField<V> => model.AddConstraint(constraint, group, lazy ?? group.Lazy, name, displayName, args, withRangeSet);

    public static Try AddConstraint<V>(
        this IAbstractLinearMetaModel<V> model,
        IMetaConstraintGroup group,
        LinearInequality<V> relation,
        bool? lazy = null,
        string? name = null,
        string? displayName = null,
        object? args = null,
        int? priority = null,
        bool? withRangeSet = false)
        where V : struct, IRealNumber<V>, INumberField<V> => model.AddConstraint(relation, group, lazy ?? group.Lazy, name, displayName, args, priority, withRangeSet);

    public static Try Partition<V>(
        this IAbstractLinearMetaModel<V> model,
        IMetaConstraintGroup group,
        LinearPolynomial<V> polynomial,
        bool? lazy = null,
        string? name = null,
        string? displayName = null,
        object? args = null)
        where V : struct, IRealNumber<V>, INumberField<V> => model.Partition(polynomial, group, lazy ?? group.Lazy, name, displayName, args);

    // group-scoped AddConstraint overloads for quadratic

    public static Try AddConstraint<V>(
        this IAbstractQuadraticMetaModel<V> model,
        IMetaConstraintGroup group,
        QuadraticInequalityOf<V> relation,
        bool? lazy = null,
        string? name = null,
        string? displayName = null,
        object? args = null,
        int? priority = null,
        bool? withRangeSet = null)
        where V : struct, IRealNumber<V>, INumberField<V> => model.AddConstraint(relation, group, lazy ?? group.Lazy, name, displayName, args, priority, withRangeSet);
}

// ===== MathConstraint =====

/// <summary>
/// 数学约束通用接口 / Common interface for math-based constraints.
/// </summary>
public interface MathConstraint {
    IMetaConstraintGroup? Group { get; }
    bool Lazy { get; }
    object? Args { get; }
    int Priority { get; }
    string Name { get; }
    string? DisplayName { get; }
}

/// <summary>机制模型约束到求解器约束的映射与对偶整理 / Mechanism constraint mapping and dual tidy helpers.</summary>
/// <remarks>
/// **`OriginMap` 与 `ToDualSolution` 在四个后端（COPT／CPLEX／Mindopt／SCIP）里逐字相同，
/// 连类型签名都相同：全部是 `LinearMechanismModel&lt;Flt64>` 与 `IConstraint&lt;Flt64, LinearCategory>`。**
/// **既然泛型特化<b>完全相同</b>，它们就只该有一份实现，所以集中到这里。**
///
/// **`OriginMap` and `ToDualSolution` are byte-identical across the four backends (COPT, CPLEX, Mindopt, SCIP), down to the
/// signatures: all `LinearMechanismModel&lt;Flt64>` and `IConstraint&lt;Flt64, LinearCategory>`.** **Since the generic
/// specialisation is <b>the same</b>, they can only have one implementation, and it lives here.**
/// </remarks>
public static class MathConstraintOrigins {
/// <summary>行号到来源约束的键解析 / Resolve a row index to its originating constraint key.</summary>
/// <remarks>
/// **四个后端各自的同一份 `Key` 实现集中到这里。**
///
/// **它们此前逐字相同（五行的三元表达式），分散在 COPT／CPLEX／Mindopt 的 `XxxDualSupport` 里，
/// 以及 SCIP 的 `ScipLpDualSupport` 里。**
///
/// **为什么必须容忍越界与空来源而不是抛错：** **`originMap` 的长度来自机制模型的约束数，
/// 而对偶向量的长度可能来自求解器返回的<b>对偶模型</b>（列数 = 原行数 + 界对偶列 + 松弛列），
/// 两者本就不等。** **越界意味着"这一行没有对应的原约束"，而那不是错误——它是
/// 对偶模型比原模型多出来的那些行的正常状态。**
///
/// **所以这个函数<b>不返回 `Result`</b>：** **它回答的是"这个位置该用哪个键"，
/// 而每种位置都有答案（有来源就用来源，没有就造一个可识别的占位键）。**
/// **一个永远有答案的查询，用 `Result` 包装只会让四个调用方各写一遍解包。**
///
/// **Four byte-identical `Key` implementations live here instead of in each backend.**
///
/// **They were identical five-line ternary expressions spread across COPT/CPLEX/Mindopt's `XxxDualSupport` and SCIP's
/// `ScipLpDualSupport`.**
///
/// **Why out-of-range and empty origins must be tolerated rather than thrown on: `originMap`'s length comes from the
/// mechanism model's constraint count, while the dual vector's length may come from the solver's <b>dual model</b>
/// (columns = primal rows + bound-dual columns + slacks), so the two are not equal by construction.** **Out of range
/// means "this row has no originating constraint", which is not an error — it is the normal state of the rows a dual
/// model adds.**
///
/// **So this returns no `Result`: it answers "which key belongs at this position", and every position has an answer
/// (the origin when there is one, an identifiable placeholder otherwise).** **A query that always has an answer, wrapped
/// in `Result`, would only make four callers unwrap it.**
/// </remarks>
/// <param name="index">行号 / The row index.</param>
/// <param name="originMap">行号到来源约束的映射 / Row index to originating constraint.</param>
/// <returns>来源约束，或占位键 / The originating constraint, or a placeholder key.</returns>
public static MathConstraint ResolveOriginKey(int index, MathConstraint?[] originMap) =>
    index >= 0 && index < originMap.Length && originMap[index] is { } origin
        ? origin
        : new FallbackMathConstraint(index);

    /// <summary>把机制模型的约束来源收集成一个按行索引的映射 / Collect mechanism constraint origins into a row-indexed map.</summary>
    /// <remarks>
    /// **`Origin` 为 `null` 是正常状态：** **由机制生成的约束（松弛行、界行、归一化行）没有原始约束可指，
    /// 而它们仍然占一行。** **所以这个映射里允许空洞，`ResolveOriginKey` 正是为这些空洞准备占位键的。**
    ///
    /// **`Origin` being null is normal: constraints generated by the mechanism (slack rows, bound rows, normalisation
    /// rows) have no original to point at, yet still occupy a row.** **So holes are expected here, and
    /// `ResolveOriginKey` exists to give those holes a placeholder key.**
    /// </remarks>
    /// <param name="mechanismModel">机制模型 / The mechanism model.</param>
    /// <returns>行索引到来源约束的映射 / Row index to originating constraint.</returns>
    public static MathConstraint?[] OriginMap(LinearMechanismModel<Flt64> mechanismModel) {
        IReadOnlyList<IConstraint<Flt64, LinearCategory>> constraints =
            ((IMechanismModel<Flt64>)mechanismModel).Constraints;
        var originMap = new MathConstraint?[constraints.Count];
        for (int index = 0; index < constraints.Count; index++) {
            originMap[index] = constraints[index].Origin;
        }
        return originMap;
    }

    /// <summary>把行对偶向量转成按约束索引的字典 / Convert a row dual vector into a constraint-keyed dictionary.</summary>
    /// <remarks>
    /// **与 `ToFarkasSolution` 的关键差别：** **这个函数<b>保留零值</b>。**
    /// **对偶值里的零是有意义的（"这条约束没有紧"），而 Farkas 乘子里的零没有意义（"这个乘子不参与证书"）。**
    /// **同一个向量长度，两种不同的取舍——所以它们是两个函数而不是一个带开关的函数。**
    ///
    /// **The key difference from `ToFarkasSolution`: this one <b>keeps zeros</b>.** **A zero dual means "this constraint
    /// is not tight", which is information; a zero Farkas multiplier means "this multiplier plays no part in the
    /// certificate", which is not.** **Same vector length, two different choices — hence two functions rather than one
    /// with a switch.**
    /// </remarks>
    /// <param name="rowDuals">行对偶向量 / Row dual vector.</param>
    /// <param name="originMap">行索引到来源约束的映射 / Row index to originating constraint.</param>
    /// <returns>约束到对偶值的字典 / Constraint to dual value.</returns>
    public static IReadOnlyDictionary<MathConstraint, Flt64> ToDualSolution(
        IReadOnlyList<Flt64> rowDuals,
        MathConstraint?[] originMap) {
        var dualSolution = new Dictionary<MathConstraint, Flt64>(rowDuals.Count);
        for (int row = 0; row < rowDuals.Count; row++) {
            dualSolution[ResolveOriginKey(row, originMap)] = rowDuals[row];
        }
        return dualSolution;
    }

    /// <summary>筛选非零 Farkas 乘子 / Keep the non-zero Farkas multipliers.</summary>
    /// <remarks>
    /// **四个后端的同一份实现集中在这里。**
    ///
    /// **与 `ToDualSolution` 的差别只有两点，而这两点都不是"开关"：**
    ///
    /// 1. **零值在这里被<b>丢弃</b>，在 `ToDualSolution` 里被保留。**
    ///    **对偶值的零是信息（"这条约束没有紧"），Farkas 乘子的零不是（"这个乘子不参与证书"）。**
    /// 2. **全零在这里是<b>失败</b>，在 `ToDualSolution` 里是合法的空集。**
    ///    **一个全零的 Farkas 向量不是证据，而一个全零的对偶向量照样是对偶。**
    ///
    /// **所以它们不是"一个函数加开关"：** **开关会表达成 `bool dropZeros`，
    /// 而这两处的差别同时涉及"丢不丢"与"全丢算不算错"——两个独立的语义，
    /// 用一个布尔把它们绑在一起，会让"丢零但不算错"和"不丢零但算错"这两种组合无法表达。**…
    ///
    /// **`backendName` 是唯一按后端变化的部分：** **它出现在错误消息里，
    /// 而错误消息是给用户看的东西——"哪个后端没给出证书"是这条消息存在的全部理由。**
    ///
    /// **Four backends share this one implementation.**
    ///
    /// **It differs from `ToDualSolution` in exactly two ways, neither of them a switch:**
    ///
    /// 1. **zeros are <b>dropped</b> here and kept there.** **A zero dual is information ("this constraint is not
    ///    tight"); a zero Farkas multiplier is not.**
    /// 2. **all-zeros is a <b>failure</b> here and a legal empty set there.** **An all-zero Farkas vector is not
    ///    evidence; an all-zero dual is still a dual.**
    ///
    /// **So they are not "one function plus a switch": a switch would be `bool dropZeros`, while these two differ in
    /// both "drop or not" and "is dropping everything an error" — two independent semantics. A boolean binding them
    /// together could not express "drop zeros but it is not an error" or "keep zeros but it is an error".**
    ///
    /// **`backendName` is the only per-backend part: it appears in the error message, and that message exists for one
    /// reason — telling the user which backend failed to produce a certificate.**
    /// </remarks>
    /// <param name="farkasDuals">Farkas 乘子向量 / The Farkas multiplier vector.</param>
    /// <param name="originMap">行索引到来源约束的映射 / Row index to originating constraint.</param>
    /// <param name="backendName">后端名，用于错误消息 / Backend name, used in the error message.</param>
    /// <returns>非零乘子，或失败 / Non-zero multipliers, or a failure.</returns>
    public static Result<IReadOnlyDictionary<MathConstraint, Flt64>, ErrorCode, Error<ErrorCode>> ToFarkasSolution(
        IReadOnlyList<Flt64> farkasDuals,
        MathConstraint?[] originMap,
        string backendName) {
        var farkasSolution = new Dictionary<MathConstraint, Flt64>(farkasDuals.Count);
        for (int row = 0; row < farkasDuals.Count; row++) {
            double value = farkasDuals[row].ToDouble();
            if (!double.IsFinite(value)) {
                return Results.Failed<IReadOnlyDictionary<MathConstraint, Flt64>>(new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    $"第 {row} 行 Farkas 乘子无效 / The Farkas multiplier for row {row} is invalid"));
            }
            if (value != 0) {
                farkasSolution[ResolveOriginKey(row, originMap)] = farkasDuals[row];
            }
        }
        if (farkasSolution.Count == 0) {
            return Results.Failed<IReadOnlyDictionary<MathConstraint, Flt64>>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                $"{backendName} 未提供可用的 Farkas 证书 / {backendName} did not provide a usable Farkas certificate"));
        }
        return Results.Ok<IReadOnlyDictionary<MathConstraint, Flt64>>(farkasSolution);
    }
}


/// <summary>
/// 缺源约束的占位键 / A placeholder key for a row with no originating constraint.
/// </summary>
/// <remarks>
/// **六个后端各自的同一份实现集中在这里。**
///
/// **此前 `CoptFallbackMathConstraint`、`CplexFallbackMathConstraint`、`MindoptFallbackMathConstraint`、
/// `HexalyFallbackMathConstraint`、`FallbackMathConstraint`（SCIP）、`FallbackMathConstraint`（Gurobi）
/// 六份类<b>逐字相同</b>，只有名字不同——六份里的四份还用了五个不同的前缀，**
/// **所以"它们其实一样"这件事在阅读时看不出来。**
///
/// **它存在的原因是真实的：** **对偶/Farkas 整理要按"来源约束"建字典，而有些行没有来源
/// （对偶模型新建的行、界对偶列）。这些行不能凭空消失——消失会让行数变化，
/// 而调用方按行号回查时就错位了。**
///
/// **所以它们得到一个<b>可识别但非模型身份</b>的键：名字里带 `__fallback_` 前缀，
/// 任何人看到它就知道"这一行没有来源"。**
///
/// **合并的收益不是行数，而是"只有一个地方定义这个约定"：** **六份里的任何一份单独变动，
/// 都会让六个后端在这一点上悄悄分叉。**
///
/// **Six byte-identical per-backend implementations live here instead of in each backend.** **Four of the six used four different name
/// prefixes, so "they are actually the same" was invisible when reading.**
///
/// **Its reason to exist is real: the dual/Farkas tidy step builds a dictionary keyed by originating constraint, and
/// some rows have no origin (rows a dual model created, bound-dual columns). Those rows must not vanish — vanishing
/// changes the row count and misaligns callers reading back by row index.**
///
/// **So they get an <b>identifiable but non-identity</b> key: the `__fallback_` prefix says "this row has no origin".**
///
/// **The gain is not line count but having one place that defines this convention: any single one of the six drifting
/// would silently fork the backends on this point.**
/// </remarks>
/// <param name="Index">行号 / The row index.</param>
public sealed record FallbackMathConstraint(int Index) : MathConstraint {
    /// <inheritdoc/>
    public IMetaConstraintGroup? Group => null;
    /// <inheritdoc/>
    public bool Lazy => false;
    /// <inheritdoc/>
    public object? Args => null;
    /// <inheritdoc/>
    public int Priority => 0;
    /// <inheritdoc/>
    public string Name => $"__fallback_{Index}";
    /// <inheritdoc/>
    public string? DisplayName => null;
}

// ===== LinearInequalityConstraint<V> =====

/// <summary>
/// 线性不等式约束 / Linear inequality constraint.
/// </summary>
public sealed record LinearInequalityConstraint<V> : MathConstraint
    where V : struct, IRealNumber<V>, INumberField<V> {
    public LinearInequality<V> Inequality { get; }
    public IMetaConstraintGroup? Group { get; }
    public bool Lazy { get; }
    public object? Args { get; }
    public int Priority { get; }
    public string Name { get; }
    public string? DisplayName { get; }
    public Comparison Sign => Inequality.Comparison;

    public LinearInequalityConstraint(
        LinearInequality<V> inequality,
        IMetaConstraintGroup? group = null,
        bool lazy = false,
        object? args = null,
        int priority = 0,
        string? name = null,
        string? displayName = null) {
        Inequality = inequality;
        Group = group;
        Lazy = lazy;
        Args = args;
        Priority = priority;
        Name = name ?? inequality.Name;
        DisplayName = displayName ?? inequality.DisplayName;
    }

    /// <summary>将内部线性不等式扁平化为 LinearFlattenData / Flatten to LinearFlattenData</summary>
    public Result<LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> FlattenData() => Inequality.ToLinearFlattenData<V>();

    public override string ToString() => Inequality.ToString();
}

// ===== QuadraticInequalityConstraint<V> =====

/// <summary>
/// 二次不等式约束 / Quadratic inequality constraint.
/// </summary>
public sealed record QuadraticInequalityConstraint<V> : MathConstraint
    where V : struct, IRealNumber<V>, INumberField<V> {
    public QuadraticInequalityOf<V> Inequality { get; }
    public IMetaConstraintGroup? Group { get; }
    public bool Lazy { get; }
    public object? Args { get; }
    public int Priority { get; }
    public string Name { get; }
    public string? DisplayName { get; }
    public Comparison Sign => Inequality.Comparison;

    public QuadraticInequalityConstraint(
        QuadraticInequalityOf<V> inequality,
        IMetaConstraintGroup? group = null,
        bool lazy = false,
        object? args = null,
        int priority = 0,
        string? name = null,
        string? displayName = null) {
        Inequality = inequality;
        Group = group;
        Lazy = lazy;
        Args = args;
        Priority = priority;
        Name = name ?? inequality.Name;
        DisplayName = displayName ?? inequality.DisplayName;
    }

    public override string ToString() => Inequality.ToString();
}

// ===== QuadraticFlattenSubObject<V> =====

/// <summary>
/// 二次展平子目标 / Quadratic flatten sub-objective.
/// </summary>
public sealed record QuadraticFlattenSubObject<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    public ObjectCategory Category { get; }
    public string Name { get; }
    /// <summary>子目标显示名称 / Sub-objective display name.</summary>
    public string? DisplayName { get; }
    public QuadraticFlattenData<V> FlattenData { get; }

    /// <summary>创建二次展平子目标 / Create a flattened quadratic sub-objective.</summary>
    /// <param name="category">目标方向 / Objective direction.</param>
    /// <param name="name">目标名称 / Objective name.</param>
    /// <param name="flattenData">展平数据 / Flattened data.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public QuadraticFlattenSubObject(
        ObjectCategory category,
        string name,
        QuadraticFlattenData<V> flattenData,
        string? displayName = null) {
        Category = category;
        Name = name;
        DisplayName = displayName;
        FlattenData = flattenData;
    }

    public V Constant => FlattenData.Constant;
}
