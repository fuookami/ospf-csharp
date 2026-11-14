# Kotlin → C# 迁移指南

本文件记录把 `ospf-kotlin`（固定基线 `12fdb587`）移植到 `ospf-csharp` 时**反复踩到的坑**。
它不是命名对照表——命名对照在 `docs/kotlin-sync-status.md` 里逐条维护；
这里只写"名字对上了、代码也编译过了，但行为是错的"那类问题。

## 一、命名对照的四个陷阱

| Kotlin | C# | 陷阱 |
| --- | --- | --- |
| `Abstract*` | `I*` | `AbstractFoo` → `IFoo`，不是 `AbstractFoo` 或 `Foo` |
| `*AcceptancePolicy` | `IMemoryAcceptance<O,V>` | 名字完全不同，`grep AcceptancePolicy` 找不到 |
| `SelectionPolicy` | `ISelectionPolicy` | 前缀 `I` |
| `sealed class X(...)`（数据载体） | `sealed record X(...)` | **`grep "class X"` 会给出假的 MISSING** |

第四条最容易误判：更新类型在 C# 侧是 `sealed record`，用 `grep "class"` 搜会以为还没移植。

## 二、泛型：C# 没有通配符

Kotlin 的 `AbstractMultiArray<T, *>` 在 C# **无对应语法**。必须显式写出约束：

```csharp
// Kotlin: AbstractMultiArray<T, *> where * : IShape
public abstract class MultiArray<T, S> where S : IShape
```

由此派生的两个具体症状：

- `using Alias = Generic<T>;` 报 **`CS0307`**——using 别名不能是开放泛型。
- 想"接受任意 `MultiArray<T, S>`"只能再引入一个泛型参数，或抽一个非泛型接口。

## 三、`Math` 被命名空间遮蔽

C# 侧有 `Fuookami.Ospf.Math` 命名空间。在 `using Fuookami.Ospf.*` 的文件里写 `Math.Abs(x)`
会解析到**命名空间**而不是 `System.Math`，报 `CS0234` 或 `CS0118`。

一律写全名：

```csharp
global::System.Math.Abs(x);
global::System.Math.Max(a, b);
global::System.Math.Sqrt(x);
global::System.Math.PI;
```

## 四、`Result` 的用法约束

- `Result<T,C,E>` 是 **abstract record，只有 3 个类型参数**；没有 1 参数或 2 参数版本。
- 取值只能 `Ok<T,C,E>.Value`；**没有 `.Error` 属性**。要读错误信息必须先转成 `Failed`：

```csharp
Failed<TSuccess, ErrorCode, Error<ErrorCode>> failed =
    result.Should().BeOfType<Failed<TSuccess, ErrorCode, Error<ErrorCode>>>().Subject;
failed.Message.Should().Contain("...");
```

- **C# 不支持部分类型推断**：`Results.Failed<T>(...)` 里 `T` 必须写全，不能只给一半。

## 五、`Quantity` 的两个坑

1. **`ToDouble()` 是 `Quantity<FltX>` 上的扩展方法，不是 `FltX` 上的**。
   `q.Value.ToDouble()` 编译不过（`FltX` 的 `Value` 还是 `protected`）。正确写法是 `q.ToDouble()`。

2. **`Quantity.Add` 只加数值、保留左操作数单位**（`Framework.Bpp3d` 的 FltX 特化版更是直接 `new(lhs.Value.Plus(rhs.Value), lhs.Unit)`）。
   把 kg 和 m 相加**永远算得出一个数**。凡是跨来源求和的地方必须自己先比单位：

```csharp
if (!used.Unit.Equals(added.Unit)) { return Failed(...); }
```

本仓库因为这个坑栽过两次（B270 重量求和、B271 层深求和），两次的变异体都是"跳过单位检查"存活。

## 六、"空壳"是本项目最高频的失败模式

已经拆掉三个：`BinCapacityConstraint`、`BinDepthConstraint`（恒返回空右端项）、
`BinLoadingOrderConstraint`、`TailBinAssignmentConstraint`（无参、恒成功、无调用方）。

识别特征：

- **没有构造函数参数**，或参数从不被读取；
- 方法**恒返回同一个常量**（空字典、`Ok`）；
- 配置里有 `Enable*` 开关，但 `grep "\.Enable"` 只有定义处没有读取处；
- **测试把空壳行为写成了预期**（`Should().BeEmpty()`）。

第 4 条最危险：它让空壳看起来"有测试覆盖"。

**处置原则**：

- 缺口能补 → 补（B269/B270/B271/B273/B274）；
- 缺口**依赖不存在的模型** → **删掉空壳并记录缺口**（B272）。留着只会让"约束存在"与"约束什么都不做"无法区分。

## 七、变异测试的三条铁律

1. **替换必须命中**：`old_string` 不在文件里就是假阳性，必须报 `ANCHOR MISS` 而不是跳过。
2. **`finally` 里还原**，结束断言文件**字节一致**。
3. **编译不过的变异体作废重写**，不算通过。常见编译失败：
   - 变异后变量未被使用（`CS0165`/`CS0103`）→ 改成 `&& false` 让变量仍"被读"；
   - 空值流分析（`if (x is null && false)`）→ 换成别的等价写法。

**等价变异必须记为等价，不得声称击杀。** 例：B274 的"回写也套到长方体上"——
`radius` 的所有使用处都被 `isCylinder` 包住，长方体分支拿到什么值都不进输出。

## 八、夹具设计：让两个实现分得开

存活变异几乎总是"夹具里两种实现同解"。有效的补法：

| 症状 | 补法 |
| --- | --- |
| 求和退化成"只看第一件" | 两件 60 kg / 容量 100（单件不超、合起来超） |
| 求和退化成"只看最后一层" | 三层 5/5/5 对箱深 8（每层单独都不超） |
| 边界 `<` 写成 `<=` | 取**恰好装满**的样本 |
| 每箱复用第一箱的值 | 两个箱子的值必须**不同** |
| 键优先级取反 | 两个键指向**不同的值** |
| 缺键时编造数值 | 表里**有别的键**、但没有这一个（不是整张表为空） |
| 排序方向反了 | 一条断言**顺序**的测试 |

## 九、门禁流程

每批收口必须四件事齐全，缺一件不算完成：

```bash
# 1. 定向测试
dotnet test tests/<项目>.csproj -c Release --filter 'FullyQualifiedName~<类名>'

# 2. 变异测试（≥3 个，必须真的能证伪）

# 3. Release 全方案强制重建（必须 0 错误）
dotnet build ospf-csharp.sln -c Release --no-restore -t:Rebuild --nologo

# 4. 默认全方案测试
dotnet test ospf-csharp.sln -c Release --no-build --no-restore
```

**警告计数是"双行"的**：`: warning ` 的行数 = 分析器警告数的 2 倍。每批**零新增**。
新增的 `IDE0007`/`IDE0008`/`IDE0011`/`IDE0022`/`IDE0065`/`CS1573` 必须在同批修掉，不要留到"以后"。

## 十、不要做的事

- **不提交**（`git commit` 需要明确要求）。
- **不切换 Kotlin checkout**。基线一律 `git ls-tree -r 12fdb587 | grep <路径> | awk '{print $3}'` 取 blob sha，
  再 `git cat-file blob <sha>` 读内容。**不要**用 `git show 12fdb587:<路径>`——Windows 路径长度会失败。
- **不写未实测的跨语言主张**。实测过的只有三处：`SerializedSolution`、`RemoteTaskSubmitRequest`、
  `configuration`/`sha256` 摘要。基准夹具只证明"夹具被覆盖"，**不证明性能**。
- **不把"前置校验"说成"模型约束"**。B269/B270/B271 的容量与深度检查在**求解前**拒绝非法数据，
  基线是把线性约束加进 `MetaModel` 参与优化。两者在"非法数据被拒"上相同，**在优化结果上不同**。

## 十一、按文件名判缺口，会报出九十一个假缺口

**做法**：把基线 `ospf-kotlin-math/src/main` 的 **320** 个文件名，跟本仓 `src/**/*.cs` 的文件名做差。

**结果**：**91 个"缺失"**。

**逐个查完**：只有 **1 个**是真的。其余都活在别的文件里：

| 候选 | 实情 |
| --- | --- |
| `Einsum` | 在 `Math/MultiArray/Einsum/EinsumParser.cs` |
| `CombinatoricsAsync` | 在 `Math/Combinatorics/CombinatoricsAsyncScope.cs` |
| `EscapeTime` | 在 `Math/Fractal/FractalAnalysis.cs` |
| `Gamma` / `Beta` | 在 `Core/Solver/Heuristic/` 的算子实现里 |
| **`ExpressionTransform`** | **真的没有**（B961 补上） |

**误报率 98.9%。**

### 为什么必然如此

Kotlin 侧一个文件常常只装一个概念，C# 侧常把同族概念合并进一个文件（`EinsumParser` 里还带 parser 与求值），
也会把一个小功能塞进一个不相干的类（`Gamma`/`Beta` 混在启发式算子里）。**两边的文件边界不是同一个东西。**

**所以"名字对不上"不是证据，只是线索。** 拿它当缺口清单，等于**用文件系统的排列方式冒充能力清单**。

### 该怎么用

| 想要 | 别这么做 | 该这么做 |
| --- | --- | --- |
| 判一个能力在不在 | 文件名不在 ⇒ 缺 | **按类型名/概念名 grep 整个 `src/`**，找到了就再读一遍确认它在做这件事 |
| 估还剩多少工作 | 数文件名之差 | **数"确认缺失"的那几个**（这次是 1 个） |
| 写进文档 | "共 91 处缺失" | **写"91 个候选，逐条核实后 N 个为真"**，并列出核实过的 |

**这一条与 §一「命名对照的四个陷阱」是同一件事的两面**：那节说名字会骗人，这节给出了它骗人的**比例**。

**B942 当年"觉得逐文件比对不可靠"就放弃了**——放弃是对的，但**没留下数字**，于是后来又有人想试。
B961 把它量了出来：**98.9%**。判据在 `NameBasedGapMethodTests`，钉住那五个误报与这个比例。

**Method**: difference the baseline's **320** math file names against this repo's. **Result**: **91 "missing"**. **After checking each**: only **one** was real; the rest live inside
differently-named files, for a **98.9% false-positive rate**. Kotlin tends to give each concept its own file while C# merges a family into one (`EinsumParser` carries the
parser too) or tucks a small feature into an unrelated class (`Gamma`/`Beta` inside heuristic operators) — **the two sides do not share a file boundary**, so **a name mismatch
is a lead, not evidence**, and using it as a gap list means **passing off the file system's arrangement as the capability list**. B942 abandoned per-file comparison because it
"felt unreliable" — right to abandon, but **it left no number behind**, so people tried again. B961 measured it: **98.9%**. `NameBasedGapMethodTests` pins the five false positives
and that rate.