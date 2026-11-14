#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Plugin.Scip;

internal static class ScipSnapshotSupport {
    internal static Result<ScipColumn[], ErrorCode, Error<ErrorCode>> CaptureColumns(
        IReadOnlyList<ModelViewVariable> variables,
        CancellationToken cancellationToken) {
        var columns = new ScipColumn[variables.Count];
        for (int i = 0; i < columns.Length; i++) {
            if (cancellationToken.IsCancellationRequested) {
                return Cancelled();
            }
            ModelViewVariable variable = variables[i];
            if (variable.Index != i || variable.Type is null) {
                return Failure("变量索引或类型无效 / Invalid variable index or type");
            }
            double? initial = variable.InitialResult?.ToDouble();
            if (initial is { } initialValue && !double.IsFinite(initialValue)) {
                return Failure("变量初始值无效 / Invalid variable initial value");
            }
            double lower = variable.LowerBound.ToDouble();
            double upper = variable.UpperBound.ToDouble();
            if (double.IsNaN(lower) || double.IsNaN(upper) || double.IsPositiveInfinity(lower)
                || double.IsNegativeInfinity(upper) || lower > upper) {
                return Failure("变量边界无效 / Invalid variable bounds");
            }
            int type;
            switch (variable.Type) {
                case Binary:
                    type = 0;
                    lower = System.Math.Max(lower, 0);
                    upper = System.Math.Min(upper, 1);
                    break;
                case Ternary:
                    type = 1;
                    lower = System.Math.Max(lower, 0);
                    upper = System.Math.Min(upper, 2);
                    break;
                case BalancedTernary:
                    type = 1;
                    lower = System.Math.Max(lower, -1);
                    upper = System.Math.Min(upper, 1);
                    break;
                case UInteger:
                    type = 1;
                    lower = System.Math.Max(lower, 0);
                    break;
                case Integer:
                    type = 1;
                    break;
                case Percentage:
                    type = 3;
                    lower = System.Math.Max(lower, 0);
                    upper = System.Math.Min(upper, 1);
                    break;
                case UContinuous:
                    type = 3;
                    lower = System.Math.Max(lower, 0);
                    break;
                case Continuous:
                    type = 3;
                    break;
                default:
                    return Unsupported("不支持自定义变量类型 / Custom variable types are unsupported");
            }
            if (lower > upper) {
                return Failure("变量边界与类型值域不相交 / Variable bounds do not intersect its type domain");
            }
            // shortcut: 仅接收二进制64精确有限整数边界；支持精确整数域后放宽 / Accept only finite binary64-exact integer bounds; relax when exact integer domains are supported.
            if (type != 3 && (!double.IsFinite(lower) || !double.IsFinite(upper)
                || lower < -9007199254740991d || upper > 9007199254740991d)) {
                return Unsupported("整数变量边界须在二进制64精确整数范围内且有限 / Integer bounds must be finite and within the binary64 exact-integer range");
            }
            columns[i] = new ScipColumn(
                Lower: lower,
                Upper: upper,
                Type: type,
                Initial: initial);
        }
        return Results.Ok(columns);
    }

    private static Result<ScipColumn[], ErrorCode, Error<ErrorCode>> Failure(string message) =>
        Results.Failed<ScipColumn[]>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private static Result<ScipColumn[], ErrorCode, Error<ErrorCode>> Unsupported(string message) =>
        Results.Failed<ScipColumn[]>(new Err<ErrorCode>(ErrorCode.Other, message));

    private static Result<ScipColumn[], ErrorCode, Error<ErrorCode>> Cancelled() =>
        Results.Failed<ScipColumn[]>(new Err<ErrorCode>(
            ErrorCode.ApplicationStopped,
            "模型快照已取消 / Model snapshot was cancelled"));
}
