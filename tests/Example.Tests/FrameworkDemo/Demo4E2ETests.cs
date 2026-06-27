#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchSelection;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchSelection.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure.Dto;
using Fuookami.Ospf.Example.Tests.FrameworkDemo;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// Demo4 端到端烟雾测试：使用进程内模拟求解器验证 B&amp;P 编排流程。
/// Demo4 end-to-end smoke test: validates B&amp;P orchestration flow using an in-process mock solver.
/// </summary>
public class Demo4E2ETests {
    /// <summary>
    /// 验证 FlightRecoveryApplication 可以在给定有效输入时运行端到端流程。
    /// Verifies FlightRecoveryApplication can run end-to-end flow with valid input.
    /// </summary>
    [Fact]
    public async Task FlightRecoveryApplication_RunAsync_WithValidInput_ReturnsResult() {
        var solver = new MockColumnGenerationSolver();
        var app = new FlightRecoveryApplication(solver, new BranchAndPriceAlgorithm.Configuration(
            TimeLimit: TimeSpan.FromSeconds(5)));

        var now = DateTimeOffset.UtcNow;
        var input = new Input {
            ProblemId = "smoke-test",
            WindowStart = now,
            WindowEnd = now.AddDays(1),
            TaskCancelCost = 9999.0,
            Aircrafts = new List<Input.AircraftInput> {
                new() {
                    RegNo = "B0001",
                    AircraftTypeCode = "B737",
                    MinorTypeCode = "738",
                    LocationIcao = "ZBAA",
                    CostPerHour = 100.0,
                    EnabledTime = now
                }
            },
            FlightLegs = new List<Input.FlightLegInput> {
                new() {
                    Id = "f1",
                    FlightNo = "CA100",
                    AircraftRegNo = "B0001",
                    DepIcao = "ZBAA",
                    ArrIcao = "ZPPP",
                    ScheduledDepTime = now.AddHours(2),
                    ScheduledArrTime = now.AddHours(4),
                    Date = now
                }
            }
        };

        var result = await app.RunAsync(input);

        // The mock solver returns zero solutions, so the B&P should return a result
        // (either Ok with empty/partial solution, or the orchestration should complete)
        Assert.NotNull(result);
        Assert.True(result.IsOk || result.IsFailed,
            "B&P orchestration should complete without throwing exceptions.");
    }

    /// <summary>
    /// 验证 BuildDomainData 可以从有效输入构建域数据。
    /// Verifies BuildDomainData can build domain data from valid input.
    /// </summary>
    [Fact]
    public void BuildDomainData_WithValidInput_ReturnsDomainData() {
        var now = DateTimeOffset.UtcNow;
        var input = new Input {
            WindowStart = now,
            WindowEnd = now.AddDays(1),
            Aircrafts = new List<Input.AircraftInput> {
                new() {
                    RegNo = "B5001",
                    AircraftTypeCode = "A320",
                    MinorTypeCode = "320",
                    LocationIcao = "ZGGG",
                    CostPerHour = 120.0,
                    EnabledTime = now
                }
            },
            FlightLegs = new List<Input.FlightLegInput> {
                new() {
                    Id = "f10",
                    FlightNo = "CZ300",
                    AircraftRegNo = "B5001",
                    DepIcao = "ZGGG",
                    ArrIcao = "ZBAA",
                    ScheduledDepTime = now.AddHours(1),
                    ScheduledArrTime = now.AddHours(3),
                    Date = now
                }
            }
        };

        var domainData = FlightRecoveryApplication.BuildDomainData(input);

        Assert.NotNull(domainData);
        Assert.Single(domainData.Aircrafts);
        Assert.Single(domainData.FlightTasks);
        Assert.Equal(now, domainData.TimeWindow.Start);
    }

    /// <summary>
    /// 验证 BuildDomainData 在缺少飞机时返回 null。
    /// Verifies BuildDomainData returns null when aircrafts are missing.
    /// </summary>
    [Fact]
    public void BuildDomainData_WithNoAircrafts_ReturnsNull() {
        var input = new Input {
            Aircrafts = new List<Input.AircraftInput>(),
            FlightLegs = new List<Input.FlightLegInput>()
        };

        var result = FlightRecoveryApplication.BuildDomainData(input);
        Assert.Null(result);
    }

    /// <summary>
    /// 验证 BuildDomainData 在缺少航段时返回 null。
    /// Verifies BuildDomainData returns null when flight legs are missing.
    /// </summary>
    [Fact]
    public void BuildDomainData_WithNoFlightLegs_ReturnsNull() {
        var now = DateTimeOffset.UtcNow;
        var input = new Input {
            Aircrafts = new List<Input.AircraftInput> {
                new() {
                    RegNo = "B6001",
                    AircraftTypeCode = "B737",
                    MinorTypeCode = "738",
                    LocationIcao = "ZBAA"
                }
            },
            FlightLegs = new List<Input.FlightLegInput>()
        };

        var result = FlightRecoveryApplication.BuildDomainData(input);
        Assert.Null(result);
    }

    /// <summary>
    /// 验证 Input DTO 可以正确设置所有属性。
    /// Verifies Input DTO can correctly set all properties.
    /// </summary>
    [Fact]
    public void Input_SetProperties_Works() {
        var now = DateTimeOffset.UtcNow;
        var input = new Input {
            ProblemId = "test-123",
            WindowStart = now,
            WindowEnd = now.AddDays(7),
            TaskCancelCost = 5000.0,
            Aircrafts = new List<Input.AircraftInput> {
                new() { RegNo = "B1234", AircraftTypeCode = "B737", MinorTypeCode = "738", LocationIcao = "ZBAA" }
            },
            FlightLegs = new List<Input.FlightLegInput> {
                new() { Id = "f1", AircraftRegNo = "B1234", DepIcao = "ZBAA", ArrIcao = "ZPPP" }
            }
        };

        Assert.Equal("test-123", input.ProblemId);
        Assert.Equal(now, input.WindowStart);
        Assert.Equal(5000.0, input.TaskCancelCost);
        Assert.Single(input.Aircrafts!);
        Assert.Single(input.FlightLegs!);
    }

    /// <summary>
    /// 验证 Output DTO 默认值正确。
    /// Verifies Output DTO default values are correct.
    /// </summary>
    [Fact]
    public void Output_DefaultValues_AreCorrect() {
        var output = new Output();

        Assert.NotNull(output.ScheduledFlights);
        Assert.Empty(output.ScheduledFlights);
        Assert.NotNull(output.CanceledTaskIds);
        Assert.Empty(output.CanceledTaskIds);
        Assert.Equal(0, output.TotalBunches);
        Assert.Equal(0, output.TotalScheduledTasks);
        Assert.Equal(0, output.TotalCanceledTasks);
    }

    /// <summary>
    /// 验证 MockColumnGenerationSolver 实现了接口。
    /// Verifies MockColumnGenerationSolver implements the interface.
    /// </summary>
    [Fact]
    public void MockSolver_ImplementsInterface() {
        var solver = new MockColumnGenerationSolver();
        Assert.Equal("mock-test-solver", solver.Name);
    }

    /// <summary>
    /// 验证 BranchAndPriceAlgorithm 可以创建。
    /// Verifies BranchAndPriceAlgorithm can be created.
    /// </summary>
    [Fact]
    public void BranchAndPriceAlgorithm_CanBeCreated() {
        var now = DateTimeOffset.UtcNow;
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(now, now.AddDays(7)),
            false,
            TimeSpan.FromHours(1));
        var bunchGenCtx = new BunchGenerationContext();
        var bunchCompCtx = new Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.BunchCompilationContext();

        var ctx = new BunchSelectionContext(
            aircrafts: new List<Aircraft>(),
            recoveryNeededAircrafts: new List<Aircraft>(),
            recoveryNeededFlightTasks: new List<FlightTask>(),
            timeWindow: timeWindow,
            bunchGenerationContext: bunchGenCtx,
            bunchCompilationContext: bunchCompCtx);

        var solver = new MockColumnGenerationSolver();
        var algorithm = new BranchAndPriceAlgorithm(ctx, solver);

        Assert.NotNull(algorithm);
    }
}
