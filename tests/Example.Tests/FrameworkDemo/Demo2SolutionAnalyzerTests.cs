// 方案分析器的判据 / Criteria for the solution analyzer.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using AircraftPosition = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.Position;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using StowageSolution = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Solution;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **方案分析器（B610）** / The solution analyzer (B610).
/// </summary>
/// <remarks>
/// 分析器把求解器的**原始值向量**翻译成配载域方案。判据的重点是三个**边界**：阈值 `0.5`、正值才收录、token 缺失是失败。
///
/// The analyzer turns the solver's **raw value vector** into a stowage-domain solution. The criteria focus on three **boundaries**: the `0.5` threshold, positive-only inclusion,
/// and a missing token being a failure.
/// </remarks>
public class Demo2SolutionAnalyzerTests {
    private static Quantity<Flt64> Grams(double value) => new(new Flt64(value), MassUnits.Gram);

    private static Item MakeItem(string name) =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck);

    private static StowagePosition MakePosition(
        string name,
        ulong id,
        bool predicateWeightNeeded = false,
        bool recommendedWeightNeeded = false) {
        var position = new AircraftPosition(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: (byte)id,
            coordinate: new PositionCoordinate(400.0 + (double)id * 200.0, 600.0 + (double)id * 200.0, -40.0, -40.0),
            shape: new PositionShape(50.0, 40.0, 50.0 * 40.0 * 60.0),
            location: PositionLocation.NormalMainLocation);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(true, false, predicateWeightNeeded, recommendedWeightNeeded),
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: 3UL,
            plw: predicateWeightNeeded ? new PositionPredicateLoadWeight(0.0, 0.0, 1000.0) : null);
    }

    private sealed class Rig
    {
        public Rig(IReadOnlyList<Item> items, IReadOnlyList<StowagePosition> positions) {
            Items = items;
            Positions = positions;

            var allowed = new HashSet<AssignmentKey>();
            foreach (Item item in Items) {
                foreach (StowagePosition position in Positions) {
                    if (StowageModel.StowageNeeded(item, position)) {
                        allowed.Add(new AssignmentKey(item.Id, position.Name));
                    }
                }
            }
            var problem = new DiscreteLoadingProblem<Flt64>(
                Items: Items.Select(item => new LoadingItem<Flt64>(item.Id, Grams(item.Weight))).ToList(),
                Positions: Positions
                    .Select(position => new LoadingPosition<Flt64>(position.Name, position.Mla, Grams(position.Mlw.Mlw)))
                    .ToList(),
                AllowedAssignments: allowed,
                Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
                LayoutGroups: new List<LayoutSelectionGroup>(),
                WeightUnit: MassUnits.Gram);

            var aircraftModel = new AircraftModel(AircraftType.B737, "B737", new AircraftMinorModel("B737"));
            Aggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.StowageAggregation(
                aircraftModel,
                new Flight(new FlightNo("CA1"), new IATACode("PEK"), new IATACode("SHA")),
                Items,
                Positions,
                new Appointment(new Dictionary<Item, AircraftPosition>()),
                new BiologicalLimit(
                    new List<(CargoType, CargoType)>(),
                    new List<(CargoType, CargoType)>(),
                    new List<CargoType>()),
                new Dictionary<NeighbourType, IReadOnlyList<Neighbour>>(),
                plannedPayload: 500.0,
                maxPayload: 2000.0,
                withMultiLoadingSchema: true,
                formula: new Formula(
                    aircraftModel, lip: 100.0, chord: 20.0, standardDatum: 120.0, forceDistanceCoefficient: 5000.0, doiCorrection: 7.0),
                fuselage: new Fuselage(null, Dow: 40000.0, Doi: 10.0, BalancedArm: 900.0),
                fuel: new Dictionary<FlightPhase, FuelConstant> {
                    [FlightPhase.TakeOff] = new FuelConstant(Density: 0.8, Weight: 1000.0, Index: 1000.0),
                    [FlightPhase.Landing] = new FuelConstant(Density: 0.8, Weight: 2000.0, Index: 2000.0),
                });
            Aggregation.Stowage.LoadingContext = new DiscreteLoadingContext<Flt64>(problem, new Flt64PackingValueAdapter());
        }

        public IReadOnlyList<Item> Items { get; }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.StowageAggregation Aggregation { get; }

        public LinearMetaModel<Flt64> Register() {
            var model = new LinearMetaModel<Flt64>();
            Aggregation.Stowage.Register(model).IsOk.Should().BeTrue();
            Aggregation.Load.Register(model).IsOk.Should().BeTrue();
            return model;
        }
    }

    /// <summary>
    /// 把模型里的全部变量填成给定值 / Fill every token of the model with the given value.
    /// </summary>
    private static IReadOnlyList<Flt64> Fill(LinearMetaModel<Flt64> model, Flt64 value) =>
        Enumerable.Repeat(value, model.Tokens.TokenList.TokensInSolver.Count).ToList();

    /// <summary>
    /// 按变量名把解值填成指定值 / Fill the solution so that a named variable takes a given value.
    /// </summary>
    private static IReadOnlyList<Flt64> FillNamed(
        LinearMetaModel<Flt64> model,
        IReadOnlyDictionary<string, double> overrides,
        double fallback = 0.0) {
        var values = new List<Flt64>();
        foreach (Fuookami.Ospf.Core.Token.Token<Flt64> token in model.Tokens.TokenList.TokensInSolver) {
            double value = overrides.TryGetValue(token.Name, out double named) ? named : fallback;
            values.Add(new Flt64(value));
        }
        return values;
    }

    /// <summary>
    /// **阈值 0.5：0.999 算装、0.4 算没装** / The 0.5 threshold: 0.999 loads, 0.4 does not.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 值 `0.999` → 收进分配表。
    /// 2. 值 `0.4` → **不收**（用 `> 0` 会错误收录，用 `>= 1` 会错误丢弃）。
    /// 3. 值 `0.51` → 收（刚过界）。
    ///
    /// Three paired points: `0.999` is collected; `0.4` is **not** (`> 0` would wrongly collect it and `>= 1` would wrongly drop it); and `0.51` is collected, just past the line.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldUseTheHalfThreshold() {
        var rig = new Rig(new List<Item> { MakeItem("I1") }, new List<StowagePosition> { MakePosition("P1", 1UL) });
        using LinearMetaModel<Flt64> model = rig.Register();

        foreach ((double value, int expected) in new[] { (0.999, 1), (0.4, 0), (0.51, 1), (1.0, 1), (0.0, 0) }) {
            IReadOnlyList<Flt64> solution = Fill(model, new Flt64(value));
            Result<StowageSolution, ErrorCode, Error<ErrorCode>> result = new SolutionAnalyzer(rig.Aggregation).Invoke(solution, model);
            result.IsOk.Should().BeTrue();
            StowageSolution analysed = ((Ok<StowageSolution, ErrorCode, Error<ErrorCode>>)result).Value;
            analysed.Assignments.Should().HaveCount(expected, "a stowed value of {0} is past the 0.5 threshold or not", value);
        }
    }

    /// <summary>
    /// **重量只收录正值，且单位来自变量** / Weights are positive-only, with the unit taken from the variable.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. `y_0` 与 `z_0` 都为正 → 两张表各有一条。
    /// 2. `y_0 = 0` → **谓词表里没有该舱位**（零意味着不使用这个模式）。
    /// 3. 舱位标志为假时**不去找 token**——所以只注册了 `y`、没注册 `z` 的模型也能分析成功。
    /// 4. 单位是**变量自带的**（`AircraftModel.WeightUnit`，千克），不是 `mlw` 的。
    ///
    /// Four paired points: positive `y_0` and `z_0` each yield one entry; `y_0 = 0` leaves the position **out of** the predicate table; a false flag means **no token lookup**, so a
    /// model registering `y` but not `z` still analyses successfully; and the unit is the **variable's** (`AircraftModel.WeightUnit`, kilograms), not `mlw`'s.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldCollectPositiveWeightsOnly() {
        var rig = new Rig(
            new List<Item> { MakeItem("I1") },
            new List<StowagePosition> {
                MakePosition("P1", 1UL, predicateWeightNeeded: true, recommendedWeightNeeded: true),
                MakePosition("P2", 2UL),
            });
        using LinearMetaModel<Flt64> model = rig.Register();

        IReadOnlyList<Flt64> solution = FillNamed(model, new Dictionary<string, double> {
            ["y_P1"] = 300.0,
            ["z_P1"] = 7.0,
        });
        Result<StowageSolution, ErrorCode, Error<ErrorCode>> result = new SolutionAnalyzer(rig.Aggregation).Invoke(solution, model);
        result.IsOk.Should().BeTrue();
        StowageSolution analysed = ((Ok<StowageSolution, ErrorCode, Error<ErrorCode>>)result).Value;

        analysed.PredicateLoadWeight.Should().ContainKey(rig.Positions[0]);
        analysed.PredicateLoadWeight[rig.Positions[0]].Value.ToDouble().Should().BeApproximately(300.0, 1e-9);
        analysed.PredicateLoadWeight[rig.Positions[0]].Unit.Should().Be(AircraftModel.WeightUnit);
        analysed.RecommendedLoadWeight.Should().ContainKey(rig.Positions[0]);
        analysed.RecommendedLoadWeight[rig.Positions[0]].Value.ToDouble().Should().BeApproximately(7.0, 1e-9);

        // 零值不收录。
        // A zero value is not collected.
        IReadOnlyList<Flt64> zeros = FillNamed(model, new Dictionary<string, double> { ["y_P1"] = 0.0, ["z_P1"] = 0.0 });
        StowageSolution zeroed = ((Ok<StowageSolution, ErrorCode, Error<ErrorCode>>)
            new SolutionAnalyzer(rig.Aggregation).Invoke(zeros, model)).Value;
        zeroed.PredicateLoadWeight.Should().BeEmpty("a zero value means the position does not use this mode");
        zeroed.RecommendedLoadWeight.Should().BeEmpty();
    }

    /// <summary>
    /// **token 缺失与值缺失都是失败，不是 0** / A missing token or value is a failure, not a zero.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. 解值向量**太短** → 失败，且消息含"缺少"。
    /// 2. 正常长度的解值 → 成功。折成 0 会造出"什么都没装"的方案，而它在下游可能看起来可行——所以必须是失败。
    ///
    /// Two paired points: a **too-short** value vector fails with a "missing" message; a correctly sized vector succeeds. Folding to zero would fabricate a "loads nothing" solution
    /// that may look feasible downstream, so it must be a failure.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailOnAShortSolutionVector() {
        var rig = new Rig(
            new List<Item> { MakeItem("I1") },
            new List<StowagePosition> { MakePosition("P1", 1UL, predicateWeightNeeded: true) });
        using LinearMetaModel<Flt64> model = rig.Register();

        Result<StowageSolution, ErrorCode, Error<ErrorCode>> short_ =
            new SolutionAnalyzer(rig.Aggregation).Invoke(new List<Flt64>(), model);
        short_.IsOk.Should().BeFalse("an empty vector cannot carry any variable value");
        (short_ as Failed<StowageSolution, ErrorCode, Error<ErrorCode>>)!.Error.Message.Should().Contain("缺少");

        Result<StowageSolution, ErrorCode, Error<ErrorCode>> ok =
            new SolutionAnalyzer(rig.Aggregation).Invoke(Fill(model, new Flt64(1.0)), model);
        ok.IsOk.Should().BeTrue();
    }

    /// <summary>
    /// **token 不在模型里时是失败** / A token absent from the model is a failure.
    /// </summary>
    /// <remarks>
    /// 构造一个**聚合的货物表与装载上下文的分配表不一致**的夹具：分配表里有 `I2->P1`，而聚合的 `Items` 里只有 `I1`。
    /// 于是分配循环找不到 `I2`，**必须是失败**——折成"跳过"会得到"什么都没装"的方案，而它在下游看起来完全可行。
    ///
    /// 这条钉住的是 `IndexOf` 返回 `null` 之前的那道守卫：**未知名字即失败**。真正的"token 缺失"分支（变量注册了但不在
    /// token 表里）在本仓构造不出来——`Tokens` 是只读抽象，注册即入表。
    ///
    /// Build a fixture where **the aggregation's item list disagrees with the loading context's assignment table**: the table has `I2->P1` while the aggregation's `Items` holds
    /// only `I1`. The assignment loop then cannot find `I2` and **must fail** — skipping would produce a "loads nothing" solution that looks perfectly feasible downstream. This
    /// pins the guard just before `IndexOf` returns `null`: **an unknown name is a failure**. The genuine "token missing" branch (a variable registered but absent from the token
    /// table) cannot be constructed here, since `Tokens` is a read-only abstraction and registration puts it in the table.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailOnAnUnknownAssignmentName() {
        var rig = new Rig(new List<Item> { MakeItem("I1") }, new List<StowagePosition> { MakePosition("P1", 1UL) });
        var assignments = new HashSet<AssignmentKey> {
            new("I1", "P1"),
            new("I2", "P1"),
        };
        var problem = new DiscreteLoadingProblem<Flt64>(
            Items: new List<LoadingItem<Flt64>> {
                new("I1", Grams(100.0)),
                new("I2", Grams(100.0)),
            },
            Positions: new List<LoadingPosition<Flt64>> { new("P1", 3UL, Grams(1000.0)) },
            AllowedAssignments: assignments,
            Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
            LayoutGroups: new List<LayoutSelectionGroup>(),
            WeightUnit: MassUnits.Gram);
        rig.Aggregation.Stowage.LoadingContext = new DiscreteLoadingContext<Flt64>(problem, new Flt64PackingValueAdapter());
        using LinearMetaModel<Flt64> model = rig.Register();

        Result<StowageSolution, ErrorCode, Error<ErrorCode>> result =
            new SolutionAnalyzer(rig.Aggregation).Invoke(Fill(model, new Flt64(1.0)), model);
        result.IsOk.Should().BeFalse("the assignment table names I2, which is not in the aggregation's items");
        // **消息必须点名"未知"**：只断言"失败"会被 `Stowage.Register` 的"符号未注册"抢先满足，那条路径也在同一批
        // 变量上失败，但原因完全不同——一个说"数据里有聚合不认识的货物"，一个说"模型没注册这个符号"。
        // **The message must say "unknown"**: asserting only "fails" is satisfied by `Stowage.Register`'s "symbol not registered", which also fails on the same variables for an
        // entirely different reason — one says "the data names an item the aggregation does not know", the other "the model has not registered this symbol".
        (result as Failed<StowageSolution, ErrorCode, Error<ErrorCode>>)!.Error.Message.Should().Contain("未知");
    }

    /// <summary>
    /// **没有装载上下文时分配表为空，不抛异常** / Without a loading context the assignment table is empty and nothing throws.
    /// </summary>
    /// <remarks>
    /// 聚合可能在没有装载上下文的情况下被构造（例如只做重量分析）。分析器此时**返回空分配表**而不是抛 `NullReferenceException`——
    /// 与 `Stowage.Register` 的守卫一致：缺上下文是"没有可分析的分配"，不是崩溃。
    ///
    /// An aggregation may exist without a loading context (weight analysis only, say). The analyzer then returns an **empty assignment table** rather than throwing, matching
    /// `Stowage.Register`'s guard: a missing context means "no assignments to analyse", not a crash.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldTolerateAMissingLoadingContext() {
        var rig = new Rig(new List<Item> { MakeItem("I1") }, new List<StowagePosition> { MakePosition("P1", 1UL) });
        rig.Aggregation.Stowage.LoadingContext = null;
        // **不调 `Stowage.Register`**：它自己就要求装载上下文，会先失败。这里只注册 `Load`，模拟"没有装载上下文但有权重变量"的模型。
        // **`Stowage.Register` is skipped**: it requires a loading context and would fail first. Only `Load` is registered, modelling a model with weight variables but no context.
        using var model = new LinearMetaModel<Flt64>();
        rig.Aggregation.Load.Register(model).IsOk.Should().BeTrue();

        Result<StowageSolution, ErrorCode, Error<ErrorCode>> result =
            new SolutionAnalyzer(rig.Aggregation).Invoke(Fill(model, new Flt64(1.0)), model);
        result.IsOk.Should().BeTrue();
        ((Ok<StowageSolution, ErrorCode, Error<ErrorCode>>)result).Value.Assignments.Should().BeEmpty();
    }
}
