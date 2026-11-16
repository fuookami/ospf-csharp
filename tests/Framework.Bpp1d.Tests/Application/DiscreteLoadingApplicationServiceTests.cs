// 离散装载应用服务判据 / Criteria for the discrete-loading application service.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Application;
using Fuookami.Ospf.Framework.Bpp.Domain.PartitionLoading;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Application;

/// <summary>
/// **离散装载应用服务** / The discrete-loading application service.
/// </summary>
/// <remarks>
/// 对应的 MILP 路径。
///
/// **判据全都不需要真实求解器**：`milpSolver` 是一个**函数参数**，所以判据可以直接注入
/// "求解器报了这个目标"，再检查应用层有没有**按完整模型重算一遍**把它抓住。
/// 这比用真求解器更有力——真求解器不会配合你造一个"目标错位"的解。
///
/// Mirrors the MILP path of. **No criterion needs a real solver**: `milpSolver` is a **function parameter**, so a criterion can
/// inject "the solver reported this objective" and check whether the application layer **recomputes it from the full model** and catches the mismatch — stronger than
/// a real solver, which will not cooperate by producing a misaligned objective.
/// </remarks>
public class DiscreteLoadingApplicationServiceTests {
    /// <summary>**目标一致时正常收口** / A consistent objective closes normally.</summary>
    [Fact]
    public async Task SolveAsync_ShouldAcceptAConsistentObjective() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new DiscreteLoadingApplicationService<Flt64>(StubMilp(objective: 1.0))
                .SolveAsync(() => Register());

        result.IsOk.Should().BeTrue(result.ToString());
        DiscreteLoadingSolveResult<Flt64> solved =
            ((Ok<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        solved.Path.Should().Be("milp_direct");
        solved.Objective.ToDouble().Should().BeApproximately(1.0, 1e-12);
    }

    /// <summary>**目标错位必须被抓住** / An objective mismatch must be caught.</summary>
    /// <remarks>
    /// **这里最要紧的一条。** 求解器说目标 `2.0`，而按完整模型重算只有 `1.0`。
    /// 少了这一步复核，**模型与求解器错位**的错误会被当成正常结果返回——那类错误在业务侧最难发现，
    /// 因为结果"看起来合理"。
    ///
    /// **The most important criterion in.** The solver reports `2.0` while a recomputation from the full model gives `1.0`. Without this re-check a
    /// **model/solver mismatch** returns as a normal answer — the hardest class to spot in business code, because the result "looks reasonable".
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldRejectAnInconsistentObjective() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new DiscreteLoadingApplicationService<Flt64>(StubMilp(objective: 2.0))
                .SolveAsync(() => Register());

        result.IsFailed.Should().BeTrue("the solver's objective disagrees with the full model");
        ((Failed<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.ApplicationError);
    }

    /// <summary>**相对容差内的浮点误差仍然通过** / Floating noise inside the relative tolerance still passes.</summary>
    /// <remarks>
    /// 反向对照：容差写成 0 时这条会红。求解器返回 `1.0000000001` 必须算作与完整模型的 `1.0` 一致。
    ///
    /// Reverse control: a zero tolerance turns this red. A solver's `1.0000000001` must count as agreeing with the full model's `1.0`.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldTolerateFloatingNoiseInTheObjective() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new DiscreteLoadingApplicationService<Flt64>(StubMilp(objective: 1.0000000001))
                .SolveAsync(() => Register());

        result.IsOk.Should().BeTrue("1e-10 is floating noise: " + result.ToString());
    }

    /// <summary>**装配失败原样透出** / A registration failure passes through untouched.</summary>
    [Fact]
    public async Task SolveAsync_ShouldPropagateARegistrationFailure() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new DiscreteLoadingApplicationService<Flt64>(StubMilp(objective: 1.0))
                .SolveAsync(() => Results.Failed<RegisteredDiscreteLoading<Flt64>>(
                    new Err<ErrorCode>(ErrorCode.DataNotFound, "no registration")));

        result.IsFailed.Should().BeTrue();
        ((Failed<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.DataNotFound, "the factory's own code must survive");
    }

    /// <summary>**求解器失败原样透出** / A solver failure passes through untouched.</summary>
    [Fact]
    public async Task SolveAsync_ShouldPropagateASolverFailure() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new DiscreteLoadingApplicationService<Flt64>(
                _ => Task.FromResult(Results.Failed<FeasibleSolverOutput<Flt64>>(
                    new Err<ErrorCode>(ErrorCode.OREngineSolvingException, "solver failed"))))
                .SolveAsync(() => Register());

        result.IsFailed.Should().BeTrue();
        ((Failed<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.OREngineSolvingException, "the solver's own code must survive");
    }

    /// <summary>**大目标下相对容差才够用** / A large objective needs the relative tolerance.</summary>
    /// <remarks>
    /// 目标 `1e9`，求解器报的比完整模型多 `5e-4`。**相对容差 `1e-6` 下容许 `1e3`，通过；纯绝对容差 `1e-6` 会判错。**
    /// 的噪音判据目标是 `1.0`，两种容差都够用，**区分不出**——这一条专门造出区分力。
    ///
    /// Objective `1e9` where the solver reports `5e-4` more than the full model. **Under the relative tolerance this allows `1e3`, so it passes; a pure absolute
    /// tolerance of `1e-6` would flag it.** the earlier noise criterion used an objective of `1.0`, where both tolerances suffice and **nothing is distinguished**.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldUseTheRelativeToleranceForLargeObjectives() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new DiscreteLoadingApplicationService<Flt64>(StubMilp(objective: 1e9 + 5e-4))
                .SolveAsync(() => Register(coefficient: 1e9));

        result.IsOk.Should().BeTrue("5e-4 on an objective of 1e9 is inside the relative tolerance: " + result.ToString());
    }

    /// <summary>**容量关系被破坏时必须由完整模型复核拒掉** / A broken capacity relation must be rejected by the full-model verification.</summary>
    /// <remarks>
    /// 分配置 1、占用置 0——**这个解能通过取解层的一切检查**（分配是二元的、货物只被选一次），
    /// 只有完整模型的容量关系能发现它。**这是"跳过完整复核"那条变异唯一的杀手。**
    ///
    /// 第一版用 `1e9` 打二元范围，**存活**：取解层的二元性检查也会拒掉它，**两层都能抓住，判据就分不出**。
    ///
    /// The assignment is 1 while the occupancy is 0 — **a solution that passes every check at the extraction layer** (the assignment is binary and the item is
    /// selected once), and only the full model's capacity relation can catch it. **This is the only killer for the "skip the full verification" mutant.** The first
    /// version used `1e9` against the binary range and **survived**, because the extraction layer's own binary check refuses it too — **two layers catch it, so the
    /// criterion cannot tell them apart**.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldRejectABrokenCapacityRelation() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new DiscreteLoadingApplicationService<Flt64>(
                model => {
                    model.Tokens.Find(Assignment)!.SetResult(new Flt64(1.0));
                    model.Tokens.Find(Occupancy)!.SetResult(new Flt64(0.0));
                    var values = new List<Flt64>();
                    foreach (Fuookami.Ospf.Core.Token.Token<Flt64> token in model.Tokens.TokensInSolver) {
                        values.Add(token.DoubleResult is { } value ? new Flt64(value) : Flt64.Zero);
                    }
                    return Task.FromResult(Results.Ok(new FeasibleSolverOutput<Flt64>(
                        Obj: new Flt64(1.0),
                        Solution: new Solution<Flt64>(values),
                        Time: global::System.TimeSpan.Zero,
                        PossibleBestObj: new Flt64(1.0),
                        Gap: new Flt64(0.0))));
                })
                .SolveAsync(() => Register());

        result.IsFailed.Should().BeTrue("an assignment without its occupancy breaks the capacity relation");
    }

    /// <summary>**值不足时必须报缺值** / A short value list must be reported as missing values.</summary>
    /// <remarks>
    /// 桩只返回**一个**值而模型有多个 token。放过会让后面的变量**静默拿到 0**——一个"没装东西"的解。
    ///
    /// The stub returns **one** value while the model has several tokens. Letting it through would leave later variables **silently at zero** — a solution that
    /// "loads nothing".
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldRejectAShortValueList() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new DiscreteLoadingApplicationService<Flt64>(ShortStub())
                .SolveAsync(() => Register());

        result.IsFailed.Should().BeTrue("the value list cannot cover every model token");
        ((Failed<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.DataNotFound);
    }

    /// <summary>**回退不得复用 Benders 改过的模型** / The fallback must not reuse the model Benders touched.</summary>
    /// <remarks>
    /// Benders 会改写它拿到的那张模型；**回退再拿同一张，等于"回退"仍然在改过的模型上跑**，而结果看上去正常。
    /// 判据直接打纯判断 `RequiresFreshModel`：**同实例 ⇒ 拒绝，不同实例或从未跑过 Benders ⇒ 放行。**
    ///
    /// Benders mutates the model it receives; **reusing it for the fallback means the "fallback" still runs on the mutated model**, while the result looks normal.
    /// The criterion hits the pure `RequiresFreshModel`: **the same instance is refused, a different one or no Benders at all is allowed.**
    /// </remarks>
    [Fact]
    public void RequiresFreshModel_ShouldRejectTheSameInstance() {
        LinearMetaModel<Flt64> rejected = new();
        LinearMetaModel<Flt64> fresh = new();

        DiscreteLoadingApplicationService<Flt64>.RequiresFreshModel(rejected, rejected).Should().BeTrue();
        DiscreteLoadingApplicationService<Flt64>.RequiresFreshModel(rejected, fresh).Should().BeFalse();
        DiscreteLoadingApplicationService<Flt64>.RequiresFreshModel(null, fresh).Should().BeFalse(
            "when Benders never ran there is nothing to reject");
    }

    /// <summary>**Benders 合并结果缺值时必须报缺值，不能填 0** / A missing merged value must fail, never be filled with zero.</summary>
    /// <remarks>
    /// Benders 只合并它认识的变量。把不认识的位置填 0，会让一个**"没装东西"的解看起来可行**。
    ///
    /// Benders merges only the variables it knows; filling the rest with zero would make a **"loads nothing" solution look feasible**.
    /// </remarks>
    [Fact]
    public void MergeBendersValues_ShouldFailOnAMissingValue() {
        LinearMetaModel<Flt64> model = new();
        var variable = new Fuookami.Ospf.Core.Variable.BinVar("missing");
        model.Add(variable).IsOk.Should().BeTrue();

        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> result =
            DiscreteLoadingApplicationService<Flt64>.MergeBendersValues(
                model,
                new Dictionary<Fuookami.Ospf.Core.Variable.IVariableItem, Flt64>());

        result.IsFailed.Should().BeTrue("the model has a token the merged solution does not cover");
        ((Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.DataNotFound);
    }

    /// <summary>**局部取值必须按局部模型的 token 定位** / Local values must be located through the local model's tokens.</summary>
    /// <remarks>
    /// 局部模型的 token 顺序和完整模型不同。**用完整模型的顺序去索引会静默把值配给错误的决策**，
    /// 而错配后的检验数看起来仍然正常。
    ///
    /// The local model's token order differs from the full model's. **Indexing by the full-model order silently pairs values with the wrong decisions**, and the
    /// resulting reduced costs still look normal.
    /// </remarks>
    [Fact]
    public async Task CollectValues_ShouldMapValuesThroughTheLocalModelTokens() {
        (PartitionLoadingContext<Flt64> context, LinearMetaModel<Flt64> model) = await MakePricingFixture();
        var request = new Fuookami.Ospf.Framework.Solver.ColumnPricingRequest<Flt64>(
            blockId: "A",
            coefficients: new Dictionary<string, Flt64>(),
            convexityPrice: Flt64.Zero,
            rowPrices: new Dictionary<string, Flt64>(),
            revision: new Fuookami.Ospf.Framework.Solver.ColumnProjectionRevision(default, 0L, 0L));

        var values = new Flt64[model.Tokens.TokensInSolver.Count];
        for (int index = 0; index < values.Length; ++index) {
            values[index] = new Flt64(index + 1.0);
        }
        var solved = new FeasibleSolverOutput<Flt64>(
            Obj: Flt64.Zero,
            Solution: new Solution<Flt64>(new List<Flt64>(values)),
            Time: global::System.TimeSpan.Zero,
            PossibleBestObj: Flt64.Zero,
            Gap: new Flt64(0.0));

        Result<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>> collected =
            MilpPartitionPricingOracle<Flt64>.CollectValues(context, request, model, solved);

        collected.IsOk.Should().BeTrue(collected.ToString());
        IReadOnlyDictionary<string, Flt64> bag =
            ((Ok<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>>)collected).Value;
        bag.Should().NotBeEmpty("partition A owns its assignment and occupancy decisions");
        foreach (KeyValuePair<string, Flt64> entry in bag) {
            entry.Value.ToDouble().Should().BeGreaterThan(0.0, "each value is 1-based, so a zero would mean a wrong index");
        }
    }

    /// <summary>**局部 token 缺失必须报缺值** / A missing local token must be a failure.</summary>
    [Fact]
    public async Task CollectValues_ShouldFailOnAMissingLocalToken() {
        (PartitionLoadingContext<Flt64> context, LinearMetaModel<Flt64> _) = await MakePricingFixture();
        var request = new Fuookami.Ospf.Framework.Solver.ColumnPricingRequest<Flt64>(
            blockId: "A",
            coefficients: new Dictionary<string, Flt64>(),
            convexityPrice: Flt64.Zero,
            rowPrices: new Dictionary<string, Flt64>(),
            revision: new Fuookami.Ospf.Framework.Solver.ColumnProjectionRevision(default, 0L, 0L));

        // 空模型里没有任何局部变量：每个决策都查不到 token。
        // An empty model holds no local variable: every decision misses its token.
        var emptyModel = new LinearMetaModel<Flt64>();
        var solved = new FeasibleSolverOutput<Flt64>(
            Obj: Flt64.Zero,
            Solution: new Solution<Flt64>(new List<Flt64>()),
            Time: global::System.TimeSpan.Zero,
            PossibleBestObj: Flt64.Zero,
            Gap: new Flt64(0.0));

        Result<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>> collected =
            MilpPartitionPricingOracle<Flt64>.CollectValues(context, request, emptyModel, solved);

        collected.IsFailed.Should().BeTrue("the local model does not hold partition A's variables");
        ((Failed<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>>)collected).Error.Code
            .Should().Be(ErrorCode.DataNotFound);
    }

    /// <summary>构造一份分区装载夹具 / Build a partition-loading fixture.</summary>
    /// <returns>上下文与模型 / The context and the model.</returns>
    private static async Task<(PartitionLoadingContext<Flt64> Context, LinearMetaModel<Flt64> Model)>
        MakePricingFixture() {
        DiscreteLoadingProblem<Flt64> problem = new(
            Items: new List<LoadingItem<Flt64>> {
                new("I1", Kilograms(1.0), Required: true, FixedPositionId: null),
            },
            Positions: new List<LoadingPosition<Flt64>> {
                new("P1", MaxCount: 5UL, MaxWeight: Kilograms(5.0), FixedOccupied: false, Enabled: true),
                new("P2", MaxCount: 5UL, MaxWeight: Kilograms(5.0), FixedOccupied: false, Enabled: true),
            },
            AllowedAssignments: new HashSet<AssignmentKey> { new("I1", "P1"), new("I1", "P2") },
            Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
            LayoutGroups: new List<LayoutSelectionGroup>(),
            WeightUnit: SIBaseUnits.Kilogram);

        var loading = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        loading.Register(model).IsOk.Should().BeTrue();

        Result<PartitionLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>> created =
            await PartitionLoadingContext<Flt64>.CreateAsync(
                loading,
                model,
                new List<LoadingPartition> {
                    new("A", new HashSet<string> { "P1" }),
                    new("B", new HashSet<string> { "P2" }),
                });
        created.IsOk.Should().BeTrue(created.ToString());
        return (((Ok<PartitionLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>>)created).Value, model);
    }

    /// <summary>构造桩求解器，把全部 token 置为可行解并报出指定目标 / Build a stub solver that fills a feasible solution and reports the given objective.</summary>
    /// <param name="objective">要报出的目标 / The objective to report.</param>
    /// <returns>桩函数 / The stub.</returns>
    private static global::System.Func<LinearMetaModel<Flt64>, Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>>
        StubMilp(double objective) =>
        model => {
            model.Tokens.Find(Assignment)!.SetResult(new Flt64(1.0));
            model.Tokens.Find(Occupancy)!.SetResult(new Flt64(1.0));

            // **按 `TokensInSolver` 的顺序读值**，与服务实现用同一个顺序——这样判据测的是目标复核，而不是索引错位。
            // **Values are read in `TokensInSolver` order**, the same order the service uses, so the criterion tests the objective re-check rather than index pairing.
            var values = new List<Flt64>();
            foreach (Fuookami.Ospf.Core.Token.Token<Flt64> token in model.Tokens.TokensInSolver) {
                values.Add(token.DoubleResult is { } value ? new Flt64(value) : Flt64.Zero);
            }
            return Task.FromResult(Results.Ok(new FeasibleSolverOutput<Flt64>(
                Obj: new Flt64(objective),
                Solution: new Solution<Flt64>(values),
                Time: global::System.TimeSpan.Zero,
                PossibleBestObj: new Flt64(objective),
                Gap: new Flt64(0.0))));
        };

    /// <summary>只返回一个值的桩 / A stub that returns a single value.</summary>
    /// <returns>桩函数 / The stub.</returns>
    private static global::System.Func<LinearMetaModel<Flt64>, Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>>
        ShortStub() =>
        model => Task.FromResult(Results.Ok(new FeasibleSolverOutput<Flt64>(
            Obj: new Flt64(1.0),
            Solution: new Solution<Flt64>(new List<Flt64> { new(1.0) }),
            Time: global::System.TimeSpan.Zero,
            PossibleBestObj: new Flt64(1.0),
            Gap: new Flt64(0.0))));

    /// <summary>当前测试里唯一的分配变量 / The single assignment variable used by the fixture.</summary>
    private static Fuookami.Ospf.Core.Variable.IVariableItem Assignment = null!;

    /// <summary>当前测试里唯一的占用变量 / The single occupancy variable used by the fixture.</summary>
    private static Fuookami.Ospf.Core.Variable.IVariableItem Occupancy = null!;

    /// <summary>构造装配结果 / Build a registration.</summary>
    /// <param name="coefficient">目标里每个分配的系数 / The objective coefficient per assignment.</param>
    /// <returns>装配结果 / The registration.</returns>
    private static Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> Register(
        double coefficient = 1.0) {
        var problem = new DiscreteLoadingProblem<Flt64>(
            Items: new List<LoadingItem<Flt64>> {
                new("I1", Kilograms(1.0), Required: true, FixedPositionId: null),
            },
            Positions: new List<LoadingPosition<Flt64>> {
                new("P1", MaxCount: 5UL, MaxWeight: Kilograms(5.0), FixedOccupied: false, Enabled: true),
            },
            AllowedAssignments: new HashSet<AssignmentKey> { new("I1", "P1") },
            Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
            LayoutGroups: new List<LayoutSelectionGroup>(),
            WeightUnit: SIBaseUnits.Kilogram);

        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        Try registered = context.Register(model);
        if (registered is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Results.Failed<RegisteredDiscreteLoading<Flt64>>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, "fixture registration failed"));
        }

        Assignment = context.Aggregation.Assignments[new AssignmentKey("I1", "P1")];
        Occupancy = context.Aggregation.Occupied["P1"];

        // **目标就是"一个分配算 1"**：这样完整模型重算出来的值恰好等于分配变量的值，判据可以直接对上。
        // **The objective charges 1 per assignment**, so a recomputation from the full model equals the assignment variable's value.
        model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(
                new List<LinearMonomial<Flt64>> { new(new Flt64(coefficient), Assignment) },
                Flt64.Zero),
            name: "loading",
            displayName: null).IsOk.Should().BeTrue();

        return Results.Ok(new RegisteredDiscreteLoading<Flt64>(context, model));
    }

    /// <summary>构造千克量 / Build a kilogram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) =>
        new(new Flt64(value), SIBaseUnits.Kilogram);
}
