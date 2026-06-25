#nullable enable

using System;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests
{
    /// <summary>
    /// Result 断言辅助方法 / Result assertion helpers.
    /// </summary>
    public static class ResultAssertions
    {
        /// <summary>
        /// 断言 Result 为 Ok 并返回值 / Assert Result is Ok and return value.
        /// </summary>
        public static T ValueOrFail<T>(Result<T, ErrorCode, Error<ErrorCode>> result)
        {
            if (result is Ok<T, ErrorCode, Error<ErrorCode>> ok)
            {
                return ok.Value;
            }
            var message = result is Failed<T, ErrorCode, Error<ErrorCode>> f ? f.Message : "Result is not Ok";
            throw new Xunit.Sdk.XunitException($"Expected Ok but got Failed: {message}");
        }

        /// <summary>
        /// 断言 Result 为 Failed / Assert Result is Failed.
        /// </summary>
        public static void AssertFailed<T>(Result<T, ErrorCode, Error<ErrorCode>> result)
        {
            Assert.True(result.IsFailed, "Expected Failed but got Ok");
        }

        /// <summary>
        /// 断言 Result 为 Ok / Assert Result is Ok.
        /// </summary>
        public static void AssertOk<T>(Result<T, ErrorCode, Error<ErrorCode>> result)
        {
            Assert.True(result.IsOk, "Expected Ok but got Failed");
        }
    }
}
