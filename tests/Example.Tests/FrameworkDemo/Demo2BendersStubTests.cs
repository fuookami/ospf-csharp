using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// / / ：Benders 路径**绝不编造方案** / The Benders path never invents a plan.
/// </summary>
/// <remarks>
/// 这条判据一路跟着状态的**前进**走，每次改的是"最终拦路虎叫什么"，**不变的是"不许编造"**：
///
/// | 批次 | 状态 | 原因 |
/// | --- | --- | --- |
/// | | `BendersModelsNotRegistered` | 主/子模型都是空壳 |
/// | | `BendersSubModelNotRegistered` | 主问题接了，子问题待接 |
/// | **** | **`BendersSolverUnavailable`** | 两个模型都接了，缺的是求解器 |
///
/// 判据每次都保留最后三句：`Succeed` 为假、**没有任何分配**、**没有任何渲染**。那三句是这个文件的**存在理由**——
/// 交出"最优解 + 造出来的分配"比交出一个失败**更有害**，因为调用方无从分辨。
///
/// This criterion has followed the status **forward**, changing only "what the final blocker is called" and keeping "no fabrication" fixed (table above). The last
/// three assertions are the reason this file exists: handing back "optimal plus invented assignments" is **more harmful** than a failure, because the caller cannot
/// tell the difference.
/// </remarks>
public sealed class Demo2BendersStubTests
{
    [Fact]
    public void Execute_ShouldRefuseTheBendersPathInsteadOfInventingAPlan() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();

        // 强制走 Benders：策略要求 Benders 且不回退 / Force the Benders path: the policy asks for Benders and forbids falling back.
        RequestDTO bendersRequest = request with {
            SolvePolicy = new SolvePolicy(PreferBenders: true, BendersFallbackToMilp: false),
        };
        (ResponseDTO Response, RenderDTO? Render) executed =
            new FullLoadApplication().Execute(bendersRequest, bendersSolver: null, milpSolver: null);

        executed.Response.Succeed.Should().BeFalse();
        executed.Response.Status.Should().Be("BendersSolverUnavailable",
            "both models are now registered; a missing solver is the honest name for what is left");
        executed.Response.Assignments.Should().BeNull("an invented assignment is worse than no assignment");
        executed.Render.Should().BeNull();

        // 备注里必须留下"两个模型长什么样"，否则"接上了"只能靠读代码相信。
        // The notes must record what both models look like, otherwise "it is wired" can only be believed by reading code.
        (executed.Response.Notes ?? []).Should().Contain(note => note.Contains("benders_single_model=built:"));
        (executed.Response.Notes ?? []).Should().NotContain(note => note.Contains("benders_sub_model_not_registered"),
            "that guard was removed in B833 once the sub registration landed");
    }
}
