#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Tests.Solver;

public class SolverFailureSupportTests {
    [Fact]
    public void SolvingException_ReturnsFailedTry() {
        Try result = SolverFailureFactory.SolvingException();
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void SolvingException_WithMessage_ReturnsFailedTry() {
        Try result = SolverFailureFactory.SolvingException("custom error");
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void EnvironmentLost_ReturnsFailedTry() {
        Try result = SolverFailureFactory.EnvironmentLost();
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void EnvironmentLost_WithMessage_ReturnsFailedTry() {
        Try result = SolverFailureFactory.EnvironmentLost("env lost detail");
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void ModelingException_ReturnsFailedTry() {
        Try result = SolverFailureFactory.ModelingException();
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void ModelingException_WithMessage_ReturnsFailedTry() {
        Try result = SolverFailureFactory.ModelingException("model error detail");
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void Terminated_ReturnsFailedTry() {
        Try result = SolverFailureFactory.Terminated();
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void SolverNotFound_ReturnsFailedTry() {
        Try result = SolverFailureFactory.SolverNotFound("CPLEX");
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void SolverNotFound_NullSolver_ReturnsFailedTry() {
        Try result = SolverFailureFactory.SolverNotFound();
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void SolverEnvironmentLost_ReturnsFailedTry() {
        Try result = SolverFailureFactory.SolverEnvironmentLost();
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void SolverSolvingException_ReturnsFailedTry() {
        Try result = SolverFailureFactory.SolverSolvingException();
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void SolverModelingException_ReturnsFailedTry() {
        Try result = SolverFailureFactory.SolverModelingException();
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void SolverTerminated_ReturnsFailedTry() {
        Try result = SolverFailureFactory.SolverTerminated();
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void ExecuteCreatingEnvironmentCallback_NullCallback_ReturnsOk() {
        Try result = SolverFailureFactory.ExecuteCreatingEnvironmentCallback<string>(
            "target", null);
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Ok<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void ExecuteCreatingEnvironmentCallback_SuccessCallback_ReturnsOk() {
        Try result = SolverFailureFactory.ExecuteCreatingEnvironmentCallback<string>(
            "target", t => Results.Ok(Results.SuccessInstance));
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Ok<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void ExecuteCreatingEnvironmentCallback_FailedCallback_ReturnsFailed() {
        Try result = SolverFailureFactory.ExecuteCreatingEnvironmentCallback<string>(
            "target", t => Results.Failed<Success>(
                new Err<ErrorCode>(ErrorCode.OREngineSolvingException, "callback failed")));
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void ExecuteCreatingEnvironmentCallback_ThrowingCallback_ReturnsFailed() {
        Try result = SolverFailureFactory.ExecuteCreatingEnvironmentCallback<string>(
            "target", t => throw new InvalidOperationException("boom"));
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Success, ErrorCode, Error<ErrorCode>>>();
    }
}
