// 策略训练契约与轨迹文件测试 / Tests for the policy-training contract and trajectory files.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Learning;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 策略训练的数据契约 / The policy trainer's data contract.
/// </summary>
public sealed class PolicyTrainingTests {
    /// <summary>**训练集不能为空** / The training set must not be empty.</summary>
    [Fact]
    public void Validate_ShouldRejectEmptyTraining() =>
        Split(Array.Empty<TrainingCase>(), new[] { Case("v") }).Validate().Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();

    /// <summary>**标识必须全局唯一** / Identifiers must be globally unique.</summary>
    [Fact]
    public void Validate_ShouldRejectDuplicateIds() =>
        Split(new[] { Case("a"), Case("a") }, new[] { Case("b") }).Validate().Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();

    /// <summary>**同一个问题不能同时出现在训练集与测试集里** / One problem cannot appear in both the training and the test set.</summary>
    /// <remarks>
    /// 这是本校验最容易被当成"多余"的一条：标识明明不同。但同一问题跨集合出现会让"泛化"失去意义——
    /// **而报告上的训练与测试指标都会很漂亮**。
    ///
    /// This is the check most likely to be judged "redundant": the identifiers clearly differ. But one problem spanning sets makes "generalization"
    /// meaningless — **while both the training and test numbers look excellent**.
    /// </remarks>
    [Fact]
    public void Validate_ShouldRejectRepeatedProblemAcrossSplits() {
        TrainingCase shared = Case("train-1", "p1");
        Split(new[] { shared }, new[] { Case("test-1", "p1") }).Validate().Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        Split(new[] { shared }, new[] { Case("test-1", "p2") }).Validate().Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**回放先按作用域筛选，再做重复检测** / Replay filters by scope before checking duplicates.</summary>
    /// <remarks>
    /// 反过来会让"另一个作用域也记录了同一段轨迹"被当成重复，**于是合法的轨迹因为被重复记录而报错**。
    /// The other order reports "another scope recorded the same trajectory" as a duplicate, **so legitimate trajectories fail because they were
    /// recorded twice**.
    /// </remarks>
    [Fact]
    public void Replay_ShouldFilterScopeBeforeDuplicateCheck() {
        PolicyTrainer<Flt64, Flt64> trainer = Trainer();
        ISelectionPolicy policy = Policy();
        SelectionTransition algorithm = Transition("e1", 1, "algorithm");
        SelectionTransition composite = Transition("e1", 1, "composite");

        trainer.Replay(policy, new[] { algorithm, composite }, "progress:1").Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>(
                "同一段轨迹在另一个作用域里不算重复 / the same trajectory in another scope is not a duplicate");

        trainer.Replay(policy, new[] { algorithm, algorithm }, "progress:1").Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**特征版本不符时拒绝回放** / Replay refuses a mismatched feature version.</summary>
    [Fact]
    public void Replay_ShouldRejectFeatureVersionMismatch() =>
        Trainer().Replay(Policy(), new[] { Transition("e1", 1, "algorithm") }, "progress:2").Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();

    /// <summary>**轨迹文件可往返，且不留下临时文件** / Trajectory files round-trip and leave no temp file behind.</summary>
    /// <remarks>
    /// "不留临时文件"是一条真断言：写入走"临时文件 → 移动"，**移动失败或异常路径漏删就会把半截文件留在目录里**，
    /// 而读取那边只会看到"轨迹少了几条"。
    ///
    /// "No temp file is left" is a real assertion: writing goes "temp file → move", and **a failed move or an exception path that misses the delete
    /// leaves a half-written file in the directory**, while the reading side only sees "a few trajectories missing".
    /// </remarks>
    [Fact]
    public void TrajectoryFiles_ShouldRoundTrip() {
        string directory = Path.Combine(Path.GetTempPath(), "ospf-trajectory-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "trajectory.jsonl");
        try {
            TrajectoryFiles.Write(path, new[] {
                Transition("e1", 1, "algorithm"),
                Transition("e1", 2, "algorithm"),
            }).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

            Result<List<SelectionTransition>, ErrorCode, Error<ErrorCode>> read =
                TrajectoryFiles.Read(path);
            read.Should().BeOfType<Ok<List<SelectionTransition>, ErrorCode, Error<ErrorCode>>>();
            List<SelectionTransition> transitions =
                ((Ok<List<SelectionTransition>, ErrorCode, Error<ErrorCode>>)read).Value;
            transitions.Should().HaveCount(2);
            transitions[1].Decision.Should().Be(2L);

            Directory.GetFiles(directory, "*.tmp-*").Should().BeEmpty(
                "写入之后不应留下临时文件 / no temp file may remain after a write");
        } finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>**写入失败时也不留下临时文件** / A failed write leaves no temp file either.</summary>
    /// <remarks>
    /// 成功路径根本不会留下临时文件（它被移走了），所以**只测成功路径等于没测这条**——本批第一次变异正是这么存活下来的。
    /// 这里让目标路径本身是一个目录，移动必然失败，再看目录里有没有残留。
    ///
    /// A successful write never leaves a temp file (it got moved away), so **testing only the success path tests nothing here** — which is exactly how
    /// the first mutation of this batch survived. The target path is made a directory so the move must fail, and the parent is then checked for leftovers.
    /// </remarks>
    [Fact]
    public void TrajectoryFiles_ShouldNotLeaveTempFileOnFailure() {
        string directory = Path.Combine(Path.GetTempPath(), "ospf-trajectory-fail-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(directory, "occupied");
        Directory.CreateDirectory(target);
        try {
            TrajectoryFiles.Write(target, new[] { Transition("e1", 1, "algorithm") }).Should()
                .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();

            Directory.GetFiles(directory, "*.tmp-*").Should().BeEmpty(
                "失败路径也要删掉临时文件 / the failure path must delete the temp file too");
        } finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>**读一个不存在的文件返回失败而不是抛出** / Reading a missing file fails instead of throwing.</summary>
    [Fact]
    public void TrajectoryFiles_ShouldFailOnMissingFile() =>
        TrajectoryFiles.Read(Path.Combine(Path.GetTempPath(), "ospf-missing-" + Guid.NewGuid().ToString("N")))
            .Should().BeOfType<Failed<List<SelectionTransition>, ErrorCode, Error<ErrorCode>>>();

    /// <summary>构造一个划分 / Build a split.</summary>
    /// <param name="training">训练集 / The training set.</param>
    /// <param name="test">测试集 / The test set.</param>
    /// <returns>划分 / The split.</returns>
    private static TrainingSplit Split(
        IReadOnlyList<TrainingCase> training, IReadOnlyList<TrainingCase> test) =>
        new(training, Array.Empty<TrainingCase>(), test);

    /// <summary>构造一个训练实例 / Build a training case.</summary>
    /// <param name="id">标识 / The identifier.</param>
    /// <param name="type">问题规格类型 / The problem spec type.</param>
    /// <returns>训练实例 / The training case.</returns>
    private static TrainingCase Case(string id, string type = "quadratic") =>
        new(id, new ComponentSpec(type, new JsonObject()));

    /// <summary>构造一个训练器 / Build a trainer.</summary>
    /// <returns>训练器 / The trainer.</returns>
    private static PolicyTrainer<Flt64, Flt64> Trainer() =>
        new(new SearchRegistry<Flt64, Flt64>(Fuookami.Ospf.Core.Solver.Value.IIntoValue<Flt64>.Identity));

    /// <summary>构造一个选择策略 / Build a selection policy.</summary>
    /// <returns>选择策略 / The selection policy.</returns>
    private static ISelectionPolicy Policy() =>
        new TabularSelectionPolicy("epsilon-greedy", new[] { "a", "b" });

    /// <summary>构造一段轨迹 / Build a transition.</summary>
    /// <param name="episode">回合 / The episode.</param>
    /// <param name="decision">决策序号 / The decision index.</param>
    /// <param name="scope">作用域 / The scope.</param>
    /// <returns>轨迹 / The transition.</returns>
    private static SelectionTransition Transition(string episode, long decision, string scope) =>
        new(episode, decision, "a",
            new SelectionContext(0, null, 0L, 0L),
            new SelectionContext(0, null, 1L, 0L),
            new[] { "a", "b" },
            new[] { "a", "b" },
            new Flt64(0.5),
            1L,
            false,
            0L,
            "progress:1",
            scope);
}
