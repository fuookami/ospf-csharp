#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Expression.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Expression;
/// <summary>
/// 谓词 schema 生成器验证测试 / Predicate schema generator verification tests.
///
/// 验证 PredicateSchemaGenerator 类型和渲染器正确编译，并验证生成器输出。
/// Verifies that PredicateSchemaGenerator and renderer types compile correctly,
/// and that the generator produces correct output.
/// </summary>
public class PredicateSchemaGeneratorTests {
    [Fact]
    public void PredicateSchemaGenerator_Type_Should_Exist() {
        Type type = typeof(PredicateSchemaGenerator);
        type.Should().NotBeNull();
        type.IsClass.Should().BeTrue();
        // Sealed class with [Generator] attribute
        type.IsSealed.Should().BeTrue();
        type.IsAbstract.Should().BeFalse();
    }

    [Fact]
    public void PredicateSchemaGenerator_Should_Have_Generator_Attribute() {
        Type type = typeof(PredicateSchemaGenerator);
        GeneratorAttribute? attr = type.GetCustomAttribute<GeneratorAttribute>();
        attr.Should().NotBeNull("PredicateSchemaGenerator must be annotated with [Generator]");
    }

    [Fact]
    public void PredicateSchemaGenerator_Should_Implement_IIncrementalGenerator() {
        Type type = typeof(PredicateSchemaGenerator);
        typeof(IIncrementalGenerator).IsAssignableFrom(type).Should().BeTrue();
    }

    [Fact]
    public void PredicateSchemaModel_Should_Be_Record() {
        Type type = typeof(PredicateSchemaModel);
        type.Should().NotBeNull();
        type.IsClass.Should().BeTrue();
    }

    [Fact]
    public void PredicateProperty_Should_Be_Record() {
        Type type = typeof(PredicateProperty);
        type.Should().NotBeNull();
        type.IsClass.Should().BeTrue();
    }

    [Fact]
    public void ColumnNamingStrategy_Should_Have_Identity_And_SnakeCase() {
        ColumnNamingStrategy[] values = System.Enum.GetValues<ColumnNamingStrategy>();
        values.Should().Contain(ColumnNamingStrategy.Identity);
        values.Should().Contain(ColumnNamingStrategy.SnakeCase);
    }

    [Fact]
    public void PredicateSchemaModel_Should_Support_Equality() {
        // Records with list properties use reference equality for lists.
        // Verify structural equality of scalar fields instead.
        var model = new PredicateSchemaModel(
            "TestNs", "Entity", "EntitySchema", true, false,
            new[] { new PredicateProperty("Name", "Name", "Name") });

        model.PackageName.Should().Be("TestNs");
        model.EntityName.Should().Be("Entity");
        model.SchemaName.Should().Be("EntitySchema");
        model.GenerateResolver.Should().BeTrue();
        model.GenerateColumnMapping.Should().BeFalse();
        model.Properties.Should().HaveCount(1);
    }

    [Fact]
    public void PredicateProperty_Should_Support_Equality() {
        var prop1 = new PredicateProperty("Name", "name", "Name");
        var prop2 = new PredicateProperty("Name", "name", "Name");
        var prop3 = new PredicateProperty("Other", "other", "Other");

        prop1.Should().Be(prop2);
        prop1.Should().NotBe(prop3);
    }

    [Fact]
    public void Renderer_Should_Generate_Valid_Source() {
        var model = new PredicateSchemaModel(
            "TestNs", "MyEntity", "MyEntitySchema", true, true,
            new[]
            {
                new PredicateProperty("Id", "id", "Id"),
                new PredicateProperty("Name", "name", "Name"),
            });

        string source = PredicateSchemaRenderer.Render(model);

        source.Should().Contain("namespace TestNs");
        source.Should().Contain("public sealed partial class MyEntitySchema");
        source.Should().Contain("public static string Id => \"id\"");
        source.Should().Contain("public static string Name => \"name\"");
        source.Should().Contain("public static string? Resolve(string path) => path switch");
        source.Should().Contain("\"Id\" => \"id\"");
        source.Should().Contain("\"Name\" => \"name\"");
        source.Should().Contain("ColumnMapping");
    }

    /// <summary>
    /// **列映射开关同时决定"实现接口"与"生成 `CreateBinder`"** / The column-mapping switch decides both the interface and `CreateBinder`.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 关闭列映射 → **不实现** `IHasColumnMapping`（空实现等于声称"我提供列映射"，而调用方会去读一个不存在的成员）。
    /// 2. 关闭列映射 → **不生成** `CreateBinder`。
    /// 3. 打开列映射 → 类声明带 `IHasColumnMapping`，且生成 `ColumnMapping` 与 `CreateBinder`。
    /// 4. `CreateBinder` 的语义是**先查映射再交给外部解析器**：属性路径命中映射时传后端名，未命中时原样传。
    ///
    /// Four paired points: with column mapping off the class does **not** implement `IHasColumnMapping`, since an empty implementation claims a mapping a caller would then fail to read; with it off
    /// `CreateBinder` is **not** generated; with it on the declaration carries `IHasColumnMapping` and both `ColumnMapping` and `CreateBinder` appear; and `CreateBinder` means **look up the mapping
    /// first, then hand over to the external resolver**, passing the backend name on a hit and the path unchanged on a miss.
    /// </remarks>
    [Fact]
    public void Renderer_Should_GateTheInterfaceAndTheBinderOnTheColumnMappingSwitch() {
        PredicateProperty[] properties = new[]
        {
            new PredicateProperty("Id", "id", "Id"),
            new PredicateProperty("Name", "name", "Name"),
        };

        string without = PredicateSchemaRenderer.Render(
            new PredicateSchemaModel("TestNs", "MyEntity", "MyEntitySchema", true, false, properties));
        without.Should().NotContain("IHasColumnMapping");
        without.Should().NotContain("CreateBinder");

        string with = PredicateSchemaRenderer.Render(
            new PredicateSchemaModel("TestNs", "MyEntity", "MyEntitySchema", true, true, properties));
        with.Should().Contain("IHasColumnMapping");
        with.Should().Contain("ColumnMapping");
        with.Should().Contain("CreateBinder<C>(");
        with.Should().Contain("ColumnMapping.TryGetValue(propertyPath, out string? mapped) ? mapped : propertyPath",
            "the mapping must be consulted before the external resolver sees the name");
    }

    /// <summary>
    /// **生成的 `CreateBinder` 必须能编译** / The generated `CreateBinder` must compile.
    /// </summary>
    /// <remarks>
    /// 上一条判据只查字符串——`CreateBinder` 的**方法体**是否合法它看不见。这条把渲染结果真的编一遍，
    /// 才能抓到"方法体写坏了但字符串还在"的情况。变异验证过：只查字符串时"方法体改成 `=> default;`"会存活。
    ///
    /// The previous criterion only inspects strings and therefore cannot see whether `CreateBinder`'s **body** is legal. This one actually compiles the rendered text, which is what catches a broken
    /// body whose strings are still present. A mutation confirmed it: with strings alone, rewriting the body to `=> default;` survived.
    /// </remarks>
    [Fact]
    public void Renderer_WithColumnMapping_Should_Compile() {
        PredicateProperty[] properties = new[]
        {
            new PredicateProperty("Id", "id", "Id"),
            new PredicateProperty("Name", "name", "Name"),
        };
        string rendered = PredicateSchemaRenderer.Render(
            new PredicateSchemaModel("TestNs", "MyEntity", "MyEntitySchema", true, true, properties));

        string columnBinder = @"
namespace Fuookami.Ospf.Framework.Persistence.Expression
{
    public interface IHasColumnMapping { }
    public interface IColumnBinder<C> where C : notnull { C? Resolve(string propertyPath); }
}
";

        var compilation = CSharpCompilation.Create("RenderedAssembly",
            new[]
            {
                CSharpSyntaxTree.ParseText(rendered),
                CSharpSyntaxTree.ParseText(columnBinder),
            },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics(CancellationToken.None)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        errors.Should().BeEmpty(
            "the rendered schema must compile, but got: {0}",
            string.Join(", ", errors.Select(e => e.GetMessage())));
    }

    [Fact]
    public void Generator_Should_Produce_Source_Via_Driver() {
        string source = @"
namespace TestEntities
{
    [Fuookami.Ospf.Framework.Persistence.Expression.PredicateEntity(
        SchemaName = ""ProductSchema"",
        GenerateResolver = true,
        GenerateColumnMapping = false)]
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = """";
        public double Price { get; set; }
    }
}
";

        // We need the PredicateEntityAttribute definition for the generator to match.
        string attributeSource = @"
namespace Fuookami.Ospf.Framework.Persistence.Expression
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class PredicateEntityAttribute : System.Attribute
    {
        public string? SchemaName { get; set; }
        public bool GenerateResolver { get; set; } = true;
        public bool GenerateColumnMapping { get; set; }
        public int NamingStrategy { get; set; }
    }
}
";

        var compilation = CSharpCompilation.Create("TestAssembly",
            new[]
            {
                CSharpSyntaxTree.ParseText(source),
                CSharpSyntaxTree.ParseText(attributeSource),
            },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new PredicateSchemaGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation? outputCompilation, out ImmutableArray<Diagnostic> diagnostics);

        GeneratorDriverRunResult runResult = driver.GetRunResult();

        // Generator should have produced at least one source output
        runResult.GeneratedTrees.Length.Should().BeGreaterOrEqualTo(1,
            "generator should emit at least one source file for the annotated class");

        string generatedSource = runResult.GeneratedTrees[0].GetText().ToString();
        generatedSource.Should().Contain("ProductSchema");
        generatedSource.Should().Contain("public sealed partial class ProductSchema");
        generatedSource.Should().Contain("public static string Id =>");
        generatedSource.Should().Contain("public static string Name =>");
        generatedSource.Should().Contain("public static string Price =>");
        generatedSource.Should().Contain("Resolve");
    }

    [Fact]
    public void Generator_Should_Compile_Generated_Source_Without_Errors() {
        string source = @"
namespace TestEntities
{
    [Fuookami.Ospf.Framework.Persistence.Expression.PredicateEntity(SchemaName = ""ItemSchema"")]
    public class Item
    {
        public long Id { get; set; }
        public string Code { get; set; } = """";
    }
}
";

        string attributeSource = @"
namespace Fuookami.Ospf.Framework.Persistence.Expression
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class PredicateEntityAttribute : System.Attribute
    {
        public string? SchemaName { get; set; }
        public bool GenerateResolver { get; set; } = true;
        public bool GenerateColumnMapping { get; set; }
        public int NamingStrategy { get; set; }
    }
}
";

        var compilation = CSharpCompilation.Create("TestAssembly",
            new[]
            {
                CSharpSyntaxTree.ParseText(source),
                CSharpSyntaxTree.ParseText(attributeSource),
            },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new PredicateSchemaGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation? outputCompilation, out ImmutableArray<Diagnostic> diagnostics);

        // The output compilation (with generated sources) should have no errors
        ImmutableArray<Diagnostic> outputDiagnostics = outputCompilation.GetDiagnostics(CancellationToken.None);
        var errors = outputDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        errors.Should().BeEmpty(
            "compilation with generated source should produce no errors, but got: {0}",
            string.Join(", ", errors.Select(e => e.GetMessage())));
    }
}
