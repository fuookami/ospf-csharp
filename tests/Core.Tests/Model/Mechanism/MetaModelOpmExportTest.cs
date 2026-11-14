#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

/// <summary>
/// 元模型 OPM 诊断导出测试 / Meta-model OPM diagnostic export tests.
/// </summary>
public class MetaModelOpmExportTest {
    [Fact]
    public async Task ExportsReadableLinearConstraintsWithoutBom() {
        string path = TempPath(".opm");
        try {
            using var model = new LinearMetaModel<Flt64>(
                name: "opm-readable-constraints",
                objectCategory: ObjectCategory.Minimum);
            var purchase = new RealVar("purchase_1");
            model.Add(purchase).IsOk.Should().BeTrue();
            model.AddConstraint(
                relation: new LinearInequality<Flt64>(
                    Lhs: Linear(purchase),
                    Rhs: Constant(new Flt64(400)),
                    Comparison: Comparison.GE),
                group: null,
                name: "purchase_lower").IsOk.Should().BeTrue();
            model.AddConstraint(
                relation: new LinearInequality<Flt64>(
                    Lhs: Linear(purchase),
                    Rhs: Constant(new Flt64(500)),
                    Comparison: Comparison.LE),
                group: null,
                name: "purchase_upper").IsOk.Should().BeTrue();
            model.AddObject(
                category: ObjectCategory.Minimum,
                flattenData: new LinearFlattenData<Flt64>(
                    Monomials: [new LinearMonomial<Flt64>(Flt64.One, purchase)],
                    Constant: Flt64.Zero),
                name: "purchase_cost",
                displayName: null).IsOk.Should().BeTrue();

            Result<Success, ErrorCode, Error<ErrorCode>> result =
                await model.ExportToOpmAsync(path);

            result.IsOk.Should().BeTrue(result.ToString());
            string content = await File.ReadAllTextAsync(path);
            content.Should().Contain("Model Name: opm-readable-constraints");
            content.Should().Contain("purchase_lower: purchase_1 >= 400");
            content.Should().Contain("purchase_upper: purchase_1 <= 500");
            content.Should().Contain("Minimum purchase_cost: purchase_1");
            content.Should().NotContain("LinearInequality(lhs=");

            byte[] bytes = await File.ReadAllBytesAsync(path);
            bool hasUtf8Bom = bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF;
            hasUtf8Bom.Should().BeFalse();
        }
        finally {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ExportsQuadraticObjectivesAndConstraints() {
        string path = TempPath(".opm");
        try {
            using var model = new QuadraticMetaModel<Flt64>(
                name: "opm-quadratic",
                objectCategory: ObjectCategory.Minimum);
            var x = new RealVar("x");
            var y = new RealVar("y");
            model.Add([x, y]).IsOk.Should().BeTrue();
            model.AddConstraint(
                relation: new QuadraticInequalityOf<Flt64>(
                    Lhs: new QuadraticPolynomial<Flt64>(
                        Monomials: [new QuadraticMonomial<Flt64>(Flt64.One, x, x)],
                        Constant: Flt64.Zero),
                    Rhs: new QuadraticPolynomial<Flt64>(
                        Monomials: [],
                        Constant: new Flt64(4)),
                    Comparison: Comparison.LE),
                group: null,
                name: "quadratic_row").IsOk.Should().BeTrue();
            model.AddObject(
                category: ObjectCategory.Minimum,
                flattenData: new QuadraticFlattenData<Flt64>(
                    Monomials: [
                        new QuadraticMonomial<Flt64>(Flt64.One, x, x),
                        new QuadraticMonomial<Flt64>(new Flt64(2), y, null),
                    ],
                    Constant: new Flt64(3)),
                name: "quadratic",
                displayName: null).IsOk.Should().BeTrue();

            Result<Success, ErrorCode, Error<ErrorCode>> result =
                await model.ExportAsync(path);

            result.IsOk.Should().BeTrue(result.ToString());
            string content = await File.ReadAllTextAsync(path);
            content.Should().Contain("Minimum quadratic: x^2 + 2 * y + 3");
            content.Should().Contain("quadratic_row: x^2 <= 4");
        }
        finally {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task DirectoryPathGeneratesModelNamedFile() {
        string directory = TempPath("-directory");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "opm-directory.opm");
        try {
            using var model = new LinearMetaModel<Flt64>(
                name: "opm-directory",
                objectCategory: ObjectCategory.Minimum);

            Result<Success, ErrorCode, Error<ErrorCode>> result =
                await model.ExportAsync(directory);

            result.IsOk.Should().BeTrue(result.ToString());
            File.Exists(path).Should().BeTrue();
            (await File.ReadAllTextAsync(path)).Should().Contain("Model Name: opm-directory");
        }
        finally {
            if (File.Exists(path)) {
                File.Delete(path);
            }
            if (Directory.Exists(directory)) {
                Directory.Delete(directory);
            }
        }
    }

    [Fact]
    public async Task ClosedModelAndInvalidPathReturnErrors() {
        string path = TempPath(".opm");
        var model = new LinearMetaModel<Flt64>(name: "closed-opm");
        model.Close();

        Result<Success, ErrorCode, Error<ErrorCode>> closed =
            await model.ExportAsync(path);

        closed.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        using var open = new LinearMetaModel<Flt64>(name: "invalid-opm-path");
        string invalid = Path.Combine(
            TempPath("-missing-parent"),
            "model.opm");
        Result<Success, ErrorCode, Error<ErrorCode>> invalidPath =
            await open.ExportAsync(invalid);
        invalidPath.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationFailed);
    }

    private static LinearPolynomial<Flt64> Linear(ISymbol symbol) => new(
        Monomials: [new LinearMonomial<Flt64>(Flt64.One, symbol)],
        Constant: Flt64.Zero);

    private static LinearPolynomial<Flt64> Constant(Flt64 value) => new(
        Monomials: [],
        Constant: value);

    private static string TempPath(string suffix) =>
        Path.Combine(
            Path.GetTempPath(),
            $"ospf-opm-{Guid.NewGuid():N}{suffix}");
}
