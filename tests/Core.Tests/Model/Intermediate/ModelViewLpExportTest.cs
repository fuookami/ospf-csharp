#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Core.Variable.Integer;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

public class ModelViewLpExportTest {
    /// <summary>
    /// 比较前统一行尾 / Normalise line endings before comparing.
    /// </summary>
    /// <remarks>
    /// **多行字面量的换行就是源文件的换行：** **这份文件在工作区是 CRLF、而导出器写的是 `\n`，** '
    /// **于是断言会变成"在比行尾而不是比内容"。** **统一之后再比，判据仍然逐字符钉住文本（含缩进与空行）。**
    ///
    /// **A multi-line literal carries the source file's line endings: this file is CRLF in the working tree while the exporter writes `\n`, so the assertion would be comparing line endings
    /// rather than text.** Normalising first keeps the criterion character-exact on the text, indentation and blank lines included.
    /// </remarks>
    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void LinearExport_ShouldMatchGoldenTextAndVariableCategories() {
        using LinearTriadModel model = LinearFixture();
        string text = Export(model);

        Normalise(text).Should().Be(Normalise(ExpectedLinear + "\n"));
        model.ContainsContinuous.Should().BeTrue();
        model.ContainsBinary.Should().BeTrue();
        model.ContainsInteger.Should().BeTrue();
        model.ContainsNotBinaryInteger.Should().BeTrue();
    }

    [Fact]
    public void EmptyObjective_ShouldKeepBaselineBlankLines() {
        using var model = new LinearTriadModel(
            variables: [
                new ModelViewVariable(
                    index: 0,
                    lowerBound: Flt64.Zero,
                    upperBound: Flt64.One,
                    type: Continuous.Instance,
                    name: "x"
                ),
            ],
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(),
                signs: [],
                rhs: [],
                names: [],
                sources: []
            ),
            objective: new Objective<LinearObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: []
            ),
            name: "empty"
        );

        Export(model).Should().Be(
            "Minimum\n\n\nSubject To\n\nBounds\n 0 <= x <= 1\n\nEnd\n");
    }

    [Fact]
    public void QuadraticExport_ShouldUseSolverReadableQuadraticBlocks() {
        using QuadraticTetradModel model = QuadraticFixture();

        Normalise(Export(model)).Should().Be(Normalise(ExpectedQuadratic + "\n"));
    }

    [Fact]
    public void Export_ShouldWriteDirectoryModelFileAsUtf8WithoutBom() {
        using LinearTriadModel model = LinearFixture();
        string directory = CreateTemporaryDirectory();
        try {
            model.Export(directory, ModelFileFormat.LP).IsOk.Should().BeTrue();
            string path = Path.Combine(directory, "linear-export.lp");
            File.Exists(path).Should().BeTrue();
            byte[] bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3
                && bytes[0] == 0xef
                && bytes[1] == 0xbb
                && bytes[2] == 0xbf);
            Normalise(Encoding.UTF8.GetString(bytes)).Should().Be(Normalise(ExpectedLinear + "\n"));
        }
        finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Export_ShouldConvertIoFailureToResult() {
        using LinearTriadModel model = LinearFixture();
        string directory = CreateTemporaryDirectory();
        try {
            string path = Path.Combine(directory, "missing", "model.lp");

            Try result = model.Export(path, ModelFileFormat.LP);

            Assert.Equal(
                ErrorCode.ApplicationFailed,
                Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(result).Code);
        }
        finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InvalidColumn_ShouldFailBeforeWriting() {
        using LinearTriadModel model = LinearModelWithCell(
            new LinearConstraintCell(
                RowIndex: 0,
                ColIndex: 1,
                Coefficient: Flt64.One
            ));
        Assert.Equal(1, model.Constraints.Lhs[0][0].ColIndex);
        MemoryStream stream = new();
        using StreamWriter writer = NewWriter(stream);

        Try result = model.ExportLP(writer);

        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(result).Code);
        stream.Length.Should().Be(0);
    }

    [Fact]
    public void ClosedModelAndNullWriter_ShouldReturnResultFailures() {
        using LinearTriadModel model = LinearFixture();
        using LinearTriadModel nullWriterModel = LinearFixture();
        MemoryStream stream = new();
        using StreamWriter writer = NewWriter(stream);
        model.Dispose();

        Assert.Equal(
            ErrorCode.ApplicationStopped,
            Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
                model.ExportLP(writer)).Code);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
                nullWriterModel.ExportLP(null)).Code);
    }

    private static string Export(LinearTriadModel model) {
        MemoryStream stream = new();
        using StreamWriter writer = NewWriter(stream);
        model.ExportLP(writer).IsOk.Should().BeTrue();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Export(QuadraticTetradModel model) {
        MemoryStream stream = new();
        using StreamWriter writer = NewWriter(stream);
        model.ExportLP(writer).IsOk.Should().BeTrue();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static StreamWriter NewWriter(MemoryStream stream) =>
        new(
            stream: stream,
            encoding: new UTF8Encoding(false),
            bufferSize: 1024,
            leaveOpen: true
        );

    private static LinearTriadModel LinearFixture() => new(
        variables: [
            Column(0, "x", NegativeInfinity, PositiveInfinity, Continuous.Instance),
            Column(1, "y", Flt64.Zero, PositiveInfinity, Binary.Instance),
            Column(2, "z", NegativeInfinity, Flt64.Zero, Integer.Instance),
            Column(3, "w", new Flt64(2), PositiveInfinity, Continuous.Instance),
            Column(4, "u", NegativeInfinity, new Flt64(3), Continuous.Instance),
            Column(5, "v", new Flt64(4), new Flt64(4), Continuous.Instance),
            Column(6, "q", new Flt64(-2), new Flt64(5), Continuous.Instance),
        ],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([
                new SparseVector([
                    new SparseVectorEntry(0, Flt64.Zero),
                    new SparseVectorEntry(1, Flt64.One),
                    new SparseVectorEntry(2, new Flt64(-2)),
                    new SparseVectorEntry(3, new Flt64(2.5)),
                    new SparseVectorEntry(4, new Flt64(-1)),
                ]),
                new SparseVector([
                    new SparseVectorEntry(5, Flt64.One),
                    new SparseVectorEntry(6, Flt64.One),
                ]),
                new SparseVector(),
            ]),
            signs: [
                ConstraintRelation.LessEqual,
                ConstraintRelation.Equal,
                ConstraintRelation.GreaterEqual,
            ],
            rhs: [new Flt64(7), new Flt64(3), Flt64.Zero],
            names: ["", "eq", "zero"],
            sources: [
                ConstraintSource.Origin,
                ConstraintSource.Origin,
                ConstraintSource.Origin,
            ]
        ),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: [
                new LinearObjectiveCell(0, Flt64.Zero),
                new LinearObjectiveCell(1, new Flt64(-1)),
                new LinearObjectiveCell(2, new Flt64(2)),
                new LinearObjectiveCell(3, new Flt64(-0.5)),
            ],
            constant: new Flt64(99)
        ),
        name: "linear-export"
    );

    private static QuadraticTetradModel QuadraticFixture() => new(
        variables: [
            Column(0, "x", Flt64.Zero, new Flt64(10), Continuous.Instance),
            Column(1, "y", new Flt64(-1), Flt64.One, Continuous.Instance),
        ],
        constraints: new QuadraticConstraintBatch(
            lhs: new SparseQuadraticMatrix([
                new SparseQuadraticVector([
                    new SparseQuadraticEntry(
                        Col1: 0,
                        Col2: -1,
                        Value: new Flt64(2)
                    ),
                    new SparseQuadraticEntry(
                        Col1: 0,
                        Col2: 1,
                        Value: new Flt64(-1)
                    ),
                ]),
                new SparseQuadraticVector([
                    new SparseQuadraticEntry(
                        Col1: 1,
                        Col2: -1,
                        Value: Flt64.One
                    ),
                ]),
            ]),
            signs: [
                ConstraintRelation.LessEqual,
                ConstraintRelation.GreaterEqual,
            ],
            rhs: [new Flt64(5), Flt64.Zero],
            names: ["quad", "linear"],
            sources: [ConstraintSource.Origin, ConstraintSource.Origin]
        ),
        objective: new Objective<QuadraticObjectiveCell>(
            category: ObjectCategory.Maximum,
            cells: [
                new QuadraticObjectiveCell(
                    Col1: 1,
                    Col2: -1,
                    Coefficient: Flt64.One
                ),
                new QuadraticObjectiveCell(
                    Col1: 0,
                    Col2: 1,
                    Coefficient: new Flt64(-2)
                ),
                new QuadraticObjectiveCell(
                    Col1: 0,
                    Col2: 0,
                    Coefficient: new Flt64(0.5)
                ),
            ],
            constant: new Flt64(99)
        ),
        name: "quadratic-export"
    );

    private static LinearTriadModel LinearModelWithCell(LinearConstraintCell cell) => new(
        variables: [
            Column(0, "x", Flt64.Zero, Flt64.One, Continuous.Instance),
        ],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([
                new SparseVector([
                    new SparseVectorEntry(cell.ColIndex, cell.Coefficient),
                ]),
            ]),
            signs: [ConstraintRelation.LessEqual],
            rhs: [Flt64.One],
            names: ["row"],
            sources: [ConstraintSource.Origin]
        ),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: [new LinearObjectiveCell(0, Flt64.One)]
        ),
        name: "invalid"
    );

    private static ModelViewVariable Column(
        int index,
        string name,
        Flt64 lowerBound,
        Flt64 upperBound,
        IVariableTypeKind type) =>
        new(
            index: index,
            lowerBound: lowerBound,
            upperBound: upperBound,
            type: type,
            name: name
        );

    private static string CreateTemporaryDirectory() {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ospf-lp-export-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static Flt64 NegativeInfinity => new(double.NegativeInfinity);

    private static Flt64 PositiveInfinity => new(double.PositiveInfinity);

    private const string ExpectedLinear = """
Minimum
-1 y + 2 z - 0.5 w

Subject To
 cons0: y - 2 z + 2.5 w - u <= 7
 eq: v + q = 3
 zero: 0 >= 0

Bounds
 x free
 y >= 0
 z <= 0
 w >= 2
 u <= 3
 v = 4
 -2 <= q <= 5

Binaries
 y
Generals
 z
End
""";

    private const string ExpectedQuadratic = """
Maximum
y + [ -2 x * y + 0.5 x ^ 2 ]

Subject To
 quad: 2 x + [ -1 x * y ] <= 5
 linear: y >= 0

Bounds
 0 <= x <= 10
 -1 <= y <= 1

End
""";
}
