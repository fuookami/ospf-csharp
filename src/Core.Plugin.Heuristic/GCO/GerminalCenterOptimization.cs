#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Gco
{
    /// <summary>
    /// 生发中心优化 / Germinal Center Optimization. Not implemented yet.
    /// </summary>
    public sealed class GerminalCenterOptimization<Obj, ObjValue, V> : HeuristicAlgorithm<Obj, ObjValue, V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        public override Task<Result<List<IIndividual<ObjValue, V>>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IAbstractCallBackModelInterface<Obj, ObjValue, V> model,
            Func<Iteration, IIndividual<ObjValue, V>, IReadOnlyList<IIndividual<ObjValue, V>>, Result<Success, ErrorCode, Error<ErrorCode>>>? runningCallBack = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Results.Failed<List<IIndividual<ObjValue, V>>>(
                    new Err<ErrorCode>(ErrorCode.ApplicationError, "GerminalCenterOptimization is not implemented yet.")));
        }
    }
}
