// CG 包络线：按总重给最小/最大指数边界 / The CG envelope: min and max index bounds as a function of total weight.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;

/// <summary>
/// 包络线点 / A point on the envelope.
/// </summary>
/// <param name="TotalWeight">此点的总重量（千克）/ The total weight at this point, in kilogram.</param>
/// <param name="Index">此点的 CG 指数（力矩单位）/ The CG index at this point, in the torque unit.</param>
public sealed record EnvelopePoint(double TotalWeight, double Index);

/// <summary>
/// 包络线侧类型 / Side type of the envelope.
/// </summary>
public enum EnvelopeSideType
{
    /// <summary>左侧（最小指数）/ Left side, the minimum index</summary>
    Left,
    /// <summary>右侧（最大指数）/ Right side, the maximum index</summary>
    Right
}

/// <summary>
/// CG 包络线 / A CG envelope.
/// </summary>
public interface IEnvelope
{
    /// <summary>飞行阶段 / The flight phase</summary>
    FlightPhase Phase { get; }

    /// <summary>名称 / The name</summary>
    string Name { get; }

    /// <summary>最小指数符号 / The minimum index symbol</summary>
    ILinearIntermediateSymbol<Flt64>? MinIndexSymbol { get; }

    /// <summary>最大指数符号 / The maximum index symbol</summary>
    ILinearIntermediateSymbol<Flt64>? MaxIndexSymbol { get; }

    /// <summary>
    /// 注册到模型 / Register with the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    Try Register(LinearMetaModel<Flt64> model);
}

/// <summary>
/// 包络线的一侧 / One side of the envelope.
/// </summary>
/// <remarks>
/// 包络线是一条**折线**：每种总重对应一个允许的 CG 指数边界。两侧分别给下界与上界，重心落在两侧之间才算合规。
///
/// 两支取值：
///
/// | 已知该阶段总重 | 产出 |
/// | --- | --- |
/// | 有 | **常数**——把那个数插值一次 |
/// | 无 | **分段线性符号**——关于该阶段总重估计的折线函数 |
///
/// 第二支是关键：总重本身是决策变量的表达式（业载 + 干操作重量 + 救生筏 + 燃油），所以"允许的 CG 指数"也是表达式。写成常数要先把总重估成
/// 某个数，而那个猜测会被当成硬边界——重心被限制在一个伪造的包络里。
///
/// 与 <see cref="MinLowPayload"/> 的插值一致（都是折线），与 `MaxLoadWeightLimit` 的阶跃**有意不同**。
///
/// An envelope is a **polyline**: each total weight has an allowed CG index bound; the sides give the lower and upper bounds and the centre of gravity must fall between them.
/// Two branches — a known phase total gives a **constant** (interpolate once), an unknown one gives a **piecewise-linear symbol over that phase's total-weight estimate**. The
/// second matters: the total weight is itself an expression over decisions (payload + dry operating weight + liferaft + fuel), so the allowed CG index is an expression too.
/// Forcing a constant would mean guessing a weight first and then treating the guess as a hard boundary. The interpolation matches <see cref="MinLowPayload"/> (both polylines)
/// and is **deliberately different** from `MaxLoadWeightLimit`'s step function.
/// </remarks>
public sealed class EnvelopeSide
{
    /// <summary>
    /// 构造包络线的一侧 / Construct one side of the envelope.
    /// </summary>
    /// <param name="name">名称 / The name.</param>
    /// <param name="type">侧类型 / The side type.</param>
    /// <param name="points">点列表 / The points.</param>
    public EnvelopeSide(string name, EnvelopeSideType type, IReadOnlyList<EnvelopePoint> points) {
        ArgumentNullException.ThrowIfNull(points);
        Name = name;
        Type = type;
        Points = points.OrderBy(point => point.TotalWeight).ToList();
    }

    /// <summary>名称 / The name</summary>
    public string Name { get; }

    /// <summary>侧类型 / The side type</summary>
    public EnvelopeSideType Type { get; }

    /// <summary>点列表（按总重升序）/ The points, ordered by total weight</summary>
    public IReadOnlyList<EnvelopePoint> Points { get; }

    /// <summary>
    /// 某总重下的指数边界 / The index bound at a total weight.
    /// </summary>
    /// <param name="totalWeight">总重量 / The total weight.</param>
    /// <returns>指数边界 / The index bound.</returns>
    /// <remarks>
    /// 四条分支：无点 → 0；不高于首点 → 首点；不低于末点 → 末点；否则相邻两点间**线性插值**（两点总重相同时取上点，避免除零）。
    ///
    /// 单位不兼容分支在 C# 里**不存在**：这里所有量都是"千克"与"力矩单位"下的 `double`，没有可携带错单位的类型。这是有意的简化，
    /// 代价是"单位写错"在类型层面拦不住——它会表现为一个荒谬的指数值，而不是一条错误消息。
    ///
    /// Four branches: no points → 0; at or below the first → the first; at or above the last → the last; otherwise **linear interpolation** between neighbours (equal weights take
 /// the upper point, avoiding division by zero). Its unit-incompatibility branches **do not exist** in C# here: every quantity is a `double` in kilogram and the torque
    /// unit, so no type could carry a wrong unit. That is a deliberate simplification and it costs the compiler's ability to catch a wrong unit — a wrong unit now shows up as an
    /// absurd index value rather than an error message.
    /// </remarks>
    public double At(double totalWeight) {
        if (Points.Count == 0) {
            return 0.0;
        }
        if (totalWeight <= Points[0].TotalWeight) {
            return Points[0].Index;
        }
        if (totalWeight >= Points[Points.Count - 1].TotalWeight) {
            return Points[Points.Count - 1].Index;
        }
        for (int i = 1; i < Points.Count; ++i) {
            EnvelopePoint lower = Points[i - 1];
            EnvelopePoint upper = Points[i];
            if (totalWeight <= upper.TotalWeight) {
                double delta = upper.TotalWeight - lower.TotalWeight;
                if (delta == 0.0) {
                    return upper.Index;
                }
                return lower.Index + (upper.Index - lower.Index) * ((totalWeight - lower.TotalWeight) / delta);
            }
        }
        return Points[Points.Count - 1].Index;
    }

    /// <summary>
    /// 关于总重估计的分段线性边界符号 / The piecewise-linear bound symbol over a total-weight estimate.
    /// </summary>
    /// <param name="totalWeightEstimate">该阶段的总重估计 / That phase's total-weight estimate.</param>
    /// <returns>指数边界符号或结构化失败 / The index bound symbol, or a structured failure.</returns>
    public Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> Piecewise(LinearExpressionSymbol totalWeightEstimate) {
        ArgumentNullException.ThrowIfNull(totalWeightEstimate);
        if (Points.Count < 2) {
            // 至少需要两点；一点时"折线"退化成常数，而分段函数构造会直接失败。应当在这里拦住，而不是让构造器抛。
            // At least two points are required; with one the polyline degenerates to a constant and the piecewise constructor rejects it. Stop it here rather than letting the
            // constructor throw.
            return Results.Failed<ILinearIntermediateSymbol<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "包络 " + Name + " 至少要两点 / envelope " + Name + " needs at least two points"));
        }
        var samples = new List<Point<Dim2, Flt64>>(Points.Count);
        foreach (EnvelopePoint point in Points) {
            samples.Add(Point<Dim2, Flt64>.Create(new Flt64(point.TotalWeight), new Flt64(point.Index)));
        }
        Result<UnivariateLinearPiecewiseFunction<Flt64>, ErrorCode, Error<ErrorCode>> function =
            UnivariateLinearPiecewiseFunction.FromPoints(
                new LinearPolynomial<Flt64>(totalWeightEstimate.Polynomial.Monomials, totalWeightEstimate.Polynomial.Constant),
                samples,
                IIntoValue<Flt64>.Identity,
                name: Name + "_" + Type.ToString().ToLowerInvariant());
        if (!function.IsOk) {
            return Results.Failed<ILinearIntermediateSymbol<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "包络 " + Name + " 的分段函数构造失败 / envelope " + Name + " could not build its piecewise function"));
        }
        // 返回**适配器本身**而不是把它的结果多项式拆出来：适配器保留了对分段函数的引用，求解时才能按实际总重求值。
        // 拆成裸多项式会得到一个线性近似，而那正是"包络"最不该被近似的地方。
        // Return **the adapter itself** rather than extracting its result polynomial: the adapter keeps the reference to the piecewise function so the solve can evaluate it at
        // the actual total weight. Extracting a bare polynomial would leave a linear approximation — exactly what an envelope must not be approximated as.
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function.Value)
        {
            Name = Name + "_" + Type.ToString().ToLowerInvariant(),
        };
        return Results.Ok<ILinearIntermediateSymbol<Flt64>>(adapter);
    }
}

/// <summary>
/// 标准 CG 包络线（左右两侧）/ The standard CG envelope with a left and a right side.
/// </summary>
/// <remarks>
/// 逐条对齐：左侧给最小指数、右侧给最大指数，各自按"该阶段总重已知与否"产常数或分段线性符号，名称分别是
/// `min_index_{包络名}_{阶段}` 与 `max_index_{包络名}_{阶段}`。
///
/// 左右两侧的产出**必须分别注册**。把两侧合成一条约束（重心 ≥ 左、≤ 右）少写一半很容易，但那样一旦某侧漏注册就无从发现——分别注册加两条判据才能钉住
/// "两侧各自成立"。
///
/// the left side yields the minimum index and the right the maximum, each producing a constant or a piecewise-linear symbol depending on whether that phase's
/// total weight is known, named `min_index_{envelope}_{phase}` and `max_index_{envelope}_{phase}`. The two sides must be **registered separately**: folding them into one row
/// (CG ≥ left, ≤ right) is easy to half-write, and a missing side becomes undetectable.
/// </remarks>
public sealed class Envelope : IEnvelope
{
    private readonly TotalWeight _totalWeight;

    /// <summary>
    /// 构造包络线 / Construct the envelope.
    /// </summary>
    /// <param name="phase">飞行阶段 / The flight phase.</param>
    /// <param name="name">名称 / The name.</param>
    /// <param name="lhsSide">左侧 / The left side.</param>
    /// <param name="rhsSide">右侧 / The right side.</param>
    /// <param name="totalWeight">总重模型 / The total weight model.</param>
    public Envelope(
        FlightPhase phase,
        string name,
        EnvelopeSide lhsSide,
        EnvelopeSide rhsSide,
        TotalWeight totalWeight) {
        Phase = phase;
        Name = name;
        LhsSide = lhsSide;
        RhsSide = rhsSide;
        _totalWeight = totalWeight;
    }

    /// <inheritdoc />
    public FlightPhase Phase { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>左侧 / The left side</summary>
    public EnvelopeSide LhsSide { get; }

    /// <summary>右侧 / The right side</summary>
    public EnvelopeSide RhsSide { get; }

    /// <inheritdoc />
    public ILinearIntermediateSymbol<Flt64>? MinIndexSymbol { get; private set; }

    /// <inheritdoc />
    public ILinearIntermediateSymbol<Flt64>? MaxIndexSymbol { get; private set; }

    /// <summary>
    /// 某总重下的最小指数 / The minimum index at a total weight.
    /// </summary>
    /// <param name="totalWeight">总重量 / The total weight.</param>
    /// <returns>最小指数 / The minimum index.</returns>
    public double MinIndexOf(double totalWeight) => LhsSide.At(totalWeight);

    /// <summary>
    /// 某总重下的最大指数 / The maximum index at a total weight.
    /// </summary>
    /// <param name="totalWeight">总重量 / The total weight.</param>
    /// <returns>最大指数 / The maximum index.</returns>
    public double MaxIndexOf(double totalWeight) => RhsSide.At(totalWeight);

    /// <inheritdoc />
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);

        Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> minimum = Bound(isMinimum: true);
        if (minimum is Failed<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> minimumFailure) {
            return Results.Failed<Success>(minimumFailure.Error);
        }
        Try addedMinimum = model.Add(minimum.Value);
        if (!addedMinimum.IsOk) {
            return addedMinimum;
        }
        MinIndexSymbol = minimum.Value;

        Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> maximum = Bound(isMinimum: false);
        if (maximum is Failed<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> maximumFailure) {
            return Results.Failed<Success>(maximumFailure.Error);
        }
        Try addedMaximum = model.Add(maximum.Value);
        if (!addedMaximum.IsOk) {
            return addedMaximum;
        }
        MaxIndexSymbol = maximum.Value;

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> Bound(bool isMinimum) {
        string prefix = isMinimum ? "min_index_" : "max_index_";
        string suffix = Name + "_" + Phase.ToString().ToLowerInvariant();
        if (_totalWeight.ComputedTotalWeight.TryGetValue(Phase, out double computed)) {
            double value = isMinimum ? MinIndexOf(computed) : MaxIndexOf(computed);
            return Results.Ok<ILinearIntermediateSymbol<Flt64>>(new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: new Flt64(value)),
                name: prefix + suffix));
        }
        if (_totalWeight.EstimateTotalWeight(Phase) is not { } estimate) {
            return Results.Failed<ILinearIntermediateSymbol<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "包络 " + Name + " 缺少 " + Phase.ToString().ToLowerInvariant()
                + " 的总重估计 / envelope " + Name + " has no estimated total weight for that phase"));
        }
        EnvelopeSide side = isMinimum ? LhsSide : RhsSide;
        Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> piecewise = side.Piecewise(estimate);
        if (!piecewise.IsOk) {
            return piecewise;
        }
        // 分段符号自带名字（`{包络名}_{left|right}`），这里换成注册名，报告里才与"最小/最大指数"的对应关系一致。
        // The piecewise symbol carries its own name (`{envelope}_{left|right}`); overwrite it with the registration name so reports line up with min/max.
        piecewise.Value.Name = prefix + suffix;
        return Results.Ok<ILinearIntermediateSymbol<Flt64>>(piecewise.Value);
    }
}

/// <summary>
/// 带条件切换的 CG 包络线（尚未实现）/ A CG envelope with conditional sides (not yet implemented).
/// </summary>
/// <remarks>
/// 骨架留给后续。**此前这里对任何输入都返回 `Ok`**，那等于告诉调用方"包络已加进模型"而实际上一行都没有——现在改成明确失败，至少不会假装成功。
///
/// Kept as a skeleton for the next step. **It used to return `Ok` for any input**, telling the caller the envelope was added while the model held nothing; it now fails explicitly
/// rather than pretending.
/// </remarks>
/// <summary>
/// 条件包络 / A conditional envelope.
/// </summary>
/// <remarks>
/// 条件包络：同一相位下有两组左右边界，**用哪一个由 `valueCondition` 决定**。三态：
///
/// * `true` → 第一组；`false` → 第二组；两者都只需把那一组算出来；
/// * `null` → **条件是决策变量**，得用 `IfThen` 造一个开关再拼：`min = 第二组 + 开关·(第一组 − 第二组)`。
///
/// **第一组/第二组写反不会报错**：两支都是合法的包络，只是边界值不同，求解照样成功。
///
/// **条件未定那一支：开关拼出来**。`min = 第二组 + 开关·(第一组 − 第二组)`，`max` 同理；开关是 `IfThenFunction`，
/// 需要一个 0/1 的条件符号与两套界（条件的取值界、差值界）。界取宽一点只让 Big-M 松弛变弱，**取窄了会把可行解切掉**，
/// 所以这里凡是拿不到"可用的有限界"就退回**断点范围**（一个必然包含真值的超集）。
///
/// **一处有意简化（`ponytail:`）**：对非分段符号本可先试"多项式本身的有限界"，更紧；但那个助手是
/// `Core` 的 internal（`BigMValues.FiniteBounds`），示例域拿不到，所以常数符号直接用 `[值, 值]`、其余一律退回断点范围。
/// 升级路径：把 `FiniteBounds` 提为公开（或实现到 `Math`），再先试紧界。
///
/// **The undetermined branch synthesises the switch**: `min = second + switch·(first − second)`, likewise `max`; the switch is an `IfThenFunction` that needs a 0/1 condition symbol
/// and two sets of bounds (the condition's range, the difference's range). A too-wide bound only weakens the Big-M relaxation, **a too-narrow one cuts off feasible solutions**, so
/// whenever a usable finite bound cannot be obtained this falls back to **the point range** (a superset that certainly contains the true values).
///
/// **One deliberate simplification (`ponytail:`)**: for non-piecewise symbols the implementation first tries the polynomial's own finite bounds, which are tighter. That helper is `Core`'s
/// internal `BigMValues.FiniteBounds`, unreachable from an example domain, so constant symbols use `[value, value]` and everything else falls back to the point range. Upgrade path: make
/// `FiniteBounds` public (or move it to `Math`) and prefer the tight bounds as this implementation does.
/// </remarks>
public sealed class ConditionalEnvelope : IEnvelope
{
    private readonly TotalWeight _totalWeight;

    private readonly Func<bool?> _valueCondition;

    private readonly Func<string, ILinearIntermediateSymbol<Flt64>>? _symbolCondition;

    public ConditionalEnvelope(
        FlightPhase phase,
        string name,
        EnvelopeSide lhsSide1,
        EnvelopeSide rhsSide1,
        EnvelopeSide lhsSide2,
        EnvelopeSide rhsSide2,
        Func<bool?> valueCondition,
        TotalWeight totalWeight,
        Func<string, ILinearIntermediateSymbol<Flt64>>? symbolCondition = null)
    {
        ArgumentNullException.ThrowIfNull(lhsSide1);
        ArgumentNullException.ThrowIfNull(rhsSide1);
        ArgumentNullException.ThrowIfNull(lhsSide2);
        ArgumentNullException.ThrowIfNull(rhsSide2);
        ArgumentNullException.ThrowIfNull(valueCondition);
        ArgumentNullException.ThrowIfNull(totalWeight);
        Phase = phase;
        Name = name;
        LhsSide1 = lhsSide1;
        RhsSide1 = rhsSide1;
        LhsSide2 = lhsSide2;
        RhsSide2 = rhsSide2;
        _valueCondition = valueCondition;
        _totalWeight = totalWeight;
        _symbolCondition = symbolCondition;
    }

    /// <inheritdoc />
    public FlightPhase Phase { get; init; }

    /// <inheritdoc />
    public string Name { get; init; } = "";

    /// <summary>条件为真时的左侧边界 / The left bound when the condition holds.</summary>
    public EnvelopeSide LhsSide1 { get; init; }

    /// <summary>条件为真时的右侧边界 / The right bound when the condition holds.</summary>
    public EnvelopeSide RhsSide1 { get; init; }

    /// <summary>条件为假时的左侧边界 / The left bound when the condition does not hold.</summary>
    public EnvelopeSide LhsSide2 { get; init; }

    /// <summary>条件为假时的右侧边界 / The right bound when the condition does not hold.</summary>
    public EnvelopeSide RhsSide2 { get; init; }

    /// <inheritdoc />
    public ILinearIntermediateSymbol<Flt64>? MinIndexSymbol { get; private set; }

    /// <inheritdoc />
    public ILinearIntermediateSymbol<Flt64>? MaxIndexSymbol { get; private set; }

    /// <inheritdoc />
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        bool? condition = _valueCondition();
        if (condition is null) {
            return RegisterSwitch(model);
        }
        bool first = condition.Value;

        Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> minimum = Side(first ? LhsSide1 : LhsSide2, isMinimum: true);
        if (minimum is Failed<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> minimumFailure) {
            return Results.Failed<Success>(minimumFailure.Error);
        }
        Try addedMinimum = model.Add(minimum.Value);
        if (!addedMinimum.IsOk) {
            return addedMinimum;
        }
        MinIndexSymbol = minimum.Value;

        Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> maximum = Side(first ? RhsSide1 : RhsSide2, isMinimum: false);
        if (maximum is Failed<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> maximumFailure) {
            return Results.Failed<Success>(maximumFailure.Error);
        }
        Try addedMaximum = model.Add(maximum.Value);
        if (!addedMaximum.IsOk) {
            return addedMaximum;
        }
        MaxIndexSymbol = maximum.Value;

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <remarks>
    /// **总重已算出时边界是常数，没算出时才分段**：这一点与无条件包络完全一致（`Envelope.Bound`），
    /// 差别只在"取哪一组"。写成一律分段，会在总重已定的相位上多造一批分段函数符号——**代价不是多几行代码，是多一批二进制变量**。
    ///
    /// **A computed total weight makes the bound a constant; only an estimated one needs a piecewise function**: exactly like the unconditional `Envelope.Bound`, the only difference being
    /// which pair is taken. Making everything piecewise would build a batch of piecewise symbols on phases whose weight is already known — **the cost is not a few lines but a batch of
    /// binary variables**.
    /// </remarks>
    /// <remarks>
    /// **先注册条件、再注册四条边界、最后拼两个开关**：`IfThenFunction` 在 `model.Add` 时会把条件与差值展开成变量，
    /// 被引用的一方必须已经在模型里，否则展开落空。顺序错了不报错，开关会退化成常数零——**那正好是"条件永远不成立"**。
    ///
    /// **The condition is registered first, then the four bounds, then the two switches**: `IfThenFunction` expands the condition and the difference into variables when it is added, so
    /// whatever it references must already be in the model or the expansion comes out empty. A wrong order raises nothing and the switch degenerates to a constant zero — **exactly
    /// "the condition never holds"**.
    /// </remarks>
    private Try RegisterSwitch(LinearMetaModel<Flt64> model) {
        if (_symbolCondition is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "条件包络 " + Name + " 的条件未定，但没给条件符号 / conditional envelope " + Name
                + " has an undecided condition but no condition symbol was supplied"));
        }
        string suffix = Name + "_" + Phase.ToString().ToLowerInvariant();
        ILinearIntermediateSymbol<Flt64> condition = _symbolCondition(suffix + "_condition");
        if (model.Add(condition) is { IsOk: false } conditionFailure) {
            return conditionFailure;
        }

        Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>>[] sides = {
            SideAt(LhsSide1), SideAt(LhsSide2), SideAt(RhsSide1), SideAt(RhsSide2),
        };
        foreach (Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> side in sides) {
            if (side is Failed<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<Success>(failure.Error);
            }
        }
        ILinearIntermediateSymbol<Flt64> min1 = sides[0].Value!;
        ILinearIntermediateSymbol<Flt64> min2 = sides[1].Value!;
        ILinearIntermediateSymbol<Flt64> max1 = sides[2].Value!;
        ILinearIntermediateSymbol<Flt64> max2 = sides[3].Value!;
        foreach (ILinearIntermediateSymbol<Flt64> side in new[] { min1, min2, max1, max2 }) {
            if (model.Add(side) is { IsOk: false } sideFailure) {
                return sideFailure;
            }
        }

        Result<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>> minBounds =
            DifferenceBounds(BoundsOf(min1, LhsSide1), BoundsOf(min2, LhsSide2));
        if (minBounds is Failed<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>> minFailure) {
            return Results.Failed<Success>(minFailure.Error);
        }
        Result<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>> maxBounds =
            DifferenceBounds(BoundsOf(max1, RhsSide1), BoundsOf(max2, RhsSide2));
        if (maxBounds is Failed<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>> maxFailure) {
            return Results.Failed<Success>(maxFailure.Error);
        }

        Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> minSwitch =
            Switch(condition, min1, min2, minBounds.Value!, suffix + "_min_switch");
        if (minSwitch is Failed<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> minSwitchFailure) {
            return Results.Failed<Success>(minSwitchFailure.Error);
        }
        Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> maxSwitch =
            Switch(condition, max1, max2, maxBounds.Value!, suffix + "_max_switch");
        if (maxSwitch is Failed<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> maxSwitchFailure) {
            return Results.Failed<Success>(maxSwitchFailure.Error);
        }

        // **开关符号本身必须进模型**：`min_index_` 的多项式引用的是开关的结果变量，而编码开关的那组辅助变量与约束
        // 只有把它加进模型才会生成。漏掉这一步不会报错，`min_index_` 引用的变量在模型里**没有任何约束**——它可以是任何值。
        // **The switch symbol itself must enter the model**: `min_index_` references the switch's result variable, and the auxiliary variables and constraints that encode the switch
        // are only generated when the symbol is added. Skipping it raises nothing and the variable `min_index_` points at has **no constraints at all** — it can be anything.
        foreach (ILinearIntermediateSymbol<Flt64> symbol in new ILinearIntermediateSymbol<Flt64>[] { minSwitch.Value!, maxSwitch.Value! }) {
            if (model.Add(symbol) is { IsOk: false } switchFailure) {
                return switchFailure;
            }
        }

        LinearPolynomial<Flt64> minSum = min2.Polynomial + minSwitch.Value!.Polynomial;
        LinearPolynomial<Flt64> maxSum = max2.Polynomial + maxSwitch.Value!.Polynomial;
        MinIndexSymbol = new LinearExpressionSymbol(
            new MutableLinearPolynomial<Flt64>(minSum.Monomials, minSum.Constant),
            name: "min_index_" + suffix);
        MaxIndexSymbol = new LinearExpressionSymbol(
            new MutableLinearPolynomial<Flt64>(maxSum.Monomials, maxSum.Constant),
            name: "max_index_" + suffix);
        foreach (ILinearIntermediateSymbol<Flt64> symbol in new ILinearIntermediateSymbol<Flt64>[] { MinIndexSymbol, MaxIndexSymbol }) {
            if (model.Add(symbol) is { IsOk: false } indexFailure) {
                return indexFailure;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <remarks>
    /// **开关的"真"分支是两者之差**：`第二组 + 开关·(第一组 − 第二组)` 在开关为 1 时给第一组、为 0 时给第二组。
    /// 差值写成"第一组减第二组"还是反过来，两种写法都能求解——**只有取错组时边界值不同这一点能分辨**。
    ///
    /// **The switch's "true" branch is the difference**: `second + switch·(first − second)` gives the first pair when the switch is 1 and the second when it is 0. Writing the difference in
    /// either direction still solves — **only the boundary values differ, which is the one thing that tells the branches apart**.
    /// </remarks>
    private static Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> Switch(
        ILinearIntermediateSymbol<Flt64> condition,
        ILinearIntermediateSymbol<Flt64> first,
        ILinearIntermediateSymbol<Flt64> second,
        LinearPolynomialBounds<Flt64> thenBounds,
        string name) {
        Result<IfThenFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfThenFunction<Flt64>.Create(
            condition: condition.Polynomial,
            then: first.Polynomial - second.Polynomial,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(1e-10),
            conditionBounds: new LinearPolynomialBounds<Flt64>(Flt64.Zero, Flt64.One),
            thenBounds: thenBounds,
            name: name);
        if (created is Failed<IfThenFunction<Flt64>, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<ILinearIntermediateSymbol<Flt64>>(failure.Error);
        }
        return Results.Ok<ILinearIntermediateSymbol<Flt64>>(new LinearFunctionSymbolAdapter<Flt64>(created.Value!)
        {
            Name = name,
        });
    }

    /// <remarks>
    /// **常数符号的界就是它自己，分段符号退回断点范围**：分段适配器的多项式只是辅助结果变量，它的有限界不代表真实输出范围；
    /// 用它的界会得到 `[0,0]` 之类的假界，**Big-M 一收就把可行解切掉了**。
    ///
    /// **A constant symbol is its own bound; a piecewise symbol falls back to the point range**: a piecewise adapter's polynomial is just a helper result variable and its finite bounds
    /// do not describe the real output, so using it yields fake bounds such as `[0, 0]` — **and a Big-M collapsed that far cuts off feasible solutions**.
    /// </remarks>
    private static LinearPolynomialBounds<Flt64> BoundsOf(ILinearIntermediateSymbol<Flt64> symbol, EnvelopeSide side) {
        if (symbol is LinearExpressionSymbol expression && expression.Polynomial.Monomials.Count == 0) {
            return new LinearPolynomialBounds<Flt64>(expression.Polynomial.Constant, expression.Polynomial.Constant);
        }
        double lower = double.PositiveInfinity;
        double upper = double.NegativeInfinity;
        foreach (EnvelopePoint point in side.Points) {
            lower = System.Math.Min(lower, point.Index);
            upper = System.Math.Max(upper, point.Index);
        }
        return new LinearPolynomialBounds<Flt64>(new Flt64(lower), new Flt64(upper));
    }

    private static Result<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>> DifferenceBounds(
        LinearPolynomialBounds<Flt64> first,
        LinearPolynomialBounds<Flt64> second) {
        var lower = new Flt64(first.LowerBound.ToDouble() - second.UpperBound.ToDouble());
        var upper = new Flt64(first.UpperBound.ToDouble() - second.LowerBound.ToDouble());
        if (!double.IsFinite(lower.ToDouble()) || !double.IsFinite(upper.ToDouble()) || lower.ToDouble() > upper.ToDouble()) {
            return Results.Failed<LinearPolynomialBounds<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "条件包络的差值范围不可用 / the conditional envelope's difference range is unusable"));
        }
        return Results.Ok<LinearPolynomialBounds<Flt64>>(new LinearPolynomialBounds<Flt64>(lower, upper));
    }

    /// <remarks>
    /// 与条件已定时的两支**共用同一套取边界的方式**：总重算得出就是常数，算不出就是分段符号（名字留给调用方）。
    ///
    /// Shares the same way of taking a bound as the two decided branches: a computed weight gives a constant, otherwise a piecewise symbol (the naming is left to the caller).
    /// </remarks>
    private Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> SideAt(EnvelopeSide side) {
        string suffix = Name + "_" + Phase.ToString().ToLowerInvariant();
        if (_totalWeight.ComputedTotalWeight.TryGetValue(Phase, out double computed)) {
            return Results.Ok<ILinearIntermediateSymbol<Flt64>>(new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: new Flt64(side.At(computed))),
                name: Name + "_" + side.Name + "_" + suffix));
        }
        if (_totalWeight.EstimateTotalWeight(Phase) is not { } estimate) {
            return Results.Failed<ILinearIntermediateSymbol<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "条件包络 " + Name + " 缺少 " + Phase.ToString().ToLowerInvariant()
                + " 的总重估计 / conditional envelope " + Name + " has no estimated total weight for that phase"));
        }
        return side.Piecewise(estimate);
    }

    private Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> Side(EnvelopeSide side, bool isMinimum) {
        string prefix = isMinimum ? "min_index_" : "max_index_";
        string suffix = Name + "_" + Phase.ToString().ToLowerInvariant();
        if (_totalWeight.ComputedTotalWeight.TryGetValue(Phase, out double computed)) {
            double value = side.At(computed);
            return Results.Ok<ILinearIntermediateSymbol<Flt64>>(new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: new Flt64(value)),
                name: prefix + suffix));
        }
        if (_totalWeight.EstimateTotalWeight(Phase) is not { } estimate) {
            return Results.Failed<ILinearIntermediateSymbol<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "条件包络 " + Name + " 缺少 " + Phase.ToString().ToLowerInvariant()
                + " 的总重估计 / conditional envelope " + Name + " has no estimated total weight for that phase"));
        }
        Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> piecewise = side.Piecewise(estimate);
        if (!piecewise.IsOk) {
            return piecewise;
        }
        piecewise.Value.Name = prefix + suffix;
        return Results.Ok<ILinearIntermediateSymbol<Flt64>>(piecewise.Value);
    }
}
