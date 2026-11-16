// 水平安定面：配平符号 / The horizontal stabilizer: the trim symbol.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;

/// <summary>
/// 水平安定面 / A horizontal stabilizer.
/// </summary>
/// <remarks>
/// 本类只承担**配平查找**与**配平符号**：
///
/// 1. **配平值按起飞重量取最近点**（`minByOrNull { |tow − 给定重量| }`），不是插值。空点表时取 0。
/// 2. **`trim` 是零多项式符号**（`LinearExpressionSymbol(Flt64.zero, …)`）。配平同样没有真正接上查表结果，
///    所以 `At` 是一个**独立可用**的查表函数，而 `TrimSymbol` 恒为零。这是现状，不是本实现的退让——沿用并在此记录。
/// 3. **警告松弛（`warnSlack`）未实现**，见下方 `ponytail:` 说明。
///
/// This class carries **trim lookup** and **the trim symbol** only: (1) **the trim is the nearest point by take-off weight**, not an interpolation, and
/// an empty table yields 0; (2) **`trim` is a zero polynomial** — the implementation itself writes `LinearExpressionSymbol(Flt64.zero, …)`, so the lookup never reaches the symbol
/// there. `At` is therefore a usable lookup on its own while `TrimSymbol` stays zero. That is its state, not a concession of this implementation.
/// (3) **the warning slack (`warnSlack`) is not implemented** — see the `ponytail:` note.
/// </remarks>
public sealed class HorizontalStabilizer
{
    /// <summary>
    /// 水平安定面键 / A horizontal stabilizer key.
    /// </summary>
    /// <param name="Angle">安定面角度 / The stabilizer angle.</param>
    /// <param name="ThrustDrate">推力衰减设置，可为空 / The thrust derate setting, if applicable.</param>
    public sealed record Key(
        Infrastructure.HorizontalStabilizerAngle Angle,
        Infrastructure.HorizontalStabilizerThrustDrate? ThrustDrate)
    {
        /// <summary>
        /// 只渲染角度 / Render the angle only.
        /// </summary>
        /// <returns>角度文本 / The angle text.</returns>
        /// <remarks>
        /// **必须重写**。`Key.ToString` 返回 `angle.ToString()`，而 C# 侧原先用 `record` 的默认输出
        /// （`Key { Angle = …, ThrustDrate = … }`）。后果不是"名字难看"：`HorizontalStabilizerLimit` 把 `Key` 拼进目标名
        /// `"horizontal stabilizer {key}"`，默认输出会让**每个目标名都带 95 个字符的类型名**，下游按名字取目标全部落空。
        ///
        /// **This override is required.** `Key.ToString` returns `angle.ToString()`, while the C# side used the record's default output. The consequence is not cosmetic:
        /// `HorizontalStabilizerLimit` interpolates the `Key` into the objective name `"horizontal stabilizer {key}"`, so the default output puts a 95-character type dump in every
        /// objective name and downstream lookups by name all miss.
        /// </remarks>
        public override string ToString() => Angle.ToString();
    }

    /// <summary>
    /// 水平安定面数据点 / A horizontal stabilizer data point.
    /// </summary>
    /// <param name="Tow">起飞重量 / Take-off weight (kg)</param>
    /// <param name="MacPercent">MAC 百分比 / MAC percentage</param>
    /// <param name="Trim">配平值 / Trim value</param>
    public sealed record Point(double Tow, double MacPercent, double Trim);

    /// <summary>
    /// 水平安定面限制 / Horizontal stabilizer trim limits.
    /// </summary>
    /// <param name="MinTrim">最小配平 / Minimum trim</param>
    /// <param name="MaxTrim">最大配平 / Maximum trim</param>
    /// <param name="WarnMinTrim">警告最小配平 / Warning minimum trim</param>
    /// <param name="WarnMaxTrim">警告最大配平 / Warning maximum trim</param>
    public sealed record Limit(
        double? MinTrim = null,
        double? MaxTrim = null,
        double? WarnMinTrim = null,
        double? WarnMaxTrim = null);

    private readonly IReadOnlyList<Point> _points;

    /// <summary>
    /// 构造水平安定面 / Construct the horizontal stabilizer.
    /// </summary>
    /// <param name="key">配置键 / The configuration key.</param>
    /// <param name="points">配平查找点 / The trim lookup points.</param>
    /// <param name="limit">配平限制 / The trim limits.</param>
    public HorizontalStabilizer(Key key, IReadOnlyList<Point> points, Limit limit) {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(limit);
        Configuration = key;
        _points = points;
        Bounds = limit;
    }

    /// <summary>配置键 / The configuration key</summary>
    public Key Configuration { get; }

    /// <summary>配平查找点 / The trim lookup points</summary>
    public IReadOnlyList<Point> Points => _points;

    /// <summary>配平限制 / The trim limits</summary>
    public Limit Bounds { get; }

    /// <summary>配平符号 / The trim symbol</summary>
    public LinearExpressionSymbol? TrimSymbol { get; private set; }

    /// <summary>
    /// 按起飞重量查最近的配平值 / Look up the nearest trim value by take-off weight.
    /// </summary>
    /// <param name="takeOffWeight">起飞重量 / The take-off weight.</param>
    /// <returns>配平值 / The trim value.</returns>
    /// <remarks>
    /// 取**最近点**，不是插值。空点表时返回 0（`?.trim ?: Flt64.zero`）。
    ///
    /// The **nearest** point, not an interpolation. An empty table yields 0 (`?.trim ?: Flt64.zero`).
    /// </remarks>
    public double At(double takeOffWeight) {
        if (_points.Count == 0) {
            return 0.0;
        }
        double best = double.NaN;
        double bestDistance = double.PositiveInfinity;
        foreach (Point point in _points) {
            double distance = System.Math.Abs(point.Tow - takeOffWeight);
            if (distance < bestDistance) {
                bestDistance = distance;
                best = point.Trim;
            }
        }
        return best;
    }

    /// <summary>
    /// 注册配平符号 / Register the trim symbol.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// `withMacOptimization` 下还会注册一个 `warnSlack`，四个分支：上下界都有 → `SlackRangeFunction`；只有下界 → 负向阈值松弛；
    /// 只有上界 → 正向阈值松弛；都没有 → 恒零符号。**起四支全部接上**，因为 `SlackRangeFunction` 与 `SlackFunction` 本仓都已具备。
    ///
 /// The implementation also registers a `warnSlack` under `withMacOptimization`, in four branches: both bounds → `SlackRangeFunction`; lower only → a negative threshold slack; upper
    /// only → a positive threshold slack; neither → a constant-zero symbol. **All four are wired up now**, since this implementation now has both `SlackRangeFunction` and `SlackFunction`.
    /// </remarks>
    public Try Register(StowageMode stowageMode, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (TrimSymbol is null) {
            TrimSymbol = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                name: Configuration + "_trim");
        }
        Try added = model.Add(TrimSymbol);
        if (!added.IsOk) {
            return added;
        }

        if (stowageMode.WithMacOptimization()) {
            if (WarnSlack is null) {
                string name = Configuration + "_trim_warn_slack";
                if (Bounds.WarnMinTrim is { } lower && Bounds.WarnMaxTrim is { } upper) {
                    // **上下界都有**：区间距离函数。
                    // **Both bounds**: a range-distance function.
                    Result<SlackRangeFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = SlackRangeFunction<Flt64>.Create(
                        input: TrimSymbol.Polynomial,
                        lower: new Flt64(lower),
                        upper: new Flt64(upper),
                        converter: IIntoValue<Flt64>.Identity,
                        name: name);
                    if (created is not Ok<SlackRangeFunction<Flt64>, ErrorCode, Error<ErrorCode>> okRange) {
                        return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                            ((Failed<SlackRangeFunction<Flt64>, ErrorCode, Error<ErrorCode>>)created).Error);
                    }
                    WarnSlack = new LinearFunctionSymbolAdapter<Flt64>(okRange.Value!);
                } else if (Bounds.WarnMinTrim is { } minOnly) {
                    // **只有下界**：负向阈值松弛。
                    // **Lower only**: a negative threshold slack.
                    WarnSlack = new LinearFunctionSymbolAdapter<Flt64>(
                        new SlackFunction<Flt64>(
                            x: TrimSymbol.Polynomial,
                            y: new LinearPolynomial<Flt64>(new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), new Flt64(minOnly)),
                            converter: IIntoValue<Flt64>.Identity,
                            withNegative: true,
                            withPositive: false,
                            threshold: true,
                            constraint: false,
                            name: name));
                } else if (Bounds.WarnMaxTrim is { } maxOnly) {
                    // **只有上界**：正向阈值松弛。
                    // **Upper only**: a positive threshold slack.
                    WarnSlack = new LinearFunctionSymbolAdapter<Flt64>(
                        new SlackFunction<Flt64>(
                            x: TrimSymbol.Polynomial,
                            y: new LinearPolynomial<Flt64>(new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), new Flt64(maxOnly)),
                            converter: IIntoValue<Flt64>.Identity,
                            withNegative: false,
                            withPositive: true,
                            threshold: true,
                            constraint: false,
                            name: name));
                } else {
                    // **都没有**：恒零符号（是这样，不是"不注册"）。
 // **Neither**: a constant-zero symbol, as this implementation does — not "skip registration".
                    // **恒零分支是普通符号**，不是函数符号——这里建的就是 `LinearExpressionSymbol`。
                    // 用 adapter 包一个普通符号在类型上就通不过，也不该硬凑。
                    // **The constant-zero branch is a plain symbol**, not a function symbol — a `LinearExpressionSymbol` is built here. Wrapping a plain symbol in the
                    // adapter does not typecheck, and should not be forced.
                    WarnSlack = new LinearExpressionSymbol(
                        new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                        name: name);
                }
            }
            Try addedSlack = model.Add(WarnSlack);
            if (!addedSlack.IsOk) {
                return addedSlack;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>配平警告松弛 / The trim warning slack</summary>
    /// <remarks>
    /// **类型是 `ISymbol`**：三个分支造的是 `LinearFunctionSymbolAdapter&lt;Flt64&gt;`（函数符号），第四个造的是 `LinearExpressionSymbol`（普通符号），
    /// `warnSlack` 也是这样两种类型混用。统一成 `IIntermediateSymbol` 让 `model.Add(IIntermediateSymbol)` 都能吃下。
    ///
    /// **The type is `IIntermediateSymbol`**: three branches build a `LinearFunctionSymbolAdapter&lt;Flt64&gt;` (a function symbol) and the fourth builds a `LinearExpressionSymbol` (a plain one), which
    /// is how its `warnSlack` also mixes the two. `IIntermediateSymbol` keeps both acceptable to `model.Add(IIntermediateSymbol)`.
    /// </remarks>
    public IIntermediateSymbol? WarnSlack { get; private set; }
}
