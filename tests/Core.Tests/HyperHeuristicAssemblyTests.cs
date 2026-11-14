// 超启发式装配测试 / Tests for the hyper-heuristic assembly.

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 超启发式的算子绑定与工作单元装配 / Operator bindings and worker assembly.
/// </summary>
public sealed class HyperHeuristicAssemblyTests {
    /// <summary>**配置里点名的算子都会被解析成候选** / Every operator named in the configuration becomes a candidate.</summary>
    [Fact]
    public void Bindings_ShouldResolveNamedOperators() {
        Result<Fuookami.Ospf.Core.Solver.Heuristic.Operator.OperatorBindings, ErrorCode,
            Error<ErrorCode>> built = Bindings(
            Algorithm(updates: new[] { "sbx" }, mutations: new[] { "gaussian" }),
            encoding: SearchEncoding.Continuous);

        built.Should().BeOfType<Ok<Fuookami.Ospf.Core.Solver.Heuristic.Operator.OperatorBindings,
            ErrorCode, Error<ErrorCode>>>();
        Fuookami.Ospf.Core.Solver.Heuristic.Operator.OperatorBindings bindings =
            ((Ok<Fuookami.Ospf.Core.Solver.Heuristic.Operator.OperatorBindings, ErrorCode,
                Error<ErrorCode>>)built).Value;
        bindings.Update!.Candidates.Should().ContainKey("sbx");
        bindings.Mutation!.Candidates.Should().ContainKey("gaussian");
        bindings.Neighborhood.Should().BeNull();
    }

    /// <summary>**未知算子必须失败，不能被悄悄丢掉** / An unknown operator must fail rather than be dropped silently.</summary>
    /// <remarks>
    /// 丢掉它会让算法用"剩余的算子"继续跑，**而配置上写着那个算子**——事后从运行轨迹里根本看不出它从未被执行。
    /// Dropping it lets the algorithm carry on with the remaining operators **while the configuration names the dropped one**, and nothing in the run
    /// trace ever shows it never ran.
    /// </remarks>
    [Fact]
    public void Bindings_ShouldRejectUnknownOperator() {
        // **必须断言错误指向"未知算子"**，而不只是"失败了"：若算子被悄悄跳过，空候选集会被选择器那条校验拦住，
        // 于是"失败"依然成立——**只是失败原因换成了别的东西**，而配置里的拼写错误再也没人指出。本批第一次变异正是这么存活的。
        // **The error must be asserted to point at the unknown operator**, not merely at "it failed": if the operator is silently skipped, the empty
        // candidate set is caught by the selector's own validation, so "it failed" still holds — **only the reason has changed**, and the misspelt name
        // in the configuration is never mentioned again. That is exactly how the first mutation of this batch survived.
        Result<Fuookami.Ospf.Core.Solver.Heuristic.Operator.OperatorBindings, ErrorCode,
            Error<ErrorCode>> built = Bindings(
            Algorithm(updates: new[] { "nope" }), encoding: SearchEncoding.Continuous);

        built.Should().BeOfType<Failed<Fuookami.Ospf.Core.Solver.Heuristic.Operator.OperatorBindings,
            ErrorCode, Error<ErrorCode>>>();
        ((Failed<Fuookami.Ospf.Core.Solver.Heuristic.Operator.OperatorBindings, ErrorCode,
            Error<ErrorCode>>)built).Error.Message.Should().Contain("unknown update",
            "错误必须指名未知算子 / the error must name the unknown operator");
    }

    /// <summary>**算子不适用于问题的编码时必须失败** / An operator unsuited to the problem's encoding must fail.</summary>
    [Fact]
    public void Bindings_ShouldRejectEncodingMismatch() =>
        Bindings(Algorithm(updates: new[] { "sbx" }), encoding: SearchEncoding.Permutation)
            .Should().BeOfType<Failed<
                Fuookami.Ospf.Core.Solver.Heuristic.Operator.OperatorBindings,
                ErrorCode, Error<ErrorCode>>>();

    /// <summary>**空列表表示"不要算子"，不是"给个默认"** / An empty list means "no operator", not "take a default".</summary>
    [Fact]
    public void Bindings_ShouldTreatEmptyListAsNoPool() {
        Result<Fuookami.Ospf.Core.Solver.Heuristic.Operator.OperatorBindings, ErrorCode,
            Error<ErrorCode>> built = Bindings(
            Algorithm(updates: Array.Empty<string>()), encoding: SearchEncoding.Continuous);

        ((Ok<Fuookami.Ospf.Core.Solver.Heuristic.Operator.OperatorBindings, ErrorCode,
            Error<ErrorCode>>)built).Value.Update.Should().BeNull();
    }

    /// <summary>**每个算法一个工作单元，且各自持有独立模型** / One working unit per algorithm, each holding its own model.</summary>
    [Fact]
    public void Assembly_ShouldBuildOneWorkerPerAlgorithm() {
        Result<IReadOnlyList<HyperHeuristicWorker<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>
            assembled = Assembly(new[] { "a", "b" });

        assembled.Should().BeOfType<Ok<IReadOnlyList<HyperHeuristicWorker<Flt64, Flt64>>,
            ErrorCode, Error<ErrorCode>>>();
        IReadOnlyList<HyperHeuristicWorker<Flt64, Flt64>> workers =
            ((Ok<IReadOnlyList<HyperHeuristicWorker<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>)
                assembled).Value;
        workers.Should().HaveCount(2);
        ReferenceEquals(workers[0].Problem.Model, workers[1].Problem.Model).Should().BeFalse(
            "共用模型会让两个算法互相改写求解状态 / a shared model lets two algorithms overwrite each other's state");
        workers[0].Random.State.Should().NotBe(workers[1].Random.State);
    }

    /// <summary>**共用模型的工厂必须被拒绝** / A factory sharing one model must be rejected.</summary>
    [Fact]
    public void Assembly_ShouldRejectSharedModel() {
        var registry = new SearchRegistry<Flt64, Flt64>(IIntoValue<Flt64>.Identity);
        var shared = new BoundedStubModel<object?>(1, -10.0, 10.0);
        registry.RegisterProblem("shared", true,
            (spec, random) => Results.Ok(ProblemFor(shared)));

        Assembly(registry, new[] { "a", "b" }, "shared").Should()
            .BeOfType<Failed<IReadOnlyList<HyperHeuristicWorker<Flt64, Flt64>>, ErrorCode,
                Error<ErrorCode>>>();
    }

    /// <summary>**工厂返回不兼容的问题定义必须被拒绝** / A factory returning an incompatible problem must be rejected.</summary>
    [Fact]
    public void Assembly_ShouldRejectIncompatibleProblem() {
        var registry = new SearchRegistry<Flt64, Flt64>(IIntoValue<Flt64>.Identity);
        int calls = 0;
        registry.RegisterProblem("drifting", true, (spec, random) => {
            calls++;
            return Results.Ok(Problem(fingerprint: calls == 1 ? "p" : "other"));
        });

        Assembly(registry, new[] { "a" }, "drifting").Should()
            .BeOfType<Failed<IReadOnlyList<HyperHeuristicWorker<Flt64, Flt64>>, ErrorCode,
                Error<ErrorCode>>>();
    }

    /// <summary>构造一组算子绑定 / Build one set of operator bindings.</summary>
    /// <param name="algorithm">算法配置 / The algorithm configuration.</param>
    /// <param name="encoding">问题编码 / The problem encoding.</param>
    /// <returns>算子绑定 / The operator bindings.</returns>
    private static Result<Fuookami.Ospf.Core.Solver.Heuristic.Operator.OperatorBindings,
        ErrorCode, Error<ErrorCode>> Bindings(AlgorithmConfig algorithm, SearchEncoding encoding) {
        var registry = new SearchRegistry<Flt64, Flt64>(IIntoValue<Flt64>.Identity);
        return HyperHeuristicBindings<Flt64, Flt64>.Build(
            registry, algorithm, Problem(encoding: encoding), new SearchRandom(1L), learning: true);
    }

    /// <summary>装配工作单元 / Assemble the working units.</summary>
    /// <param name="ids">算法标识 / The algorithm identifiers.</param>
    /// <returns>工作单元 / The working units.</returns>
    private static Result<IReadOnlyList<HyperHeuristicWorker<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>
        Assembly(string[] ids) {
        var registry = new SearchRegistry<Flt64, Flt64>(IIntoValue<Flt64>.Identity);
        registry.RegisterProblem("quadratic", true, (spec, random) => Results.Ok(Problem()));
        return Assembly(registry, ids, "quadratic");
    }

    /// <summary>用给定注册表装配 / Assemble with a given registry.</summary>
    /// <param name="registry">注册表 / The registry.</param>
    /// <param name="ids">算法标识 / The algorithm identifiers.</param>
    /// <param name="problemType">问题规格类型 / The problem spec type.</param>
    /// <returns>工作单元 / The working units.</returns>
    private static Result<IReadOnlyList<HyperHeuristicWorker<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>
        Assembly(SearchRegistry<Flt64, Flt64> registry, string[] ids, string problemType) {
        var algorithms = new List<AlgorithmConfig>(ids.Length);
        foreach (string id in ids) {
            algorithms.Add(new AlgorithmConfig(id, new ComponentSpec("pso", new JsonObject()), 20));
        }
        var config = new HyperHeuristicConfig(
            new ComponentSpec(problemType, new JsonObject()),
            algorithms,
            new BudgetConfig(Evaluations: 1000L));
        return HyperHeuristicAssembly<Flt64, Flt64>.Build(config, registry, new SearchRandom(7L));
    }

    /// <summary>构造算法配置 / Build an algorithm configuration.</summary>
    /// <param name="updates">更新算子 / The update operators.</param>
    /// <param name="mutations">变异算子 / The mutation operators.</param>
    /// <returns>算法配置 / The algorithm configuration.</returns>
    private static AlgorithmConfig Algorithm(string[]? updates = null, string[]? mutations = null) =>
        new(
            "a",
            new ComponentSpec("pso", new JsonObject()),
            20,
            updates is null ? null : Specs(updates),
            mutations is null ? null : Specs(mutations));

    /// <summary>把算子名转成规格列表 / Turn operator names into specs.</summary>
    /// <param name="types">类型名 / The type names.</param>
    /// <returns>规格列表 / The specs.</returns>
    private static IReadOnlyList<ComponentSpec> Specs(string[] types) {
        var specs = new List<ComponentSpec>(types.Length);
        foreach (string type in types) {
            specs.Add(new ComponentSpec(type, new JsonObject()));
        }
        return specs;
    }

    /// <summary>构造问题 / Build a problem.</summary>
    /// <param name="fingerprint">指纹 / The fingerprint.</param>
    /// <param name="encoding">编码 / The encoding.</param>
    /// <returns>问题 / The problem.</returns>
    private static SearchProblem<Flt64, Flt64> Problem(
        string fingerprint = "p", SearchEncoding encoding = SearchEncoding.Continuous) =>
        new(
            "p", fingerprint,
            new BoundedStubModel<object?>(1, -10.0, 10.0),
            SearchCodecs.Flt64,
            SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() < rhs.ToDouble(),
            validSolution: _ => true,
            encoding: encoding,
            objectiveCoordinates: value => new[] { value },
            admissible: _ => true);

    /// <summary>用给定模型构造问题 / Build a problem over a given model.</summary>
    /// <param name="model">模型 / The model.</param>
    /// <returns>问题 / The problem.</returns>
    private static SearchProblem<Flt64, Flt64> ProblemFor(BoundedStubModel<object?> model) =>
        new("p", "p", model,
            SearchCodecs.Flt64,
            SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() < rhs.ToDouble(),
            validSolution: _ => true,
            objectiveCoordinates: value => new[] { value },
            admissible: _ => true);
}
