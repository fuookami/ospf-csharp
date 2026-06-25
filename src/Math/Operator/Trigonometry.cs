#nullable enable

namespace Fuookami.Ospf.Math.Operator
{
    /// <summary>
    /// 三角函数接口 / Trigonometry Interface
    /// </summary>
    /// <typeparam name="TRet">返回值类型 / Return type</typeparam>
    public interface ITrigonometry<out TRet>
    {
        // Basic trigonometric functions
        /// <summary>正弦 sin(x) / Sine</summary>
        TRet Sin();
        /// <summary>余弦 cos(x) / Cosine</summary>
        TRet Cos();
        /// <summary>正割 sec(x) / Secant</summary>
        TRet? Sec();
        /// <summary>余割 csc(x) / Cosecant</summary>
        TRet? Csc();
        /// <summary>正切 tan(x) / Tangent</summary>
        TRet? Tan();
        /// <summary>余切 cot(x) / Cotangent</summary>
        TRet? Cot();

        // Inverse trigonometric functions
        /// <summary>反正弦 asin(x) / Arcsine</summary>
        TRet? Asin();
        /// <summary>反余弦 acos(x) / Arccosine</summary>
        TRet? Acos();
        /// <summary>反正割 asec(x) / Arcsecant</summary>
        TRet? Asec();
        /// <summary>反余割 acsc(x) / Arccosecant</summary>
        TRet? Acsc();
        /// <summary>反正切 atan(x) / Arctangent</summary>
        TRet Atan();
        /// <summary>反余切 acot(x) / Arccotangent</summary>
        TRet? Acot();

        // Hyperbolic functions
        /// <summary>双曲正弦 sinh(x) / Hyperbolic sine</summary>
        TRet Sinh();
        /// <summary>双曲余弦 cosh(x) / Hyperbolic cosine</summary>
        TRet Cosh();
        /// <summary>双曲正割 sech(x) / Hyperbolic secant</summary>
        TRet Sech();
        /// <summary>双曲余割 csch(x) / Hyperbolic cosecant</summary>
        TRet? Csch();
        /// <summary>双曲正切 tanh(x) / Hyperbolic tangent</summary>
        TRet Tanh();
        /// <summary>双曲余切 coth(x) / Hyperbolic cotangent</summary>
        TRet? Coth();

        // Inverse hyperbolic functions
        /// <summary>反双曲正弦 asinh(x) / Inverse hyperbolic sine</summary>
        TRet Asinh();
        /// <summary>反双曲余弦 acosh(x) / Inverse hyperbolic cosine</summary>
        TRet? Acosh();
        /// <summary>反双曲正割 asech(x) / Inverse hyperbolic secant</summary>
        TRet? Asech();
        /// <summary>反双曲余割 acsch(x) / Inverse hyperbolic cosecant</summary>
        TRet? Acsch();
        /// <summary>反双曲正切 atanh(x) / Inverse hyperbolic tangent</summary>
        TRet? Atanh();
        /// <summary>反双曲余切 acoth(x) / Inverse hyperbolic cotangent</summary>
        TRet? Acoth();
    }

    /// <summary>
    /// 三角函数辅助方法 / Trigonometry helper methods
    /// </summary>
    public static class TrigonometryOperators
    {
        /// <summary>正弦 / Sine</summary>
        public static TRet Sin<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Sin();
        /// <summary>余弦 / Cosine</summary>
        public static TRet Cos<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Cos();
        /// <summary>正割 / Secant</summary>
        public static TRet? Sec<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Sec();
        /// <summary>余割 / Cosecant</summary>
        public static TRet? Csc<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Csc();
        /// <summary>正切 / Tangent</summary>
        public static TRet? Tan<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Tan();
        /// <summary>余切 / Cotangent</summary>
        public static TRet? Cot<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Cot();
        /// <summary>反正弦 / Arcsine</summary>
        public static TRet? Asin<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Asin();
        /// <summary>反余弦 / Arccosine</summary>
        public static TRet? Acos<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Acos();
        /// <summary>反正割 / Arcsecant</summary>
        public static TRet? Asec<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Asec();
        /// <summary>反余割 / Arccosecant</summary>
        public static TRet? Acsc<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Acsc();
        /// <summary>反正切 / Arctangent</summary>
        public static TRet Atan<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Atan();
        /// <summary>反余切 / Arccotangent</summary>
        public static TRet? Acot<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Acot();
        /// <summary>双曲正弦 / Hyperbolic sine</summary>
        public static TRet Sinh<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Sinh();
        /// <summary>双曲余弦 / Hyperbolic cosine</summary>
        public static TRet Cosh<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Cosh();
        /// <summary>双曲正割 / Hyperbolic secant</summary>
        public static TRet Sech<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Sech();
        /// <summary>双曲余割 / Hyperbolic cosecant</summary>
        public static TRet? Csch<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Csch();
        /// <summary>双曲正切 / Hyperbolic tangent</summary>
        public static TRet Tanh<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Tanh();
        /// <summary>双曲余切 / Hyperbolic cotangent</summary>
        public static TRet? Coth<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Coth();
        /// <summary>反双曲正弦 / Inverse hyperbolic sine</summary>
        public static TRet Asinh<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Asinh();
        /// <summary>反双曲余弦 / Inverse hyperbolic cosine</summary>
        public static TRet? Acosh<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Acosh();
        /// <summary>反双曲正割 / Inverse hyperbolic secant</summary>
        public static TRet? Asech<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Asech();
        /// <summary>反双曲余割 / Inverse hyperbolic cosecant</summary>
        public static TRet? Acsch<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Acsch();
        /// <summary>反双曲正切 / Inverse hyperbolic tangent</summary>
        public static TRet? Atanh<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Atanh();
        /// <summary>反双曲余切 / Inverse hyperbolic cotangent</summary>
        public static TRet? Acoth<T, TRet>(T x) where T : ITrigonometry<TRet> => x.Acoth();
    }
}
