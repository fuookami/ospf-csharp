#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Heuristic.Evo;
using Fuookami.Ospf.Core.Plugin.Heuristic.Gco;
using Fuookami.Ospf.Core.Plugin.Heuristic.Hca;
using Fuookami.Ospf.Core.Plugin.Heuristic.Hs;
using Fuookami.Ospf.Core.Plugin.Heuristic.Ns;
using Fuookami.Ospf.Core.Plugin.Heuristic.Soa;
using Fuookami.Ospf.Core.Plugin.Heuristic.Tests.Shared;
using Fuookami.Ospf.Core.Plugin.Heuristic.Warso;
using Fuookami.Ospf.Core.Plugin.Heuristic.Wca;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Tests.Stub;
/// <summary>
/// stub algorithm tests (not yet implemented).
/// stub algorithm tests (not yet implemented).
/// Covers: EVO, GCO, HCA, HS, NS, SOA, WarSO, WCA.
/// </summary>
public class StubAlgorithmTest {
    // ==================== EVO ====================

    /// <summary>
    /// EVO should return failed result with ApplicationError.
    /// </summary>
    [Fact]
    public async Task EnergyValleyOptimizer_ShouldReturnNotImplemented() {
        // Arrange
        var evo = new EnergyValleyOptimizer<Flt64, Flt64, Flt64>();
        var model = new TestCallBackModel(dimension: 3, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await evo.InvokeAsync(model);

        // Assert
        result.IsFailed.Should().BeTrue();
        var failed = (Utils.Functional.Failed<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>)result;
        failed.Code.Should().Be(ErrorCode.ApplicationError);
        failed.Message.Should().Contain("not implemented");
    }

    // ==================== GCO ====================

    /// <summary>
    /// GCO should return failed result with ApplicationError.
    /// </summary>
    [Fact]
    public async Task GerminalCenterOptimization_ShouldReturnNotImplemented() {
        // Arrange
        var gco = new GerminalCenterOptimization<Flt64, Flt64, Flt64>();
        var model = new TestCallBackModel(dimension: 3, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await gco.InvokeAsync(model);

        // Assert
        result.IsFailed.Should().BeTrue();
        var failed = (Utils.Functional.Failed<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>)result;
        failed.Code.Should().Be(ErrorCode.ApplicationError);
        failed.Message.Should().Contain("not implemented");
    }

    // ==================== HCA ====================

    /// <summary>
    /// HCA should return failed result with ApplicationError.
    /// </summary>
    [Fact]
    public async Task HillClimbingAlgorithm_ShouldReturnNotImplemented() {
        // Arrange
        var hca = new HillClimbingAlgorithm<Flt64, Flt64, Flt64>();
        var model = new TestCallBackModel(dimension: 3, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await hca.InvokeAsync(model);

        // Assert
        result.IsFailed.Should().BeTrue();
        var failed = (Utils.Functional.Failed<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>)result;
        failed.Code.Should().Be(ErrorCode.ApplicationError);
        failed.Message.Should().Contain("not implemented");
    }

    // ==================== HS ====================

    /// <summary>
    /// HS should return failed result with ApplicationError.
    /// </summary>
    [Fact]
    public async Task HarmonySearch_ShouldReturnNotImplemented() {
        // Arrange
        var hs = new HarmonySearch<Flt64, Flt64, Flt64>();
        var model = new TestCallBackModel(dimension: 3, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await hs.InvokeAsync(model);

        // Assert
        result.IsFailed.Should().BeTrue();
        var failed = (Utils.Functional.Failed<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>)result;
        failed.Code.Should().Be(ErrorCode.ApplicationError);
        failed.Message.Should().Contain("not implemented");
    }

    // ==================== NS ====================

    /// <summary>
    /// NS should return failed result with ApplicationError.
    /// </summary>
    [Fact]
    public async Task NeighborhoodSearch_ShouldReturnNotImplemented() {
        // Arrange
        var ns = new NeighborhoodSearch<Flt64, Flt64, Flt64>();
        var model = new TestCallBackModel(dimension: 3, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await ns.InvokeAsync(model);

        // Assert
        result.IsFailed.Should().BeTrue();
        var failed = (Utils.Functional.Failed<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>)result;
        failed.Code.Should().Be(ErrorCode.ApplicationError);
        failed.Message.Should().Contain("not implemented");
    }

    // ==================== SOA ====================

    /// <summary>
    /// SOA should return failed result with ApplicationError.
    /// </summary>
    [Fact]
    public async Task SeagullOptimizationAlgorithm_ShouldReturnNotImplemented() {
        // Arrange
        var soa = new SeagullOptimizationAlgorithm<Flt64, Flt64, Flt64>();
        var model = new TestCallBackModel(dimension: 3, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await soa.InvokeAsync(model);

        // Assert
        result.IsFailed.Should().BeTrue();
        var failed = (Utils.Functional.Failed<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>)result;
        failed.Code.Should().Be(ErrorCode.ApplicationError);
        failed.Message.Should().Contain("not implemented");
    }

    // ==================== WarSO ====================

    /// <summary>
    /// WarSO should return failed result with ApplicationError.
    /// </summary>
    [Fact]
    public async Task WarStrategyOptimizationAlgorithm_ShouldReturnNotImplemented() {
        // Arrange
        var warso = new WarStrategyOptimizationAlgorithm<Flt64, Flt64, Flt64>();
        var model = new TestCallBackModel(dimension: 3, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await warso.InvokeAsync(model);

        // Assert
        result.IsFailed.Should().BeTrue();
        var failed = (Utils.Functional.Failed<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>)result;
        failed.Code.Should().Be(ErrorCode.ApplicationError);
        failed.Message.Should().Contain("not implemented");
    }

    // ==================== WCA ====================

    /// <summary>
    /// WCA should return failed result with ApplicationError.
    /// </summary>
    [Fact]
    public async Task WaterCycleAlgorithm_ShouldReturnNotImplemented() {
        // Arrange
        var wca = new WaterCycleAlgorithm<Flt64, Flt64, Flt64>();
        var model = new TestCallBackModel(dimension: 3, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await wca.InvokeAsync(model);

        // Assert
        result.IsFailed.Should().BeTrue();
        var failed = (Utils.Functional.Failed<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>)result;
        failed.Code.Should().Be(ErrorCode.ApplicationError);
        failed.Message.Should().Contain("not implemented");
    }
}
