#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using System.Collections.Frozen;
using System.Linq;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

internal sealed record GurobiBackendIdentity(string SolverId, string BackendName) {
    internal static readonly GurobiBackendIdentity Gurobi = new("gurobi", "Gurobi");
    internal static readonly GurobiBackendIdentity Gurobi11 = new("gurobi11", "Gurobi 11");
}

internal static class GurobiReportSupport {
    internal static SolveReport<Flt64> Cancelled(
        SolverConfig config,
        bool quadratic,
        GurobiBackendIdentity identity,
        string? nativeVersion) {
        (SolverProvenance provenance, SolveFingerprints fingerprints) =
            Metadata(config, quadratic, identity, nativeVersion);
        return new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.Unknown,
            TerminationReason = TerminationReason.Cancelled,
            SolutionPresence = SolutionPresence.None,
            Provenance = provenance,
            Fingerprints = fingerprints,
        };
    }

    internal static (SolverProvenance Provenance, SolveFingerprints Fingerprints) Metadata(
        SolverConfig config,
        bool quadratic,
        GurobiBackendIdentity identity,
        string? nativeVersion) {
        // **能力声明按证据写，不按"看起来应该支持"写**。三条都有代码出处：
        // `Dual` / `Farkas` ← `GurobiBendersDecompositionSolver` 里 `Get(GRB.DoubleAttr.Pi)` 与
        // `Get(GRB.DoubleAttr.FarkasDual)` 的实测取值（之前这里写的是 `false`，**在报告里低于实际能力**）；
        // `NativeIis` 起改为 `true`：Gurobi 现在有真的原生 IIS 分析器（`GurobiNativeIisInfeasibilityAnalyzer`），
        // 它读 `iisConfig`、跑 `computeIIS`、并在真实后端上验过。**此前写 `false` 的理由是旧入口不读 `iisConfig`——
        // 那个理由对诊断路径已经不成立了。**
        //
        // **但要说清它现在管什么**：`LinearIisCompute` 传给编排器的是 `solver.Descriptor?.Capabilities`，
        // 而 Gurobi 没有覆写 `Descriptor`（默认 `null`），**所以这个标志当前不参与过滤**，它只是报告里的能力声明。
        // 改它不是为了让分析器能跑——分析器本来就能跑；改它是因为**声明和实现对不上了**。
        //
        // **Capabilities are written from evidence, not from "it ought to support that"** . Each of the three has a code origin: `Dual` / `Farkas` come from
        // the measured `Get(GRB.DoubleAttr.Pi)` and `Get(GRB.DoubleAttr.FarkasDual)` reads in `GurobiBendersDecompositionSolver` (before this said `false`, i.e. the
        // report **understated the real backend**); `NativeIis` became `true`, when a real native-IIS analyzer (`GurobiNativeIisInfeasibilityAnalyzer`) started
        // reading `iisConfig`, running `computeIIS` and being verified on the real backend. **The old reason for `false` — that the legacy entry never reads `iisConfig` — no
        // longer holds for the diagnostic path.** **Be precise about what it governs now**: `LinearIisCompute` hands the orchestrator `solver.Descriptor?.Capabilities`, and
        // Gurobi does not override `Descriptor` (it defaults to `null`), **so this flag does not currently take part in filtering** — it is a capability declaration in the
        // report. Changing it is not what lets the analyzer run (it already does); it is because **the declaration and the implementation stopped agreeing.**
        var capabilities = new SolverCapabilities(
            ModelTypes: (quadratic
                ? new[] { SolverModelType.Qp, SolverModelType.Qcp, SolverModelType.Mip }
                : new[] { SolverModelType.Lp, SolverModelType.Mip }).ToFrozenSet(),
            NativeIis: true,
            Dual: true,
            Farkas: true,
            WarmStart: true,
            SolutionPool: true,
            Callback: true,
            Interrupt: true);
        SolverProvenance provenance = config.Provenance(
            new SolverDescriptor(
                SolverId: identity.SolverId,
                BackendName: identity.BackendName,
                Capabilities: capabilities,
                BackendVersion: nativeVersion),
            nativeVersion);
        return (
            provenance,
            new SolveFingerprints(
                Configuration: SolveFingerprinting.Configuration(provenance.EffectiveParameters),
                Solver: SolveFingerprinting.Solver(provenance.Descriptor)));
    }

    internal static Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ToLegacy(
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result) {
        if (result is Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<FeasibleSolverOutput<Flt64>>(failed.Error);
        }
        if (result is Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        if (result is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "未知求解结果 / Unknown solve result"));
        }
        SolveReport<Flt64> report = ok.Value;
        if (report.ProblemStatus == ProblemStatus.Feasible &&
            report.Solution is { Objective: { } objective } solution &&
            report.Statistics.SolveTime is { } time) {
            return Results.Ok(new FeasibleSolverOutput<Flt64>(
                Obj: objective,
                Solution: solution.Values,
                Time: time,
                PossibleBestObj: report.Statistics.BestBound ?? objective,
                Gap: report.Statistics.Gap ?? Flt64.Zero,
                Iterations: report.Statistics.Iterations,
                NodeCount: report.Statistics.Nodes,
                BestBound: report.Statistics.BestBound,
                MipGap: report.Statistics.Gap,
                SolveTime: time));
        }
        ErrorCode code = report.ProblemStatus switch {
            ProblemStatus.Infeasible => ErrorCode.ORModelInfeasible,
            ProblemStatus.Unbounded => ErrorCode.ORModelUnbounded,
            ProblemStatus.InfeasibleOrUnbounded => ErrorCode.ORModelInfeasibleOrUnbounded,
            _ when report.TerminationReason == TerminationReason.Cancelled => ErrorCode.ApplicationStopped,
            _ => ErrorCode.OREngineSolvingException,
        };
        return Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(
            code,
            $"旧接口无法返回无解报告：{report.TerminationReason} / Legacy interface cannot return a report without a solution: {report.TerminationReason}"));
    }
}
