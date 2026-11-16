// 搜索注册表测试 / Tests for the search registry.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 搜索注册表 / The search registry.
/// </summary>
public sealed class SearchRegistryTests {
    /// <summary>**未知的组件名必须失败，而不是退回默认算子** / An unknown component name must fail rather than fall back to a default.</summary>
    /// <remarks>
    /// 退回默认会让 `mutation: "gaussain"` 安静地变成一个和配置无关的算子，**而结果曲线只是"平滑一点"**。
    /// Falling back turns `mutation: "gaussain"` into an operator unrelated to the configuration, **while the curve merely looks "a bit smoother"**.
    /// </remarks>
    [Fact]
    public void Update_ShouldFailOnUnknownType() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        registry.Update(Spec("sbx"), SearchEncoding.Continuous).Should()
            .BeOfType<Ok<ISolutionUpdate, ErrorCode, Error<ErrorCode>>>();
        registry.Update(Spec("gaussain"), SearchEncoding.Continuous).Should()
            .BeOfType<Failed<ISolutionUpdate, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**编码不匹配必须失败** / An encoding mismatch must fail.</summary>
    /// <remarks>
    /// 回退会让一个专为排列设计的交叉算子被用在连续问题上，**而它照样产出一个解**——只是语义已经不对了。
    /// A fallback applies a permutation-only crossover to a continuous problem, **and it still produces a solution** whose meaning is wrong.
    /// </remarks>
    [Fact]
    public void Update_ShouldRejectEncodingMismatch() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        registry.Update(Spec("single-point"), SearchEncoding.Continuous).Should()
            .BeOfType<Ok<ISolutionUpdate, ErrorCode, Error<ErrorCode>>>();
        registry.Update(Spec("single-point"), SearchEncoding.Permutation).Should()
            .BeOfType<Failed<ISolutionUpdate, ErrorCode, Error<ErrorCode>>>();
        registry.Update(Spec("order"), SearchEncoding.Permutation).Should()
            .BeOfType<Ok<ISolutionUpdate, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**内建组件的参数同样受校验** / Built-in components validate their parameters too.</summary>
    [Fact]
    public void Components_ShouldValidateParameters() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        registry.Parameter(Spec("linear", ("limit", 0))).Should()
            .BeOfType<Failed<IParameterSource, ErrorCode, Error<ErrorCode>>>();
        registry.Parameter(Spec("linear", ("limit", 10))).Should()
            .BeOfType<Ok<IParameterSource, ErrorCode, Error<ErrorCode>>>();
        registry.Mutation(Spec("gaussian", ("sigma", -1.0)), SearchEncoding.Continuous).Should()
            .BeOfType<Failed<IMutationOperator, ErrorCode, Error<ErrorCode>>>();
        registry.Neighborhood(Spec("perturbation", ("amount", 2)), SearchEncoding.Continuous).Should()
            .BeOfType<Ok<INeighborhood, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**重复注册被拒绝** / A duplicate registration is rejected.</summary>
    /// <remarks>
    /// 允许覆盖会让"后注册的那个生效"变成一条隐式规则：**同名组件在不同位置注册两份时，行为取决于顺序**，
    /// 而配置里看不出这件事。
    /// Allowing overwrites turns "the later registration wins" into an implicit rule: **when one component is registered twice in different places,
    /// behaviour depends on order**, and the configuration cannot show it.
    /// </remarks>
    [Fact]
    public void Register_ShouldRejectDuplicates() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        registry.RegisterUpdate("single-point", true, AllEncodings(),
                spec => RegistryParameters.Empty(spec).Map(_ => (ISolutionUpdate)SinglePointCross.Instance))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        registry.RegisterUpdate("custom", true, AllEncodings(),
                spec => RegistryParameters.Empty(spec).Map(_ => (ISolutionUpdate)SinglePointCross.Instance))
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**可重建性只认两样东西：默认无状态集，和 `IRecoverableComponent`** / Recoverability honours exactly two things: the default stateless set and `IRecoverableComponent`.</summary>
    /// <remarks>
    /// **这里有一个容易误判的事实，必须记下来**：`Entry.Stateless`（注册时传的 `stateless`）**并不进入可重建集合**。
    /// `statelessComponents` 只有两类来源——初始化时硬编码的一份清单，以及 `registerFeatures/Reward/Acceptance/Exchange/Allocator(stateless = true)`
    /// 追加的条目。`registerUpdate(stateless = true)` **不追加**。
    ///
    /// 也就是说：**"注册时声明无状态"与"恢复后可以就地重建"在本实现里不是同一件事**。
    /// 本测试把这条差异钉住，避免后来者以为两者等价而把某个更新算子当成可重建的。
    ///
    /// **There is an easily misjudged fact here that must be recorded**: `Entry.Stateless` (the `stateless` flag passed at registration) **does not
 /// feed the recoverable set**. Its `statelessComponents` has exactly two sources — a hard-coded list at construction, plus entries
    /// appended by `registerFeatures/Reward/Acceptance/Exchange/Allocator(stateless = true)`. `registerUpdate(stateless = true)` **does not append**.
    ///
    /// In other words, **"declared stateless at registration" and "can be rebuilt in place after a restore" are not the same thing** here
 /// (nor in the implementation). This test pins that difference so a later reader does not assume equivalence and treat an update operator as
    /// recoverable.
    /// </remarks>
    [Fact]
    public void ComponentRecoverable_ShouldHonourTheStatelessSetOnly() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        // 默认清单里的条目：可重建。
        // An entry from the default list: recoverable.
        registry.ComponentRecoverable("reward", Spec("improvement"), new ImprovementRewardPolicy())
            .Should().BeTrue();

        // 声明了 stateless 的**更新算子**：仍不进入可重建集合。
        // An **update operator** declared stateless: still outside the recoverable set.
        ISolutionUpdate update = ((Ok<ISolutionUpdate, ErrorCode, Error<ErrorCode>>)
            registry.Update(Spec("single-point"), SearchEncoding.Continuous)).Value;
        registry.ComponentRecoverable("update", Spec("single-point"), update).Should().BeFalse(
            "registerUpdate 的 stateless 不进入可重建集合 / registerUpdate's stateless flag does not feed the recoverable set");

        // 显式以 stateless 注册的回报策略：进入集合。
        // A reward policy explicitly registered as stateless: joins the set.
        registry.RegisterReward("custom-reward", true, () => new ImprovementRewardPolicy());
        registry.ComponentRecoverable("reward", Spec("custom-reward"), new ImprovementRewardPolicy())
            .Should().BeTrue();
    }

    /// <summary>**没有状态的过滤器不算可重建** / A filter with no state is not recoverable.</summary>
    [Fact]
    public void RecoverableProblem_ShouldRequireStateOrStateless() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        registry.RecoverableProblem(
            Spec("stateless-problem"), Problem(state: false)).Should().BeFalse();
        registry.RegisterProblem("stateless-problem", true,
            (spec, random) => Results.Ok(Problem(state: false)));
        registry.RecoverableProblem(
            Spec("stateless-problem"), Problem(state: false)).Should().BeTrue(
            "已声明为无状态回调 / declared as stateless call-backs");
    }

    /// <summary>**周期交换的第一次不触发** / The first step of a periodic exchange does not fire.</summary>
    /// <remarks>
    /// 把判断写成"对周期取模为零"，会让**第一步就发生一次迁移**，而"迁移周期为 5"的配置看起来完全生效——只是第一次提前了。
    /// Writing the test as "divisible by the period" makes the **first step migrate**, while a "period of five" configuration looks in effect.
    /// </remarks>
    [Fact]
    public void PeriodicExchange_ShouldSkipDecisionZero() {
        SearchRegistry<Flt64, Flt64> registry = Registry();
        IExchangePolicy<Flt64, Flt64> exchange = ((Ok<IExchangePolicy<Flt64, Flt64>, ErrorCode,
            Error<ErrorCode>>)registry.Exchange(Spec("periodic", ("period", 2), ("amount", 1)))).Value;
        var working = new List<IIndividual<Flt64, Flt64>> {
            Individual(1.0), Individual(2.0),
        };

        exchange.Select(0, "island-0", working).Should().BeEmpty(
            "决策序号 0 不触发交换 / decision zero never fires");
        exchange.Select(1, "island-0", working).Should().BeEmpty();
        exchange.Select(2, "island-0", working).Should().HaveCount(1);
        exchange.Select(4, "island-0", working).Should().HaveCount(1);
    }

    /// <summary>**非法周期在构造期被拒绝** / An invalid period is rejected at construction.</summary>
    [Fact]
    public void Exchange_ShouldRejectInvalidPeriod() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        registry.Exchange(Spec("periodic", ("period", 0), ("amount", 1))).Should()
            .BeOfType<Failed<IExchangePolicy<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        registry.Exchange(Spec("nope")).Should()
            .BeOfType<Failed<IExchangePolicy<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**分配器与特征提取器同样对未知名失败** / Allocators and feature extractors fail on unknown names too.</summary>
    [Fact]
    public void Lookups_ShouldFailOnUnknownNames() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        registry.Allocator(Spec("fixed")).Should()
            .BeOfType<Ok<IBudgetAllocator, ErrorCode, Error<ErrorCode>>>();
        registry.Allocator(Spec("fixedy")).Should()
            .BeOfType<Failed<IBudgetAllocator, ErrorCode, Error<ErrorCode>>>();
        registry.Features(Spec("progress")).Should()
            .BeOfType<Ok<IFeatureExtractor, ErrorCode, Error<ErrorCode>>>();
        registry.Features(Spec("progress2")).Should()
            .BeOfType<Failed<IFeatureExtractor, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**未注册的选择器退化成表格策略，但参数仍然受校验，而且这只覆盖七个已知名字** / An unregistered selector degrades to the tabular policy with parameters validated, and that covers only the seven known names.</summary>
    /// <remarks>
    /// 这是唯一的"回退"。它与"未知组件退回默认算子"不同：表格策略本身就是"用通用表格近似任意选择器"，
    /// 退化后**仍在同一族行为里**。但**参数校验不能跟着一起退化**——否则 `epsilon: 2.0` 会被安静地接受。
    /// This is its only "fallback". It differs from "an unknown component falls back to a default operator": the tabular policy is
    /// itself "approximate any selector with a generic table", so the degenerate case **stays in the same family**. But **parameter validation must
    /// not degrade with it** — otherwise `epsilon: 2.0` is quietly accepted.
    /// </remarks>
    [Fact]
    public void Selection_ShouldFallBackToTabularWithValidation() {
        SearchRegistry<Flt64, Flt64> registry = Registry();
        string[] actions = new[] { "gwo", "sca" };

        registry.Selection(Spec("q-learning", ("epsilon", 0.2)), actions, true).Should()
            .BeOfType<Ok<ISelectionPolicy, ErrorCode, Error<ErrorCode>>>();
        registry.Selection(Spec("epsilon-greedy", ("epsilonn", 0.2)), actions, true).Should()
            .BeOfType<Failed<ISelectionPolicy, ErrorCode, Error<ErrorCode>>>();
        // **"回退"不是兜底**：表格策略自己只认那七个名字，其它名字一律失败。
 // **The "fallback" is not a catch-all**: the tabular policy only recognises those seven names; everything else fails (.
        registry.Selection(Spec("my-selector"), actions, true).Should()
            .BeOfType<Failed<ISelectionPolicy, ErrorCode, Error<ErrorCode>>>();
        registry.Selection(new ComponentSpec("q-learning", new JsonObject(), 2), actions, true).Should()
            .BeOfType<Failed<ISelectionPolicy, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**未知回报必须失败；无参数回报拒绝任何参数** / An unknown reward must fail, and a parameterless reward rejects any parameter.</summary>
    [Fact]
    public void Reward_ShouldFailOnUnknownAndOnStrayParameters() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        registry.Reward(Spec("improvement")).Should()
            .BeOfType<Ok<IRewardPolicy, ErrorCode, Error<ErrorCode>>>();
        registry.Reward(Spec("nope")).Should()
            .BeOfType<Failed<IRewardPolicy, ErrorCode, Error<ErrorCode>>>();
        registry.Reward(Spec("dominance")).Should()
            .BeOfType<Ok<IRewardPolicy, ErrorCode, Error<ErrorCode>>>();
        registry.Reward(Spec("dominance", ("window", 5))).Should()
            .BeOfType<Failed<IRewardPolicy, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**时间归一化的最小时长必须为正** / The time-normalised minimum duration must be positive.</summary>
    /// <remarks>
    /// 取 0 会让"每秒奖励"变成除零，**而回报表面上仍然是一个数**——只在极端情况下变成无穷大，
    /// 随后所有比较都变成"不更好"。
    /// Zero turns "reward per second" into a division by zero **while the reward still looks like a number**; it only becomes infinite in the
    /// extreme case, after which every comparison says "not better".
    /// </remarks>
    [Fact]
    public void Reward_ShouldRejectNonPositiveTimeNormalisation() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        registry.Reward(Spec("time-normalized", ("epsilonSeconds", 0.0))).Should()
            .BeOfType<Failed<IRewardPolicy, ErrorCode, Error<ErrorCode>>>();
        registry.Reward(Spec("time-normalized", ("epsilonSeconds", 0.001))).Should()
            .BeOfType<Ok<IRewardPolicy, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**超体积参考点的两种写法互斥，且必须是数组或成对出现** / The two reference spellings are exclusive and must be an array or a pair.</summary>
    /// <remarks>
    /// 两类错误都必须拒绝，否则"参考点缺失"会安静地变成一个空参考点，**超体积恒为 0，而回报曲线只是一直是 0**。
    /// Both mistakes must be rejected, otherwise a missing reference quietly becomes an empty one, **the hypervolume stays zero, and the reward
    /// curve merely reads zero throughout**.
    /// </remarks>
    [Fact]
    public void Reward_ShouldValidateHypervolumeReference() {
        SearchRegistry<Flt64, Flt64> registry = Registry();
        var reference = new JsonArray(1.0, 1.0);

        registry.Reward(Spec("hypervolume-delta", ("reference", reference))).Should()
            .BeOfType<Ok<IRewardPolicy, ErrorCode, Error<ErrorCode>>>();
        registry.Reward(Spec("hypervolume-delta")).Should()
            .BeOfType<Ok<IRewardPolicy, ErrorCode, Error<ErrorCode>>>();
        // **互斥用例必须两边都成对**：只给 `referenceX` 时，"分量必须成对"那条检查会先一步失败，
        // 于是"互斥"是否生效根本无从判断——这里第一版正是这么写的，变异因此存活。
        // **The exclusivity case must provide both components**: with only `referenceX` the "components must come in pairs" check fails first,
        // leaving exclusivity untested — exactly how the first version of this test was written, which is why the mutation survived.
        registry.Reward(Spec("hypervolume-delta",
                ("reference", reference), ("referenceX", 1.0), ("referenceY", 1.0))).Should()
            .BeOfType<Failed<IRewardPolicy, ErrorCode, Error<ErrorCode>>>(
                "两种写法互斥 / the two spellings are exclusive");
        registry.Reward(Spec("hypervolume-delta", ("referenceX", 1.0))).Should()
            .BeOfType<Failed<IRewardPolicy, ErrorCode, Error<ErrorCode>>>(
                "分量形式必须成对 / the component form must come in pairs");
        registry.Reward(Spec("hypervolume-delta", ("reference", 1.0))).Should()
            .BeOfType<Failed<IRewardPolicy, ErrorCode, Error<ErrorCode>>>(
                "参考点必须为数组 / the reference must be an array");
        registry.Reward(Spec("hypervolume-delta",
                ("referenceX", 1.0), ("referenceY", 2.0))).Should()
            .BeOfType<Ok<IRewardPolicy, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**超体积模式只接受已知取值** / The hypervolume mode accepts only known values.</summary>
    [Fact]
    public void Reward_ShouldValidateHypervolumeMode() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        registry.Reward(Spec("hypervolume-delta", ("mode", "sampled"))).Should()
            .BeOfType<Ok<IRewardPolicy, ErrorCode, Error<ErrorCode>>>();
        registry.Reward(Spec("hypervolume-delta", ("mode", "exact"))).Should()
            .BeOfType<Ok<IRewardPolicy, ErrorCode, Error<ErrorCode>>>();
        registry.Reward(Spec("hypervolume-delta", ("mode", "fast"))).Should()
            .BeOfType<Failed<IRewardPolicy, ErrorCode, Error<ErrorCode>>>();
        registry.Reward(Spec("hypervolume-delta", ("samples", 0))).Should()
            .BeOfType<Failed<IRewardPolicy, ErrorCode, Error<ErrorCode>>>(
                "采样数必须为正整数 / the sample count must be a positive integer");
    }

    /// <summary>**接受策略从坐标重建支配判据，缺坐标就失败** / Acceptance rebuilds dominance from coordinates and fails without them.</summary>
    /// <remarks>
    /// `Dominates` 接受 `ObjValue`，而接受策略持有的是坐标向量——**没有从向量回到 `ObjValue` 的通路**。
    /// 这里钉住的是"缺坐标时必须失败"，而不是"偷偷用一个恒假判据"：后者会让 `auto` 接受策略**永远只接受更好解**，
    /// 而配置里写的是 `auto`。
    ///
    /// `Dominates` accepts `ObjValue` while an acceptance policy holds coordinate vectors — **there is no path back from a vector to `ObjValue`**.
    /// What is pinned here is "must fail without coordinates", not "quietly use an always-false predicate": the latter would make `auto` **accept only
    /// improving solutions** while the configuration says `auto`.
    /// </remarks>
    [Fact]
    public void Acceptance_ShouldRequireCoordinates() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        registry.Acceptance(Spec("auto"), Problem(state: false, coordinates: true)).Should()
            .BeOfType<Ok<IMemoryAcceptance<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        registry.Acceptance(Spec("auto"), Problem(state: false, coordinates: false)).Should()
            .BeOfType<Failed<IMemoryAcceptance<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        registry.Acceptance(Spec("auto", ("length", 5)), Problem(state: false, coordinates: true))
            .Should().BeOfType<Failed<IMemoryAcceptance<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>(
                "auto 不接受任何参数 / auto takes no parameters");
    }

    /// <summary>**滞后接受只适用于单目标** / Late acceptance applies to scalar objectives only.</summary>
    [Fact]
    public void Acceptance_ShouldRejectLateForMultiObjective() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        registry.Acceptance(Spec("late", ("length", 10)),
                Problem(state: false, coordinates: true))
            .Should().BeOfType<Ok<IMemoryAcceptance<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        registry.Acceptance(Spec("late", ("length", 10)),
                Problem(state: false, coordinates: true, multiObjective: true))
            .Should().BeOfType<Failed<IMemoryAcceptance<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**记录接受要求比例落在 [0,1] 且坐标为标量** / Record acceptance requires a ratio in [0,1] and scalar coordinates.</summary>
    [Fact]
    public void Acceptance_ShouldValidateRecordRatio() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        registry.Acceptance(Spec("record-to-record", ("initialRatio", 0.05), ("horizon", 100)),
                Problem(state: false, coordinates: true))
            .Should().BeOfType<Ok<IMemoryAcceptance<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        registry.Acceptance(Spec("record-to-record", ("initialRatio", 1.5)),
                Problem(state: false, coordinates: true))
            .Should().BeOfType<Failed<IMemoryAcceptance<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        registry.Acceptance(Spec("record-to-record", ("initialRatio", -0.1)),
                Problem(state: false, coordinates: true))
            .Should().BeOfType<Failed<IMemoryAcceptance<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**`not-worse` 是默认接受策略，因此必须能构造出来** / `not-worse` is its default acceptance policy and must be constructible.</summary>
    /// <remarks>
 /// **这是对 earlier/226 一次判断的反转**。当时以"注册表不该替问题做主"为由把 `not-worse` 留空，而那条理由站不住：
    /// `Acceptance` 本来就接收 `SearchProblem`，支配判据与坐标投影就地可得。留空的后果是**默认配置根本跑不起来**
    /// （`HyperHeuristicConfig` 的 `acceptance` 默认值就是 `not-worse`）。
    ///
 /// **This reverses a decision from earlier/226.** `not-worse` was left out on the grounds that "the registry must not decide for the problem",
    /// and that reason does not hold: `Acceptance` already receives the `SearchProblem`, so the dominance predicate and the coordinate projection are
    /// right there. Leaving it out meant **its default configuration could not run at all** (`HyperHeuristicConfig`'s `acceptance` default
    /// is `not-worse`).
    /// </remarks>
    [Fact]
    public void Acceptance_ShouldBuildNotWorseWithCoordinates() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        registry.Acceptance(Spec("not-worse"), Problem(state: false, coordinates: true)).Should()
            .BeOfType<Ok<IMemoryAcceptance<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        registry.Acceptance(Spec("not-worse"), Problem(state: false, coordinates: false)).Should()
            .BeOfType<Failed<IMemoryAcceptance<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>(
                "缺坐标时仍然失败 / still fails without coordinates");
    }

    /// <summary>**`Create` 先校验参数，再装配；组合算法明确说"未实现"** / `Create` validates before assembling and reports composed algorithms as not implemented.</summary>
    [Fact]
    public void Create_ShouldValidateBeforeAssembling() {
        SearchRegistry<Flt64, Flt64> registry = Registry();
        SearchRandom random = new(7L);
        var bindings = new Fuookami.Ospf.Core.Solver.Heuristic.Operator.OperatorBindings(
            null, null, null,
            new Dictionary<string, Fuookami.Ospf.Core.Solver.Heuristic.Operator.OperatorPool<
                Fuookami.Ospf.Core.Solver.Heuristic.Operator.IParameterSource>>(
                System.StringComparer.Ordinal));

        registry.Create(new AlgorithmConfig("cfg", Spec("pso"), 20),
                Problem(state: false, coordinates: true), random, bindings)
            .Should().BeOfType<Ok<ISearchSession<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        registry.Create(new AlgorithmConfig("cfg", Spec("portfolio"), 20),
                Problem(state: false, coordinates: true), random, bindings)
            .Should().BeOfType<Failed<ISearchSession<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>(
                "组合装配未实现，必须明说 / composed assembly is not implemented and must say so");
        registry.Create(new AlgorithmConfig("cfg", Spec("clpso"), 1),
                Problem(state: false, coordinates: true), random, bindings)
            .Should().BeOfType<Failed<ISearchSession<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>(
                "参数先校验 / parameters are validated first");
    }

    /// <summary>测试用的坐标支配 / Coordinate dominance for tests.</summary>
    private sealed class CoordinateDominanceForTests :
        Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective.IDominance {
        /// <inheritdoc/>
        public bool Dominates(IReadOnlyList<Flt64> lhs, IReadOnlyList<Flt64> rhs) =>
            lhs.Count == rhs.Count && lhs.Count > 0
            && lhs[0].ToDouble() < rhs[0].ToDouble();
    }

    /// <summary>构造默认裁剪策略的注册表 / Build a registry with the default trimming policy.</summary>
    /// <returns>注册表 / The registry.</returns>
    private static SearchRegistry<Flt64, Flt64> Registry() =>
        new(IIntoValue<Flt64>.Identity);

    /// <summary>全部编码 / All encodings.</summary>
    /// <returns>编码集合 / The encoding set.</returns>
    private static IReadOnlySet<SearchEncoding> AllEncodings() =>
        new HashSet<SearchEncoding>(new[] {
            SearchEncoding.Continuous, SearchEncoding.Permutation, SearchEncoding.Custom,
        });

    /// <summary>构造组件规格 / Build a component spec.</summary>
    /// <param name="type">类型 / The type.</param>
    /// <param name="parameters">参数 / The parameters.</param>
    /// <returns>组件规格 / The component spec.</returns>
    private static ComponentSpec Spec(string type, params (string Key, object Value)[] parameters) {
        var node = new JsonObject();
        foreach ((string key, object value) in parameters) {
            node[key] = value switch {
                JsonNode json => json.DeepClone(),
                double number => JsonValue.Create(number),
                int integer => JsonValue.Create(integer),
                bool boolean => JsonValue.Create(boolean),
                string text => JsonValue.Create(text),
                _ => JsonValue.Create(value.ToString()),
            };
        }
        return new ComponentSpec(type, node);
    }

    /// <summary>构造一个个体 / Build one individual.</summary>
    /// <param name="objective">目标值 / The objective value.</param>
    /// <returns>个体 / The individual.</returns>
    private static IIndividual<Flt64, Flt64> Individual(double objective) =>
        new SolutionWithFitness<Flt64, Flt64>(
            new[] { new Flt64(objective) }, new Flt64(objective), objective);

    /// <summary>构造一个问题边界 / Build a problem boundary.</summary>
    /// <param name="state">是否带状态 / Whether it carries state.</param>
    /// <param name="coordinates">是否提供目标坐标 / Whether objective coordinates are supplied.</param>
    /// <param name="multiObjective">是否为多目标 / Whether it is multi-objective.</param>
    /// <returns>问题边界 / The problem boundary.</returns>
    private static SearchProblem<Flt64, Flt64> Problem(
        bool state, bool coordinates = false, bool multiObjective = false) =>
        new SearchProblem<Flt64, Flt64>(
            "p", "p", new BoundedStubModel<object?>(1, -10.0, 10.0),
            SearchCodecs.Flt64, SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() < rhs.ToDouble(),
            multiObjective: multiObjective,
            validSolution: _ => true,
            objectiveCoordinates: coordinates
                ? value => new[] { value }
                : null,
            admissible: _ => true,
            state: state ? SnapshotCodec() : null);

    /// <summary>构造一个最小状态句柄 / Build a minimal state handle.</summary>
    /// <returns>状态句柄 / The state handle.</returns>
    private static ISearchProblemState SnapshotCodec() => new JsonState();

    /// <summary>原样透传 JSON 的状态句柄 / A state handle that passes JSON through.</summary>
    private sealed class JsonState : ISearchProblemState {
        /// <inheritdoc/>
        public Result<JsonElement, ErrorCode, Error<ErrorCode>> Save() =>
            Results.Ok(JsonDocument.Parse("{}").RootElement.Clone());

        /// <inheritdoc/>
        public Result<Success, ErrorCode, Error<ErrorCode>> Load(JsonElement state) =>
            Results.Ok(new Success());
    }
}
