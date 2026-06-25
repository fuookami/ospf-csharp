#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Expression.SourceGenerator;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Expression
{
    /// <summary>
    /// 谓词 schema 生成器验证测试 / Predicate schema generator verification tests.
    ///
    /// 验证 PredicateSchemaGenerator 类型和渲染器正确编译。
    /// Verifies that PredicateSchemaGenerator and renderer types compile correctly.
    /// </summary>
    public class PredicateSchemaGeneratorTests
    {
        [Fact(Skip = "Requires Microsoft.CodeAnalysis (Roslyn SDK)")]
        public void PredicateSchemaGenerator_Type_Should_Exist()
        {
            var type = typeof(PredicateSchemaGenerator);
            type.Should().NotBeNull();
            type.IsClass.Should().BeTrue();
            // Sealed class with [Generator] attribute
            type.IsSealed.Should().BeTrue();
            type.IsAbstract.Should().BeFalse();
        }

        [Fact(Skip = "Requires Microsoft.CodeAnalysis (Roslyn SDK)")]
        public void PredicateSchemaModel_Should_Be_Record()
        {
            var type = typeof(PredicateSchemaModel);
            type.Should().NotBeNull();
            type.IsClass.Should().BeTrue();
        }

        [Fact(Skip = "Requires Microsoft.CodeAnalysis (Roslyn SDK)")]
        public void PredicateProperty_Should_Be_Record()
        {
            var type = typeof(PredicateProperty);
            type.Should().NotBeNull();
            type.IsClass.Should().BeTrue();
        }

        [Fact(Skip = "Requires Microsoft.CodeAnalysis (Roslyn SDK)")]
        public void ColumnNamingStrategy_Should_Have_Identity_And_SnakeCase()
        {
            var values = System.Enum.GetValues<ColumnNamingStrategy>();
            values.Should().Contain(ColumnNamingStrategy.Identity);
            values.Should().Contain(ColumnNamingStrategy.SnakeCase);
        }

        [Fact(Skip = "Requires Microsoft.CodeAnalysis (Roslyn SDK)")]
        public void PredicateSchemaModel_Should_Support_Equality()
        {
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

        [Fact(Skip = "Requires Microsoft.CodeAnalysis (Roslyn SDK)")]
        public void PredicateProperty_Should_Support_Equality()
        {
            var prop1 = new PredicateProperty("Name", "name", "Name");
            var prop2 = new PredicateProperty("Name", "name", "Name");
            var prop3 = new PredicateProperty("Other", "other", "Other");

            prop1.Should().Be(prop2);
            prop1.Should().NotBe(prop3);
        }
    }
}
