#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using ScaleBase = Fuookami.Ospf.Utils.Functional.Either<Fuookami.Ospf.Math.Algebra.Number.FltX, Fuookami.Ospf.Math.Algebra.Number.RtnX>;

namespace Fuookami.Ospf.Math;
/// <summary>
/// 科学计数法缩放因子 / Scientific scaling factor
/// </summary>
public sealed record Scale(IReadOnlyList<(ScaleBase Base, FltX Index)> Scales) {
    // Predefined scale constants
    /// <summary>阿托 (10^-18) / Atto</summary>
    public static readonly Scale Atto = Invoke(10, -18);
    /// <summary>飞托 (10^-15) / Femto</summary>
    public static readonly Scale Femto = Invoke(10, -15);
    /// <summary>皮可 (10^-12) / Pico</summary>
    public static readonly Scale Pico = Invoke(10, -12);
    /// <summary>纳诺 (10^-9) / Nano</summary>
    public static readonly Scale Nano = Invoke(10, -9);
    /// <summary>微 (10^-6) / Micro</summary>
    public static readonly Scale Micro = Invoke(10, -6);
    /// <summary>毫 (10^-3) / Milli</summary>
    public static readonly Scale Milli = Invoke(10, -3);
    /// <summary>厘 (10^-2) / Centi</summary>
    public static readonly Scale Centi = Invoke(10, -2);
    /// <summary>分 (10^-1) / Deci</summary>
    public static readonly Scale Deci = Invoke(10, -1);
    /// <summary>十 (10^1) / Deca</summary>
    public static readonly Scale Deca = Invoke(10, 1);
    /// <summary>百 (10^2) / Hecto</summary>
    public static readonly Scale Hecto = Invoke(10, 2);
    /// <summary>千 (10^3) / Kilo</summary>
    public static readonly Scale Kilo = Invoke(10, 3);
    /// <summary>兆 (10^6) / Mega</summary>
    public static readonly Scale Mega = Invoke(10, 6);
    /// <summary>吉 (10^9) / Giga</summary>
    public static readonly Scale Giga = Invoke(10, 9);
    /// <summary>太 (10^12) / Tera</summary>
    public static readonly Scale Tera = Invoke(10, 12);
    /// <summary>拍 (10^15) / Peta</summary>
    public static readonly Scale Peta = Invoke(10, 15);
    /// <summary>艾 (10^18) / Exa</summary>
    public static readonly Scale Exa = Invoke(10, 18);

    /// <summary>创建缩放因子 / Create scaling factor</summary>
    public static Scale Invoke(FltX base_, FltX index) =>
        new(new List<(ScaleBase, FltX)> { (new ScaleBase.Left(base_), index) });

    /// <summary>创建缩放因子 / Create scaling factor</summary>
    public static Scale Invoke(FltX base_, int index = 1) =>
        Invoke(base_, new FltX(index));

    /// <summary>创建缩放因子 / Create scaling factor</summary>
    public static Scale Invoke(double base_, int index = 1) =>
        Invoke(new FltX(base_), new FltX(index));

    /// <summary>创建缩放因子 / Create scaling factor</summary>
    public static Scale Invoke(int base_, int index = 1) =>
        Invoke(new FltX(base_), new FltX(index));

    /// <summary>创建缩放因子 / Create scaling factor</summary>
    public static Scale Invoke(RtnX base_, FltX index) =>
        new(new List<(ScaleBase, FltX)> { (new ScaleBase.Right(base_), index) });

    /// <summary>创建缩放因子 / Create scaling factor</summary>
    public static Scale Invoke(RtnX base_, int index = 1) =>
        Invoke(base_, new FltX(index));

    /// <summary>
    /// 缩放因子值（可能为 null）/ Scaling factor value (may be null)
    /// </summary>
    public FltX? ValueOrNull {
        get {
            try {
                FltX result = FltX.One;
                foreach ((ScaleBase base_, FltX index) in Scales) {
                    FltX baseValue = base_.Match(
                        left => left,
                        right => right.ToFltX()
                    );
                    int exp = (int)index.ToFlt64().ToDouble();
                    if (exp >= 0) {
                        for (int i = 0; i < exp; i++) {
                            result = result * baseValue;
                        }
                    }
                    else {
                        for (int i = 0; i < -exp; i++) {
                            result = result / baseValue;
                        }
                    }
                }
                return result;
            }
            catch {
                return null;
            }
        }
    }

    /// <summary>缩放因子值 / Scaling factor value</summary>
    public FltX? Value => ValueOrNull;

    /// <summary>安全获取缩放因子值 / Safely get scaling factor value</summary>
    public Result<FltX, ErrorCode, Error<ErrorCode>> ValueSafe() {
        FltX? val = ValueOrNull;
        if (val is FltX nonNull) {
            return Results.Ok(nonNull);
        }
        return Results.Failed<FltX>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "Scale value is undefined"));
    }

    /// <summary>一元加运算符 / Unary plus operator</summary>
    public static Scale operator +(Scale s) => s;

    /// <summary>乘以 FltX / Multiply by FltX</summary>
    public Scale Times(FltX other) =>
        new(Scales.Append((new ScaleBase.Left(other), FltX.One)).ToList());

    /// <summary>乘以 RtnX / Multiply by RtnX</summary>
    public Scale Times(RtnX other) =>
        new(Scales.Append((new ScaleBase.Right(other), FltX.One)).ToList());

    /// <summary>除以 FltX / Divide by FltX</summary>
    public Scale? Div(FltX other) => DivOrNull(other);

    /// <summary>除以 FltX（可能为 null）/ Divide by FltX (may be null)</summary>
    public Scale? DivOrNull(FltX other) {
        if (other.Eq(FltX.Zero)) {
            return null;
        }

        return new(Scales.Append((new ScaleBase.Left(other), new FltX(-1))).ToList());
    }

    /// <summary>安全除以 FltX / Safely divide by FltX</summary>
    public Result<Scale, ErrorCode, Error<ErrorCode>> DivSafe(FltX other) {
        Scale? result = DivOrNull(other);
        return result is not null
            ? Results.Ok(result)
            : Results.Failed<Scale>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "Division by zero"));
    }

    /// <summary>除以 RtnX / Divide by RtnX</summary>
    public Scale? Div(RtnX other) => DivOrNull(other);

    /// <summary>除以 RtnX（可能为 null）/ Divide by RtnX (may be null)</summary>
    public Scale? DivOrNull(RtnX other) {
        if (other.Eq(RtnX.Zero)) {
            return null;
        }

        return new(Scales.Append((new ScaleBase.Right(other), new FltX(-1))).ToList());
    }

    /// <summary>安全除以 RtnX / Safely divide by RtnX</summary>
    public Result<Scale, ErrorCode, Error<ErrorCode>> DivSafe(RtnX other) {
        Scale? result = DivOrNull(other);
        return result is not null
            ? Results.Ok(result)
            : Results.Failed<Scale>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "Division by zero"));
    }

    /// <summary>乘以 FltX 运算符 / Multiply by FltX operator</summary>
    public static Scale operator *(Scale lhs, FltX rhs) => lhs.Times(rhs);
    /// <summary>乘以 RtnX 运算符 / Multiply by RtnX operator</summary>
    public static Scale operator *(Scale lhs, RtnX rhs) => lhs.Times(rhs);
    /// <summary>除以 FltX 运算符 / Divide by FltX operator</summary>
    public static Scale? operator /(Scale lhs, FltX rhs) => lhs.DivOrNull(rhs);
    /// <summary>除以 RtnX 运算符 / Divide by RtnX operator</summary>
    public static Scale? operator /(Scale lhs, RtnX rhs) => lhs.DivOrNull(rhs);
    /// <summary>合并缩放因子（乘法）/ Merge scales (multiplication)</summary>
    public static Scale operator *(Scale lhs, Scale rhs) =>
        new(lhs.Scales.Concat(rhs.Scales).ToList());
    /// <summary>合并缩放因子（除法）/ Merge scales (division)</summary>
    public static Scale operator /(Scale lhs, Scale rhs) {
        List<(ScaleBase, FltX)> negated = rhs.Scales
            .Select(s => (s.Base, -s.Index))
            .ToList();
        return new(lhs.Scales.Concat(negated).ToList());
    }
}

/// <summary>缩放因子访问器 / Scale accessors</summary>
public static class ScaleAccessors {
    /// <summary>获取缩放因子值 / Get scale value</summary>
    public static FltX? GetValue(Scale scale) => scale.Value;
}
