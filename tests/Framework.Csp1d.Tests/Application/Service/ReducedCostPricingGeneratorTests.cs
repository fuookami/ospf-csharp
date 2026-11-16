#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Application.Service;

/// <summary>
/// `ReducedCostPricingGenerator` 必须**真的按对偶价格**决定要不要出列。
/// `ReducedCostPricingGenerator` must **actually decide by dual price** whether to emit a column.
/// </summary>
/// <remarks>
/// **实现前它只是转发给初始生成器**，`ShadowPrices` 与 `MaxGeneratedPlans` **一个都没读**。
/// 后果：每轮定价产出的都是初始方案 → 第一轮全部重复 → **以 `AllDuplicates` 立刻收敛**，
/// 列生成**从来没真正跑过定价**。
///
/// **这正是那种"接口、实现、统计、KPI 全都在，只有连接件是空壳"的缺陷形态**：
/// 类名、接口、构造参数都齐全，`Generate` 也确实返回 `IReadOnlyList<CuttingPlan<V>>`，
/// **只是那个返回值跟对偶价格毫无关系**。
///
/// 判据分三组：**对偶价格缺失/非正必须不出列**、**正价格必须出列**、**`MaxGeneratedPlans` 必须被尊重**。
///
/// **Before the implementation it merely delegated to the initial generator** — `ShadowPrices` and `MaxGeneratedPlans` were **not read at all**. Consequence: every
/// round's pricing produced the initial plans, so round one was all duplicates and it **terminated immediately with `AllDuplicates`** — column generation **never
/// actually priced anything**.
///
/// **This is exactly the "interfaces, implementations, statistics and KPIs all present, only the connecting piece is a shell" defect shape**: the class name, the
/// interface and the constructor parameter were all there, and `Generate` did return `IReadOnlyList<CuttingPlan<V>>` — **it just had nothing to do with dual prices**.
///
/// Three groups: **missing/non-positive dual price yields no column**, **a positive one does**, and **`MaxGeneratedPlans` is honoured**.
/// </remarks>
public class ReducedCostPricingGeneratorTests {
    /// <summary>只有名称、没有符号的单位 / A unit with a name but no symbol.</summary>
    private sealed class NameOnlyUnit : PhysicalUnit {
        private readonly DerivedQuantity _quantity;

        public NameOnlyUnit(DerivedQuantity quantity) => _quantity = quantity;

        public override string? Name => "name-only-unit";

        public override string? Symbol => null;

        public override DerivedQuantity Quantity => _quantity;

        public override UnitConversionRule ConversionRule => SIBaseUnits.Meter.ConversionRule;
    }

    /// <summary>没有名称也没有符号的单位 / A unit with neither a name nor a symbol.</summary>
    private sealed class NamelessUnit : PhysicalUnit {
        private readonly DerivedQuantity _quantity;

        public NamelessUnit(DerivedQuantity quantity) => _quantity = quantity;

        public override string? Name => null;

        public override string? Symbol => null;

        public override DerivedQuantity Quantity => _quantity;

        public override UnitConversionRule ConversionRule => SIBaseUnits.Meter.ConversionRule;
    }

    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial() =>
        new(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));

    private static Product<Flt64> MakeProduct(string id, double width) =>
        new(id: id, name: id, width: new List<Quantity<Flt64>> { Millimetres(width) });

    private static Csp1dPricingInput<Flt64> MakeInput(
        IReadOnlyList<ProductDemand<Flt64>> demands,
        ShadowPriceMap<Flt64> shadowPrices,
        ulong maxGeneratedPlans = 64UL) =>
        new(
            GenerationInput: new GenerationInput<Flt64> {
                Materials = new List<Material<Flt64>> { MakeMaterial() },
                Demands = demands,
            },
            ShadowPrices: shadowPrices,
            MaxGeneratedPlans: new UInt64(maxGeneratedPlans));

    private static ProductDemand<Flt64> MakeDemand(Product<Flt64> product) =>
        new(product, Millimetres(50.0));

    private static ShadowPriceMap<Flt64> PricesWith(string productId, double price) =>
        new(new Dictionary<Csp1dShadowPriceKey, Flt64> {
            [new ProductDemandShadowPriceKey(productId, "mm")] = new Flt64(price),
        });

    /// <summary>只产出一条带 `machineId` 方案的初始生成器 / An initial generator yielding exactly one plan carrying a `machineId`.</summary>
    /// <remarks>
    /// **必须替换初始生成器，不能只改输入**：定价器是**包装器**，方案来自 `_initialGenerator`，
    /// 而不是来自 `GenerationInput` 里的任何东西。**只改输入时那条带 `machineId` 的方案根本不会被枚举出来。**
    ///
    /// **The initial generator must be replaced, not merely the input**: the pricing generator is a **wrapper**, so plans come from `_initialGenerator` and not from
    /// anything in `GenerationInput`. **Changing only the input means the plan carrying the `machineId` is never enumerated.**
    /// </remarks>
    private sealed class MachinePlanGenerator : ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> {
        private readonly string _machineId;
        private readonly double _capacityConsumption;

        public MachinePlanGenerator(string machineId, double capacityConsumption) {
            _machineId = machineId;
            _capacityConsumption = capacityConsumption;
        }

        public IReadOnlyList<CuttingPlan<Flt64>> Generate(GenerationInput<Flt64> input) {
            Material<Flt64> material = input.Materials[0];
            var product = new Product<Flt64>(
                id: "A",
                name: "product-a",
                width: new List<Quantity<Flt64>> { Millimetres(50.0) });
            // **需求贡献不能省**：缺了它需求项为 0，收益只剩设备项，
            // 恰好等于成本 1 而**不严格大于**，判据会假红。
            // **The demand contribution cannot be omitted**: without it the demand term is 0 and the benefit is just the machine term, which equals the cost of 1 and
            // is **not strictly greater**, so the criterion fails spuriously.
            return new List<CuttingPlan<Flt64>> {
                new(
                    id: "priced-1",
                    material: material,
                    slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) },
                    machineId: _machineId,
                    demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                        new(product, Millimetres(50.0)),
                    },
                    capacityConsumption: _capacityConsumption > 0.0 ? Millimetres(_capacityConsumption) : null),
            };
        }
    }

    private static Csp1dPricingInput<Flt64> MakeInputWithMachine(
        IReadOnlyList<ProductDemand<Flt64>> demands,
        ShadowPriceMap<Flt64> shadowPrices,
        string machineId,
        double capacityConsumption = 0.0) =>
        new(
            GenerationInput: new GenerationInput<Flt64> {
                Materials = new List<Material<Flt64>> { MakeMaterial() },
                Demands = demands,
            },
            ShadowPrices: shadowPrices,
            MaxGeneratedPlans: new UInt64(64UL));

    private static ReducedCostPricingGenerator<Flt64> MakeMachinePricing(
        string machineId,
        double capacityConsumption) =>
        new(new MachinePlanGenerator(machineId, capacityConsumption));

    private static ReducedCostPricingGenerator<Flt64> MakePricing() =>
        new(new SimpleInitialGenerator<Flt64>());

    /// <summary>**对偶价格为正时必须出列** / A positive dual price must yield a column.</summary>
    [Fact]
    public void Generate_ShouldEmitAColumnWhenTheDualPriceIsPositive() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            PricesWith("A", 3.5));

        IReadOnlyList<CuttingPlan<Flt64>> plans = MakePricing().Generate(input);

        plans.Should().ContainSingle("a positive dual price makes the demand worth a column");
        plans[0].Slices.Should().ContainSingle();
        plans[0].Slices[0].Width.Should().Be(Millimetres(50.0));
    }

    /// <summary>**对偶价格缺失时不得出列** / A missing dual price must yield no column.</summary>
    /// <remarks>
    /// 这条钉住"**必须查对偶价格**"。旧实现不查，**无论有没有对偶价格都出列**——
    /// 所以只要有一条判据断言"缺价格时为空"，旧实现立刻红。
    ///
    /// This pins "**the dual price must be looked up**". The old implementation never looked, so it emitted a column **whether or not** a price existed — any criterion
    /// asserting "empty when the price is missing" is therefore red against it.
    /// </remarks>
    [Fact]
    public void Generate_ShouldEmitNothingWhenTheDualPriceIsMissing() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            new ShadowPriceMap<Flt64>());

        MakePricing().Generate(input).Should().BeEmpty();
    }

    /// <summary>**对偶价格为零或负时不得出列** / A zero or negative dual price must yield no column.</summary>
    /// <remarks>
    /// **零与负必须分开测**：`> 0` 与 `>= 0` 只差一个字符，但语义完全不同——
    /// 零价格意味着"这条需求已经不再改善目标"，正是**收敛的信号**。
    ///
    /// **Zero and negative must be tested separately**: `> 0` versus `>= 0` differs by one character but not in meaning — a zero price means "this demand no longer
    /// improves the objective", which is precisely **the convergence signal**.
    /// </remarks>
    [Fact]
    public void Generate_ShouldEmitNothingWhenTheDualPriceIsZeroOrNegative() {
        Product<Flt64> zeroProduct = MakeProduct("A", 50.0);
        MakePricing().Generate(MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(zeroProduct) },
            PricesWith("A", 0.0))).Should().BeEmpty("a zero dual price is the convergence signal");

        Product<Flt64> negativeProduct = MakeProduct("A", 50.0);
        MakePricing().Generate(MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(negativeProduct) },
            PricesWith("A", -2.0))).Should().BeEmpty("a negative dual price must not add a column");
    }

    /// <summary>**只有名称的单位必须用名称作键** / A name-only unit must key on its name.</summary>
    /// <remarks>
    /// **变异测试发现的缺口**：把单位键写成 `Symbol ?? ToString`（**丢掉 name 回退**）时判据全绿——
    /// 因为当时唯一的自定义单位**既无 name 也无 symbol**，两条路径在它身上**结果相同**。
    ///
    /// 回退顺序是 `symbol ?: name ?: toString`。**只测"都没有"的那种单位，
    /// 无法区分"按顺序回退"与"跳过 name 直接 toString"**——两者在没有 name 时一致。
    ///
    /// 这条用一个**有 name、无 symbol** 的单位，把对偶价格存在 `"name-only-unit"` 下。
    /// 若实现跳过 name 回退，会去查 `ToString` 的渲染值，**查不到**。
    ///
    /// **这条判据原本是为了杀死一个变异，结果发现那个变异是"语义等价"的。**
    /// 把单位键写成 `Symbol ?? ToString`（丢掉显式的 name 回退）**仍然全绿**，原因是
    /// `PhysicalUnit.ToString` **自己就是 `Symbol ?? Name ?? "量纲(比例)"`**——显式回退与经由 `ToString` 回退**结果相同**。
    ///
    /// **一个"杀不死"的变异有两种可能**：判据弱，或**变异本身不改变语义**。这里是后者。
    /// 我先前按"变异存活 = 判据有洞"处理，这次不成立；**判据保留仍是必要的**（它钉住了 name 回退的具体取值）。
    ///
    /// **本段最初还写过一条"真实隐患"，那是错的，已更正。** 原文称
    /// `DerivedPhysicalUnit.ToString` 是 `Symbol ?? ""`——**归属错了**：`DerivedPhysicalUnit`
    /// **并不覆盖 `ToString`**，它继承基类的三回退。写成 `Symbol ?? ""` 的是 **`AnonymousPhysicalUnit`**，
    /// 而那与基线**逐字相同**（基线 `override fun toString = symbol ?: ""`）。
    ///
    /// 所以"匿名单位无 symbol 无 name 时键为空串、两个不同单位共键"**是基线行为，不是偏离**。
    /// 这一点现在由 `UnitKeyConvergenceTests.AnonymousUnit_ShouldKeyAsTheBaselineDoes` 钉住。
    ///
    /// **This paragraph also once claimed a "real hazard" that was wrong and is corrected here.** It said `DerivedPhysicalUnit.ToString` was `Symbol ?? ""` — **wrong
    /// attribution**: `DerivedPhysicalUnit` **does not override `ToString`** and inherits the base's three-step fallback. The `Symbol ?? ""` version belongs to
    /// **`AnonymousPhysicalUnit`**, and that is **byte-for-byte identical** to the code (`override fun toString = symbol ?: ""`).
    ///
    /// So "a symbol-less, nameless anonymous unit keys to an empty string, letting two distinct units collide" is **baseline behaviour, not a divergence**. It is now
    /// pinned by `UnitKeyConvergenceTests.AnonymousUnit_ShouldKeyAsTheBaselineDoes`.
    ///
    /// **The criterion was written to kill a mutant that turned out to be semantically equivalent.** Writing the unit key as `Symbol ?? ToString` (**dropping the
    /// explicit name fallback**) **stays green**, because `PhysicalUnit.ToString` **is itself `Symbol ?? Name ?? "dimension(scale)"`** — the explicit fallback and
    /// the via-`ToString` fallback **agree**.
    ///
    /// **A mutant that cannot be killed has two explanations**: a weak criterion, or **the mutation does not change semantics**. This is the latter. I had been
    /// treating "the mutant survived" as "the criterion has a hole"; that does not hold here. **The criterion is still worth keeping** (it pins exactly what the name
    /// fallback yields).
    ///
    /// **A real hazard found along the way**: `DerivedPhysicalUnit.ToString` is `Symbol ?? ""` — it **neither falls back to the name nor renders the dimension**.
    /// So for a symbol-less, nameless derived unit, `GenerationQuantityMath.UnitKey` returns **an empty string**, making distinct derived units **share one key**. This
    /// path **has no case constructed** in (building a minimal derived unit is costly); recorded as a gap.
    /// </remarks>
    [Fact]
    public void Generate_ShouldKeyOnTheNameWhenTheUnitHasNoSymbol() {
        var unit = new NameOnlyUnit(SIBaseUnits.Meter.Quantity);
        var product = new Product<Flt64>(
            id: "A",
            name: "A",
            width: new List<Quantity<Flt64>> { new(new Flt64(50.0), unit) });
        var demand = new ProductDemand<Flt64>(product, new Quantity<Flt64>(new Flt64(50.0), unit));
        var prices = new Dictionary<Csp1dShadowPriceKey, Flt64> {
            [new ProductDemandShadowPriceKey("A", "name-only-unit")] = new Flt64(2.0),
        };
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: new Quantity<Flt64>(new Flt64(100.0), unit),
                    UpperBound: new Quantity<Flt64>(new Flt64(200.0), unit)),
                Step: new Quantity<Flt64>(new Flt64(1.0), unit)));
        var input = new Csp1dPricingInput<Flt64>(
            GenerationInput: new GenerationInput<Flt64> {
                Materials = new List<Material<Flt64>> { material },
                Demands = new List<ProductDemand<Flt64>> { demand },
            },
            ShadowPrices: new ShadowPriceMap<Flt64>(prices),
            MaxGeneratedPlans: new UInt64(64UL));

        MakePricing().Generate(input).Should().ContainSingle(
            "the name is the second fallback, so the lookup must use \"name-only-unit\"");
    }

    /// <summary>**单位键必须与基线 `shadowPriceUnitSymbol` 一致（含第三条回退）** / The unit key must match its `shadowPriceUnitSymbol`, third fallback included.</summary>
    /// <remarks>
    /// 基线：`unit.symbol ?: unit.name ?: unit.toString`。
    ///
    /// **这里曾经写成 `Symbol ?? Name ?? string.Empty`**——前两条一致，**第三条落空串**。
    /// 后果：所有"既无 symbol 又无 name"的单位**共用一个键**，`(产品, "")` 会让**不同单位的对偶价格互相命中**，
    /// 于是定价拿错价格、把不该出的列出来。
    ///
    /// 判据用**一个无 symbol、无 name 的自定义单位**，把对偶价格按 `GenerationQuantityMath.UnitKey(unit)`
    /// 的正确取值存入；若实现落空串，就**查不到**这条价格、返回空。
    ///
    /// The code is `unit.symbol ?: unit.name ?: unit.toString`.
    ///
    /// **This was once written as `Symbol ?? Name ?? string.Empty`** — the first two fallbacks agree, **the third lands on an empty string**. Consequence: every unit
    /// with neither symbol nor name **shares one key**, so `(product, "")` makes **dual prices of different units match each other**, pricing picks the wrong price and
    /// emits columns it should not.
    ///
    /// The criterion uses **a custom unit with neither symbol nor name**, storing the dual price under the correct `GenerationQuantityMath.UnitKey(unit)` value; an
    /// implementation landing on an empty string would **fail to find** it and return empty.
    /// </remarks>
    [Fact]
    public void Generate_ShouldKeyTheUnitAsTheBaselineDoesIncludingTheThirdFallback() {
        var unit = new NamelessUnit(SIBaseUnits.Meter.Quantity);
        var product = new Product<Flt64>(
            id: "A",
            name: "A",
            width: new List<Quantity<Flt64>> { new(new Flt64(50.0), unit) });
        var demand = new ProductDemand<Flt64>(product, new Quantity<Flt64>(new Flt64(50.0), unit));
        var prices = new Dictionary<Csp1dShadowPriceKey, Flt64> {
            [new ProductDemandShadowPriceKey("A", GenerationQuantityMath.UnitKey(unit))] = new Flt64(2.0),
        };
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: new Quantity<Flt64>(new Flt64(100.0), unit),
                    UpperBound: new Quantity<Flt64>(new Flt64(200.0), unit)),
                Step: new Quantity<Flt64>(new Flt64(1.0), unit)));
        var input = new Csp1dPricingInput<Flt64>(
            GenerationInput: new GenerationInput<Flt64> {
                Materials = new List<Material<Flt64>> { material },
                Demands = new List<ProductDemand<Flt64>> { demand },
            },
            ShadowPrices: new ShadowPriceMap<Flt64>(prices),
            MaxGeneratedPlans: new UInt64(64UL));

        MakePricing().Generate(input).Should().ContainSingle(
            "the dual price is stored under the UnitKey value, so the lookup must use it too");
    }

    /// <summary>**池里已有的方案由列生成侧过滤，定价器不过滤** / Plans already in the pool are filtered on the column-generation side, not in the pricing generator.</summary>
    /// <remarks>
    /// **这条判据被反转过一次，值得记下。**
    ///
    /// 加了"定价器过滤池里已有方案"，判据断言"已有方案不出列"。
    /// 之后发现**这个过滤放在 `deduplicatePlans`（列生成侧）**，定价器只做候选内部去重。
    /// 两者的可观测差别是终止原因：
    ///
    /// | 过滤位置 | 定价报 | 终止原因 |
    /// | --- | --- | --- |
    /// | **定价器里** | 0 条 | `PricingConverged` |
    /// | **列生成侧**（基线） | N 条 | `AllDuplicates` |
    ///
    /// 所以**定价器必须报出**池里已有的方案——过滤是下游的事。判据随之反转。
    ///
    /// 下面那条 `Generate_ShouldCollapseDuplicateCandidates` 保留，它管的是**候选内部**的重复。
    ///
    /// **This criterion was once inverted, which is worth recording.**
    ///
    /// added "the pricing generator filters plans already in the pool" together with a criterion asserting "an existing plan yields nothing". Later it turned out
    /// **the code puts that filter in `deduplicatePlans` (the column-generation side)**, with the pricing generator only deduping among candidates. The observable
    /// difference is the termination reason (table above).
    ///
    /// So **the pricing generator must report plans already in the pool** — filtering is downstream. The criterion is inverted accordingly.
    ///
    /// The `Generate_ShouldCollapseDuplicateCandidates` criterion below stays; it covers duplicates **within the candidates**.
    /// </remarks>
    [Fact]
    public void Generate_ShouldStillReportPlansAlreadyInThePool() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            PricesWith("A", 0.05));
        IReadOnlyList<CuttingPlan<Flt64>> initial = new SimpleInitialGenerator<Flt64>()
            .Generate(input.GenerationInput);
        initial.Should().ContainSingle("the fixture relies on exactly one initial plan");

        var withExisting = new Csp1dPricingInput<Flt64>(
            GenerationInput: new GenerationInput<Flt64> {
                Materials = input.GenerationInput.Materials,
                Demands = input.GenerationInput.Demands,
                Costars = input.GenerationInput.Costars,
                ExistingPlans = initial,
            },
            ShadowPrices: input.ShadowPrices,
            MaxGeneratedPlans: input.MaxGeneratedPlans);

        MakePricing().Generate(withExisting).Should().ContainSingle(
            "the pricing generator reports candidates; the column-generation side dedupes them away");
    }

    /// <summary>**去重必须发生在截断之前** / Deduplication must happen before truncation.</summary>
    /// <remarks>
    /// **顺序错误的代价是"名额被浪费"**：候选是 `[A, A, B]`、上限 **2** 时——
    ///
    /// | 顺序 | 结果 |
    /// | --- | --- |
    /// | **先截断再去重** | 截断留下 `[A, A]` → 去重后**只剩 1 条** |
    /// | **先去重再截断** | 去重得 `[A, B]` → **留下 2 条** |
    ///
    /// **上限必须是 2 而不是 1**：上限 1 时两种顺序都只留 1 条，**判据没有区分力**——
    /// 最初就写了 1，**变异存活**，这是判据设计错误而不是实现错误。
    ///
    /// **注意：本判据钉的是"去重生效"，不是"顺序"**。当前实现是**全员去重后再截断**，
    /// 所以"去掉 `seenKeys`"这个变异**恰好等价**——它只会让 `[A, A, B]` 变成 `[A, A]`，
    /// 再截断成 2 条，**输出仍是 2**。顺序在这个夹具下观察不到。
    ///
    /// **要真正钉住顺序，需要"重复项排在前、未重复项排在后"且上限恰好卡在中间**——
    /// 而那要求初始生成器的产出顺序可控。这里**没有做到**，记入缺口。
    ///
    /// 夹具：三个需求分别指向 A、A、B（**同一产品对象重复一次**），初始生成器产出 3 条，
    /// 规范化后是 `[A, A, B]`。
    ///
    /// **The cost of the wrong order is "wasted slots"**: with candidates `[A, A, B]` and a limit of **2** (table above).
    ///
    /// **The limit must be 2, not 1**: with 1 both orders keep a single column and **the criterion has no discriminating power** — my first version used 1 and the
    /// **mutant survived**, which is a criterion-design error rather than an implementation error.
    ///
    /// The fixture: three demands pointing at A, A and B (**the same product object twice**), so the initial generator yields 3 plans and canonicalisation gives
    /// `[A, A, B]`.
    /// </remarks>
    [Fact]
    public void Generate_ShouldCollapseDuplicateCandidates() {
        Product<Flt64> productA = MakeProduct("A", 50.0);
        Product<Flt64> productB = MakeProduct("B", 50.0);
        var demands = new List<ProductDemand<Flt64>> {
            MakeDemand(productA),
            MakeDemand(productA),
            MakeDemand(productB),
        };
        var prices = new Dictionary<Csp1dShadowPriceKey, Flt64> {
            [new ProductDemandShadowPriceKey("A", "mm")] = new Flt64(0.05),
            [new ProductDemandShadowPriceKey("B", "mm")] = new Flt64(0.05),
        };
        Csp1dPricingInput<Flt64> input = MakeInput(
            demands,
            new ShadowPriceMap<Flt64>(prices),
            maxGeneratedPlans: 2UL);

        IReadOnlyList<CuttingPlan<Flt64>> plans = MakePricing().Generate(input);

        plans.Should().HaveCount(2,
            "deduplicating [A, A, B] yields [A, B], so both slots can be filled");
    }

    /// <summary>**上限截断时必须留下收益最高的列，而不是枚举顺序的前几条** / Truncation must keep the highest-benefit columns, not the first ones enumerated.</summary>
    /// <remarks>
    /// **"先排序、再取上限"**，C# 旧实现是**按枚举顺序取上限**。上限截断时，**排序决定了哪几条被留下**。
    ///
    /// 夹具设计：三个产品 A / B / C，**按枚举顺序 A < B < C 的收益递增**（A=1.5、B=2.5、C=3.5），
    /// 上限 **2**。
    ///
    /// - **正确**：留下 **B 与 C**（收益最高的两个）
    /// - **旧实现**：留下 **A 与 B**（枚举顺序的前两个）
    ///
    /// 断言"**恰好留下 B 和 C**"而不是"数量是 2"——**只断言数量时旧实现也通过**。
    ///
    /// **The code sorts first, then takes the limit**; the old C# implementation took the limit **in enumeration order**. When the limit truncates, **the sort
    /// decides which columns survive**.
    ///
    /// The fixture uses three products A / B / C with **increasing benefit in enumeration order A < B < C** (1.5, 2.5, 3.5) and a limit of **2**.
    ///
    /// - **Correct**: keep **B and C** (the two highest benefits)
    /// - **Old implementation**: keep **A and B** (the first two in enumeration order)
    ///
    /// It asserts "**exactly B and C**" rather than "the count is 2" — **asserting only the count lets the old implementation pass**.
    /// </remarks>
    [Fact]
    public void Generate_ShouldKeepTheHighestBenefitColumnsWhenTheLimitTruncates() {
        var demands = new List<ProductDemand<Flt64>>();
        var prices = new Dictionary<Csp1dShadowPriceKey, Flt64>();
        // 贡献量都是 50，对偶价 0.03 / 0.05 / 0.07 → 收益 1.5 / 2.5 / 3.5
        foreach ((string id, double price) in new[] { ("A", 0.03), ("B", 0.05), ("C", 0.07) }) {
            Product<Flt64> product = MakeProduct(id, 50.0);
            demands.Add(MakeDemand(product));
            prices[new ProductDemandShadowPriceKey(id, "mm")] = new Flt64(price);
        }

        IReadOnlyList<CuttingPlan<Flt64>> plans = MakePricing().Generate(
            MakeInput(demands, new ShadowPriceMap<Flt64>(prices), maxGeneratedPlans: 2UL));

        plans.Should().HaveCount(2);
        plans.Should().OnlyContain(
            plan => plan.Slices[0].Production.Id == "B" || plan.Slices[0].Production.Id == "C",
            "the limit must keep the two highest-benefit columns (B and C), not the first two enumerated (A and B)");
    }

    /// <summary>**收益修正器必须按顺序折叠** / Benefit modifiers must fold in order.</summary>
    /// <remarks>
    /// 夹具：需求收益 0.5（**低于成本 1**）。两个修正器 `+0.3`、`+0.4` → **1.2 > 1，出列**。
    ///
    /// **顺序折叠意味着每一步的输出是下一步的输入**；若实现只应用第一个（`0.8`），判据红。
    ///
    /// The fixture: demand benefit 0.5 (**below cost 1**). Two modifiers `+0.3`, `+0.4` → **1.2 > 1, emit**. **Folding in order means each step's output feeds the
    /// next**; an implementation applying only the first stops at 0.8 and goes red.
    /// </remarks>
    [Fact]
    public void Generate_ShouldFoldBenefitModifiersInOrder() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            PricesWith("A", 0.01)) with {
            BenefitModifiers = new List<global::System.Func<CuttingPlan<Flt64>, Flt64, Flt64>> {
                (_, b) => new Flt64(b.ToDouble() + 0.3),
                (_, b) => new Flt64(b.ToDouble() + 0.4),
            },
        };

        MakePricing().Generate(input).Should().ContainSingle(
            "0.5 + 0.3 + 0.4 is 1.2, above the cost of 1");
    }

    /// <summary>**成本修正器必须按顺序折叠** / Cost modifiers must fold in order.</summary>
    /// <remarks>
    /// **反向**：需求收益 `2.5`（**高于成本 1，本该出列**）。两个成本修正器 `+0.7`、`+0.9`
    /// → 成本 `2.6 > 2.5`，**不出列**。若实现漏掉第二个（成本 1.7），判据红。
    ///
    /// **The reverse**: demand benefit `2.5` (**above cost 1, so it should emit**). Two cost modifiers `+0.7`, `+0.9` → cost `2.6 > 2.5`, so **no column**. An
    /// implementation skipping the second stops at 1.7 and goes red.
    /// </remarks>
    [Fact]
    public void Generate_ShouldFoldCostModifiersInOrder() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            PricesWith("A", 0.05)) with {
            CostModifiers = new List<global::System.Func<CuttingPlan<Flt64>, Flt64, Flt64>> {
                (_, c) => new Flt64(c.ToDouble() + 0.7),
                (_, c) => new Flt64(c.ToDouble() + 0.9),
            },
        };

        MakePricing().Generate(input).Should().BeEmpty(
            "cost 1 + 0.7 + 0.9 is 2.6, above the benefit of 2.5");
    }

    /// <summary>**判据返回 null 必须继续问下一条** / A judge returning null must let the next judge decide.</summary>
    /// <remarks>
    /// **这是三值语义的核心判据。** 第一条判据**只管产品 B**（这里返回 `null`），
    /// 第二条返回 `true`。若实现把 `null` 当成 `false`，方案会被判成不改善、**不出列**。
    ///
    /// 收益只有 `0.5`（**默认判据会拒绝**），所以这条判据**必须靠自定义判据通过**——
    /// 这就同时钉住了"自定义判据优先于默认"。
    ///
    /// **This is the core criterion for the three-valued semantics.** The first judge **only handles product B** (so it returns `null` here) and the second returns
    /// `true`. Treating `null` as `false` rejects the plan and **emits nothing**.
    ///
    /// The benefit is only `0.5` (**the default would reject**), so passing **requires the custom judge** — which also pins "custom judges take precedence over the
    /// default".
    /// </remarks>
    [Fact]
    public void Generate_ShouldContinueToTheNextJudgeWhenOneReturnsNull() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            PricesWith("A", 0.01)) with {
            ImprovingJudges = new List<global::System.Func<CuttingPlan<Flt64>, Flt64, Flt64, bool?>> {
                (plan, _, _) => plan.Slices[0].Production.Id == "B" ? true : null,
                (_, _, _) => true,
            },
        };

        MakePricing().Generate(input).Should().ContainSingle(
            "the first judge abstains and the second accepts");
    }

    /// <summary>**全部判据不表态时必须回退默认逻辑** / All judges abstaining must fall back to the default.</summary>
    /// <remarks>
    /// **反向判据**：两条判据都返回 `null`，收益 `0.5 < 1` → **默认逻辑拒绝，不出列**。
    /// 若实现把"全不表态"当成"接受"，会错误出列。
    ///
    /// **The reverse criterion**: both judges return `null` and the benefit `0.5 < 1` → **the default rejects, no column**. An implementation treating "all abstain" as
    /// "accept" would emit wrongly.
    /// </remarks>
    [Fact]
    public void Generate_ShouldFallBackToTheDefaultWhenAllJudgesAbstain() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            PricesWith("A", 0.01)) with {
            ImprovingJudges = new List<global::System.Func<CuttingPlan<Flt64>, Flt64, Flt64, bool?>> {
                (_, _, _) => null,
                (_, _, _) => null,
            },
        };

        MakePricing().Generate(input).Should().BeEmpty(
            "all judges abstained, so the default benefit > cost rejects 0.5");
    }

    /// <summary>**物料用量对偶价必须计入收益** / The material-usage dual must count toward the benefit.</summary>
    /// <remarks>
    /// **时这条判据写不出来**：物料项根本没实现，`MaterialUsageShadowPriceKey` **只有生产方没有消费方**。
    ///
    /// 夹具：需求收益只有 `50 × 0.01 = 0.5`（**低于成本 1，本该不出列**），
    /// 但物料 `M1` 的对偶价是 `1.0`——**加起来 1.5 > 1，必须出列**。
    ///
    /// **这条判据的鉴别力来自"单靠需求项不够"**：若实现漏掉物料项，收益停在 0.5，判据红。
    ///
    /// **This criterion could not be written**: the material term did not exist and `MaterialUsageShadowPriceKey` had **only a producer, no consumer**.
    ///
    /// The fixture: the demand benefit is only `50 × 0.01 = 0.5` (**below cost 1, so it should not emit**), but material `M1`'s dual is `1.0` — **totalling 1.5 > 1, so it
    /// must emit**.
    ///
    /// **Its discriminating power comes from "the demand term alone is not enough"**: an implementation that drops the material term stops at 0.5 and goes red.
    /// </remarks>
    [Fact]
    public void Generate_ShouldCountTheMaterialUsageDualTowardsTheBenefit() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        var prices = new Dictionary<Csp1dShadowPriceKey, Flt64> {
            [new ProductDemandShadowPriceKey("A", "mm")] = new Flt64(0.01),
            [new MaterialUsageShadowPriceKey("M1")] = new Flt64(1.0),
        };
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            new ShadowPriceMap<Flt64>(prices));

        MakePricing().Generate(input).Should().ContainSingle(
            "0.5 from the demand plus 1.0 from the material clears the cost of 1");
    }

    /// <summary>**设备批次对偶价必须计入收益** / The machine-batch dual must count toward the benefit.</summary>
    /// <remarks>
    /// 与上一条同形，但**走的是另一条分支**（`plan.MachineId`）。方案必须带 `machineId`，
    /// 否则 `MachineBatchShadowPriceKey` 那一段**根本不执行**。
    ///
    /// Same shape as the previous one but **a different branch** (`plan.MachineId`). The plan must carry a `machineId`, or the
    /// `MachineBatchShadowPriceKey` block **never runs**.
    /// </remarks>
    [Fact]
    public void Generate_ShouldCountTheMachineBatchDualTowardsTheBenefit() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        var prices = new Dictionary<Csp1dShadowPriceKey, Flt64> {
            [new ProductDemandShadowPriceKey("A", "mm")] = new Flt64(0.01),
            [new MachineBatchShadowPriceKey("MC1")] = new Flt64(1.0),
        };
        Csp1dPricingInput<Flt64> input = MakeInputWithMachine(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            new ShadowPriceMap<Flt64>(prices),
            "MC1");

        MakeMachinePricing("MC1", 0.0).Generate(input).Should().ContainSingle(
            "0.5 from the demand plus 1.0 from the machine batch clears the cost of 1");
    }

    /// <summary>**设备产能对偶价 × 产能消耗必须计入收益** / The machine-capacity dual times the capacity consumption must count.</summary>
    /// <remarks>
    /// **这项是乘积，不是加数**：`capacityConsumption × machineCapacityDual`。
    /// 若实现漏掉乘数（只加对偶价），`1.0 × 2.0 = 2.0` 会变成 `2.0`——**恰好相同**，
    /// 所以夹具刻意让两者不等：产能消耗 `0.5`、对偶价 `2.0` → 乘积 `1.0`，
    /// 而"只加对偶价"会得到 `2.0`。**再用需求项 0.5 卡住**：
    /// 乘积路径 `0.5 + 1.0 = 1.5 > 1` 出列；漏乘数路径 `0.5 + 2.0 = 2.5 > 1` **也出列**——
    /// 所以还要补一条**反向**判据（见下一条测试类）。
    ///
    /// **This term is a product, not an addend**: `capacityConsumption × machineCapacityDual`. An implementation that drops the multiplier (adding only the dual)
    /// would turn `1.0 × 2.0 = 2.0` into `2.0` — **coincidentally the same**, so the fixture deliberately makes them differ: consumption `0.5`, dual `2.0` → product `1.0`,
    /// while "add the dual only" yields `2.0`. The demand term of 0.5 then pins the threshold: the product path gives `0.5 + 1.0 = 1.5 > 1` and emits, while the
    /// missing-multiplier path gives `0.5 + 2.0 = 2.5 > 1` and **also emits** — hence a **reverse** criterion is needed (next test class).
    /// </remarks>
    [Fact]
    public void Generate_ShouldCountTheMachineCapacityDualTimesTheConsumption() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        var prices = new Dictionary<Csp1dShadowPriceKey, Flt64> {
            [new ProductDemandShadowPriceKey("A", "mm")] = new Flt64(0.01),
            [new MachineCapacityShadowPriceKey("MC1")] = new Flt64(2.0),
        };
        Csp1dPricingInput<Flt64> input = MakeInputWithMachine(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            new ShadowPriceMap<Flt64>(prices),
            "MC1",
            capacityConsumption: 0.5);

        MakeMachinePricing("MC1", 0.5).Generate(input).Should().ContainSingle(
            "0.5 from the demand plus 0.5 × 2.0 from the capacity clears the cost of 1");
    }

    /// <summary>**产能项必须是乘积，不能用"只加对偶价"的近似** / The capacity term must be a product, not the "add the dual only" shortcut.</summary>
    /// <remarks>
    /// **变异测试发现的缺口**：把 `consumption × dual` 改成 `dual` 时判据**全部通过**——
    /// 因为既有判据只问"**出列了吗**"，而两条路径都超过成本 `1`。
    ///
    /// 夹具让两者**数值不同**：产能消耗 `0.5`、对偶价 `2.0`。
    ///
    /// | 路径 | 收益 |
    /// | --- | --- |
    /// | 正确（`0.5 × 2.0`） | `0.5 + 1.0 = 1.5` |
    /// | 漏乘数（`2.0`） | `0.5 + 2.0 = 2.5` |
    ///
    /// **两者都出列**，所以要**直接断言收益**——这需要把 `ComputeDualBenefit` 暴露成可测的。
    /// 这里把它改为 `internal static`，测试用 `InternalsVisibleTo` 可见。
    ///
    /// **A gap found by mutation testing**: changing `consumption × dual` to `dual` left every criterion green — the existing ones only ask "**did it emit**", and both
    /// paths exceed the cost of 1.
    ///
    /// The fixture makes them **numerically different**: consumption `0.5`, dual `2.0` (table above). **Both emit**, so the criterion must **assert the benefit directly**,
    /// which requires exposing `ComputeDualBenefit`; makes it `internal static`, visible to tests.
    /// </remarks>
    [Fact]
    public void ComputeDualBenefit_ShouldMultiplyTheConsumptionByTheCapacityDual() {
        Material<Flt64> material = MakeMaterial();
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) });
        var plan = new CuttingPlan<Flt64>(
            id: "p",
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) },
            machineId: "MC1",
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(50.0)),
            },
            capacityConsumption: Millimetres(0.5));
        var prices = new ShadowPriceMap<Flt64>(new Dictionary<Csp1dShadowPriceKey, Flt64> {
            [new ProductDemandShadowPriceKey("A", "mm")] = new Flt64(0.01),
            [new MachineCapacityShadowPriceKey("MC1")] = new Flt64(2.0),
        });

        double benefit = ReducedCostPricingGenerator<Flt64>.ComputeDualBenefitForTest(plan, prices);

        // 0.5（需求） + 0.5 × 2.0（产能） = 1.5；漏乘数会得到 2.5
        benefit.Should().BeApproximately(1.5, 1e-9,
            "the capacity term must be consumption × dual, not the dual alone");
    }

    /// <summary>**没有 machineId 时设备两项都不得计入** / Without a machineId neither machine term may count.</summary>
    /// <remarks>
    /// **反向判据**：给同样的两个设备对偶价，但方案**不带 `machineId`**——收益只剩 `0.5`，
    /// **必须不出列**。若实现不看 `machineId` 就加设备项，会得到 `1.5` 并错误出列。
    ///
    /// **The reverse criterion**: with the same two machine duals but **no `machineId`** on the plan, the benefit is only `0.5` and it **must not emit**. An implementation
    /// that adds machine terms without checking `machineId` would reach `1.5` and emit wrongly.
    /// </remarks>
    [Fact]
    public void Generate_ShouldIgnoreTheMachineDualsWhenThePlanHasNoMachineId() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        var prices = new Dictionary<Csp1dShadowPriceKey, Flt64> {
            [new ProductDemandShadowPriceKey("A", "mm")] = new Flt64(0.01),
            [new MachineBatchShadowPriceKey("MC1")] = new Flt64(1.0),
            [new MachineCapacityShadowPriceKey("MC1")] = new Flt64(2.0),
        };
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            new ShadowPriceMap<Flt64>(prices));

        MakePricing().Generate(input).Should().BeEmpty(
            "without a machineId the machine duals must not count, leaving only 0.5");
    }

    /// <summary>**对偶收益不超过目标成本时不得出列** / A dual benefit not exceeding the objective cost must yield no column.</summary>
    /// <remarks>
    /// **这是"减成本"的实质判据**，也是 那版实现**没有**的一步。
    ///
    /// 把基线 `SimplePricingGenerator` 的语义（"对偶价为正就出列"）写进了
    /// **`ReducedCostPricingGenerator`** 这个类——**类名和语义对不上**。
    /// `ReducedCostPricingGenerator` 是**包装器**：enumerator 枚举候选，
    /// 再按 `benefit > objectiveCost` 过滤（`objectiveCost` 基础值是 **1**）。
    ///
    /// 判据用"**收益 0.5、成本 1**"：对偶价为正（`0.01 > 0`，旧实现会出列），
    /// 但收益 `50 × 0.01 = 0.5` **不超过 1**——**这正是旧实现与新实现唯一分歧的地方**。
    ///
    /// **This is the substantive "reduced cost" criterion**, and the step the earlier version **lacked**.
    ///
    /// wrote the code `SimplePricingGenerator` semantics ("emit when the dual price is positive") into the class named
    /// **`ReducedCostPricingGenerator`** — **name and semantics disagreed**. In the code `ReducedCostPricingGenerator` is a **wrapper**: the enumerator enumerates
    /// candidates, which are then filtered by `benefit > objectiveCost` (whose base is **1**).
    ///
    /// The criterion uses "**benefit 0.5, cost 1**": the dual price is positive (`0.01 > 0`, so the old implementation would emit), but the benefit
    /// `50 × 0.01 = 0.5` **does not exceed 1** — **exactly the only place the old and new implementations disagree**.
    /// </remarks>
    [Fact]
    public void Generate_ShouldEmitNothingWhenTheDualBenefitDoesNotExceedTheCost() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            PricesWith("A", 0.01));

        MakePricing().Generate(input).Should().BeEmpty(
            "50 units times 0.01 is 0.5, below the base objective cost of 1");
    }

    /// <summary>**对偶收益超过目标成本时必须出列** / A dual benefit above the objective cost must yield a column.</summary>
    /// <remarks>
    /// 与上一条**成对**：只测"收益不足时不出列"时，一个"永不出列"的实现也会通过。
    /// 这里收益 `50 × 0.05 = 2.5 > 1`，**必须出列**。
    ///
    /// **Paired with the previous one**: testing only "no column when the benefit is short" lets an "never emit" implementation pass. Here the benefit is
    /// `50 × 0.05 = 2.5 > 1`, so it **must** emit.
    /// </remarks>
    [Fact]
    public void Generate_ShouldEmitAColumnWhenTheDualBenefitExceedsTheCost() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            PricesWith("A", 0.05));

        MakePricing().Generate(input).Should().ContainSingle(
            "50 units times 0.05 is 2.5, above the base objective cost of 1");
    }

    /// <summary>**方案使用惩罚必须抬高成本** / A plan-usage penalty must raise the cost.</summary>
    /// <remarks>
    /// 收益固定 `50 × 0.05 = 2.5`。**不配置惩罚时成本 1 → 出列**；配置 `PlanUsagePenalty = 2`
    /// 后成本 `3` → **收益 2.5 不够，不出列**。
    ///
    /// **同一条收益、只差配置**，这正是"成本算进判据"的直接证据。
    /// 旧实现**完全没有配置项**，这条判据在它上面**根本写不出来**。
    ///
    /// The benefit is fixed at `50 × 0.05 = 2.5`. **Without a penalty the cost is 1 → emit**; with `PlanUsagePenalty = 2` the cost is `3` → **2.5 falls short, no
    /// column**.
    ///
    /// **Same benefit, only the config differs** — direct evidence that the cost feeds the test. The old implementation  **had no config at all**, so this
    /// criterion **could not even be written** against it.
    /// </remarks>
    [Fact]
    public void Generate_ShouldEmitNothingWhenAPlanUsagePenaltyPushesTheCostAboveTheBenefit() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        Csp1dPricingInput<Flt64> baseline = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            PricesWith("A", 0.05));
        Csp1dPricingInput<Flt64> penalised = baseline with {
            ObjectiveConfig = new Csp1dPricingObjectiveConfig<Flt64>(PlanUsagePenalty: new Flt64(2.0)),
        };

        MakePricing().Generate(baseline).Should().ContainSingle("with base cost 1 the benefit 2.5 wins");
        MakePricing().Generate(penalised).Should().BeEmpty("with cost 3 the benefit 2.5 no longer wins");
    }

    /// <summary>**按物料 id 的成本惩罚必须生效** / The per-material-id cost penalty must apply.</summary>
    /// <remarks>
    /// **这条钉住"按物料查表"这一步**：惩罚挂在 `M1` 上，方案用的正是 `M1`。
    /// 若实现查错了键（或压根不查表），成本仍是 `1`，判据会红。
    ///
    /// **This pins the "look up by material id" step**: the penalty is keyed on `M1`, which is exactly the material the plan uses. An implementation that looks up the
    /// wrong key (or does not look up at all) leaves the cost at `1` and the criterion goes red.
    /// </remarks>
    [Fact]
    public void Generate_ShouldApplyTheMaterialCostPenaltyForThePlansMaterial() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            PricesWith("A", 0.05)) with {
            ObjectiveConfig = new Csp1dPricingObjectiveConfig<Flt64>(
                MaterialCostPenalty: new Dictionary<string, Flt64> { ["M1"] = new Flt64(2.0) }),
        };

        MakePricing().Generate(input).Should().BeEmpty(
            "the plan uses material M1, whose penalty raises the cost to 3");
    }

    /// <summary>**惩罚不作用于别的物料** / A penalty must not apply to a different material.</summary>
    /// <remarks>
    /// **反向判据**：惩罚挂在 `M2` 上，方案用的是 `M1`——成本仍是 `1`，**必须出列**。
    /// 只测正向时，一个"忽略 id、只要表非空就加惩罚"的实现也会通过。
    ///
    /// **The reverse criterion**: the penalty is keyed on `M2` while the plan uses `M1`, so the cost stays `1` and it **must** emit. Testing only the positive direction
    /// would let an "ignore the id and add a penalty whenever the map is non-empty" implementation pass.
    /// </remarks>
    [Fact]
    public void Generate_ShouldNotApplyAPenaltyKeyedOnADifferentMaterial() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            PricesWith("A", 0.05)) with {
            ObjectiveConfig = new Csp1dPricingObjectiveConfig<Flt64>(
                MaterialCostPenalty: new Dictionary<string, Flt64> { ["M2"] = new Flt64(2.0) }),
        };

        MakePricing().Generate(input).Should().ContainSingle(
            "the penalty is keyed on M2 but the plan uses M1");
    }

    /// <summary>**`MaxGeneratedPlans` 必须被尊重** / `MaxGeneratedPlans` must be honoured.</summary>
    /// <remarks>
    /// 旧实现**从不读这个参数**，所以定价可能一次吐出远多于允许数量的列。
    /// 断言"恰好等于上限"而不是"不超过"，**防止实现对上限理解错误却仍然通过**。
    ///
    /// The old implementation **never read this parameter**, so pricing could emit far more columns than allowed. It asserts "exactly the limit" rather than "at most",
    /// **so an implementation that misreads the limit cannot pass**.
    /// </remarks>
    [Fact]
    public void Generate_ShouldRespectTheMaxGeneratedPlansLimit() {
        var demands = new List<ProductDemand<Flt64>>();
        var prices = new Dictionary<Csp1dShadowPriceKey, Flt64>();
        foreach (string id in new[] { "A", "B", "C" }) {
            Product<Flt64> product = MakeProduct(id, 50.0);
            demands.Add(MakeDemand(product));
            prices[new ProductDemandShadowPriceKey(id, "mm")] = new Flt64(1.0);
        }

        IReadOnlyList<CuttingPlan<Flt64>> plans = MakePricing().Generate(
            MakeInput(demands, new ShadowPriceMap<Flt64>(prices), maxGeneratedPlans: 2UL));

        plans.Should().HaveCount(2, "three demands are priced but the limit is two");
    }

    /// <summary>**定价出的列必须带需求贡献** / A priced column must carry its demand contribution.</summary>
    /// <remarks>
    /// **变异测试发现的缺口**：把 `demandContributions` 改成 `null` 时判据**全部通过**——
    /// 因为此前没有任何一条判据看需求贡献，只看了"有几条切片"。
    ///
    /// **需求贡献是列与需求之间的唯一连接件**：丢了它，下游的需求满足量统计会认为
    /// **这条列没有满足任何需求**，方案看起来可行、实际不可行。
    ///
    /// 断言"恰好一条、产品对得上、数量对得上"——三者缺一都可能让下游算错。
    ///
    /// **A gap found by mutation testing**: setting `demandContributions` to `null` left **every** criterion green — nothing looked at contributions, only at "how many
    /// slices".
    ///
    /// **The demand contribution is the only link between a column and its demand**: drop it and downstream demand-satisfaction accounting sees **a column that
    /// satisfies nothing**, so an apparently feasible plan is actually infeasible.
    ///
    /// It asserts "exactly one, same product, same quantity" — missing any one lets downstream compute wrongly.
    /// </remarks>
    [Fact]
    public void Generate_ShouldAttachTheDemandContributionToThePricedColumn() {
        Product<Flt64> product = MakeProduct("A", 50.0);
        ProductDemand<Flt64> demand = MakeDemand(product);
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { demand },
            PricesWith("A", 3.5));

        IReadOnlyList<CuttingPlan<Flt64>> plans = MakePricing().Generate(input);

        plans.Should().ContainSingle();
        plans[0].DemandContributions.Should().ContainSingle("the column must link back to its demand");
        plans[0].DemandContributions[0].Product.Id.Should().Be("A");
        plans[0].DemandContributions[0].Quantity.Should().Be(demand.Quantity);
    }

    /// <summary>**产品宽度超出物料可切范围时不得出列** / A product width outside the material range must yield no column.</summary>
    /// <remarks>
    /// 物料可切上限是 200mm；这里给一个 400mm 的产品宽度。若实现不查宽度，
    /// 会生成**物理上切不出来**的方案——而这类方案在下游才炸，**定位成本很高**。
    ///
    /// The material's cuttable upper bound is 200mm; this feeds a 400mm product width. An implementation that skips the width check produces **physically impossible**
    /// plans — which blow up downstream, where **diagnosis is expensive**.
    /// </remarks>
    [Fact]
    public void Generate_ShouldEmitNothingWhenTheProductWidthExceedsTheMaterial() {
        Product<Flt64> product = MakeProduct("A", 400.0);
        Csp1dPricingInput<Flt64> input = MakeInput(
            new List<ProductDemand<Flt64>> { MakeDemand(product) },
            PricesWith("A", 3.5));

        MakePricing().Generate(input).Should().BeEmpty();
    }
}
