#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp3d.Application
{
    /// <summary>物料装箱混合需求策略 / Material packing mixed demand policy.</summary>
    public enum MaterialPackingMixedDemandPolicy { Reject, PreferItem, PreferMaterial }

    /// <summary>列生成应用请求 / Column generation application request.</summary>
    public sealed record ColumnGenerationApplicationRequest(
        IReadOnlyList<(Item Item, ulong Amount)> ItemDemands,
        IReadOnlyList<(Material<FltX> Material, ulong Amount)>? MaterialAmountDemands = null,
        IReadOnlyList<(Material<FltX> Material, Quantity<FltX> Weight)>? MaterialWeightDemands = null,
        IReadOnlyList<Bpp3dDemandEntry<FltX>>? DemandEntries = null,
        IReadOnlyList<BinLayer>? InitialColumns = null,
        IReadOnlyList<Bin<BinLayer, FltX>>? FinalBins = null,
        IReadOnlyList<IBpp3dLayerGenerator<FltX>>? Generators = null,
        ColumnGenerationConfig? CgConfig = null,
        DepthBoundaryLayerOrientationPolicy? DepthBoundaryLayerOrientationPolicy = null,
        ColumnGenerationStandardExecutorConfig? ExecutorConfig = null);

    /// <summary>列生成应用响应 / Column generation application response.</summary>
    public sealed record ColumnGenerationApplicationResponse(
        ColumnGenerationResult<FltX> Result);

    /// <summary>列生成应用服务 / Column generation application service.</summary>
    public sealed class ColumnGenerationApplicationService
    {
        private readonly IColumnGenerationSolver _solver;

        public ColumnGenerationApplicationService(IColumnGenerationSolver solver)
        {
            _solver = solver;
        }

        public static IReadOnlyList<IBpp3dLayerGenerator<FltX>> DefaultGenerators { get; } = new IBpp3dLayerGenerator<FltX>[]
        {
            new BlockLayerGenerator(), new BLLocalLayerGenerator(), new BLGlobalLayerGenerator(),
            new PatternLayerGenerator(), new PileLayerGenerator(), new CirclePackingLayerGenerator(),
            new HistoricalLayerGenerator()
        };

        /// <summary>求解 / Solve.</summary>
        public async Task<Result<ColumnGenerationApplicationResponse, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ColumnGenerationApplicationRequest request,
            ColumnGenerationSolutionAnalyzer<FltX>? solutionAnalyzer = null)
        {
            var resolvedItemDemands = request.ItemDemands;
            var entries = request.DemandEntries ?? BuildDemandEntries(resolvedItemDemands);

            var executors = ColumnGenerationStandardExecutors.FromDemandEntries(
                solver: _solver,
                itemDemands: resolvedItemDemands,
                demandEntries: entries.ToList(),
                finalBins: request.FinalBins,
                config: request.ExecutorConfig ?? ColumnGenerationStandardExecutorConfig.Default);

            var generators = request.Generators?.Count > 0 ? request.Generators : DefaultGenerators;
            var layerGenerator = new LayerGenerationAdapter(generators);

            var algorithm = new ColumnGenerationAlgorithm<FltX>(
                layerGenerator: layerGenerator,
                rmpSolver: executors.RmpSolver(),
                finalMilpSolver: executors.FinalSolver(),
                layerRequestBuilder: executors.RequestBuilder(),
                solutionAnalyzer: solutionAnalyzer,
                initialColumns: () => Task.FromResult(request.InitialColumns ?? (IReadOnlyList<BinLayer>)Array.Empty<BinLayer>()));

            var solveResult = await algorithm.SolveAsync(
                resolvedItemDemands.Select(d => d.Item).ToList(),
                request.CgConfig);

            if (solveResult is not Ok<ColumnGenerationResult<FltX>, ErrorCode, Error<ErrorCode>> ok)
            {
                var err = (solveResult as Failed<ColumnGenerationResult<FltX>, ErrorCode, Error<ErrorCode>>)?.Error
                    ?? new Err<ErrorCode>(ErrorCode.Unknown);
                return new Failed<ColumnGenerationApplicationResponse, ErrorCode, Error<ErrorCode>>(err);
            }

            return new Ok<ColumnGenerationApplicationResponse, ErrorCode, Error<ErrorCode>>(
                new ColumnGenerationApplicationResponse(ok.Value));
        }

        private static IReadOnlyList<Bpp3dDemandEntry<FltX>> BuildDemandEntries(IReadOnlyList<(Item Item, ulong Amount)> itemDemands)
        {
            return itemDemands.Select(d => new Bpp3dDemandEntry<FltX>(
                new Bpp3dDemandMode.ItemAmount(),
                new Bpp3dDemandKey.ItemKey(d.Item.Id),
                new FltX((double)d.Amount))).ToList();
        }
    }

    /// <summary>层生成适配器 / Layer generation adapter.</summary>
    internal sealed class LayerGenerationAdapter : IBpp3dLayerGenerator<FltX>
    {
        private readonly IReadOnlyList<IBpp3dLayerGenerator<FltX>> _generators;
        public LayerGenerationAdapter(IReadOnlyList<IBpp3dLayerGenerator<FltX>> generators) => _generators = generators;

        public async Task<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>> GenerateAsync(Bpp3dLayerGenerationRequest<FltX> request)
        {
            var all = new List<Bpp3dLayerGenerationResult<FltX>>();
            foreach (var gen in _generators)
                all.AddRange(await gen.GenerateAsync(request));
            return all;
        }
    }
}
