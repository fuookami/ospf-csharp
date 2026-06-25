#nullable enable

namespace Fuookami.Ospf.Utils.Config
{
    /// <summary>版本信息 / Version info (mirrors ospf-kotlin Version data object).</summary>
    public static class Version
    {
        /// <summary>主版本号 / Major version.</summary>
        public static uint MajorVersion => 1;

        /// <summary>次版本号 / Minor version.</summary>
        public static uint MinorVersion => 0;

        /// <summary>修订版本号 / Modify version.</summary>
        public static uint ModifyVersion => 0;

        /// <summary>版本字符串 / Version string.</summary>
        public static string VersionString => $"{MajorVersion}.{MinorVersion}.{ModifyVersion}";
    }
}
