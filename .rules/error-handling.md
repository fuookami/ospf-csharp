# 错误处理规范

## 1. 核心原则

**整个项目统一使用返回错误（Result 模式），不抛出异常。**

所有可能失败的操作都应返回 `Result<T, C, E>` 或其变体（`ExResult`、`Try`、`Ret<T>` 等），而不是抛出异常。

## 2. 错误类型体系

### 2.1 基础类型（Fuookami.Ospf.Utils）

- `ErrorCode` - 错误码枚举，定义所有标准错误码
- `Error<C>` - 错误基类（abstract record）
  - `Err<C>` - 基本错误，包含 Code 和 Message
  - `LazyErr<C>` - 惰性消息错误，延迟消息构造
  - `ExErr<C, T>` - 带关联值的错误
  - `LazyExErr<C, T>` - 惰性带关联值错误
- `IError<C>` - 错误接口

### 2.2 结果类型（Fuookami.Ospf.Utils）

- `Result<T, C, E>` - 基础结果类型（abstract record）
  - `Ok<T, C, E>` - 成功结果，包含值
  - `Failed<T, C, E>` - 失败结果，包含单个错误
  - `Fatal<T, C, E>` - 致命结果，包含多个错误

- `ExResult<T, C, E>` - 扩展结果类型（abstract record）
  - `Ok<T, C, E>` - 成功结果
  - `Failed<T, C, E>` - 失败结果
  - `Fatal<T, C, E>` - 致命结果
  - `Warn<T, C, E>` - 警告结果，同时包含值和警告

### 2.3 类型别名

- `Try` - 无返回值的结果：`Result<Success, ErrorCode, Error<ErrorCode>>`
- `TryOf<C>` - 自定义错误码的无返回值结果：`Result<Success, C, Error<C>>`
- `TryWith<E>` - 自定义错误类型的无返回值结果：`Result<Success, ErrorCode, E>`
- `Ret<T>` - 带返回值的结果：`Result<T, ErrorCode, Error<ErrorCode>>`
- `RetOf<T, C>` - 自定义错误码的带返回值结果：`Result<T, C, Error<C>>`
- `ExTry` - 无返回值的扩展结果：`ExResult<Success, ErrorCode, Error<ErrorCode>>`
- `ExTryWithCode<C>` - 自定义错误码的无返回值扩展结果：`ExResult<Success, C, Error<C>>`
- `ExTryWith<E>` - 自定义错误类型的无返回值扩展结果：`ExResult<Success, ErrorCode, E>`
- `ExRet<T>` - 带返回值的扩展结果：`ExResult<T, ErrorCode, Error<ErrorCode>>`
- `ExRetWithCode<T, C>` - 自定义错误码的带返回值扩展结果：`ExResult<T, C, Error<C>>`

## 3. 使用规范

### 3.1 函数签名

```csharp
// 正确：返回 Result
public Ret<ParsedData> Parse(string input)
{
    return IsValid(input)
        ? new Ok<ParsedData, ErrorCode, Error<ErrorCode>>(ParseData(input))
        : new Failed<ParsedData, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, "Invalid input format");
}

// 正确：返回 Try（无有意义返回值）
public Try Save(Data data)
{
    return _repository.Save(data)
        ? Result.Ok
        : new Failed<Success, ErrorCode, Error<ErrorCode>>(ErrorCode.ApplicationFailed, "Save failed");
}

// 错误：抛出异常
public ParsedData Parse(string input)
{
    if (!IsValid(input))
    {
        throw new ArgumentException("Invalid input format");
    }
    return ParseData(input);
}
```

### 3.2 错误传播

使用 `Run`、`ExRun` 等方法顺序执行多个可能失败的操作：

```csharp
public Ret<Output> Process(string input)
{
    return Result.Run(
        () => Validate(input),
        () => Transform(input),
        lastBlock: output => Save(output)
    );
}
```

### 3.3 错误映射

使用 `Map` 转换成功值，使用 `IfFailed` 处理失败：

```csharp
var result = Parse(input)
    .Map(data => data.ToString())
    .IfFailed(error => Logger.LogError("Parse failed: {Message}", error.Message));
```

### 3.4 工厂方法

使用提供的工厂方法创建结果：

```csharp
// 成功
new Ok<T, C, E>(value)
Result.Ok  // Try 的成功实例
Result.Ok(value)  // Ret<T> 的成功实例

// 失败
new Failed<T, C, E>(ErrorCode.IllegalArgument, "message")
new Failed<T, C, E>(ErrorCode.IllegalArgument, "message", additionalValue)

// 致命
new Fatal<T, C, E>(ErrorCode.ApplicationError, "fatal message")
new Fatal<T, C, E>(new[] { error1, error2 })

// 警告
new Warn<T, C, E>(value, ErrorCode.Other, "warning message")
new Warn<T, C, E>(value, ErrorCode.Other, "warning message", warningValue)
```

## 4. 禁止的模式

### 4.1 禁止抛出异常

```csharp
// 禁止
throw new ArgumentException("...");
throw new NotSupportedException("...");
throw new InvalidOperationException("...");
throw new ApplicationException("...");
```

### 4.2 禁止使用 ApplicationException

`ApplicationException` 存在是为了与外部库交互的兼容性，不应在业务代码中使用。

### 4.3 禁止在 Result 处理中抛异常

```csharp
// 禁止
switch (result)
{
    case Failed<Success, ErrorCode, Error<ErrorCode>> failed:
        throw new InvalidOperationException(failed.Error.Message);
    // ...
}
```

## 5. 允许的例外情况

### 5.1 测试代码

测试代码中可以使用异常来：
- 模拟失败场景
- 断言预期行为
- 测试 stub 实现

```csharp
// 测试中允许
public override Type Method() => throw new NotSupportedException("stub");
```

### 5.2 外部库交互

与不支持 Result 模式的外部库交互时，可以在边界处捕获异常并转换为 Result：

```csharp
public Ret<Response> ExternalCall()
{
    try
    {
        return new Ok<Response, ErrorCode, Error<ErrorCode>>(_externalLibrary.DoSomething());
    }
    catch (ExternalException e)
    {
        return new Failed<Response, ErrorCode, Error<ErrorCode>>(ErrorCode.Other, $"External call failed: {e.Message}");
    }
}
```

### 5.3 协议边界不变量

以下场景因协议或不变量约束而保留 `throw`/`ArgumentException`，不属于迁移范围：

- **序列化协议**：`JsonConverter.ReadJson` 返回类型为 `object`，框架不允许返回 `Result<T, C, E>`，反序列化失败必须抛 `JsonException`。
- **迭代器协议**：`IEnumerator.MoveNext()` 在 `Current` 访问时必须在无效状态下抛 `InvalidOperationException`，这是 .NET 标准库契约。
- **值对象内部不变量**：`UInteger`/`Integer`/`Rational` 的倒数、零分母等在内部工厂返回 `Result` 后的不可达路径中保留 `throw`，作为防御性断言。
- **已编译闭包运行时校验**：求值闭包中参数数量等校验在已编译求值闭包中运行，属于调用方契约违反的快速失败，不返回 `Ret`。

## 6. ErrorCode 扩展

当现有 ErrorCode 不足以表达错误类型时：

1. 首先检查是否可以复用现有 ErrorCode
2. 如果需要新的领域特定错误码，在相应模块定义扩展枚举
3. 确保错误码值不与现有 ErrorCode 冲突

## 7. 错误消息规范

- 使用简洁明了的中英双语消息
- 包含足够的上下文信息（如参数值、状态）
- 避免暴露内部实现细节
- 格式：`"操作失败：原因 / Operation failed: reason"`

## 8. C# 特有注意事项

### 8.1 三泛型参数设计

`Result<T, C, E>` 使用三个泛型参数，与 Kotlin 版本保持一致：
- `T` - 成功值的类型
- `C` - 错误码的类型（必须为非空类型，通常为 `ErrorCode`）
- `E` - 错误的类型（必须继承自 `Error<C>`）

```csharp
// 完整泛型参数
Result<ParsedData, ErrorCode, Error<ErrorCode>>

// 使用类型别名简化
Ret<ParsedData>

// 自定义错误码类型
Result<ParsedData, CustomErrorCode, Error<CustomErrorCode>>
RetOf<ParsedData, CustomErrorCode>
```

C# 不支持 Kotlin 的 `out` 协变泛型修饰符用于 class 类型参数，但 record 类型天然不可变，
因此在需要协变的场景（如 LINQ、Map）中，通过隐式转换或工厂方法实现类型安全转换。

### 8.2 Pattern Matching

使用 C# 的 pattern matching 与 Result 模式配合：

```csharp
return result switch
{
    Ok<T, C, E> ok => ProcessValue(ok.Value),
    Failed<T, C, E> failed => HandleError(failed.Error),
    Fatal<T, C, E> fatal => HandleFatal(fatal.Errors),
    _ => throw new UnreachableException()
};
```

### 8.3 LINQ 集成

实现 LINQ 扩展方法以支持查询语法：

```csharp
var query = from data in Parse(input)
            from transformed in Transform(data)
            from saved in Save(transformed)
            select saved;
```

### 8.4 Async 支持

提供异步版本的扩展方法：

```csharp
public async Task<Result<T, C, E>> BindAsync<T, C, E>(
    this Result<T, C, E> result,
    Func<T, Task<Result<T, C, E>>> func)
    where C : notnull
    where E : Error<C>
{
    return result switch
    {
        Ok<T, C, E> ok => await func(ok.Value),
        _ => result
    };
}
```
