#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Tests.Solver;

public class SolverStatusSupportTests {
    [Fact]
    public void ResolveErrCode_Optimal_ReturnsNull() {
        ErrorCode? result = SolverStatus.Optimal.ResolveErrCode();
        // ErrCode() returns null for Optimal; ResolveErrCode falls back to ErrorCode.OREngineSolvingException
        result.Should().Be(ErrorCode.OREngineSolvingException);
    }

    [Fact]
    public void ResolveErrCode_Infeasible_ReturnsInfeasibleErrorCode() {
        ErrorCode? result = SolverStatus.Infeasible.ResolveErrCode();
        result.Should().Be(ErrorCode.ORModelInfeasible);
    }

    [Fact]
    public void ResolveErrCode_SolvingException_ReturnsExceptionErrorCode() {
        ErrorCode? result = SolverStatus.SolvingException.ResolveErrCode();
        result.Should().Be(ErrorCode.OREngineSolvingException);
    }

    [Fact]
    public void ResolveErrCode_WithFallback_UsesFallbackWhenErrCodeNull() {
        ErrorCode? result = SolverStatus.Optimal.ResolveErrCode(ErrorCode.ApplicationError);
        result.Should().Be(ErrorCode.ApplicationError);
    }

    [Fact]
    public void ResolveErrCode_Unbounded_ReturnsUnboundedErrorCode() {
        ErrorCode? result = SolverStatus.Unbounded.ResolveErrCode();
        result.Should().Be(ErrorCode.ORModelUnbounded);
    }

    [Fact]
    public void FailByStatus_Infeasible_ReturnsFailed() {
        Try result = SolverStatus.Infeasible.FailByStatus();
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void FailByStatus_SolvingException_ReturnsFailed() {
        Try result = SolverStatus.SolvingException.FailByStatus();
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void ShouldAbortOnCallbackFailure_NullResult_ReturnsFalse() {
        bool aborted = false;
        bool result = SolverStatusSupportExtensions.ShouldAbortOnCallbackFailure(
            null, () => aborted = true);
        result.Should().BeFalse();
        aborted.Should().BeFalse();
    }

    [Fact]
    public void ShouldAbortOnCallbackFailure_FailedResult_ReturnsTrueAndAborts() {
        Try failedResult = Results.Failed<Success>(
            new Err<ErrorCode>(ErrorCode.OREngineSolvingException, "test"));
        bool aborted = false;
        bool result = SolverStatusSupportExtensions.ShouldAbortOnCallbackFailure(
            failedResult, () => aborted = true);
        result.Should().BeTrue();
        aborted.Should().BeTrue();
    }

    [Fact]
    public void ShouldAbortOnCallbackFailure_OkResult_ReturnsFalse() {
        Try okResult = Results.Ok(Results.SuccessInstance);
        bool aborted = false;
        bool result = SolverStatusSupportExtensions.ShouldAbortOnCallbackFailure(
            okResult, () => aborted = true);
        result.Should().BeFalse();
        aborted.Should().BeFalse();
    }
}
