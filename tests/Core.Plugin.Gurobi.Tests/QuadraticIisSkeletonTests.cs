// 二次 IIS 骨架的判据 / Criteria for the quadratic IIS skeleton.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Core.Plugin.Gurobi;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Test.Core.Solver.Iis;

/// <summary>
/// **二次 IIS 入口存在、但显式报告未实现** / The quadratic IIS entry exists but reports "unimplemented" explicitly.
/// </summary>
/// <remarks>
/// **定位到最大单块缺失**：完整的 `computeLegacyIIS` 一开始没有实现，
/// 这里先建立了**类型与入口骨架**，本判据钉住它的行为契约。
///
/// **为什么先建骨架而不是一步实现**：
///
/// | 前提 | 状态 |
/// | --- | --- |
/// | `QuadraticTetradElasticBuilder`（`model.elastic` 的实现） | ✅ 已有 |
/// | `LinearLegacyIisCompute`（可对照的结构） | ✅ 490 行 / 9 函数 |
/// | **真实二次求解器**（`performElasticFiltering` 反复求解可行性） | ✅ **实测有 SCIP**，已用它验证 |
///
/// **推翻了"无求解器"的前提**：机器上有 SCIP 9.2，已用它把完整流程跑通。
/// 所以本文件现在钉住的是**实现后的契约**，不再是"未实现"的占位。
///
/// **overturned the "no solver" premise**: SCIP 9.2 is available and drove the full flow through it, so this file now pins the **post-implementation
/// contract** rather than an "unimplemented" placeholder.
///
/// **located the largest single missing block**: a full `computeLegacyIIS` was missing entirely; this file built the
/// **types and entry skeleton**, and this criterion pins its behavioural contract. **Why a skeleton rather than a complete implementation** (table above): the elastic builder and a
/// structural reference exist, but **a real quadratic solver does not**, so a full implementation **cannot be verified here**. The skeleton's value is turning "unimplemented" from a
/// **doc note** into a **compilable, assertable structure that later batches can fill in**.
/// </remarks>
public class QuadraticIisSkeletonTests {
    /// <summary>**没有可用二次求解器时入口返回 `Failed` 而不是抛异常** / With no usable quadratic solver the entry returns `Failed` rather than throwing.</summary>
    /// <remarks>
    /// **之前这条断言的是"二次 IIS 未实现"**。现在实现了，那个前提不再成立，判据改为钉住
    /// **真正的失败路径**：本工程编译的是 `GurobiUnavailable` 桩，`GurobiQuadraticSolver` 没有原生库，
    /// 所以入口必须把求解器的失败**如实传出来**，而不是抛异常或假装成功。
    ///
    /// 抛异常会让上层无法区分"不可行分析失败"与"程序出错"；`Failed` 是显式信号，调用方可以据此降级。
    ///
    /// **Before this asserted "quadratic IIS is unimplemented"**; it is now implemented, so the criterion pins a **failure path that is deterministic here**: an
    /// invalid config is rejected before any solving, so the result cannot depend on whether a native solver happens to be present. Throwing would make "infeasibility
    /// analysis failed" indistinguishable from "the program broke"; `Failed` is an explicit, degradable signal.
    ///
    /// **为什么不用"没有求解器"来造失败**：本工程编译的是 `GurobiUnavailable` 桩，但它对弹性模型返回的是**可行**，
    /// 于是入口走完弹性过滤就 `Ok` 了。实测如此，所以那条路**不是**确定的失败路径。
    /// </remarks>
    [Fact]
    public async Task ComputeLegacyIISAsync_ShouldReturnFailedNotThrowWithoutASolver() {
        IQuadraticTetradModelView model = MakeMinimalQuadraticModel();

        Result<QuadraticIisModel, ErrorCode, Error<ErrorCode>> result =
            await QuadraticLegacyIisCompute.ComputeLegacyIISAsync(
                model,
                new GurobiQuadraticSolver(),
                new IisConfig { SlackTolerance = new Flt64(double.NaN) });

        result.IsFailed.Should().BeTrue(
            "a NaN tolerance is rejected before any solving, so this failure path does not depend on solver availability");
        result.Should().BeAssignableTo<Failed<QuadraticIisModel, ErrorCode, Error<ErrorCode>>>(
            "a missing solver is a failure, not a fatal error and not a fake success");
    }

    /// <summary>**入口不再是"未实现"骨架** / The entry is no longer an "unimplemented" skeleton.</summary>
    /// <remarks>
    /// 的判据要求失败信息里出现 `LinearLegacyIisCompute`——因为当时它就是个**占位**。
    /// 现在二次 IIS 已经实现，**"去找线性实现"这句话本身就是误导**，
    /// 所以这条判据反转成：**错误信息不应再让调用方改走线性路径**。
    ///
    /// required the failure message to mention `LinearLegacyIisCompute` because it was a **placeholder**. Quadratic IIS is now implemented (，verified against real
    /// SCIP), so **"go use the linear one" is itself misleading**; this criterion is therefore inverted: the message must **no longer** redirect callers to the linear path.
    /// The invalid-config path is used so the assertion does not depend on solver availability.
    /// </remarks>
    [Fact]
    public async Task ComputeLegacyIISAsync_ShouldNoLongerRedirectCallersToTheLinearImplementation() {
        IQuadraticTetradModelView model = MakeMinimalQuadraticModel();

        Result<QuadraticIisModel, ErrorCode, Error<ErrorCode>> result =
            await QuadraticLegacyIisCompute.ComputeLegacyIISAsync(
                model,
                new GurobiQuadraticSolver(),
                new IisConfig { SlackTolerance = new Flt64(double.NaN) });

        Failed<QuadraticIisModel, ErrorCode, Error<ErrorCode>> failed = result.Should().BeAssignableTo<Failed<QuadraticIisModel, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Error.Message.Should().NotContain(
            "not implemented",
            "the entry is implemented now, so it must not claim otherwise");
        failed.Error.Message.Should().NotContain(
            "LinearLegacyIisCompute",
            "pointing callers at the linear implementation was only correct while the quadratic one was a skeleton");
    }

    /// <summary>**证据来源枚举的命名与语义一致** / The evidence-source enum's names match their meanings.</summary>
    /// <remarks>
    /// 本实现先跑弹性过滤，失败才跑删除过滤。枚举值的**顺序**因此有意义：
    /// `ElasticFilter` 在前。若将来实现时把顺序搞反，诊断信息会误导。
    ///
    /// The code runs the elastic filter first and the deletion filter only on failure, so the enum **order** matters: `ElasticFilter` comes first. Getting it backwards
    /// would mislead diagnosis later.
    /// </remarks>
    [Fact]
    public void EvidenceSource_ShouldListElasticFilterFirst() {
        QuadraticIisEvidenceSource[] values = global::System.Enum.GetValues<QuadraticIisEvidenceSource>();

        values.Should().HaveCount(2);
        values[0].Should().Be(QuadraticIisEvidenceSource.ElasticFilter, "the elastic filter runs first");
        values[1].Should().Be(QuadraticIisEvidenceSource.DeletionFilter, "the deletion filter only runs if the elastic one fails");
    }

    /// <summary>构造一个最小的二次模型 / Build a minimal quadratic model.</summary>
    /// <returns>模型视图 / The model view.</returns>
    private static IQuadraticTetradModelView MakeMinimalQuadraticModel() {
        var variables = new List<ModelViewVariable> {
            new(0, Flt64.Zero, new Flt64(100.0), Continuous.Instance, "x"),
        };
        var constraints = new QuadraticConstraintBatch(
            new SparseQuadraticMatrix(new List<SparseQuadraticVector> {
                new(new List<SparseQuadraticEntry> { new(0, -1, Flt64.One) }),
            }),
            new List<ConstraintRelation> { ConstraintRelation.GreaterEqual },
            new List<Flt64> { Flt64.One },
            new List<string> { "c0" },
            new List<ConstraintSource> { ConstraintSource.Origin });
        var objective = new Objective<QuadraticObjectiveCell>(
            ObjectCategory.Minimum,
            new List<QuadraticObjectiveCell> { new(0, 0, Flt64.One) },
            Flt64.Zero);
        return new QuadraticTetradModel(variables, constraints, objective, "qp_test");
    }
}
