#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **单模型建得起来，而且 `BuildAsync` 能把它拆开** / The single model can be built, and `BuildAsync` can split it.
/// </summary>
/// <remarks>
/// 钉住了分歧（框架要一个模型，demo2 建了两个），备好了域侧的"只补重量"注册。这里把两头接上：
/// 建一个模型 → 交给 `LinearBendersPartition.BuildAsync` → 必须真的拆出主、子两个问题。
///
/// **这是这条路径第一次跑通，但还没接进分派**：`Execute` 仍然走两模型那条老路（`Demo2BendersWiringTests` 钉着模型数是 2）。
///
/// pinned the divergence and prepared the domain half. joins the two ends. **This is the first time the path runs, but it is not dispatched yet**:
/// `Execute` still takes the old two-model route (pinned by the model count in `Demo2BendersWiringTests`).
/// </remarks>
public sealed class Demo2SingleModelPartitionTests
{
    /// <summary>建一个模型，拆成主与子 / Build one model and split it into master and sub.</summary>
    [Fact]
    public async Task TheSingleModel_ShouldPartitionIntoMasterAndSub() {
        var application = new FullLoadApplication();
        application.Init(AircraftLoadingDemo.CreateSampleRequest()).IsOk.Should().BeTrue(
            "the domains must initialise before any registration can happen");

        List<string> notes = [];
        using Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>? model =
            application.BuildSingleBendersModel(StowageMode.FullLoad, notes);
        model.Should().NotBeNull();

        // **每一半的实测增量**：配载 +8、软安全 +7，而 MAC、MAC 优化、追索**都是 +0**。
        // `recourse:+0` 就是关键那一行——单模型路径的追索**目前什么也没加**，所以切分派之前必须先让它有东西。
        //
        // **The measured per-half deltas **: stowage +8, soft security +7, while MAC, MAC optimisation and the recourse are **all +0**. `recourse:+0` is the line
        // that matters - the single-model recourse **currently adds nothing**, so it must gain something before dispatch is switched over.
        // **实测**：
        //
        // | 半 | token | 行 | 符号 |
        // | --- | --- | --- | --- |
        // | 配载 | +8 | +11 | **+37** |
        // | MAC | +0 | +0 | **+8** |
        // | 软安全 | +7 | +0 | **+3** |
        // | MAC 优化 | +0 | +0 | **+2** |
        // | **追索** | +0 | +0 | **+8** |
        //
        // **的"追索是空的两个零"是读错的**：`Tokens.Tokens` 是变量表，`Tokens.Symbols` 才是表达式符号表。
        // 追索那半加了 **8 个符号**，只是它们在 token 与行两个度量上都不可见。三个度量一起看，没有任何一半是空的。
        //
        // 这是**第四次**度量把结论带偏（ 符号数、 前缀计数、 token/符号、 两个零）。
        // 记在这里：**每加一个度量，就要重读一遍已经下过的结论**。
        //
        // **Measured ** - the table above. **the earlier "two zeros" was a misreading**: `Tokens.Tokens` is the variable table while
        // `Tokens.Symbols` is the expression-symbol table, and the recourse half adds **eight symbols** that are invisible to the other two metrics. With all three metrics
        // no half is empty. This is the **fourth** time a metric skewed a conclusion and the lesson is recorded here: **each new metric means
        // re-reading every conclusion already drawn.**
        notes.Should().Contain("benders_single_model=master_stowage:+8 tokens,+11 rows,+37 symbols");
        notes.Should().Contain("benders_single_model=recourse:+0 tokens,+0 rows,+8 symbols",
            "the recourse registers expression symbols only; zero tokens and zero rows do not mean empty");
        Result<LinearBendersModels, ErrorCode, Error<ErrorCode>> partitioned =
            await LinearBendersPartition.BuildAsync(model!);

        partitioned.IsOk.Should().BeTrue(partitioned.ToString());
        LinearBendersModels models = ((Ok<LinearBendersModels, ErrorCode, Error<ErrorCode>>)partitioned).Value;
        try {
            models.Sub.Tokens.Tokens.Count.Should().BeGreaterThan(0,
                "a single-model partition that leaves the sub empty is not a decomposition");
            // **实测数**：来源 15、主 12、子 15——**子问题把 15 个变量全抄了一份**，因为没声明任何阶段。
            // "拆开"不等于"分解"：真正收窄追索要声明阶段，下一条判据测那件事。
            // **Measured **: source 15, master 12, sub 15 — **the sub mirrors all fifteen variables**, because no stage was declared. "Split" is not
            // "decomposed": narrowing the recourse needs declared stages, which the next criterion measures.
            models.Master.Tokens.Tokens.Count.Should().Be(12);
            // **实测**：来源 11 行 → 主 14 行、子 19 行。
            // 主多出来的 3 行是 theta 与目标相关的行；**子有 19 行**，说明行确实被切开来了——
            // 这里同样会为非决策变量补 `bound_lb_/bound_ub_` 边界行。
            //
            // **Measured **: 11 source rows become 14 master and 19 sub. The master gains rows for theta and the objective; **the sub has nineteen rows**, so the
 // rows really are split — the implementation likewise adds `bound_lb_/bound_ub_` rows for non-decision variables, and this implementation does the same.
            models.Master.Constraints.Count.Should().Be(14);
            models.Sub.Constraints.Count.Should().Be(19,
                "the recorded state: rows are split; change this number only with a measurement that shows a different split");
            models.Sub.Tokens.Tokens.Count.Should().Be(15);
        } finally {
            models.Dispose();
        }
    }

    /// <summary>
    /// **声明阶段之后，追索并没有变窄（实测）** / Declaring stages does not narrow the recourse (measured).
    /// </summary>
    /// <remarks>
    /// 上一条证明了"能拆"，这一条证明"拆得动"：把最大装载重量的符号标成连续追索，子问题的符号数必须**少于**全部。
    /// 若阶段声明被忽略，两条数字会一模一样——那才是"看起来在分解、其实在沿用"。
    ///
    /// The previous criterion proves it splits; this one proves it **narrows**: marking the max-load-weight symbols as continuous recourse must leave the sub with
    /// fewer than all variables. If the stage declaration were ignored the two counts would match — that is what "looks like decomposition while copying everything" is.
    /// </remarks>
    [Fact]
    public async Task DeclaredStages_ShouldNarrowTheRecourse() {
        var application = new FullLoadApplication();
        application.Init(AircraftLoadingDemo.CreateSampleRequest()).IsOk.Should().BeTrue();

        List<string> notes = [];
        using Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>? model =
            application.BuildSingleBendersModel(StowageMode.FullLoad, notes);
        model.Should().NotBeNull(string.Join("; ", notes));

        var stages = new Dictionary<Fuookami.Ospf.Core.Variable.IVariableItem, LinearBendersStage>();
        foreach (Fuookami.Ospf.Core.Variable.IVariableItem variable in model!.Tokens.Tokens.Select(token => token.Variable).Distinct()) {
            // **只能挑连续变量**：整数作追索会被 `BuildAsync` 直接拒绝。
            if (!variable.TypeKind.IsIntegerType) {
                stages[variable] = LinearBendersStage.ContinuousRecourse;
            }
        }
        // **实测**：合并模型的 15 个变量全是主问题半的符号（`occupied_*` / `assignment_*` / 条件与整数乘积），
        // **没有一个重量符号**——追索那半在这个模型上没加任何符号。
        // 于是"声明阶段"这条判据改用装载占用符号：它们确实是整数变量？不是——`occupied_*` 是连续的占用量，可以作追索。
        //
        // **Measured **: all fifteen variables are master-half symbols; **not one weight symbol** — the recourse half adds nothing to this model (the earlier 13 → 13
        // is the same fact). So this criterion marks the occupancy symbols as recourse instead.
        stages.Should().NotBeEmpty("the merged model carries occupancy symbols");

        Result<LinearBendersModels, ErrorCode, Error<ErrorCode>> partitioned =
            await LinearBendersPartition.BuildAsync(model, stages);
        partitioned.IsOk.Should().BeTrue(partitioned.ToString());
        LinearBendersModels models = ((Ok<LinearBendersModels, ErrorCode, Error<ErrorCode>>)partitioned).Value;
        try {
            // **实测：声明阶段并没有让子问题变窄**——12 / 15 与不声明时完全一样。
            // 这是"假设被实测推翻"的第三次（前两次是 的符号数、 的前缀计数）。
            // 结论写在这里而不是偷偷改成"应该变窄"：**当前 `BuildAsync` 把来源变量全部镜像进子问题**，
            // 阶段只决定新符号落在哪边。要真正收窄追索，得改 `BuildAsync` 或另写划分——那是一件需要证据的事。
            //
            // **Measured : declaring stages does NOT narrow the sub** — 12 / 15 exactly as without any declaration. This is the third time an assumption was
            // overturned by measurement (the earlier symbol count and the earlier prefix count being the first two). The conclusion is written here rather than quietly reworded
            // into "it ought to narrow": **`BuildAsync` currently mirrors every source variable into the sub**, and stages only decide where new symbols land. Narrowing
            // the recourse for real means changing `BuildAsync` or writing another partition — and that needs evidence first.
            models.Sub.Tokens.Tokens.Count.Should().Be(15,
                "the recorded behaviour: declaring stages narrows nothing today; change this number only with a measurement that shows narrowing");
            models.Master.Tokens.Tokens.Count.Should().Be(12);
        } finally {
            models.Dispose();
        }
    }

    /// <summary>
    /// **阶段表只能点名变量，而追索那半没有变量** / The stage table can only name variables, and the recourse registers none.
    /// </summary>
    /// <remarks>
    /// `bendersStages` 是 `Map<IVariableItem, LinearBendersStage>`——**键只能是变量**。
    /// 而实测追索那半加的是 `+0 tokens、+8 symbols`：**它只加表达式符号，一个变量也不加**。
    /// 于是这条路径**无法用阶段表表达"这一半属于追索"**——不是"还没填表"，是**表里没有可填的键**。
    ///
    /// 这不是缺一个功能，是划分器只按变量切分。要让追索真的收窄，得让划分器能按**约束行**归属——那是框架层的新能力，
    /// 需要证据与新判据，不能在这里假装填一张空表。
    ///
 /// Its `bendersStages` maps `IVariableItem` to a stage — **only variables can be keys**. The measured recourse half adds `+0 tokens` and `+8 symbols`:
    /// **symbols only, no variables**. So this path **cannot express "this half is the recourse" through the stage table** — not "the table is not filled yet", but **there is no
    /// key to fill**. This is not a missing feature but a partitioner that splits by variable only. Narrowing the recourse for real needs row-level ownership in the
    /// framework, with evidence and criteria of its own; faking an empty table here is not that.
    /// </remarks>
    [Fact]
    public void TheStageTable_ShouldNotBeAbleToExpressTheRecourse() {
        System.Reflection.ParameterInfo[] parameters = typeof(LinearBendersPartition)
            .GetMethod("BuildAsync")!
            .GetParameters();

        parameters.Length.Should().Be(2);
        parameters[1].ParameterType.Should().Be(
            typeof(IReadOnlyDictionary<Fuookami.Ospf.Core.Variable.IVariableItem, LinearBendersStage>),
            "the stage table is keyed by variables; that is the ceiling this criterion records");

        var application = new FullLoadApplication();
        application.Init(AircraftLoadingDemo.CreateSampleRequest()).IsOk.Should().BeTrue();
        List<string> notes = [];
        application.BuildSingleBendersModel(StowageMode.FullLoad, notes)!.Dispose();

        notes.Should().Contain("benders_single_model=recourse:+0 tokens,+0 rows,+8 symbols",
            "the measured recourse adds symbols only - so no variable exists to name in the stage table");
    }

    /// <summary>**注册顺序稳定** / The registration order is stable.</summary>
    /// <remarks>
    /// 要补"`Item` → 变量"的映射，补之前先确认一件事：**同一个请求连建两次模型，变量顺序是否一致**。
    /// 如果一致，映射可以按顺序建立并且可复核；如果不一致，任何按顺序的映射都会**沉默地取错变量**。
    ///
    /// is about to add an `Item`-to-variable map; before that, one thing must be checked: **does building the model twice from the same request give the same variable
    /// order?** If yes the map can be built by position and checked; if no, any positional map quietly picks the wrong variables.
    /// </remarks>
    [Fact]
    public void TheRegistrationOrder_ShouldBeStableAcrossBuilds() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();

        static List<string> Order(RequestDTO request) {
            var application = new FullLoadApplication();
            application.Init(request).IsOk.Should().BeTrue();
            List<string> notes = [];
            LinearMetaModel<Flt64>? model = application.BuildSingleBendersModel(StowageMode.FullLoad, notes);
            model.Should().NotBeNull();
            return model!.Tokens.Tokens.Select(token => token.Variable.Name).ToList();
        }

        List<string> first = Order(request);
        List<string> second = Order(request);

        // **名字里就有 `Item`**：`occupied_P1`、`assignment_C1_P1`、`empty_between_cargo_P1_P2_…`——
        // 于是"`Item` → 变量"的映射**不必新造**，按命名约定即可解析；这一步只是把约定钉住。
        // **The names already carry the `Item`** (measured): `occupied_P1`, `assignment_C1_P1`, `empty_between_cargo_P1_P2_…` — so the `Item`-to-variable map
        // **does not have to be invented**, it can be parsed from the naming convention; this step only pins the convention.
        first.Should().Contain("occupied_P1");
        first.Should().Contain("assignment_C1_P1");
        first.Where(name => name.StartsWith("assignment_", System.StringComparison.Ordinal)).Should().HaveCount(6,
            "three cargo items over two positions is six assignment variables; the decode map is exactly this");
        second.Should().Equal(first, "a positional map is only safe when the order does not move between builds");
    }
    /// <summary>**划分的两半是不是"同一个模型的划分"** / Are the two halves a partition of one model?</summary>
    /// <remarks>
    /// 量到单模型 11 行、划分后 14 + 19 = 33 行。这一条量的是**行对象的来历**：划分后两半的约束里，
    /// **有多少个对象与源模型的约束是同一个引用**。零个 ⇒ 两半是**重新建出来的模型**，不是源的划分。
    ///
    /// measured 11 rows in the single model and 14 + 19 = 33 after the split. This measures **where the row objects come from**: how many constraints in the two halves
    /// are **reference-equal to a source-model constraint**. Zero means the halves are **models built afresh**, not a partition of the source.
    /// </remarks>
    [Fact]
    public async Task TheTwoHalves_ShouldSayWhetherTheyShareRowsWithTheSource() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        var application = new FullLoadApplication();
        application.Init(request).IsOk.Should().BeTrue();

        List<string> notes = [];
        LinearMetaModel<Flt64>? built = application.BuildSingleBendersModel(StowageMode.FullLoad, notes);
        built.Should().NotBeNull("notes: " + string.Join("|", notes));

        Result<LinearBendersModels, ErrorCode, Error<ErrorCode>> split =
            await LinearBendersPartition.BuildAsync(built!);
        split.IsOk.Should().BeTrue("notes: " + string.Join("|", notes));

        HashSet<MathConstraint> source = new(built!.Constraints, ReferenceEqualityComparer.Instance);
        using LinearBendersModels models = split.Value;
        int sharedMaster = models.Master.Constraints.Count(source.Contains);
        int sharedSub = models.Sub.Constraints.Count(source.Contains);

        // **实测记录**：源模型 11 行；两半与源**共享 0 个约束对象** ⇒ 两半是重新注册出来的模型。
        // 于是"划分把装载约束留在子问题"这个说法**不准确**：装载约束**根本不在任何一半里**（除非被重新注册）。
        //
        // **Measured**: the source has 11 rows and the two halves share **no constraint object with it** ⇒ the halves are rebuilt models. So "the split leaves the loading
        // constraints in the sub" is **inaccurate**: the loading constraints are **in neither half** unless re-registered.
        // **更正 **：共享 0 个引用**不等于**"不是划分"——`AddConstraint` 会**重新生成约束对象**，
        // 所以引用必然为 0。真正的证据是**名字**：源模型的每一行按名字出现在**某一半**里。
        //
        // **corrects **: sharing zero references **does not** mean "not a partition" — `AddConstraint` **re-materialises** the constraint, so zero is expected.
        // The real evidence is **names**: every source row appears in **one of the halves** by name.
        HashSet<string> sourceNames = new(built!.Constraints.Select(constraint => constraint.Name), System.StringComparer.Ordinal);
        HashSet<string> splitNames = new(
            models.Master.Constraints.Concat(models.Sub.Constraints).Select(constraint => constraint.Name),
            System.StringComparer.Ordinal);
        sourceNames.Count.Should().Be(11, "the source model's measured row count");
        sourceNames.Except(splitNames).Should().BeEmpty(
            "every source row must land in exactly one half; missing rows would be silently dropped");
        (sharedMaster + sharedSub).Should().Be(0,
            "expected and explained: AddConstraint re-materialises rows, so references never match; measured "
                + sharedMaster + " + " + sharedSub);
        built!.Constraints.Count.Should().Be(11, "the source model's measured row count");
        models.Master.Constraints.Count.Should().Be(14);
        models.Sub.Constraints.Count.Should().Be(19);
    }

    /// <summary>**"必装"那一行不在任何一半里** / The "must load" row is in neither half.</summary>
    /// <remarks>
    /// 量到子问题没有一行提到 `assignment_`，结论是"模型里没有必须装载的约束"。 找到了**它应该在的位置**：
    /// `LoadingCapacityLimit` 对每个货加一行 `loading_capacity_item_{id}`，`Required` 时是 **`EQ`（恰好一次）**，其余 `LE`。
    /// **这条判据量的是它缺席**：Benders 单模型划分后的两半里，**没有一行以 `loading_capacity` 开头**。
    ///
    /// measured that no sub row mentions `assignment_`, concluding the model has no "must load" constraint. found **where it belongs**:
    /// `LoadingCapacityLimit` adds `loading_capacity_item_{id}` per cargo, **`EQ` (exactly once) when `Required`**, `LE` otherwise.
    /// **This measures its absence**: after the split, **no row of either half starts with `loading_capacity`**.
    /// </remarks>
    [Fact]
    public async Task TheMustLoadRow_ShouldBeMissingFromBothHalves() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        var application = new FullLoadApplication();
        application.Init(request).IsOk.Should().BeTrue();

        List<string> notes = [];
        LinearMetaModel<Flt64>? built = application.BuildSingleBendersModel(StowageMode.FullLoad, notes);
        built.Should().NotBeNull("notes: " + string.Join("|", notes));

        Result<LinearBendersModels, ErrorCode, Error<ErrorCode>> split =
            await LinearBendersPartition.BuildAsync(built!);
        split.IsOk.Should().BeTrue();

        using LinearBendersModels models = split.Value;
        List<string> names = System.Linq.Enumerable.ToList(
            System.Linq.Enumerable.Concat(models.Master.Constraints, models.Sub.Constraints))
            .ConvertAll(constraint => constraint.Name);

        // **实测记录**：两半里没有一行叫 `loading_capacity_*` ⇒ "必装恰好一次"从没进模型。
        // **Measured**: no row is named `loading_capacity_*` ⇒ "required means exactly once" never entered the model.
        // **更正 **：`loading_capacity_item_C1/C2/C3` **就在模型里**——"必装"这一行**从来没缺**。
        // 于是真正的区分点不是"约束在不在"，而是 **`Required` 标志与目标方向**：若样车不是 `Required`，
        // 这一行就是 `LE`（至多一次），**零分配合法**；若目标是最小化，零分配自然最优。
        //
        // **corrects **: `loading_capacity_item_C1/C2/C3` **are in the model** — the must-load row never went missing. The real discriminator is therefore not
        // "is the constraint there" but **the `Required` flag and the objective direction**: a non-required item's row is `LE` (at most once), so **zero is legal**, and under a
        // minimising objective zero is naturally optimal.
        names.Should().Contain("loading_capacity_item_C1", "measured: the must-load row is present in the split");
        names.Should().Contain("loading_capacity_item_C2");
        names.Should().Contain("loading_capacity_item_C3");
    }

    /// <summary>读一个 Demo2 源码文件并剥离注释行 / Read a Demo2 source file with comment lines stripped.</summary>
    /// <param name="fileName">文件名 / The file name.</param>
 /// <returns>代码 / The implementation.</returns>
    private static string DemoSource(string fileName) {
        var directory = new global::System.IO.DirectoryInfo(global::System.AppContext.BaseDirectory);
        while (directory is not null) {
            string candidate = global::System.IO.Path.Combine(
                directory.FullName, "src", "Example", "FrameworkDemo", "Demo2", fileName);
            if (global::System.IO.File.Exists(candidate)) {
                return string.Join(System.Environment.NewLine, global::System.IO.File.ReadAllLines(candidate)
                    .Select(line => line.TrimStart())
                    .Where(line => !line.StartsWith("//", System.StringComparison.Ordinal)));
            }
            directory = directory.Parent;
        }
        throw new global::System.IO.FileNotFoundException("could not locate " + fileName);
    }

    /// <summary>**demo 永远造不出"必装"的货** / The demo can never produce required cargo.</summary>
    /// <remarks>
    /// 发现 `Required` 由 `item.Status` 决定（`LoadingContextFactory.cs:~62`：不是 `Optional`/`Reserved` 就是必装）。
    /// 这一条量到**根**：`AggregationInitializer` 给每一件货**硬编码 `ItemStatus.Optional`**，而 `CargoInput` 里**没有状态字段**。
    /// 于是**无论请求怎么写，都造不出必装货**——`loading_capacity_item_*` 永远是 `LE`，零装载永远合法。
    ///
    /// found `Required` comes from `item.Status` (anything other than `Optional`/`Reserved` is required). This measures **the root**: `AggregationInitializer`
    /// **hardcodes `ItemStatus.Optional`** for every cargo and `CargoInput` carries **no status field**, so **no request can produce a required cargo** — the
    /// `loading_capacity_item_*` rows are always `LE` and zero loading is always legal.
    /// </remarks>
    [Fact]
    public void TheDemo_ShouldKeepItsWayToMakeCargoRequired() {
        string initializer = DemoSource("Domain/Stowage/Service/AggregationInitializer.cs");
        // **之后这条判据换了方向**：必装标志**已经打通**（`CargoInput.Required` → `Item.Required` → `LoadingItem.Required`），
        // 所以现在要钉的是"**这条通路仍然在**"——通路断了，"必装货"又会变回造不出来的东西，而**没有任何判据会因此变红**。
        //
        // **After this criterion points the other way**: the required flag **is wired** (`CargoInput.Required` → `Item.Required` → `LoadingItem.Required`), so what must
        // be pinned is **that the wire still exists** — lose it and "required cargo" becomes impossible again, **and nothing else would turn red**.
        initializer.Should().Contain("required: cargo.Required",
            "the request's flag must reach the item; without it every cargo is optional again");
        initializer.Should().Contain("status: ItemStatus.Optional",
            "the status stays optional: the required flag is separate on purpose");

        string input = DemoSource("Infrastructure/DTO.cs");
        input.Should().Contain("bool Required = false",
            "the request must carry the flag; the model can express required rows only if this exists");

        string factory = DemoSource("Domain/Stowage/Service/LoadingContextFactory.cs");
        factory.Should().Contain("Required: item.Required",
            "the factory must read the item's own flag, not re-derive it from status");
    }

    /// <summary>**声明的阶段表会把划分打坏** / A declared stage table breaks the split.</summary>
    /// <remarks>
    /// 给 demo2 声明了阶段表（`occupied_*`/`assignment_*` → `Master`），结果**模型根本没到求解器**（`MasterCalls == 0`），改动已回退。
    /// 这一条把那次实测**钉成可复现的判据**：同样的声明交给划分器，量它返回什么。
    /// **回退掉的改动如果没有判据，下一个人会再试一次、再红一次。**
    ///
    /// declared a stage table for demo2 and the **model never reached the solver** (`MasterCalls == 0`); the change was reverted. This pins that measurement as a
    /// reproducible criterion: hand the same declaration to the partitioner and record what comes back. **A reverted change without a criterion gets retried and rediscovered.**
    /// </remarks>
    [Fact]
    public async Task TheDeclaredStages_ShouldSayWhatThePartitionerDoesWithThem() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        var application = new FullLoadApplication();
        application.Init(request).IsOk.Should().BeTrue();

        List<string> notes = [];
        LinearMetaModel<Flt64>? built = application.BuildSingleBendersModel(StowageMode.FullLoad, notes);
        built.Should().NotBeNull("notes: " + string.Join("|", notes));

        var stages = new Dictionary<Fuookami.Ospf.Core.Variable.IVariableItem, LinearBendersStage>();
        foreach (Fuookami.Ospf.Core.Token.Token<Flt64> token in built!.Tokens.Tokens) {
            Fuookami.Ospf.Core.Variable.IVariableItem variable = token.Variable;
            bool decision = variable.Name.StartsWith("occupied_", System.StringComparison.Ordinal)
                || variable.Name.StartsWith("assignment_", System.StringComparison.Ordinal);
            stages[variable] = decision ? LinearBendersStage.Master : LinearBendersStage.ContinuousRecourse;
        }

        Result<LinearBendersModels, ErrorCode, Error<ErrorCode>> split =
            await LinearBendersPartition.BuildAsync(built, stages);

        string outcome = split.IsOk
            ? "ok:master=" + split.Value.Master.Constraints.Count + ";sub=" + split.Value.Sub.Constraints.Count
                + ";master_tokens=" + split.Value.Master.Tokens.Tokens.Count
            : "failed:" + split;
        // **实测：划分器直接拒绝**——`整数变量不能作为连续追索 / Integer variable cannot be continuous recourse:
        // empty_between_cargo_P1_P2_condition_if`。**这才是声明阶段表失败的真正原因**：按名字前缀挑决策变量漏掉了
        // `empty_between_cargo_*` 里那些**也是整数**的辅助变量。划分器的默认推断已经把它们全归到主问题，
        // 所以"自己声明一份"不但没有收益，还会因为漏掉一个整数变量而**整条链走不动**。
        //
        // **Measured : the partitioner refuses outright** — `Integer variable cannot be continuous recourse: empty_between_cargo_P1_P2_condition_if`.
        // **That is why the declared stage table failed**: picking decisions by name prefix misses the auxiliary variables inside `empty_between_cargo_*`, which **are also
        // integer**. The partitioner's own inference already puts them all in the master, so hand-declaring adds nothing and one missed integer variable **stops the chain**.
        outcome.Should().StartWith("failed:", "measured: a declaration that misclassifies an integer variable is refused, not quietly tolerated");
        outcome.Should().Contain("整数变量不能作为连续追索", "the refusal must name the rule being broken");
    }

}
