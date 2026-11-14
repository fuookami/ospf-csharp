#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Distribution;

/// <summary>
/// 概率分布采样接口 / Probability distribution sampling interface.
/// 随机性由调用方注入的均匀熵源提供 / Randomness is supplied by an injected uniform entropy source.
/// </summary>
public interface Distribution {
    /// <summary>校验分布参数 / Validate distribution parameters.</summary>
    Result<Success, ErrorCode, Error<ErrorCode>> Validate();

    /// <summary>使用已校验参数采样 / Sample with validated parameters.</summary>
    Flt64 Sample(Func<Flt64?> entropy);

    /// <summary>校验后采样 / Validate, then sample.</summary>
    Result<Flt64, ErrorCode, Error<ErrorCode>> Invoke(Func<Flt64?> entropy)
        => Validate().Map(_ => Sample(entropy));
}

/// <summary>概率分布采样扩展 / Probability distribution sampling extensions.</summary>
public static class DistributionExtensions {
    /// <summary>校验后采样 / Validate, then sample.</summary>
    public static Result<Flt64, ErrorCode, Error<ErrorCode>> Invoke(
        this Distribution distribution,
        Func<Flt64?> entropy)
        => distribution.Validate().Map(_ => distribution.Sample(entropy));
}

internal static class DistributionMath {
    internal const double UnitEpsilon = 1.0e-12;

    internal static Result<Success, ErrorCode, Error<ErrorCode>> Ok() => Results.OkInstance;

    internal static Result<T, ErrorCode, Error<ErrorCode>> Failed<T>(string message)
        => new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);

    internal static double UniformUnit(Func<Flt64?> entropy) {
        Flt64? sample = entropy();
        if (sample is null) {
            return 0.0;
        }

        double value = sample.Value.ToDouble();
        if (double.IsNaN(value) || value <= 0.0) {
            return 0.0;
        }
        if (value >= 1.0) {
            return 1.0 - UnitEpsilon;
        }
        return value;
    }

    internal static double StandardGaussian(Func<Flt64?> entropy) {
        double u1 = System.Math.Max(UniformUnit(entropy), UnitEpsilon);
        double u2 = UniformUnit(entropy);
        return System.Math.Sqrt(-2.0 * System.Math.Log(u1))
            * System.Math.Cos(2.0 * System.Math.PI * u2);
    }
}

/// <summary>均匀分布 / Uniform distribution.</summary>
public sealed record Uniform(Flt64 LowerBound, Flt64 UpperBound) : Distribution {
    public Uniform() : this(Flt64.Zero, Flt64.One) { }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        return LowerBound.ToDouble() <= UpperBound.ToDouble()
            ? DistributionMath.Ok()
            : DistributionMath.Failed<Success>("下界不得大于上界 / lowerBound must not exceed upperBound");
    }

    /// <inheritdoc/>
    public Flt64 Sample(Func<Flt64?> entropy) {
        double lower = LowerBound.ToDouble();
        double width = UpperBound.ToDouble() - lower;
        return new Flt64(lower + DistributionMath.UniformUnit(entropy) * width);
    }
}

/// <summary>高斯（正态）分布 / Gaussian (normal) distribution.</summary>
public sealed record Gaussian(Flt64 Mean, Flt64 StandardDeviation) : Distribution {
    public Gaussian() : this(Flt64.Zero, Flt64.One) { }
    public Gaussian(Flt64 StandardDeviation) : this(Flt64.Zero, StandardDeviation) { }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        return StandardDeviation.ToDouble() >= 0.0
            ? DistributionMath.Ok()
            : DistributionMath.Failed<Success>("标准差不得为负 / standardDeviation must be non-negative");
    }

    /// <inheritdoc/>
    public Flt64 Sample(Func<Flt64?> entropy) {
        return new Flt64(
            Mean.ToDouble()
            + StandardDeviation.ToDouble() * DistributionMath.StandardGaussian(entropy));
    }
}

/// <summary>柯西分布 / Cauchy distribution.</summary>
public sealed record Cauchy(Flt64 Location, Flt64 Scale) : Distribution {
    public Cauchy() : this(Flt64.Zero, Flt64.One) { }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        return Scale.ToDouble() > 0.0
            ? DistributionMath.Ok()
            : DistributionMath.Failed<Success>("尺度参数必须为正 / scale must be positive");
    }

    /// <inheritdoc/>
    public Flt64 Sample(Func<Flt64?> entropy) {
        double u = DistributionMath.UniformUnit(entropy);
        return new Flt64(Location.ToDouble() + Scale.ToDouble() * System.Math.Tan(System.Math.PI * (u - 0.5)));
    }
}

/// <summary>指数分布 / Exponential distribution.</summary>
public sealed record Exponential(Flt64 Rate) : Distribution {
    public Exponential() : this(Flt64.One) { }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        return Rate.ToDouble() > 0.0
            ? DistributionMath.Ok()
            : DistributionMath.Failed<Success>("速率参数必须为正 / rate must be positive");
    }

    /// <inheritdoc/>
    public Flt64 Sample(Func<Flt64?> entropy) {
        double u = System.Math.Max(DistributionMath.UniformUnit(entropy), DistributionMath.UnitEpsilon);
        return new Flt64(-System.Math.Log(1.0 - u) / Rate.ToDouble());
    }
}

/// <summary>伽马分布 / Gamma distribution.</summary>
public sealed record Gamma(Flt64 Shape, Flt64 Scale) : Distribution {
    public Gamma() : this(Flt64.One, Flt64.One) { }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        if (Shape.ToDouble() <= 0.0) {
            return DistributionMath.Failed<Success>("形状参数必须为正 / shape must be positive");
        }
        if (Scale.ToDouble() <= 0.0) {
            return DistributionMath.Failed<Success>("尺度参数必须为正 / scale must be positive");
        }
        return DistributionMath.Ok();
    }

    /// <inheritdoc/>
    public Flt64 Sample(Func<Flt64?> entropy) {
        double k = Shape.ToDouble();
        if (k < 1.0) {
            double u = System.Math.Max(DistributionMath.UniformUnit(entropy), DistributionMath.UnitEpsilon);
            double baseSample = new Gamma(new Flt64(k + 1.0), Scale).Sample(entropy).ToDouble();
            return new Flt64(baseSample * System.Math.Pow(u, 1.0 / k));
        }

        double d = k - 1.0 / 3.0;
        double c = 1.0 / System.Math.Sqrt(9.0 * d);
        while (true) {
            double x = DistributionMath.StandardGaussian(entropy);
            double v = 1.0 + c * x;
            if (v <= 0.0) {
                continue;
            }

            double v3 = v * v * v;
            double u = DistributionMath.UniformUnit(entropy);
            if (u <= 0.0) {
                continue;
            }
            if (System.Math.Log(u) < 0.5 * x * x + d - d * v3 + d * System.Math.Log(v3)) {
                return new Flt64(d * v3 * Scale.ToDouble());
            }
        }
    }
}

/// <summary>贝塔分布 / Beta distribution.</summary>
public sealed record Beta(Flt64 Alpha, Flt64 BetaShape) : Distribution {
    public Beta() : this(Flt64.One, Flt64.One) { }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        if (Alpha.ToDouble() <= 0.0) {
            return DistributionMath.Failed<Success>("α 必须为正 / alpha must be positive");
        }
        if (BetaShape.ToDouble() <= 0.0) {
            return DistributionMath.Failed<Success>("β 必须为正 / beta must be positive");
        }
        return DistributionMath.Ok();
    }

    /// <inheritdoc/>
    public Flt64 Sample(Func<Flt64?> entropy) {
        double x = new Gamma(Alpha, Flt64.One).Sample(entropy).ToDouble();
        double y = new Gamma(BetaShape, Flt64.One).Sample(entropy).ToDouble();
        double sum = x + y;
        return new Flt64(sum <= 0.0 ? 0.5 : x / sum);
    }
}

/// <summary>学生 t 分布 / Student's t distribution.</summary>
public sealed record StudentT(Flt64 DegreesOfFreedom) : Distribution {
    public StudentT() : this(Flt64.One) { }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        return DegreesOfFreedom.ToDouble() > 0.0
            ? DistributionMath.Ok()
            : DistributionMath.Failed<Success>("自由度必须为正 / degreesOfFreedom must be positive");
    }

    /// <inheritdoc/>
    public Flt64 Sample(Func<Flt64?> entropy) {
        double dof = DegreesOfFreedom.ToDouble();
        double z = DistributionMath.StandardGaussian(entropy);
        double chiSquared = new Gamma(new Flt64(dof / 2.0), new Flt64(2.0)).Sample(entropy).ToDouble();
        return new Flt64(z / System.Math.Sqrt(chiSquared / dof));
    }
}

/// <summary>对称莱维稳定分布 / Symmetric Lévy stable distribution.</summary>
public sealed record Levy(Flt64 Alpha) : Distribution {
    public Levy() : this(new Flt64(1.5)) { }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        double alpha = Alpha.ToDouble();
        return alpha > 0.0 && alpha <= 2.0
            ? DistributionMath.Ok()
            : DistributionMath.Failed<Success>("α 必须位于 (0, 2] / alpha must be within (0, 2]");
    }

    /// <inheritdoc/>
    public Flt64 Sample(Func<Flt64?> entropy) {
        double alpha = Alpha.ToDouble();
        if (alpha >= 2.0) {
            return new Flt64(DistributionMath.StandardGaussian(entropy) * System.Math.Sqrt(2.0));
        }

        double u = System.Math.Clamp(
            DistributionMath.UniformUnit(entropy),
            DistributionMath.UnitEpsilon,
            1.0 - DistributionMath.UnitEpsilon);
        double v = (u - 0.5) * System.Math.PI;
        if (alpha == 1.0) {
            return new Flt64(System.Math.Tan(v));
        }

        double w = -System.Math.Log(System.Math.Max(
            DistributionMath.UniformUnit(entropy),
            DistributionMath.UnitEpsilon));
        double first = System.Math.Sin(alpha * v) / System.Math.Pow(System.Math.Cos(v), 1.0 / alpha);
        double second = System.Math.Pow(
            System.Math.Cos(v - alpha * v) / w,
            (1.0 - alpha) / alpha);
        return new Flt64(first * second);
    }
}

/// <summary>截断分布包装 / Truncated distribution wrapper.</summary>
public sealed record Truncated(
    Distribution Distribution,
    Flt64 LowerBound,
    Flt64 UpperBound,
    int MaxAttempts = 128) : Distribution {
    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        if (LowerBound.ToDouble() > UpperBound.ToDouble()) {
            return DistributionMath.Failed<Success>("下界不得大于上界 / lowerBound must not exceed upperBound");
        }
        if (MaxAttempts <= 0) {
            return DistributionMath.Failed<Success>("最大尝试次数必须为正 / maxAttempts must be positive");
        }
        return Distribution.Validate();
    }

    /// <inheritdoc/>
    public Flt64 Sample(Func<Flt64?> entropy) {
        double lower = LowerBound.ToDouble();
        double upper = UpperBound.ToDouble();
        for (int attempt = 0; attempt < MaxAttempts; attempt++) {
            Flt64 value = Distribution.Sample(entropy);
            if (value.ToDouble() >= lower && value.ToDouble() <= upper) {
                return value;
            }
        }

        return new Flt64(System.Math.Clamp(Distribution.Sample(entropy).ToDouble(), lower, upper));
    }
}

/// <summary>单位超球面方向采样器 / Unit hypersphere direction sampler.</summary>
public sealed class DirectionSampler(int dimension) {
    /// <summary>维度 / Dimensionality.</summary>
    public int Dimension { get; } = dimension;

    /// <summary>校验采样维度 / Validate the sampling dimension.</summary>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        return Dimension > 0
            ? DistributionMath.Ok()
            : DistributionMath.Failed<Success>("维度必须为正 / dimension must be positive");
    }

    /// <summary>采样单位方向向量 / Sample a unit direction vector.</summary>
    public IReadOnlyList<Flt64> Sample(Func<Flt64?> entropy) {
        double[] raw = new double[Dimension];
        double squaredNorm = 0.0;
        for (int index = 0; index < raw.Length; index++) {
            raw[index] = DistributionMath.StandardGaussian(entropy);
            squaredNorm += raw[index] * raw[index];
        }

        double norm = System.Math.Sqrt(squaredNorm);
        if (norm <= DistributionMath.UnitEpsilon) {
            var fallback = new Flt64[Dimension];
            fallback[0] = Flt64.One;
            return fallback;
        }

        var result = new Flt64[Dimension];
        for (int index = 0; index < raw.Length; index++) {
            result[index] = new Flt64(raw[index] / norm);
        }
        return result;
    }

    /// <summary>校验后采样 / Validate, then sample.</summary>
    public Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> Invoke(Func<Flt64?> entropy)
        => Validate().Map(_ => Sample(entropy));
}
