#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **不可行不是"求解出错"** / Infeasibility is not "the solve went wrong".
/// </summary>
/// <remarks>
/// 有一条分支本仓没有：
/// **求解器返回 `ORModelInfeasible` 或 `ORModelInfeasibleOrUnbounded` 时，公开响应是 `NoSolution`**，
/// 并追加一句"已映射为 NoSolution"。**本仓原来不看错误码，一律报 `DirectSolveFailed`。**
///
/// **区别不是措辞**：**"求解出错"让人去查后端，"模型不可行"让人去查模型**——
/// **而错误的那一个会把排查引到反方向**。
///
/// **直接测映射函数**：为了跑它去造一个满足 `ILinearSolver` 六个抽象重载的假求解器，
/// **假件会比被测逻辑长十倍，而多出来的部分证明不了任何事。**
///
/// It carries a branch this implementation lacked:
/// **when the solver returns `ORModelInfeasible` or `ORModelInfeasibleOrUnbounded`, the public response is `NoSolution`**,
/// with one sentence saying the mapping happened. **This implementation never looked at the implementation and reported `DirectSolveFailed` for everything.**
///
/// **The difference is not wording**: **"the solve went wrong" sends somebody to the backend, "the model is infeasible" sends them to the model —
/// the wrong one points the search backwards.**
///
/// **The mapping function is tested directly**: building a fake solver that satisfies all six abstract overloads of `ILinearSolver`
/// **would be ten times longer than the logic under test, and the extra part proves nothing.**
/// </remarks>
public class Demo2InfeasibleResponseTests
{
    /// <summary>**不可行报 `NoSolution`，并说明已映射** / Infeasibility reports `NoSolution` and says it was mapped.</summary>
    /// <remarks>
    /// **断言的是状态字符串本身**：`DirectSolveFailed` 与 `NoSolution` 是**两个不同的公开契约**，
    /// 调用方与 `CodeOf` 都按它们分流。
    ///
    /// **The status string itself is what is asserted**: `DirectSolveFailed` and `NoSolution` are **two different public contracts**,
    /// and both callers and `CodeOf` route on them.
    /// </remarks>
    [Theory]
    [InlineData(ErrorCode.ORModelInfeasible)]
    [InlineData(ErrorCode.ORModelInfeasibleOrUnbounded)]
    public void TheInfeasibleSolve_ShouldReportNoSolution(ErrorCode code)
    {
        var notes = new List<string> { "solver_path=milp_direct" };

        ResponseDTO response = DirectMilpRunner.SolverFailureResponse(notes, code, "solver test failure");

        response.Status.Should().Be("NoSolution",
            "an infeasible model is a NoSolution contract, not a solver error");
        (response.Notes ?? []).Any(note => note.Contains("已映射为 NoSolution")).Should().BeTrue(
            "the response still carries the DirectSolveFailed diagnostic, so it has to say the mapping happened");
        response.Notes.Should().Contain("solver_path=milp_direct",
            "the diagnostics gathered before the failure must survive the mapping");
    }

    /// <summary>**其它错误码是 `Error`，且 notes 只有那条错误消息** / Every other code is `Error`, with only the message in the notes.</summary>
    /// <remarks>
    /// **这条防的是"把整个失败分支都改成 NoSolution"**：那会让真正的后端故障
    /// **伪装成"模型不可行"**，而那是**更难查**的那一种。
    ///
    /// **状态名如此**：非不可行分支返回 `ResponseDTO(request, error)`，
    /// 而那个构造函数写死 `status = "Error"`、`notes = listOf(error.message)`。
    /// **`"DirectSolveFailed"` 是本仓自造的**（搜不到），**所以它不是可以钉的预期行为。**
    ///
    /// **This guards against rewriting the whole failure branch as `NoSolution`**: that would disguise a real backend fault
    /// **as "the model is infeasible"**, the **harder** of the two to diagnose.
    ///
    /// **The status name follows the contract**: the non-infeasible branch returns `ResponseDTO(request, error)`,
    /// and that constructor hard-codes `status = "Error"` and `notes = listOf(error.message)`.
 /// **`"DirectSolveFailed"` was invented by this implementation** (it appears nowhere in the implementation), **so it is not expected behaviour to pin.**
    /// </remarks>
    [Theory]
    [InlineData(ErrorCode.OREngineSolvingException)]
    [InlineData(ErrorCode.ORModelUnbounded)]
    public void TheOtherFailures_ShouldBePlainErrors(ErrorCode code)
    {
        ResponseDTO response = DirectMilpRunner.SolverFailureResponse([], code, "solver test failure");

        response.Status.Should().Be("Error",
            "a backend fault must not be reported as an infeasible model, and the baseline calls it Error");
        response.Succeed.Should().BeFalse();
        // **`Equal` 的可变参数就是期望元素，没有 reason 位置**——**这已经是本仓第三次踩同一脚。**
        // **`Equal`'s variadic parameters are the expected elements, with no reason slot** — **the third time in this repo.**
        string[] expectedNotes = ["solver test failure"];
        (response.Notes ?? []).Should().Equal(expectedNotes,
            "the baseline keeps only the error message, not the accumulated diagnostics");
    }
}
