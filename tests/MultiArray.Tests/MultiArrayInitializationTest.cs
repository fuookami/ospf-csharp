#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.MultiArray.Tests;

public class MultiArrayInitializationTest {
    [Fact]
    public void ConstantFactoriesDoNotDecodeCoordinates() {
        foreach (StorageOrder order in Enum.GetValues<StorageOrder>()) {
            var shape = new CountingShape(DynShape.WithOrder(new[] { 2, 3, 4 }, order));
            object value = new();
            MultiArray<object, CountingShape> immutable =
                MultiArray<object, CountingShape>.Factory.NewWith(shape, value);
            MutableMultiArray<object, CountingShape> mutable =
                MutableMultiArray<object, CountingShape>.Factory.NewWith(shape, value);

            immutable.Should().OnlyContain(item => ReferenceEquals(item, value));
            mutable.Should().OnlyContain(item => ReferenceEquals(item, value));
            shape.CoordinateReads.Should().Be(0);
        }
    }

    [Fact]
    public void DefaultFactoriesPreserveEmptyAndScalarShapes() {
        foreach (int[] dimensions in new[] {
            new[] { 2, 0, 3 },
            Array.Empty<int>(),
        }) {
            IShape shape = new CountingShape(DynShape.Invoke(dimensions));
            int expectedSize = dimensions.Length == 0 ? 1 : 0;

            MultiArray<int, IShape>.Factory.NewWith(shape, 7).Should().HaveCount(expectedSize);
            MutableMultiArray<int, IShape>.Factory.NewWith(shape, 7).Should().HaveCount(expectedSize);
        }
    }

    [Fact]
    public void GeneratorsReceiveIndependentCoordinates() {
        var shape = DynShape.Invoke(new[] { 2, 3 });
        var retained = new List<int[]>();
        MultiArray<int, DynShape> array = MultiArray<int, DynShape>.Factory.NewBy(shape, (index, vector) => {
            retained.Add(vector);
            return index;
        });

        array.ToList().Should().Equal(Enumerable.Range(0, shape.Size));
        for (int i = 0; i < retained.Count; i++) {
            retained[i].Should().Equal(shape.VectorUnchecked(i));
        }

        retained[0][0] = 99;
        retained[1][0].Should().NotBe(99);
    }

    private sealed class CountingShape : IShape {
        private readonly IShape _inner;

        public CountingShape(IShape inner) => _inner = inner;

        public int CoordinateReads { get; private set; }
        public int Dimension => _inner.Dimension;
        public int Size => _inner.Size;
        public int[] Offsets => _inner.Offsets;
        public StorageOrder StorageOrder => _inner.StorageOrder;
        public int this[int index] => _inner[index];

        public Result<int, ErrorCode, Error<ErrorCode>> Index(int[] vector) => _inner.Index(vector);

        public Result<int[], ErrorCode, Error<ErrorCode>> Vector(int index) {
            CoordinateReads++;
            return _inner.Vector(index);
        }
    }
}
