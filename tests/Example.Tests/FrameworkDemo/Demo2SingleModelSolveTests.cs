#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **单模型 + 框架算法这条链真的跑到求解器** / The "single model plus framework algorithm" chain really reaches the solver.
/// </summary>
/// <remarks>
/// 证明单模型建得起来、 证明行被切开，但**"谁把它交给解算器"一直没接过**。这里接上，并且**还没接进分派**：
/// `Execute` 仍走两模型那条路，等这条链的结果与旧路对齐后再翻开关。
///
/// showed the single model builds and showed rows are split, but **nothing ever handed it to a solver**. connects that, and **it is not dispatched
/// yet**: `Execute` still takes the two-model route, and the switch flips once this chain's results line up with the old one.
/// </remarks>
public sealed class Demo2SingleModelSolveTests
{
    /// <summary>跑到求解器，且失败要说明白 / Reaches the solver, and explains the failure.</summary>
    [Fact]
    public async Task TheSingleModelChain_ShouldReachTheSolver() {
        var application = new FullLoadApplication();
        application.Init(AircraftLoadingDemo.CreateSampleRequest()).IsOk.Should().BeTrue(
            "the domains must initialise before any registration can happen");

        var stub = new RecordingBendersSolver();
        List<string> notes = [];

        ResponseDTO response = await application.SolveSingleModelBendersAsync(
            StowageMode.FullLoad, stub, milpSolver: null, notes);

        // **实测**：这条链把模型交给桩（`MasterCalls > 0`），并由桩的失败收场；备注里要能看见切分。
        // **Measured **: the chain hands the model to the stub (`MasterCalls > 0`) and ends in the stub's failure; the split must be visible in the notes.
        stub.MasterCalls.Should().BeGreaterThan(0, "the point of is that the model really reaches the solver");
        notes.Should().Contain(note => note.StartsWith("benders_split=", System.StringComparison.Ordinal),
            "the split must be reported; got: " + string.Join("|", notes));
        response.Succeed.Should().BeFalse("the stub solves nothing, so the run cannot succeed");
        // **失败时一定不带分配**：默认门禁能守的那一半守卫就是这一条，成功那一半在真后端门内。
        // **A failure never carries assignments**: this is the half of the guard the default gate can hold; the success half lives in the real-backend gate.
        response.Assignments.Should().BeNull("a failing run must never come with invented assignments");
        notes.Should().Contain(note => note.Contains("no MILP solver was supplied", System.StringComparison.Ordinal),
            "the failure must name which fallback reason applies");
    }

    /// <summary>脚本求解器在框架算法上到底报什么 / What the scripted solver reports on the framework algorithm.</summary>
    [Fact]
    public async Task TheScriptedSolverOnTheFrameworkAlgorithm_ShouldSayWhyItFails() {
        var application = new FullLoadApplication();
        application.Init(AircraftLoadingDemo.CreateSampleRequest()).IsOk.Should().BeTrue();

        var scripted = new ScriptedBendersSolver([(100.0, 100.0), (100.0, 100.0)], solutionLength: 3);
        List<string> notes = [];
        ResponseDTO response = await application.SolveSingleModelBendersAsync(
            StowageMode.FullLoad, scripted, milpSolver: null, notes);

        // **实测**：`master=1`、**`sub=0`**——框架算法在主问题解完那一刻就停了，**从没调用子问题**。
        // 原因在契约里：脚本求解器返回的是**按位置排的解向量**（长度 3），而单模型的主问题有 12 个 token；
        // 算法要按变量身份取出 theta 的取值，取不到就失败。**这不是"脚本写错了"，是两套契约的形状不同**。
        //
        // **Measured **: `master=1`, **`sub=0`** — the framework algorithm stops right after the master solve and **never calls the sub**. The contract explains it:
        // the scripted solver returns a **positional** solution vector (length 3) while the single master has twelve tokens, and the algorithm needs the theta value by variable
        // identity. **This is not "the script is wrong": the two contracts have different shapes.**
        scripted.MasterCalls.Should().Be(1);
        // **实测**：改成按 token 数产解之后，算法**过了主问题、真的调用了子问题**（`sub=1`），但仍然失败。
        // 也就是说 的阻塞点（主问题解向量对不上）已经解除，**下一个阻塞点在子问题结果的形状上**（脚本返回 `Cuts: null`、无对偶）。
        //
        // **Measured **: once the vector follows the token count, the algorithm **gets past the master and really calls the sub** (`sub=1`) — the blocker is gone —
        // and it still fails. **The next blocker is the shape of the sub result** (the script returns `Cuts: null` with no duals).
        scripted.MasterCalls.Should().Be(1);
        scripted.SubCalls.Should().Be(1);
        // 把失败点定位到了**：不是割、不是对偶，是"可行子问题缺少统一报告"——
        // `LinearFeasibleResult.Report` 留给旧适配器为空，而空报告不能用来证明最优。
        //
        // **located the failure**: not cuts, not duals — "feasible subproblem is missing its unified report". `LinearFeasibleResult.Report` is optional for legacy
        // adapters, and a null report cannot prove optimality.
        // **修掉了"违反变量范围"**（按变量下界取值），下一个阻塞是**违反约束**：
        // "合并解违反约束 / Merged solution violates constraint"。
        // 也就是说：**编造一个合法点比编造一个合法值难得多**——阈值上的数看起来都对，约束却不一定满足。
        // 这条不打算靠"再调一调脚本"糊过去，后续改用**真求解器**（SCIP 集成开关）来跑收敛。
        //
        // **fixed the bounds violation** (values now follow each variable's lower bound) and the next blocker is **constraint feasibility**:
        // "merged solution violates constraint". **Inventing a feasible point is much harder than inventing legal values.** This will not be papered over by "tuning the
        // script once more": the next step uses a **real solver** (the SCIP integration switch) for the convergence run.
        notes.Should().Contain(note => note.Contains("合并解违反约束", System.StringComparison.Ordinal),
            "the note must carry the algorithm's own reason, so the next reader never guesses");
        response.Succeed.Should().BeFalse();
        response.Succeed.Should().BeFalse();
        notes.Should().Contain(note => note.Contains("no MILP solver was supplied", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// **成功分支不带分配**（源码层）：默认门禁守不住行为，就守住代码。
    /// **The success branch carries no assignments** (source level): where the default gate cannot hold the behaviour, it holds the code.
    /// </summary>
    /// <remarks>
    /// 行为版本在真后端门内（`Demo2BendersScipTests`，断言 `Optimal` 时分配为 `null`）。这一条是**默认门禁的替身**：
    /// 成功分支必须写 `assignments: null` 并且留下 `not_decoded` 的备注——**少任何一样，行为版本就有机会被悄悄改坏而默认门禁不知道**。
    ///
    /// The behavioural version lives in the real-backend gate. This one is **the default gate's stand-in**: the success branch must pass `assignments: null` and
    /// leave the `not_decoded` note — **without both, the behaviour could be broken silently while the default gate stays green**.
    /// </remarks>
    [Fact]
    public void TheSuccessBranch_ShouldDecodeByNameAndNeverSilently() {
        string code = Source();
        // **之后这条判据守的东西变了**：分配真的解码了，要守的是**"解码不许沉默地返回空列表"**——
        // 名字对不上、值不二元，都必须**说一句**（`not_decoded:…`），而不是给一个看起来像"什么都没装"的空列表。
        //
        // **What this guards changed**: assignments are actually decoded now, so the thing to guard is **"decoding must never silently return an empty list"** —
        // an unmatched name and a non-binary value must each **say so** (`not_decoded:…`) instead of handing back an empty list that looks like "nothing was loaded".
        // **之后解码器只吃"名字 → 值"**：这条判据同时守住"成功分支必须经过它"和"它不吃变量对象"。
        // **Since the decoder takes "name to value" only**: this pins both "the success branch goes through it" and "it does not take variable objects".
        code.Should().Contain("DecodeAssignments(named)", "the success branch must go through the named decoder, never a positional read");
        code.Should().Contain("named[variable.Name] = value.ToDouble()", "the projection to names happens before decoding, which is what makes it testable");
        code.Should().Contain("not_decoded:non_binary:", "a non-binary value must be refused by name");
        code.Should().Contain("not_decoded:no_assignment_variables", "finding no assignment variables at all must be said out loud");
    }

    /// <summary>读 `FullLoadApplication.cs`（剥掉注释行）/ Read the source with comment lines stripped.</summary>
    /// <returns>代码 / The code.</returns>
    private static string Source() {
        var directory = new global::System.IO.DirectoryInfo(global::System.AppContext.BaseDirectory);
        while (directory is not null) {
            string candidate = global::System.IO.Path.Combine(
                directory.FullName, "src", "Example", "FrameworkDemo", "Demo2", "Application", "FullLoadApplication.cs");
            if (global::System.IO.File.Exists(candidate)) {
                return string.Join(System.Environment.NewLine, global::System.IO.File.ReadAllLines(candidate)
                    .Where(line => !line.TrimStart().StartsWith("//", global::System.StringComparison.Ordinal)));
            }
            directory = directory.Parent;
        }
        throw new global::System.IO.FileNotFoundException("could not locate FullLoadApplication.cs");
    }
    /// <summary>**解码器的四种结局，默认门禁里直接测** / The decoder's four outcomes, tested in the default gate.</summary>
    /// <remarks>
    /// 证明整条链能解出分配，但那条判据在真后端门内（要 SCIP）。这里把**解码器**本身做成纯函数（`名字 → 值`，
    /// `internal` 可见），于是它的四种结局在**默认门禁**里就能测——不需要求解器、不需要构造变量对象。
    ///
 /// proved the chain decodes, but that criterion needs SCIP. made **the decoder itself** a pure function (`name to value`, `internal`), so its four
    /// outcomes are testable **in the default gate** — no solver, no variable objects.
    ///
    /// **沉默地给一个空列表最坏**：它和"什么都没装"无法区分，所以每种异常结局都必须有名字。
    /// **An empty list returned silently is the worst outcome**: indistinguishable from "nothing was loaded", hence every abnormal outcome must be named.
    /// </remarks>
    [Fact]
    public void TheDecoder_ShouldNameEachOutcome() {
        static Dictionary<string, double> Values(params (string Name, double Value)[] pairs) {
            var values = new Dictionary<string, double>(System.StringComparer.Ordinal);
            foreach ((string name, double value) in pairs) {
                values[name] = value;
            }
            return values;
        }

        (IReadOnlyList<string>? decoded, string how) = FullLoadApplication.DecodeAssignments(
            Values(("assignment_C1_P1", 1.0), ("assignment_C2_P2", 1.0), ("occupied_P1", 1.0)));
        decoded.Should().BeEquivalentTo(new[] { "C1->P1", "C2->P2" });
        how.Should().Be("decoded:2");

        (IReadOnlyList<string>? empty, string emptyHow) = FullLoadApplication.DecodeAssignments(
            Values(("assignment_C1_P1", 0.0), ("assignment_C2_P2", 0.0)));
        empty.Should().BeNull("an empty plan must not be handed over as if it were a result");
        emptyHow.Should().Be("not_decoded:empty_master_assignment");

        (IReadOnlyList<string>? fractional, string fractionalHow) = FullLoadApplication.DecodeAssignments(
            Values(("assignment_C1_P1", 0.5)));
        fractional.Should().BeNull("0.5 is the solver saying \"this is not an integer solution\"");
        fractionalHow.Should().Be("not_decoded:non_binary:assignment_C1_P1");

        (IReadOnlyList<string>? none, string noneHow) = FullLoadApplication.DecodeAssignments(
            Values(("occupied_P1", 1.0)));
        none.Should().BeNull();
        noneHow.Should().Be("not_decoded:no_assignment_variables");
    }

}
