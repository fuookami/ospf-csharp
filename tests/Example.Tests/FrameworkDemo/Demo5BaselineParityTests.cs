using System;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo5;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// B849：基线 Demo5 的每个文件，C# 侧都有对应物 / Every baseline Demo5 file has a counterpart here.
/// </summary>
/// <remarks>
/// 计划里"Demo5 迁移"挂了很久，**查下来已经做完了**：基线 `framework_demo/demo5/` 的六个文件，
/// 在这里都有对应类型，而且 `Demo5SolverFactory` 比基线更严格（基线 `when (solver) { "scip" -> …; else -> Gurobi }`
/// 会把拼错的名字**静默当成 Gurobi**，这里显式失败）。
///
/// 这条判据把**文件到类型的映射**写进代码：删掉任何一个类型都会红，也就不会有人再把"Demo5 迁移"当成待办。
///
/// The plan carried "Demo5 migration" for a long time, and **it is already done**: all six files of the baseline's `framework_demo/demo5/` have counterparts here, and
/// `Demo5SolverFactory` is stricter than the baseline (its `when (solver) { "scip" -> …; else -> Gurobi }` would **silently treat a misspelt name as Gurobi**;
/// this version fails explicitly). The criterion writes the **file-to-type mapping** into code: deleting any type reds it, so nobody mistakes "Demo5 migration" for
/// pending work again.
/// </remarks>
public sealed class Demo5BaselineParityTests
{
    /// <summary>
    /// **六个基线文件都有对应类型** / All six baseline files have counterpart types.
    /// </summary>
    /// <remarks>
    /// 映射（基线文件 → C# 类型）：
    ///
    /// | 基线 | C# |
    /// | --- | --- |
    /// | `Application.kt` | `Application` |
    /// | `infrastructure/SemanticParameter.kt` | `SemanticParameter` |
    /// | `infrastructure/Solver.kt` | `Demo5SolverFactory` |
    /// | `infrastructure/dto/Input.kt` | `Input` |
    /// | `infrastructure/dto/Output.kt` | `Output` + `RouteOutput` + `StopOutput` + `TraceOutput` |
    /// | `infrastructure/io/SolomonData*.kt` | `SolomonDataReader` + `SolomonInstanceAdapter` |
    ///
    /// The mapping above is what this criterion pins.
    /// </remarks>
    [Fact]
    public void EveryBaselineFile_ShouldHaveACounterpartType() {
        Type[] expected = [
            typeof(Application),
            typeof(SemanticParameter),
            typeof(Demo5SolverFactory),
            typeof(Input),
            typeof(Output),
            typeof(RouteOutput),
            typeof(StopOutput),
            typeof(TraceOutput),
            typeof(Fuookami.Ospf.Example.FrameworkDemo.Demo5.Solomon.SolomonDataReader),
            typeof(Fuookami.Ospf.Example.FrameworkDemo.Demo5.Solomon.SolomonInstanceAdapter<Fuookami.Ospf.Math.Algebra.Number.Flt64>),
        ];

        foreach (Type type in expected) {
            type.Should().NotBeNull();
            type.IsPublic.Should().BeTrue(type.Name + " must stay public: it is the counterpart of a baseline file");
        }
    }

    /// <summary>
    /// **未知求解器名必须失败，不能静默退回 Gurobi** / An unknown solver name must fail rather than quietly falling back to Gurobi.
    /// </summary>
    /// <remarks>
    /// 这是本仓**故意偏离基线**的一处，也是唯一一处偏离：基线把拼错的名字当成 Gurobi，
    /// 于是 `"gruobi"` 会拿到一个**能跑但用错后端**的求解器。判据钉住这个偏离，免得以后有人"为了对齐基线"把它改回去。
    ///
    /// This is the port's **one deliberate divergence**: the baseline treats a misspelt name as Gurobi, so `"gruobi"` yields a solver that **runs with the wrong
    /// backend**. The criterion pins the divergence so nobody "aligns with the baseline" by reverting it.
    /// </remarks>
    [Fact]
    public void AnUnknownSolverName_ShouldFail() {
        Result<IColumnGenerationSolver, ErrorCode, Error<ErrorCode>> created = Demo5SolverFactory.Create("gruobi");

        created.IsFailed.Should().BeTrue("a misspelt backend name must not silently select Gurobi");
        created.IsOk.Should().BeFalse();
    }
}
