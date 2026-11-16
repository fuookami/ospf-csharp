#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Onnx;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Core.Plugin.Gurobi;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// **从 ONNX 二次域一路走到 Gurobi 的最优值** / From the ONNX second-degree domain all the way to a Gurobi optimum.
/// </summary>
/// <remarks>
/// **前面的批次只证明了两端：** **ONNX 侧能产出二次多项式、求解器侧能解二次目标；** **中间那一段——"产出的多项式真能挂进元模型并求解"——**
/// **一直没验证过。** **这里补的就是那一段。**
///
/// **模型是 `(x − 3)²`：** **它在 `[0,10]` 上的最小值是 0（落在 `x = 3`），** **在 `[5,10]` 上是 4（落在 `x = 5`）。** '
/// **两条成对，理由与二次目标那批相同：** **只测 `[0,10]` 时，一个丢掉 `x²` 的一次模型会在 `x = 0` 给 9、一个"总返回 0"的实现也会通过；** '
/// **`[5,10]` 上的 4 能同时排除这两者。**
///
/// **Earlier work only proved the two ends: the ONNX side produces quadratic polynomials and the solver side solves a quadratic objective; the middle segment — "the polynomial
/// really attaches to a meta model and solves" — was never verified.** ** fills that segment.**
///
/// **The model is (x − 3)²: its minimum on [0,10] is 0 at x = 3, and on [5,10] it is 4 at x = 5.** The pair exists for the same reason as the quadratic-objective batch: **on [0,10] alone a
/// first-degree model without x² gives 9 at x = 0 and an implementation that always returns 0 would also pass; 4 on [5,10] rules out both.**
/// </remarks>
public class OnnxQuadraticSolverGurobiIntegrationTest
{
    /// <summary>**导入出来的二次多项式是 `(x − 3)²` 的形状** / The imported polynomial has the shape of (x − 3)².</summary>
    /// <remarks>
    /// **先钉住多项式本身，再钉最优值：** **否则"最优值是 0"可能来自一个完全不同的目标。**
    ///
    /// **Pin the polynomial first, then the optimum: otherwise "the optimum is zero" could come from an entirely different objective.**
    /// </remarks>
    [Fact]
    public void TheImportedObjective_ShouldBeTheSquaredShift() {
        OnnxImporter.Imported imported = ImportShiftSquared();

        imported.IsOk.Should().BeTrue(imported.Failure);
        imported.OutputQuadraticElements.Should().HaveCount(1);
        QuadraticPolynomial<Flt64> polynomial = imported.OutputQuadraticElements![0];

        polynomial.Monomials.Should().HaveCount(2, "x·x merges into x² and the cross terms merge into one x term");
        polynomial.Constant.ToDouble().Should().BeApproximately(9.0, 1e-9, "(x − 3)² has a constant term of 9");

        var squared = polynomial.Monomials.Where(monomial => monomial.Symbol2 is not null).ToList();
        squared.Should().HaveCount(1, "exactly one quadratic term");
        squared[0].Coefficient.ToDouble().Should().BeApproximately(1.0, 1e-9);

        var linear = polynomial.Monomials.Where(monomial => monomial.Symbol2 is null).ToList();
        linear.Should().HaveCount(1);
        linear[0].Coefficient.ToDouble().Should().BeApproximately(-6.0, 1e-9, "the cross term 2·x·(−3) is −6x");
    }

    /// <summary>**`[0,10]` 上最小值是 0** / On [0,10] the minimum is zero.</summary>
    [Fact]
    public async Task TheImportedObjective_ShouldReachZeroOnWideBounds() {
        OnnxImporter.Imported imported = ImportShiftSquared();
        imported.IsOk.Should().BeTrue(imported.Failure);

        FeasibleSolverOutput<Flt64> solved = await Solve(imported, 0.0, 10.0);

        solved.Obj.ToDouble().Should().BeApproximately(0.0, 1e-5,
            "the minimum of (x − 3)² is zero at x = 3; a model missing x² would stop at 9 on this bound");
    }

    /// <summary>**`[5,10]` 上最小值是 4** / On [5,10] the minimum is four.</summary>
    /// <remarks>
    /// **这一条同时排除两种错：** **丢掉 `x²` 会在 `x = 0` 给 9（这个界上不可行，会落到 5，给 2），** **而"总返回 0"的实现会直接给 0。** '
    /// **正确答案 4 落在 `x = 5`，两者都不对。**
    ///
    /// **This rules out two mistakes at once: dropping x² puts the optimum at x = 0, which is infeasible here so it lands on 5 with 2, and an implementation that always returns 0 would give 0.**
    /// **The right answer is 4 at x = 5, and neither mistake produces it.**
    /// </remarks>
    [Fact]
    public async Task TheImportedObjective_ShouldSitOnTheBound() {
        OnnxImporter.Imported imported = ImportShiftSquared();
        imported.IsOk.Should().BeTrue(imported.Failure);

        FeasibleSolverOutput<Flt64> solved = await Solve(imported, 5.0, 10.0);

        solved.Obj.ToDouble().Should().BeApproximately(4.0, 1e-5, "(x − 3)² at x = 5 is 4");
    }

    /// <summary>
    /// 用导入出来的变量求解 / Solve with the imported variable.
    /// </summary>
    /// <remarks>
    /// **变量必须取自导入结果，不能在这里新建一个同名 `RealVar`：** **`RealVar("x")` 两次会得到两个不同实例，** '
    /// **而元模型的 token 表里只有我先加的那个——** **目标里的单项式指向另一个实例，** **机制模型因此为空，报错是"模型为空"。** '
    /// **那条消息不指向命名冲突，它指向"表里没有这个符号"。**
    ///
    /// **The variable has to come from the import result rather than a fresh RealVar of the same name: two RealVar("x") calls give two different instances,** and the meta model's token table only
    /// holds the one added first — **the objective's monomials point at the other instance, the mechanism model comes out empty, and the error says "Model is null".** That message points at "the
    /// symbol is not in the table", not at a naming clash.
    /// </remarks>
    private static async Task<FeasibleSolverOutput<Flt64>> Solve(
        OnnxImporter.Imported imported,
        double lower,
        double upper) {
        QuadraticPolynomial<Flt64> polynomial = imported.OutputQuadraticElements![0];
        using var meta = new QuadraticMetaModel<Flt64>();
        RealVar x = imported.Variables!["x"];
        MustSucceed(meta.Add(x), "the variable has to be in the token table before anything references it");

        foreach (LinearInequality<Flt64> bound in Bounds(x, lower, upper)) {
            MustSucceed(meta.AddConstraint(relation: bound, group: null), "a bound has to be accepted before the objective is attached");
        }

        var flatten = new QuadraticFlattenData<Flt64>(polynomial.Monomials, polynomial.Constant);
        MustSucceed(meta.AddObject(ObjectCategory.Minimum, flatten, "onnx-quadratic", null),
            "QuadraticMetaModel carries the quadratic AddObject overload");

        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(meta)).Value;
        using QuadraticTetradModel matrix = mechanism.ToQuadraticTetradModel().Value;

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiQuadraticSolver().InvokeAsync((IQuadraticTetradModelView)matrix);
        solved.IsOk.Should().BeTrue(solved.ToString());
        return solved.Value;
    }

    private static OnnxImporter.Imported ImportShiftSquared() => OnnxImporter.Import(
        ShiftSquaredModelBytes(),
        new OnnxImportOptions(
            mode: OnnxImportMode.Quadratic,
            inputBounds: new Dictionary<string, OnnxBounds> { ["x"] = new OnnxBounds(-10.0, 10.0) }),
        new Dictionary<string, IReadOnlyList<RealVar>> {
            ["x"] = new[] { new RealVar("x") },
        });

    // t = x − 3（常量 3 来自 initializer）；y = t · t；输出 y。
    private static byte[] ShiftSquaredModelBytes() => Concat(
        VarintField(1, 8),
        BytesField(8, Concat(StringField(1, ""), VarintField(2, 13))),
        BytesField(7, Concat(
            Node("Sub", new[] { "x", "k" }, "t"),
            Node("Mul", new[] { "t", "t" }, "y"),
            StringField(2, "g"),
            BytesField(5, TensorBytes("k", new long[] { 1, 1 }, new[] { 3f })),
            BytesField(11, ValueInfo2("x", 1, 1)),
            BytesField(12, ValueInfo2("y", 1, 1)))));

    private static byte[] TensorBytes(string name, long[] dims, float[] values) => Concat(
        PackedVarintField(1, dims),
        VarintField(2, 1),
        PackedFloatField(4, values),
        StringField(8, name));

    // ValueInfoProto：名字 + 类型（elem_type = 1、形状各维）。与 ONNX 测试里同一个编码。
    private static byte[] ValueInfo2(string name, params long[] dims) {
        var dtype = Concat(VarintField(1, 1), BytesField(2, ShapeProto(dims)));
        return Concat(StringField(1, name), BytesField(2, BytesField(1, dtype)));
    }

    private static byte[] ShapeProto(long[] dims) {
        var parts = new List<byte[]>();
        foreach (long dim in dims) {
            parts.Add(BytesField(1, VarintField(1, dim)));
        }
        return Concat(parts.ToArray());
    }

    private static byte[] Node(string opType, string[] inputs, string output) {
        var parts = new List<byte[]>();
        foreach (string input in inputs) {
            parts.Add(StringField(1, input));
        }
        parts.Add(StringField(2, output));
        parts.Add(StringField(4, opType));
        // 节点要包在 `GraphProto.node`（第 1 字段）里；**不包的话它会被当成图输入，而报错是"变长整数被截断"——** '
        // **那条消息指向字节层面，离真正的原因有两层远。**
        //
        // A node has to be wrapped in GraphProto.node (field 1); **unwrapped it is read as a graph input and the error says "truncated varint" —** a byte-level message two layers away
        // from the cause.
        return BytesField(1, Concat(parts.ToArray()));
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

    private static byte[] StringField(int field, string value) => BytesField(field, System.Text.Encoding.UTF8.GetBytes(value));

    private static byte[] BytesField(int field, byte[] payload) => Concat(Tag(field, 2), Varint((ulong)payload.Length), payload);

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

    private static byte[] Concat(params byte[][] parts) {
        var result = new List<byte>();
        foreach (byte[] part in parts) {
            result.AddRange(part);
        }
        return result.ToArray();
    }

    private static void MustSucceed(Try step, string why) => step.IsFailed.Should().BeFalse(why);

    private static IReadOnlyList<LinearInequality<Flt64>> Bounds(RealVar x, double lower, double upper) {
        OnnxBoundsConstraints.Generated generated = OnnxBoundsConstraints.ForAll(
            new Dictionary<string, ISymbol> { ["x"] = x },
            new Dictionary<string, OnnxBounds> { ["x"] = new OnnxBounds(lower, upper) },
            out IReadOnlyList<string> missing);
        generated.Failure.Should().BeNull(generated.Failure);
        missing.Should().BeEmpty();
        return generated.Constraints;
    }
}