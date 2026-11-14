#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>在提交辅助变量前构建机制模型及矩阵 / Build a mechanism model and matrix before committing auxiliary variables.</summary>
public static class MechanismModelBuildExtensions {
    /// <summary>构建线性机制模型和矩阵 / Build a linear mechanism model and matrix.</summary>
    /// <remarks>成功时须分别释放模型和矩阵；不恢复外部回调副作用，不支持并发或重入修改。取消仅在检查点生效，最后检查后仍可能提交。 / Dispose both model and matrix on success; external callback effects are not restored and concurrent or reentrant mutation is unsupported. Cancellation is observed at checkpoints and may arrive too late to prevent commit.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">源元模型 / Source meta-model.</param>
    /// <param name="fixedVariables">用于准备符号及消元的固定值 / Fixed values for symbol preparation and elimination.</param>
    /// <param name="registrationStatusCallBack">注册状态回调 / Registration status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>机制模型及矩阵，或失败结果 / Mechanism model and matrix, or a failure.</returns>
    public static Task<Result<(LinearMechanismModel<V> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>>> BuildLinearTriadModelAsync<V>(
        this LinearMetaModel<V>? model,
        IReadOnlyDictionary<IVariableItem, V>? fixedVariables = null,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        CancellationToken cancellationToken = default)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (model is null) {
            return Task.FromResult(Failure<(LinearMechanismModel<V>, LinearTriadModel)>(
                ErrorCode.IllegalArgument, "元模型为空 / Meta-model is null"));
        }
        return BuildAsync<V, LinearMechanismModel<V>, LinearTriadModel>(
            fixedVariables: fixedVariables,
            cancellationToken: cancellationToken,
            build: (fixedSnapshot, beforeCommit) => LinearMechanismModel<V>.InvokeBeforeCommitAsync(
                metaModel: model,
                fixedVariables: fixedSnapshot,
                registrationStatusCallBack: registrationStatusCallBack,
                beforeCommit: beforeCommit
            ),
            dump: (mechanism, fixedSnapshot) => mechanism.ToLinearTriadModel(
                fixedVariables: fixedSnapshot,
                cancellationToken: cancellationToken
            )
        );
    }

    /// <summary>构建二次机制模型和矩阵 / Build a quadratic mechanism model and matrix.</summary>
    /// <remarks>成功时须分别释放模型和矩阵；不恢复外部回调副作用，不支持并发或重入修改。取消仅在检查点生效，最后检查后仍可能提交。 / Dispose both model and matrix on success; external callback effects are not restored and concurrent or reentrant mutation is unsupported. Cancellation is observed at checkpoints and may arrive too late to prevent commit.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">源元模型 / Source meta-model.</param>
    /// <param name="fixedVariables">用于准备符号及消元的固定值 / Fixed values for symbol preparation and elimination.</param>
    /// <param name="registrationStatusCallBack">注册状态回调 / Registration status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>机制模型及矩阵，或失败结果 / Mechanism model and matrix, or a failure.</returns>
    public static Task<Result<(QuadraticMechanismModel<V> Model, QuadraticTetradModel Matrix), ErrorCode, Error<ErrorCode>>> BuildQuadraticTetradModelAsync<V>(
        this QuadraticMetaModel<V>? model,
        IReadOnlyDictionary<IVariableItem, V>? fixedVariables = null,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        CancellationToken cancellationToken = default)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (model is null) {
            return Task.FromResult(Failure<(QuadraticMechanismModel<V>, QuadraticTetradModel)>(
                ErrorCode.IllegalArgument, "元模型为空 / Meta-model is null"));
        }
        return BuildAsync<V, QuadraticMechanismModel<V>, QuadraticTetradModel>(
            fixedVariables: fixedVariables,
            cancellationToken: cancellationToken,
            build: (fixedSnapshot, beforeCommit) => QuadraticMechanismModel<V>.InvokeBeforeCommitAsync(
                metaModel: model,
                fixedVariables: fixedSnapshot,
                registrationStatusCallBack: registrationStatusCallBack,
                beforeCommit: beforeCommit
            ),
            dump: (mechanism, fixedSnapshot) => mechanism.ToQuadraticTetradModel(
                fixedVariables: fixedSnapshot,
                cancellationToken: cancellationToken
            )
        );
    }

    private static async Task<Result<(TModel Model, TMatrix Matrix), ErrorCode, Error<ErrorCode>>> BuildAsync<V, TModel, TMatrix>(
        IReadOnlyDictionary<IVariableItem, V>? fixedVariables,
        CancellationToken cancellationToken,
        Func<IReadOnlyDictionary<IVariableItem, V>?, Func<TModel, Try>, Task<Result<TModel, ErrorCode, Error<ErrorCode>>>> build,
        Func<TModel, IReadOnlyDictionary<IVariableItem, V>?, Result<TMatrix, ErrorCode, Error<ErrorCode>>> dump)
        where V : struct, IRealNumber<V>, INumberField<V>
        where TModel : class, IDisposable
        where TMatrix : class, IDisposable {
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled<(TModel, TMatrix)>();
        }
        Dictionary<IVariableItem, V>? fixedSnapshot;
        try {
            fixedSnapshot = fixedVariables is null ? null : new Dictionary<IVariableItem, V>(fixedVariables);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure<(TModel, TMatrix)>(
                ErrorCode.IllegalArgument,
                $"无法读取固定变量 / Cannot read fixed variables: {error.Message}");
        }
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled<(TModel, TMatrix)>();
        }

        TMatrix? matrix = null;
        Result<TModel, ErrorCode, Error<ErrorCode>> result;
        try {
            result = await build(fixedSnapshot, mechanism => {
                Result<TMatrix, ErrorCode, Error<ErrorCode>> dumped = dump(mechanism, fixedSnapshot);
                if (!dumped.IsOk) {
                    return dumped.Map(_ => Results.SuccessInstance);
                }
                matrix = dumped.Value;
                return cancellationToken.IsCancellationRequested
                    ? Cancelled<Success>()
                    : Results.OkInstance;
            });
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            matrix?.Dispose();
            return Failure<(TModel, TMatrix)>(
                ErrorCode.ApplicationError,
                $"模型构建失败：{error.Message} / Model construction failed: {error.Message}");
        }
        if (result is Failed<TModel, ErrorCode, Error<ErrorCode>> failed) {
            matrix?.Dispose();
            return Results.Failed<(TModel, TMatrix)>(failed.Error);
        }
        if (result is Fatal<TModel, ErrorCode, Error<ErrorCode>> fatal) {
            matrix?.Dispose();
            return new Fatal<(TModel, TMatrix), ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        if (matrix is null) {
            result.Value.Dispose();
            return Failure<(TModel, TMatrix)>(ErrorCode.ApplicationError, "矩阵构建结果无效 / Invalid matrix build result");
        }
        return Results.Ok((result.Value, matrix));
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Cancelled<T>() =>
        Failure<T>(ErrorCode.ApplicationStopped, "模型构建已取消 / Model construction was cancelled");

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(ErrorCode code, string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));
}
