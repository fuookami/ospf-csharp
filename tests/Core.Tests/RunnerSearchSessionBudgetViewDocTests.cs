// 评估会话的预算视图：说明与代码一致（B777）/ The evaluation session's budget views: remarks match the code (B777).

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// **`Rejected`/`Remaining` 是"既定形状差异"，不是缺口（B777）** / `Rejected`/`Remaining` are an established shape difference, not a gap (B777).
/// </summary>
/// <remarks>
/// 说明原来说"本移植的算子尚未有消费者"。查过之后：**基线的消费者在**——`FitnessEvaluator` 读 `rejected` 丢掉被拒的评估、
/// `LocalSearchUpdates` 读 `remaining <= 2` 提前退出。本移植在 Batch 157 把约束与预算从模型上剥离（`SearchConstraints` 取代模型装饰器，
/// 预算走 `SearchBudget`），所以算子拿不到模型侧的读数，**调用方用 `SearchBudget` 包住整次调用**。
///
/// 两句话的意思完全不同：前者是"这边还没人用"（暗示等有人用了就补），后者是"**架构上就不在那里**"（补了也没人用）。
///
/// The remarks used to say "this port's operators have no consumer yet". Checking: the **baseline consumers exist** — `FitnessEvaluator` reads `rejected` to drop a rejected
/// evaluation and `LocalSearchUpdates` reads `remaining <= 2` to exit early. This port detached constraints and budgets from the model in Batch 157 (`SearchConstraints`
/// replaced the model decorator, budgets go through `SearchBudget`), so operators cannot reach either reading and **the caller wraps the whole call in a `SearchBudget`**.
///
/// The two sentences mean very different things: the first is "nobody uses it here yet" (implying it will come back once someone does), the second is "**it is architecturally not
/// there**" (adding it would leave it unused).
/// </remarks>
public class RunnerSearchSessionBudgetViewDocTests {
    private static string Source(params string[] parts) {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            string[] all = new string[parts.Length + 1];
            all[0] = directory;
            Array.Copy(parts, 0, all, 1, parts.Length);
            string candidate = Path.Combine(all);
            if (File.Exists(candidate)) {
                return File.ReadAllText(candidate);
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("could not locate " + parts[^1]);
    }

    /// <summary>
    /// **说明写出"基线的消费者在哪"与"本移植为什么不补"** / The remarks name both the baseline consumers and why nothing is added here.
    /// </summary>
    /// <remarks>
    /// 四点成对：点名 `FitnessEvaluator` / 点名 `LocalSearchUpdates` / 点名 `SearchBudget` / **不再出现"尚未有消费者"**。
    /// 那四个词看起来无害，实际把"架构决定"写成了"待办"。
    ///
    /// Four paired points: `FitnessEvaluator` is named; `LocalSearchUpdates` is named; `SearchBudget` is named; and **"尚未有消费者" is gone**. Those words look harmless but turn
    /// an architectural decision into a to-do.
    /// </remarks>
    [Fact]
    public void Remarks_ShouldExplainTheShapeDifferenceInsteadOfAConsumerWait() {
        string source = Source("src", "Core", "Solver", "Heuristic", "Running", "RunnerSearchSession.cs");

        source.Should().Contain("FitnessEvaluator");
        source.Should().Contain("LocalSearchUpdates");
        source.Should().Contain("SearchBudget");
        source.Should().NotContain("本移植的算子尚未有消费者");
    }

    /// <summary>
    /// **算子的那份差异说明还在**（两处不能各说各话）/ The operator-side note is still there (the two places must not disagree).
    /// </summary>
    /// <remarks>
    /// 两点成对：`LocalSearchComposites` 里"没有模型侧预算读取"这句在 / 它点名 `SearchBudget`。
    /// 会话侧说"调用方包住"，算子侧说"这里不读模型"——**两句话必须同时存在，否则单看一处会以为另一边漏了**。
    ///
    /// Two paired points: the "no model-side budget read" sentence is in `LocalSearchComposites`; and it names `SearchBudget`. The session side says "the caller wraps it",
    /// the operator side says "nothing is read here" — **both must exist, or reading just one makes it look like the other side is missing.**
    /// </remarks>
    [Fact]
    public void OperatorSide_ShouldStillCarryItsHalfOfTheStory() {
        string source = Source("src", "Core", "Solver", "Heuristic", "Operator", "Update", "LocalSearchComposites.cs");

        source.Should().Contain("没有");
        source.Should().Contain("SearchBudget");
    }
}
