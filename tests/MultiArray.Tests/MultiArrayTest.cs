#nullable enable

using FluentAssertions;
using Fuookami.Ospf.MultiArray;
using System;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.MultiArray.Tests;

public class MultiArrayTest {
    [Fact]
    public void NewWith_CreatesArrayWithFillValue() {
        MultiArray<int, Shape2> arr = MultiArray<int, Shape2>.Factory.NewWith(Shape2.Invoke(2, 3), 42);
        arr.Count.Should().Be(6);
        arr[0].Should().Be(42);
        arr[5].Should().Be(42);
    }

    [Fact]
    public void NewBy_CreatesArrayWithGenerator() {
        MultiArray<int, Shape2> arr = MultiArray<int, Shape2>.Factory.NewBy(Shape2.Invoke(2, 3), (i, v) => i * 10);
        arr[0].Should().Be(0);
        arr[1].Should().Be(10);
        arr[5].Should().Be(50);
    }

    [Fact]
    public void Indexer_VectorIndex() {
        MultiArray<int, Shape2> arr = MultiArray<int, Shape2>.Factory.NewWith(Shape2.Invoke(2, 3), 0);
        MutableMultiArray<int, Shape2> mutable = arr.ToMutable();
        mutable[new[] { 0, 1 }] = 7;
        mutable[new[] { 1, 2 }] = 99;
        mutable.ToImmutable()[new[] { 0, 1 }].Should().Be(7);
        mutable.ToImmutable()[new[] { 1, 2 }].Should().Be(99);
    }

    [Fact]
    public void Enumerate_ReturnsCorrectTuples() {
        MultiArray<int, Shape1> arr = MultiArray<int, Shape1>.Factory.NewBy(Shape1.Invoke(3), (i, v) => i * 2);
        var items = arr.Enumerate().ToList();
        items.Should().HaveCount(3);

        items[0].LinearIndex.Should().Be(0);
        items[0].Value.Should().Be(0);
        items[1].LinearIndex.Should().Be(1);
        items[1].Value.Should().Be(2);
        items[2].LinearIndex.Should().Be(2);
        items[2].Value.Should().Be(4);
    }

    [Fact]
    public void MutableMultiArray_Fill() {
        MutableMultiArray<int, Shape2> arr = MutableMultiArray<int, Shape2>.Factory.NewWith(Shape2.Invoke(2, 3), 0);
        arr.Fill(5);
        for (int i = 0; i < 6; i++) {
            arr[i].Should().Be(5);
        }
    }

    [Fact]
    public void MutableMultiArray_SetAndGet() {
        MutableMultiArray<int, Shape2> arr = MutableMultiArray<int, Shape2>.Factory.NewWith(Shape2.Invoke(2, 3), 0);
        arr[new[] { 1, 1 }] = 42;
        arr[new[] { 1, 1 }].Should().Be(42);
    }

    [Fact]
    public void ToStorageOrder_ConvertsCorrectly() {
        MultiArray<int, Shape2> arr = MultiArray<int, Shape2>.Factory.NewBy(Shape2.Invoke(2, 3), (i, v) => i);
        MultiArray<int, DynShape> colMajor = arr.ToStorageOrder(StorageOrder.ColumnMajor);

        // Same values accessible by same vector
        colMajor[new[] { 0, 0 }].Should().Be(arr[new[] { 0, 0 }]);
        colMajor[new[] { 1, 2 }].Should().Be(arr[new[] { 1, 2 }]);
    }
}
