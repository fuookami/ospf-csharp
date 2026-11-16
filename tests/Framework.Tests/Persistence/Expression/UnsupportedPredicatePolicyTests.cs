// 不支持谓词策略的判据 / Criteria for the unsupported predicate policy.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Expression;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Persistence.Expression;

/// <summary>
/// **不支持谓词策略** / The unsupported predicate policy.
/// </summary>
/// <remarks>
/// 判据的重点：**策略决定错误码**（三个码各指一种处置）、**只有 `AlwaysFalse` 会被降级**、
/// **`FailFast` 与 `ClientFilter` 必须原样传播**。
///
/// The criteria focus on **the policy deciding the error code** (three codes pointing at three different reactions), on **only `AlwaysFalse` being degraded**, and on **`FailFast` and `ClientFilter`
/// propagating untouched**.
/// </remarks>
public class UnsupportedPredicatePolicyTests {
    /// <summary>
    /// **三种策略给出三个不同的错误码** / The three policies give three different error codes.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. `FailFast` → `IllegalArgument`（调用方写错了谓词）。
    /// 2. `ClientFilter` → `ApplicationFailed`（后端做不到，换后端可以）。
    /// 3. `AlwaysFalse` → `ApplicationError`（后端做不到，且已按恒假降级）。
    ///
    /// 三个码必须互不相同——合并成一个会让调用方无从判断该改代码还是该换后端。
    ///
    /// Three paired points: `FailFast` gives `IllegalArgument` (the caller wrote the predicate wrongly); `ClientFilter` gives `ApplicationFailed` (this backend cannot, another can); and `AlwaysFalse`
 /// gives `ApplicationError` (this backend cannot and has degraded to constant false). The three must differ, since merging them would leave the caller unable to tell whether to change the implementation
    /// or the backend.
    /// </remarks>
    [Fact]
    public void ToError_ShouldMapEachPolicyToItsOwnCode() {
        var failFast = UnsupportedPredicateDetail
            .FailFast("Between", "not translatable", "ktorm").ToError();
        var clientFilter = UnsupportedPredicateDetail
            .ClientFilter("Between", "not translatable", "ktorm").ToError();
        var alwaysFalse = UnsupportedPredicateDetail
            .AlwaysFalse("Between", "not translatable", "ktorm").ToError();

        failFast.Code.Should().Be(ErrorCode.IllegalArgument);
        clientFilter.Code.Should().Be(ErrorCode.ApplicationFailed);
        alwaysFalse.Code.Should().Be(ErrorCode.ApplicationError);

        failFast.Code.Should().NotBe(clientFilter.Code);
        clientFilter.Code.Should().NotBe(alwaysFalse.Code);

        failFast.Message.Should().Contain("policy=FailFast");
        failFast.Message.Should().Contain("backend=ktorm");
        failFast.Value.Should().BeOfType<UnsupportedPredicateDetail>("the detail must travel with the error");
    }

    /// <summary>
    /// **只有 `AlwaysFalse` 会被降级** / Only `AlwaysFalse` is degraded.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. `AlwaysFalse` 的失败 → **成功**，值为构造出的恒假值。
    /// 2. `FailFast` 的失败 → **仍是失败**（降级会把"写错谓词"变成"查询为空"）。
    /// 3. `ClientFilter` 的失败 → **仍是失败**。
    /// 4. 成功结果 → 原样返回，构造器**不被调用**。
    ///
    /// Four paired points: an `AlwaysFalse` failure becomes a **success** carrying the constructed constant-false value; a `FailFast` failure **stays a failure**, since degrading it would turn "the
    /// predicate was written wrongly" into "the query returned nothing"; a `ClientFilter` failure stays one too; and a success passes through with the factory **never invoked**.
    /// </remarks>
    [Fact]
    public void WithAlwaysFalseUnsupported_ShouldDegradeOnlyAlwaysFalse() {
        int invoked = 0;
        Func<string> createFalse = () => {
            invoked++;
            return "false-predicate";
        };

        Result<string, ErrorCode, Error<ErrorCode>> alwaysFalse = Results
            .Failed<string>(UnsupportedPredicateDetail.AlwaysFalse("Between", "nope", "ktorm").ToError())
            .WithAlwaysFalseUnsupported(createFalse);
        alwaysFalse.IsOk.Should().BeTrue("the caller explicitly asked for a constant-false degradation");
        alwaysFalse.Value.Should().Be("false-predicate");
        invoked.Should().Be(1);

        Result<string, ErrorCode, Error<ErrorCode>> failFast = Results
            .Failed<string>(UnsupportedPredicateDetail.FailFast("Between", "nope", "ktorm").ToError())
            .WithAlwaysFalseUnsupported(createFalse);
        failFast.IsOk.Should().BeFalse("FailFast must never be swallowed");
        invoked.Should().Be(1, "the factory must not run for a non-degradable failure");

        Result<string, ErrorCode, Error<ErrorCode>> clientFilter = Results
            .Failed<string>(UnsupportedPredicateDetail.ClientFilter("Between", "nope", "ktorm").ToError())
            .WithAlwaysFalseUnsupported(createFalse);
        clientFilter.IsOk.Should().BeFalse();
        invoked.Should().Be(1);

        Result<string, ErrorCode, Error<ErrorCode>> success = Results.Ok("original");
        success.WithAlwaysFalseUnsupported(createFalse).Value.Should().Be("original");
        invoked.Should().Be(1, "a success must pass through untouched");
    }

    /// <summary>
    /// **失败传播保留错误对象** / Failure propagation keeps the error object.
    /// </summary>
    /// <remarks>
    /// 两点成对：失败传播后**错误码与详情都不变**；成功传播得到 `null`（没有失败要传播）。
    ///
 /// Two paired points: after propagation **both the implementation and the detail are unchanged**; and propagating a success yields `null`, since there is no failure to carry.
    /// </remarks>
    [Fact]
    public void PropagateFailure_ShouldKeepTheError() {
        var error = UnsupportedPredicateDetail.FailFast("Regex", "no", "ktorm").ToError();
        Result<object?, ErrorCode, Error<ErrorCode>> source = Results.Failed<object?>(error);

        Result<int, ErrorCode, Error<ErrorCode>>? propagated = source.PropagateFailure<int>();
        propagated.Should().NotBeNull();
        propagated!.IsOk.Should().BeFalse();
        ((Failed<int, ErrorCode, Error<ErrorCode>>)propagated).Error.Code.Should().Be(ErrorCode.IllegalArgument);
        ((Failed<int, ErrorCode, Error<ErrorCode>>)propagated).Error.Value.Should().BeOfType<UnsupportedPredicateDetail>();

        source = Results.Ok<object?>("ok");
        source.PropagateFailure<int>().Should().BeNull("there is no failure to propagate");
    }
}
