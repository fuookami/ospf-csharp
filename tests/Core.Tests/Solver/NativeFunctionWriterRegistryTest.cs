#nullable enable

using System;
using System.Collections.Generic;
using Xunit;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Solver;

public class NativeFunctionWriterRegistryTest {
    [Fact]
    public void Write_ShouldDispatchInOrderAndSkipEmptyBatches() {
        var calls = new List<string>();
        var registry = new NativeFunctionWriterRegistry<string, string>([
            new DelegateWriter((_, _, batch) => {
                calls.Add($"first:{batch[0]}");
                return Results.OkInstance;
            }),
            new DelegateWriter((_, _, batch) => {
                calls.Add($"second:{batch[0]}");
                return Results.OkInstance;
            }),
        ]);

        Try result = registry.Write(
            model: "model",
            variables: new Dictionary<VariableItemKey, string>(),
            batches: [
                new object[] { "a" },
                [],
            ]
        );

        Assert.True(result.IsOk);
        Assert.Equal(new[] { "first:a" }, calls);
    }

    [Fact]
    public void TypedWriter_ShouldFilterStructuresAndStillDispatchMixedBatch() {
        var calls = new List<string>();
        var writer = new StringWriter(calls);

        Try result = writer.Write(
            model: "model",
            variables: new Dictionary<VariableItemKey, string>(),
            batch: new object[] { "first", 3, "second" }
        );

        Assert.True(result.IsOk);
        Assert.Equal(new[] { "first", "second" }, calls);
    }

    [Fact]
    public void Write_ShouldRejectCountMismatchBeforeDispatch() {
        int callCount = 0;
        var registry = new NativeFunctionWriterRegistry<string, string>([
            new DelegateWriter((_, _, _) => {
                callCount++;
                return Results.OkInstance;
            }),
        ]);

        Try result = registry.Write(
            model: "model",
            variables: new Dictionary<VariableItemKey, string>(),
            batches: []
        );

        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(result).Code);
        Assert.Equal(0, callCount);
    }

    [Fact]
    public void Write_ShouldStopAtFirstFailure() {
        var calls = new List<string>();
        var registry = new NativeFunctionWriterRegistry<string, string>([
            new DelegateWriter((_, _, _) => {
                calls.Add("first");
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "first failed"));
            }),
            new DelegateWriter((_, _, _) => {
                calls.Add("second");
                return Results.OkInstance;
            }),
        ]);

        Try result = registry.Write(
            model: "model",
            variables: new Dictionary<VariableItemKey, string>(),
            batches: [
                new object[] { "a" },
                new object[] { "b" },
            ]
        );

        Assert.Equal(
            ErrorCode.ApplicationFailed,
            Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(result).Code);
        Assert.Equal(new[] { "first" }, calls);
    }

    private sealed class DelegateWriter(
        Func<
            string,
            IReadOnlyDictionary<VariableItemKey, string>,
            IReadOnlyList<object>,
            Try> write)
        : INativeFunctionWriter<string, string> {
        public Try Write(
            string model,
            IReadOnlyDictionary<VariableItemKey, string> variables,
            IReadOnlyList<object> batch) =>
            write(model, variables, batch);
    }

    private sealed class StringWriter(List<string> calls)
        : TypedNativeFunctionWriter<string, string, string> {
        protected override Try WriteTyped(
            string model,
            IReadOnlyDictionary<VariableItemKey, string> variables,
            IReadOnlyList<string> batch) {
            calls.AddRange(batch);
            return Results.OkInstance;
        }
    }
}
