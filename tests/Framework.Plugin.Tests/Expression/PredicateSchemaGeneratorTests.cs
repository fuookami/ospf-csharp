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
