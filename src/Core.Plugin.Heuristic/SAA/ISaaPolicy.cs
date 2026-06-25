#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Saa
{
    /// <summary>
    /// 模拟退火策略接口 / Simulated Annealing policy interface.
    /// </summary>
    public interface ISaaPolicy
    {
        string Name { get; }
        int MaxIterations { get; }
        int MarkovLength { get; }
        bool Finished(Iteration iteration);
        /// <summary>获取当前温度 / Get current temperature</summary>
        Flt64 Temperature(Iteration iteration);
        /// <summary>接受准则 / Acceptance criterion: returns true if worse solution should be accepted</summary>
        bool Accept(Iteration iteration, Flt64 currentObjective, Flt64 newObjective);
    }
}
