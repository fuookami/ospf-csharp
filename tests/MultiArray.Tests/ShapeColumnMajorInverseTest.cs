#nullable enable

using FluentAssertions;
using System;
using Xunit;

namespace Fuookami.Ospf.MultiArray.Tests;

public class ShapeColumnMajorInverseTest {
    [Fact]
    public void ColumnMajorVectorAndIndexRoundTrip() {
        IShape[] shapes = {
            Shape2.WithOrder(3, 4, StorageOrder.ColumnMajor),
            Shape3.WithOrder(2, 3, 4, StorageOrder.ColumnMajor),
            Shape4.WithOrder(2, 3, 4, 5, StorageOrder.ColumnMajor),
            DynShape.WithOrder(new[] { 2, 3, 4, 5 }, StorageOrder.ColumnMajor),
        };

        foreach (IShape shape in shapes) {
            for (int i = 0; i < shape.Size; i++) {
                int[] vector = shape.Vector(i).Value;
                shape.Index(vector).Value.Should().Be(i);
            }
        }
    }
}
