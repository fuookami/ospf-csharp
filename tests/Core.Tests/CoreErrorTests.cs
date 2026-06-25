#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Error;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

public class CoreErrorTests {
    [Fact]
    public void VariableError_NotFound_HasCorrectCodeAndMessage() {
        var error = new VariableError.NotFound("x1");
        error.ErrorCode.Should().Be(ErrorCode.DataNotFound);
        error.Message.Should().Contain("x1");
    }

    [Fact]
    public void VariableError_AlreadyExists_HasCorrectCode() {
        var error = new VariableError.AlreadyExists("x2");
        error.ErrorCode.Should().Be(ErrorCode.TokenExisted);
    }

    [Fact]
    public void VariableError_NameConflict_HasCorrectCode() {
        var error = new VariableError.NameConflict("x3");
        error.ErrorCode.Should().Be(ErrorCode.SymbolRepetitive);
    }

    [Fact]
    public void ModelError_NotInitialized_HasCorrectCode() {
        var error = new ModelError.NotInitialized();
        error.ErrorCode.Should().Be(ErrorCode.ApplicationError);
        error.Message.Should().Contain("not initialized");
    }

    [Fact]
    public void ModelError_MissingObjective_HasCorrectCode() {
        var error = new ModelError.MissingObjective();
        error.ErrorCode.Should().Be(ErrorCode.DataEmpty);
    }

    [Fact]
    public void SolverError_Infeasible_HasCorrectCode() {
        var error = new SolverError.Infeasible();
        error.ErrorCode.Should().Be(ErrorCode.ORModelInfeasible);
    }

    [Fact]
    public void SolverError_Unbounded_HasCorrectCode() {
        var error = new SolverError.Unbounded();
        error.ErrorCode.Should().Be(ErrorCode.ORModelUnbounded);
    }

    [Fact]
    public void SolverError_Timeout_HasCorrectCode() {
        var error = new SolverError.Timeout(TimeSpan.FromSeconds(30));
        error.ErrorCode.Should().Be(ErrorCode.OREngineTerminated);
        error.Message.Should().Contain("30");
    }

    [Fact]
    public void CoreError_Variable_WrapsVariableError() {
        var inner = new VariableError.NotFound("x");
        var core = new CoreError.Variable(inner);
        core.ErrorCode.Should().Be(ErrorCode.DataNotFound);
        core.Detail.Should().BeSameAs(inner);
    }

    [Fact]
    public void CoreError_Model_WrapsModelError() {
        var inner = new ModelError.NotInitialized();
        var core = new CoreError.Model(inner);
        core.ErrorCode.Should().Be(ErrorCode.ApplicationError);
    }

    [Fact]
    public void CoreError_Solver_WrapsSolverError() {
        var inner = new SolverError.Infeasible();
        var core = new CoreError.Solver(inner);
        core.ErrorCode.Should().Be(ErrorCode.ORModelInfeasible);
    }

    [Fact]
    public void AsCoreError_Extension_VariableError() {
        VariableError error = new VariableError.NotFound("x");
        CoreError core = error.AsCoreError();
        core.Should().BeOfType<CoreError.Variable>();
    }

    [Fact]
    public void AsCoreError_Extension_ModelError() {
        ModelError error = new ModelError.NotInitialized();
        CoreError core = error.AsCoreError();
        core.Should().BeOfType<CoreError.Model>();
    }

    [Fact]
    public void AsCoreError_Extension_SolverError() {
        SolverError error = new SolverError.Infeasible();
        CoreError core = error.AsCoreError();
        core.Should().BeOfType<CoreError.Solver>();
    }

    [Fact]
    public void SolverNotFoundError_WithSolver() {
        var error = new SolverNotFoundError("CPLEX");
        error.Solver.Should().Be("CPLEX");
        error.Code.Should().Be(ErrorCode.SolverNotFound);
    }

    [Fact]
    public void SolverNotFoundError_WithoutSolver() {
        var error = new SolverNotFoundError();
        error.Solver.Should().BeNull();
        error.Message.Should().Contain("No solver valid");
    }

    [Fact]
    public void SolverTerminatedError_HasCorrectCode() {
        var error = new SolverTerminatedError();
        error.Code.Should().Be(ErrorCode.OREngineTerminated);
    }

    [Fact]
    public void IStructuredCoreError_ToError() {
        IStructuredCoreError error = new VariableError.NotFound("x");
        var err = error.ToError();
        err.Code.Should().Be(ErrorCode.DataNotFound);
    }

    [Fact]
    public void IStructuredCoreError_ToFailed() {
        IStructuredCoreError error = new VariableError.NotFound("x");
        Result<int, ErrorCode, Error<ErrorCode>> result = error.ToFailed<int>();
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<int, ErrorCode, Error<ErrorCode>>>();
    }
}
