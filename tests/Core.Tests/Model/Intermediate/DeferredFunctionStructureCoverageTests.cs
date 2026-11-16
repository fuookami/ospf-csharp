#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 七种延迟结构逐种走通 / Each of the seven deferred structures walked through the chain.
/// </summary>
/// <remarks>
/// 把"一种通了"扩到"每一种都通了"，并固定住"已声明但未实现"的那两种。
///
/// **为什么必须逐种测：** **`SupportsDeferredFallback` 是一次<b>类型分派</b>，而物化器是<b>另一套</b>类型分派。
/// 两套分派各自维护一张表，而<b>没有任何东西保证两张表一致</b>。**
///
/// Widens "one kind works" to "every kind works", and pins the two that are declared but not implemented.
///
/// **Why each kind must be tested: `SupportsDeferredFallback` is one <b>type dispatch</b> and the materialisers are
/// <b>another</b>.** **Two tables, maintained separately, with <b>nothing guaranteeing they agree</b>.**
/// </remarks>
public class DeferredFunctionStructureCoverageTests
{
    /// <summary>
    /// **每一种有物化器的结构，转换后都会真的多出行** /
    /// Every structure with a materialiser really gains rows when converted.
    /// </summary>
    /// <param name="kind">结构种类 / The structure kind.</param>
    /// <remarks>
    /// **用 `Theory` 而不是五条 `Fact`：** **五条 `Fact` 里漏掉一种是最容易犯的错，而 `InlineData` 漏一种
    /// 至少会在读数时被发现（"怎么只有四条"）。**
    ///
    /// **A `Theory` rather than five `Fact`s: dropping one kind is the easiest mistake, and an `InlineData` omission at least
    /// shows up when reading the count ("why only four").**
    /// </remarks>
    [Theory]
    [InlineData("abs")]
    [InlineData("max")]
    [InlineData("semi")]
    [InlineData("binary-logic")]
    [InlineData("piecewise")]
    [InlineData("indicator")]
    [InlineData("masking")]
    public async Task EachMaterialisedKind_ShouldGainRowsWhenConverted(string kind) {
        int eagerRows = await RowsAsync(kind, FunctionExpansionPolicy.Eager);
        int deferredRows = await RowsAsync(kind, FunctionExpansionPolicy.DeferredNativeFirst);

        deferredRows.Should().BeGreaterThan(
            eagerRows,
            $"a deferred '{kind}' structure must gain its fallback rows at conversion; otherwise the triad model silently "
            + "loses constraints and still solves");
    }

    /// <summary>
    /// **两张表在七种上完全一致** / The two tables agree on all seven.
    /// </summary>
    /// <remarks>
    /// **这条判据原本写的是"两张表差的那两种是 Indicator 与 Masking"——而那是<b>错的</b>。**
    ///
    /// **我据"只看到五个 `Unsupported(...)`"推断出"只有五个物化器"，
    /// 而真正的分派在 `CoreMaterializer` 里，它有<b>七个</b>分支（Indicator 与 Masking 直接调用
    /// 各自结构上的 `MaterializeFallbackConstraints`）。**
    ///
    /// **"看不到 `Unsupported`"≠"没有分支"：** **`Unsupported` 是<b>默认分支</b>的形状，
    /// 而正常分支根本不写它。数默认分支的个数，数出来的是"漏了谁"，不是"支持了谁"。**
    ///
    /// **而这条判据现在钉的是"两张表一致"：** **少一种是缺陷，多声明一种也是缺陷。
    /// 无论哪种，都必须有人回来改这句话。**
    ///
    /// **This criterion originally read "the two tables differ on Indicator and Masking" — and that was <b>wrong</b>.**
    ///
    /// **I inferred "five materialisers" from "five `Unsupported(...)` calls", but the real dispatch is in
    /// `CoreMaterializer`, which has <b>seven</b> branches (Indicator and Masking call `MaterializeFallbackConstraints` on
    /// their own structure directly).**
    ///
    /// **"No `Unsupported` in sight" is not "no branch": `Unsupported` is the shape of the <b>default</b> branch, and normal
    /// branches never mention it.** **Counting default branches counts "who is missing", not "who is supported".**
    ///
    /// **The criterion now pins "the two tables agree": one kind missing is a defect, one declared too many is a defect, and
    /// either way someone has to come back and change this sentence.**
    /// </remarks>
    [Fact]
    public void TheTwoTables_ShouldAgreeOnAllSeven() {
        HashSet<string> declared = DeclaredFallbackKinds();
        HashSet<string> dispatched = DispatchedKinds();

        declared.Should().HaveCount(7, "seven kinds declare deferred-fallback support");
        dispatched.Should().HaveCount(7, "the core materialiser dispatches seven kinds");

        HashSet<string> onlyDeclared = new(declared);
        onlyDeclared.ExceptWith(dispatched);
        onlyDeclared.Should().BeEmpty(
            "a kind that declares support but has no branch would be selected as pending and then fail, breaking the "
            + "whole conversion");

        HashSet<string> onlyDispatched = new(dispatched);
        onlyDispatched.ExceptWith(declared);
        onlyDispatched.Should().BeEmpty(
            "a branch for a kind that does not declare support is unreachable code");
    }

    /// <summary>
    /// **极值的两组行随方向翻号，而 Big-M 项不翻** /
    /// The extremum's two row families flip with the direction, while the Big-M term does not.
    /// </summary>
    /// <remarks>
    /// **起点是一个具体缺陷：** **`MaxStructure.Minimum` 这个属性<b>没有任何地方读它</b>——
 /// 声明了、`MaxFunction` 的投影也在填，而生成逻辑无论方向都产 MAX 行。**
    /// **于是 `min` 被当成 MAX 物化：模型合法、能解，只是答案是最大值。**
    ///
    /// **这条判据是<b>不用求解器</b>的那半边，另一半是 `MinRows_ShouldPinTheAnswer`（真实求解，
    /// 三个后端各一条）。两半都要有：**
    ///
    /// **结构判据跑在默认套件里、快，但它只能看符号；**
    /// **求解判据慢，但它是唯一能证明"这组行压出来的答案是对的"的东西。**
    ///
    /// **而这一条特意同时断言两件事：** **`result` 的系数随方向翻号，Big-M 的系数<b>不翻</b>。**
    /// **曾经"全翻"，于是未选中的输入变成 `result ≥ input + M`，模型直接无解。**
    ///
    /// **The starting point is a concrete defect: `MaxStructure.Minimum` was <b>read by nothing</b> — declared, filled by the
 /// `MaxFunction` projection, and the generation produced MAX rows either way.** **So a `min` materialised as a
    /// MAX: legal, solvable, answering with the maximum.**
    ///
    /// **This criterion is the <b>solver-free</b> half; the other half is `MinRows_ShouldPinTheAnswer` (a real solve, once
    /// per backend). Both are needed: the structural one is fast and lives in the default suite but only sees signs, while
    /// the solve is slow and is the only thing that proves the rows force the right answer.**
    ///
    /// **It deliberately asserts two things at once: the coefficient of `result` flips with the direction, and the Big-M
    /// coefficient <b>does not</b>.** **the earlier first version flipped everything, and an unselected input then read
    /// `result >= input + M` — infeasible outright.**
    /// </remarks>
    [Fact]
    public void ExtremumRows_ShouldFlipTheDirectionButNotTheBigM() {
        var x = new RealVar("flip-x");
        var result = new RealVar("flip-y");
        var selector = new BinVar("flip-s");

        (double Result, double BigM) Coefficients(bool minimum) {
            var structure = new MaxStructure(
                Inputs: [Linear(x)],
                ResultVariable: result,
                SelectorVariables: [selector],
                BigMValues: [new Flt64(50.0)],
                Converter: IIntoValue<Flt64>.Identity,
                Name: "flip") {
                Minimum = minimum,
            };
            var ok = (Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>)
                structure.MaterializeFallbackConstraints();
            // **第二组是"收紧／松弛"那一组，Big-M 只出现在它里面。**
            // **The slack family is the second one, and Big-M appears only there.**
            LinearInequality<Flt64> slack = ok.Value.Constraints[1];
            double resultCoefficient = 0;
            double bigMCoefficient = 0;
            foreach (LinearMonomial<Flt64> monomial in slack.Lhs.Monomials) {
                if (ReferenceEquals(monomial.Symbol, result)) {
                    resultCoefficient = monomial.Coefficient.ToDouble();
                }
                if (ReferenceEquals(monomial.Symbol, selector)) {
                    bigMCoefficient = monomial.Coefficient.ToDouble();
                }
            }
            return (resultCoefficient, bigMCoefficient);
        }

        (double maxResult, double maxBigM) = Coefficients(minimum: false);
        (double minResult, double minBigM) = Coefficients(minimum: true);

        maxResult.Should().Be(1.0, "MAX tightens upward: result - input + M*s <= M");
        minResult.Should().Be(-1.0, "MIN tightens downward: input - result + M*s <= M");
        maxBigM.Should().Be(50.0, "the Big-M term is never negated");
        minBigM.Should().Be(50.0, "negating it turns the unselected input into result >= input + M, which is infeasible");
    }

    /// <summary>
    /// **七种全覆盖** / All seven are covered.
    /// </summary>
    /// <remarks>
    /// **这条判据原本说"六种覆盖、第七个是 Masking"，后来反过来——
    /// 而它<b>是自己变红逼着改的</b>，因为 `masking` 一加进去它就报"不该出现"。**
    ///
    /// **这就是缺口判据的第六次生效：** **它描述"现在缺什么"，于是"补上了"无法静默通过。**
    ///
    /// **而这次它多挡了一件事：** **如果我只是加了一行 `InlineData("masking")` 而没真的让它跑通，
    /// 那么 `Theory` 会红在 `masking` 那一格，而不是红在这条判据上——
    /// 两种红指向完全不同的原因（"没跑通"与"没更新文档"），而红的位置说清是哪一种。**
    ///
    /// **It used to say "six covered, the seventh is Masking"; now it turns around — and it was <b>the criterion going red
    /// that forced the edit</b>, since adding `masking` made it complain "must not appear".**
    ///
    /// **That is the sixth time a gap criterion has done its job: it describes "what is missing now", so closing the gap
    /// cannot pass silently.**
    ///
    /// **This time it also separated two failures: had I merely added `InlineData("masking")` without making it work, the
    /// `Theory` would have gone red on the `masking` case rather than on this criterion — and where it goes red says which
    /// of "it does not work" and "the note was not updated" actually happened.**
    /// </remarks>
    [Fact]
    public void AllSeven_ShouldBeCovered() {
        string root = RepositoryRoot();
        string tests = System.IO.File.ReadAllText(
            System.IO.Path.Combine(
                root, "tests/Core.Tests/Model/Intermediate/DeferredFunctionStructureCoverageTests.cs"));

        string[] covered = ["abs", "max", "piecewise", "semi", "binary-logic", "indicator", "masking"];
        foreach (string kind in covered) {
            tests.Should().Contain(
                $"[InlineData(\"{kind}\")]",
                $"'{kind}' is covered end to end and dropping it must not pass silently");
        }
    }

    /// <summary>
    /// **PWL 仍然需要捕获好的输入边界，而它现在已经被端到端覆盖** /    /// <summary>
    /// **PWL 仍然需要捕获好的输入边界，而它现在已经被端到端覆盖** /
    /// PWL still requires captured input bounds, and it is now covered end to end.
    /// </summary>
    /// <remarks>
    /// **这条判据原本说"这里覆盖不到 PWL"，后来它仍然是那个意思——
    /// 而这里把它改成了两件事：**
    ///
    /// **① 前提仍然存在**：**`CapturedInputBounds` 为 null 时物化返回
    /// "has no input bounds proof"。这是<b>契约</b>，不是缺陷，而契约不会因为被覆盖而消失。**
    ///
    /// **② 前提是可以满足的**：**`LinearPolynomialBounds` 只有上下界两个字段，
    /// 所以手工构造的 PWL 也能通过，`Theory` 里现在已经加了 `piecewise`。**
    ///
    /// **为什么这两件事要一起断言：** **只断言①，判据会在"PWL 从没被测过"时依然绿——
    /// 因为那句话在源码里永远存在。只断言②，契约就会被悄悄忘掉。**
    ///
    /// **一条判据同时钉"契约在这里"与"有人真的过了这道契约"，才是完整的。**
    ///
    /// **The criterion used to say "this cannot cover PWL"; it still means that — and this
    /// splits it into two things:**
    ///
    /// **① The precondition still exists: with `CapturedInputBounds` null, materialisation returns "has no input bounds
    /// proof". That is a <b>contract</b>, not a defect, and a contract does not disappear because it is covered.**
    ///
    /// **② The precondition is satisfiable: `LinearPolynomialBounds` is just a lower and an upper bound, so a hand-built
    /// PWL passes too, and the `Theory` now includes `piecewise`.**
    ///
    /// **Why both together: asserting only ① stays green while PWL is never tested, because that sentence is always in the
    /// source; asserting only ② lets the contract be quietly forgotten.**
    ///
    /// **A criterion that pins both "the contract is here" and "someone really got past it" is the complete one.**
    /// </remarks>
    [Fact]
    public void Piecewise_ShouldStillRequireBoundsAndNowBeCovered() {
        string root = RepositoryRoot();
        string source = System.IO.File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Model/Intermediate/UnivariateLinearPiecewiseStructure.cs"));

        source.Should().Contain("has no input bounds proof",
            "the PWL materialiser refuses a structure without captured bounds — that contract still holds");

        string tests = System.IO.File.ReadAllText(
            System.IO.Path.Combine(
                root, "tests/Core.Tests/Model/Intermediate/DeferredFunctionStructureCoverageTests.cs"));
        tests.Should().Contain(
            "[InlineData(\"piecewise\")]",
            "PWL must be in the theory, or the batch quietly stops covering it while this criterion stays green");
    }

    /// <summary>读出 `SupportsDeferredFallback` 声明支持的七种 / Read the seven kinds declared as supported.</summary>
    private static HashSet<string> DeclaredFallbackKinds() {
        var structure = typeof(DeferredFunctionStructure).Assembly
            .GetTypes()
            .Where(static type => type is { IsAbstract: false, IsInterface: false }
                && typeof(DeferredFunctionStructure).IsAssignableFrom(type))
            .ToDictionary(static type => type.Name, static type => type, StringComparer.Ordinal);

        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (Type type in structure.Values) {
            DeferredFunctionStructure instance = MakeEmpty(type);
            if (instance.SupportsDeferredFallback()) {
                declared.Add(type.Name);
            }
        }
        return declared;
    }

    /// <summary>造一个只为调用类型分派用的空实例 / Build an empty instance just to call the type dispatch.</summary>
    private static DeferredFunctionStructure MakeEmpty(Type type) =>
        (DeferredFunctionStructure)(type.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [],
            modifiers: null)?.Invoke(null)
            ?? System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type));

    /// <summary>读出 `CoreMaterializer` 真正分派的种类 / Read the kinds the core materialiser actually dispatches.</summary>
    /// <remarks>
    /// **只读 `CoreMaterializer` 那一段，不读整个文件：** **五个具体物化器类里也写着类型名，
    /// 把整文件扫一遍会把"定义了物化器"和"分派到了"混在一起。**
    ///
    /// **Read only the `CoreMaterializer` region: the five concrete materialiser classes also mention type names, and
    /// scanning the whole file would conflate "a materialiser exists" with "it is dispatched".**
    /// </remarks>
    private static HashSet<string> DispatchedKinds() {
        string root = RepositoryRoot();
        string source = System.IO.File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Model/Intermediate/DeferredFunctionFallbackMaterializers.cs"));

        int start = source.IndexOf("private sealed class CoreMaterializer", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the core materialiser must exist");
        string body = source[start..];

        var dispatched = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in new[]
                 {
                     "AbsStructure", "MaxStructure", "SemiStructure",
                     "UnivariateLinearPiecewiseStructure", "BinaryLogicStructure",
                     "IndicatorStructure", "MaskingStructure",
                 }) {
            if (body.Contains(name + " ", StringComparison.Ordinal)) {
                dispatched.Add(name);
            }
        }
        return dispatched;
    }

    /// <summary>建一个带指定结构种类的模型，转换成三元模型并返回行数 / Build, convert, and return rows.</summary>
    private static async Task<int> RowsAsync(string kind, FunctionExpansionPolicy policy) {
        (DeferredFunctionStructure structure, IVariableItem[] variables) = Build(kind);

        using var parent = new LinearMetaModel<Flt64>();
        bool registered = parent.Add(variables).IsOk;
        registered.Should().BeTrue($"the variables a '{kind}' fallback references must exist in the token table");

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                parent,
                functionExpansionPolicy: policy,
                deferredFunctionStructures: [structure]);
        Explain(built, $"building the '{kind}' model");
        bool builtOk = built.IsOk;
        builtOk.Should().BeTrue($"building a valid '{kind}' model must succeed");

        using LinearMechanismModel<Flt64> model =
            ((Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)built).Value;

        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> converted = model.ToLinearTriadModel();
        Explain(converted, $"converting the '{kind}' model");
        bool convertedOk = converted.IsOk;
        convertedOk.Should().BeTrue($"converting a built '{kind}' model must succeed");

        using LinearTriadModel triad =
            ((Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>>)converted).Value;
        return triad.Constraints.ConstraintCount;
    }

    private static (DeferredFunctionStructure Structure, IVariableItem[] Variables) Build(string kind) {
        var y = new RealVar("y");
        switch (kind) {
            case "abs": {
                var positive = new RealVar("abs-pos");
                var negative = new RealVar("abs-neg");
                var sign = new RealVar("abs-sign");
                var structure = new AbsStructure(
                    Input: Linear(y),
                    ResultVariable: y,
                    PositiveVariable: positive,
                    NegativeVariable: negative,
                    SignVariable: sign,
                    PositiveBigM: new Flt64(100.0),
                    NegativeBigM: new Flt64(100.0),
                    Converter: IIntoValue<Flt64>.Identity,
                    Name: kind);
                return (structure, [y, positive, negative, sign]);
            }
            case "max": {
                var result = new RealVar("max-result");
                var selector = new RealVar("max-selector");
                var structure = new MaxStructure(
                    Inputs: [Linear(y)],
                    ResultVariable: result,
                    SelectorVariables: [selector],
                    BigMValues: [new Flt64(100.0)],
                    Converter: IIntoValue<Flt64>.Identity,
                    Name: kind);
                return (structure, [y, result, selector]);
            }
            case "piecewise": {
                // **输入变量的范围必须落在捕获的边界证明之内**：`ValidateFallbackInputBounds`
                // 会把当前范围与 `CapturedInputBounds` 比较，超出了就报
                // "input bounds... exceed the captured Big-M proof"。
                // **The input variable's range must sit inside the captured proof**: `ValidateFallbackInputBounds`
                // compares the current range against `CapturedInputBounds` and refuses when it is wider.
                y.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(10)).Value);
                var result = new RealVar("pwl-result");
                var selector = new RealVar("pwl-selector");
                var structure = new UnivariateLinearPiecewiseStructure(
                    Input: Linear(y),
                    Breakpoints: [new Flt64(0.0), new Flt64(10.0)],
                    Slopes: [new Flt64(1.0)],
                    Intercepts: [Flt64.Zero],
                    ResultVariable: result,
                    SelectorVariables: [selector]) {
                    // **PWL 的转换器不是构造参数而是一个 init 属性——它不传就是 null，
                    // 而物化会因此返回"has no value converter"。**
                    // **PWL's converter is an init property, not a constructor parameter — left unset it is null and
                    // materialisation returns "has no value converter".**
                    Converter = IIntoValue<Flt64>.Identity,
                    // **`CapturedInputBounds` 同理，不传就报"has no input bounds proof"。
                    // 而这个前提<b>是可以补的</b>——它只有上下界两个字段。**
                    // **`CapturedInputBounds` likewise: unset means "has no input bounds proof". The precondition is
                    // <b>satisfiable</b> — it is just a lower and an upper bound.**
                    CapturedInputBounds = new Ok<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>(
                        new LinearPolynomialBounds<Flt64>(Flt64.Zero, new Flt64(10))),
                };
                return (structure, [y, result, selector]);
            }
            case "semi": {
                var result = new RealVar("semi-result");
                var indicator = new RealVar("semi-indicator");
                var structure = new SemiStructure(
                    LowerBound: new Flt64(1.0),
                    UpperBound: new Flt64(10.0),
                    ResultVariable: result,
                    IndicatorVariable: indicator,
                    Converter: IIntoValue<Flt64>.Identity,
                    Name: kind);
                return (structure, [result, indicator]);
            }
            case "binary-logic": {
                // **输入与结果都必须是二元类型**：物化会检查 `TypeKind.IsBinaryType`，
                // 而 `RealVar` 是连续变量——用它只会得到"invalid binary logic structure inputs"。
                // **Inputs and result must all be binary-typed**: materialisation checks `TypeKind.IsBinaryType`, and
                // `RealVar` is continuous — using it only yields "invalid binary logic structure inputs".
                var left = new BinVar("logic-left");
                var right = new BinVar("logic-right");
                var result = new BinVar("logic-result");
                var structure = new BinaryLogicStructure(
                    Operation: BinaryLogicOperation.And,
                    Inputs: [left, right],
                    ResultVariable: result,
                    Converter: IIntoValue<Flt64>.Identity,
                    Name: kind);
                return (structure, [left, right, result]);
            }
            case "indicator": {
                // **指示变量必须是二元类型，输入范围必须落在捕获的边界证明之内。**
                // **The indicator variable must be binary, and the input range must sit inside the captured proof.**
                y.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(10)).Value);
                var indicator = new BinVar("indicator-result");
                var structure = new IndicatorStructure(
                    Input: Linear(y),
                    ResultVariable: indicator,
                    BigM: new Flt64(100.0),
                    Tolerance: new Flt64(1e-6),
                    Converter: IIntoValue<Flt64>.Identity,
                    Name: kind) {
                    // **`IndicatorStructure` 的这个属性是裸的 `LinearPolynomialBounds?`，
                    // 而 PWL 那个是 `Result<...>?`——两个同名属性类型不同，这一点靠编译器才发现。**
                    // **This property on `IndicatorStructure` is a bare `LinearPolynomialBounds?`, while PWL's is
                    // `Result<...>?` — same name, different type, and only the compiler said so.**
                    CapturedInputBounds = new LinearPolynomialBounds<Flt64>(Flt64.Zero, new Flt64(10)),
                };
                return (structure, [y, indicator]);
            }
            case "masking": {
                // **mask 必须是二元列；条件值的边界就是它的上下界。**
                // **The mask must be a binary column; the conditional value's bounds are just a lower and an upper.**
                y.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(10)).Value);
                var result = new RealVar("mask-result");
                var mask = new BinVar("mask-flag");
                var structure = new MaskingStructure(
                    Value: new ConditionalValue(
                        Input: Linear(y),
                        ResultVariable: result,
                        Bounds: new ConditionBounds<Flt64>(Flt64.Zero, new Flt64(10)),
                        Name: kind),
                    Mask: mask,
                    Converter: IIntoValue<Flt64>.Identity);
                return (structure, [y, result, mask]);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown structure kind");
        }
    }

    private static LinearPolynomial<Flt64> Linear(IVariableItem variable) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, variable) }, Flt64.Zero);

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
        System.IO.DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (System.IO.File.Exists(System.IO.Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}
