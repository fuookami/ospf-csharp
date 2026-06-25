#nullable enable

using System;
using System.Linq;
using FluentAssertions;
using Xunit;

using Fuookami.Ospf.MultiArray;

namespace Fuookami.Ospf.MultiArray.Tests
{
    public class ShapeTest
    {
        [Fact]
        public void Shape1_CreateAndAccess()
        {
            var shape = Shape1.Invoke(5);
            shape.Dimension.Should().Be(1);
            shape.Size.Should().Be(5);
            shape[0].Should().Be(5);
            shape.StorageOrder.Should().Be(StorageOrder.RowMajor);
        }

        [Fact]
        public void Shape2_IndexAndVector()
        {
            var shape = Shape2.Invoke(3, 4);
            shape.Dimension.Should().Be(2);
            shape.Size.Should().Be(12);

            var indexResult = shape.Index(new[] { 1, 2 });
            indexResult.IsOk.Should().BeTrue();
            indexResult.Value.Should().Be(6); // 1*4 + 2*1 = 6

            var vectorResult = shape.Vector(6);
            vectorResult.IsOk.Should().BeTrue();
            vectorResult.Value.Should().Equal(1, 2);
        }

        [Fact]
        public void Shape3_WithColumnMajor()
        {
            var shape = Shape3.WithOrder(2, 3, 4, StorageOrder.ColumnMajor);
            shape.Offsets.Should().Equal(1, 2, 6);

            var indexResult = shape.Index(new[] { 1, 2, 1 });
            indexResult.IsOk.Should().BeTrue();
            indexResult.Value.Should().Be(1 + 2 * 2 + 1 * 6); // 1 + 4 + 6 = 11
        }

        [Fact]
        public void Shape4_BasicOperations()
        {
            var shape = Shape4.Invoke(2, 3, 4, 5);
            shape.Size.Should().Be(120);
            shape.Dimension.Should().Be(4);

            var indexResult = shape.Index(new[] { 1, 2, 3, 4 });
            indexResult.IsOk.Should().BeTrue();
            indexResult.Value.Should().Be(1 * 60 + 2 * 20 + 3 * 5 + 4);
        }

        [Fact]
        public void DynShape_CreateAndResize()
        {
            var shape = DynShape.Invoke(new[] { 2, 3, 4 });
            shape.Dimension.Should().Be(3);
            shape.Size.Should().Be(24);
            shape.StorageOrder.Should().Be(StorageOrder.RowMajor);
        }

        [Fact]
        public void Shape_IndexOutOfBounds_ReturnsFailed()
        {
            var shape = Shape2.Invoke(3, 4);
            var result = shape.Index(new[] { 5, 0 });
            result.IsFailed.Should().BeTrue();
        }

        [Fact]
        public void Shape_DimensionMismatch_ReturnsFailed()
        {
            var shape = Shape2.Invoke(3, 4);
            var result = shape.Index(new[] { 1 });
            result.IsFailed.Should().BeTrue();
        }
    }
}
