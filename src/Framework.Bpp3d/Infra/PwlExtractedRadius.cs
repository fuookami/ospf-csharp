// 从 PWL 求解结果中提取的半径 / The radius extracted from a PWL solve.
// 与固定 Kotlin 中的 `PWLExtractedRadius` 对应。
// .

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;

/// <summary>
/// 从 PWL 求解结果中提取的半径 / The radius extracted from a PWL solve.
/// </summary>
/// <param name="VariableName">变量名 / The variable name.</param>
/// <param name="SolverRadius">求解器给出的半径 / The radius the solver chose.</param>
/// <param name="SolverRadiusSquared">求解器给出的 r²（近似值）/ The r² the solver produced (an approximation).</param>
/// <param name="ActualRadiusSquared">真实的 r² / The true r².</param>
/// <param name="PwlAbsoluteError">PWL 绝对误差 / The PWL absolute error.</param>
/// <param name="PwlRelativeError">PWL 相对误差 / The PWL relative error.</param>
/// <param name="IsWithinEnvelope">是否落在保守包络内 / Whether it lies inside the conservative envelope.</param>
/// <param name="Envelope">保守包络 / The conservative envelope.</param>
/// <param name="PwlApproximation">PWL 近似 / The PWL approximation.</param>
/// <remarks>
/// **`SolverRadiusSquared` 与 `ActualRadiusSquared` 是两个不同的数，不能互相顶替**：前者是求解器在弦上算出的
/// 占用代理，后者是真实几何。用前者当体积会系统性偏低/偏高（弦在 `r²` 上方，所以代理偏大、体积偏大），
/// 用后者当约束则与模型实际约束的不是同一个量。两个都留着，并在诊断里分别输出。
///
/// **`SolverRadiusSquared` and `ActualRadiusSquared` are two different numbers and cannot stand in for each other**: the former is the footprint proxy the solver works
/// with on the chord, the latter is the real geometry. Using the proxy as the volume biases it systematically (the chord lies above `r²`, so the proxy is larger and so
/// is the volume), while using the true value in a constraint would not be the quantity the model actually constrained. Both are kept and reported separately.
/// </remarks>
public sealed record PwlExtractedRadius(
    string VariableName,
    FltX SolverRadius,
    FltX SolverRadiusSquared,
    FltX ActualRadiusSquared,
    FltX PwlAbsoluteError,
    FltX PwlRelativeError,
    bool IsWithinEnvelope,
    ConservativeRadiusEnvelope Envelope,
    PwlRadiusSquaredApproximation PwlApproximation) {
    /// <summary>
    /// 用真实半径平方计算圆柱体积 / The cylinder volume from the true r².
    /// </summary>
    /// <param name="height">圆柱高度 / The cylinder height.</param>
    /// <param name="pi">圆周率 / Pi.</param>
    /// <returns>真实体积 / The actual volume.</returns>
    public FltX ActualVolume(FltX height, FltX pi) => pi * ActualRadiusSquared * height;

    /// <summary>
    /// 用 PWL 近似半径平方计算圆柱体积 / The cylinder volume from the PWL r².
    /// </summary>
    /// <param name="height">圆柱高度 / The cylinder height.</param>
    /// <param name="pi">圆周率 / Pi.</param>
    /// <returns>PWL 近似体积 / The PWL volume.</returns>
    public FltX PwlVolume(FltX height, FltX pi) => pi * SolverRadiusSquared * height;

    /// <summary>
    /// 诊断信息 / Diagnostic info.
    /// </summary>
    /// <returns>键值对 / Key-value pairs.</returns>
    public IReadOnlyDictionary<string, string> Info() {
        string prefix = "pwl_radius_" + VariableName;
        return new Dictionary<string, string> {
            [prefix + "_r"] = SolverRadius.ToFlt64().ToDouble().ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            [prefix + "_q"] = SolverRadiusSquared.ToFlt64().ToDouble().ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            [prefix + "_r_squared"] = ActualRadiusSquared.ToFlt64().ToDouble().ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            [prefix + "_abs_error"] = PwlAbsoluteError.ToFlt64().ToDouble().ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            [prefix + "_rel_error"] = PwlRelativeError.ToFlt64().ToDouble().ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            [prefix + "_within_envelope"] = IsWithinEnvelope.ToString(),
            [prefix + "_pwl_max_rel_error"] = PwlApproximation.MaxRelativeError.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// 由求解器结果构造 / Build from a solver result.
    /// </summary>
    /// <param name="variableName">变量名 / The variable name.</param>
    /// <param name="solverRadius">求解器半径 / The solver radius.</param>
    /// <param name="solverRadiusSquared">求解器 r² / The solver r².</param>
    /// <param name="envelope">保守包络 / The conservative envelope.</param>
    /// <param name="approximation">PWL 近似 / The PWL approximation.</param>
    /// <returns>提取结果 / The extracted radius.</returns>
    /// <remarks>
    /// 误差由 `approximation` 现场算出，**不由调用方传入**：让调用方传误差等于让它有机会传错，
    /// 而这个字段正是用来判断近似是否可信的。包络判定复用 `IsRadiusValid`，避免两处半径合法性判断各自演化。
    ///
    /// The errors are computed from `approximation` on the spot, **not supplied by the caller**: letting the caller pass them in means it can pass them wrong, and this is
    /// exactly the field used to judge whether the approximation can be trusted. The envelope decision reuses `IsRadiusValid` so the two radius-validity checks cannot
    /// drift apart.
    /// </remarks>
    public static PwlExtractedRadius FromSolver(
        string variableName,
        FltX solverRadius,
        FltX solverRadiusSquared,
        ConservativeRadiusEnvelope envelope,
        PwlRadiusSquaredApproximation approximation) {
        System.ArgumentNullException.ThrowIfNull(envelope);
        System.ArgumentNullException.ThrowIfNull(approximation);

        FltX actualSquared = solverRadius * solverRadius;
        // **误差走十进制重载**：`double` 重载要把半径降级再升级，两次舍入会让"刚好在容差上"的判断随机翻转。
        // **The error goes through the decimal overload**: the `double` one lowers the radius and raises the result, and two roundings make a borderline verdict flip at random.
        FltX absoluteError = approximation.ActualError(solverRadius);

        return new PwlExtractedRadius(
            VariableName: variableName,
            SolverRadius: solverRadius,
            SolverRadiusSquared: solverRadiusSquared,
            ActualRadiusSquared: actualSquared,
            PwlAbsoluteError: absoluteError,
            PwlRelativeError: actualSquared.ToFlt64().ToDouble() < 1e-12
                ? FltX.Zero
                : absoluteError / actualSquared,
            IsWithinEnvelope: envelope.IsRadiusValid(new Quantity<FltX>(solverRadius, envelope.RMax.Unit)),
            Envelope: envelope,
            PwlApproximation: approximation);
    }
}
