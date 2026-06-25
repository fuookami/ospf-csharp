#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils.MetaProgramming;
using Fuookami.Ospf.Utils.Serialization;
using Xunit;

namespace Fuookami.Ospf.Utils.Tests.Serialization
{
    public class JsonNamingPolicyTest
    {
        [Fact]
        public void CamelCaseToFrontend_ConvertsCorrectly()
        {
            var policy = new OspfJsonNamingPolicy(NamingSystem.CamelCase, NamingSystem.SnakeCase);
            policy.ConvertName("helloWorld").Should().Be("hello_world");
        }

        [Fact]
        public void SnakeCaseToFrontend_ConvertsCorrectly()
        {
            var policy = new OspfJsonNamingPolicy(NamingSystem.SnakeCase, NamingSystem.CamelCase);
            policy.ConvertName("hello_world").Should().Be("helloWorld");
        }
    }
}
