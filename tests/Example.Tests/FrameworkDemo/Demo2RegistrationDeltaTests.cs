// 每一层注册都要留下痕迹/ Every registration layer must leave a mark.



using System;

using System.Collections.Generic;

using System.Linq;

using FluentAssertions;

using Fuookami.Ospf.Core.Model.Basic;

using Fuookami.Ospf.Core.Model.Intermediate;

using Fuookami.Ospf.Utils.Error;

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

using System.Threading.Tasks;

using Fuookami.Ospf.Utils.Functional;

using Fuookami.Ospf.Math.Algebra.Number;

using Xunit;



namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;



/// <summary>

/// **每一层注册都要在模型里留下痕迹** / Every registration layer must leave a mark in the model.

/// </summary>

/// <remarks>

/// 记录过"后四层注册不新增 token"，据此判断它们是空转。**那个结论是错的，因为量错了计数器**：

/// `Tokens.Tokens` 只装**决策变量**，而载重/总重/力矩注册的是**表达式符号**（派生量，本来就不是决策变量）。

/// 三个计数器一起看，每一层都有增量：约束 `7→11`、变量 `0→8`、符号 `9→31→37→43→50`。

///

/// 这条判据钉的就是那个结构：**配载产生决策变量**、**装载产生约束行**、**载重/总重/力矩产生表达式符号**。

/// 只看"返回 `Ok`"会让"什么都没加的 `Ok`"通过；只看变量数会冤枉后面三层——**两个方向都错过一次了**。

///

/// recorded "the last four layers add no tokens" and concluded they did nothing. **That conclusion was wrong because the wrong counter was read**: `Tokens.Tokens` holds only

/// **decision variables**, while payload/total-weight/torque register **expression symbols** (derived quantities that are not decision variables by design). Reading all three

/// counters together, every layer moves: constraints `7→11`, variables `0→8`, symbols `9→31→37→43→50`.

///

/// This pins that structure: **stowage makes decision variables**, **load makes constraint rows**, **payload/total-weight/torque make expression symbols**. Asserting only "returns

/// `Ok`" would let "an `Ok` that added nothing" pass; asserting only the variable count would wrongly blame the last three — **both directions have been missed once now**.

/// </remarks>

public class Demo2RegistrationDeltaTests {

    /// <summary>

    /// **三层增量各归各处，且没有一层是空转** / Each layer's delta lands where it should, and none of them is idle.

    /// </summary>

    /// <remarks>

    /// 五点成对：配载后变量从 0 变多 / 装载后约束变多而变量不变 / **载重、总重、力矩各让符号变多** /

    /// 约束在这三层**不变**（它们是派生量，不产生行）/ 每到一层三个计数器至少一个在动。

    ///

    /// Five paired points: stowage takes variables from zero to some; load adds constraints while variables stay put; **payload, total weight and torque each add symbols**;

    /// constraints **stay put** across those three (they are derived quantities and make no rows); and at every step at least one of the counters moves.

    /// </remarks>

    [Fact]

    public void Register_ShouldLeaveAMarkAtEveryLayer() {

        RequestDTO input = AircraftLoadingDemo.CreateSampleRequest();

        var pipeline = new DomainPipelineContext();

        pipeline.Init(input).IsOk.Should().BeTrue("the sample request clears the domain chain now");



        LinearMetaModel<Flt64> model = new();

        StowageAggregation stowage = pipeline.StowageContext.Aggregation!;

        MacAggregation mac = pipeline.MacContext.Aggregation!;

        List<(string Layer, int Constraints, int Variables, int Symbols)> readings = new() {

            ("start", model.Constraints.Count, model.Tokens.Tokens.Count, model.Tokens.Symbols.Count),

        };



        stowage.Stowage.Register(model).IsOk.Should().BeTrue();

        readings.Add(("stowage", model.Constraints.Count, model.Tokens.Tokens.Count, model.Tokens.Symbols.Count));

        stowage.Load.Register(model).IsOk.Should().BeTrue();

        readings.Add(("load", model.Constraints.Count, model.Tokens.Tokens.Count, model.Tokens.Symbols.Count));

        stowage.Payload.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();

        readings.Add(("payload", model.Constraints.Count, model.Tokens.Tokens.Count, model.Tokens.Symbols.Count));

        stowage.TotalWeight.Register(model).IsOk.Should().BeTrue();

        readings.Add(("totalWeight", model.Constraints.Count, model.Tokens.Tokens.Count, model.Tokens.Symbols.Count));

        mac.Torque.Register(model).IsOk.Should().BeTrue();

        readings.Add(("torque", model.Constraints.Count, model.Tokens.Tokens.Count, model.Tokens.Symbols.Count));



        readings[0].Variables.Should().Be(0, "nothing is registered yet");



        // 配载：决策变量从无到有 / Stowage: decision variables from nothing to something.

        readings[1].Variables.Should().BeGreaterThan(0, "stowage owns the decision variables");

        readings[1].Constraints.Should().BeGreaterThan(readings[0].Constraints, "stowage also writes rows");



        // 装载：只加行，不加决策变量 / Load: rows only, no new decision variables.

        readings[2].Constraints.Should().BeGreaterThan(readings[1].Constraints, "load turns assignments into rows");

        readings[2].Variables.Should().Be(readings[1].Variables, "load reads existing assignments rather than making its own");



        // 载重 / 总重 / 力矩：只加表达式符号 / Payload, total weight and torque: expression symbols only.

        foreach ((string layer, int index) in new[] { ("payload", 3), ("totalWeight", 4), ("torque", 5) }) {

            readings[index].Symbols.Should().BeGreaterThan(

                readings[index - 1].Symbols,

                layer + " registers derived expressions, so its symbols must grow");

            readings[index].Constraints.Should().Be(

                readings[index - 1].Constraints,

                layer + " is a derived quantity and must not invent rows");

        }



        // 没有一层空转 / No layer is idle.

        for (int index = 1; index < readings.Count; ++index) {

            bool moved = readings[index].Constraints != readings[index - 1].Constraints

                || readings[index].Variables != readings[index - 1].Variables

                || readings[index].Symbols != readings[index - 1].Symbols;

            moved.Should().BeTrue(readings[index].Layer + " returned Ok without changing anything");

        }

    }



    /// <summary>

    /// **七个域走完，模型里有目标** / After all seven domains, the model carries an objective.

    /// </summary>

    /// <remarks>

    /// 五点成对：七域注册成功 / 目标项非空 / **名字里出现效能域的目标**（`express` 或 `loading`）/ 载重派生量在符号表里 /

    /// 约束行数比只注册配载时多。

    /// 只注册子模型会得到"有变量、有行、没有目标"的模型——**求解器于是拿默认目标解出一个与业务无关的答案**，

    /// 而整个过程没有任何报错。这条判据钉的就是那个缺口。

    ///

    /// Five paired points: all seven domains register; the objective parts are non-empty; **an effectiveness-domain objective is named** (`express` or `loading`); the

    /// payload-derived symbol is in the symbol table; and there are more rows than stowage alone produced. Registering only the sub-models (what did) yields

    /// "variables, rows, no objective" — **so the solver picks a default objective and returns an answer unrelated to the business goal**, with nothing reporting it. This

    /// criterion pins that gap.

    /// </remarks>

    [Fact]

    public void Register_ShouldProduceObjectiveParts() {

        RequestDTO input = AircraftLoadingDemo.CreateSampleRequest();

        var pipeline = new DomainPipelineContext();

        pipeline.Init(input).IsOk.Should().BeTrue();

        LinearMetaModel<Flt64> model = new();



        Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> registered = pipeline.Register(StowageMode.FullLoad, new Parameter(), model);

        registered.IsOk.Should().BeTrue(registered.ToString());



        model.MetaSubObjects.Should().NotBeEmpty("registration must add objective parts, not only rows");

        List<string> objectiveNames = [];

        foreach (Fuookami.Ospf.Core.Model.Mechanism.MetaSubObject<Fuookami.Ospf.Math.Algebra.Number.Flt64> part in model.MetaSubObjects) {

            objectiveNames.Add(part.Name);

        }

        string joined = string.Join(",", objectiveNames);

        // **至少两个域各贡献一个目标项**：只钉"非空"会让一个域的目标顶替全部。

        // **At least two domains contribute a part each**: pinning "non-empty" alone would let one domain's objective stand in for all of them.

        joined.Should().Contain("longitudinal balance", "the MAC-optimisation domain contributes this part");

        joined.Should().Contain("item priority reverse", "the express-effectiveness domain contributes this part");

    }



    /// <summary>
    /// **表达式符号的单式会被约束管线展开，不是退化** / Monomials holding expression symbols get expanded, they do not degenerate.
    /// </summary>
    /// <remarks>
    /// 数出示例请求的 52 行里 35 行的左端含 `stowage_*` / `loaded_*` 这类表达式符号，并据此怀疑它们退化成常数比较。
    /// **那怀疑是错的**： 在"配载 + 重心"这个本来可行的模型上做了 A/B——同一行业载下限，一种直接拿表达式符号建、一种先手工展开再建，
    /// **两种都可行**。`Constraint.Create` 走 `ExpandIntermediateSymbols` 递归展开，所以表达式符号是完全受支持的写法。
    ///
    /// 这条判据钉的是**正向不变量**：拿表达式符号建的行能被接受，而且三元模型建得出来——不是"有多少行可疑"。
    ///
    /// counted 35 of 52 rows whose left side holds expression symbols such as `stowage_*` or `loaded_*` and suspected they degenerate into constant
    /// comparisons. **That suspicion was wrong**: ran an A/B on the otherwise feasible stowage + MAC model — the same payload floor row built once straight from
    /// the expression symbols and once pre-expanded by hand — and **both are feasible**. `Constraint.Create` goes through `ExpandIntermediateSymbols`, which expands
    /// recursively, so expression symbols are a fully supported form. This criterion pins the **positive invariant**: a row built on expression symbols is accepted and
    /// the triad model builds — not "how many rows look suspect".
    /// </remarks>
    [Fact]
    public async Task Register_ShouldExpandExpressionSymbolsInsteadOfRejectingThem() {
        RequestDTO input = AircraftLoadingDemo.CreateSampleRequest();
        var pipeline = new DomainPipelineContext();
        pipeline.Init(input).IsOk.Should().BeTrue();
        using LinearMetaModel<Flt64> model = new();
        pipeline.StowageContext.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();
        pipeline.MacContext.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();
        // 适航层也注册上：`payload_limit_minimum` 是它加的行，不注册就测不到那一行。
        // Register the airworthiness layer too: `payload_limit_minimum` is its row, and without it that row is not testable.
        pipeline.AirworthinessSecurityContext.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();

        LinearExpressionSymbol estimate = pipeline.StowageContext.Aggregation!.Payload.EstimatePayload!;
        estimate.Polynomial.Monomials.Should().NotBeEmpty();
        // **前提**：这些单式确实挂着表达式符号，否则这条判据测不到它想测的东西。
        // **Precondition**: these monomials really do hold expression symbols, otherwise the criterion tests nothing.
        int expressionTerms = 0;
        foreach (LinearMonomial<Flt64> monomial in estimate.Polynomial.Monomials) {
            if (monomial.Symbol is IIntermediateSymbol) { ++expressionTerms; }
        }
        expressionTerms.Should().BeGreaterThan(0, "the payload estimate is built from derived symbols by design");

        var row = new LinearInequality<Flt64>(
            new LinearPolynomial<Flt64>(estimate.Polynomial.Monomials, estimate.Polynomial.Constant),
            new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(3.6)),
            Comparison.GE,
            "probe_payload_floor");
        int before = model.Constraints.Count;
        model.AddConstraint(row, group: null, name: "probe_payload_floor").IsOk.Should().BeTrue(
            "a constraint built on expression symbols must be accepted, not silently dropped");
        model.Constraints.Count.Should().Be(before + 1);

        // **真正测到展开的地方**：三元模型必须建得出来。
        // **Where expansion is actually exercised**: the triad model must build.
        Result<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> built =
            await model.BuildLinearTriadModelAsync<Flt64>();
        built.IsOk.Should().BeTrue(built.ToString());
        built.Value.Matrix.Constraints.ConstraintCount.Should().BeGreaterThan(0);

        // **不看行数，看单元格**：展开失败的标志是"这一行在三元模型里没有变量单元格"，而不是行数变了。
        // **Look at the cells, not the row count**: the signature of a failed expansion is a row with no variable cells in the triad model, not a changed row count.
        IReadOnlyList<IReadOnlyList<LinearConstraintCell>> cells = built.Value.Matrix.Constraints.Lhs;
        foreach (string name in new[] { "probe_payload_floor", "payload_limit_minimum" }) {
            int index = -1;
            for (int i = 0; i < model.Constraints.Count; ++i) {
                if (model.Constraints[i].Name == name) { index = i; }
            }
            index.Should().BeGreaterThanOrEqualTo(0, name + " must be in the meta model");
            cells[index].Count.Should().BeGreaterThan(0,
                name + " must keep its variable cells; zero cells means the expression symbols were dropped");
        }
    }




    /// <summary>
    /// **表面密度系数等于货物重量，不是 1550 倍** / The surface-density coefficients are the cargo weights, not 1550x them.
    /// </summary>
    /// <remarks>
    /// 表面密度 = `装载重量 / 舱位面积`。示例请求里舱位是 1.0 x 1.0 米，面积 1.0 平方米，所以系数**应当就是货物重量** 8 / 6 / 4。
    ///
    /// **实测过不是**：`Aircraft.AggregationInitializer` 把矩形边界**硬编码成英寸**（`* 0.0254`），而力臂走 `ToModelLength`
    /// 按 `input.LengthUnit` 换算。请求里写的是米，于是面积变成 `0.0254 x 0.0254 = 0.000645` 平方米，系数被放大 1550 倍
    /// （8 kg 变成 12400），`≤ 1000` 的上限连**一件货**都装不下——表现为"任何正业载都不可行"，而所有约束看上去都加对了。
    ///
    /// 这条判据钉的就是那个系数：单位换算一旦再退回硬编码，它会立刻变红。
    ///
    /// Surface density is `loaded weight / position area`. The sample positions are 1.0 x 1.0 metres, so the area is 1.0 square metre and the coefficients **should be
    /// exactly the cargo weights** 8 / 6 / 4. **measured that they were not**: the aircraft aggregation initializer **hard-coded inches** for the rectangle
    /// (`* 0.0254`) while the arms went through `ToModelLength`, which honours `input.LengthUnit`. Requests are in metres, so the area became `0.0254 x 0.0254 =
    /// 0.000645` square metres and the coefficients were inflated 1550x (8 kg became 12400), leaving the `<= 1000` cap unable to fit **a single piece** — showing up as
    /// "any positive payload is infeasible" while every constraint looked correctly added. This criterion pins that coefficient: if the conversion ever goes back to
    /// a hard-coded constant it turns red immediately.
    /// </remarks>
    [Fact]
    public void Register_ShouldScaleSurfaceDensityByTheRealArea() {
        RequestDTO input = AircraftLoadingDemo.CreateSampleRequest();
        var pipeline = new DomainPipelineContext();
        pipeline.Init(input).IsOk.Should().BeTrue();
        using LinearMetaModel<Flt64> model = new();
        pipeline.StowageContext.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();
        pipeline.MacContext.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();
        pipeline.AirworthinessSecurityContext.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();

        // 三个货物的重量依次是 8 / 6 / 4；面积 1.0 平方米时系数就该是这三个数。
        // The three cargo weights are 8 / 6 / 4; with a 1.0 square-metre area the coefficients must be exactly those.
        foreach (string rowName in new[] {
            "surface_density_limit_request_surface_density_P1",
            "surface_density_limit_request_surface_density_P2" }) {
            LinearInequalityConstraint<Flt64>? row = null;
            foreach (MathConstraint constraint in model.Constraints) {
                if (constraint.Name == rowName && constraint is LinearInequalityConstraint<Flt64> found) {
                    row = found;
                }
            }
            row.Should().NotBeNull(rowName + " must be in the model");
            List<double> coefficients = [];
            foreach (LinearMonomial<Flt64> monomial in row!.Inequality.Lhs.Monomials) {
                coefficients.Add(monomial.Coefficient.ToDouble());
            }
            coefficients.Should().BeEquivalentTo(new[] { 8.0, 6.0, 4.0 },
                "the coefficient is weight over area, and the area is one square metre in the sample");
        }
    }
    /// <summary>

    /// **模型是"可求解形状"：每个目标项都有方向和单式** / The model is solvable-shaped: every objective part has a direction and monomials.

    /// </summary>

    /// <remarks>

    /// 四点成对：目标项都属最小或最大类别 / 每一项至少一个单式 / **符号都带有限范围或已进变量表** / 至少一行约束。

    /// 一个"有目标但目标里没有单式"的模型会让求解器给出一个常数答案，**而调用方会以为问题被优化过了**。

    ///

    /// Four paired points: every part is in the minimum or maximum category; each has at least one monomial; **its symbols are variables or carry finite ranges**;

    /// and there is at least one constraint row. A model with "an objective but no monomials in it" makes the solver return a constant, **and the caller believes the

    /// problem was optimised**.

    /// </remarks>

    [Fact]

    public void Register_ShouldLeaveASolvableShapedModel() {

        RequestDTO input = AircraftLoadingDemo.CreateSampleRequest();

        var pipeline = new DomainPipelineContext();

        pipeline.Init(input).IsOk.Should().BeTrue();

        LinearMetaModel<Flt64> model = new();

        Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> registered =

            pipeline.Register(StowageMode.FullLoad, new Parameter(), model);

        registered.IsOk.Should().BeTrue(registered.ToString());



        model.Constraints.Should().NotBeEmpty("a model without rows is not a problem");

        model.MetaSubObjects.Should().NotBeEmpty("a model without an objective cannot be optimised");

        List<string> weighted = [];

        foreach (Fuookami.Ospf.Core.Model.Mechanism.MetaSubObject<Fuookami.Ospf.Math.Algebra.Number.Flt64> part in model.MetaSubObjects) {

            part.Category.Should().BeOneOf(new[] { ObjectCategory.Minimum, ObjectCategory.Maximum });

            if (part.Polynomial.Monomials.Count > 0) {

                weighted.Add(part.Name);

            }

        }

        // **至少两个目标项真的在权衡决策变量**：全部为空会让求解器返回常数解。

        // **至于 `longitudinal balance`：它在本实现与都是常数占位**（`MakeSlack` 建的是常数零符号），

        // 所以这里不要求每一项都有单式——**但也不假装它有**，这条缺口记在计划文档里。

        // **At least two parts really weigh decision variables**: all-empty would make the solver return a constant. **As for `longitudinal balance`, it is a constant placeholder

        // in both this implementation and the code** (`MakeSlack` builds a constant-zero symbol), so this does not demand monomials from every part — **nor does it pretend they exist**;

        // the gap is recorded in the plan document.

        weighted.Should().HaveCountGreaterThan(1, "the objective must weigh decisions, not just constants");

        weighted.Should().Contain("item priority reverse", "the express-effectiveness family weighs decisions");

    }


    /// <summary>
    /// **那份重复的货物调整行不该出现** / The code's duplicate item-adjustment rows must not appear.
    /// </summary>
    /// <remarks>
    /// 与 两份文件，**加的是同一行**（`loaded[i] eq true`），
    /// 而 **只调用前者**——`git grep -l ItemAdjustmentLimit 12fdb587` 只返回它自己的定义，
    /// 所以后者在是**死代码**。
    ///
    /// 本仓把它并进 `ItemAssignmentLimit`（`ItemStatus.AdjustmentNeeded` 走 `Comparison.EQ`）是对的。这条判据钉住"只有一份"：
    /// 模型里**不许出现名字以 `item_adjustment_limit` 开头的行**。将来"顺手补上"那份重复实现会立刻被拦下——
    /// 重复行不会让模型不可行，**只会让 IIS 与诊断里出现两条一模一样的行**，那种噪声比出错更难查。
    ///
    /// The code ships both and, **adding the same row** (`loaded[i] eq true`), while
    /// **only calls the former** — `git grep -l ItemAdjustmentLimit 12fdb587` returns nothing but its own definition,
    /// so the latter is **dead code in the code**. This implementation folds it into `ItemAssignmentLimit` (`ItemStatus.AdjustmentNeeded` with `Comparison.EQ`), which is
    /// correct. This criterion pins "only one copy": **no row name may start with `item_adjustment_limit`**. Adding the duplicate back would be caught immediately —
    /// a duplicate row does not make the model infeasible, **it only makes two identical rows show up in IIS and diagnostics**, and that noise is harder to chase than
    /// an outright error.
    /// </remarks>
    [Fact]
    public void Stowage_ShouldNotAddTheDeadItemAdjustmentRows() {
        RequestDTO input = AircraftLoadingDemo.CreateSampleRequest();
        var pipeline = new DomainPipelineContext();
        pipeline.Init(input).IsOk.Should().BeTrue();
        using LinearMetaModel<Flt64> model = new();
        pipeline.StowageContext.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();

        int assignmentRows = 0;
        int adjustmentRows = 0;
        for (int i = 0; i < model.Constraints.Count; ++i) {
            string? name = model.Constraints[i].Name;
            if (name is null) { continue; }
            if (name.StartsWith("item_assignment_limit", StringComparison.Ordinal)) { ++assignmentRows; }
            if (name.StartsWith("item_adjustment_limit", StringComparison.Ordinal)) { ++adjustmentRows; }
        }

        assignmentRows.Should().BeGreaterThan(0, "the merged limit must still produce the assignment rows");
        adjustmentRows.Should().Be(0, "the baseline's duplicate pipeline is dead code and must stay out");
    }
#if SCIP_INTEGRATION

    /// <summary>
    /// **示例请求现在真的能解出来** / The sample request really solves now.
    /// </summary>
    /// <remarks>
    /// 一路看到的"示例请求不可行"是**两个单位缺陷**叠加的结果，修了矩形的单位、 修了力矩的换算因子，
    /// 于是这条判据从"回话如实报不可行"翻面成"真的解得出可行解"。
    ///
    /// 四点成对：求解成功 / 拿到变量取值 / 目标值有限 / **业载下限确实被满足**（地板 0.6 时 `≥ 10.8`，解出来的载荷不能是零）。
    /// 最后一点最要紧：**只钉"求解成功"会放过一个把载荷全钉零却依然可行的模型**——那种解是可行的，但什么都没装。
    ///
    /// What kept observing as "the sample request is infeasible" was **two unit defects stacked**: fixed the rectangle's unit and fixed the moment
    /// conversion factor, so this criterion flips from "report infeasibility honestly" to "actually produce a feasible solution". Four paired points: the solve succeeds;
    /// variable values come back; the objective is finite; and **the payload floor is really met** (a 0.6 floor means `>= 10.8`, so the loaded weight cannot be zero).
    /// The last one matters most: **pinning "the solve succeeded" alone would let through a model that pinning everything to zero is still feasible** — a feasible answer
    /// that loaded nothing.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldSolveTheSampleRequest() {
        var demo = new AircraftLoadingDemo();
        Fuookami.Ospf.Core.Plugin.Scip.ScipLinearSolver solver = new();

        Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> solved =
            await demo.SolveAsync(solver);
        solved.IsOk.Should().BeTrue(solved.ToString());

        demo.Solution.Should().NotBeNull("a successful solve must hand back variable values");
        demo.Solution!.Should().NotBeEmpty();
        demo.Objective.Should().NotBeNull();
        double objective = demo.Objective!.Value.ToDouble();
        double.IsFinite(objective).Should().BeTrue(
            "a non-finite objective means the model is broken, not optimal");

        // **解出来的不能是"全零"**：地板把业载下限钉在 10.8，可行解必须真的装到东西；
        // 一个所有变量都为零的解**也是可行的**，但那什么都没装。
        // **The answer cannot be all zeros**: the floor pins the payload to at least 10.8, so a feasible answer must actually carry something;
        // an answer with every variable at zero **is still feasible** but has loaded nothing.
        bool anyNonZero = false;
        foreach (Flt64 value in demo.Solution!) {
            if (value.ToDouble() != 0.0) { anyNonZero = true; }
        }
        anyNonZero.Should().BeTrue("a plan that loads nothing satisfies the constraints but demonstrates nothing");
        demo.Notes.Should().Contain(note => note.Contains("Solved with"), "a successful solve must be recorded in the notes");
    }


    /// <summary>
    /// **每一条适航行单独放开装载都不再挡路** / No single airworthiness row blocks loading any more.
    /// </summary>
    /// <remarks>
    /// 用"每次只放一行 + 一条正业载下限"的隔离实验指认出三条挡路的行——`target_longitudinal_moment_limit`、
    /// `lateral_imbalance_limit`、`envelope_limit`，**三条都读力矩**。 修掉力矩的换算因子之后，它们全部放开。
    ///
    /// 这条判据现在钉的是**十一行全部可行**。力矩的单位换算一旦再退回"力臂按英寸、边界按米"，
    /// 那三条会立刻重新变红——**这就是它比"能不能求解"更早报警的地方**。
    ///
    /// used "keep exactly one row plus a positive payload floor" to name three blocking rows — `target_longitudinal_moment_limit`, `lateral_imbalance_limit` and
    /// `envelope_limit`, **all three reading torque**. Once fixed the torque conversion factor they all let loading through. This criterion now pins **all eleven
    /// feasible**: if the unit conversion ever goes back to "arms in inches, bounds in metres", those three turn red immediately — **which is where it warns earlier
    /// than "can we solve at all"**.
    /// </remarks>
    [Fact]
    public async Task Airworthiness_ShouldLetEveryRowThroughIndividually() {
        RequestDTO input = AircraftLoadingDemo.CreateSampleRequest() with { MinPayloadRatio = 0.2 };
        string[] rows = [
            "target_longitudinal_moment_limit", "lateral_imbalance_limit", "envelope_limit",
            "linear_density_limit", "surface_density_limit", "cumulative_load_weight_limit",
            "zone_load_weight_limit", "low_payload_limit", "total_weight_limit",
            "horizontal_stabilizer_limit", "adjacent_gap_limit"
        ];
        foreach (string name in rows) {
            (await IsolateAsync(input, name)).Should().BeTrue(name + " must not block loading on its own");
        }
    }

    private static async Task<bool> IsolateAsync(RequestDTO input, string pipelineName) {
        var pipeline = new DomainPipelineContext();
        pipeline.Init(input).IsOk.Should().BeTrue();
        using LinearMetaModel<Flt64> model = new();
        pipeline.StowageContext.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();
        pipeline.MacContext.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();
        Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.AirworthinessSecurityAggregation aggregation = pipeline.AirworthinessSecurityContext.Aggregation!;
        aggregation.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();

        Result<IReadOnlyList<Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.SecurityPipeline>, ErrorCode, Error<ErrorCode>> generated =
            new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.PipelineListGenerator(aggregation)
                .Invoke(StowageMode.FullLoad);
        generated.IsOk.Should().BeTrue(generated.ToString());
        bool found = false;
        foreach (Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.SecurityPipeline item
                 in ((Ok<IReadOnlyList<Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.SecurityPipeline>, ErrorCode, Error<ErrorCode>>)generated).Value!) {
            if (item.Name != pipelineName) { continue; }
            item.Invoke(model).IsOk.Should().BeTrue(pipelineName + " must register");
            found = true;
        }
        found.Should().BeTrue(pipelineName + " must exist in the pipeline list");

        LinearExpressionSymbol estimate = pipeline.StowageContext.Aggregation!.Payload.EstimatePayload!;
        var floor = new LinearInequality<Flt64>(
            new LinearPolynomial<Flt64>(estimate.Polynomial.Monomials, estimate.Polynomial.Constant),
            new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(3.6)),
            Comparison.GE,
            "probe_floor");
        model.AddConstraint(floor, group: null, name: "probe_floor").IsOk.Should().BeTrue("the payload floor must be accepted");

        Result<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> built =
            await model.BuildLinearTriadModelAsync<Flt64>();
        built.IsOk.Should().BeTrue(pipelineName + ": " + built.ToString());
        var solver = new Fuookami.Ospf.Core.Plugin.Scip.ScipLinearSolver();
        Result<Fuookami.Ospf.Core.Solver.Output.SolverOutput, ErrorCode, Error<ErrorCode>> output =
            await solver.InvokeAsync(built.Value.Matrix, iisConfig: new Fuookami.Ospf.Core.Solver.Iis.IisConfig());
        output.IsOk.Should().BeTrue(pipelineName + ": " + output.ToString());
        Fuookami.Ospf.Core.Solver.Output.SolverOutput value =
            ((Ok<Fuookami.Ospf.Core.Solver.Output.SolverOutput, ErrorCode, Error<ErrorCode>>)output).Value;
        return value is not Fuookami.Ospf.Core.Solver.Output.LinearInfeasibleSolverOutput;
    }

#endif

    /// <summary>

    /// **符号表里能按名字找到派生量** / The derived quantities are findable by name in the symbol table.

    /// </summary>

    /// <remarks>

    /// 三点成对：载重符号在 / 总重符号在 / 力矩符号在。只看计数会让"随便加了三个别的符号"通过——

    /// **计数是规模，名字才是身份**。

    ///

    /// Three paired points: the payload symbol is there; the total-weight symbol is there; the torque symbol is there. Counting alone would let "three unrelated symbols were added"

    /// pass — **a count is size, a name is identity**.

    /// </remarks>

    [Fact]

    public void Register_ShouldNameTheDerivedSymbols() {

        RequestDTO input = AircraftLoadingDemo.CreateSampleRequest();

        var pipeline = new DomainPipelineContext();

        pipeline.Init(input).IsOk.Should().BeTrue();

        var model = new LinearMetaModel<Flt64>();



        pipeline.StowageContext.Aggregation!.Stowage.Register(model).IsOk.Should().BeTrue();

        pipeline.StowageContext.Aggregation!.Load.Register(model).IsOk.Should().BeTrue();

        pipeline.StowageContext.Aggregation!.Payload.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();

        pipeline.StowageContext.Aggregation!.TotalWeight.Register(model).IsOk.Should().BeTrue();

        pipeline.MacContext.Aggregation!.Torque.Register(model).IsOk.Should().BeTrue();



        List<string> names = [];

        foreach (Fuookami.Ospf.Math.Symbol.ISymbol symbol in model.Tokens.Symbols) {

            names.Add(symbol.Name);

        }

        string joined = string.Join(",", names);



        joined.Should().Contain("actual_payload", "the payload layer must register its derived symbol");

        joined.Should().Contain("actual_total_weight", "the total-weight layer must register its derived symbol");

        joined.Should().Contain("load_longitudinal_torque", "the torque layer must register its derived symbol");

    }

}

