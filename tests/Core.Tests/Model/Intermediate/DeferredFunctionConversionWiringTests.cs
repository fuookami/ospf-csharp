#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 转换处的延迟物化接线 / The deferred-materialisation wiring at the conversion choke point.
/// </summary>
/// <remarks>
/// B1241 把物化接到 `ToLinearTriadModel`——**七个后端插件、十四个调用点唯一的转换咽喉**。
///
/// **接在这里的理由不是"方便"，而是<b>正确性</b>：三元模型装不下延迟结构。
/// 一个带延迟结构的机制模型被直接转换，得到的不是一个"稍后展开"的三元模型，
/// 而是一个<b>少了那几段约束</b>的三元模型——而它照样能求解、照样给解。**
///
/// **而这一批还查出一件更大的事：没有任何生产模型<b>带得来</b>延迟结构。
/// 接线让整条链在原理上可达，而现在仍然不可达，因为唯一的构建路径不给这两个参数。
/// 那条缺口由 `TheBuildPath_ShouldNotYetSupplyTheStructures` 钉住。**
///
/// B1241 wires materialisation into `ToLinearTriadModel`, the single conversion choke point for seven plugin assemblies and
/// fourteen call sites.
///
/// **The reason is <b>correctness</b>, not convenience: a triad model cannot hold deferred structures.**
///
/// **This batch also found something bigger: no production model can <b>arrive</b> carrying deferred structures.**
/// </remarks>
public class DeferredFunctionConversionWiringTests
{
    /// <summary>
    /// **构建路径现在真的给这两个参数了** / The build path now really supplies both parameters.
    /// </summary>
    /// <remarks>
    /// **B1241 这条判据说的是"还不给"，B1242 把它反过来——而<b>是它自己变红逼着改的</b>。**
    ///
    /// **这就是缺口判据的设计意图：** **它描述"现在缺什么"，于是"补上了"这件事无法静默通过。**
    /// **补的人必须回来把这句话改掉，而改的时候必然要确认"是不是真的补上了"。**
    ///
    /// **如果它当初写成"构造点应该传这两个参数"（一条<b>正常</b>判据），
    /// 那它在 B1241 就是红的——而一个长期红的判据会被改成 `Should().BeTrue()`，
    /// 于是六批之后没人知道它原本在等什么。**
    ///
    /// **B1241's version of this criterion said "not yet supplied"; B1242 turns it around — and it was <b>the criterion going
    /// red that forced the edit</b>.**
    ///
    /// **That is the design intent: it describes "what is missing now", so closing the gap cannot pass silently.** **The
    /// closer must come back and change this sentence, and changing it forces them to confirm the gap really closed.**
    ///
    /// **Had it been written as "the construction site should pass these two parameters" — an <b>ordinary</b> criterion — it
    /// would have been red in B1241, and a long-red criterion gets rewritten as `Should().BeTrue()`, so six batches later
    /// nobody would know what it was waiting for.**
    /// </remarks>
    [Fact]
    public void TheBuildPath_ShouldNowSupplyBothParameters() {
        string root = RepositoryRoot();
        string source = File.ReadAllText(Path.Combine(root, "src/Core/Model/Mechanism/MechanismModel.cs"));

        int build = source.IndexOf("var model = new LinearMechanismModel<V>(", StringComparison.Ordinal);
        build.Should().BeGreaterThan(0, "there is exactly one build site to inspect");

        int end = source.IndexOf(");", build, StringComparison.Ordinal);
        end.Should().BeGreaterThan(build);
        string call = source[build..end];

        call.Should().Contain("functionExpansionPolicy: functionExpansionPolicy",
            "the policy has to reach the model, or every production model stays Eager");
        // **B1251 之后传的是 `effectiveStructures`（调用方显式给的，或从函数符号派生出来的），
        // 所以这里钉"结构确实传到了"，而不是钉某个具体的变量名。**
        // **After B1251 the argument is `effectiveStructures` (explicitly supplied, or derived from the function symbols),
        // so this pins "the structures really arrive" rather than one particular variable name.**
        call.Should().Contain("deferredFunctionStructures: effectiveStructures",
            "the structures have to reach the model, or materialisation has nothing to expand");
    }

    /// <summary>
    /// **端到端：经真实构建路径产出的延迟模型，转换后行数变多** /
    /// End to end: a deferred model produced by the real build path gains rows when converted.
    /// </summary>
    /// <remarks>
    /// **这条判据在 B1241 之前<b>写不出来</b>。**
    ///
    /// **不是"没人写"，而是<b>写出来也跑不起来</b>：** **测试必须自己造模型，
    /// 而自己造的模型没有来源快照，转换直接返回"来源模型快照缺失"。**
    /// **而 B1241 给副本补上快照继承之后，测试又发现另一半也不成立——
    /// 构建路径根本不产带延迟结构的模型。**
    ///
    /// **两端都通之后，这条判据才能同时满足：** **模型由 `InvokeAsync` 建（于是有快照），
    /// 结构由参数传进去（于是有待展开的东西），转换于是真的会展开，而行数差是可数的。**
    ///
    /// **这是"一条判据跑不起来"作为线索的第二次收获：** **上一次（B1241）它指向了副本丢快照，
    /// 这一次它指向了构建路径不供给输入——两个不同的缺陷，同一条跑不起来的判据。**
    ///
    /// **This criterion <b>could not be written</b> before B1241.**
    ///
    /// **Not "nobody wrote it" but "<b>it could not run</b>": a test had to build its own model, and a hand-built model has no
    /// source snapshot, so conversion returned "Source model snapshot is missing".** **After B1241 gave the copy snapshot
    /// inheritance, the same test revealed the other half — the build path produced no deferred structures at all.**
    ///
    /// **With both ends open the criterion can finally hold: the model comes from `InvokeAsync` (so it has a snapshot), the
    /// structures come through parameters (so there is something to expand), conversion really expands, and the row
    /// difference is countable.**
    ///
    /// **This is the second harvest from "a criterion that could not run": in B1241 it pointed at the lost snapshot, here at
    /// the build path not supplying its input — two different defects, one unrunnable criterion.**
    /// </remarks>
    [Fact]
    public async Task ADeferredModelFromTheBuildPath_ShouldGainRowsWhenConverted() {
        int eagerRows = await BuiltAndConvertedRowsAsync(FunctionExpansionPolicy.Eager);
        int deferredRows = await BuiltAndConvertedRowsAsync(FunctionExpansionPolicy.DeferredNativeFirst);

        deferredRows.Should().BeGreaterThan(
            eagerRows,
            "a model built as deferred must gain its fallback rows at conversion; without this the triad model silently "
            + "loses constraints and still solves");
    }

    /// <summary>
    /// **七个后端都把配置策略传了进来** / All seven backends pass their config policy through.
    /// </summary>
    /// <remarks>
    /// **接线最容易的失败形态不是"接错"，而是"只接了<b>一部分</b>"：**
    /// **一个后端接上了，另外六个仍然直接转换——于是同一个模型在不同后端上得到<b>不同的约束集</b>，
    /// 而那两份都求解成功。**
    ///
    /// **所以这条判据按文件枚举，而不是抽查两三个。**
    ///
    /// **The easiest failure here is not "wired wrong" but "wired <b>partly</b>": one backend passes the policy while six
    /// still convert directly — so the same model yields <b>different constraint sets</b> per backend, and both solve
    /// fine.**
    ///
    /// **So this criterion enumerates files rather than spot-checking two or three.**
    /// </remarks>
    [Fact]
    public void EveryLinearSolver_ShouldPassItsConfigPolicy() {
        string root = RepositoryRoot();
        string[] solvers = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*LinearSolver.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains("obj", StringComparison.Ordinal))
            .Where(static path => !path.Contains("bin", StringComparison.Ordinal))
            .ToArray();

        string[] converting = solvers
            .Where(path => File.ReadAllText(path).Contains("ToLinearTriadModel(", StringComparison.Ordinal))
            .ToArray();
        converting.Should().HaveCount(7, "seven backends convert a mechanism model to a triad model");

        string[] wiring = converting
            .Where(path => File.ReadAllText(path).Contains("configPolicy: Config.FunctionExpansionPolicy",
                StringComparison.Ordinal))
            .ToArray();

        wiring.Should().HaveCount(
            converting.Length,
            "every backend that converts must pass its config policy, or the same model means different things per backend");
    }

    /// <summary>
    /// **转换的默认值是安全那一侧** / The conversion default is the safe side.
    /// </summary>
    /// <remarks>
    /// **`ToLinearTriadModel` 新增的 `configPolicy` 缺省为 `DeferredNativeFirst`，<b>不是</b>
    /// 与 `SolverConfig` 一样的 `Eager`。**
    ///
    /// **这个不对称是有意的：** **带配置的求解器显式传入自己的值（那种情况下 `Eager` 是用户的明确选择），
    /// 而检查类调用点不传——如果缺省也是 `Eager`，那些调用点就会跳过展开，
    /// 得到一个<b>少了约束</b>的三元模型。**
    ///
    /// **The new `configPolicy` defaults to `DeferredNativeFirst`, <b>not</b> to the `Eager` that `SolverConfig` uses.**
    ///
    /// **The asymmetry is deliberate: a configured solver passes its own value, while inspection call sites pass nothing —
    /// with an `Eager` default those sites would skip expansion and get a triad model <b>missing constraints</b>.**
    /// </remarks>
    [Fact]
    public void TheConversionDefault_ShouldBeTheSafeSide() {
        string root = RepositoryRoot();
        string extensions = File.ReadAllText(
            Path.Combine(root, "src/Core/Model/Intermediate/LinearMechanismModelDumpExtensions.cs"));

        extensions.Should().Contain(
            "FunctionExpansionPolicy configPolicy = FunctionExpansionPolicy.DeferredNativeFirst",
            "a missing argument must mean \"ask the model\", never \"silently skip expansion\"");
    }

    /// <summary>
    /// **副本继承了来源快照，否则它根本不能转换** /
    /// The copy inherits the source snapshot, or it cannot convert at all.
    /// </summary>
    /// <remarks>
    /// **这是本批抓到的一个<b>会让整条接线必然失败</b>的缺陷，而它的形态值得记住：**
    ///
    /// **`_sourceSnapshot` 只在构建路径里赋值，而 `Materialize` 用 `new LinearMechanismModel<...>` 造副本——
    /// 副本没有快照，于是 `ValidateSourceSnapshot` 返回 `来源模型快照缺失`。**
    ///
    /// **<b>物化本身是对的、转换本身也是对的、接线位置也是对的，而串起来必然失败。</b>
    /// 因为副本丢了源模型的一件状态，而"复制"这件事没被检查过"应该复制哪些状态"。**
    ///
    /// **抓到它的不是"测了物化"也不是"测了转换"，而是<b>测了物化之后再转换</b>——
    /// 而那条判据<b>跑不起来</b>，因为测试自己造的模型同样没有快照。
    /// 这个"跑不起来"本身就是线索：一条链的两端都必须由真实构建路径供给，才有可能被端到端测到。**
    ///
    /// **This is a defect found this batch that would have made the whole wiring fail every time.**
    ///
    /// **<b>Materialisation is right, conversion is right, the wiring point is right, and the chain fails every time.</b>**
    ///
    /// **What surfaced it was <b>materialising and then converting</b> — and that criterion <b>could not run</b>, because
    /// the test's own models have no snapshot either.** **That inability is itself the clue: both ends of the chain must be
    /// fed by the real build path before it can be tested end to end.**
    /// </remarks>
    [Fact]
    public void TheCopy_ShouldInheritTheSourceSnapshot() {
        string root = RepositoryRoot();

        string model = File.ReadAllText(Path.Combine(root, "src/Core/Model/Mechanism/MechanismModel.cs"));
        model.Should().Contain("internal void InheritSourceSnapshotFrom(",
            "the copy needs a way to carry the snapshot over");

        string driver = File.ReadAllText(
            Path.Combine(root, "src/Core/Model/Intermediate/DeferredFunctionFallbackDriver.cs"));
        int copy = driver.IndexOf("var copy = new LinearMechanismModel<Flt64>(", StringComparison.Ordinal);
        copy.Should().BeGreaterThan(0, "the entry point makes exactly one copy");

        int inherit = driver.IndexOf("copy.InheritSourceSnapshotFrom(model);", copy, StringComparison.Ordinal);
        inherit.Should().BeGreaterThan(
            copy,
            "the snapshot must be inherited right after the copy is made, or the copy cannot become a triad model");
    }

    /// <summary>
    /// **转换函数里真的先物化再转换** / The converter really materialises before dumping.
    /// </summary>
    /// <remarks>
    /// **上一条只钉了"默认值是安全那一侧"，钉不住"物化真的被调用"。**
    /// **把整段物化删掉，默认值那条依然绿——因为默认值还在。**
    ///
    /// **所以还需要这一条：物化调用的位置必须在 `Dump(` <b>之前**，
    /// 因为"先物化再转换"的<b>顺序</b>就是那条正确性论证本身。**
    ///
    /// **The previous criterion pins "the default is the safe side" but not "materialisation is really called".**
    /// **Delete the whole block and it stays green — the default is still there.**
    ///
    /// **Hence this one: the call must come <b>before</b> `Dump(`, because that <b>order</b> is the correctness argument.**
    /// </remarks>
    [Fact]
    public void TheConversion_ShouldMaterialiseBeforeDumping() {
        string root = RepositoryRoot();
        string extensions = File.ReadAllText(
            Path.Combine(root, "src/Core/Model/Intermediate/LinearMechanismModelDumpExtensions.cs"));

        int materialise = extensions.IndexOf("DeferredFunctionMaterialization.Materialize(", StringComparison.Ordinal);
        int dump = extensions.IndexOf("return Dump(target, fixedVariables, cancellationToken);", StringComparison.Ordinal);

        materialise.Should().BeGreaterThan(0, "the converter must expand deferred structures");
        dump.Should().BeGreaterThan(0, "the converter still has to dump afterwards");
        materialise.Should().BeLessThan(
            dump,
            "expanding after dumping would be pointless: the triad model cannot hold the structures");
    }

    /// <summary>
    /// **用真实构建路径建一个带 `Abs` 结构的模型，转换成三元模型并返回行数** /
    /// Build a model carrying one `Abs` structure through the real path, convert it, and return its rows.
    /// </summary>
    /// <remarks>
    /// **注意变量要先注册进 meta model：** **物化产出的约束引用了它们，而 `AddConstraint` 通过 token 表解析；
    /// 没注册就解析不到（B1239 踩过，报 `variable y is not registered`）。**
    ///
    /// **Variables must be registered on the meta model first: the materialised constraints reference them and
    /// `AddConstraint` resolves them through the token table.** **Unregistered ones cannot be resolved — B1239 hit exactly
    /// that.**
    /// </remarks>
    private static async Task<int> BuiltAndConvertedRowsAsync(FunctionExpansionPolicy policy) {
        var y = new RealVar("y");
        var positive = new RealVar("y-pos");
        var negative = new RealVar("y-neg");
        var sign = new RealVar("y-sign");
        using var parent = new LinearMetaModel<Flt64>();

        bool registered = parent.Add([y, positive, negative, sign]).IsOk;
        registered.Should().BeTrue("the variables the fallback rows reference must exist in the token table");

        var structure = new AbsStructure(
            Input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, y) }, Flt64.Zero),
            ResultVariable: y,
            PositiveVariable: positive,
            NegativeVariable: negative,
            SignVariable: sign,
            PositiveBigM: new Flt64(100.0),
            NegativeBigM: new Flt64(100.0),
            Converter: IIntoValue<Flt64>.Identity,
            Name: "y");

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                parent,
                functionExpansionPolicy: policy,
                deferredFunctionStructures: [structure]);
        Explain(built, "building the model");
        bool builtOk = built.IsOk;
        builtOk.Should().BeTrue("building a valid model must succeed");

        using LinearMechanismModel<Flt64> model =
            ((Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)built).Value;

        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> converted = model.ToLinearTriadModel();
        Explain(converted, "converting the model");
        bool convertedOk = converted.IsOk;
        convertedOk.Should().BeTrue("converting a built model must succeed");

        using LinearTriadModel triad =
            ((Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>>)converted).Value;
        return triad.Constraints.ConstraintCount;
    }

    /// <summary>
    /// **把失败原因写进断言消息** / Put the failure reason into the assertion message.
    /// </summary>
    /// <remarks>
    /// **没有这个，断言只会说"期望真、实际假"，而需要知道的是<b>为什么</b>。
    /// 本批两次因此少绕一圈：`variable y is not registered` 与 `来源模型快照缺失`。**
    ///
    /// **Without this an assertion only says "expected true, got false", while what is needed is <b>why</b>.** **Two
    /// detours were avoided this batch because the reason was visible.**
    /// </remarks>
    private static void Explain<T>(Result<T, ErrorCode, Error<ErrorCode>> result, string what) {
        if (result is Failed<T, ErrorCode, Error<ErrorCode>> failure) {
            failure.Error.Message.Should().BeEmpty($"{what} failed because: {failure.Error.Message}");
        }
        if (result is Fatal<T, ErrorCode, Error<ErrorCode>> fatal) {
            string joined = string.Join(" | ", fatal.Errors.Select(static e => e.Message));
            joined.Should().BeEmpty($"{what} failed fatally because: {joined}");
        }
    }

    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}
