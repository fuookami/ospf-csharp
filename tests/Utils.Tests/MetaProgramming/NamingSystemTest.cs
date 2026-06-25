#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils.MetaProgramming;
using Xunit;

namespace Fuookami.Ospf.Utils.Tests.MetaProgramming;

public class NamingSystemTest {
    [Fact]
    public void CamelCase_Backend_ConvertsCorrectly() {
        string[] words = new[] { "hello", "world" };
        NamingSystem.CamelCase.Backend(words).Should().Be("helloWorld");
    }

    [Fact]
    public void PascalCase_Backend_ConvertsCorrectly() {
        string[] words = new[] { "hello", "world" };
        NamingSystem.PascalCase.Backend(words).Should().Be("HelloWorld");
    }

    [Fact]
    public void SnakeCase_Backend_ConvertsCorrectly() {
        string[] words = new[] { "hello", "world" };
        NamingSystem.SnakeCase.Backend(words).Should().Be("hello_world");
    }

    [Fact]
    public void KebabCase_Backend_ConvertsCorrectly() {
        string[] words = new[] { "hello", "world" };
        NamingSystem.KebabCase.Backend(words).Should().Be("hello-world");
    }

    [Fact]
    public void NameTransfer_Invoke_RoundTrip() {
        var transfer = new NameTransfer(NamingSystem.CamelCase, NamingSystem.SnakeCase);
        string result = transfer.Invoke("helloWorld");
        result.Should().Be("hello_world");
    }

    [Fact]
    public void NameTransfer_Reverse_RoundTrip() {
        var transfer = new NameTransfer(NamingSystem.CamelCase, NamingSystem.SnakeCase);
        string result = transfer.Reverse("hello_world");
        result.Should().Be("helloWorld");
    }
}
