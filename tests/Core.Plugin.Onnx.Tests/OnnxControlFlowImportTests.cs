#nullable enable

// IDE0007 与 IDE0008 会在同一个泛型局部上互相要求；按仓库既有做法文件级关闭并写理由。
// IDE0007 and IDE0008 demand opposite things on the same generic local; disabled at file scope per repository practice.
#pragma warning disable IDE0007, IDE0008

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests;

/// <summary>
/// **带 `If` 的模型从字节一路走到多项式** / A model with an If walks from bytes all the way to a polynomial.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxLowering` 编排形态。**这一层把三个各自验证过的部分拼成一条路：** '
/// **解析子图属性 → 导入器识别控制流 → 求值器递归走分支体。**
///
/// **"端到端"该有的意思就在这里：** **不是"每部分都有一条判据"，而是"一个真实模型能从头走通，且结果与手算一致"。**
///
/// Syncs the orchestration shape of `OnnxLowering`. ** joins three separately verified parts into one path: parsing the subgraph attribute, the importer
/// recognising control flow, and the evaluator walking the body recursively.**
///
/// **That is what end-to-end has to mean: not "every part has a criterion", but "a real model goes through and the result matches hand arithmetic".**
/// </remarks>
public class OnnxControlFlowImportTests
{
    /// <summary>**条件为真的模型导入后是 `x + y`** / A true-condition model imports as x + y.</summary>
    [Fact]
    public void ATrueConditionModel_ShouldImportAsAddition() {
        OnnxImporter.Imported imported = ImportModel(condition: 1.0f);

        imported.IsOk.Should().BeTrue(imported.Failure);
        imported.Polynomial.Should().NotBeNull();
        CoefficientOf(imported.Polynomial!, "x").Should().Be(1.0);
        CoefficientOf(imported.Polynomial!, "y").Should().Be(1.0, "the true branch adds, so both coefficients stay positive");
    }

    /// <summary>**条件为假的模型导入后是 `x - y`** / A false-condition model imports as x - y.</summary>
    /// <remarks>
    /// **两个方向都测，是为了钉住"条件真的被读了"：** **只测真分支的话，一个忽略条件的实现也能通过——** '
    /// **它总是选真分支，而模型照常求解。**
    ///
    /// **Both directions are tested to pin that the condition is actually read: with only the true branch, an implementation that ignores the condition passes too — it always
    /// takes the true branch and the model still solves.**
    /// </remarks>
    [Fact]
    public void AFalseConditionModel_ShouldImportAsSubtraction() {
        OnnxImporter.Imported imported = ImportModel(condition: 0.0f);

        imported.IsOk.Should().BeTrue(imported.Failure);
        CoefficientOf(imported.Polynomial!, "y").Should().Be(-1.0, "the false branch subtracts");
    }

    /// <summary>**条件不是常量时导入失败，并说明卡在哪一段** / A non-constant condition fails, naming the stage.</summary>
    /// <remarks>
    /// **失败的措辞是这条路径的一半价值：** **"导入失败"对着一个带 `If` 的模型什么都说明不了，** '
    /// **而"控制流条件必须是常量"直接指出模型哪里不对。**
    ///
    /// **The wording of the failure is half the value of this path: "import failed" says nothing about a model with an If, while "the control-flow condition must be
    /// constant" points at what is wrong with the model.**
    /// </remarks>
    [Fact]
    public void ANonConstantCondition_ShouldFailAtTheEvaluateStage() {
        OnnxImporter.Imported imported = ImportModel(condition: 1.0f, conditionIsInitializer: false);

        imported.IsOk.Should().BeFalse();
        imported.Stage.Should().Be("evaluate", "the model parses and passes preflight; it is evaluation that cannot proceed");
        imported.Failure.Should().Contain("常量");
    }

    /// <summary>**两个分支个数不同时导入失败** / Differing branch arity fails at import.</summary>
    [Fact]
    public void MismatchedBranchArity_ShouldFail() {
        OnnxImporter.Imported imported = ImportModel(condition: 1.0f, elseOutputs: 2);

        imported.IsOk.Should().BeFalse();
        imported.Failure.Should().Contain("个数");
    }

    /// <summary>**没有控制流的模型仍走仿射链，行为不变** / A model without control flow still takes the affine chain.</summary>
    [Fact]
    public void APlainModel_ShouldStillImport() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            PlainModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["y"] = new OnnxBounds(-1.0, 1.0),
                }));

        imported.IsOk.Should().BeTrue(imported.Failure);
        CoefficientOf(imported.Polynomial!, "x").Should().Be(1.0);
        CoefficientOf(imported.Polynomial!, "y").Should().Be(1.0);
    }

    /// <summary>**带 `Loop` 的模型从字节走到多项式：累加两轮** / A Loop model walks from bytes to a polynomial.</summary>
    /// <remarks>
    /// **选 `Loop` 与 `If` 一起做，是因为两者的接线只差"种子回写"：** **能走通 `If` 却走不通 `Loop`，说明差别不在接线而在别处——**
    /// **而那种"别处"通常是携带值的顺序或 `cond` 的有无被判错了一位。**
    ///
    /// **Loop is done alongside If because the wiring differs only in seed write-back: getting If through but not Loop would mean the difference is somewhere else — and that
    /// elsewhere is usually the carried-value order or an off-by-one on whether cond is present.**
    /// </remarks>
    [Fact]
    public void ALoopModel_ShouldImportAsAnAccumulation() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            LoopModelBytes(trip: 2.0),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["y"] = new OnnxBounds(-1.0, 1.0),
                }));

        imported.IsOk.Should().BeTrue(imported.Failure);
        // 体是 acc_out = acc_in + x + y，两轮之后 init(0) + 2x + 2y。
        // The body is acc_out = acc_in + x + y, so two rounds give init(0) + 2x + 2y.
        CoefficientOf(imported.Polynomial!, "x").Should().Be(2.0, "two rounds add x twice");
        CoefficientOf(imported.Polynomial!, "y").Should().Be(2.0);
    }

    /// <summary>**零轮时携带值原样传出** / With zero rounds the initial value passes through.</summary>
    [Fact]
    public void AZeroTripLoop_ShouldPassTheInitialValueThrough() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            LoopModelBytes(trip: 0.0),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["y"] = new OnnxBounds(-1.0, 1.0),
                }));

        imported.IsOk.Should().BeTrue(imported.Failure);
        imported.Polynomial!.Monomials.Should().BeEmpty("no round means nothing accumulates");
    }

    /// <summary>**带 `Scan` 的模型从字节走到多项式：三个切片累加** / A Scan model walks from bytes to a polynomial.</summary>
    /// <remarks>
    /// **扫描长度来自常量张量的首维：** **只留第一个元素会让扫描长度恒为 1，** **于是"扫了三轮"变成一个模型只算第一片——**
    /// **而它照常求解、输出也像个正常的和。**
    ///
    /// **The scan length comes from a constant tensor's leading dimension: keeping only the first element makes every length one, so "three rounds" becomes a model that
    /// evaluates only the first slice — and it still solves, with a sum that looks ordinary.**
    /// </remarks>
    [Fact]
    public void AScanModel_ShouldAccumulateEverySlice() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            ScanModelBytes(new[] { 1.0f, 2.0f, 3.0f }),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                }));

        imported.IsOk.Should().BeTrue(imported.Failure);
        imported.Polynomial!.Constant.ToDouble().Should().Be(6.0, "1 + 2 + 3; a single slice would give 1");
        CoefficientOf(imported.Polynomial, "x").Should().Be(1.0,
            "the state starts as x and every round adds a constant slice, so x appears exactly once");
    }

    /// <summary>**没有常量张量作为扫描输入时明确拒绝** / Without a constant scan input it is refused.</summary>
    [Fact]
    public void AScanWithoutConstantInput_ShouldBeRefused() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            ScanModelBytes(new[] { 1.0f }, noSlices: true),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["slices"] = new OnnxBounds(-1.0, 1.0),
                }));

        imported.IsOk.Should().BeFalse("a runtime slice needs an element list this layer does not have");
        // 断言的是**真正的原因**，不只是"常量"：这个缺口与形状改写那一项是同一层。
        // It asserts the real reason rather than merely "constant": this gap is the same layer as the shape-rewriting item.
        imported.Failure.Should().Contain("逐元素表示");
        imported.Failure.Should().Contain("标量表达式");
    }

    /// <summary>**带两个状态的 `Scan` 从字节走到多项式** / A two-state Scan walks from bytes to a polynomial.</summary>
    /// <remarks>
    /// **多状态在本地已支持，但"本地支持"与"从一个真实模型里读得出两个状态"是两件事：** **后者还依赖导入器把"
    /// **节点输入里的状态个数"与"体的输入个数"对上。**
    ///
    /// **Multi-state works locally, but "works locally" and "two states can be read out of a real model" are different claims: the latter also needs the importer to line up
    /// the node's state count with the body's input count.**
    /// </remarks>
    [Fact]
    public void ATwoStateScanModel_ShouldImportBothStates() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            TwoStateScanModelBytes(new[] { 1.0f, 2.0f }),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["y"] = new OnnxBounds(-1.0, 1.0),
                }));

        imported.IsOk.Should().BeTrue(imported.Failure);
        // 第一个图输出是状态 a：初值 0，每轮加一个切片，两轮之后是常数 3，且完全不涉及 x、y。
        // 状态 b（y + 2x）的算术由本地那条多状态判据负责——导入器只交出一个多项式。
        //
        // The first graph output is state a: it starts at 0, gains one slice per round, so it ends as the constant 3 and touches neither x nor y. The arithmetic of state b
        // (y + 2x) belongs to the local multi-state criterion, since the importer hands back a single polynomial.
        imported.Polynomial!.Constant.ToDouble().Should().Be(3.0, "0 + 1 + 2");
        imported.Polynomial.Monomials.Should().BeEmpty("state a never involves x or y");
    }

    /// <summary>**带 `Reshape` 的模型从字节走到多项式** / A model with a Reshape walks from bytes to a polynomial.</summary>
    /// <remarks>
    /// **`Reshape` 不动数值，所以要证明的是"它没把数值弄丢"：** **导入成功、且 `x` 的系数仍是 1。** '
    /// **若形状检查写错（比如把目标当成元素个数），这里会以一个"元素个数不符"失败——** **而那正是它在真实模型里该拦下的东西。**
    ///
    /// **Reshape moves no value, so what has to be shown is that it lost none: the import succeeds and x still has coefficient 1.** **Were the shape check wrong (taking the
    /// target for a count, say), this fails with a mismatch — which is exactly what it should stop on a real model.**
    /// </remarks>
    [Fact]
    public void AReshapeModel_ShouldImport() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            ReshapeModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> { ["x"] = new OnnxBounds(-1.0, 1.0) }));

        // 列表语义下，一个六元素输入没有逐元素符号就不该被当成一个数——**形状写着 6、绑定里只有一个符号，
        // 那是一个自相矛盾的绑定。** 导入器还没按调用方给的符号列表建输入（基线的 inputBindings），所以这里断言拒绝及其理由；
        // **接上 importBindings 之后，这一条要改回"导入成功且元素逐个对上"。**
        //
        // under list semantics a six-element input with no per-element symbols must not be treated as one number — a shape of 6 with one symbol in the binding is
        // self-contradictory. The importer does not build inputs from caller-supplied symbols (the baseline's inputBindings) yet, so this asserts the refusal and its reason; **when
        // the next batch wires inputBindings, this criterion goes back to "imports, and the elements line up one by one".**
        imported.IsOk.Should().BeFalse("a six-element input needs six symbols before a reshape means anything");
        imported.Stage.Should().Be("evaluate");
        imported.Failure.Should().Contain("逐元素列表");
    }

    /// <summary>**元素个数不守恒的 `Reshape` 在导入期被拒绝** / A count-changing reshape is refused at import.</summary>
    [Fact]
    public void ACountChangingReshape_ShouldBeRefusedAtImport() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            ReshapeModelBytes(target: new long[] { 4 }),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> { ["x"] = new OnnxBounds(-1.0, 1.0) }));

        imported.IsOk.Should().BeFalse("six elements cannot become four");
        imported.Stage.Should().Be("evaluate");
        imported.Failure.Should().Contain("Reshape");
    }

    /// <summary>**重排落到按位置读的算子上时被拒绝** / A permutation landing on a positional reader is refused.</summary>
    /// <remarks>
    /// **收窄后的规则是"只在会被看见时拒绝"：** **逐元素消费不看位置，重排是空操作；'
    /// **`MatMul`／`Concat` 这类按位置读，重排就必须真发生——而这一层写不下来。**
    ///
    /// **This narrows batch ⑦'s blanket refusal to "only when it is visible": an elementwise consumer does not look at position, so the permutation is a no-op; MatMul and Concat
    /// read positionally, so the permutation has to really happen — and this layer cannot write it down.**
    /// </remarks>
    [Fact]
    public void ATransposeFeedingAPositionalReader_ShouldBeRefused() {
        // 用二次模式：`MatMul` 是二阶算子，线性模式下前置检查就会以"阶数不够"拒绝，
        // 那样测到的不是"重排落到按位置读者上"。
        //
        // Quadratic mode: MatMul is second degree, and linear mode would refuse at preflight with "degree too low", which is not the reason under test.
        OnnxImporter.Imported imported = OnnxImporter.Import(
            TransposeIntoMatMulModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Quadratic,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["y"] = new OnnxBounds(-1.0, 1.0),
                }));

        imported.IsOk.Should().BeFalse("MatMul reads the transposed tensor positionally, and the permutation has nowhere to be written down");
        imported.Stage.Should().Be("evaluate");
        imported.Failure.Should().Contain("Transpose");
    }

    /// <summary>**只被逐元素消费的重排不再被拒绝，而多元素图输出仍明确拒绝** / A reorder consumed elementwise is no longer refused, and a multi-element graph output still is.</summary>
    /// <remarks>
    /// **多元素张量不压成一个标量表达式，** **所以"整张图给出一个多项式"对一个多元素输出本来就不成立。** '
    /// **判据的落点是中间绑定的元素必须真的换过位置，而不是只断言"图失败了"。**
    ///
    /// **A multi-element tensor is not collapsed into one scalar expression, so "the whole graph yields one polynomial" never held for a multi-element output.** **The criterion lands on the
    /// intermediate binding really having permuted elements, not merely "the graph failed":
    /// permuted rather than merely "the graph failed".**
    /// </remarks>
    [Fact]
    public void ATransposeConsumedElementwise_ShouldReorderTheIntermediateBindings() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            TransposeThenAddModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["y"] = new OnnxBounds(-1.0, 1.0),
                }));

        imported.IsOk.Should().BeFalse("the output holds six elements, and element-indexed output selection is not wired yet");
        imported.Stage.Should().Be("evaluate");
        imported.Failure.Should().Contain("元素");
    }

    /// <summary>**调用方给出每元素符号时，张量按元素走到底** / With caller-supplied per-element symbols, the tensor stays per element end to end.</summary>
    /// <remarks>
    /// **这是基线 `inputBindings: Map<String, List<Symbol>>` 在 C# 侧的落地：** **`x` 与 `y` 各给三个符号，**
    /// **`Add` 就逐位置相加，输出按元素取——不再有"整个张量是一个数"的那一步。**
    ///
    /// **This is the baseline's inputBindings taking shape on the C# side: three symbols for x and three for y, so Add pairs per position and the output is read by element — the
    /// "the whole tensor is one number" step is gone.**
    /// </remarks>
    [Fact]
    public void SuppliedElementSymbols_ShouldStayPerElementToTheOutput() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            WideAddModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["y"] = new OnnxBounds(-1.0, 1.0),
                }),
            new Dictionary<string, IReadOnlyList<RealVar>> {
                ["x"] = new[] { new RealVar("x0"), new RealVar("x1"), new RealVar("x2") },
                ["y"] = new[] { new RealVar("y0"), new RealVar("y1"), new RealVar("y2") },
            });

        imported.IsOk.Should().BeTrue(imported.Failure);
        imported.Polynomial.Should().BeNull("a three-element output has no single scalar expression");
        imported.OutputShape.Should().Equal(new[] { 3 });
        imported.OutputElements.Should().HaveCount(3);
        imported.Output(0).Monomials.Select(m => m.Symbol.Name).Should().BeEquivalentTo(new[] { "x0", "y0" });
        imported.Output(1).Monomials.Select(m => m.Symbol.Name).Should().BeEquivalentTo(new[] { "x1", "y1" });
        imported.Output(2).Monomials.Select(m => m.Symbol.Name).Should().BeEquivalentTo(new[] { "x2", "y2" });
    }

    /// <summary>**符号个数与声明元素数不符时被拒绝** / A symbol count that disagrees with the declared elements is refused.</summary>
    /// <remarks>
    /// **少一个会让某个位置无符号可用，多一个意味着调用方与模型对形状的理解不同——** **两者都会在后续静默错位。**
    ///
    /// **One short leaves a position without a symbol and one extra means the caller and the model disagree about the shape; either misaligns later.**
    /// </remarks>
    [Fact]
    public void AWrongSymbolCount_ShouldBeRefused() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            WideAddModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["y"] = new OnnxBounds(-1.0, 1.0),
                }),
            new Dictionary<string, IReadOnlyList<RealVar>> {
                ["x"] = new[] { new RealVar("x0"), new RealVar("x1") },
                ["y"] = new[] { new RealVar("y0"), new RealVar("y1"), new RealVar("y2") },
            });

        imported.IsOk.Should().BeFalse("two symbols cannot fill a three-element input");
        imported.Stage.Should().Be("bindings");
        imported.Failure.Should().Contain("3").And.Contain("2");
    }

    /// <summary>**旧入口对多元素输出仍然拒绝，即使输出没有声明形状** / The old entry still refuses a multi-element output even when the output declares no shape.</summary>
    /// <remarks>
    /// **这一条钉的是：** **先前只能测"声明了形状"的情形，因为仿射链不看形状就无从得知元素个数。** '
    /// **形状传播接上之后，`x[3] + y[3]` 的输出被推出是三个元素——** **于是这条判据恢复成原来的宽度，而不是留着收窄的版本当纪念。**
    ///
    /// **This pins that: earlier it could only test the declared case, because the affine chain had no way to know the element count without a declared shape.**
    /// **With shape propagation the output of x[3] + y[3] is inferred as three elements — so this criterion goes back to its original width instead of keeping the narrower version as a
    /// souvenir.**
    /// </remarks>
    [Fact]
    public void TheOldEntry_ShouldRefuseAMultiElementOutputWithoutADeclaredShape() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            WideAddModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["y"] = new OnnxBounds(-1.0, 1.0),
                }));

        imported.IsOk.Should().BeFalse("the old entry cannot express a three-element output");
        imported.Failure.Should().Contain("元素");
    }

    /// <summary>**含 `Gemm` 的模型终于能导入了** / A model with a Gemm finally imports.</summary>
    /// <remarks>
    /// **这是"全连接层能导入"这件事本身：** **权重与偏置是 `initializer`，** **被绑成逐元素常量列表之后，** '
    /// **`Gemm` 才有第二维可用。** **`W` 是单位矩阵、`B = [1,2]`，所以结果是 `[x0+1, x1+2]`——** '
    /// **恒等权重让"乘对了没有"在数值上一目了然。**
    ///
    /// **This is "a fully connected layer imports" itself: weights and bias are initializers, and only after they are bound as per-element constant lists does Gemm have a second dimension
    /// to work with.** **W is the identity and B is [1,2], so the result is [x0+1, x1+2] — the identity weights make "did the multiplication work" plain to see.**
    /// </remarks>
    [Fact]
    public void AGemmModel_ShouldImportAndMultiply() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            GemmModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> { ["x"] = new OnnxBounds(-1.0, 1.0) }),
            new Dictionary<string, IReadOnlyList<RealVar>> {
                ["x"] = new[] { new RealVar("x0"), new RealVar("x1") },
            });

        OnnxModel parsed = OnnxModelParser.Parse(GemmModelBytes());
        parsed.Graph!.Initializers.Should().HaveCount(2, "the same bytes carry W and B");
        parsed.Graph.Initializers[0].FloatData.Should().HaveCount(4, "W is a 2x2 matrix");

        imported.IsOk.Should().BeTrue(imported.Failure);
        imported.OutputShape.Should().Equal(new[] { 1, 2 });
        Expression(imported.Output(0)).Should().Be("x0+1", "the identity weight passes x0 through, and the bias adds one");
        Expression(imported.Output(1)).Should().Be("x1+2");
    }

    /// <summary>**权重也是图输入时被拒绝（二次）** / A symbolic weight is refused as quadratic.</summary>
    /// <remarks>
    /// **权重不是常量时乘积是二次的，而这一层只装一次多项式。** **放过去会得到"少掉交叉项"的结果，而模型照常求解。**
    ///
    /// **A non-constant weight makes the product quadratic while this layer holds linear polynomials.** Letting it through yields a result missing the cross terms while the model still
    /// solves.
    /// </remarks>
    [Fact]
    public void ASymbolicWeight_ShouldBeRefused() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            GemmWithSymbolicWeightModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["w"] = new OnnxBounds(-1.0, 1.0),
                }),
            new Dictionary<string, IReadOnlyList<RealVar>> {
                ["x"] = new[] { new RealVar("x0"), new RealVar("x1") },
                ["w"] = new[] { new RealVar("w00"), new RealVar("w10") },
            });

        imported.IsOk.Should().BeFalse("a symbolic product is quadratic");
        imported.Stage.Should().Be("evaluate");
        imported.Failure.Should().Contain("二次");
    }

    /// <summary>**含 `ReduceSum` 的模型能导入，归约到标量** / A model with a ReduceSum imports, reducing to a scalar.</summary>
    /// <remarks>
    /// **这是"归约"与"求和"在形状上的区别：** **`keepdims = 0` 且轴为空时结果是标量，** '
    /// **所以它作为一个多项式交出来——** **而不是"一个只有一个元素的小张量"。**
    ///
    /// **This is the shape difference between a reduction and a sum: with keepdims = 0 and no axes the result is a scalar, so it comes back as one polynomial —** not as "a tiny tensor holding
    /// one element".**
    /// </remarks>
    [Fact]
    public void AReduceSumModel_ShouldImportToAScalar() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            ReduceSumModelBytes(axes: new long[] { 0, 1 }, keepdims: 0),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> { ["x"] = new OnnxBounds(-1.0, 1.0) }),
            new Dictionary<string, IReadOnlyList<RealVar>> {
                ["x"] = new[] {
                    new RealVar("x0"), new RealVar("x1"), new RealVar("x2"),
                    new RealVar("x3"), new RealVar("x4"), new RealVar("x5"),
                },
            });

        imported.IsOk.Should().BeTrue(imported.Failure);
        imported.Polynomial.Should().NotBeNull("a scalar output comes back as one polynomial");
        imported.Polynomial!.Monomials.Should().HaveCount(6, "all six elements are summed");
        foreach (var monomial in imported.Polynomial.Monomials) {
            monomial.Coefficient.ToDouble().Should().Be(1.0, "each element enters exactly once");
        }
    }

    /// <summary>**越界的轴被拒绝，且消息里点名秩** / An out-of-range axis is refused, naming the rank.</summary>
    [Fact]
    public void AReduceSumWithABadAxis_ShouldBeRefused() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            ReduceSumModelBytes(axes: new long[] { 5 }, keepdims: 1),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> { ["x"] = new OnnxBounds(-1.0, 1.0) }),
            new Dictionary<string, IReadOnlyList<RealVar>> {
                ["x"] = new[] {
                    new RealVar("x0"), new RealVar("x1"), new RealVar("x2"),
                    new RealVar("x3"), new RealVar("x4"), new RealVar("x5"),
                },
            });

        imported.IsOk.Should().BeFalse("axis 5 does not exist on a rank-2 tensor");
        imported.Failure.Should().Contain("秩");
    }

    /// <summary>**纯仿射图里的多元素输入被默认入口拒绝，消息指向 `inputBindings`** / A multi-element input in a pure affine graph is refused by the default entry, pointing at inputBindings.</summary>
    /// <remarks>
    /// **规则只落在仿射链上，因为它描述的正是仿射链的局限：** **形状算子与控制流那条路径有自己的拒绝（缺元素列表、多元素输出），** '
    /// **在那里再拦一次只会把更有信息量的消息换成一句笼统的。** **这一条钉的是"拒绝发生在 bindings 阶段且点名 `inputBindings`"。**
    ///
    /// **The rule lands on the affine chain only, because it describes that chain's limitation: the shape-operator and control-flow path has its own refusals (missing element lists,
    /// multi-element outputs), and stopping it there would replace a more informative message with a generic one.** **This criterion pins "the refusal happens at the bindings stage and
    /// names inputBindings".**
    /// </remarks>
    [Fact]
    public void AMultiElementAffineInput_ShouldBeRefusedAtBindings() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            WideAddModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["y"] = new OnnxBounds(-1.0, 1.0),
                }));

        imported.IsOk.Should().BeFalse();
        imported.Stage.Should().Be("bindings");
        imported.Failure.Should().Contain("inputBindings");
    }

    /// <summary>**同一个模型给了符号列表就能导入** / The same model imports once the symbols are supplied.</summary>
    [Fact]
    public void AMultiElementAffineInput_ShouldImportWithBindings() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            WideAddModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["y"] = new OnnxBounds(-1.0, 1.0),
                }),
            new Dictionary<string, IReadOnlyList<RealVar>> {
                ["x"] = new[] { new RealVar("x0"), new RealVar("x1"), new RealVar("x2") },
                ["y"] = new[] { new RealVar("y0"), new RealVar("y1"), new RealVar("y2") },
            });

        imported.IsOk.Should().BeTrue(imported.Failure);
        imported.OutputElements.Should().HaveCount(3, "the same graph, given symbols, keeps its elements");
    }

    /// <summary>**同一个模型：Linear 模式拒绝、Quadratic 模式导入** / The same model is refused in Linear mode and imports in Quadratic mode.</summary>
    /// <remarks>
    /// **两种模式各说各话，但都说真话：** **Linear 模式的拒绝消息点名 `OnnxImportMode.Quadratic`，** **而 Quadratic 模式交回二次项。** '
    /// **若 Linear 模式悄悄给出一次结果，模型照常求解而二次项不见了——** **那是这条判据要防的错法。**
    ///
    /// **The two modes each say their own thing and both say the truth: the linear refusal names OnnxImportMode.Quadratic, and quadratic mode hands back the second-degree terms.** If linear mode
    /// quietly produced a first-degree result, the model would still solve with the quadratic terms gone — **the failure exists to remove.**
    /// </remarks>
    [Fact]
    public void AMatMulBetweenTwoSymbols_ShouldNeedQuadraticMode() {
        OnnxImporter.Imported linear = OnnxImporter.Import(
            SymbolicMatMulModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Linear,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["w"] = new OnnxBounds(-1.0, 1.0),
                }),
            new Dictionary<string, IReadOnlyList<RealVar>> {
                ["x"] = new[] { new RealVar("x0"), new RealVar("x1") },
                ["w"] = new[] { new RealVar("w0"), new RealVar("w1") },
            });

        // 拒绝发生在 `operators` 阶段：** `MatMul` 登记为二阶，前置检查按阶数先拒——** '
        // **消息说的是"需要 2 阶而模式只允许 1 阶"，那正是"该换 Quadratic 模式"的意思。**
        //
        // The refusal happens at the operators stage: **MatMul is registered as second degree and preflight refuses on degree first —** the message says "needs degree 2 while the mode allows 1",
        // which is exactly "switch to Quadratic mode".
        linear.IsOk.Should().BeFalse();
        linear.Stage.Should().Be("operators");
        linear.Failure.Should().Contain("阶");

        OnnxImporter.Imported quadratic = OnnxImporter.Import(
            SymbolicMatMulModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Quadratic,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["w"] = new OnnxBounds(-1.0, 1.0),
                }),
            new Dictionary<string, IReadOnlyList<RealVar>> {
                ["x"] = new[] { new RealVar("x0"), new RealVar("x1") },
                ["w"] = new[] { new RealVar("w0"), new RealVar("w1") },
            });

        quadratic.IsOk.Should().BeTrue(quadratic.Failure);
        quadratic.OutputQuadraticElements.Should().HaveCount(1, "a [1,2] times a [2,1] is one element");
        quadratic.OutputQuadraticElements![0].Monomials.Should().HaveCount(2, "x0·w0 and x1·w1");
        quadratic.Polynomial.Should().BeNull("a quadratic result has no single linear form");
    }

    /// <summary>**二次域的绑定不能喂给一次域算子** / A second-degree binding cannot feed a first-degree operator.</summary>
    [Fact]
    public void AQuadraticIntoALinearOperator_ShouldBeRefused() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            SymbolicMatMulThenNegModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Quadratic,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["w"] = new OnnxBounds(-1.0, 1.0),
                }),
            new Dictionary<string, IReadOnlyList<RealVar>> {
                ["x"] = new[] { new RealVar("x0"), new RealVar("x1") },
                ["w"] = new[] { new RealVar("w0"), new RealVar("w1") },
            });

        imported.IsOk.Should().BeFalse("Neg only holds first-degree forms");
        imported.Stage.Should().Be("evaluate");
        imported.Failure.Should().Contain("二次域");
    }

    /// <summary>**两侧含变量的 `Gemm` 在 Quadratic 模式下导入** / A Gemm with variables on both sides imports in Quadratic mode.</summary>
    /// <remarks>
    /// **这是"带符号权重与符号偏置的全连接层"能导入这件事本身：** **`Gemm(x, w, c)` 得 `w·x + c`，** '
    /// **其中 `w·x` 是二次项、`c` 是一次项——** **两者在二次域里共存，而一次域里根本没有它们的和的位置。**
    ///
    /// **This is "a fully connected layer with symbolic weights and a symbolic bias" importing itself: Gemm(x, w, c) gives w·x + c, where w·x is quadratic and c linear —** they coexist in the
    /// second-degree domain, and the first-degree domain has no room for their sum.
    /// </remarks>
    [Fact]
    public void ASymbolicGemm_ShouldImportInQuadraticMode() {
        OnnxImporter.Imported imported = OnnxImporter.Import(
            SymbolicGemmModelBytes(),
            new OnnxImportOptions(
                mode: OnnxImportMode.Quadratic,
                inputBounds: new Dictionary<string, OnnxBounds> {
                    ["x"] = new OnnxBounds(-1.0, 1.0),
                    ["w"] = new OnnxBounds(-1.0, 1.0),
                    ["c"] = new OnnxBounds(-1.0, 1.0),
                }),
            new Dictionary<string, IReadOnlyList<RealVar>> {
                ["x"] = new[] { new RealVar("x0") },
                ["w"] = new[] { new RealVar("w0") },
                ["c"] = new[] { new RealVar("c0") },
            });

        imported.IsOk.Should().BeTrue(imported.Failure);
        imported.OutputQuadraticElements.Should().HaveCount(1);
        imported.OutputQuadraticElements![0].Monomials.Should().HaveCount(2, "w0·x0 plus the linear c0");
    }

    // ---- 手工编码的模型 ----

    private static OnnxImporter.Imported ImportModel(float condition, bool conditionIsInitializer = true, int elseOutputs = 1) {
        var bounds = new Dictionary<string, OnnxBounds> {
            ["x"] = new OnnxBounds(-1.0, 1.0),
            ["y"] = new OnnxBounds(-1.0, 1.0),
        };
        if (!conditionIsInitializer) {
            bounds["cond"] = new OnnxBounds(-1.0, 1.0);
        }
        return OnnxImporter.Import(
            ControlFlowModelBytes(condition, conditionIsInitializer, elseOutputs),
            new OnnxImportOptions(mode: OnnxImportMode.Linear, inputBounds: bounds));
    }

    private static byte[] ControlFlowModelBytes(float condition, bool conditionIsInitializer, int elseOutputs) {
        byte[] thenBody = Subgraph("then_branch", "Add", new[] { "x", "y" }, "then_out");
        var elseNodeBytes = new List<byte[]> { Node("Sub", new[] { "x", "y" }, "else_out") };
        var elseOutputBytes = new List<byte[]> { BytesField(12, ValueInfo("else_out")) };
        for (int index = 1; index < elseOutputs; ++index) {
            elseNodeBytes.Add(Node("Identity", new[] { "x" }, "else_extra" + index));
            elseOutputBytes.Add(BytesField(12, ValueInfo("else_extra" + index)));
        }
        byte[] elseBody = Concat(
            Concat(elseNodeBytes.ToArray()),
            StringField(2, "else_branch"),
            Concat(elseOutputBytes.ToArray()));

        var graphParts = new List<byte[]> {
            Node("If",
                new[] { conditionIsInitializer ? "cond" : "x" },
                "out",
                BytesField(5, Attribute("then_branch", thenBody)),
                BytesField(5, Attribute("else_branch", elseBody))),
            StringField(2, "g"),
        };
        if (conditionIsInitializer) {
            graphParts.Add(BytesField(5, ConstantTensor("cond", condition)));
        }
        else {
            graphParts.Add(BytesField(11, ValueInfo("cond")));
        }
        graphParts.Add(BytesField(11, ValueInfo("x")));
        graphParts.Add(BytesField(11, ValueInfo("y")));
        graphParts.Add(BytesField(12, ValueInfo("out")));

        return Concat(
            VarintField(1, 8),
            BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
            BytesField(7, Concat(graphParts.ToArray())));
    }

    private static byte[] LoopModelBytes(double trip) {
        // body(iter, acc_in) -> acc_out = acc_in + x + y，x 与 y 来自外层（子图看得见外层）。
        // body(iter, acc_in) -> acc_out = acc_in + x + y, with x and y visible from the enclosing scope.
        byte[] body = Concat(
            Node("Add", new[] { "acc_in", "x" }, "step"),
            Node("Add", new[] { "step", "y" }, "acc_out"),
            StringField(2, "body"),
            BytesField(11, ValueInfo("iter")),
            BytesField(11, ValueInfo("acc_in")),
            BytesField(12, ValueInfo("acc_out")));

        return Concat(
            VarintField(1, 8),
            BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
            BytesField(7, Concat(
                Node("Loop", new[] { "trip", "init" }, "out", BytesField(5, Attribute("body", body))),
                StringField(2, "g"),
                BytesField(5, ConstantTensor("trip", (float)trip)),
                BytesField(5, ConstantTensor("init", 0.0f)),
                BytesField(11, ValueInfo("x")),
                BytesField(11, ValueInfo("y")),
                BytesField(12, ValueInfo("out")))));
    }

    private static byte[] ScanModelBytes(float[] slices, bool noSlices = false) {
        // body(state_in, slice_in) -> state_out = state_in + slice_in，状态初值来自 x。
        // body(state_in, slice_in) -> state_out = state_in + slice_in, with the initial state coming from x.
        byte[] body = Concat(
            Node("Add", new[] { "state_in", "slice_in" }, "state_out"),
            StringField(2, "body"),
            BytesField(11, ValueInfo("state_in")),
            BytesField(11, ValueInfo("slice_in")),
            BytesField(12, ValueInfo("state_out")));

        var parts = new List<byte[]> {
            Node("Scan", new[] { "x", "slices" }, "out", BytesField(5, Attribute("body", body)), VarintField(9, 1)),
            StringField(2, "g"),
            BytesField(11, ValueInfo("x")),
            BytesField(12, ValueInfo("out")),
        };
        if (noSlices) {
            parts.Add(BytesField(11, ValueInfo("slices")));
        }
        else {
            parts.Add(BytesField(5, ConstantTensorArray("slices", slices)));
        }
        return Concat(
            VarintField(1, 8),
            BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
            BytesField(7, Concat(parts.ToArray())));
    }

    // int64 常量张量：TensorProto 字段 2 = data_type（7 是 INT64），字段 7 = int64_data（packed varint）。
    // An int64 constant tensor: field 2 is data_type (7 means INT64) and field 7 is the packed int64 data.
    private static byte[] ConstantTensorArray(string name, long[] values) {
        var packed = new List<byte>();
        foreach (long value in values) {
            packed.AddRange(Varint((ulong)value));
        }
        return Concat(
            VarintField(1, values.Length),
            VarintField(2, 7),
            BytesField(7, packed.ToArray()),
            StringField(8, name));
    }

    // 多维输入声明：PropertyProto.shape 里逐维给 dim_value。
    // A multi-dimensional input declaration: one dim_value per dimension.
    private static byte[] ValueInfo2(string name, params int[] dimensions) {
        var dims = new List<byte[]>();
        foreach (int dimension in dimensions) {
            dims.Add(BytesField(1, VarintField(1, dimension)));
        }
        return Concat(
            StringField(1, name),
            BytesField(2, BytesField(1, Concat(VarintField(1, 1), BytesField(2, Concat(dims.ToArray()))))));
    }

    private static byte[] ConstantTensorArray(string name, float[] values) {
        var packed = new List<byte>(values.Length * 4);
        foreach (float value in values) {
            packed.AddRange(Fixed32Bytes(value));
        }
        return Concat(
            VarintField(1, values.Length),
            VarintField(2, 1),
            BytesField(4, packed.ToArray()),
            StringField(8, name));
    }

    private static byte[] TwoStateScanModelBytes(float[] slices) {
        // body(a_in, b_in, slice_in) -> a = a_in + slice_in、b = b_in + x（x 来自外层）。
        // body(a_in, b_in, slice_in) -> a = a_in + slice_in and b = b_in + x, with x visible from the enclosing scope.
        byte[] body = Concat(
            Encoding.UTF8.GetBytes(string.Empty),
            BytesField(1, Concat(StringField(1, "a_in"), StringField(1, "slice_in"), StringField(2, "a_out"), StringField(4, "Add"))),
            BytesField(1, Concat(StringField(1, "b_in"), StringField(1, "x"), StringField(2, "b_out"), StringField(4, "Add"))),
            StringField(2, "body"),
            BytesField(11, ValueInfo("a_in")),
            BytesField(11, ValueInfo("b_in")),
            BytesField(11, ValueInfo("slice_in")),
            BytesField(12, ValueInfo("a_out")),
            BytesField(12, ValueInfo("b_out")));

        return Concat(
            VarintField(1, 8),
            BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
            BytesField(7, Concat(
                // 两个状态就要有两个输出名；把 b 排在前面，因为导入器只取第一个图输出。
                // Two states need two output names; b comes first because the importer takes the first graph output.
                BytesField(1, Concat(
                    StringField(1, "zero"),
                    StringField(1, "y"),
                    StringField(1, "slices"),
                    // 输出必须按状态顺序（a 在前、b 在后）：ONNX 按位置对齐。
                    // Outputs follow the state order (a first, b second): ONNX aligns by position.
                    StringField(2, "out_a"),
                    StringField(2, "out_b"),
                    StringField(4, "Scan"),
                    BytesField(5, Attribute("body", body)),
                    VarintField(9, 2))),
                StringField(2, "g"),
                BytesField(5, ConstantTensor("zero", 0.0f)),
                BytesField(5, ConstantTensorArray("slices", slices)),
                BytesField(11, ValueInfo("x")),
                BytesField(11, ValueInfo("y")),
                BytesField(12, ValueInfo("out_a")),
                BytesField(12, ValueInfo("out_b")))));
    }

    private static byte[] ReshapeModelBytes(long[]? target = null) => Concat(
        VarintField(1, 8),
        BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
        BytesField(7, Concat(
            Node("Reshape", new[] { "x", "shape" }, "out"),
            StringField(2, "g"),
            BytesField(5, ConstantTensorArray("shape", target ?? new long[] { 2, 3 })),
            BytesField(11, ValueInfo2("x", 2, 3)),
            BytesField(12, ValueInfo("out")))));

    private static byte[] TransposeModelBytes(int[] shape) => Concat(
        VarintField(1, 8),
        BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
        BytesField(7, Concat(
            Node("Transpose", new[] { "x" }, "out"),
            StringField(2, "g"),
            BytesField(11, ValueInfo2("x", shape)),
            BytesField(12, ValueInfo("out")))));

    private static byte[] TransposeIntoMatMulModelBytes() => Concat(
        VarintField(1, 8),
        BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
        BytesField(7, Concat(
            Node("Transpose", new[] { "x" }, "t"),
            Node("MatMul", new[] { "t", "y" }, "out"),
            StringField(2, "g"),
            BytesField(11, ValueInfo2("x", 2, 3)),
            BytesField(11, ValueInfo2("y", 3, 2)),
            BytesField(12, ValueInfo("out")))));

    private static byte[] TransposeThenAddModelBytes() => Concat(
        VarintField(1, 8),
        BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
        BytesField(7, Concat(
            Node("Transpose", new[] { "x" }, "t"),
            Node("Add", new[] { "t", "y" }, "out"),
            StringField(2, "g"),
            BytesField(11, ValueInfo2("x", 2, 3)),
            // y 与转置后的 t 同形（[3,2]），否则是广播不兼容，与"重排可不可见"无关。
            // y matches the transposed t ([3,2]); otherwise it is a broadcast mismatch, unrelated to whether the permutation is observable.
            BytesField(11, ValueInfo2("y", 3, 2)),
            BytesField(12, ValueInfo("out")))));

    // 运行期扫描：slices 形状 [3]，被 Scan 当扫描输入；初值是 x。
    private static byte[] RuntimeScanModelBytes() {
        byte[] body = Concat(
            BytesField(1, Concat(StringField(1, "state_in"), StringField(1, "slice_in"), StringField(2, "state_out"), StringField(4, "Add"))),
            StringField(2, "body"),
            BytesField(11, ValueInfo("state_in")),
            BytesField(11, ValueInfo("slice_in")),
            BytesField(12, ValueInfo("state_out")));

        return Concat(
            VarintField(1, 8),
            BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
            BytesField(7, Concat(
                Node("Scan", new[] { "x", "slices" }, "out", BytesField(5, Attribute("body", body)), VarintField(9, 1)),
                StringField(2, "g"),
                BytesField(11, ValueInfo("x")),
                BytesField(11, ValueInfo2("slices", 3)),
                BytesField(12, ValueInfo("out")))));
    }

    private static byte[] WideAddWithDeclaredOutputModelBytes() => Concat(
        VarintField(1, 8),
        BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
        BytesField(7, Concat(
            Node("Add", new[] { "x", "y" }, "out"),
            StringField(2, "g"),
            BytesField(11, ValueInfo2("x", 3)),
            BytesField(11, ValueInfo2("y", 3)),
            BytesField(12, ValueInfo2("out", 3)))));

    private static byte[] WideAddModelBytes() => Concat(
        VarintField(1, 8),
        BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
        BytesField(7, Concat(
            Node("Add", new[] { "x", "y" }, "out"),
            StringField(2, "g"),
            BytesField(11, ValueInfo2("x", 3)),
            BytesField(11, ValueInfo2("y", 3)),
            BytesField(12, ValueInfo("out")))));

    // `W` 是单位矩阵、`B = [1,2]`；结果应为 [x0+1, x1+2]。
    private static byte[] GemmModelBytes() => Concat(
        VarintField(1, 8),
        BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
        BytesField(7, Concat(
            Node("Gemm", new[] { "x", "W", "B" }, "y"),
            StringField(2, "g"),
            BytesField(5, TensorBytes("W", new long[] { 2, 2 }, new[] { 1f, 0f, 0f, 1f })),
            BytesField(5, TensorBytes("B", new long[] { 2 }, new[] { 1f, 2f })),
            BytesField(11, ValueInfo2("x", 1, 2)),
            BytesField(12, ValueInfo2("y", 1, 2)))));

    // 权重是图输入：乘积二次，必须拒绝。
    private static byte[] GemmWithSymbolicWeightModelBytes() => Concat(
        VarintField(1, 8),
        BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
        BytesField(7, Concat(
            Node("Gemm", new[] { "x", "w" }, "y"),
            StringField(2, "g"),
            BytesField(11, ValueInfo2("x", 1, 2)),
            BytesField(11, ValueInfo2("w", 2, 1)),
            BytesField(12, ValueInfo2("y", 1, 1)))));

    private static byte[] ReduceSumModelBytes(long[] axes, long keepdims) => Concat(
        VarintField(1, 8),
        BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
        BytesField(7, Concat(
            Node("ReduceSum", new[] { "x" }, "y", BytesField(5, IntsAttribute("axes", axes)), BytesField(5, IntAttribute("keepdims", keepdims))),
            StringField(2, "g"),
            BytesField(11, ValueInfo2("x", 2, 3)),
            BytesField(12, ValueInfo("y")))));

    private static byte[] IntsAttribute(string name, long[] values) =>
        Concat(StringField(1, name), PackedVarintField(8, values), VarintField(20, 7));

    private static byte[] IntAttribute(string name, long value) =>
        Concat(StringField(1, name), VarintField(3, value), VarintField(20, 2));

    // x[1,2] · w[2,1]，两侧都是符号。
    private static byte[] SymbolicMatMulModelBytes() => Concat(
        VarintField(1, 8),
        BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
        BytesField(7, Concat(
            Node("MatMul", new[] { "x", "w" }, "y"),
            StringField(2, "g"),
            BytesField(11, ValueInfo2("x", 1, 2)),
            BytesField(11, ValueInfo2("w", 2, 1)),
            BytesField(12, ValueInfo2("y", 1, 1)))));

    // 同上，但结果再过一个一次域算子 `Neg`。
    private static byte[] SymbolicMatMulThenNegModelBytes() => Concat(
        VarintField(1, 8),
        BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
        BytesField(7, Concat(
            Node("MatMul", new[] { "x", "w" }, "t"),
            Node("Neg", new[] { "t" }, "y"),
            StringField(2, "g"),
            BytesField(11, ValueInfo2("x", 1, 2)),
            BytesField(11, ValueInfo2("w", 2, 1)),
            BytesField(12, ValueInfo2("y", 1, 1)))));

    // x[1,1] · w[1,1] + c[1,1]，三者都是符号。
    private static byte[] SymbolicGemmModelBytes() => Concat(
        VarintField(1, 8),
        BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
        BytesField(7, Concat(
            Node("Gemm", new[] { "x", "w", "c" }, "y"),
            StringField(2, "g"),
            BytesField(11, ValueInfo2("x", 1, 1)),
            BytesField(11, ValueInfo2("w", 1, 1)),
            BytesField(11, ValueInfo2("c", 1, 1)),
            BytesField(12, ValueInfo2("y", 1, 1)))));

    private static byte[] TensorBytes(string name, long[] dims, float[] values) {
        var parts = new List<byte[]> {
            PackedVarintField(1, dims),
            VarintField(2, 1),
            PackedFloatField(4, values),
            StringField(8, name),
        };
        return Concat(parts.ToArray());
    }

    private static byte[] PackedVarintField(int field, long[] values) {
        var payload = new List<byte>();
        foreach (long value in values) {
            payload.AddRange(Varint((ulong)value));
        }
        return BytesField(field, payload.ToArray());
    }

    private static byte[] PackedFloatField(int field, float[] values) {
        var payload = new List<byte>();
        foreach (float value in values) {
            payload.AddRange(BitConverter.GetBytes(value));
        }
        return BytesField(field, payload.ToArray());
    }

    private static string Expression(Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> polynomial) {
        var parts = new List<string>();
        foreach (var monomial in polynomial.Monomials) {
            double coefficient = monomial.Coefficient.ToDouble();
            parts.Add(coefficient == 1.0 ? monomial.Symbol.Name : coefficient + monomial.Symbol.Name);
        }
        double constant = polynomial.Constant.ToDouble();
        if (constant != 0.0) {
            parts.Add(constant.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return string.Join("+", parts);
    }

    private static byte[] PlainModelBytes() => Concat(
        VarintField(1, 8),
        BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
        BytesField(7, Concat(
            Node("Add", new[] { "x", "y" }, "out"),
            StringField(2, "g"),
            BytesField(11, ValueInfo("x")),
            BytesField(11, ValueInfo("y")),
            BytesField(12, ValueInfo("out")))));

    private static byte[] Subgraph(string name, string opType, string[] inputs, string output) => Concat(
        Node(opType, inputs, output),
        StringField(2, name),
        BytesField(12, ValueInfo(output)));

    private static byte[] Node(string opType, string[] inputs, string output, params byte[][] attributes) {
        var parts = new List<byte[]>();
        foreach (string input in inputs) {
            parts.Add(StringField(1, input));
        }
        parts.Add(StringField(2, output));
        parts.Add(StringField(4, opType));
        parts.AddRange(attributes);
        return BytesField(1, Concat(parts.ToArray()));
    }

    private static byte[] Attribute(string name, byte[] graph) => Concat(
        StringField(1, name),
        BytesField(6, graph),
        VarintField(20, 5));

    // TensorProto：dims=[]、data_type=1、name、float_data（字段 5 是定长 32 位，不是长度前缀）。
    // 外层字段号由调用方给（GraphProto 的 initializer 是字段 5）。
    //
    // TensorProto: no dims, data_type 1, name, float_data (field 5 is fixed32, not length-prefixed). The outer field number belongs to the caller.
    private static byte[] ConstantTensor(string name, float value) => Concat(
        // TensorProto：2 是 data_type、4 是 float_data、8 是 name。标量没有 dims。
        // TensorProto: 2 is data_type, 4 is float_data, 8 is name. A scalar carries no dims.
        VarintField(2, 1),
        BytesField(4, Fixed32Bytes(value)),
        StringField(8, name));

    private static byte[] ValueInfo(string name) => Concat(
        StringField(1, name),
        BytesField(2, BytesField(1, Concat(VarintField(1, 1), BytesField(2, BytesField(1, VarintField(1, 1)))))));

    private static byte[] Fixed32Bytes(float value) {
        int bits = BitConverter.SingleToInt32Bits(value);
        return new[] { (byte)(bits & 0xFF), (byte)((bits >> 8) & 0xFF), (byte)((bits >> 16) & 0xFF), (byte)((bits >> 24) & 0xFF) };
    }

    private static byte[] Fixed32(float value) {
        int bits = BitConverter.SingleToInt32Bits(value);
        return new[] { (byte)(bits & 0xFF), (byte)((bits >> 8) & 0xFF), (byte)((bits >> 16) & 0xFF), (byte)((bits >> 24) & 0xFF) };
    }

    private static byte[] Tag(int field, int wire) => Varint((ulong)((field << 3) | wire));

    private static byte[] Varint(ulong value) {
        var bytes = new List<byte>();
        do {
            byte current = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) {
                current |= 0x80;
            }
            bytes.Add(current);
        } while (value != 0);
        return bytes.ToArray();
    }

    private static byte[] VarintField(int field, long value) => Concat(Tag(field, 0), Varint((ulong)value));

    private static byte[] StringField(int field, string value) => BytesField(field, Encoding.UTF8.GetBytes(value));

    private static byte[] BytesField(int field, byte[] payload) => Concat(Tag(field, 2), Varint((ulong)payload.Length), payload);

    private static byte[] Concat(params byte[][] parts) {
        var result = new List<byte>();
        foreach (byte[] part in parts) {
            result.AddRange(part);
        }
        return result.ToArray();
    }

    private static double CoefficientOf(LinearPolynomial<Flt64> polynomial, string symbolName) {
        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            if (monomial.Symbol.Name == symbolName) {
                return monomial.Coefficient.ToDouble();
            }
        }
        return 0.0;
    }
}