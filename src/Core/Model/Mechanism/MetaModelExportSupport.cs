#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Threading.Tasks;

using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Mechanism;

/// <summary>
/// 元模型导出支持扩展 / Meta-model export support extensions.
/// <para>提供便捷的元模型导出方法，支持 LP 格式和展开格式。</para>
/// <para>Provides convenience methods for exporting meta-models in LP format
/// and unfolded format.</para>
/// </summary>
public static class MetaModelExportExtensions {
    /// <summary>
    /// 导出元模型为 LP 格式。
    /// Export meta-model to LP format.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="model">元模型 / Meta-model</param>
    /// <param name="path">导出路径（可空，默认使用模型名称）/ Export path (nullable, defaults to model name)</param>
    /// <returns>操作结果 / Operation result</returns>
    public static async Task<Try> ExportToLpAsync<V>(
        this IMetaModel<V> model,
        string? path = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        return await model.ExportAsync(path ?? model.Name);
    }

    /// <summary>
    /// 导出元模型为展开格式。
    /// Export meta-model in unfolded format.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="model">元模型 / Meta-model</param>
    /// <param name="path">导出路径 / Export path</param>
    /// <returns>操作结果 / Operation result</returns>
    public static async Task<Try> ExportUnfoldedAsync<V>(
        this IMetaModel<V> model,
        string path)
        where V : struct, IRealNumber<V>, INumberField<V> {
        return await model.ExportAsync(path, true);
    }
}
