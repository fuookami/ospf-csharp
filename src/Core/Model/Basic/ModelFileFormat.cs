#nullable enable

namespace Fuookami.Ospf.Core.Model.Basic
{
    /// <summary>
    /// 模型文件格式 / Model file format
    /// </summary>
    public enum ModelFileFormat
    {
        /// <summary>LP 格式 / LP format</summary>
        LP,
    }

    /// <summary>
    /// ModelFileFormat 扩展方法 / ModelFileFormat extension methods
    /// </summary>
    public static class ModelFileFormatExtensions
    {
        /// <summary>转换为小写格式字符串 / Convert to lowercase format string</summary>
        public static string ToFormatString(this ModelFileFormat format) => format switch
        {
            ModelFileFormat.LP => "lp",
            _ => format.ToString().ToLowerInvariant(),
        };
    }
}
