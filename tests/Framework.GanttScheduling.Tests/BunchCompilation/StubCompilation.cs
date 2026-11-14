// 测试用占位编译 / A placeholder compilation for tests.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;

/// <summary>
/// 占位编译结果 / A placeholder compilation.
/// </summary>
public sealed class StubCompilation : ICompilation {
    /// <inheritdoc/>
    public bool TaskCancelEnabled => false;

    /// <inheritdoc/>
    public bool WithExecutorLeisure => false;

    /// <inheritdoc/>
    public Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);

}

/// <summary>
/// 带执行器编译表的占位编译 / A placeholder compilation carrying an executor table.
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type.</typeparam>
public sealed class StubCompilation<E> : ICompilation, IExecutorCompilation<E> where E : Executor {
    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="executors">填进表的执行器 / The executors placed in the table.</param>
    public StubCompilation(IReadOnlyList<E> executors) {
        var table = new Dictionary<E, object>();
        foreach (E executor in executors) {
            table[executor] = new object();
        }
        ExecutorCompilation = table;
    }

    /// <inheritdoc/>
    public bool TaskCancelEnabled => false;

    /// <inheritdoc/>
    public bool WithExecutorLeisure => false;

    /// <inheritdoc/>
    public IReadOnlyDictionary<E, object> ExecutorCompilation { get; }

    /// <inheritdoc/>
    public Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);
}

/// <summary>
/// 每次读取都返回不同表的占位编译 / A placeholder compilation whose table changes between reads.
/// </summary>
/// <typeparam name="T">任务类型 / Task type.</typeparam>
/// <remarks>
/// 迭代编译的任务在列生成过程中持续增长。这个替身模拟那件事：第一次读到全表，之后只读到更短的表。
/// 若约束把表缓存下来，第二次仍然会看到第一次那张表，于是**追加进去的任务查不到**。
///
/// An iterative compilation keeps appending tasks during column generation. This double mimics that: the first read sees the full table, later reads see a shorter one. If the
/// constraint caches the table, the second call still sees the first one, so **appended tasks cannot be found**.
/// </remarks>
public sealed class ChangingCompilation<T> : ICompilation, ITaskCompilation<TestExecutor, TestAssignmentPolicy>
    where T : IAbstractTask<TestExecutor, TestAssignmentPolicy> {
    private readonly IReadOnlyList<T> _visible;

    /// <summary>读取次数 / The number of reads.</summary>
    public int Reads { get; private set; }

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="visible">第一次之后仍可见的任务 / The tasks still visible after the first read.</param>
    public ChangingCompilation(IReadOnlyList<T> visible) {
        _visible = visible;
    }

    /// <inheritdoc/>
    public bool TaskCancelEnabled => false;

    /// <inheritdoc/>
    public bool WithExecutorLeisure => false;

    /// <inheritdoc/>
    public IReadOnlyDictionary<IAbstractTask<TestExecutor, TestAssignmentPolicy>, object> TaskCompilationTable {
        get {
            Reads++;
            var table = new Dictionary<IAbstractTask<TestExecutor, TestAssignmentPolicy>, object>();
            IReadOnlyList<T> source = Reads == 1 ? _visible : Array.Empty<T>();
            foreach (T task in source) {
                table[task] = new object();
            }
            return table;
        }
    }

    /// <inheritdoc/>
    public Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);
}

/// <summary>
/// 带任务编译表的占位编译 / A placeholder compilation carrying a task table.
/// </summary>
/// <typeparam name="T">任务类型 / Task type.</typeparam>
public sealed class StubCompilationTaskTable<T>
    : ICompilation, ITaskCompilation<TestExecutor, TestAssignmentPolicy>
    where T : IAbstractTask<TestExecutor, TestAssignmentPolicy> {
    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="tasks">填进表的任务 / The tasks placed in the table.</param>
    public StubCompilationTaskTable(IReadOnlyList<T> tasks) {
        var table = new Dictionary<IAbstractTask<TestExecutor, TestAssignmentPolicy>, object>();
        foreach (T task in tasks) {
            table[task] = new object();
        }
        TaskCompilationTable = table;
    }

    /// <summary>
    /// 约束可以施加的模型 / A model the constraint can be applied to.
    /// </summary>
    public Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Flt64> Model { get; } = new();

    /// <inheritdoc/>
    public bool TaskCancelEnabled => false;

    /// <inheritdoc/>
    public bool WithExecutorLeisure => false;

    /// <inheritdoc/>
    public IReadOnlyDictionary<IAbstractTask<TestExecutor, TestAssignmentPolicy>, object> TaskCompilationTable { get; }

    /// <inheritdoc/>
    public Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
