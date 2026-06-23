# 项目规范

## 1. 编码风格

### 1.1 注释语言
编写注释时，要中英双语。

### 1.2 版权声明
不需要添加版权声明。

### 1.3 ReadMe 文件
英文 ReadMe：README.md，中文 ReadMe：README_ch.md，要添加超链接能互相跳转。

### 1.4 Shell 工具
PowerShell 用：pwsh.exe。

### 1.5 函数调用命名参数规范
超过 2 个参数时，使用多行和命名参数。

### 1.6 C# 文件排版与 using 规范

本节约束 C# 源文件的基础排版风格。重构时应优先保持本项目既有手写风格，不使用会大幅改写 using、空行和换行的自动格式化结果作为最终形态。

#### 1.6.1 文件整体结构

文件各部分的排列顺序和间距必须遵循以下规范：

1. **文件级指令**（如 `#nullable enable`）必须放在文件最开头。
2. `using` 声明放在命名空间之前（或文件顶部），与命名空间之间不空行。
3. 命名空间与首个类型声明之间必须有且仅有一个空行。
4. 文件末尾必须有且仅有一个换行符（即最后一个 `}` 后有一个空行）。

#### 1.6.2 using 排列

**整体顺序**：

1. 所有 `using` 连续排列，中间不按来源分组插空行。
2. using 来源层级按以下顺序排列（被依赖方排在前面）：
   - System / Microsoft 标准库
   - 第三方库（如 `Newtonsoft.Json`、`Serilog` 等）
   - `Fuookami.Ospf.*`（按模块依赖深度升序）
3. `Fuookami.Ospf.*` 内部按模块依赖层级排列，从底层到上层：
   - `Fuookami.Ospf.Utils.*`（基础工具，被所有模块依赖）
   - `Fuookami.Ospf.Math.*`（数学库）
   - `Fuookami.Ospf.Core.*`（优化核心）
   - `Fuookami.Ospf.Framework.*`（应用框架）
4. 同一模块层级内，按命名空间字典序升序排列。
5. 禁止 using 末尾使用分号以外的字符。
6. 允许使用 `using static` 导入静态成员，排在普通 using 之后。

**正确示例 1**（以 Core 模块中的文件为例）：

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Core.Model.Mechanism;

namespace Fuookami.Ospf.Core.Model.Mechanism
{
    // ...
}
```

**错误示例**：

```csharp
using Fuookami.Ospf.Core.Variable;  // Core 应排在 Utils 之后
using System;                         // System 应排在最前面

using Fuookami.Ospf.Utils.Functional; // 空行分隔 using
```

#### 1.6.3 换行、缩进与空行

**基础规则**：

- 使用 4 空格缩进，不使用 tab。
- 使用尾逗号（trailing comma）以减少 diff 噪音。
- 顶层声明之间保留一行空行。
- 方法之间保留一行空行。
- 类/结构体结束前不保留多余空行。
- 禁止保留重复 XML 文档注释或连续重复注释块。

**方法声明与调用**：

- 1-2 个参数且语义简单时可单行书写。
- 超过 2 个参数时，按 `1.5 函数调用命名参数规范` 使用多行和命名参数。
- 多行参数列表中，参数缩进一层；闭合括号与调用起始位置对齐。

```csharp
var material = Material.From(
    id: id,
    code: code,
    name: name,
    status: MaterialStatus.Active
);
```

**返回与表达式体**：

- 非平凡方法使用块体和显式 `return`。
- 简单派生属性、简单单行方法可使用表达式体。

```csharp
public bool IsActive => Status == UserStatus.Active;

public Result<Material> Create(MaterialCreateInput input)
{
    return input.Validate().Map(validated =>
        new Material(
            id: NextId(),
            spec: validated
        )
    );
}
```

#### 1.6.4 注释与分段

- 公共类、接口、重要公共方法使用中文 XML 文档注释。
- 简短属性可使用单行注释，如 `/// <summary>是否已加载用户组引用</summary>`。
- 服务和仓储内部允许使用 `// ==================== 查询 ====================` 形式分段。
- 注释应说明业务意图、加载态语义或分段边界；避免重复描述代码本身。

#### 1.6.5 XML 文档注释标签与覆盖要求

**标签规范**：

- 公共类 / 接口必须使用 `<summary>` 标注。
- 公共方法必须使用 `<param>` 标注每个参数，使用 `<returns>` 标注返回值（返回 `void` 或语义自明时可省略 `<returns>`）。
- 泛型类型参数语义非常规时使用 `<typeparam>` 说明。
- 单行注释（仅一句话、无标签需求）仍然允许，不必强加空标签。

**覆盖要求**：

- 每个重载（overload）都必须有独立的 XML 文档注释，不得仅为一组重载的第一个添加注释。

### 1.7 泛型化命名规范

对外 API 使用业务自然名表达稳定抽象，不使用迁移期技术命名。

- 泛型化后的主接口、主模型、主服务占用自然名。
- 不使用 `V`、`Typed`、`Generic` 作为迁移痕迹型前后缀。
- 需要保留的 `Flt64` 专用接口、桥接接口或兼容入口，使用 `Flt64` 后缀显式标识。
- 内部变量、测试名、文档示例应尽量同步上述命名，避免保留迁移期表达。

### 1.8 Nullable Reference Types

项目启用 nullable reference types，需遵循：

- 所有公共 API 必须显式标注可空性。
- 使用 `?` 标记可空引用类型，而非依赖 `!` 抑制警告。
- 对于可能为 null 的参数，使用 `ArgumentNullException` 或 Guard 模式验证。
- 避免使用 `null!` 强制非空断言，除非有充分注释说明原因。
