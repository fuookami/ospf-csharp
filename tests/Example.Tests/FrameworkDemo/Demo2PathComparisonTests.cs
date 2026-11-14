#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// B872：两条构建路径的实测对比 / The measured comparison between the two build paths.
/// </summary>
/// <remarks>
/// **结论出乎预料**：两模型那条路在**全新的应用实例**上就失败——配载那一半报 `上下文已经注册 / Context already registered`；
/// 而单模型那条路四处全成功。也就是说，**demo2 的 Benders 一直没接上，不是"子问题待接"，是主问题第一步就被拒**。
///
/// **The conclusion was not expected**: the two-model path fails on a **fresh application** — the stowage half reports `Context already registered` — while the
/// single-model path registers all four halves. In other words **demo2's Benders was never wired: not "the sub is pending", but the master's first step is refused**.
/// </remarks>
public sealed class Demo2PathComparisonTests
{
    /// <summary>单模型那条路：四处注册全成功 / The single-model path: all four halves register.</summary>
    [Fact]
    public void TheSingleModelPath_ShouldRegisterEverything() {
        var application = new FullLoadApplication();
        application.Init(AircraftLoadingDemo.CreateSampleRequest()).IsOk.Should().BeTrue();

        List<string> notes = [];
        using Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>? model =
            application.BuildSingleBendersModel(StowageMode.FullLoad, notes);
        model.Should().NotBeNull(string.Join("; ", notes));

        // **实测数（B872）**：15 个 token、11 行、58 个符号、4 个目标。
        // **Measured (B872)**: fifteen tokens, eleven rows, fifty-eight symbols, four objectives.
        notes.Should().Contain("benders_single_model=built:tokens=15,rows=11,symbols=58,objectives=4");
        notes.Should().NotContain(note => note.Contains("failed", System.StringComparison.Ordinal));
    }

    /// <summary>两模型那条路单独跑：注册成功、无备注 / The two-model path on its own: registers cleanly, no notes.</summary>
    [Fact]
    public void TheTwoModelPath_ShouldRegisterWhenItRunsFirst() {
        var application = new FullLoadApplication();
        application.Init(AircraftLoadingDemo.CreateSampleRequest()).IsOk.Should().BeTrue();

        List<string> notes = [];
        FullLoadApplication.BendersModels models = application.BuildBendersModels(StowageMode.FullLoad, notes);
        models.MasterModel.Dispose();
        models.SubModel.Dispose();
        // **实测（B872）**：两模型那条路在**单独跑**时注册成功、备注为空；而在**同一个进程里先跑过单模型那条路**之后，
        // 它的配载注册就被拒（`上下文已经注册 / Context already registered`）——**域上下文跨应用实例共享一次性状态**。
        //
        // **Measured (B872)**: run alone the two-model path registers cleanly; but **after the single-model path has run in the same process**, its stowage
        // registration is refused (`Context already registered`) — **the domain contexts share one-shot state across application instances**.
        notes.Should().BeEmpty("the two-model path registers cleanly when it runs first in the process");
    }
}
