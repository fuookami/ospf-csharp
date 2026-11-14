#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>二次机制模型的矩阵转换 / Matrix conversion for a quadratic mechanism model.</summary>
public static class QuadraticMechanismModelDumpExtensions {
    /// <summary>转换为二次四元模型 / Convert to a quadratic tetrad model.</summary>
    /// <remarks>系数按数值类型转换为 Flt64，可能舍入；固定值必须通过转换器往返。转换器检查仅覆盖零、一及固定值，不支持并发修改。 / Coefficients convert to Flt64 and may round; fixed values must round-trip through the converter. Converter checks cover only zero, one and fixed values; concurrent mutation is unsupported.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">机制模型 / Mechanism model.</param>
    /// <param name="fixedVariables">待代换的已注册变量及其值 / Registered variables and values to substitute.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>矩阵模型或转换错误 / Matrix model or a conversion error.</returns>
    public static Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>> ToQuadraticTetradModel<V>(
        this QuadraticMechanismModel<V> model,
        IReadOnlyDictionary<IVariableItem, V>? fixedVariables = null,
        CancellationToken cancellationToken = default)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            return Dump(model, fixedVariables, cancellationToken);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure($"无法转换二次模型：{error.Message} / Cannot convert quadratic model: {error.Message}");
        }
    }

    private static Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>> Dump<V>(
        QuadraticMechanismModel<V> model,
        IReadOnlyDictionary<IVariableItem, V>? fixedVariables,
        CancellationToken cancellationToken)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (model is null) {
            return Failure("机制模型为空 / Mechanism model is null");
        }
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled();
        }
        if (model.IsDisposed || model.Parent.IsClosed) {
            return Results.Failed<QuadraticTetradModel>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped, "模型已关闭 / Model is closed"));
        }
        if (!MechanismModelDumpVariables.TryCaptureConverter(model.Parent.Converter, out (V Zero, V One) converterSnapshot)) {
            return Failure("模型数值转换器无效 / Invalid model value converter");
        }
        Result<Success, ErrorCode, Error<ErrorCode>> sourceCheck = model.ValidateSourceSnapshot(checkVariables: false);
        if (!sourceCheck.IsOk) {
            return MechanismModelSourceSnapshot<V>.Propagate<QuadraticTetradModel>(sourceCheck);
        }
        if (model.ValidateBoundDependencies() is Failed<Success, ErrorCode, Error<ErrorCode>> staleBounds) {
            return Results.Failed<QuadraticTetradModel>(staleBounds.Error);
        }

        IReadOnlyList<Token<V>> tokens = model.Tokens.TokensInSolver;
        Result<MechanismModelSourceSnapshot<V>.VariableSnapshot[], ErrorCode, Error<ErrorCode>> captured =
            MechanismModelDumpVariables.Capture(tokens);
        if (captured is Failed<MechanismModelSourceSnapshot<V>.VariableSnapshot[], ErrorCode, Error<ErrorCode>> failedCapture) {
            return Results.Failed<QuadraticTetradModel>(failedCapture.Error);
        }
        if (captured is Fatal<MechanismModelSourceSnapshot<V>.VariableSnapshot[], ErrorCode, Error<ErrorCode>> fatalCapture) {
            return new Fatal<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>(fatalCapture.Errors);
        }
        var fixedConversions = new List<(V Native, double Encoded)>();
        Result<(Dictionary<VariableItemKey, int> Indexes, List<ModelViewVariable> Variables, Dictionary<VariableItemKey, double> FixedValues), ErrorCode, Error<ErrorCode>> variableResult =
            MechanismModelDumpVariables.Build(
                tokens: tokens,
                fixedVariables: fixedVariables,
                converter: model.Parent.Converter,
                fixedConversions: fixedConversions,
                cancellationToken: cancellationToken
            );
        if (variableResult is Failed<(Dictionary<VariableItemKey, int> Indexes, List<ModelViewVariable> Variables, Dictionary<VariableItemKey, double> FixedValues), ErrorCode, Error<ErrorCode>> failedVariables) {
            return Results.Failed<QuadraticTetradModel>(failedVariables.Error);
        }
        if (variableResult is Fatal<(Dictionary<VariableItemKey, int> Indexes, List<ModelViewVariable> Variables, Dictionary<VariableItemKey, double> FixedValues), ErrorCode, Error<ErrorCode>> fatalVariables) {
            return new Fatal<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>(fatalVariables.Errors);
        }
        (Dictionary<VariableItemKey, int> indexes, List<ModelViewVariable> variables, Dictionary<VariableItemKey, double> fixedValues) = variableResult.Value;

        IReadOnlyList<IConstraint<V, QuadraticCategory>> constraints = ((IAbstractQuadraticMechanismModel<V>)model).Constraints;
        Result<MechanismModelRowsSnapshot<V>, ErrorCode, Error<ErrorCode>> rowsSnapshot = MechanismModelRowsSnapshot<V>.Capture(constraints);
        if (rowsSnapshot is Failed<MechanismModelRowsSnapshot<V>, ErrorCode, Error<ErrorCode>> failedRows) {
            return Results.Failed<QuadraticTetradModel>(failedRows.Error);
        }
        if (rowsSnapshot is Fatal<MechanismModelRowsSnapshot<V>, ErrorCode, Error<ErrorCode>> fatalRows) {
            return new Fatal<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>(fatalRows.Errors);
        }
        var rows = new List<SparseQuadraticVector>(constraints.Count);
        var signs = new List<ConstraintRelation>(constraints.Count);
        var rhs = new List<Flt64>(constraints.Count);
        var names = new List<string>(constraints.Count);
        var sources = new List<ConstraintSource>(constraints.Count);
        foreach (IConstraint<V, QuadraticCategory> constraint in constraints) {
            if (cancellationToken.IsCancellationRequested) {
                return Cancelled();
            }
            if (constraint.Lazy) {
                return Failure($"暂不支持惰性约束：{constraint.Name} / Lazy constraint is not yet supported: {constraint.Name}");
            }
            if (!MechanismModelDumpVariables.TryConvert(constraint.Rhs, out double constraintRhs)
                || (constraint.Sign != ConstraintRelation.LessEqual
                    && constraint.Sign != ConstraintRelation.Equal
                    && constraint.Sign != ConstraintRelation.GreaterEqual)) {
                return Failure($"约束无效：{constraint.Name} / Invalid constraint: {constraint.Name}");
            }
            var coefficients = new Dictionary<(int First, int Second), double>();
            double rowConstant = 0.0;
            foreach (ICell<V> cell in constraint.Lhs) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled();
                }
                if (!TryAccumulate(
                    cell: cell,
                    indexes: indexes,
                    fixedValues: fixedValues,
                    factor: 1.0,
                    coefficients: coefficients,
                    constant: ref rowConstant
                )) {
                    return Failure($"约束项无法解析或系数无效：{constraint.Name} / Unresolved constraint term or invalid coefficient: {constraint.Name}");
                }
            }
            rows.Add(new SparseQuadraticVector(coefficients
                .Where(pair => pair.Value != 0.0)
                .OrderBy(pair => pair.Key.First)
                .ThenBy(pair => pair.Key.Second)
                .Select(pair => new SparseQuadraticEntry(
                    Col1: pair.Key.First,
                    Col2: pair.Key.Second,
                    Value: new Flt64(pair.Value)))
                .ToList()));
            signs.Add(constraint.Sign);
            double rowRhs = constraintRhs - rowConstant;
            if (!double.IsFinite(rowRhs)) {
                return Failure($"固定变量代换溢出：{constraint.Name} / Fixed-variable substitution overflow: {constraint.Name}");
            }
            rhs.Add(new Flt64(rowRhs));
            names.Add(constraint.Name);
            sources.Add(ConstraintSource.Origin);
        }

        IReadOnlyList<object> parts = model.ObjectFunction.SubObjects;
        ObjectCategory direction = parts.Count == 1 && parts[0] is QuadraticSubObject<V> single
            ? single.Category
            : model.ObjectFunction.Category;
        if (direction is not ObjectCategory.Minimum and not ObjectCategory.Maximum) {
            return Failure("目标方向无效 / Invalid objective direction");
        }
        var objectiveCoefficients = new Dictionary<(int First, int Second), double>();
        double constant = 0.0;
        foreach (object part in parts) {
            if (cancellationToken.IsCancellationRequested) {
                return Cancelled();
            }
            if (part is not QuadraticSubObject<V> objective
                || objective.Category is not ObjectCategory.Minimum and not ObjectCategory.Maximum) {
                return Failure("目标项类型或方向无效 / Invalid objective part type or direction");
            }
            double factor = objective.Category == direction ? 1.0 : -1.0;
            if (!MechanismModelDumpVariables.TryConvert(objective.Constant, out double termConstant)) {
                return Failure($"目标常数无效：{objective.Name} / Invalid objective constant: {objective.Name}");
            }
            constant += factor * termConstant;
            if (!double.IsFinite(termConstant) || !double.IsFinite(constant)) {
                return Failure($"目标常数无效：{objective.Name} / Invalid objective constant: {objective.Name}");
            }
            foreach (ICell<V> cell in objective.Cells) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled();
                }
                if (!TryAccumulate(
                    cell: cell,
                    indexes: indexes,
                    fixedValues: fixedValues,
                    factor: factor,
                    coefficients: objectiveCoefficients,
                    constant: ref constant
                )) {
                    return Failure($"目标项无法解析或系数无效：{objective.Name} / Unresolved objective term or invalid coefficient: {objective.Name}");
                }
            }
        }
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled();
        }
        var objectiveCells = objectiveCoefficients
            .Where(pair => pair.Value != 0.0)
            .OrderBy(pair => pair.Key.First)
            .ThenBy(pair => pair.Key.Second)
            .Select(pair => new QuadraticObjectiveCell(
                Col1: pair.Key.First,
                Col2: pair.Key.Second,
                Coefficient: new Flt64(pair.Value)))
            .ToList();
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled();
        }
        if (model.ValidateBoundDependencies() is Failed<Success, ErrorCode, Error<ErrorCode>> changedBounds) {
            return Results.Failed<QuadraticTetradModel>(changedBounds.Error);
        }
        sourceCheck = model.ValidateSourceSnapshot(checkVariables: false);
        if (!sourceCheck.IsOk) {
            return MechanismModelSourceSnapshot<V>.Propagate<QuadraticTetradModel>(sourceCheck);
        }
        Result<Success, ErrorCode, Error<ErrorCode>> variableCheck =
            MechanismModelSourceSnapshot<V>.ValidateVariables(tokens, captured.Value);
        if (!variableCheck.IsOk) {
            return MechanismModelSourceSnapshot<V>.Propagate<QuadraticTetradModel>(variableCheck);
        }
        Result<Success, ErrorCode, Error<ErrorCode>> rowsCheck = rowsSnapshot.Value.Validate(constraints);
        if (!rowsCheck.IsOk) {
            return MechanismModelSourceSnapshot<V>.Propagate<QuadraticTetradModel>(rowsCheck);
        }
        if (!MechanismModelDumpVariables.ConverterCurrent(model.Parent.Converter, converterSnapshot) ||
            !MechanismModelDumpVariables.FixedConversionsCurrent(model.Parent.Converter, fixedConversions)) {
            return Failure("数值转换已变更 / Numeric conversion changed");
        }
        return MatrixIdentitySupport.Materialize(
            registry: model.Parent.IdentityRegistry,
            variables: variables,
            rows: constraints.Select(row => (object?)row.Origin ?? row).ToArray(),
            objectives: parts.Cast<SubObject<V>>().Select(part => part.Origin).ToArray(),
            objectiveFallback: model.ObjectFunction,
            create: (resolvedVariables, identities, objectiveIdentity, revision) => {
                var batch = new QuadraticConstraintBatch(
                    lhs: new SparseQuadraticMatrix(rows),
                    signs: signs,
                    rhs: rhs,
                    names: names,
                    sources: sources,
                    identities: identities,
                    identityNamespace: model.Parent.IdentityRegistry?.Namespace,
                    identitySchemaVersion: model.Parent.IdentityRegistry?.SchemaVersion
                );
                var objective = new Objective<QuadraticObjectiveCell>(
                    category: direction,
                    cells: objectiveCells,
                    constant: new Flt64(constant),
                    identity: objectiveIdentity,
                    identityNamespace: model.Parent.IdentityRegistry?.Namespace,
                    identitySchemaVersion: model.Parent.IdentityRegistry?.SchemaVersion
                );
                var matrix = new QuadraticTetradModel(
                    variables: resolvedVariables,
                    constraints: batch,
                    objective: objective,
                    name: model.Name
                ) {
                    SolutionMapping = MechanismModelSolutionMapping.Create(
                        source: model.SolutionIdentity,
                        tokens: tokens,
                        variableStates: captured.Value,
                        variables: resolvedVariables,
                        indexes: indexes,
                        fixedValues: fixedValues,
                        fixedConversions: fixedConversions,
                        converterSnapshot: converterSnapshot,
                        rows: rowsSnapshot.Value,
                        matrix: MechanismMatrixSnapshot.Capture(batch, objective),
                        identityRevision: revision
                    ),
                };
                Result<Success, ErrorCode, Error<ErrorCode>> identityCheck = matrix.ValidateIdentity();
                if (!identityCheck.IsOk) {
                    matrix.Dispose();
                    return MechanismModelSourceSnapshot<V>.Propagate<QuadraticTetradModel>(identityCheck);
                }
                if (!MechanismModelDumpVariables.ConverterCurrent(model.Parent.Converter, converterSnapshot) ||
                    !MechanismModelDumpVariables.FixedConversionsCurrent(model.Parent.Converter, fixedConversions)) {
                    matrix.Dispose();
                    return Failure("数值转换已变更 / Numeric conversion changed");
                }
                if (cancellationToken.IsCancellationRequested) {
                    matrix.Dispose();
                    return Cancelled();
                }
                return Results.Ok(matrix);
            }
        );
    }

    private static bool TryAccumulate<V>(
        ICell<V> cell,
        IReadOnlyDictionary<VariableItemKey, int> indexes,
        IReadOnlyDictionary<VariableItemKey, double> fixedValues,
        double factor,
        Dictionary<(int First, int Second), double> coefficients,
        ref double constant)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (cell is not IQuadraticCell<V> quadratic) {
            return false;
        }
        if (!MechanismModelDumpVariables.TryConvert(quadratic.Coefficient, out double coefficient)) {
            return false;
        }
        int first = -1;
        if (fixedValues.TryGetValue(quadratic.Token1.Key, out double fixedFirst)) {
            coefficient *= fixedFirst;
        }
        else if (!indexes.TryGetValue(quadratic.Token1.Key, out first)) {
            return false;
        }
        int second = -1;
        if (quadratic.Token2 is { } token2) {
            if (fixedValues.TryGetValue(token2.Key, out double fixedSecond)) {
                coefficient *= fixedSecond;
            }
            else if (!indexes.TryGetValue(token2.Key, out second)) {
                return false;
            }
        }
        if (!double.IsFinite(coefficient)) {
            return false;
        }
        if (first < 0 && second < 0) {
            constant += factor * coefficient;
            return double.IsFinite(constant);
        }
        if (first < 0) {
            first = second;
            second = -1;
        }
        else if (second >= 0 && first > second) {
            (first, second) = (second, first);
        }
        (int, int) key = (first, second);
        coefficients.TryGetValue(key, out double previous);
        double sum = previous + factor * coefficient;
        if (!double.IsFinite(coefficient) || !double.IsFinite(sum)) {
            return false;
        }
        coefficients[key] = sum;
        return true;
    }

    private static Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        Results.Failed<QuadraticTetradModel>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private static Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>> Cancelled() =>
        Results.Failed<QuadraticTetradModel>(new Err<ErrorCode>(
            ErrorCode.ApplicationStopped,
            "二次模型转换已取消 / Quadratic model conversion was cancelled"));
}
