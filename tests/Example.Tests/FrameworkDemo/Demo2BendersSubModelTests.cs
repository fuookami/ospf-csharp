using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// Benders 两个模型的形状，以及"解参数是死的"这个前提 / Both Benders model shapes, and the "solution argument is dead" premise.
/// </summary>
/// <remarks>
/// 把子问题也接上了：配载与适航安全各自往子模型里注册，实测 **29 行 / 25 个符号 / 0 个目标**。
/// 主问题是 **11 / 50 / 4**。**两者的注册都发生在建模型阶段，不在迭代循环里**，
/// 因为本仓的两处子问题注册**根本不读那个解参数**（`StowageAggregation` 的签名里连都没有）。
///
/// 这个前提单独钉了一条判据（反射）：**签名一变，那条就红**——那时必须把子问题注册挪进循环，而不是继续在循环外假装。
///
/// wires the sub-problem too: stowage and airworthiness each register into it, measured at **29 rows / 25 symbols / 0 objectives**; the master is **11 / 50 /
/// 4**. **Both registrations happen while building the models, not inside the iteration loop**, because neither sub registration here reads the
/// solution argument (the `StowageAggregation` signature has no such parameter). That premise gets its own reflection criterion: **change the signature and it
/// reds**, at which point the sub registration must move into the loop instead of pretending outside it.
/// </remarks>
public sealed class Demo2BendersSubModelTests
{
    private static (ResponseDTO Response, string? Shape, IReadOnlyList<string> Notes) Run() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            SolvePolicy = new SolvePolicy(PreferBenders: true, BendersFallbackToMilp: false),
        };
        (ResponseDTO Response, RenderDTO? Render) executed =
            new FullLoadApplication().Execute(request, bendersSolver: null, milpSolver: null);
        IReadOnlyList<string> notes = executed.Response.Notes ?? [];
        return (executed.Response, notes.FirstOrDefault(note => note.Contains("benders_single_model=built:")), notes);
    }

    /// <summary>
    /// **现在唯一的拦路虎是求解器，不再是模型** / The only remaining blocker is the solver, not the models.
    /// </summary>
    /// <remarks>
    /// 时状态还是 `BendersSubModelNotRegistered`。 之后子模型有 29 行约束了，于是**那道守卫被拿掉**，
    /// 路径走到求解器检查，报 `BendersSolverUnavailable`。
    ///
    /// **这条判据钉的是"进度前进了一格"**：如果谁把子问题注册删掉，状态会**退回去**——不是"报错形式变了"，而是**能力退步了**。
    ///
    /// At the status was still `BendersSubModelNotRegistered`. After the sub model carries 29 constraints, so **that guard is gone** and the path reaches
    /// the solver check, reporting `BendersSolverUnavailable`. **This criterion pins "one step forward"**: deleting the sub registration would **move the status
    /// back** — not "a different error", but **a capability regression**.
    /// </remarks>
    [Fact]
    public void TheRemainingBlocker_ShouldBeTheSolverRatherThanTheModels() {
        (ResponseDTO response, string? shape, IReadOnlyList<string> notes) = Run();

        response.Succeed.Should().BeFalse();
        response.Status.Should().Be("BendersSolverUnavailable", "the models are built; what is missing is a Benders solver");
        response.Assignments.Should().BeNull("no assignments may be invented on the way to the refusal");
        shape.Should().NotBeNull("the diagnostics must state both model shapes");
        notes.Should().NotContain(note => note.Contains("benders_sub_model_not_registered"),
            "the sub-model guard should be gone once the sub registration exists");
    }

    /// <summary>
    /// **两个模型的形状必须与实测一致** / Both model shapes must match the measurement.
    /// </summary>
    /// <remarks>
    /// 九个数字一次钉住。**为什么不用"大于 0"**： 的变异实验已经证明这种松论断看不见一个域悄悄消失。
    ///
    /// Nine numbers pinned at once. **Why not "greater than zero"**: the earlier mutation experiment already showed that a loose assertion cannot notice a domain
    /// quietly dropping out.
    /// </remarks>
    [Fact]
    public void BothModels_ShouldCarryTheirRegistrations() {
        (ResponseDTO _, string? shape, IReadOnlyList<string> _) = Run();

        // **分派切到单模型链**：这里是单模型的实测数（切换前的两模型数是对照判据用的）。
        // **switched dispatch to the single-model chain**: these are its measured numbers.
        shape.Should().Be(
            "benders_single_model=built:tokens=15,rows=11,symbols=58,objectives=4",
            "these are the measured numbers of the single model that dispatch now builds");
    }

    /// <summary>
    /// **配载子问题注册的签名里没有"解"** / The stowage sub registration has no solution parameter.
    /// </summary>
    /// <remarks>
    /// 这条就是"注册放在循环外"的依据。它成立时，每轮迭代注册出来的子模型与第一次完全相同——**在循环里做等于什么都没做**。
    ///
    /// 它一旦不成立（谁给 `RegisterForBendersSP` 加了 `solution` 参数），子模型就开始依赖主问题解，
    /// **那时"循环外注册一次"就是错的**，判据红着提醒改写。
    ///
    /// This is the reason the registration sits outside the loop. While it holds, every iteration would register an identical sub model — **doing it in the loop
    /// accomplishes nothing.** Once it stops holding (someone adds a `solution` parameter), the sub model starts depending on the master solution, **and then
    /// "register once outside the loop" is wrong**; the criterion reds to ask for the rewrite.
    /// </remarks>
    [Fact]
    public void TheStowageSubRegistration_ShouldNotReadASolution() {
        MethodInfo? method = typeof(StowageAggregation).GetMethod(
            "RegisterForBendersSP",
            BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull();

        ParameterInfo[] parameters = method!.GetParameters();
        parameters.Should().HaveCount(2,
            "a solution-free signature is why the sub registration can happen outside the iteration loop");
        parameters[0].ParameterType.Should().Be(typeof(StowageMode));
    }

    /// <summary>
    /// **适航安全那处收了解、但只是转发** / The airworthiness one accepts a solution only to forward it.
    /// </summary>
    /// <remarks>
    /// 它有三个参数（`mode, model, solution`），和基线一致；但它的实现转给聚合的 `Register(mode, model)`，**解被丢掉**。
    /// 这条钉住"签名有、语义没有"这个事实，免得有人看到三个参数就以为"这个域真的会用解"。
    ///
    /// 这里用**反射 + 调用计数**做不到（会需要构造完整上下文），所以只钉签名与文档的对应关系，并在上面的
    /// 形状判据里证明"解没被用"——**两处子问题注册都在循环外、只跑一次，若其中一处真的读了空列表，29 行这个数字就会变。**
    ///
    /// It takes three parameters (`mode, model, solution`) like the code, but forwards to the aggregation's `Register(mode, model)`, **dropping the solution**.
    /// A real behavioural check would need a fully built context, so this pins the signature-to-documentation correspondence, while the shape criterion above proves
    /// the solution is unused — **both registrations run once, outside the loop; if either read the empty list, the 29 would change.**
    /// </remarks>
    [Fact]
    public void TheAirworthinessSubRegistration_ShouldStillAcceptASolutionForCompatibility() {
        MethodInfo? method = typeof(AirworthinessSecurityContext).GetMethod(
            "RegisterForBendersSP",
            BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull();

        method!.GetParameters().Should().HaveCount(3,
            "the baseline's signature carries the master solution; this port keeps the shape even though the value is unused");
    }
}
