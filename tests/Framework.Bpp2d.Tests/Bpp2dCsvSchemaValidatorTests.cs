// CSV 表头校验的判据 / Criteria for the CSV header validation.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Application;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// **先报第一个出错的位置，声明形态与实际不符要报错，未知列要拒** / Report the first offending position, reject a declared shape that disagrees, and reject unknown columns.
/// </summary>
/// <remarks>
/// 三处都会让使用者拿到一个"能跑但算错"的结果：报最后一个错误等于没报，以声明为准会把物料表当成分组表解析，
/// 放过未知列则意味着拼错的列名被静默忽略、该列数据整体丢失。
///
/// All three would hand the user a result that runs but computes the wrong thing: reporting the last error reports nothing, honouring the declaration parses a material
/// table as a grouped table, and accepting unknown columns silently drops a misspelled column's data entirely.
/// </remarks>
public class Bpp2dCsvSchemaValidatorTests {
    /// **分组形态的表头被识别，且行数不含表头** / A grouped header is detected and the row count excludes it.
    [Fact]
    public void Validate_ShouldDetectTheGroupedShapeAndCountTheRows() {
        const string csv = "group_index,layer_index,item_id,amount\n0,0,A,2\n0,1,B,3\n";

        Bpp2dCsvSchema schema = RetAssertions.ValueOrFail(Bpp2dCsvSchemaValidator.Validate(csv));

        schema.Kind.Should().Be(Bpp2dCsvDatasetKind.GroupedLayer);
        schema.Rows.Should().Be(2, "the header line is not a data row");
        schema.Columns.Should().HaveCount(4);
    }

    /// **物料形态的表头被识别** / A material header is detected.
    [Fact]
    public void Validate_ShouldDetectTheMaterialShape() {
        const string csv = "material,width,amount\nA,1.5,2\n";

        Bpp2dCsvSchema schema = RetAssertions.ValueOrFail(Bpp2dCsvSchemaValidator.Validate(csv));

        schema.Kind.Should().Be(Bpp2dCsvDatasetKind.MaterialWidthAmount);
        schema.Rows.Should().Be(1);
    }

    /// **重复列被拒** / Duplicate columns are rejected.
    [Fact]
    public void Validate_ShouldRejectDuplicateColumns() {
        const string csv = "group_index,layer_index,item_id,amount,amount\n0,0,A,2,2\n";

        Bpp2dCsvSchemaValidator.Validate(csv)
            .Should().BeOfType<Failed<Bpp2dCsvSchema, ErrorCode, Error<ErrorCode>>>(
                "a duplicated column makes every row ambiguous");
    }

    /// **两种必填列都不齐时无法识别** / An unrecognisable header is rejected.
    [Fact]
    public void Validate_ShouldRejectAnUnrecognisableHeader() {
        const string csv = "foo,bar\n1,2\n";

        Bpp2dCsvSchemaValidator.Validate(csv)
            .Should().BeOfType<Failed<Bpp2dCsvSchema, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **声明形态与实际不符时报错，而不是以声明为准** / A declared shape that disagrees is an error, not the winner.
    /// </summary>
    /// <remarks>
    /// 这个文件是物料表，但调用方声明成"分组表"。若以声明为准，解析器会去找 `item_id` 而找不到，
    /// 报出的错误会出现在几层之后，与真正的原因（调用方认错了文件）毫无关联。
    ///
    /// The file is a material table while the caller declares it grouped. Honouring the declaration would make the parser look for `item_id`, and the error would surface
    /// several layers away from the real cause (the caller misidentified the file).
    /// </remarks>
    [Fact]
    public void Validate_ShouldRejectAMismatchedDeclaredKind() {
        const string csv = "material,width,amount\nA,1.5,2\n";

        Bpp2dCsvSchemaValidator.Validate(csv, Bpp2dCsvDatasetKind.GroupedLayer)
            .Should().BeOfType<Failed<Bpp2dCsvSchema, ErrorCode, Error<ErrorCode>>>();

        RetAssertions.ValueOrFail(Bpp2dCsvSchemaValidator.Validate(csv, Bpp2dCsvDatasetKind.MaterialWidthAmount))
            .Kind.Should().Be(Bpp2dCsvDatasetKind.MaterialWidthAmount, "an honest declaration is accepted");
    }

    /// **未知列被拒** / Unknown columns are rejected.
    [Fact]
    public void Validate_ShouldRejectUnknownColumns() {
        const string csv = "group_index, layer_index, item_id, amount, amuont\n0,0,A,2,2\n";

        Bpp2dCsvSchemaValidator.Validate(csv)
            .Should().BeOfType<Failed<Bpp2dCsvSchema, ErrorCode, Error<ErrorCode>>>(
                "a misspelled column would otherwise be silently ignored and its data lost");
    }

    /// **空文本被拒** / Empty text is rejected.
    [Fact]
    public void Validate_ShouldRejectEmptyText() {
        Bpp2dCsvSchemaValidator.Validate("   \n \n")
            .Should().BeOfType<Failed<Bpp2dCsvSchema, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **报第一个列数不匹配的行，不是最后一个** / Report the first mismatched row, not the last.
    /// </summary>
    /// <remarks>
    /// 末尾两行都错时，报第 3 行还是第 4 行决定了使用者能不能直接定位。实现若在校验循环里继续扫描，
    /// 很容易把错误留在最后一次赋值上。
    ///
    /// With the last two rows both wrong, reporting line 3 versus line 4 decides whether the user can locate the problem at once. An implementation that keeps scanning the loop easily
    /// leaves the error on the final assignment.
    /// </remarks>
    [Fact]
    public void Validate_ShouldReportTheFirstMalformedRow() {
        const string csv = "group_index,layer_index,item_id,amount\n0,0,A,2\n0,1,B\n0,2,C,4,5\n";

        Bpp2dCsvSchemaValidator.Validate(csv)
            .Should().BeOfType<Failed<Bpp2dCsvSchema, ErrorCode, Error<ErrorCode>>>()
            .Which.Error.Message
            .Should().Contain("第 3 行", "the first offending row is line 3, not the final line 4");
        Bpp2dCsvSchemaValidator.Validate(csv)
            .Should().BeOfType<Failed<Bpp2dCsvSchema, ErrorCode, Error<ErrorCode>>>()
            .Which.Error.Message
            .Should().NotContain("第 4 行");
    }

    /// **表头列名两侧空白被裁掉** / Whitespace around header cells is trimmed.
    [Fact]
    public void Validate_ShouldTrimHeaderWhitespace() {
        const string csv = " group_index , layer_index , item_id , amount \n0,0,A,2\n";

        Bpp2dCsvSchema schema = RetAssertions.ValueOrFail(Bpp2dCsvSchemaValidator.Validate(csv));

        schema.Columns.Should().Equal(new List<string> { "group_index", "layer_index", "item_id", "amount" });
    }
}
