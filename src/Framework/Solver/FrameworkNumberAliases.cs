#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Number;

// Framework-level number-type aliases (mirrors Kotlin FrameworkNumberAliases.kt).
// These are convenience using aliases for the most common Flt64/Rtn64/FltX/RtnX combinations.

// Flt64 aliases
using Flt64LinearMetaModel = Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>;
using Flt64FeasibleSolverOutput = Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Fuookami.Ospf.Math.Algebra.Number.Flt64>;
using Flt64SolutionPool = System.Collections.Generic.List<System.Collections.Generic.List<Fuookami.Ospf.Math.Algebra.Number.Flt64>>;

// FltX aliases
using FltXLinearMetaModel = Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.FltX>;
using FltXFeasibleSolverOutput = Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Fuookami.Ospf.Math.Algebra.Number.FltX>;

// RtnX aliases
using RtnXLinearMetaModel = Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.RtnX>;
using RtnXFeasibleSolverOutput = Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Fuookami.Ospf.Math.Algebra.Number.RtnX>;
