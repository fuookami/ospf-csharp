#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver.Value
{
    /// <summary>
    /// 求解值验证工具。
    /// Solve value validation utility.
    /// </summary>
    public static class SolveValueValidation
    {
        /// <summary>
        /// 验证求解器 Flt64 值是否有效（非 NaN、非无穷）。
        /// Validate that a solver Flt64 value is valid (not NaN, not infinite).
        /// </summary>
        public static Try ValidateSolverFlt64Value(Flt64 value, string name = "")
        {
            var d = value.ToDouble();
            if (double.IsNaN(d))
            {
                return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, $"Solver value '{name}' is NaN."));
            }
            if (double.IsInfinity(d))
            {
                return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, $"Solver value '{name}' is infinite."));
            }
            return Results.Ok(Results.SuccessInstance);
        }

        /// <summary>
        /// 验证求解器 Flt64 边界是否有效。
        /// Validate that a solver Flt64 bound is valid.
        /// </summary>
        public static Try ValidateSolverFlt64Bound(Flt64 lowerBound, Flt64 upperBound, string name = "")
        {
            if (lowerBound.Gr(upperBound))
            {
                return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ORSolutionInvalid,
                    $"Solver bound '{name}' has lower bound ({lowerBound}) > upper bound ({upperBound})."));
            }
            return Results.Ok(Results.SuccessInstance);
        }

        /// <summary>
        /// 验证线性模型值转换。
        /// Validate linear model value conversion.
        /// </summary>
        public static Try ValidateLinearModelValueConversion(Flt64 value, SolveValueConversionPolicy policy, string name = "")
        {
            if (policy == SolveValueConversionPolicy.Strict)
            {
                return ValidateSolverFlt64Value(value, name);
            }
            return Results.Ok(Results.SuccessInstance);
        }

        /// <summary>
        /// 验证二次模型值转换。
        /// Validate quadratic model value conversion.
        /// </summary>
        public static Try ValidateQuadraticModelValueConversion(Flt64 value, SolveValueConversionPolicy policy, string name = "")
        {
            if (policy == SolveValueConversionPolicy.Strict)
            {
                return ValidateSolverFlt64Value(value, name);
            }
            return Results.Ok(Results.SuccessInstance);
        }
    }
}
