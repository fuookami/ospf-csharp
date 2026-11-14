#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Token;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Model.Mechanism;

/// <summary>将求解器解回填到来源模型 / Apply solver solutions to the source model.</summary>
public static class MechanismModelSolutionExtensions {
    /// <summary>在没有解向量时检查线性矩阵的 token 快照 / Check a linear matrix token snapshot without a solution vector.</summary>
    /// <remarks>检查来源直接结构、机制行及原矩阵内容；不验证嵌套结构或证据。 / Checks direct source structure, mechanism rows and original matrix contents; does not validate nested structure or evidence.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">来源机制模型 / Source mechanism model.</param>
    /// <param name="matrix">求解所用的原矩阵 / Original matrix used for solving.</param>
    /// <returns>快照校验结果 / Snapshot validation result.</returns>
    public static Try ValidateSolverTokenSnapshot<V>(this LinearMechanismModel<V>? model, LinearTriadModel? matrix)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        ValidateSnapshot(
            model: model,
            parent: model?.Parent,
            mapping: matrix?.SolutionMapping,
            variables: matrix?.Variables,
            matrix: matrix
        );

    /// <summary>同时检查来源矩阵和实际求解的连续松弛矩阵 / Check both the source matrix and the continuous relaxation used for solving.</summary>
    /// <remarks>调用方须提供实际求解的矩阵并避免并发修改；不检查原生求解器模型或证明。 / The caller must provide the matrix actually solved and avoid concurrent mutation; native solver models and proofs are not checked.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">来源机制模型 / Source mechanism model.</param>
    /// <param name="matrix">直接转储的来源矩阵 / Directly dumped source matrix.</param>
    /// <param name="solvedMatrix">由来源矩阵创建的连续松弛矩阵 / Continuous relaxation created from the source matrix.</param>
    /// <returns>快照校验结果 / Snapshot validation result.</returns>
    public static Try ValidateSolverTokenSnapshot<V>(
        this LinearMechanismModel<V>? model,
        LinearTriadModel? matrix,
        LinearTriadModel? solvedMatrix)
        where V : struct, IRealNumber<V>, INumberField<V> {
        Try source = model.ValidateSolverTokenSnapshot(matrix);
        return source.IsOk ? ValidateRelaxation(matrix, solvedMatrix) : source;
    }

    /// <summary>在没有解向量时检查二次矩阵的 token 快照 / Check a quadratic matrix token snapshot without a solution vector.</summary>
    /// <remarks>检查来源直接结构、机制行及原矩阵内容；不验证嵌套结构或证据。 / Checks direct source structure, mechanism rows and original matrix contents; does not validate nested structure or evidence.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">来源机制模型 / Source mechanism model.</param>
    /// <param name="matrix">求解所用的原矩阵 / Original matrix used for solving.</param>
    /// <returns>快照校验结果 / Snapshot validation result.</returns>
    public static Try ValidateSolverTokenSnapshot<V>(this QuadraticMechanismModel<V>? model, QuadraticTetradModel? matrix)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        ValidateSnapshot(
            model: model,
            parent: model?.Parent,
            mapping: matrix?.SolutionMapping,
            variables: matrix?.Variables,
            matrix: matrix
        );

    /// <summary>还原消去固定列的线性求解结果 / Restore a linear solution with eliminated fixed columns.</summary>
    /// <remarks>仅接受此机制模型直接转储的原矩阵；不验证所有结构变化或解的可行性。 / Requires the original matrix dumped from this mechanism; does not validate all structural changes or solution feasibility.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">来源机制模型 / Source mechanism model.</param>
    /// <param name="matrix">求解所用的原矩阵 / Original matrix used for solving.</param>
    /// <param name="solution">按矩阵列顺序排列的解 / Solution in matrix column order.</param>
    /// <returns>按机制模型列顺序排列的完整解或错误 / Complete solution in mechanism column order or an error.</returns>
    public static Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> RestoreSolverSolution<V>(
        this LinearMechanismModel<V>? model,
        LinearTriadModel? matrix,
        IReadOnlyList<Flt64>? solution)
        where V : struct, IRealNumber<V>, INumberField<V> => Restore(
            model: model,
            parent: model?.Parent,
            mapping: matrix?.SolutionMapping,
            variables: matrix?.Variables,
            matrix: matrix,
            solution: solution
        );

    /// <summary>验证松弛矩阵并还原消去固定列的求解结果 / Validate the relaxation and restore eliminated fixed columns.</summary>
    /// <remarks>不验证原生求解器模型变更、解的可行性或并发修改。 / Does not validate native solver model changes, solution feasibility, or concurrent mutation.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">来源机制模型 / Source mechanism model.</param>
    /// <param name="matrix">直接转储的来源矩阵 / Directly dumped source matrix.</param>
    /// <param name="solvedMatrix">实际求解的连续松弛矩阵 / Continuous relaxation used for solving.</param>
    /// <param name="solution">按矩阵列顺序排列的解 / Solution in matrix column order.</param>
    /// <returns>完整解或错误 / Complete solution or an error.</returns>
    public static Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> RestoreSolverSolution<V>(
        this LinearMechanismModel<V>? model,
        LinearTriadModel? matrix,
        LinearTriadModel? solvedMatrix,
        IReadOnlyList<Flt64>? solution)
        where V : struct, IRealNumber<V>, INumberField<V> => Restore(
            model: model,
            parent: model?.Parent,
            mapping: matrix?.SolutionMapping,
            variables: matrix?.Variables,
            matrix: matrix,
            solution: solution,
            validateSolvedMatrix: () => ValidateRelaxation(matrix, solvedMatrix)
        );

    /// <summary>还原消去固定列的二次求解结果 / Restore a quadratic solution with eliminated fixed columns.</summary>
    /// <remarks>仅接受此机制模型直接转储的原矩阵；不验证所有结构变化或解的可行性。 / Requires the original matrix dumped from this mechanism; does not validate all structural changes or solution feasibility.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">来源机制模型 / Source mechanism model.</param>
    /// <param name="matrix">求解所用的原矩阵 / Original matrix used for solving.</param>
    /// <param name="solution">按矩阵列顺序排列的解 / Solution in matrix column order.</param>
    /// <returns>按机制模型列顺序排列的完整解或错误 / Complete solution in mechanism column order or an error.</returns>
    public static Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> RestoreSolverSolution<V>(
        this QuadraticMechanismModel<V>? model,
        QuadraticTetradModel? matrix,
        IReadOnlyList<Flt64>? solution)
        where V : struct, IRealNumber<V>, INumberField<V> => Restore(
            model: model,
            parent: model?.Parent,
            mapping: matrix?.SolutionMapping,
            variables: matrix?.Variables,
            matrix: matrix,
            solution: solution
        );

    /// <summary>还原并回填线性求解结果 / Restore and apply a linear solver solution.</summary>
    /// <remarks>从转储到回填期间不得修改模型；不验证所有结构变化或解的可行性。 / Do not mutate the model between dumping and application; not all structural changes or solution feasibility are checked.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">来源机制模型 / Source mechanism model.</param>
    /// <param name="matrix">求解所用的原矩阵 / Original matrix used for solving.</param>
    /// <param name="solution">按矩阵列顺序排列的解 / Solution in matrix column order.</param>
    /// <returns>回填结果 / Application result.</returns>
    public static Try ApplySolverSolution<V>(
        this LinearMechanismModel<V>? model,
        LinearTriadModel? matrix,
        IReadOnlyList<Flt64>? solution)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        model.RestoreSolverSolution(matrix, solution).Bind(values => model is null
            ? Failure(ErrorCode.IllegalArgument, "机制模型为空 / Mechanism model is null")
            : Apply(
                model: model,
                parent: model.Parent,
                solution: values,
                validateMatrix: () => model.ValidateSolverTokenSnapshot(matrix)
            ));

    /// <summary>验证松弛矩阵并回填求解结果 / Validate the relaxation and apply the solver solution.</summary>
    /// <remarks>不验证原生求解器模型变更、解的可行性或并发修改。 / Does not validate native solver model changes, solution feasibility, or concurrent mutation.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">来源机制模型 / Source mechanism model.</param>
    /// <param name="matrix">直接转储的来源矩阵 / Directly dumped source matrix.</param>
    /// <param name="solvedMatrix">实际求解的连续松弛矩阵 / Continuous relaxation used for solving.</param>
    /// <param name="solution">按矩阵列顺序排列的解 / Solution in matrix column order.</param>
    /// <returns>回填结果 / Application result.</returns>
    public static Try ApplySolverSolution<V>(
        this LinearMechanismModel<V>? model,
        LinearTriadModel? matrix,
        LinearTriadModel? solvedMatrix,
        IReadOnlyList<Flt64>? solution)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        model.RestoreSolverSolution(
            matrix: matrix,
            solvedMatrix: solvedMatrix,
            solution: solution
        ).Bind(values => model is null
            ? Failure(ErrorCode.IllegalArgument, "机制模型为空 / Mechanism model is null")
            : Apply(
                model: model,
                parent: model.Parent,
                solution: values,
                validateMatrix: () => model.ValidateSolverTokenSnapshot(matrix, solvedMatrix)
            ));

    /// <summary>还原并回填二次求解结果 / Restore and apply a quadratic solver solution.</summary>
    /// <remarks>从转储到回填期间不得修改模型；不验证所有结构变化或解的可行性。 / Do not mutate the model between dumping and application; not all structural changes or solution feasibility are checked.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">来源机制模型 / Source mechanism model.</param>
    /// <param name="matrix">求解所用的原矩阵 / Original matrix used for solving.</param>
    /// <param name="solution">按矩阵列顺序排列的解 / Solution in matrix column order.</param>
    /// <returns>回填结果 / Application result.</returns>
    public static Try ApplySolverSolution<V>(
        this QuadraticMechanismModel<V>? model,
        QuadraticTetradModel? matrix,
        IReadOnlyList<Flt64>? solution)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        model.RestoreSolverSolution(matrix, solution).Bind(values => model is null
            ? Failure(ErrorCode.IllegalArgument, "机制模型为空 / Mechanism model is null")
            : Apply(
                model: model,
                parent: model.Parent,
                solution: values,
                validateMatrix: () => model.ValidateSolverTokenSnapshot(matrix)
            ));

    /// <summary>按线性机制模型的列顺序回填完整解 / Apply a complete solution in linear-mechanism column order.</summary>
    /// <remarks>从转储到回填期间不得修改模型；此方法不验证所有结构变化。 / Do not mutate the model between dumping and application; not all structural changes are checked.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">产生列顺序的机制模型 / Mechanism model defining column order.</param>
    /// <param name="solution">完整求解器结果 / Complete solver result.</param>
    /// <returns>回填结果 / Application result.</returns>
    public static Try ApplySolverSolution<V>(this LinearMechanismModel<V>? model, IReadOnlyList<Flt64>? solution)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        model is null ? Failure(ErrorCode.IllegalArgument, "机制模型为空 / Mechanism model is null") : Apply(
            model: model,
            parent: model.Parent,
            solution: solution
        );

    /// <summary>按二次机制模型的列顺序回填完整解 / Apply a complete solution in quadratic-mechanism column order.</summary>
    /// <remarks>从转储到回填期间不得修改模型；此方法不验证所有结构变化。 / Do not mutate the model between dumping and application; not all structural changes are checked.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">产生列顺序的机制模型 / Mechanism model defining column order.</param>
    /// <param name="solution">完整求解器结果 / Complete solver result.</param>
    /// <returns>回填结果 / Application result.</returns>
    public static Try ApplySolverSolution<V>(this QuadraticMechanismModel<V>? model, IReadOnlyList<Flt64>? solution)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        model is null ? Failure(ErrorCode.IllegalArgument, "机制模型为空 / Mechanism model is null") : Apply(
            model: model,
            parent: model.Parent,
            solution: solution
        );

    private static Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> Restore<V>(
        BasicMechanismModel<V>? model,
        AbstractMetaModel<V>? parent,
        MechanismModelSolutionMapping? mapping,
        IReadOnlyList<ModelViewVariable>? variables,
        object? matrix,
        IReadOnlyList<Flt64>? solution,
        Func<Try>? validateSolvedMatrix = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (model is null || parent is null || mapping is null || variables is null || matrix is null) {
            return Results.Failed<IReadOnlyList<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "矩阵或来源模型无效 / Invalid matrix or source model"));
        }
        if (model.IsDisposed || parent.IsClosed) {
            return Results.Failed<IReadOnlyList<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "模型已关闭 / Model is closed"));
        }
        Try CheckSnapshot() {
            Try source = ValidateSnapshot(
                model: model,
                parent: parent,
                mapping: mapping,
                variables: variables,
                matrix: matrix
            );
            return source.IsOk && validateSolvedMatrix is not null ? validateSolvedMatrix() : source;
        }
        Try sourceCheck = CheckSnapshot();
        if (!sourceCheck.IsOk) {
            return sourceCheck.Map<IReadOnlyList<Flt64>>(_ => Array.Empty<Flt64>());
        }
        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> restored = mapping.Restore(
            source: model.SolutionIdentity,
            variables: variables,
            solution: solution
        );
        if (!restored.IsOk) {
            return restored;
        }
        sourceCheck = CheckSnapshot();
        if (!sourceCheck.IsOk) {
            return sourceCheck.Map<IReadOnlyList<Flt64>>(_ => Array.Empty<Flt64>());
        }
        return ValidateValues(restored.Value).Map(_ => restored.Value);
    }

    private static Try ValidateRelaxation(LinearTriadModel? matrix, LinearTriadModel? solvedMatrix) =>
        solvedMatrix is null
            ? Failure(ErrorCode.IllegalArgument, "松弛矩阵为空 / Relaxed matrix is null")
            : solvedMatrix.ValidateRelaxation(matrix);

    private static Try ValidateSnapshot<V>(
        BasicMechanismModel<V>? model,
        AbstractMetaModel<V>? parent,
        MechanismModelSolutionMapping? mapping,
        IReadOnlyList<ModelViewVariable>? variables,
        object? matrix)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (model is null || parent is null || mapping is null || variables is null || matrix is null) {
            return Failure(ErrorCode.IllegalArgument, "矩阵或来源模型无效 / Invalid matrix or source model");
        }
        if (model.IsDisposed || parent.IsClosed) {
            return Failure(ErrorCode.ApplicationStopped, "模型已关闭 / Model is closed");
        }
        Try columns = mapping.Validate(model.SolutionIdentity, variables);
        if (!columns.IsOk) {
            return columns;
        }
        Try structure = mapping.ValidateStructure(
            model: model,
            parent: parent,
            matrix: matrix
        );
        if (!structure.IsOk) {
            return structure;
        }
        IReadOnlyList<Token<V>> tokens = model.Tokens.TokensInSolver;
        if (!mapping.MatchesTokens(tokens)) {
            return Failure(ErrorCode.IllegalArgument, "机制模型变量已变更，请重新求解 / Mechanism tokens changed; solve again");
        }
        Try source = ValidateTokens(
            model: model,
            parent: parent,
            expected: tokens,
            checkVariables: false
        );
        return source.IsOk ? mapping.ValidateVariables(tokens) : source;
    }

    private static Try Apply<V>(
        BasicMechanismModel<V> model,
        AbstractMetaModel<V> parent,
        IReadOnlyList<Flt64>? solution,
        Func<Try>? validateMatrix = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (model.IsDisposed || parent.IsClosed) {
            return Failure(ErrorCode.ApplicationStopped, "模型已关闭 / Model is closed");
        }
        if (solution is null) {
            return Failure(ErrorCode.IllegalArgument, "求解器结果为空 / Solver solution is null");
        }

        IReadOnlyList<Token<V>> expected = model.Tokens.TokensInSolver;
        Try CheckSource() {
            Try tokens = ValidateTokens(
                model: model,
                parent: parent,
                expected: expected,
                checkVariables: validateMatrix is null
            );
            return tokens.IsOk && validateMatrix is not null ? validateMatrix() : tokens;
        }
        Try sourceCheck = CheckSource();
        if (!sourceCheck.IsOk) {
            return sourceCheck;
        }
        Flt64[] values;
        try {
            if (solution.Count != expected.Count) {
                return Failure(ErrorCode.IllegalArgument, "求解器结果长度无效 / Invalid solver solution length");
            }
            values = new Flt64[expected.Count];
            for (int i = 0; i < values.Length; i++) {
                values[i] = solution[i];
            }
            if (solution.Count != values.Length) {
                return Failure(ErrorCode.IllegalArgument, "求解器结果长度已改变 / Solver solution length changed");
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure(ErrorCode.IllegalArgument, $"无法读取求解器结果 / Cannot read solver solution: {error.Message}");
        }
        Try validation = ValidateValues(values);
        if (validation.IsOk) {
            validation = ValidateConversions(values, parent.Converter);
        }
        if (validation.IsOk) {
            validation = CheckSource();
        }
        if (!validation.IsOk) {
            return validation;
        }

        try {
            model.Tokens.Flush();
            parent.Tokens.SetSolverSolution(values);
            return Results.OkInstance;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            // shortcut: 回调副作用无法回滚，支持回调隔离后恢复原值 / Callback effects cannot be rolled back; restore values when callback isolation is available.
            return new Fatal<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                $"回填失败，模型可能已受损，请重建 / Solution application failed; model may be inconsistent, rebuild it: {error.Message}"));
        }
    }

    private static Try ValidateValues(IReadOnlyList<Flt64> values) {
        if (values.Any(value => !double.IsFinite(value.ToDouble()))) {
            return Failure(ErrorCode.IllegalArgument, "求解器结果数值无效 / Invalid solver solution values");
        }
        return Results.OkInstance;
    }

    private static Try ValidateConversions<V>(IReadOnlyList<Flt64> values, IFlt64ValueConverter<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            foreach (Flt64 value in values) {
                V decoded = converter.IntoValue(value);
                if (!MechanismModelDumpVariables.TryConvert(decoded, out double encoded) ||
                    converter.FromValue(decoded).ToDouble() != encoded ||
                    (value.ToDouble() == 0) != (encoded == 0) ||
                    value.ToDouble() != 0 && System.Math.Sign(value.ToDouble()) != System.Math.Sign(encoded) ||
                    NumericType<V>.Integer && !NumericValueValidation.IsExactIntegerConversion(decoded, value)) {
                    return Failure(ErrorCode.IllegalArgument, "求解器结果无法转换 / Solver solution cannot be converted");
                }
            }
            return Results.OkInstance;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure(ErrorCode.IllegalArgument, $"求解器结果转换失败 / Solver solution conversion failed: {error.Message}");
        }
    }

    private static class NumericType<V> where V : struct {
        internal static readonly bool Integer = typeof(V).GetInterfaces()
            .Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IInteger<>));
    }

    private static Try ValidateTokens<V>(
        BasicMechanismModel<V> model,
        AbstractMetaModel<V> parent,
        IReadOnlyList<Token<V>> expected,
        bool checkVariables)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (model.IsDisposed || parent.IsClosed) {
            return Failure(ErrorCode.ApplicationStopped, "模型已关闭 / Model is closed");
        }
        IReadOnlyList<Token<V>> current = parent.Tokens.TokensInSolver;
        if (current.Count != expected.Count || !current.Zip(expected).All(pair => ReferenceEquals(pair.First, pair.Second))) {
            return Failure(ErrorCode.IllegalArgument, "模型变量已变更，请重新求解 / Model tokens changed; solve again");
        }
        Try sourceCheck = model.ValidateSourceSnapshot(checkVariables);
        if (!sourceCheck.IsOk) {
            return sourceCheck;
        }
        return model.ValidateBoundDependencies();
    }

    private static Try Failure(ErrorCode code, string message) => Results.Failed<Success>(new Err<ErrorCode>(code, message));
}
