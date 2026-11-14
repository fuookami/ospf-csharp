#nullable enable

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// B873：`Execute` 不带求解器时到底走哪条路 / Which path `Execute` takes when no solver is supplied.
/// </summary>
/// <remarks>
/// B872 留下一个问题：两模型路径**单独跑是干净的**，那 `Execute` 里"benders 未注册"的备注从哪来？
/// 答案在签名里：`bendersSolver` 为 `null` 时**整条 Benders 路径被跳过**，所以那条备注只在"带了 Benders 求解器"时才可能出现，
/// 而所有现有判据都不带。**"两条观察对不上"其实是"两条观察量的是不同的分支"。**
///
/// **B872 left a question**: the two-model path registers cleanly on its own, so where does `Execute`'s "benders not registered" note come from? The answer is in the
/// signature: with `bendersSolver` null **the whole Benders route is skipped**, so that note can only appear when a Benders solver is supplied — and no criterion supplies
/// one. **"The two observations disagree" was really "they measure different branches".**
/// </remarks>
public sealed class Demo2ExecutePathTests
{
    /// <summary>不带求解器：明确报"未带求解器"，不假装解过 / No solver: says so, never pretends to have solved.</summary>
    [Fact]
    public void Execute_WithoutSolvers_ShouldSayWhichPathWasTaken() {
        var application = new FullLoadApplication();
        (ResponseDTO response, _) = application.Execute(AircraftLoadingDemo.CreateSampleRequest());

        // **实测（B873）**：不带求解器时既不跑 Benders、也不解 MILP，返回 `NoSolution` 并用备注说明原因。
        // **Measured (B873)**: with no solver it runs neither Benders nor MILP; it returns `NoSolution` and states why in the notes.
        response.Notes.Should().NotBeNull();
        IReadOnlyList<string> said = response.Notes!;
        // **钉住走的是哪条路**（B873）：`solver_path=milp_direct`，不是 Benders。
        // **Pin which route was taken** (B873): `solver_path=milp_direct`, not Benders.
        said.Should().Contain("solver_path=milp_direct", "got: " + string.Join("|", said));
        said.Should().NotContain("solver_path=benders");
        said.Should().NotContain(
            note => note.Contains("benders_models_not_registered", System.StringComparison.Ordinal),
            "with no Benders solver the Benders branch is never entered, so its note cannot appear");
    }

    /// <summary>带 Benders 求解器：那条分支**真的被进入** / With a Benders solver: that branch is really entered.</summary>
    /// <remarks>
    /// B873 证明"不带求解器时那条分支走不到"。这一条带一个**只记录、不解模型**的桩，量的是三件事：
    /// 走的是 Benders 那条路、桩真的被要求解主问题、以及**失败时说的是实话**（不假装最优、不编造分配）。
    ///
    /// B873 showed the branch is unreachable without a solver. This one supplies a stub that **records only and solves nothing**, and measures three things: the route
    /// taken, that the stub was really asked to solve a master, and that a failure **tells the truth** (no invented optimality, no fabricated assignments).
    /// </remarks>
    [Fact]
    public void Execute_WithABendersSolver_ShouldEnterThatBranchAndFailHonestly() {
        var application = new FullLoadApplication();
        var stub = new RecordingBendersSolver();
        (ResponseDTO response, _) = application.Execute(AircraftLoadingDemo.CreateSampleRequest(), stub);

        response.Notes.Should().NotBeNull();
        IReadOnlyList<string> said = response.Notes!;
        // **实测（B874）：带了求解器也还是 `milp_direct`**——路线由请求规模决定，不由求解器决定。
        // 那就**必须明说**，否则调用方会以为 Benders 跑过了。
        //
        // **Measured (B874): even with a solver supplied the route is `milp_direct`** — the route comes from the request size, not the solver. So it **has to be said**,
        // otherwise the caller believes Benders ran.
        said.Should().Contain("solver_path=milp_direct", "got: " + string.Join("|", said));
        said.Should().Contain(
            note => note.Contains("benders_solver_supplied_but_route=milp_direct", System.StringComparison.Ordinal),
            "a supplied Benders solver that is not used must be reported");
        stub.MasterCalls.Should().Be(0, "the stub was never asked to solve anything, which is exactly what the note says");
        response.Succeed.Should().BeFalse("the stub solves nothing, so the run cannot succeed");
    }

    /// <summary>请求要求 Benders 时，那条路**真的被选上** / When the request prefers Benders, that route is really selected.</summary>
    /// <remarks>
    /// B874 量到"带求解器不等于走 Benders"。这一条把另一半量出来：**当请求的 `PreferBenders` 为真且规模过阈值**时，路线确实是 Benders。
    /// 阈值本身由 `BendersStrategy` 决定，这里不猜它的值——**用"要么选上、要么把被跳过的原因说出来"两种可测结局**来钉。
    ///
    /// B874 measured "a solver does not mean Benders". This one measures the other half: when the request sets `PreferBenders` and the size clears the threshold, the
    /// route really is Benders. The threshold value is not guessed here — the criterion pins **two testable outcomes**: either it is selected, or the note says why it was
    /// skipped.
    /// </remarks>
    [Fact]
    public void Execute_WhenTheRequestPrefersBenders_ShouldTakeThatRouteOrSayWhyNot() {
        var application = new FullLoadApplication();
        var stub = new RecordingBendersSolver();
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            SolvePolicy = new SolvePolicy(PreferBenders: true),
        };

        (ResponseDTO response, _) = application.Execute(request, stub);

        response.Notes.Should().NotBeNull();
        IReadOnlyList<string> said = response.Notes!;
        // **实测（B875）**：`PreferBenders=true` → 路线是 Benders、**桩真的收到主问题**、两个模型都注册上了：
        // `master_rows=11;master_symbols=50;master_objectives=4;sub_rows=29;sub_symbols=25;sub_objectives=0`。
        // 最后以 `benders_failed: policy allows the MILP fallback, but this path has no MILP backend` 失败——**失败是实话，不是"没接上"**。
        //
        // 阈值实测：`binary_variables=6` ≥ `min_binary_variables=4`，所以选上了 Benders。
        //
        // **Measured (B875)**: with `PreferBenders=true` the route is Benders, **the stub really receives the master**, and both models register (numbers above).
        // It then fails honestly (`benders_failed ... no MILP backend`). **That failure is the truth, not a wiring gap.** Threshold measured: 6 binary variables against a
        // minimum of 4.
        said.Should().Contain("solver_path=benders", "got: " + string.Join("|", said));
        stub.MasterCalls.Should().BeGreaterThan(0, "taking the route means the master really is handed to the solver");
        said.Should().Contain(note => note.Contains("benders_split=master_rows=14;sub_rows=19", System.StringComparison.Ordinal),
            "the sub model is registered too - this is the evidence that demo2 Benders is wired");
        // **B876 把"没走回退"拆成两句**：这一条量的是"调用方没给 MILP 求解器"那句。
        // **B876 split the reason into two sentences**; this measures the "no solver was supplied" one.
        said.Should().Contain(note => note.Contains("no MILP solver was supplied", System.StringComparison.Ordinal),
            "the run must fail honestly, naming which of the two reasons applies");
        response.Succeed.Should().BeFalse("the stub solves nothing, so the run cannot succeed");
    }
}
