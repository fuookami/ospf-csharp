using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic.Learning;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// B853：清单上"训练/评估没做"是**陈旧结论** / The inventory's "training/evaluation is missing" is stale.
/// </summary>
/// <remarks>
/// 清单里挂着 `policyTraining` 的 train / evaluate 两项。逐项核对：
///
/// | 清单说的 | 实际 | 判据 |
/// | --- | --- | --- |
/// | train 没做 | `PolicyTrainer&lt;O,V&gt;.Train` 在 | `PolicyTrainingRunTests` 有 3 条 |
/// | evaluate 没做 | `PolicyEvaluator&lt;O,V&gt;.Evaluate` 在 | `PolicyTrainingRunTests` 有 5 条 |
/// | 轨迹文件 | `TrajectoryFiles.Write` / `Read` 在 | `PolicyTrainingTests` |
///
/// **这些不是"新做"，是"做完了没人划掉"。** B849 的 Demo5 是同一个病的第二例。
///
/// 判据钉住这个公开表面：**删掉任何一个入口都会红**，也就不会有人第三次把"训练/评估"写成待办。
///
/// The inventory carries `policyTraining`'s train and evaluate as pending. Checked one by one (table above): **these are not pending work, they are work nobody struck
/// off** — B849's Demo5 was the second case of the same disease. This criterion pins the public surface: **removing any entry point reds it**, so nobody writes
/// "training/evaluation" down as pending a third time.
/// </remarks>
public sealed class PolicyTrainingSurfaceTests
{
    /// <summary>
    /// **训练/评估的公开入口都在** / All the public entry points are present.
    /// </summary>
    /// <remarks>
    /// 反射不检查行为（行为有 `PolicyTrainingRunTests` 的 10 条判据），只检查**入口还在**——
    /// 这两件事分开钉，是因为"入口没了"和"入口算错了"需要不同的修法。
    ///
    /// Reflection does not check behaviour (the ten criteria in `PolicyTrainingRunTests` do); it checks that **the entries still exist**. The two are pinned apart because
    /// "the entry is gone" and "the entry computes the wrong thing" need different repairs.
    /// </remarks>
    [Fact]
    public void TheTrainingAndEvaluationEntries_ShouldExist() {
        Type trainer = typeof(PolicyTrainer<Flt64, Flt64>);
        trainer.GetMethod("Train").Should().NotBeNull("training is the first of the two pending items in the inventory");
        trainer.GetMethod("Replay").Should().NotBeNull("replaying a recorded trajectory is the way to re-check a run");

        Type evaluator = typeof(PolicyEvaluator<Flt64, Flt64>);
        evaluator.GetMethod("Evaluate").Should().NotBeNull("evaluation is the second pending item");

        Type files = typeof(TrajectoryFiles);
        files.GetMethod("Write").Should().NotBeNull("trajectories must be writable to disk");
        files.GetMethod("Read").Should().NotBeNull("and readable back, otherwise a run cannot be re-checked");
    }

    /// <summary>
    /// **训练与评估各有一条行为判据在跑** / Each of the two has behavioural criteria running.
    /// </summary>
    /// <remarks>
    /// 这条判据扫的是**判据文件本身**：`PolicyTrainingRunTests` 里必须仍然有调用 `Train(` 与 `Evaluate(` 的用例。
    /// 它挡住的是"有人把行为判据删了但把入口留着"——那时表面判据还绿，而**行为已经没人验**。
    ///
    /// This criterion scans **the criteria file itself**: `PolicyTrainingRunTests` must still contain cases calling `Train(` and `Evaluate(`. It blocks "someone deleted
    /// the behavioural criteria but kept the entry points" — the surface criterion would still be green while **nobody verifies the behaviour**.
    /// </remarks>
    [Fact]
    public void TheBehaviouralCriteria_ShouldStillExerciseBothEntries() {
        string source = FindTestSource("PolicyTrainingRunTests.cs");

        source.Should().Contain(".Train(", "the trainer's behaviour must be exercised by a criterion");
        source.Should().Contain(".Evaluate(", "the evaluator's behaviour must be exercised by a criterion");
    }

    /// <summary>找到某个判据文件 / Locate a criteria file.</summary>
    /// <param name="name">文件名 / File name.</param>
    /// <returns>内容 / The content.</returns>
    private static string FindTestSource(string name) {
        var directory = new global::System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            string candidate = global::System.IO.Path.Combine(directory.FullName, "tests", "Core.Tests", name);
            if (global::System.IO.File.Exists(candidate)) {
                return global::System.IO.File.ReadAllText(candidate);
            }
            directory = directory.Parent;
        }

        throw new global::System.IO.FileNotFoundException("could not locate " + name);
    }
}
