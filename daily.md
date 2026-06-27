# ospf-csharp 剩余未完成清单

**更新日期**: 2026-06-25（Round 9 后）
**当前状态**: 1,162/1,336 = **86.9%** 覆盖率，2,089 测试通过，0 失败，0 警告

---

## 一、总体差距

| 模块 | Kotlin | C# | 差距 | 状态 |
|------|--------|-----|------|------|
| Utils | 47 | 49 | -2 | ✅ 完成 |
| MultiArray | 9 | 11 | -2 | ✅ 完成 |
| **Math** | 267 | 246 | **21** | 🟡 接近 |
| **Quantities** | 63 | 52 | **11** | 🟡 接近 |
| **Core** | 140 | 137 | **3** | ≈ 接近 |
| **Framework** | 49 | 39 | **10** | ≈ 接近 |
| **Core.Plugin** | 82 | ~67 | **~15** | 🟡 结构化 |
| **Framework.Plugin** | 38 | 14 | **24** | 🟡 部分 |
| **Example** | 306 | 200 | **106** | 🟡 Demo2/4 域模型存根 |
| **Framework.Bpp3d** | 105 | 100 | **5** | ≈ 接近 |
| Framework.Bpp2d | 2 | 9 | -7 | ✅ 完成 |
| Framework.Csp1d | 84 | 86 | -2 | ✅ 完成 |
| Framework.GanttScheduling | 139 | 137 | 2 | ≈ 接近 |
| Benchmark | 5 | 5 | 0 | ✅ 完成 |

---

## 二、按优先级排序的未完成项

### 🔴 高优先级（影响功能完整性）

#### 1. FrameworkDemo Demo2 域模型存根（101→176）
- **当前状态**: 101 C# 文件，但 6 个域上下文中的域模型为存根实现（AggregationInitializer/PipelineListGenerator/SolutionAnalyzer 为空实现或 TODO）
- **具体差距**:
  - `Domain/Aircraft/Service/AggregationInitializer.cs` — 存根
  - `Domain/Aircraft/Service/NeighbourCalculator.cs` — 存根
  - `Domain/Stowage/Service/AggregationInitializer.cs` — 存根
  - `Domain/Stowage/Service/PipelineListGenerator.cs` — 存根
  - `Domain/Stowage/Service/SolutionAnalyzer.cs` — 存根
  - 4 个 MacOptimization/SoftSecurity/PayloadMaximization 的 AggregationInitializer — 存根
- **行动**: 补充每个域上下文的 AggregationInitializer + PipelineListGenerator + SolutionAnalyzer 实现
- **参考**: `ospf-kotlin/ospf-kotlin-example/.../framework_demo/demo2/domain/` 各 context 的 service 目录

#### 2. FrameworkDemo Demo4 域模型完善（53→81）
- **当前状态**: 53 C# 文件，B&P 算法已接线，但部分域模型为存根
- **具体差距**:
  - `Domain/Cargo/CargoDomain.cs` — 存根
  - `Domain/Passenger/PassengerDomain.cs` — 存根
  - `Domain/BunchGeneration/Service/` — 部分实现（RouteGraphGenerator 已实现，AggregationInitializer 存根）
  - `Application/Application.cs` — BuildDomainData 简化实现
- **行动**: 补充 Cargo/Passenger 域模型 + 完善 BunchGeneration service 实现
- **参考**: `ospf-kotlin/ospf-kotlin-example/.../framework_demo/demo4/domain/`

#### 3. BunchCostMinimization 管道（C# 泛型不变性问题）
- **当前状态**: `Demo4/Domain/BunchCompilation/Service/PipelineListGenerator.cs` 中 `BunchCostMinimization` 管道因 `BunchCompilation<FlightTaskBunch,...>` 无法向上转型为 `BunchCompilation<AbstractTaskBunch<...>,...>` 而跳过
- **行动**: 解决方案：(a) 使用接口协变，或 (b) 在 `AbstractTaskBunch` 层面实现 `BunchCostMinimization`，或 (c) 使用泛型约束放宽
- **参考**: `ospf-kotlin/ospf-kotlin-framework-gantt-scheduling/.../bunch_compilation/service/limits/BunchCostMinimization.kt`

### 🟡 中优先级（影响模块完整性）

#### 4. Math Symbol/Operation 差距（21 文件）
- **当前状态**: 246/267，Round 9 移植了 9 个文件
- **剩余差距**:
  - `Inequality` — 基础定义（已存在于 `Symbol/Inequality/`，可能是包结构差异）
  - `PowerVectorKey` — 已在 `Symbol/Monomial/PowerVectorKey.cs`
  - `QuickOps` — 已扩展到 `QuickDsl.cs` 中
  - `ToPolynomial` — 已在 `Symbol/PolynomialConversionInterfaces.cs`
- **实际剩余**: 可能是包结构差异（kotlin 数多于 C# 数，但功能已覆盖）；确认后无需额外移植
- **行动**: 逐文件比对确认哪些是真正的功能缺失 vs 包结构差异

#### 5. Framework.Plugin 差距（24 文件）
- **当前状态**: 14/38，EF Core 替代 ktorm/mybatis
- **具体差距**:
  - `persistence-ktorm` (12 kt) → EF Core 已替代（功能覆盖）
  - `persistence-mybatis` (8 kt) → EF Core 已替代
  - `persistence-mongodb` (10 kt) → C# MongoDB.Driver（1 文件，部分覆盖）
  - `persistence-redis` (1 kt) → C# StackExchange.Redis（1 文件）
  - `message-kafka` (2 kt) → C# Confluent.Kafka（1 文件）
  - `persistence-expression-ksp` (3 kt) → Roslyn 源生成器（1 文件，已启用）
- **行动**: 补充 MongoDB/Redis/Kafka 的完整 API 覆盖（当前只有部分接口）

#### 6. Core.Plugin 剩余适配器差距（~15 文件）
- **Gurobi11**: 4/9（缺 CG/Benders/Quadratic — CS1955 错误）
- **MindOPT**: 4/9（结构化移植，缺 CG/Benders/Quadratic）
- **SCIP**: 4/8（结构化移植，缺 CG/Benders/Quadratic）
- **COPT**: 6/10（缺 CG/Benders/Quadratic）
- **CPLEX**: 6/8（缺 CG/Benders/Quadratic）
- **行动**: 修复 CS1955 编译错误（`Task.FromResult<Result<...>>` 调用模式），补全 CG/Benders/Quadratic 结构化移植
- **参考**: `src/Core.Plugin.Gurobi/GurobiColumnGenerationSolver.cs` 和 `GurobiBendersDecompositionSolver.cs`（已实现，可作为模式参考）

#### 7. Framework 差距（10 文件）
- **当前状态**: 39/49
- **差距**: 扩展方法、管道辅助、远程 solver 域类型
- **行动**: 逐文件比对 kotlin Framework，补充缺失的扩展方法和辅助类型

#### 8. Core 差距（3 文件）
- **当前状态**: 137/140
- **差距**: 可能是包结构差异或少量扩展方法
- **行动**: 逐文件比对确认

### 🟢 低优先级（质量/平台完善）

#### 9. Bpp3d 剩余上下文（5 文件）
- **当前状态**: 100/105，Round 7-8 已补充大部分
- **差距**: 可能是 LayerGeneration/LayerAssignment 少量文件
- **行动**: 逐文件比对

#### 10. Quantities 差距（11 文件）
- **当前状态**: 52/63
- **差距**: unit 定义文件 + quantity 扩展方法
- **行动**: 补充缺失的物理量单位定义

#### 11. WorkingCalendar 完善（Round 7 部分）
- **当前状态**: 6 个文件（2,869 行），Round 7 移植
- **差距**: Kotlin 3,309 行，部分功能可能未完全移植
- **行动**: 逐方法比对确认

#### 12. Heuristic 泛型化 Int64/UInt64 实际求解测试
- **当前状态**: 9 个通用测试（类型验证），Flt64 实际求解测试已有
- **差距**: 缺少 Int64/UInt64 实际优化求解端到端测试
- **行动**: 添加使用 Int64/UInt64 作为决策变量的 GA/PSO 实际求解测试

#### 13. Roslyn 源生成器完善
- **当前状态**: `PredicateSchemaGenerator.cs` 已有，测试已启用
- **差距**: 可能需要更多 predicate schema 注解支持
- **行动**: 根据使用反馈迭代

#### 14. 示例测试完善
- **当前状态**: 213 Example 测试
- **差距**: Demo2/4 域模型存根测试可能过于简化
- **行动**: 随域模型完善同步更新测试

---

## 三、技术债务

| 项目 | 描述 | 优先级 |
|------|------|--------|
| C# 泛型不变性 | `BunchCompilation<FlightTaskBunch,...>` 无法向上转型 | 高 |
| CS1955 编译错误 | `Task.FromResult<Result<...>>` 调用模式问题（影响 CG/Benders 适配器） | 中 |
| 210 个 IDE 风格警告 | `dotnet format` 预警告（非编译器警告，未在 CI 中强制） | 低 |
| FrameworkDemo 域模型存根 | Demo2/4 部分 service 实现为空 | 高 |
| 供应商 solver CG/Benders | COPT/CPLEX/SCIP/Gurobi11/MindOPT 缺 CG/Benders/Quadratic | 中 |

---

## 四、下一轮建议执行顺序

1. **P0**: 补充 Demo2 域模型存根（AggregationInitializer/PipelineListGenerator/SolutionAnalyzer）
2. **P0**: 补充 Demo4 域模型存根（Cargo/Passenger + BunchGeneration services）
3. **P1**: 修复 CS1955 编译错误，补全供应商 CG/Benders/Quadratic
4. **P1**: 补充 FrameworkPlugin MongoDB/Redis/Kafka 完整覆盖
5. **P2**: 补充 Math/Quantities/Core/Framework 剩余差距
6. **P2**: WorkingCalendar 完善
7. **P3**: Heuristic 泛型化实际求解测试
8. **P3**: 技术债务清理

---

## 五、验证命令

```bash
# 构建
dotnet build ospf-csharp.sln -c Release --no-incremental

# 测试
dotnet test ospf-csharp.sln -c Release --no-build

# 覆盖率
dotnet test ospf-csharp.sln -c Debug --collect:"XPlat Code Coverage"

# 格式检查
dotnet format ospf-csharp.sln --verify-no-changes

# 跨平台验证
dotnet build ospf-csharp.sln -c Release --os linux

# 基准测试
dotnet run -c Release --project src/Benchmark -- --filter '*'
```
