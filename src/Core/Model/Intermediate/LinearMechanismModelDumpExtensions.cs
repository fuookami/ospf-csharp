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

/// <summary>线性机制模型的矩阵转换 / Matrix conversion for a linear mechanism model.</summary>
public static class LinearMechanismModelDumpExtensions {
    /// <summary>转换为线性三元模型 / Convert to a linear triad model.</summary>
    /// <remarks>系数按数值类型转换为 Flt64，可能舍入；固定值必须通过转换器往返。转换器检查仅覆盖零、一及固定值，不支持并发修改。 / Coefficients convert to Flt64 and may round; fixed values must round-trip through the converter. Converter checks cover only zero, one and fixed values; concurrent mutation is unsupported.</remarks>
    /// <typeparam name="V">模型数值类型 / Model numeric type.</typeparam>
    /// <param name="model">机制模型 / Mechanism model.</param>
    /// <param name="fixedVariables">待代换的已注册变量及其值 / Registered variables and values to substitute.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>矩阵模型或转换错误 / Matrix model or a conversion error.</returns>
    /// <summary>
    /// **把机制模型转换成三元模型，先展开延迟函数** / Convert a mechanism model to a triad model, expanding deferred
    /// functions first.
    /// </summary>
    /// <remarks>
    /// 把延迟物化接到<b>这里</b>，因为这个扩展是<b>所有后端唯一的转换咽喉</b>：
    /// 十四个调用点、七个后端插件，全部经过它。
    ///
    /// **为什么接在转换处而不是接在每个求解器里：** **三元模型<b>装不下</b>延迟结构。
    /// 一个带 `DeferredFunctionStructures` 的机制模型被直接转换，得到的不是一个"稍后展开"的三元模型，
    /// 而是一个<b>少了那几段约束</b>的三元模型——而它照样能求解、照样给解。
    /// 所以"先展开再转换"不是策略，是<b>转换正确性的前提</b>。**
    ///
    /// **`configPolicy` 的默认值是 `DeferredNativeFirst`，含义是"没有求解器配置时，听模型自己的"：**
    /// **配置侧 `SolverConfig.FunctionExpansionPolicy` 缺省为 `Eager`，如果这里也缺省 `Eager`，
    /// 那么所有后端都会跳过展开、转换出一份缺约束的三元模型——默认值必须是安全的那一侧。**
    /// **于是带配置的求解器显式把它传进来（那份 `Eager` 是<b>用户的明确选择</b>，不是缺省），
    /// 而不带配置的检查类调用点（`*Unavailable`、构建扩展）走模型自己的策略。**
    ///
    /// **Deferred materialisation is wired <b>here</b>, because this extension is the <b>single conversion choke point</b>
    /// for every backend: fourteen call sites across seven plugin assemblies all pass through it.**
    ///
    /// **Why at conversion rather than in each solver: a triad model <b>cannot hold</b> deferred structures.** **Converting a
    /// mechanism model that carries `DeferredFunctionStructures` without expanding does not yield a triad model that will
    /// expand later — it yields one <b>missing those constraint slices</b>, and that model still solves and still returns a
    /// solution.** **So "expand first, then convert" is not a strategy but a precondition for a correct conversion.**
    ///
    /// **`configPolicy` defaults to `DeferredNativeFirst`, meaning "with no solver configuration, the model's own intent
    /// decides": `SolverConfig.FunctionExpansionPolicy` defaults to `Eager`, and if this did too, every backend would skip
    /// expansion and convert to a triad model missing constraints — the default has to be the safe side.** **A configured
    /// solver passes its value explicitly (that `Eager` is the <b>user's explicit choice</b>, not a default), while
    /// inspection call sites without a config (`*Unavailable`, the build extension) follow the model's own policy.**
    /// </remarks>
    /// <param name="configPolicy">求解器配置上的展开策略 / The expansion policy from the solver configuration.</param>
    public static Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> ToLinearTriadModel<V>(
        this LinearMechanismModel<V> model,
        IReadOnlyDictionary<IVariableItem, V>? fixedVariables = null,
        CancellationToken cancellationToken = default,
        FunctionExpansionPolicy configPolicy = FunctionExpansionPolicy.DeferredNativeFirst)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            LinearMechanismModel<V> target = model;
            if (model is LinearMechanismModel<Flt64> flt64) {
                if (DeferredFunctionSolveEntry.ShouldAttempt(configPolicy, flt64.FunctionExpansionPolicy)) {
                    Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> expanded =
                        DeferredFunctionMaterialization.Materialize(flt64);
                    switch (expanded) {
                        case Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> failure:
                            return Results.Failed<LinearTriadModel>(failure.Error);
                        case Fatal<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> fatal:
                            return new Fatal<LinearTriadModel, ErrorCode, Error<ErrorCode>>(fatal.Errors);
                        case Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> ok:
                            // **只有真的复制了才换：没有待处理结构时 `Materialize` 返回原引用。**
                            // **Only swap when a copy was actually made: with nothing pending, `Materialize` returns the
                            // source reference.**
                            if (!ReferenceEquals(ok.Value, flt64)) {
                                // **`V` 在这一支里必然是 `Flt64`**（上面那个模式匹配已经证明了），
                                // 所以这个转换是恒等的；写成这样是因为编译器不知道这件事。
                                // **`V` is necessarily `Flt64` in this branch** (the pattern match above proved it), so this
                                // cast is identity; it is written out because the compiler cannot know.
                                target = (LinearMechanismModel<V>)(object)ok.Value;
                            }
                            break;
                    }
                }
            }

            return Dump(target, fixedVariables, cancellationToken);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure($"无法转换线性模型：{error.Message} / Cannot convert linear model: {error.Message}");
        }
    }

    private static Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> Dump<V>(
        LinearMechanismModel<V> model,
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
            return Results.Failed<LinearTriadModel>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped, "模型已关闭 / Model is closed"));
        }
        if (!MechanismModelDumpVariables.TryCaptureConverter(model.Parent.Converter, out (V Zero, V One) converterSnapshot)) {
            return Failure("模型数值转换器无效 / Invalid model value converter");
        }
        Result<Success, ErrorCode, Error<ErrorCode>> sourceCheck = model.ValidateSourceSnapshot(checkVariables: false);
        if (!sourceCheck.IsOk) {
            return MechanismModelSourceSnapshot<V>.Propagate<LinearTriadModel>(sourceCheck);
        }
        if (model.ValidateBoundDependencies() is Failed<Success, ErrorCode, Error<ErrorCode>> staleBounds) {
            return Results.Failed<LinearTriadModel>(staleBounds.Error);
        }

        IReadOnlyList<Token<V>> tokens = model.Tokens.TokensInSolver;
        Result<MechanismModelSourceSnapshot<V>.VariableSnapshot[], ErrorCode, Error<ErrorCode>> captured =
            MechanismModelDumpVariables.Capture(tokens);
        if (captured is Failed<MechanismModelSourceSnapshot<V>.VariableSnapshot[], ErrorCode, Error<ErrorCode>> failedCapture) {
            return Results.Failed<LinearTriadModel>(failedCapture.Error);
        }
        if (captured is Fatal<MechanismModelSourceSnapshot<V>.VariableSnapshot[], ErrorCode, Error<ErrorCode>> fatalCapture) {
            return new Fatal<LinearTriadModel, ErrorCode, Error<ErrorCode>>(fatalCapture.Errors);
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
            return Results.Failed<LinearTriadModel>(failedVariables.Error);
        }
        if (variableResult is Fatal<(Dictionary<VariableItemKey, int> Indexes, List<ModelViewVariable> Variables, Dictionary<VariableItemKey, double> FixedValues), ErrorCode, Error<ErrorCode>> fatalVariables) {
            return new Fatal<LinearTriadModel, ErrorCode, Error<ErrorCode>>(fatalVariables.Errors);
        }
        (Dictionary<VariableItemKey, int> indexes, List<ModelViewVariable> variables, Dictionary<VariableItemKey, double> fixedValues) = variableResult.Value;

        IReadOnlyList<IConstraint<V, LinearCategory>> constraints = ((IMechanismModel<V>)model).Constraints;
        Result<MechanismModelRowsSnapshot<V>, ErrorCode, Error<ErrorCode>> rowsSnapshot = MechanismModelRowsSnapshot<V>.Capture(constraints);
        if (rowsSnapshot is Failed<MechanismModelRowsSnapshot<V>, ErrorCode, Error<ErrorCode>> failedRows) {
            return Results.Failed<LinearTriadModel>(failedRows.Error);
        }
        if (rowsSnapshot is Fatal<MechanismModelRowsSnapshot<V>, ErrorCode, Error<ErrorCode>> fatalRows) {
            return new Fatal<LinearTriadModel, ErrorCode, Error<ErrorCode>>(fatalRows.Errors);
        }
        var rows = new List<SparseVector>(constraints.Count);
        var signs = new List<ConstraintRelation>(constraints.Count);
        var rhs = new List<Flt64>(constraints.Count);
        var names = new List<string>(constraints.Count);
        var sources = new List<ConstraintSource>(constraints.Count);
        foreach (IConstraint<V, LinearCategory> constraint in constraints) {
            if (cancellationToken.IsCancellationRequested) {
                return Cancelled();
            }
            if (constraint.Lazy) {
                return Failure($"暂不支持惰性约束：{constraint.Name} / Lazy constraint is not yet supported: {constraint.Name}");
            }
            if (!MechanismModelDumpVariables.TryConvert(constraint.Rhs, out double rowRhs)
                || (constraint.Sign != ConstraintRelation.LessEqual
                    && constraint.Sign != ConstraintRelation.Equal
                    && constraint.Sign != ConstraintRelation.GreaterEqual)) {
                return Failure($"约束无效：{constraint.Name} / Invalid constraint: {constraint.Name}");
            }
            var coefficients = new Dictionary<int, double>();
            foreach (ICell<V> cell in constraint.Lhs) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled();
                }
                if (cell is not ILinearCell<V> linear) {
                    return Failure($"约束项无法解析：{constraint.Name} / Unresolved constraint term: {constraint.Name}");
                }
                if (!MechanismModelDumpVariables.TryConvert(linear.Coefficient, out double coefficient)) {
                    return Failure($"约束系数无效：{constraint.Name} / Invalid constraint coefficient: {constraint.Name}");
                }
                if (fixedValues.TryGetValue(linear.Token.Key, out double fixedValue)) {
                    double contribution = coefficient * fixedValue;
                    rowRhs -= contribution;
                    if (!double.IsFinite(contribution) || !double.IsFinite(rowRhs)) {
                        return Failure($"固定变量代换溢出：{constraint.Name} / Fixed-variable substitution overflow: {constraint.Name}");
                    }
                    continue;
                }
                if (!indexes.TryGetValue(linear.Token.Key, out int index)) {
                    return Failure($"约束项无法解析：{constraint.Name} / Unresolved constraint term: {constraint.Name}");
                }
                coefficients.TryGetValue(index, out double previous);
                double sum = previous + coefficient;
                if (!double.IsFinite(sum)) {
                    return Failure($"约束系数无效：{constraint.Name} / Invalid constraint coefficient: {constraint.Name}");
                }
                coefficients[index] = sum;
            }
            rows.Add(new SparseVector(coefficients
                .Where(pair => pair.Value != 0.0)
                .OrderBy(pair => pair.Key)
                .Select(pair => new SparseVectorEntry(pair.Key, new Flt64(pair.Value)))
                .ToList()));
            signs.Add(constraint.Sign);
            rhs.Add(new Flt64(rowRhs));
            names.Add(constraint.Name);
            sources.Add(ConstraintSource.Origin);
        }

        IReadOnlyList<object> parts = model.ObjectFunction.SubObjects;
        ObjectCategory direction = parts.Count == 1 && parts[0] is LinearSubObject<V> single
            ? single.Category
            : model.ObjectFunction.Category;
        if (direction is not ObjectCategory.Minimum and not ObjectCategory.Maximum) {
            return Failure("目标方向无效 / Invalid objective direction");
        }
        double[] objectiveCoefficients = new double[variables.Count];
        double constant = 0.0;
        foreach (object part in parts) {
            if (cancellationToken.IsCancellationRequested) {
                return Cancelled();
            }
            if (part is not LinearSubObject<V> objective
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
                if (cell is not ILinearCell<V> linear) {
                    return Failure($"目标项无法解析：{objective.Name} / Unresolved objective term: {objective.Name}");
                }
                if (!MechanismModelDumpVariables.TryConvert(linear.Coefficient, out double coefficient)) {
                    return Failure($"目标系数无效：{objective.Name} / Invalid objective coefficient: {objective.Name}");
                }
                if (fixedValues.TryGetValue(linear.Token.Key, out double fixedValue)) {
                    double contribution = coefficient * fixedValue;
                    constant += factor * contribution;
                    if (!double.IsFinite(contribution) || !double.IsFinite(constant)) {
                        return Failure($"固定变量代换溢出：{objective.Name} / Fixed-variable substitution overflow: {objective.Name}");
                    }
                    continue;
                }
                if (!indexes.TryGetValue(linear.Token.Key, out int index)) {
                    return Failure($"目标项无法解析：{objective.Name} / Unresolved objective term: {objective.Name}");
                }
                double sum = objectiveCoefficients[index] + factor * coefficient;
                if (!double.IsFinite(sum)) {
                    return Failure($"目标系数无效：{objective.Name} / Invalid objective coefficient: {objective.Name}");
                }
                objectiveCoefficients[index] = sum;
            }
        }
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled();
        }
        var objectiveCells = new List<LinearObjectiveCell>();
        for (int i = 0; i < objectiveCoefficients.Length; i++) {
            if (cancellationToken.IsCancellationRequested) {
                return Cancelled();
            }
            if (objectiveCoefficients[i] != 0.0) {
                objectiveCells.Add(new LinearObjectiveCell(i, new Flt64(objectiveCoefficients[i])));
            }
        }
        if (model.ValidateBoundDependencies() is Failed<Success, ErrorCode, Error<ErrorCode>> changedBounds) {
            return Results.Failed<LinearTriadModel>(changedBounds.Error);
        }
        sourceCheck = model.ValidateSourceSnapshot(checkVariables: false);
        if (!sourceCheck.IsOk) {
            return MechanismModelSourceSnapshot<V>.Propagate<LinearTriadModel>(sourceCheck);
        }
        Result<Success, ErrorCode, Error<ErrorCode>> variableCheck =
            MechanismModelSourceSnapshot<V>.ValidateVariables(tokens, captured.Value);
        if (!variableCheck.IsOk) {
            return MechanismModelSourceSnapshot<V>.Propagate<LinearTriadModel>(variableCheck);
        }
        Result<Success, ErrorCode, Error<ErrorCode>> rowsCheck = rowsSnapshot.Value.Validate(constraints);
        if (!rowsCheck.IsOk) {
            return MechanismModelSourceSnapshot<V>.Propagate<LinearTriadModel>(rowsCheck);
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
                var batch = new LinearConstraintBatch(
                    lhs: new SparseMatrix(rows),
                    signs: signs,
                    rhs: rhs,
                    names: names,
                    sources: sources,
                    identities: identities,
                    identityNamespace: model.Parent.IdentityRegistry?.Namespace,
                    identitySchemaVersion: model.Parent.IdentityRegistry?.SchemaVersion
                );
                var objective = new Objective<LinearObjectiveCell>(
                    category: direction,
                    cells: objectiveCells,
                    constant: new Flt64(constant),
                    identity: objectiveIdentity,
                    identityNamespace: model.Parent.IdentityRegistry?.Namespace,
                    identitySchemaVersion: model.Parent.IdentityRegistry?.SchemaVersion
                );
                var matrix = new LinearTriadModel(
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
                    return MechanismModelSourceSnapshot<V>.Propagate<LinearTriadModel>(identityCheck);
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

    private static Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        Results.Failed<LinearTriadModel>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private static Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> Cancelled() =>
        Results.Failed<LinearTriadModel>(new Err<ErrorCode>(
            ErrorCode.ApplicationStopped,
            "线性模型转换已取消 / Linear model conversion was cancelled"));
}
