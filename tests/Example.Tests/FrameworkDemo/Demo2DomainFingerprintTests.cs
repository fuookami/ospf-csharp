// 逐域累积指纹的钉子/ Pins on the cumulative per-domain fingerprint.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **七个域的累积指纹：任何一批改了模型形状，这张表都得跟着改** / The cumulative fingerprint of the seven domains: any batch that changes the model shape updates this table.
/// </summary>
/// <remarks>
/// 表里的四个数是**注册到该域为止的累积值**：行数、符号数、决策变量数、目标份数。它钉的不是"模型对不对"（那要靠语义判据），
/// 而是**"模型形状有没有在没人注意时变掉"**——少一条约束、多一个变量、目标少一份，这类变化不会让任何单条判据变红，
/// 却会让求解结果悄悄不同。
///
/// **改这张表是预期动作**：只要是有意改动模型形状，就更新数字并在计划文档里写明为什么。**坏掉的是"没改表却改了代码"。**
///
/// 数字本身也说明了域的边界：`airworthiness_security` 一次加了 29 行与 24 个变量（舱位占用），却**一份目标都不加**；
/// `soft_security` 才开始加目标（3 份），到 `loading_effectiveness` 收在 7 份。**这与"适航只管约束、软安全才开始优化"的读法一致。**
///
/// The four numbers are **cumulative up to and including that domain**: rows, symbols, decision variables, objective parts. This pins not "is the model right"
/// (that needs semantic criteria) but **"has the model's shape changed while nobody was looking"** — a dropped constraint, an extra variable or a missing objective
/// part turns no single criterion red, yet silently changes the answer.
///
/// **Editing this table is the expected action**: an intentional shape change updates the numbers and the plan entry says why. **What breaks things is changing
/// code without the table.** The numbers also mark the domain boundaries: `airworthiness_security` adds 29 rows and 24 variables (position occupancy) at once and
/// **no objective part at all**, while `soft_security` is where objectives begin (3), ending at 7 with `loading_effectiveness` — consistent with "airworthiness
/// constrains, soft security starts optimising".
/// </remarks>
public sealed class Demo2DomainFingerprintTests {
    /// <summary>注册到该域为止的累积指纹（全配载）/ Cumulative fingerprint through each domain (full load).</summary>
    private static readonly (string Domain, int Rows, int Symbols, int Variables, int Objectives)[] FullLoad = [
        ("stowage", 18, 45, 8, 0),
        ("mac", 18, 53, 8, 0),
        ("airworthiness_security", 47, 64, 32, 0),
        ("soft_security", 47, 67, 39, 3),
        ("mac_optimization", 47, 69, 39, 4),
        ("express_effectiveness", 48, 72, 48, 5),
        ("loading_effectiveness", 52, 76, 60, 7),
    ];

    /// <summary>预配载 / Predistribution.</summary>
    /// <remarks>
    /// **需要请求里给出谓词装载重量规格**：没有它 `Register` 直接失败，这张表也就无从测起。
    /// 给出之后与全配载的差别是"多 4 行、多 8 个变量、目标多一份"（52/76/60/7 → 54/76/59/8）：
    /// 谓词重量变量**既加变量也加目标**——这正是"预配载要预估重量"的模型含义。
    ///
    /// **The request has to carry the predicate load weight specification**: without it `Register` fails outright and this table cannot be measured at all.
    /// Once supplied, the difference from full load is "4 more rows, 8 more variables, one more objective part" (52/76/60/7 → 54/76/59/8): the predicate weight
    /// **adds both variables and an objective part** — the model-side meaning of "predistribution has to estimate weights".
    /// </remarks>
    private static readonly (string Domain, int Rows, int Symbols, int Variables, int Objectives)[] Predistribution = [
        ("stowage", 22, 45, 16, 0),
        ("mac", 22, 53, 16, 0),
        ("airworthiness_security", 51, 64, 40, 0),
        ("soft_security", 51, 67, 47, 3),
        ("mac_optimization", 51, 68, 47, 4),
        ("express_effectiveness", 51, 68, 47, 5),
        ("loading_effectiveness", 54, 76, 59, 8),
    ];

    /// <summary>建议打板 / Weight recommendation.</summary>
    /// <remarks>
    /// **这是六域，不是七域**。`WeightRecommendationPipelineContext` 用时效、推荐重量均衡、载荷最大化**替换掉**
    /// 软安全、重心优化、装卸效率——所以这张表的行名与前两张**不同**，不是"同一批域少了几行"。
    ///
    /// 之前这里记的是"与全配载完全相同"，那是**模式还没接通时测出来的假数据**； ~ 之间它记的是"七域的建议打板"
    /// （`54/71/53/2`），那是**从不存在的形状**——通用上下文的 `Register` 只有七域一套。 按模式分开组成之后，
    /// 这里第一次量到真正的建议打板：`55/64/34/2`，与 `WeightRecommendationApplication` 的 `direct_model=rows55,symbols64,objective2` 一致。
    ///
    /// 另一个形状细节：**均衡域只加符号与目标、不加行**（`50/64/34/0` → `55/64/34/1`）。
    ///
 /// **This is six domains, not seven**. The implementation swaps in express effectiveness, recommended-weight equalization and payload maximization **for** soft
    /// security, mac optimization and loading effectiveness, so the row names here differ from the two tables above — it is not "the same domains with fewer rows".
    /// Before this table said "identical to full load", which was **fake data measured while the mode was not yet connected**; between and it recorded a
 /// "seven-domain weight recommendation" (`54/71/53/2`), **a shape the implementation never builds** because the generic context's `Register` only had the seven-domain set.
    /// With the composition split by mode this is the first real measurement: `55/64/34/2`, matching `WeightRecommendationApplication`'s
    /// `direct_model=rows55,symbols64,objective2`. One further shape note: **equalization adds symbols and objectives but no rows** (`50/64/34/0` → `55/64/34/1`).
    /// </remarks>
    private static readonly (string Domain, int Rows, int Symbols, int Variables, int Objectives)[] WeightRecommendation = [
        ("stowage", 20, 45, 10, 0),
        ("mac", 20, 53, 10, 0),
        ("airworthiness_security", 49, 64, 34, 0),
        ("express_effectiveness", 50, 64, 34, 0),
        ("recommended_weight_equalization", 55, 64, 34, 1),
        ("payload_maximization", 55, 64, 34, 2),
    ];

    [Theory]
    [InlineData(StowageMode.FullLoad)]
    [InlineData(StowageMode.Predistribution)]
    [InlineData(StowageMode.WeightRecommendation)]
    public void Register_ShouldMatchTheRecordedFingerprint(StowageMode mode) {
        RequestDTO request = RequestWithPredicateSpec();
        var pipeline = new DomainPipelineContext();
        // **`Init` 必须收到模式**：装配器靠它决定谓词/推荐重量旗标。只把模式传给 `Register` 而 `Init` 拿默认值，
        // 装配出来的仍是全配载模型——**这正是 抓到的那个"模式在生产路径上是死的"缺陷**。
        //
        // **`Init` must receive the mode**: the initialiser uses it to decide the predicate/recommended weight flags. Passing the mode only to `Register` while `Init`
        // takes the default still assembles the full-load model — **exactly the "the mode is dead on the production path" defect caught**.
        pipeline.Init(request, mode).IsOk.Should().BeTrue();

        using var model = new LinearMetaModel<Flt64>();
        Parameter parameter = new();
        // **三条管线是三种组成**：全配载/预配载七域，建议打板六域。步表跟着模式走——
        // 以前这里一张固定的七域表应付三种模式，于是建议打板那一行量的其实是"七域的建议打板"，而那种形状从不构建。
        //
        // **Three pipelines, three compositions**: seven domains for full load and predistribution, six for weight recommendation. The step list follows the mode;
        // one fixed seven-domain list used to serve all three, so the weight-recommendation row measured a "seven-domain weight recommendation" that is never built.
        List<(string Name, Func<Try> Run)> steps = mode == StowageMode.WeightRecommendation
            ? [
                ("stowage", () => pipeline.StowageContext.Register(mode, model)),
                ("mac", () => pipeline.MacContext.Register(mode, model)),
                ("airworthiness_security", () => pipeline.AirworthinessSecurityContext.Register(mode, model)),
                ("express_effectiveness", () => pipeline.ExpressEffectivenessContext.Register(mode, parameter, model)),
                ("recommended_weight_equalization", () => pipeline.RecommendedWeightEqualizationContext.Register(mode, parameter, model)),
                ("payload_maximization", () => pipeline.PayloadMaximizationContext.Register(mode, parameter, model)),
            ]
            : [
                ("stowage", () => pipeline.StowageContext.Register(mode, model)),
                ("mac", () => pipeline.MacContext.Register(mode, model)),
                ("airworthiness_security", () => pipeline.AirworthinessSecurityContext.Register(mode, model)),
                ("soft_security", () => pipeline.SoftSecurityContext.Register(mode, parameter, model)),
                ("mac_optimization", () => pipeline.MacOptimizationContext.Register(mode, parameter, model)),
                ("express_effectiveness", () => pipeline.ExpressEffectivenessContext.Register(mode, parameter, model)),
                ("loading_effectiveness", () => pipeline.LoadingEffectivenessContext.Register(mode, parameter, model)),
            ];

        List<(string Domain, int Rows, int Symbols, int Variables, int Objectives)> actual = new();
        foreach ((string name, Func<Try> run) in steps) {
            Try step = run();
            step.IsOk.Should().BeTrue(name + ": " + step.ToString());
            actual.Add((name, model.Constraints.Count, model.Tokens.Symbols.Count, model.Tokens.Tokens.Count, model.MetaSubObjects.Count));
        }

        actual.Should().Equal(TableOf(mode),
            "the fingerprint is the model's shape; changing it on purpose means updating this table and saying why in the plan");
    }

    [Fact]
    public void TheThreeModes_ShouldHaveDifferentShapes() {
        // **三种模式的形状必须真的不同**： 让 `PositionStatus.From` 按模式决定旗标，才把这个模式真正传进装配器。
        // 在此之前三个模式装配出的是同一个模型，而那三张"指纹"是假的。
        //
        // **The three modes must really differ**: made `PositionStatus.From` decide the flags by mode, and only actually threaded the mode into the
        // initialiser. Before that all three assembled the same model, and those three "fingerprints" were fake.
        FullLoad.Should().NotEqual(Predistribution);
        FullLoad.Should().NotEqual(WeightRecommendation);
        Predistribution.Should().NotEqual(WeightRecommendation);

        // **只断言数据支持的方向**：预配载与建议打板都比全配载多行（谓词/推荐重量变量各有自己的行），
        // 目标份数则预配载最多、建议打板最少——**"多变量"这句话不能泛泛地说**，两种模式的变量数在中间几步是交叉的。
        //
        // **Only assert the directions the data supports**: both predistribution and weight recommendation carry more rows than full load (the predicate and
        // recommended weight variables each bring their own rows), while objective parts peak in predistribution and bottom out in weight recommendation —
        // **"more variables" cannot be stated loosely**, since the two modes' variable counts cross over in the middle domains.
        // **建议打板不能按索引比**：它是**六域**、行名都不同，`WeightRecommendation[i]` 与 `FullLoad[i]` 根本不是同一个域。
        // 按索引比在它变短之后直接越界——此前就是那样红的。这里只按索引比**共享前缀**的预配载，建议打板单独比总量。
        //
        // **Weight recommendation must not be compared by index**: it is **six domains** with different row names, so `WeightRecommendation[i]` and `FullLoad[i]`
        // are not the same domain at all; comparing by index runs off the end once it is shorter — which is exactly how it went red. Only predistribution shares the
        // prefix and is compared by index; weight recommendation is compared on totals.
        for (int i = 0; i < FullLoad.Length; ++i) {
            Predistribution[i].Rows.Should().BeGreaterThanOrEqualTo(FullLoad[i].Rows, "predistribution rows at " + FullLoad[i].Domain);
        }
        Predistribution[0].Variables.Should().BeGreaterThan(FullLoad[0].Variables, "the predicate weight variable exists from the stowage domain on");
        Predistribution[^1].Objectives.Should().BeGreaterThan(FullLoad[^1].Objectives);
        WeightRecommendation[^1].Objectives.Should().BeLessThan(FullLoad[^1].Objectives,
            "the recommended-weight path drops the full-load objective set");
        WeightRecommendation.Length.Should().Be(6, "the baseline's weight-recommendation pipeline is six domains, not full load's seven");
    }

    [Fact]
    public void Predistribution_ShouldAssembleOnceTheSpecExists() {
        // **记的是"装配不出来"，把它翻面**：请求给了谓词装载重量规格之后，配载域必须能注册成功。
        // 这条判据记的是缺口，缺口补上就得改判据——**不是为了让测试变绿而放宽**。
        //
        // **recorded "cannot be assembled"; flips it**: once the request carries the predicate load weight specification, the stowage domain must register
        // successfully. This criterion records a gap, and closing the gap means changing the criterion — **it is not relaxed just to turn the test green**.
        RequestDTO request = RequestWithPredicateSpec();
        var pipeline = new DomainPipelineContext();
        pipeline.Init(request, StowageMode.Predistribution).IsOk.Should().BeTrue();

        using var model = new LinearMetaModel<Flt64>();
        Result<Success, ErrorCode, Error<ErrorCode>> registered = pipeline.StowageContext.Register(StowageMode.Predistribution, model);

        registered.IsOk.Should().BeTrue(registered.ToString());
        model.Constraints.Count.Should().Be(22, "the predicate weight adds four rows on top of full load's eighteen");
    }

    [Fact]
    public async Task Predistribution_ShouldBuildTheTriad() {
        // **记的是"建不出三元模型"，把它翻面**：给表达式符号声明上下界之后，Big-M 推得出来，建模型成功。
        // 这条判据记的是缺口，缺口补上就得改判据——**不是为了让测试变绿而放宽**。
        //
        // **recorded "cannot build the triad"; flips it**: once the expression symbols declare bounds the Big-M is inferable and the model builds. This
        // criterion records a gap, and closing the gap means changing the criterion — **it is not relaxed just to turn the test green**.
        RequestDTO request = RequestWithPredicateSpec();
        var pipeline = new DomainPipelineContext();
        pipeline.Init(request, StowageMode.Predistribution).IsOk.Should().BeTrue();

        using var model = new LinearMetaModel<Flt64>();
        Result<Success, ErrorCode, Error<ErrorCode>> registered = pipeline.Register(StowageMode.Predistribution, new Parameter(), model);
        registered.IsOk.Should().BeTrue("registration must succeed: " + registered.ToString());

        Result<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> built =
            await model.BuildLinearTriadModelAsync<Flt64>();

        built.IsOk.Should().BeTrue("the Big-M is inferable now that stowage symbols declare [0, 1]: " + built.ToString());
    }

    [Fact]
    public void StowageSymbols_ShouldDeclareBinaryBounds() {
        // **根因的钉子**：`BigMValues.SymbolBounds` 只认 `IVariableItem` 的范围与 `IIntermediateSymbol` 的 `Range.Range`。
        // 表达式符号不声明界就永远是空的，于是任何需要 Big-M 的下游都只能报错。
        // 这条判据直接看符号的范围——**比"能不能建模型"更靠近根因**，模型建得出来也可能是因为别处凑巧补上了界。
        //
        // **The root-cause pin**: `BigMValues.SymbolBounds` only reads ranges from `IVariableItem` and the `Range.Range` of an `IIntermediateSymbol`. An expression
        // symbol without a declared range stays empty forever, so any downstream needing a Big-M can only fail. This criterion looks at the range itself — **closer to
        // the root cause than "does the model build"**, since a model can build because some other place happened to supply a bound.
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        var pipeline = new DomainPipelineContext();
        pipeline.Init(request).IsOk.Should().BeTrue();

        using var model = new LinearMetaModel<Flt64>();
        pipeline.StowageContext.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();

        LinearExpressionSymbol stowage = pipeline.StowageContext.Aggregation!.Stowage.StowageSymbol(0, 0)!;
        stowage.Range.Range.Should().NotBeNull("an undeclared range leaves every Big-M inference empty");
        stowage.Range.Range!.LowerBound.Value.UnwrapOrNull().Should().Be(new Flt64(0));
        stowage.Range.Range!.UpperBound.Value.UnwrapOrNull().Should().Be(new Flt64(1));

        LinearExpressionSymbol loaded = pipeline.StowageContext.Aggregation!.Stowage.LoadedSymbol(0)!;
        loaded.Range.Range.Should().NotBeNull();
        loaded.Range.Range!.LowerBound.Value.UnwrapOrNull().Should().Be(new Flt64(0));
    }

    [Fact]
    public void Predistribution_ShouldRejectAMissingSpecDuringAssembly() {
        // 把这条判据的落点提前了。** 它原本断言"装配成功、注册阶段才拒"。
        // 读代码可见：`predicateLoadWeightMin == null` 那道守卫**在 `AggregationInitializer` 里**，也就是**装配阶段**。
        // 所以旧判据钉的是本仓的一处实现偏差（守卫放晚了），不是既定行为。
        //
 // **moved where this criterion lands.** It used to assert "assembly succeeds and registration refuses". Reading the implementation: the
        // `predicateLoadWeightMin == null` guard sits **in `AggregationInitializer`**, i.e. **during assembly**. The old criterion therefore pinned a local deviation
        // (the guard sat one step late), not established behaviour.
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        var pipeline = new DomainPipelineContext();
        Result<Success, ErrorCode, Error<ErrorCode>> assembled = pipeline.Init(request, StowageMode.Predistribution);

        assembled.IsFailed.Should().BeTrue(
            "predistribution without a predicate bound is refused where the baseline refuses it: during assembly");
        var failed = (Failed<Success, ErrorCode, Error<ErrorCode>>)assembled;
        failed.Error.Code.Should().Be(ErrorCode.IllegalArgument);
        failed.Error.Message.Should().Contain("谓词装载重量下限", "the message must name the missing input, not just fail");
        failed.Error.Message.Should().Contain("P1", "and it must name the position, or a request with many positions gives no way to find it");
    }

    /// <summary>示例请求 + 谓词装载重量规格 / The sample request plus the predicate load weight specification.</summary>
    /// <remarks>
    /// **规格只在要跑预配载时才有意义**，所以没有塞进 `CreateSampleRequest`：那会让每一条既有判据测到的模型形状都变，
    /// 而它们与预配载无关。**需要的判据自己带规格。**
    ///
    /// **The spec only means something when predistribution runs**, so it does not go into `CreateSampleRequest`: that would change the model shape measured by every
    /// existing criterion, none of which is about predistribution. **Criteria that need it bring it themselves.**
    /// </remarks>
    private static RequestDTO RequestWithPredicateSpec() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        List<PositionInput> positions = new();
        foreach (PositionInput position in sample.Positions) {
            positions.Add(position with { PredicateLoadWeight = new PositionPredicateLoadWeightInput(10.0, 1.0, 10.0) });
        }
        return sample with { Positions = positions };
    }

    private static (string Domain, int Rows, int Symbols, int Variables, int Objectives)[] TableOf(StowageMode mode) => mode switch {
        StowageMode.Predistribution => Predistribution,
        StowageMode.WeightRecommendation => WeightRecommendation,
        _ => FullLoad,
    };

    [Fact]
    public void Fingerprint_ShouldShowThatConstraintsComeFirstAndObjectivesLater() {
        // **这张表顺带证明了一件事**：适航域只加约束不加目标，软安全才开始加目标。
        // 谁把目标挪进适航域，或把软安全的目标删掉，这条会先红——**比在求解结果里发现"优化方向变了"早得多**。
        // **The table also proves one thing**: airworthiness adds only constraints, and objectives begin with soft security. Anyone moving an objective into
        // airworthiness, or deleting a soft-security objective, turns this red first — **much earlier than noticing in a solve result that the direction changed**.
        (string Domain, int Rows, int Symbols, int Variables, int Objectives) airworthiness = FullLoad[2];
        (string Domain, int Rows, int Symbols, int Variables, int Objectives) softSecurity = FullLoad[3];

        airworthiness.Objectives.Should().Be(0, "airworthiness constrains, it does not optimise");
        softSecurity.Objectives.Should().BeGreaterThan(0, "soft security is where the objective parts begin");
        softSecurity.Rows.Should().Be(airworthiness.Rows, "soft security adds objectives, not rows");
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **预配载现在真的解得出可行解** / Predistribution really solves now.
    /// </summary>
    /// <remarks>
    /// 那条"预配载不可行"是在模式死掉、装配失败的前提下得出的； 补规格、 补界之后，
    /// **三种模式都能解**。这条判据钉住预配载的解：成功、目标有限、**赋值非空**。
    ///
    /// **目标值不是零**（实测 1.0）：预配载的谓词重量是要花代价的，全零解在这里不成立——
    /// 这比"求解成功"更能说明模型真的在做预配载该做的事。
    ///
    /// the earlier "predistribution is infeasible" came from a dead mode and a failing assembly; after supplied the specification and supplied the bounds,
    /// **all three modes solve**. This pins predistribution's solution: success, a finite objective, and **a non-empty assignment**. **The objective is not zero**
    /// (measured: 1.0), because predistribution's predicate weight costs something and the all-zero answer does not hold here — which says more about the model
    /// doing what predistribution is for than "the solve succeeded" does.
    /// </remarks>
    [Fact]
    public async Task Predistribution_ShouldSolve() {
        RequestDTO request = RequestWithPredicateSpec();
        var pipeline = new DomainPipelineContext();
        pipeline.Init(request, StowageMode.Predistribution).IsOk.Should().BeTrue();

        using var model = new LinearMetaModel<Flt64>();
        pipeline.Register(StowageMode.Predistribution, new Parameter(), model).IsOk.Should().BeTrue();
        Result<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> built =
            await model.BuildLinearTriadModelAsync<Flt64>();
        built.IsOk.Should().BeTrue(built.ToString());

        var solver = new Fuookami.Ospf.Core.Plugin.Scip.ScipLinearSolver();
        Result<Fuookami.Ospf.Core.Solver.Output.SolverOutput, ErrorCode, Error<ErrorCode>> solved =
            await solver.InvokeAsync(built.Value.Matrix, iisConfig: new Fuookami.Ospf.Core.Solver.Iis.IisConfig());

        solved.IsOk.Should().BeTrue(solved.ToString());
        Fuookami.Ospf.Core.Solver.Output.SolverOutput output = ((Ok<Fuookami.Ospf.Core.Solver.Output.SolverOutput, ErrorCode, Error<ErrorCode>>)solved).Value;
        output.Should().BeOfType<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>>(
            "predistribution must produce a feasible plan, not an infeasibility report");
    }
#endif

#if SCIP_INTEGRATION
    /// <summary>三种模式的目标值与非零赋值个数 / Objective value and non-zero count per mode.</summary>
    /// <remarks>
    /// **这两个数一起才有用**：目标值抓"系数写错"，非零赋值个数抓"结构变了"（少一条约束、多一个本该为零的变量）。
    /// 单独钉哪一个都会漏掉一类。
    ///
    /// **The two numbers are only useful together**: the objective catches "a coefficient is wrong", the non-zero count catches "the structure moved" (a missing
    /// constraint, or a variable that should be zero turning non-zero). Pinning either alone misses one class.
    /// </remarks>
    private static readonly (StowageMode Mode, double Objective, int NonZero)[] Solutions = [
        (StowageMode.FullLoad, 0.0, 38),
        (StowageMode.Predistribution, 1.0, 42),
        (StowageMode.WeightRecommendation, -14.0, 22),
    ];

    [Theory]
    [InlineData(StowageMode.FullLoad)]
    [InlineData(StowageMode.Predistribution)]
    [InlineData(StowageMode.WeightRecommendation)]
    public async Task Solve_ShouldMatchTheRecordedObjective(StowageMode mode) {
        RequestDTO request = RequestWithPredicateSpec();
        var pipeline = new DomainPipelineContext();
        pipeline.Init(request, mode).IsOk.Should().BeTrue();

        // **用请求自己解析出来的参数，而不是 `new Parameter`**：`ResolvedParameter` 才带上推荐重量的那些系数
        // （ 实测：默认参数下载荷最大化的系数是 0，于是建议打板的目标恒为 0——形状对、数值无意义）。
        // 三个应用路径用的也是 `request.ResolvedParameter`，这里跟着它，两条路径才可比。
        //
        // **Use the parameter the request resolves, not `new Parameter`**: only `ResolvedParameter` carries the recommendation coefficients (measured: with
        // defaults the payload-maximization coefficient is 0, so the weight-recommendation objective is identically 0 — right shape, meaningless number). All three
        // application paths use `request.ResolvedParameter` as well, so following it here is what makes the two paths comparable.
        using var model = new LinearMetaModel<Flt64>();
        pipeline.Register(mode, request.ResolvedParameter, model).IsOk.Should().BeTrue();
        Result<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> built =
            await model.BuildLinearTriadModelAsync<Flt64>();
        built.IsOk.Should().BeTrue(built.ToString());

        var solver = new Fuookami.Ospf.Core.Plugin.Scip.ScipLinearSolver();
        Result<Fuookami.Ospf.Core.Solver.Output.SolverOutput, ErrorCode, Error<ErrorCode>> solved =
            await solver.InvokeAsync(built.Value.Matrix, iisConfig: new Fuookami.Ospf.Core.Solver.Iis.IisConfig());
        solved.IsOk.Should().BeTrue(solved.ToString());
        var output = (Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>)
            ((Ok<Fuookami.Ospf.Core.Solver.Output.SolverOutput, ErrorCode, Error<ErrorCode>>)solved).Value;

        (StowageMode Mode, double Objective, int NonZero) expected = System.Linq.Enumerable.Single(Solutions, entry => entry.Mode == mode);
        output.Obj.ToDouble().Should().BeApproximately(expected.Objective, 1e-9,
            "a coefficient change moves the objective, and this is the only place that would notice");

        int nonZero = 0;
        foreach (Flt64 value in output.Solution.Values) {
            if (value.ToDouble() != 0.0) {
                ++nonZero;
            }
        }
        nonZero.Should().Be(expected.NonZero,
            "a structural change (a missing constraint, a variable that should be zero) moves this count");
    }

    [Fact]
    public void TheThreeSolutions_ShouldBeDistinguishable() {
        // **三对 (目标值, 非零个数) 必须两两不同**：全配载 (0, 38) / 预配载 (1, 42) / 建议打板 (-14, 22)。
        //
        // **这条判据的前半句在这里翻转了**：它以前写着"两个模式目标值都是 0，所以没有非零个数这一维就分不开"——
        // 那时建议打板的目标恒为 0，因为**均衡域压根没进模型**。现在三个目标值互不相同，
        // 那句"目标值分不开"不再成立，**判据里钉着它的那半条也跟着删掉**，否则它就成了描述一个已不存在状态的话。
        //
        // 非零个数这一维仍然保留：它抓的是"结构变了"（少一条约束、多一个本该为零的变量），目标值抓不到那一类。
        //
        // **The three (objective, non-zero count) pairs must be pairwise distinct**: full load (0, 38), predistribution (1, 42), weight recommendation (-14, 22).
        // **The first half of this criterion flipped in**: it used to say "two modes share an objective of zero, so without the non-zero dimension
        // full load and weight recommendation would be indistinguishable" — at that time weight recommendation's objective was identically 0 because **equalization never
        // reached the model** (added/ ). The three objectives now differ, so that claim is dead and **the half of the criterion that pinned it is dropped**;
        // leaving it in would keep describing a state that no longer exists. The non-zero dimension stays: it catches "the structure moved", which the objective does not.
        System.Linq.Enumerable.Select(Solutions, entry => (entry.Objective, entry.NonZero)).Should().OnlyHaveUniqueItems();
        System.Linq.Enumerable.Count(System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(Solutions, entry => entry.Objective)))
            .Should().Be(Solutions.Length,
            "all three objectives now differ; equalization and payload maximization reach the weight-recommendation model, so its objective is no longer identically zero");
    }
#endif
}
