#nullable enable

namespace Fuookami.Ospf.Utils.Functional
{
    /// <summary>Boolean 辅助方法 / Boolean helper methods.</summary>
    public static class BooleanExtensions
    {
        /// <summary>转换为 Ret&lt;bool&gt; / Convert to Ret&lt;bool&gt;.</summary>
        public static Result<bool, Error.ErrorCode, Error.Error<Error.ErrorCode>> ToRet(this bool value) =>
            value
                ? new Ok<bool, Error.ErrorCode, Error.Error<Error.ErrorCode>>(true)
                : new Failed<bool, Error.ErrorCode, Error.Error<Error.ErrorCode>>(Error.ErrorCode.IllegalArgument, "Condition not met");
    }
}
