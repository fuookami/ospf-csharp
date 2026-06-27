#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;

/// <summary>
/// 渲染适配器 / Renderer adapter.
/// 将装箱结果转换为渲染 DTO / Converts packing results to rendering DTOs.
/// </summary>
public sealed class PackingRendererAdapter {
    /// <summary>
    /// 将装箱结果转换为渲染方案 DTO / Convert packing result to rendering schema DTO.
    /// </summary>
    /// <param name="result">装箱结果 / Packing result</param>
    /// <returns>渲染方案 DTO / Rendering schema DTO</returns>
    public Result<SchemaDTO, ErrorCode, Error<ErrorCode>> ToSchema(PackingResult result) {
        var kpi = new Dictionary<string, string> {
            ["bin_count"] = result.Aggregation.Bins.Count.ToString(),
            ["material_count"] = result.MaterialSummary.Count.ToString()
        };
        if (result.Info is not null) {
            foreach (var kvp in result.Info) {
                kpi[kvp.Key] = kvp.Value;
            }
        }
        return new Ok<SchemaDTO, ErrorCode, Error<ErrorCode>>(new SchemaDTO(kpi));
    }
}
