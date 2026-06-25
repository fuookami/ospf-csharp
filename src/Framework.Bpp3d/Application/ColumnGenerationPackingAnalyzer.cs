#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp3d.Application
{
    /// <summary>列生成装箱快照 / Column generation packing snapshot.</summary>
    public sealed record ColumnGenerationPackingSnapshot(
        IReadOnlyList<Bin<BinLayer, FltX>> Bins,
        PackingResult PackingResult,
        SchemaDTO Schema);

    /// <summary>列生成装箱分析器 / Column generation packing analyzer.</summary>
    public sealed class ColumnGenerationPackingAnalyzer
    {
        private readonly Packer _packer;
        private readonly PackingRendererAdapter _rendererAdapter;

        public ColumnGenerationPackingAnalyzer(Packer? packer = null, PackingRendererAdapter? rendererAdapter = null)
        {
            _packer = packer ?? new Packer();
            _rendererAdapter = rendererAdapter ?? new PackingRendererAdapter();
        }

        public ColumnGenerationPackingSnapshot? Latest { get; private set; }

        /// <summary>分析当前列生成状态 / Analyze current CG state.</summary>
        public async Task<Result<Success, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(ColumnGenerationState<FltX> state)
        {
            IReadOnlyList<Bin<BinLayer, FltX>> bins;
            if (state.Bins.Count > 0)
            {
                bins = state.Bins;
            }
            else
            {
                var binList = new List<Bin<BinLayer, FltX>>();
                foreach (var layer in state.Columns)
                {
                    if (layer.Bin is null) continue;
                    var placement = BinLayerHelpers.BinLayerPlacementOf(layer.Copy(), BinLayerHelpers.Point3FltX());
                    binList.Add(BinLayerHelpers.LayerBinOf(layer.Bin, new[] { placement }));
                }
                bins = binList;
            }

            var packingResultRet = await _packer.InvokeAsync(bins);
            if (packingResultRet is not Ok<PackingResult, ErrorCode, Error<ErrorCode>> packingOk)
            {
                var err = (packingResultRet as Failed<PackingResult, ErrorCode, Error<ErrorCode>>)?.Error
                    ?? new Err<ErrorCode>(ErrorCode.Unknown);
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(err);
            }

            var schemaRet = _rendererAdapter.ToSchema(packingResultRet.Value);
            if (schemaRet is not Ok<SchemaDTO, ErrorCode, Error<ErrorCode>> schemaOk)
            {
                var sErr = (schemaRet as Failed<SchemaDTO, ErrorCode, Error<ErrorCode>>)?.Error
                    ?? new Err<ErrorCode>(ErrorCode.Unknown);
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(sErr);
            }

            var schemaKpi = new Dictionary<string, string>(schemaOk.Value.Kpi)
            {
                ["continuous_radius_solver_prototype_count"] = state.ContinuousRadiusSolverPrototypes.Count.ToString(),
                ["continuous_radius_solver_prototype_variables"] = string.Join("|", state.ContinuousRadiusSolverPrototypes.Select(p => p.VariableName))
            };

            Latest = new ColumnGenerationPackingSnapshot(bins, packingResultRet.Value, new SchemaDTO(schemaKpi));
            return new Ok<Success, ErrorCode, Error<ErrorCode>>(Results.SuccessInstance);
        }
    }
}
