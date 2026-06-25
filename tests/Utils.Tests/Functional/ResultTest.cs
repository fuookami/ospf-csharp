#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Utils.Tests.Functional
{
    using R = Result<int, ErrorCode, Error<ErrorCode>>;
    using RS = Result<Success, ErrorCode, Error<ErrorCode>>;
    using ER = ExResult<int, ErrorCode, Error<ErrorCode>>;

    public class ResultTest
    {
        [Fact]
        public void Ok_IsOk_True()
        {
            R result = Results.Ok(42);
            result.IsOk.Should().BeTrue();
            result.IsFailed.Should().BeFalse();
            result.Value.Should().Be(42);
        }

        [Fact]
        public void Failed_IsFailed_True()
        {
            R result = new Failed<int, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, "bad arg");
            result.IsFailed.Should().BeTrue();
            result.IsOk.Should().BeFalse();
        }

        [Fact]
        public void Fatal_IsFailed_WithMultipleErrors()
        {
            R result = new Fatal<int, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.DataNotFound, "not found"),
                new Err<ErrorCode>(ErrorCode.DataEmpty, "empty"));
            result.IsFailed.Should().BeTrue();
            ((Fatal<int, ErrorCode, Error<ErrorCode>>)result).Size.Should().Be(2);
        }

        [Fact]
        public void Warn_IsWarned_True()
        {
            ER result = new Warn<int, ErrorCode, Error<ErrorCode>>(42, ErrorCode.Other, "warning");
            result.IsWarned.Should().BeTrue();
            result.IsOk.Should().BeFalse();
            result.IsFailed.Should().BeFalse();
            ((Warn<int, ErrorCode, Error<ErrorCode>>)result).Value.Should().Be(42);
        }

        [Fact]
        public void Map_Ok_TransformsValue()
        {
            var result = Results.Ok(21).Map(x => x * 2);
            result.IsOk.Should().BeTrue();
            result.Value.Should().Be(42);
        }

        [Fact]
        public void Map_Failed_PropagatesError()
        {
            R failed = new Failed<int, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, "bad");
            var result = failed.Map(x => x * 2);
            result.IsFailed.Should().BeTrue();
        }

        [Fact]
        public void Bind_Ok_ChainsOperations()
        {
            var result = Results.Ok(21).Bind<int>(x => Results.Ok(x * 2));
            result.IsOk.Should().BeTrue();
            result.Value.Should().Be(42);
        }

        [Fact]
        public void Bind_Failed_ShortCircuits()
        {
            R failed = new Failed<int, ErrorCode, Error<ErrorCode>>(ErrorCode.DataNotFound, "not found");
            var result = failed.Bind<int>(x => Results.Ok(x * 2));
            result.IsFailed.Should().BeTrue();
        }

        [Fact]
        public void Success_Instance_IsSingleton()
        {
            var s1 = Results.SuccessInstance;
            var s2 = Results.SuccessInstance;
            s1.Should().BeSameAs(s2);
        }

        [Fact]
        public void Try_Ok_IsSuccess()
        {
            Try ok = Results.OkInstance;
            ok.IsOk.Should().BeTrue();
        }

        [Fact]
        public void Run_AllSucceed_ReturnsOk()
        {
            var result = Results.Run(
                () => (RS)Results.OkInstance,
                () => (RS)Results.OkInstance);
            result.IsOk.Should().BeTrue();
        }

        [Fact]
        public void Run_OneFails_StopsAndReturnsFailed()
        {
            var result = Results.Run(
                () => (RS)Results.OkInstance,
                () => (RS)new Failed<Success, ErrorCode, Error<ErrorCode>>(ErrorCode.DataNotFound, "not found"),
                () => (RS)Results.OkInstance);
            result.IsFailed.Should().BeTrue();
        }

        [Fact]
        public void LINQ_SelectMany_WorksCorrectly()
        {
            var result = from a in Results.Ok(10)
                         from b in Results.Ok(20)
                         select a + b;
            result.IsOk.Should().BeTrue();
            result.Value.Should().Be(30);
        }

        [Fact]
        public void LINQ_SelectMany_FailedShortCircuits()
        {
            var result = from a in Results.Ok(10)
                         from b in (R)new Failed<int, ErrorCode, Error<ErrorCode>>(ErrorCode.DataNotFound, "not found")
                         select a + b;
            result.IsFailed.Should().BeTrue();
        }

        [Fact]
        public void IfOk_ExecutesOnSuccess()
        {
            int captured = 0;
            Results.Ok(42).IfOk(ok => captured = ok.Value);
            captured.Should().Be(42);
        }

        [Fact]
        public void IfFailed_ExecutesOnFailure()
        {
            ErrorCode captured = ErrorCode.None;
            R failed = new Failed<int, ErrorCode, Error<ErrorCode>>(ErrorCode.DataNotFound, "not found");
            failed.IfFailed(f => captured = ((Failed<int, ErrorCode, Error<ErrorCode>>)f).Code);
            captured.Should().Be(ErrorCode.DataNotFound);
        }

        // Note: BindAsync/MapAsync extension method tests deferred to Phase 3
        // (async extension methods require further investigation on .NET 10)
    }
}
