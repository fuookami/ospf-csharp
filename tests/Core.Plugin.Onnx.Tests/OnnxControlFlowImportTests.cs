#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
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
/// 同步 Kotlin `dee897d2` 的 `OnnxLowering` 编排形态。**这一批把三个已经各自验证过的部分拼成一条路：** '
/// **解析子图属性 → 导入器识别控制流 → 求值器递归走分支体。**
///
/// **"端到端"该有的意思就在这里：** **不是"每部分都有一条判据"，而是"一个真实模型能从头走通，且结果与手算一致"。**
///
/// Syncs the orchestration shape of `OnnxLowering`. **This batch joins three separately verified parts into one path: parsing the subgraph attribute, the importer
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

        imported.IsOk.Should().BeFalse("the length of a non-constant scan is unknown at import time");
        imported.Failure.Should().Contain("常量");
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