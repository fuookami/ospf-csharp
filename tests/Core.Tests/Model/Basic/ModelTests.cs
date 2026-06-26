#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Basic;

public class ModelTests {
    [Fact]
    public void IModel_HasNameProperty() {
        IModel<Flt64> model = new TestLinearModel("testModel");
        model.Name.Should().Be("testModel");
    }

    [Fact]
    public void ILinearModel_ExtendsIModel() {
        ILinearModel<Flt64> model = new TestLinearModel("linearModel");
        model.Should().BeAssignableTo<IModel<Flt64>>();
        model.Name.Should().Be("linearModel");
    }

    [Fact]
    public void IQuadraticModel_ExtendsIModel() {
        IQuadraticModel<Flt64> model = new TestQuadraticModel("quadraticModel");
        model.Should().BeAssignableTo<IModel<Flt64>>();
        model.Name.Should().Be("quadraticModel");
    }

    [Fact]
    public void BasicModel_DefaultName_IsEmpty() {
        var model = new ConcreteBasicModel();
        model.Name.Should().BeEmpty();
    }

    [Fact]
    public void BasicModel_WithName_ReturnsName() {
        var model = new ConcreteBasicModel("initial");
        model.Name.Should().Be("initial");
    }

    [Fact]
    public void BasicModel_ToString_ReturnsName() {
        var model = new ConcreteBasicModel("myModel");
        model.ToString().Should().Be("myModel");
    }

    // Concrete test implementations
    private class TestLinearModel : ILinearModel<Flt64> {
        public string Name { get; }
        public TestLinearModel(string name) => Name = name;
    }

    private class TestQuadraticModel : IQuadraticModel<Flt64> {
        public string Name { get; }
        public TestQuadraticModel(string name) => Name = name;
    }

    private class ConcreteBasicModel : BasicModel<Flt64> {
        public ConcreteBasicModel(string name = "") : base(name) { }
    }
}
