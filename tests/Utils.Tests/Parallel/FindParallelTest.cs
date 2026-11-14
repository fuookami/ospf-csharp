#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Utils.Parallel;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using RetBool = Fuookami.Ospf.Utils.Functional.Result<bool, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using RetError = Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>;

namespace Fuookami.Ospf.Utils.Tests.Parallel;

public class FindParallelTest {
    [Fact]
    public async Task BooleanPredicatesMatchSequenceSemantics() {
        int[] source = { 1, 2, 3, 4 };

        foreach (int concurrency in new[] { 1, 4 }) {
            (await source.AllParallelly(value => value < 5, concurrency)).Should().BeTrue();
            (await source.AllParallelly(value => value < 4, concurrency)).Should().BeFalse();
            (await source.AnyParallelly(value => value == 3, concurrency)).Should().BeTrue();
            (await source.NoneParallelly(value => value > 4, concurrency)).Should().BeTrue();
            (await source.CountParallelly(value => value % 2 == 0, concurrency)).Should().Be(2);
        }

        (await Array.Empty<int>().AllParallelly(_ => false)).Should().BeTrue();
        (await Array.Empty<int>().AnyParallelly(_ => true)).Should().BeFalse();
        (await Array.Empty<int>().NoneParallelly(_ => true)).Should().BeTrue();
    }

    [Fact]
    public async Task TryPredicatesPreserveFailures() {
        int[] source = { 1, 2, 3 };
        var error = new Err<ErrorCode>(ErrorCode.ApplicationException, "failed");

        RetBool allResult = await source.TryAllParallelly(
            value => value == 2
                ? Results.Ok(false)
                : new Failed<bool, ErrorCode, RetError>(error),
            concurrency: 4);
        RetBool anyResult = await source.TryAnyParallelly(
            value => value == 2
                ? Results.Ok(true)
                : new Failed<bool, ErrorCode, RetError>(error),
            concurrency: 4);
        RetBool noneResult = await source.TryNoneParallelly(
            _ => new Failed<bool, ErrorCode, RetError>(error),
            concurrency: 4);

        allResult.Should().BeOfType<Failed<bool, ErrorCode, RetError>>();
        anyResult.Should().BeOfType<Failed<bool, ErrorCode, RetError>>();
        noneResult.Should().BeOfType<Failed<bool, ErrorCode, RetError>>();
        ((Failed<bool, ErrorCode, RetError>)allResult).Error.Should().Be(error);
        ((Failed<bool, ErrorCode, RetError>)anyResult).Error.Should().Be(error);
        ((Failed<bool, ErrorCode, RetError>)noneResult).Error.Should().Be(error);
    }

    [Fact]
    public async Task TryPredicatePreservesFatalFailures() {
        int[] source = { 1, 2 };
        var fatal = new Fatal<bool, ErrorCode, RetError>(
            new Err<ErrorCode>(ErrorCode.ApplicationException, "one"),
            new Err<ErrorCode>(ErrorCode.ApplicationException, "two"));

        RetBool result = await source.TryAnyParallelly(_ => fatal, concurrency: 2);

        result.Should().BeOfType<Fatal<bool, ErrorCode, RetError>>();
        ((Fatal<bool, ErrorCode, RetError>)result).Size.Should().Be(2);
    }

    [Fact]
    public async Task ExTryPredicatesCollectEveryFailure() {
        int[] source = { 1, 2, 3, 4 };
        int invoked = 0;

        ExResult<bool, ErrorCode, RetError> result = await source.ExTryAllParallelly(value => {
            Interlocked.Increment(ref invoked);
            return value % 2 == 0
                ? Results.Ok(true)
                : new Failed<bool, ErrorCode, RetError>(
                    ErrorCode.ApplicationException,
                    $"failed-{value}");
        }, concurrency: 4);

        invoked.Should().Be(4);
        result.Should().BeOfType<Fatal<bool, ErrorCode, RetError>>();
        ((Fatal<bool, ErrorCode, RetError>)result).Size.Should().Be(2);
    }

    [Fact]
    public async Task ExTryAnyRemainsFatalWhenAnotherPredicateSucceeds() {
        ExResult<bool, ErrorCode, RetError> result = await new[] { 1, 2, 3 }.ExTryAnyParallelly(
            value => value == 2
                ? Results.Ok(true)
                : new Failed<bool, ErrorCode, RetError>(
                    ErrorCode.ApplicationException,
                    $"failed-{value}"),
            concurrency: 3);

        result.Should().BeOfType<Fatal<bool, ErrorCode, RetError>>();
        ((Fatal<bool, ErrorCode, RetError>)result).Size.Should().Be(2);
    }
}
