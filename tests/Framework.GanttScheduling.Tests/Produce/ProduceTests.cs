#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Produce;
// ===== Test helper types =====

public class ProdTestExecutor : Executor {
    public ProdTestExecutor(string id, string name) : base(id, name) { }
}

public class TestMaterial : IMaterial {
    public TestMaterial(string name, int index) {
        Name = name;
        Index = index;
    }

    public string Name { get; }
    public int Index { get; }
}

public class TestProduct : TestMaterial, IProduct {
    public TestProduct(string name, int index) : base(name, index) { }
}

public class TestRawMaterial : TestMaterial, IRawMaterial {
    public TestRawMaterial(string name, int index) : base(name, index) { }
}

// ===== Tests =====

public class MaterialTests {
    [Fact]
    public void TestMaterialCreation() {
        var material = new TestProduct("Steel", 0);
        material.Name.Should().Be("Steel");
        material.Index.Should().Be(0);
        ((IMaterial)material).Material.Should().Be(material);
    }

    [Fact]
    public void TestProductInterface() {
        var product = new TestProduct("Widget", 0);
        product.Should().BeAssignableTo<IMaterial>();
        product.Should().BeAssignableTo<IProduct>();
    }

    [Fact]
    public void TestRawMaterialInterface() {
        var raw = new TestRawMaterial("Iron", 1);
        raw.Should().BeAssignableTo<IMaterial>();
        raw.Should().BeAssignableTo<IRawMaterial>();
    }
}

public class MaterialDemandTests {
    [Fact]
    public void TestMaterialDemandCreation() {
        var demand = new MaterialDemand(
            LowerBound: new Flt64(10.0),
            UpperBound: new Flt64(100.0),
            LessQuantityValue: new Flt64(5.0),
            OverQuantityValue: new Flt64(10.0));

        demand.LowerBound.Should().Be(new Flt64(10.0));
        demand.UpperBound.Should().Be(new Flt64(100.0));
        demand.LessEnabled.Should().BeTrue();
        demand.OverEnabled.Should().BeTrue();
    }

    [Fact]
    public void TestMaterialDemandWithoutSlack() {
        var demand = new MaterialDemand(
            LowerBound: new Flt64(10.0),
            UpperBound: new Flt64(100.0));

        demand.LessEnabled.Should().BeFalse();
        demand.OverEnabled.Should().BeFalse();
        demand.SolverLessQuantity().Should().Be(Flt64.Zero);
        demand.SolverOverQuantity().Should().Be(Flt64.Zero);
    }

    [Fact]
    public void TestMaterialDemandSolverValues() {
        var demand = new MaterialDemand(
            LowerBound: new Flt64(10.0),
            UpperBound: new Flt64(100.0),
            LessQuantityValue: new Flt64(5.0),
            OverQuantityValue: new Flt64(10.0));

        demand.SolverLowerBound().Should().Be(new Flt64(10.0));
        demand.SolverUpperBound().Should().Be(new Flt64(100.0));
        demand.SolverLessQuantity().Should().Be(new Flt64(5.0));
        demand.SolverOverQuantity().Should().Be(new Flt64(10.0));
        demand.SolverRangeLowerBound().Should().Be(new Flt64(5.0));
        demand.SolverRangeUpperBound().Should().Be(new Flt64(110.0));
    }
}

public class MaterialReservesTests {
    [Fact]
    public void TestMaterialReservesCreation() {
        var reserves = new MaterialReserves(
            LowerBound: new Flt64(20.0),
            UpperBound: new Flt64(200.0),
            LessQuantityValue: new Flt64(10.0),
            OverQuantityValue: new Flt64(20.0));

        reserves.LowerBound.Should().Be(new Flt64(20.0));
        reserves.UpperBound.Should().Be(new Flt64(200.0));
        reserves.LessEnabled.Should().BeTrue();
        reserves.OverEnabled.Should().BeTrue();
    }

    [Fact]
    public void TestMaterialReservesSolverValues() {
        var reserves = new MaterialReserves(
            LowerBound: new Flt64(20.0),
            UpperBound: new Flt64(200.0),
            LessQuantityValue: new Flt64(10.0),
            OverQuantityValue: new Flt64(20.0));

        reserves.SolverRangeLowerBound().Should().Be(new Flt64(10.0));
        reserves.SolverRangeUpperBound().Should().Be(new Flt64(220.0));
    }
}

public class ProduceSlackTests {
    [Fact]
    public void TestPositiveSlack() {
        Flt64 result = ProduceSlackHelper.ProduceSlack(
            new Flt64(15.0), new Flt64(10.0), withNegative: false, withPositive: true);
        result.Should().Be(new Flt64(5.0));
    }

    [Fact]
    public void TestNegativeSlack() {
        Flt64 result = ProduceSlackHelper.ProduceSlack(
            new Flt64(5.0), new Flt64(10.0), withNegative: true, withPositive: false);
        result.Should().Be(new Flt64(5.0));
    }

    [Fact]
    public void TestNoSlack() {
        Flt64 result = ProduceSlackHelper.ProduceSlack(
            new Flt64(10.0), new Flt64(10.0), withNegative: false, withPositive: false);
        result.Should().Be(Flt64.Zero);
    }
}

public class TaskSchedulingProduceTests {
    [Fact]
    public void TestCreation() {
        var product = new TestProduct("Widget", 0);
        var demand = new MaterialDemand(new Flt64(10.0), new Flt64(100.0));
        var produce = new TaskSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) });

        produce.OverEnabled.Should().BeFalse();
        produce.LessEnabled.Should().BeFalse();
    }

    [Fact]
    public void TestRegisterReturnsFailed() {
        var product = new TestProduct("Widget", 0);
        var produce = new TaskSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)null) });

        Result<Success, ErrorCode, Error<ErrorCode>> result = produce.Register(new object());
        result.IsFailed.Should().BeTrue();
    }
}

public class BunchSchedulingProduceTests {
    [Fact]
    public void TestCreation() {
        var product = new TestProduct("Widget", 0);
        var demand = new MaterialDemand(new Flt64(10.0), new Flt64(100.0));
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) });

        produce.OverEnabled.Should().BeTrue();
        produce.LessEnabled.Should().BeTrue();
    }

    [Fact]
    public void TestRegister() {
        var product = new TestProduct("Widget", 0);
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)null) });

        Result<Success, ErrorCode, Error<ErrorCode>> result = produce.Register(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class TaskSchedulingConsumptionTests {
    [Fact]
    public void TestCreation() {
        var material = new TestRawMaterial("Iron", 0);
        var reserves = new MaterialReserves(new Flt64(10.0), new Flt64(100.0));
        var consumption = new TaskSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) });

        consumption.OverEnabled.Should().BeFalse();
        consumption.LessEnabled.Should().BeFalse();
    }

    [Fact]
    public void TestRegisterReturnsFailed() {
        var material = new TestRawMaterial("Iron", 0);
        var consumption = new TaskSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)null) });

        Result<Success, ErrorCode, Error<ErrorCode>> result = consumption.Register(new object());
        result.IsFailed.Should().BeTrue();
    }
}

public class BunchSchedulingConsumptionTests {
    [Fact]
    public void TestCreation() {
        var material = new TestRawMaterial("Iron", 0);
        var reserves = new MaterialReserves(new Flt64(10.0), new Flt64(100.0));
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) });

        consumption.OverEnabled.Should().BeTrue();
        consumption.LessEnabled.Should().BeTrue();
    }

    [Fact]
    public void TestRegister() {
        var material = new TestRawMaterial("Iron", 0);
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)null) });

        Result<Success, ErrorCode, Error<ErrorCode>> result = consumption.Register(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class ProduceSolverValueTests {
    [Fact]
    public void TestSolverValueRangeLower() {
        var demand = new MaterialDemand(
            new Flt64(10.0), new Flt64(100.0),
            new Flt64(5.0), new Flt64(10.0));

        demand.SolverValueRangeLower().Should().Be(new Flt64(5.0));
    }

    [Fact]
    public void TestSolverValueRangeUpper() {
        var demand = new MaterialDemand(
            new Flt64(10.0), new Flt64(100.0),
            new Flt64(5.0), new Flt64(10.0));

        demand.SolverValueRangeUpper().Should().Be(new Flt64(110.0));
    }

    [Fact]
    public void TestReservesSolverValueRange() {
        var reserves = new MaterialReserves(
            new Flt64(20.0), new Flt64(200.0),
            new Flt64(10.0), new Flt64(20.0));

        reserves.SolverValueRangeLower().Should().Be(new Flt64(10.0));
        reserves.SolverValueRangeUpper().Should().Be(new Flt64(220.0));
    }
}

public class ProduceQuantityConstraintTests {
    [Fact]
    public void TestCreation() {
        var product = new TestProduct("Widget", 0);
        var demand = new MaterialDemand(new Flt64(10.0), new Flt64(100.0));
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) });

        var constraint = new ProduceQuantityConstraint<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) }, produce);

        ((IMetaConstraintGroup)constraint).Name.Should().Be("produce_quantity");
        ((IMetaConstraintGroup)constraint).Lazy.Should().BeFalse();
    }

    [Fact]
    public void TestInvoke() {
        var product = new TestProduct("Widget", 0);
        var demand = new MaterialDemand(new Flt64(10.0), new Flt64(100.0));
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) });

        var constraint = new ProduceQuantityConstraint<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) }, produce);

        Result<Success, ErrorCode, Error<ErrorCode>> result = constraint.Invoke(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class ConsumptionQuantityConstraintTests {
    [Fact]
    public void TestCreation() {
        var material = new TestRawMaterial("Iron", 0);
        var reserves = new MaterialReserves(new Flt64(10.0), new Flt64(100.0));
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) });

        var constraint = new ConsumptionQuantityConstraint<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) }, consumption);

        ((IMetaConstraintGroup)constraint).Name.Should().Be("consumption_quantity");
    }

    [Fact]
    public void TestInvoke() {
        var material = new TestRawMaterial("Iron", 0);
        var reserves = new MaterialReserves(new Flt64(10.0), new Flt64(100.0));
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) });

        var constraint = new ConsumptionQuantityConstraint<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) }, consumption);

        Result<Success, ErrorCode, Error<ErrorCode>> result = constraint.Invoke(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class ProduceLessQuantityMinimizationTests {
    [Fact]
    public void TestCreation() {
        var product = new TestProduct("Widget", 0);
        var demand = new MaterialDemand(new Flt64(10.0), new Flt64(100.0), LessQuantityValue: new Flt64(5.0));
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) });

        var minimization = new ProduceLessQuantityMinimization<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) }, produce);

        ((IMetaConstraintGroup)minimization).Name.Should().Be("produce_less_quantity_minimization");
    }

    [Fact]
    public void TestInvoke() {
        var product = new TestProduct("Widget", 0);
        var demand = new MaterialDemand(new Flt64(10.0), new Flt64(100.0), LessQuantityValue: new Flt64(5.0));
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) });

        var minimization = new ProduceLessQuantityMinimization<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) }, produce);

        Result<Success, ErrorCode, Error<ErrorCode>> result = minimization.Invoke(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class ProduceOverQuantityMinimizationTests {
    [Fact]
    public void TestCreation() {
        var product = new TestProduct("Widget", 0);
        var demand = new MaterialDemand(new Flt64(10.0), new Flt64(100.0), OverQuantityValue: new Flt64(10.0));
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) });

        var minimization = new ProduceOverQuantityMinimization<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) }, produce);

        ((IMetaConstraintGroup)minimization).Name.Should().Be("produce_over_quantity_minimization");
    }

    [Fact]
    public void TestInvoke() {
        var product = new TestProduct("Widget", 0);
        var demand = new MaterialDemand(new Flt64(10.0), new Flt64(100.0), OverQuantityValue: new Flt64(10.0));
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) });

        var minimization = new ProduceOverQuantityMinimization<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) }, produce);

        Result<Success, ErrorCode, Error<ErrorCode>> result = minimization.Invoke(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class ProduceQuantityMaximizationTests {
    [Fact]
    public void TestCreation() {
        var product = new TestProduct("Widget", 0);
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)null) });

        var maximization = new ProduceQuantityMaximization<TestProduct>(
            new[] { product }, produce);

        ((IMetaConstraintGroup)maximization).Name.Should().Be("produce_quantity_maximization");
    }

    [Fact]
    public void TestInvoke() {
        var product = new TestProduct("Widget", 0);
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)null) });

        var maximization = new ProduceQuantityMaximization<TestProduct>(
            new[] { product }, produce);

        Result<Success, ErrorCode, Error<ErrorCode>> result = maximization.Invoke(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class ProduceQuantityMinimizationTests {
    [Fact]
    public void TestCreation() {
        var product = new TestProduct("Widget", 0);
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)null) });

        var minimization = new ProduceQuantityMinimization<TestProduct>(
            new[] { product }, produce);

        ((IMetaConstraintGroup)minimization).Name.Should().Be("produce_quantity_minimization");
    }

    [Fact]
    public void TestInvoke() {
        var product = new TestProduct("Widget", 0);
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)null) });

        var minimization = new ProduceQuantityMinimization<TestProduct>(
            new[] { product }, produce);

        Result<Success, ErrorCode, Error<ErrorCode>> result = minimization.Invoke(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class ConsumptionLessQuantityMinimizationTests {
    [Fact]
    public void TestCreation() {
        var material = new TestRawMaterial("Iron", 0);
        var reserves = new MaterialReserves(new Flt64(10.0), new Flt64(100.0), LessQuantityValue: new Flt64(5.0));
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) });

        var minimization = new ConsumptionLessQuantityMinimization<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) }, consumption);

        ((IMetaConstraintGroup)minimization).Name.Should().Be("consumption_less_quantity_minimization");
    }

    [Fact]
    public void TestInvoke() {
        var material = new TestRawMaterial("Iron", 0);
        var reserves = new MaterialReserves(new Flt64(10.0), new Flt64(100.0), LessQuantityValue: new Flt64(5.0));
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) });

        var minimization = new ConsumptionLessQuantityMinimization<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) }, consumption);

        Result<Success, ErrorCode, Error<ErrorCode>> result = minimization.Invoke(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class ConsumptionOverQuantityMinimizationTests {
    [Fact]
    public void TestCreation() {
        var material = new TestRawMaterial("Iron", 0);
        var reserves = new MaterialReserves(new Flt64(10.0), new Flt64(100.0), OverQuantityValue: new Flt64(10.0));
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) });

        var minimization = new ConsumptionOverQuantityMinimization<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) }, consumption);

        ((IMetaConstraintGroup)minimization).Name.Should().Be("consumption_over_quantity_minimization");
    }

    [Fact]
    public void TestInvoke() {
        var material = new TestRawMaterial("Iron", 0);
        var reserves = new MaterialReserves(new Flt64(10.0), new Flt64(100.0), OverQuantityValue: new Flt64(10.0));
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) });

        var minimization = new ConsumptionOverQuantityMinimization<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) }, consumption);

        Result<Success, ErrorCode, Error<ErrorCode>> result = minimization.Invoke(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class ConsumptionQuantityMaximizationTests {
    [Fact]
    public void TestCreation() {
        var material = new TestRawMaterial("Iron", 0);
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)null) });

        var maximization = new ConsumptionQuantityMaximization<TestRawMaterial>(
            new[] { material }, consumption);

        ((IMetaConstraintGroup)maximization).Name.Should().Be("consumption_quantity_maximization");
    }

    [Fact]
    public void TestInvoke() {
        var material = new TestRawMaterial("Iron", 0);
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)null) });

        var maximization = new ConsumptionQuantityMaximization<TestRawMaterial>(
            new[] { material }, consumption);

        Result<Success, ErrorCode, Error<ErrorCode>> result = maximization.Invoke(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class ConsumptionQuantityMinimizationTests {
    [Fact]
    public void TestCreation() {
        var material = new TestRawMaterial("Iron", 0);
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)null) });

        var minimization = new ConsumptionQuantityMinimization<TestRawMaterial>(
            new[] { material }, consumption);

        ((IMetaConstraintGroup)minimization).Name.Should().Be("consumption_quantity_minimization");
    }

    [Fact]
    public void TestInvoke() {
        var material = new TestRawMaterial("Iron", 0);
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)null) });

        var minimization = new ConsumptionQuantityMinimization<TestRawMaterial>(
            new[] { material }, consumption);

        Result<Success, ErrorCode, Error<ErrorCode>> result = minimization.Invoke(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class ShadowPriceKeyTests {
    [Fact]
    public void TestProduceQuantityShadowPriceKey() {
        var product = new TestProduct("Widget", 0);
        var key = new ProduceQuantityShadowPriceKey(product);

        key.Product.Should().Be(product);
        key.Limit.Should().Be(typeof(ProduceQuantityShadowPriceKey));
    }

    [Fact]
    public void TestProduceQuantityShadowPriceKeyEquality() {
        var product = new TestProduct("Widget", 0);
        var key1 = new ProduceQuantityShadowPriceKey(product);
        var key2 = new ProduceQuantityShadowPriceKey(product);

        key1.Equals(key2).Should().BeTrue();
    }

    [Fact]
    public void TestConsumptionQuantityShadowPriceKey() {
        var material = new TestRawMaterial("Iron", 0);
        var key = new ConsumptionQuantityShadowPriceKey(material);

        key.Material.Should().Be(material);
        key.Limit.Should().Be(typeof(ConsumptionQuantityShadowPriceKey));
    }

    [Fact]
    public void TestConsumptionQuantityShadowPriceKeyEquality() {
        var material = new TestRawMaterial("Iron", 0);
        var key1 = new ConsumptionQuantityShadowPriceKey(material);
        var key2 = new ConsumptionQuantityShadowPriceKey(material);

        key1.Equals(key2).Should().BeTrue();
    }
}
