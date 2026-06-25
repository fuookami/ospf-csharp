#nullable enable

using FluentAssertions;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.MultiArray.Tests;

public class DataFrameTest {
    [Fact]
    public void DataFrame_BasicConstruction() {
        var df = new DataFrame<int>(3, 2, new[] { "A", "B" });
        df.NRows.Should().Be(3);
        df.NCols.Should().Be(2);
        df.ColumnNames.Should().Equal("A", "B");
    }

    [Fact]
    public void DataFrame_SetAndGet() {
        var df = new DataFrame<int>(2, 2, new[] { "X", "Y" });
        df.Set(0, 0, 1);
        df.Set(0, 1, 2);
        df.Set(1, 0, 3);
        df.Set(1, 1, 4);

        df.Get(0, 0).Should().Be(1);
        df.Get(1, 1).Should().Be(4);
    }

    [Fact]
    public void DataFrame_GetByNameSafe() {
        var df = new DataFrame<string>(2, 2, new[] { "Name", "Age" });
        df.Set(0, 0, "Alice");
        df.Set(0, 1, "30");

        Result<string?, ErrorCode, Error<ErrorCode>> result = df.GetByNameSafe(0, "Name");
        result.IsOk.Should().BeTrue();
        result.Value.Should().Be("Alice");
    }

    [Fact]
    public void DataFrame_GetColumnByNameSafe_NotFound() {
        var df = new DataFrame<int>(2, 2, new[] { "A", "B" });
        Result<IReadOnlyList<int>, ErrorCode, Error<ErrorCode>> result = df.GetColumnByNameSafe("C");
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void DataFrame_FromMap() {
        var data = new Dictionary<string, IReadOnlyList<string?>>
        {
            { "X", new string?[] { "a", "b", "c" } },
            { "Y", new string?[] { "d", "e", "f" } }
        };
        var df = DataFrame<string>.FromMap(data);

        df.NRows.Should().Be(3);
        df.NCols.Should().Be(2);
        df.Get(0, 0).Should().Be("a");
        df.Get(2, 1).Should().Be("f");
    }

    [Fact]
    public void DataFrame_Filter() {
        var df = new DataFrame<int>(3, 2, new[] { "A", "B" });
        df.Set(0, 0, 1); df.Set(0, 1, 10);
        df.Set(1, 0, 2); df.Set(1, 1, 20);
        df.Set(2, 0, 3); df.Set(2, 1, 30);

        DataFrame<int> filtered = df.Filter(row => row[0] > 1);
        filtered.NRows.Should().Be(2);
        filtered.Get(0, 0).Should().Be(2);
    }

    [Fact]
    public void DataFrame_CopyWithAddedRow() {
        var df = new DataFrame<string>(1, 2, new[] { "A", "B" });
        df.Set(0, 0, "x"); df.Set(0, 1, "y");

        IReadOnlyList<string?> newRow = new string?[] { "a", "b" };
        DataFrame<string> newDf = df.CopyWithAddedRow(newRow);
        newDf.NRows.Should().Be(2);
        newDf.Get(1, 0).Should().Be("a");
    }

    [Fact]
    public void DataFrameBuilder_Works() {
        var df = DataFrame<int>.Build(builder => {
            builder.Row(1, 2);
            builder.Row(3, 4);
        }, "X", "Y");

        df.NRows.Should().Be(2);
        df.Get(0, 0).Should().Be(1);
        df.Get(1, 1).Should().Be(4);
    }

    [Fact]
    public void NullableValue_ToString() {
        var nv = new NullableValue<string>("hello");
        nv.ToString().Should().Be("hello");

        var nvNull = new NullableValue<string>(null);
        nvNull.ToString().Should().Be("null");
    }
}
