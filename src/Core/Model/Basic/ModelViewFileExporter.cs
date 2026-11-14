#nullable enable

using System;
using System.IO;
using System.Text;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Basic;

/// <summary>模型文件导出的公共扩展入口 / Public extension entry points for model-file export.</summary>
public static class ModelViewExportExtensions {
    /// <summary>使用当前目录和指定格式导出模型 / Export the model to the current directory in the requested format.</summary>
    /// <typeparam name="TCell">约束单元格类型 / Constraint cell type.</typeparam>
    /// <param name="model">模型 / Model.</param>
    /// <param name="format">文件格式 / File format.</param>
    /// <returns>导出结果 / Export result.</returns>
    public static Try Export<TCell>(this IBasicModelView<TCell> model, ModelFileFormat format)
        where TCell : IConstraintCell<TCell>, ICopyable<TCell> =>
        ModelViewFileExporter.Export(model, format);

    /// <summary>使用指定文件或目录路径导出模型 / Export the model to a file or directory path.</summary>
    /// <typeparam name="TCell">约束单元格类型 / Constraint cell type.</typeparam>
    /// <param name="model">模型 / Model.</param>
    /// <param name="path">文件路径，或已存在且应写入模型名文件的目录 / File path, or an existing directory that should receive a file named after the model.</param>
    /// <param name="format">文件格式 / File format.</param>
    /// <returns>导出结果 / Export result.</returns>
    public static Try Export<TCell>(
        this IBasicModelView<TCell> model,
        string path,
        ModelFileFormat format)
        where TCell : IConstraintCell<TCell>, ICopyable<TCell> =>
        ModelViewFileExporter.Export(model, path, format);
}

/// <summary>模型文件导出入口 / Model file export entry point.</summary>
internal static class ModelViewFileExporter {
    /// <summary>导出到当前目录 / Export to the current directory.</summary>
    /// <typeparam name="TCell">约束单元格类型 / Constraint cell type.</typeparam>
    /// <param name="model">模型 / Model.</param>
    /// <param name="format">文件格式 / File format.</param>
    /// <returns>导出结果 / Export result.</returns>
    public static Try Export<TCell>(IBasicModelView<TCell> model, ModelFileFormat format)
        where TCell : IConstraintCell<TCell>, ICopyable<TCell> =>
        Export(model, Directory.GetCurrentDirectory(), format);

    /// <summary>导出到文件或目录 / Export to a file or directory.</summary>
    /// <typeparam name="TCell">约束单元格类型 / Constraint cell type.</typeparam>
    /// <param name="model">模型 / Model.</param>
    /// <param name="path">文件路径或已存在目录 / File path or an existing directory.</param>
    /// <param name="format">文件格式 / File format.</param>
    /// <returns>导出结果 / Export result.</returns>
    public static Try Export<TCell>(
        IBasicModelView<TCell> model,
        string? path,
        ModelFileFormat format)
        where TCell : IConstraintCell<TCell>, ICopyable<TCell> {
        if (model is null || string.IsNullOrWhiteSpace(path)) {
            return Failure(ErrorCode.IllegalArgument, "模型或导出路径为空 / Model or export path is null");
        }
        if (format != ModelFileFormat.LP) {
            return Failure(ErrorCode.IllegalArgument, $"不支持的模型格式：{format} / Unsupported model format: {format}");
        }
        string filePath = Directory.Exists(path)
            ? Path.Combine(path, $"{model.Name}.{format.ToFormatString()}")
            : path;
        try {
            using var writer = new StreamWriter(
                path: filePath,
                append: false,
                encoding: new UTF8Encoding(false)
            );
            return model.ExportLP(writer);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Failure(
                ErrorCode.ApplicationFailed,
                $"导出模型失败：{error.Message} / Cannot export model: {error.Message}");
        }
    }

    private static Try Failure(ErrorCode code, string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(code, message));
}
