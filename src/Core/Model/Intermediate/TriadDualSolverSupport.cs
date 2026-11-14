#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;

namespace Fuookami.Ospf.Core.Model.Intermediate;
/// <summary>三元模型对偶求解支持 / Triad model dual solving support.</summary>
public static class TriadDualSolverSupport {
    /// <summary>求解对偶模型并取回原始约束的行对偶值 / Solve the dual model and return row duals for the original constraints.</summary>
    /// <remarks>
    /// 用于后端逐行对偶不可信时的复核路径：先由 <see cref="LinearTriadModel.Dual()"/> 构造对偶模型，再交给
    /// <paramref name="solver"/> 求解；求解失败或无解时原样传播，不伪造零对偶。行到机制约束的映射由调用方按行索引完成。
    /// Verification path for untrusted backend row duals: build the dual model with
    /// <see cref="LinearTriadModel.Dual()"/>, then solve it with <paramref name="solver"/>. Failures and missing
    /// solutions propagate unchanged instead of fabricating zero duals. Callers map rows back to mechanism constraints
    /// by row index.
    /// </remarks>
    /// <param name="model">原始线性三元模型 / Original linear triad model.</param>
    /// <param name="solver">求解对偶模型的线性求解器 / Linear solver used for the dual model.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>与原始约束行同序的对偶值或错误 / Row-aligned dual values or an error.</returns>
    public static async Task<Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>> SolveDualAsync(
        LinearTriadModel? model,
        ILinearSolver? solver,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        return await SolveBuiltDualAsync(
            model: model,
            solver: solver,
            build: linearModel => linearModel.Dual(),
            buildFailure: "无法构建对偶模型 / Cannot build the dual model",
            solveFailure: "对偶模型未返回解 / Dual model returned no solution",
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);
    }

    /// <summary>求解 Farkas 对偶模型并取回原始约束的逐行乘子 / Solve the Farkas dual model and return row multipliers for the original constraints.</summary>
    /// <remarks>
    /// 用于不可行 LP 的证书提取：先由 <see cref="LinearTriadModel.FarkasDual()"/> 构造 Farkas 对偶模型，再交给
    /// <paramref name="solver"/> 求解；归一化行使乘子不可能全零，因此返回值就是原始约束行的 Farkas 乘子。
    /// 失败与无解原样传播，不伪造零证书。行到机制约束的映射由调用方按行索引完成。
    /// Certificate extraction for an infeasible LP: build the model with <see cref="LinearTriadModel.FarkasDual()"/>,
    /// then solve it with <paramref name="solver"/>. The normalization row makes an all-zero certificate impossible, so
    /// the returned values are the Farkas multipliers of the original rows. Failures and missing solutions propagate
    /// unchanged instead of fabricating a zero certificate. Callers map rows back to mechanism constraints by row index.
    /// </remarks>
    /// <param name="model">原始线性三元模型 / Original linear triad model.</param>
    /// <param name="solver">求解 Farkas 对偶模型的线性求解器 / Linear solver used for the Farkas dual model.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>与原始约束行同序的 Farkas 乘子或错误 / Row-aligned Farkas multipliers or an error.</returns>
    public static Task<Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>> SolveFarkasDualAsync(
        LinearTriadModel? model,
        ILinearSolver? solver,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) => SolveBuiltDualAsync(
            model: model,
            solver: solver,
            build: linearModel => linearModel.FarkasDual(),
            buildFailure: "无法构建 Farkas 对偶模型 / Cannot build the Farkas dual model",
            solveFailure: "Farkas 对偶模型未返回解 / Farkas dual model returned no solution",
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);

    /// <summary>构建并求解派生对偶模型的公共流程 / Shared flow that builds and solves a derived dual model.</summary>
    private static async Task<Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>> SolveBuiltDualAsync(
        LinearTriadModel? model,
        ILinearSolver? solver,
        Func<LinearTriadModel, Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> build,
        string buildFailure,
        string solveFailure,
        SolvingStatusCallBack? solvingStatusCallBack,
        CancellationToken cancellationToken) {
        if (model is null) {
            return Results.Failed<IReadOnlyList<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "原始模型为空 / Original model is null"));
        }
        if (solver is null) {
            return Results.Failed<IReadOnlyList<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "缺少求解对偶模型的线性求解器 / No linear solver for the dual model"));
        }
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> dual = build(model);
        if (dual is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> dualOk) {
            return Propagate<IReadOnlyList<Flt64>, LinearTriadModel>(
                dual, buildFailure);
        }
        using LinearTriadModel dualModel = dualOk.Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved = await solver.InvokeAsync(
            model: (ILinearTriadModelView)dualModel,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);
        if (solved is not Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solvedOk) {
            return Propagate<IReadOnlyList<Flt64>, FeasibleSolverOutput<Flt64>>(
                solved, solveFailure);
        }
        return model.TidyDualSolution(solvedOk.Value.Solution.Values);
    }

    /// <summary>传播非成功结果 / Propagate a non-success result.</summary>
    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result,
        string message) {
        if (result is Failed<S, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<T>(failed.Error);
        }
        if (result is Fatal<S, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        return Results.Failed<T>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, message));
    }
}
