#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver;

/// <summary>求解器无关的原生函数结构 writer / Backend-neutral native function-structure writer.</summary>
/// <typeparam name="TModel">求解器模型类型 / Solver model type.</typeparam>
/// <typeparam name="TVariable">求解器变量类型 / Solver variable type.</typeparam>
public interface INativeFunctionWriter<TModel, TVariable> {
    /// <summary>将一批类型擦除结构写入求解器模型 / Write a batch of type-erased structures to the solver model.</summary>
    /// <param name="model">求解器模型 / Solver model.</param>
    /// <param name="variables">按变量项键索引的原生变量 / Native variables indexed by variable-item key.</param>
    /// <param name="batch">待写入的结构 / Structures to write.</param>
    /// <returns>写入结果 / Write result.</returns>
    Try Write(
        TModel model,
        IReadOnlyDictionary<VariableItemKey, TVariable> variables,
        IReadOnlyList<object> batch);
}

/// <summary>按结构类型过滤的原生 writer 适配器 / Native writer adapter that filters by structure type.</summary>
/// <typeparam name="TModel">求解器模型类型 / Solver model type.</typeparam>
/// <typeparam name="TVariable">求解器变量类型 / Solver variable type.</typeparam>
/// <typeparam name="TStructure">writer 处理的结构类型 / Structure type handled by the writer.</typeparam>
public abstract class TypedNativeFunctionWriter<TModel, TVariable, TStructure> : INativeFunctionWriter<TModel, TVariable>
    where TStructure : class {
    /// <inheritdoc/>
    public Try Write(
        TModel model,
        IReadOnlyDictionary<VariableItemKey, TVariable> variables,
        IReadOnlyList<object> batch) {
        var typed = new List<TStructure>();
        foreach (object item in batch) {
            if (item is TStructure structure) {
                typed.Add(structure);
            }
        }
        return WriteTyped(model, variables, typed);
    }

    /// <summary>写入已过滤的同构结构 / Write the filtered homogeneous structures.</summary>
    /// <param name="model">求解器模型 / Solver model.</param>
    /// <param name="variables">原生变量映射 / Native variable map.</param>
    /// <param name="batch">已过滤结构 / Filtered structures.</param>
    /// <returns>写入结果 / Write result.</returns>
    protected abstract Try WriteTyped(
        TModel model,
        IReadOnlyDictionary<VariableItemKey, TVariable> variables,
        IReadOnlyList<TStructure> batch);
}

/// <summary>按注册顺序协调多个原生 writer / Coordinate native writers in registration order.</summary>
/// <typeparam name="TModel">求解器模型类型 / Solver model type.</typeparam>
/// <typeparam name="TVariable">求解器变量类型 / Solver variable type.</typeparam>
public sealed class NativeFunctionWriterRegistry<TModel, TVariable> {
    private readonly IReadOnlyList<INativeFunctionWriter<TModel, TVariable>> _writers;

    /// <summary>创建 writer 注册表 / Create a writer registry.</summary>
    /// <param name="writers">按 dispatch 顺序排列的 writer / Writers in dispatch order.</param>
    public NativeFunctionWriterRegistry(IReadOnlyList<INativeFunctionWriter<TModel, TVariable>> writers) {
        _writers = new List<INativeFunctionWriter<TModel, TVariable>>(writers).AsReadOnly();
    }

    /// <summary>把各批结构交给对应 writer / Dispatch each structure batch to its corresponding writer.</summary>
    /// <param name="model">求解器模型 / Solver model.</param>
    /// <param name="variables">按变量项键索引的原生变量 / Native variables indexed by variable-item key.</param>
    /// <param name="batches">与 writer 一一对应的结构批次 / Structure batches corresponding to writers.</param>
    /// <returns>成功、首次失败或参数错误 / Success, the first failure, or an argument error.</returns>
    public Try Write(
        TModel model,
        IReadOnlyDictionary<VariableItemKey, TVariable> variables,
        IReadOnlyList<IReadOnlyList<object>> batches) {
        if (batches is null || _writers.Count != batches.Count) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "native writer 与 batch 数量不一致 / Native writer and batch counts do not match"));
        }
        for (int index = 0; index < _writers.Count; index++) {
            IReadOnlyList<object> batch = batches[index];
            if (batch is null) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "native batch 为空引用 / Native batch is null"));
            }
            if (batch.Count == 0) {
                continue;
            }
            Try result = _writers[index].Write(model, variables, batch);
            if (!result.IsOk) {
                return result;
            }
        }
        return Results.OkInstance;
    }
}
