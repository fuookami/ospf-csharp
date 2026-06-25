#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Framework.Bpp3d.Application;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Application
{
    /// <summary>Ret 解包辅助 / Ret unwrap helper.</summary>
    internal static class RetAssertions
    {
        public static T Unwrap<T>(this Result<T, ErrorCode, Error<ErrorCode>> result) => result switch
        {
            Ok<T, ErrorCode, Error<ErrorCode>> ok => ok.Value,
            Failed<T, ErrorCode, Error<ErrorCode>> f => throw new AssertionFailedException($"Failed: {f.Error.Message}"),
            Fatal<T, ErrorCode, Error<ErrorCode>> fat => throw new AssertionFailedException($"Fatal: {fat.FirstError?.Message}"),
            _ => throw new AssertionFailedException("Unknown result type")
        };
    }

    internal class AssertionFailedException : Exception
    {
        public AssertionFailedException(string message) : base(message) { }
    }

    public class ColumnGenerationAlgorithmTest
    {
        [Fact]
        public void AlgorithmShouldLiveInApplicationService()
        {
            var algorithm = new ColumnGenerationAlgorithm<FltX>(
                layerGenerator: new NoOpLayerGenerator());
            Assert.NotNull(algorithm);
        }

        [Fact]
        public async Task AlgorithmShouldStopWhenNoAcceptedColumns()
        {
            var algorithm = new ColumnGenerationAlgorithm<FltX>(
                layerGenerator: new NoOpLayerGenerator());

            var result = await algorithm.SolveAsync(
                items: new List<Item>(),
                config: ColumnGenerationConfig.Default with { FinalMilpEnabled = false });

            var unwrapped = result.Unwrap();
            Assert.Equal(0, unwrapped.IterationCount);
            Assert.Empty(unwrapped.Columns);
            Assert.Equal(1, unwrapped.LpSolvedTimes);
            Assert.False(unwrapped.FinalSolved);
        }

        [Fact]
        public async Task AlgorithmShouldCompleteLpSpAddColumnAndFinalMilpFlow()
        {
            var seedItem = new Item("item-cg-seed", "item-cg-seed");
            var generatedItem = new Item("item-cg-generated", "item-cg-generated");

            var seedLayer = MakeLayer("seed");
            var generatedLayer = MakeLayer("generated");

            var rmpCallStates = new List<(int Iteration, int ColumnCount)>();
            var finalSolverColumnCount = -1;

            var algorithm = new ColumnGenerationAlgorithm<FltX>(
                layerGenerator: new TestLayerGenerator(request =>
                {
                    if (request.Iteration == 0)
                    {
                        return Task.FromResult<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>>(
                            new List<Bpp3dLayerGenerationResult<FltX>>
                            {
                                new(generatedLayer, new FltX(-1.0), "test-add-column")
                            });
                    }
                    return Task.FromResult<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>>(
                        Array.Empty<Bpp3dLayerGenerationResult<FltX>>());
                }),
                rmpSolver: async state =>
                {
                    rmpCallStates.Add((state.Iteration, state.Columns.Count));
                    await Task.CompletedTask;
                    return new Ok<ColumnGenerationLpResult<FltX>, ErrorCode, Error<ErrorCode>>(
                        new ColumnGenerationLpResult<FltX>(
                            new Dictionary<DemandModeKey, FltX>(),
                            new FltX(100.0 - state.Iteration),
                            new Dictionary<string, string>
                            {
                                ["solver"] = "stub-lp",
                                ["iteration"] = state.Iteration.ToString()
                            }));
                },
                finalMilpSolver: async state =>
                {
                    finalSolverColumnCount = state.Columns.Count;
                    await Task.CompletedTask;
                    return new Ok<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>(
                        new ColumnGenerationFinalResult<FltX>(
                            state.Columns,
                            Objective: new FltX(77.0),
                            Info: new Dictionary<string, string> { ["solver"] = "stub-final" }));
                },
                initialColumns: () => Task.FromResult<IReadOnlyList<BinLayer>>(new List<BinLayer> { seedLayer }));

            var result = await algorithm.SolveAsync(
                items: new List<Item> { seedItem, generatedItem },
                config: new ColumnGenerationConfig(IterationLimit: 4, MaxColumnsPerIteration: 16));

            var unwrapped = result.Unwrap();
            Assert.Equal(1, unwrapped.IterationCount);
            Assert.Equal(2, unwrapped.LpSolvedTimes);
            Assert.True(unwrapped.FinalSolved);
            Assert.Equal(2, unwrapped.Columns.Count);
            Assert.Equal(new List<(int, int)> { (0, 1), (1, 2) }, rmpCallStates);
            Assert.Equal(2, finalSolverColumnCount);
            Assert.Equal(2, unwrapped.LpInfos.Count);
            Assert.Equal("stub-final", unwrapped.FinalInfo["solver"]);
        }

        [Fact]
        public async Task RmpSolverAndRequestBuilderShouldBePreferredWhenProvided()
        {
            var fallbackRmpCounter = 0;
            var rmpSolverCounter = 0;
            var requestBuilderCounter = 0;
            var observedMaxCandidates = -1;

            var algorithm = new ColumnGenerationAlgorithm<FltX>(
                layerGenerator: new TestLayerGenerator(request =>
                {
                    observedMaxCandidates = request.MaxCandidates;
                    return Task.FromResult<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>>(
                        Array.Empty<Bpp3dLayerGenerationResult<FltX>>());
                }),
                rmpSolver: async state =>
                {
                    Interlocked.Increment(ref rmpSolverCounter);
                    await Task.CompletedTask;
                    return new Ok<ColumnGenerationLpResult<FltX>, ErrorCode, Error<ErrorCode>>(
                        new ColumnGenerationLpResult<FltX>(
                            new Dictionary<DemandModeKey, FltX>(),
                            new FltX(42.0)));
                },
                layerRequestBuilder: async (state, items, config) =>
                {
                    Interlocked.Increment(ref requestBuilderCounter);
                    await Task.CompletedTask;
                    return new Bpp3dLayerGenerationRequest<FltX>(
                        Iteration: state.Iteration,
                        Items: items.Cast<object>().ToList(),
                        ExistingLayers: state.Columns,
                        MaxCandidates: 1);
                },
                solveRmpWithResult: async state =>
                {
                    Interlocked.Increment(ref fallbackRmpCounter);
                    await Task.CompletedTask;
                    return new ColumnGenerationLpResult<FltX>(new Dictionary<DemandModeKey, FltX>());
                });

            var result = await algorithm.SolveAsync(
                items: new List<Item>(),
                config: new ColumnGenerationConfig(MaxColumnsPerIteration: 99));

            var unwrapped = result.Unwrap();
            Assert.Equal(1, rmpSolverCounter);
            Assert.Equal(0, fallbackRmpCounter);
            Assert.Equal(1, requestBuilderCounter);
            Assert.Equal(1, observedMaxCandidates);
            Assert.Equal(new List<FltX?> { new FltX(42.0) }, unwrapped.LpObjectives);
        }

        [Fact]
        public async Task AlgorithmShouldInvokeFinalSolveAnalyzerAndHeartbeatCallbacks()
        {
            var finalSolveCounter = 0;
            var analyzeCounter = 0;
            var heartbeatCounter = 0;

            var algorithm = new ColumnGenerationAlgorithm<FltX>(
                layerGenerator: new NoOpLayerGenerator(),
                solveFinalMilp: async state =>
                {
                    Interlocked.Increment(ref finalSolveCounter);
                    await Task.CompletedTask;
                },
                analyzeSolution: async state =>
                {
                    Interlocked.Increment(ref analyzeCounter);
                    await Task.CompletedTask;
                },
                onIterationHeartbeat: async state =>
                {
                    Interlocked.Increment(ref heartbeatCounter);
                    await Task.CompletedTask;
                });

            var result = await algorithm.SolveAsync(items: new List<Item>());

            var unwrapped = result.Unwrap();
            Assert.Equal(1, finalSolveCounter);
            Assert.Equal(1, analyzeCounter);
            Assert.Equal(0, heartbeatCounter);
            Assert.True(unwrapped.FinalSolved);
        }

        [Fact]
        public async Task ApplicationServiceShouldSupportModelDemandEntryPoint()
        {
            var item = new Item("item-svc", "item-svc");
            var seedLayer = MakeLayer("svc-seed");

            var solver = new FakeColumnGenerationSolver("stub-cg-svc-solver");
            var service = new ColumnGenerationApplicationService(solver);

            var request = new ColumnGenerationApplicationRequest(
                ItemDemands: new List<(Item, ulong)> { (item, 1) },
                InitialColumns: new List<BinLayer> { seedLayer },
                Generators: new List<IBpp3dLayerGenerator<FltX>> { new NoOpLayerGenerator() },
                CgConfig: new ColumnGenerationConfig(FinalMilpEnabled: false));

            var response = await service.SolveAsync(request);

            var unwrapped = response.Unwrap();
            Assert.Equal(1, unwrapped.Result.LpSolvedTimes);
            Assert.Single(unwrapped.Result.Columns);
            Assert.False(unwrapped.Result.FinalSolved);
        }

        // ===== Helpers =====

        private static BinLayer MakeLayer(string id)
        {
            return new BinLayer
            {
                Iteration = 0,
                Shape = new Container3Shape<FltX>(
                    new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter),
                    new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter),
                    new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter)),
                Units = Array.Empty<QuantityPlacement3<Item, FltX>>()
            };
        }
    }

    // ===== Test doubles =====

    internal sealed class NoOpLayerGenerator : IBpp3dLayerGenerator<FltX>
    {
        public Task<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>> GenerateAsync(Bpp3dLayerGenerationRequest<FltX> request)
            => Task.FromResult<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>>(Array.Empty<Bpp3dLayerGenerationResult<FltX>>());
    }

    internal sealed class TestLayerGenerator : IBpp3dLayerGenerator<FltX>
    {
        private readonly Func<Bpp3dLayerGenerationRequest<FltX>, Task<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>>> _generate;
        public TestLayerGenerator(Func<Bpp3dLayerGenerationRequest<FltX>, Task<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>>> generate)
            => _generate = generate;
        public Task<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>> GenerateAsync(Bpp3dLayerGenerationRequest<FltX> request)
            => _generate(request);
    }

    internal sealed class FakeColumnGenerationSolver : Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver
    {
        public string Name { get; }

        public FakeColumnGenerationSolver(string name) { Name = name; }

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name, LinearMetaModel<Flt64> metaModel, bool toLogModel = false,
            RegistrationStatusCallBack? rscb = null, SolvingStatusCallBack? sscb = null)
        {
            var output = new FeasibleSolverOutput<Flt64>(
                new Flt64(9.0),
                new Solution<Flt64>(Array.Empty<Flt64>()),
                TimeSpan.Zero,
                new Flt64(9.0),
                Flt64.Zero);
            return Task.FromResult<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(
                new Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(output));
        }

        public Task<Result<Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name, LinearMetaModel<Flt64> metaModel, bool toLogModel = false,
            RegistrationStatusCallBack? rscb = null, SolvingStatusCallBack? sscb = null)
        {
            var output = new FeasibleSolverOutput<Flt64>(
                new Flt64(3.0),
                new Solution<Flt64>(Array.Empty<Flt64>()),
                TimeSpan.Zero,
                new Flt64(3.0),
                Flt64.Zero);
            var lpResult = new Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver.LpResult(
                output,
                new Dictionary<Fuookami.Ospf.Core.Model.Mechanism.MathConstraint, Flt64>());
            return Task.FromResult<Result<Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>>(
                new Ok<Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>(lpResult));
        }

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name, LinearMetaModel<Flt64> metaModel, ulong amount, bool toLogModel = false,
            RegistrationStatusCallBack? rscb = null, SolvingStatusCallBack? sscb = null)
        {
            var output = new FeasibleSolverOutput<Flt64>(
                new Flt64(9.0), new Solution<Flt64>(Array.Empty<Flt64>()), TimeSpan.Zero, new Flt64(9.0), Flt64.Zero);
            return Task.FromResult<Result<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>>(
                new Ok<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>(
                    (output, new List<List<Flt64>>())));
        }

        public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter,
            bool toLogModel = false, RegistrationStatusCallBack? rscb = null, SolvingStatusCallBack? sscb = null)
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            var output = new FeasibleSolverOutput<V>(
                new Flt64(9.0),
                new Solution<V>(Array.Empty<V>()),
                TimeSpan.Zero,
                new Flt64(9.0),
                Flt64.Zero);
            return Task.FromResult<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>>(
                new Ok<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>(output));
        }

        public Task<Result<Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter,
            bool toLogModel = false, RegistrationStatusCallBack? rscb = null, SolvingStatusCallBack? sscb = null)
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            var output = new FeasibleSolverOutput<V>(
                new Flt64(3.0),
                new Solution<V>(Array.Empty<V>()),
                TimeSpan.Zero,
                new Flt64(3.0),
                Flt64.Zero);
            var lpResult = new Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver.LpResultOf<V>(
                output,
                new Dictionary<Fuookami.Ospf.Core.Model.Mechanism.MathConstraint, Flt64>());
            return Task.FromResult<Result<Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>>(
                new Ok<Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>(lpResult));
        }
    }
}
