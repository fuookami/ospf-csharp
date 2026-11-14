// 测试用迭代任务 / An iterative task double for tests.

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;

/// <summary>
/// 迭代任务替身 / An iterative task double.
/// </summary>
/// <remarks>
/// 迭代任务比普通任务只多一个 `Iteration`。这里包一层而不是给 `TestTask` 加接口：
/// 普通任务的判据不该被迫接受一个它们根本用不到的迭代字段。
///
/// An iterative task only adds an `Iteration` over a plain one. This wraps rather than adding the interface to `TestTask`: criteria for plain tasks should not be forced to carry
/// an iteration field they never use.
/// </remarks>
public class TestIterativeTask : IIterativeAbstractTask<TestExecutor, TestAssignmentPolicy> {
    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="inner">被包装的任务 / The wrapped task.</param>
    /// <param name="iteration">迭代次数 / The iteration number.</param>
    public TestIterativeTask(TestTask inner, long iteration = 0) {
        Inner = inner;
        Iteration = iteration;
    }

    /// <summary>被包装的任务 / The wrapped task.</summary>
    public TestTask Inner { get; }

    /// <inheritdoc/>
    public long Iteration { get; }

    /// <inheritdoc/>
    public string Id => Inner.Id;

    /// <inheritdoc/>
    public string Name => Inner.Name;

    /// <inheritdoc/>
    public TestAssignmentPolicy? AssignmentPolicy => Inner.AssignmentPolicy;

    /// <inheritdoc/>
    public TimeRange? Time => Inner.Time;

    /// <inheritdoc/>
    public TestExecutor? Executor => Inner.Executor;

    /// <inheritdoc/>
    public int Index => Inner.Index;

    /// <inheritdoc/>
    public void SetIndexed() => Inner.SetIndexed();

    /// <inheritdoc/>
    public bool? PartialEq(IAbstractTask<TestExecutor, TestAssignmentPolicy> rhs) => Inner.PartialEq(rhs);
}
