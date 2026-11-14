#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Plugin.Cplex;

internal sealed class CplexLinearDualCapture {
    internal bool CaptureFarkas { get; init; }
    internal IReadOnlyList<Flt64>? RowDuals { get; set; }
    internal IReadOnlyList<Flt64>? FarkasDuals { get; set; }
}

internal sealed record CplexLinearOutcome(
    SolveReport<Flt64> Report,
    IReadOnlyList<Flt64>? RowDuals,
    IReadOnlyList<Flt64>? FarkasDuals);

internal static class CplexDualSupport {



    internal static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result,
        string message) {
        if (result is Failed<S, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<T>(failed.Error);
        }
        if (result is Fatal<S, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        return Results.Failed<T>(new Err<ErrorCode>(
            ErrorCode.OREngineSolvingException,
            message));
    }

}

