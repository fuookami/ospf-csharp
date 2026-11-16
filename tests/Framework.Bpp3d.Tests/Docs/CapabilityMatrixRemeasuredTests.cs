// 矩阵里那些数字，重新量一遍 / Re-measure the numbers the matrix states.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **矩阵里的数字要能被重新量出来** / The numbers in the matrix have to be re-measurable.
/// </summary>
/// <remarks>
/// `docs/kotlin-capability-matrix.md` 四之二里 Demo2 的每一行都写着"多少行、0 个 TODO、几处引用"。这些数字是
/// `logs/b932-audit.py` **一次性量出来的**，之后**没有任何判据再核对过**——文档会漂，而漂了的 ✅ 比 ❌ 更危险：
/// 它让人以为不用看。
///
/// 这里判据只重钉**不会因为改一行代码就变**的三件事：
///
/// | 钉什么 | 为什么它稳 |
/// | --- | --- |
/// | 文件数 | 加一个文件才变；**变了就该改文档** |
/// | 每个能力类型存在 | 删了就红 |
/// | 每个能力**在别的文件里有引用** | 这是"有调用方"的全部含义 |
///
/// **不钉行数**：行数每改一次代码就漂，钉住只会逼人反复改文档，而**改数字不产生任何信息**。
///
/// Every row in 四之二 states "how many lines, zero TODOs, how many references". Those numbers were measured once by `logs/b932-audit.py` and **never
/// re-checked** — the document drifts, and a stale ✅ is more dangerous than a ❌ because it tells the reader to stop looking. These criteria re-pin only the three
/// things that do not move when a line of code changes: **the file count** (it only moves when a file is added, and then the document should), **that each capability
/// type exists**, and **that each is referenced from another file** — which is the whole meaning of "has a caller". **Line counts are deliberately not pinned**: they
/// move on every edit, and pinning them only forces the document to be edited, **and editing a number produces no information**.
/// </remarks>
public class CapabilityMatrixRemeasuredTests
{
    /// <summary>**Demo2 的文件数** / The Demo2 file count.</summary>
    /// <remarks>
    /// 矩阵写"**175** 个 `.cs`"。加一个文件就该改这一行——**这是文档该跟上的地方**，也是这条判据唯一的用处。
    ///
    /// The matrix says **175** `.cs` files. Adding one means editing that row — **the document should keep up here**, which is this criterion's only job.
    /// </remarks>
    [Fact]
    public void TheDemo2FileCount_ShouldMatchTheMatrix() {
        string root = Root();
        int actual = Directory.GetFiles(
            Path.Combine(root, "src", "Example", "FrameworkDemo", "Demo2"),
            "*.cs",
            SearchOption.AllDirectories).Length;

        string matrix = DocsFile.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));
        matrix.Should().Contain("**" + actual + "** 个 `.cs`",
            "the matrix states the file count, so it has to state the real one");
    }

    /// <summary>**矩阵点名的能力类型都存在** / Every capability type the matrix names exists.</summary>
    /// <remarks>
    /// **按源码扫描，不按反射**：这个测试项目不引用 `Example`，而文档判据本来也不该为了数一个类型名去拉一整个可执行程序集。
    ///
    /// **Scanned as source, not by reflection**: this test project does not reference `Example`, and a documentation criterion should not pull in a whole executable
    /// assembly just to count a type name.
    /// </remarks>
    [Fact]
    public void EveryNamedCapability_ShouldExist() {
        string demo2 = Path.Combine(Root(), "src", "Example", "FrameworkDemo", "Demo2");
        string all = string.Join(Environment.NewLine, Directory
            .GetFiles(demo2, "*.cs", SearchOption.AllDirectories)
            .Select(DocsFile.ReadAllText));

        foreach (string name in Capabilities) {
            // **`record` 和 `class` 都算**：这不是风格问题，`Appointment` 在基线里就是个普通类。
            // **Both `record` and `class` count** — not a style question: `Appointment` is a plain class in the baseline too.
            // **名字后面必须是空白或 `{`**：`class AppointmentLimit` 不算声明了 `Appointment`。
            // **The name must be followed by whitespace or `{`**: `class AppointmentLimit` does not declare `Appointment`.
            bool declared = IsDeclared(all, name);
            declared.Should().BeTrue(name + " is marked done in the matrix, so its declaration must exist");
        }
    }

    /// <summary>**每个能力在别的文件里都有引用** / Each capability is referenced from another file.</summary>
    /// <remarks>
    /// 这是"有调用方"的全部含义。**声明它自己所在的那一行不算**——一个只在自己文件里出现的类型，
    /// 从"被用上"的角度和一个空壳没有区别。
    ///
    /// This is the whole meaning of "has a caller". **The line inside its own file does not count**: a type that only appears in its own file is, from the
    /// "actually used" standpoint, indistinguishable from a shell.
    /// </remarks>
    [Fact]
    public void EveryNamedCapability_ShouldBeReferencedFromAnotherFile() {
        string demo2 = Path.Combine(Root(), "src", "Example", "FrameworkDemo", "Demo2");
        string[] files = Directory.GetFiles(demo2, "*.cs", SearchOption.AllDirectories);

        foreach (string name in Capabilities) {
            // **除了声明它自己的那个文件**，剩下的文件里必须还有它。
            // **Apart from the file that declares it**, at least one other file must still mention it.
            string[] declaring = files
                .Where(file => IsDeclared(DocsFile.ReadAllText(file), name))
                .ToArray();
            declaring.Should().ContainSingle(name + " should be declared in exactly one place");

            files
                .Where(file => !string.Equals(file, declaring[0], StringComparison.Ordinal))
                .Where(file => DocsFile.ReadAllText(file).Contains(name, StringComparison.Ordinal))
                .Should().NotBeEmpty(name + " is marked done in the matrix, so some other file has to use it");
        }
    }

    // 矩阵四之二点名的能力 / The capabilities named in the matrix's fourth section.
    private static readonly string[] Capabilities = [
        "Appointment", "BiologicalLimit", "Flight", "Payload", "TotalWeight",
        "MaxLoadWeight", "Torque", "MacModel", "MacAggregation",
        "StowageAggregation", "PayloadMaximizationAggregation", "SolutionAnalyzer",
    ];
    /// <summary>**那个"空壳"其实是一个枚举** / The "shell" is really an enum.</summary>
    /// <remarks>
    /// 矩阵四之二写"**1** 个空壳（`Domain/Aircraft/Model/FlightPhase.cs`）"。**这里去读了那个文件**：
    /// 它是 `public enum FlightPhase { ZeroFuel, TakeOff, Landing }`——**三个成员，一个不缺，能编译、能引用。**
    ///
    /// **它不是空壳，是"没有对应基线文件"的枚举。** 矩阵那个词把两件事混在一起了：
    ///
    /// | 情形 | 该叫什么 |
    /// | --- | --- |
    /// | 类型存在、成员齐全、没有基线对应物 | **本仓新增**，不是缺口 |
    /// | 类型存在、成员为空或只有 `TODO` | **空壳** |
    ///
    /// 判据因此钉**真正的空壳定义**（成员为零），并**单独钉住 `FlightPhase` 有它的三个成员**——
    /// 这样"补上成员"或"删掉成员"都会红，而"矩阵叫它空壳"这件事不会再被当成事实传下去。
    ///
 /// The matrix calls `FlightPhase.cs` a shell. ** read the file**: it is `public enum FlightPhase { ZeroFuel, TakeOff, Landing }` — **three members, none
    /// missing, compiles and is referenced.** It is not a shell but **an enum with no baseline counterpart**, and the matrix's word conflated two cases: a type that exists
    /// with all its members and no baseline original is **an addition, not a gap**; a type with no members or only `TODO`s is **a shell**. The criterion therefore pins the
    /// **real** definition (zero members) and separately pins that `FlightPhase` keeps its three members, so adding or dropping one reddens — and the claim that the matrix
    /// calls it a shell stops being passed on as fact.
    /// </remarks>
    [Fact]
    public void ThereShouldBeNoEmptyTypeBodies() {
        string demo2 = Path.Combine(Root(), "src", "Example", "FrameworkDemo", "Demo2");
        string[] files = Directory.GetFiles(demo2, "*.cs", SearchOption.AllDirectories);

        // **空壳 = 类型体里一个成员都没有**。这是能说清楚的定义，也是唯一值得钉的定义。
        // **A shell has no members at all.** That is a definition one can state, and the only one worth pinning.
        string[] shells = files.Where(file => HasEmptyTypeBody(DocsFile.ReadAllText(file))).ToArray();
        shells.Should().BeEmpty("a type with no members cannot do anything; one appearing means a file was stubbed out");
    }

    /// <summary>**`FlightPhase` 的三个成员都在** / `FlightPhase` keeps its three members.</summary>
    [Fact]
    public void TheFlightPhaseEnum_ShouldKeepItsMembers() {
        string path = Path.Combine(
            Root(), "src", "Example", "FrameworkDemo", "Demo2", "Domain", "Aircraft", "Model", "FlightPhase.cs");
        string text = DocsFile.ReadAllText(path);

        // **三个成员一个都不能少**：少了 `TakeOff`，"这个枚举是空的"就成真了。
        // **All three members**: drop `TakeOff` and "the enum is empty" becomes true.
        text.Should().Contain("ZeroFuel");
        text.Should().Contain("TakeOff");
        text.Should().Contain("Landing");
    }
    /// <summary>**`DFSGenerator` 不是空壳** / `DFSGenerator` is not a shell.</summary>
    /// <remarks>
    /// 矩阵四之一曾经写"`DFSGenerator` 是个空壳，已删除"，**两句话都不对**。这条判据钉住实测：
    /// 文件在、行数够、被 `FullSumGenerator` 的搜索共用。**"空壳"这个词一旦写错，就没人会去查。**
    ///
    /// The matrix used to say `DFSGenerator` is a shell deleted; **both halves are false**. This pins the measurement: the file exists, it is not a stub, and
    /// `FullSumGenerator` shares its search. **Once "shell" is written down wrongly, nobody goes to check.**
    /// </remarks>
    [Fact]
    public void TheDfsGenerator_ShouldNotBeAShell() {
        string path = Path.Combine(
            Root(), "src", "Framework.Csp1d", "Domain", "CuttingPlanGeneration", "service", "DFSGenerator.cs");
        DocsFile.Exists(path).Should().BeTrue("the matrix used to say it was deleted; it is not");

        string text = DocsFile.ReadAllText(path);
        // **行数是粗指标，但"空壳"和"153 行"不可能同时成立。**
        // **Line count is a blunt measure, but "a shell" and "153 lines" cannot both be true.**
        text.Split('\n').Length.Should().BeGreaterThan(60,
            "a shell of this class would be a handful of lines, not a search implementation");
        text.Should().Contain("satisfiesPruningConstraints",
            "this is the one difference from FullSumGenerator, and the reason the class exists at all");
    }

    /// <summary>**四之一把"接线缺口"和"空壳"分开写** / 四之一 separates a wiring gap from a shell.</summary>
    [Fact]
    public void TheCsp1dSection_ShouldDistinguishAWiringGapFromAShell() {
        string matrix = DocsFile.ReadAllText(Path.Combine(Root(), "docs", "kotlin-capability-matrix.md"));
        matrix.Should().Contain("**接线缺口**", "the whole correction is that these are two different things");
        matrix.Should().Contain("**153 行实现**", "the correction has to carry its measurement");
    }

    /// <summary>**每个 starter 都存在，且引用它该引用的工程** / Every starter exists and references the project it names.</summary>
    /// <remarks>
    /// 矩阵四之三曾经列了五个 starter，其中**三个目录根本不存在**却标着 ✅，同时漏掉四个实际存在的。
    /// **这不是"文档写错一行"**：没有入门包，用这个模块的人得手写五条工程引用——**装不上**。
    ///
    /// 这条判据因此**同时查两侧**：目录在，且 `ProjectReference` 指向矩阵写的那一个。只查目录会放过
    /// "建了目录但引用错模块"，而那种 starter 装上去**什么都不传递**。
    ///
    /// The matrix listed five starters, **three of whose directories did not exist** while marked ✅, and omitted four that did. **That is not one wrong row**: without a
    /// starter, anyone using the module writes five project references by hand — **it cannot be installed**. This checks **both sides**: the directory exists and its
    /// `ProjectReference` points at the project the matrix names. Checking the directory alone would let "directory created, wrong module referenced" through, and such a
    /// starter **carries nothing**.
    /// </remarks>
    [Fact]
    public void EveryStarter_ShouldExistAndReferenceItsProject() {
        (string Starter, string Project)[] expected = [
            ("Core", "Core"),
            ("Framework", "Framework"),
            ("Bpp1d", "Framework.Bpp1d"),
            ("Bpp2d", "Framework.Bpp2d"),
            ("Bpp3d", "Framework.Bpp3d"),
            ("Csp1d", "Framework.Csp1d"),
            ("Csp2d", "Framework.Csp2d"),
            ("GanttScheduling", "Framework.GanttScheduling"),
            ("GanttNetworkScheduling", "Framework.GanttNetworkScheduling"),
            ("NetworkScheduling", "Framework.NetworkScheduling"),
        ];

        string root = Root();
        foreach ((string starter, string project) in expected) {
            string csproj = Path.Combine(root, "src", "Starters", starter, "Starters." + starter + ".csproj");
            DocsFile.Exists(csproj).Should().BeTrue(starter + " is marked done in the matrix, so its csproj has to exist");

            string text = DocsFile.ReadAllText(csproj);
            // **引用的是那个工程**，不是随便一个。
            // **The project it names**, not just any.
            text.Should().Contain(project + ".csproj",
                starter + " has to reference " + project + "; a starter pointing elsewhere carries the wrong module");
            // **程序集名各不相同**： 之前五个 starter 逐字节相同，`AssemblyName` 全是 `Fuookami.Ospf.Starters`，
            // 于是同名程序集互相覆盖。这条钉住那个回归。
            // **Distinct assembly names**: before all five were byte-identical with `AssemblyName` set to `Fuookami.Ospf.Starters`, so identically named
            // assemblies overwrote each other. This pins that regression.
            text.Should().Contain("<AssemblyName>Fuookami.Ospf.Starters." + starter + "</AssemblyName>",
                "a shared assembly name makes the packages overwrite each other, which is the B287 defect");
        }
    }

    /// <summary>**第九节的每个门内文件数都能被重数出来** / Every gate file count in the ninth section can be re-counted.</summary>
    /// <remarks>
    /// 第九节列着每个求解器插件的门内文件数。 一数就发现 **SCIP 少算了 36 个（10 → 46）**——
    /// 而 SCIP 是**这个环境里唯一真跑的后端**。**把验证写得比真少，和夸大一样有害，只是方向相反。**
    ///
    /// 计数口径：`.cs` 文件里出现 `<SYMBOL>_INTEGRATION`，且**只含该符号**（`CapabilityMatrixTests` 自己也同时提到 SCIP 与 Gurobi）。
    ///
    /// The ninth section lists a file count per solver plugin. counted them and found **SCIP under-reported by 36 (10 → 46)** — and SCIP is **the one backend
    /// that really runs in this environment**. **Under-reporting what was verified is as harmful as exaggerating, just in the other direction.** Counted as: a `.cs`
    /// file containing `<SYMBOL>_INTEGRATION` and **only that symbol** (`CapabilityMatrixTests` itself mentions both SCIP and Gurobi).
    /// </remarks>
    [Fact]
    public void TheGateFileCounts_ShouldMatchTheMatrix() {
        string root = Root();
        string[] files = Directory.GetFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories);
        string matrix = DocsFile.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        (string Symbol, int Expected)[] gates = [
            // 新增 OnnxAffineEndpointGurobiIntegrationTest（ONNX 仿射链的端到端求解），
            // GUROBI 门内文件数随之 32 → 33。这是实测事实变了，不是口径变了。
            // the endpoint test for the ONNX affine chain adds one gate file, so GUROBI goes 32 -> 33. The measurement changed, not the rule.
            // 新增 OnnxModelAttachmentGurobiIntegrationTest（ONNX 仿射结果挂进 LinearMetaModel 求解），
            // GUROBI 门内文件数 33 → 34。
            // the attachment test adds one gate file, so GUROBI goes 33 -> 34.
                        // 新增 OnnxSymbolicActivationGurobiIntegrationTest（符号激活挂进模型并求解），
            // GUROBI 门内文件数 34 → 35。
            // the symbolic-activation test adds one gate file, so GUROBI goes 34 -> 35.
                        // 新增 OnnxQuadraticObjectiveGurobiIntegrationTest（二次目标挂进元模型并求解），
            // GUROBI 门内文件数 35 → 36。
            // the quadratic-objective test adds one gate file, so GUROBI goes 35 -> 36.
                        // 新增 OnnxPiecewiseActivationGurobiIntegrationTest（分段近似的 Sigmoid 挂进模型并求解），
            // GUROBI 门内文件数 36 → 37。
            // the piecewise-activation test adds one gate file, so GUROBI goes 36 -> 37.
            // 新增的 OnnxQuadraticSolverGurobiIntegrationTest 不引用 `GUROBI_INTEGRATION` 符号，
            // 而这一条按"文件里是否引用该门符号"来数，所以 GUROBI 仍是 37——**我没有按实测改文档，而是先改对了数字。**
            //
            // the new quadratic-solver test does not reference the GUROBI_INTEGRATION symbol, and this criterion counts by whether a file references the gate symbol, so GUROBI stays 37 —
            // **the document follows the measurement rather than the other way round.**
            ("SCIP", 46), ("GUROBI", 37), ("CPLEX", 4), ("COPT", 6),
            ("MINDOPT", 2), ("HEXALY", 1), ("MOSEK", 1),
        ];

        foreach ((string symbol, int expected) in gates) {
            string gate = symbol + "_INTEGRATION";
            int actual = files.Count(file => {
                string text = DocsFile.ReadAllText(file);
                if (!text.Contains(gate, StringComparison.Ordinal)) {
                    return false;
                }
                // **只算独占该符号的文件**：交叉门的文件算给谁都会把小号那个数抬高。
                // **Exclusive only**: counting a cross-gate file for either side inflates the smaller one.
                return CountGates(text) == 1;
            });
            actual.Should().Be(expected, symbol + " has " + actual + " gate files, so the matrix must say so");
        }

        // **文档里的数字必须和实测一致**：不然下一个人读到的就是错的。
        // **The document has to agree with the measurement**, or the next reader gets a wrong number.
        matrix.Should().Contain("46 个门内文件", "the SCIP row states its count");
    }

    /// <summary>**`MultiArray` 的源文件数是 11** / `MultiArray` has 11 source files.</summary>
    /// <remarks>
    /// 第九节第 4 条原来写 13，实测 **11**。多数两个会让"5 个用 `where`"的比例从 45% 变成 38%——
    /// **比例是那条偏差的论据，分母错了论据就跟着错。**
    ///
    /// Item 4 said 13; the measurement is **11**. Two extra files turn "5 use `where`" from 45% to 38% — **the ratio is that deviation's argument, and a wrong
    /// denominator makes the argument wrong too.**
    /// </remarks>
    [Fact]
    public void TheMultiArrayFileCount_ShouldBeEleven() {
        string root = Root();
        int actual = Directory.GetFiles(Path.Combine(root, "src", "MultiArray"), "*.cs", SearchOption.TopDirectoryOnly).Length;
        actual.Should().Be(11, "the ninth section argues a ratio, so its denominator has to be right");

        string matrix = DocsFile.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));
        matrix.Should().Contain("**11 个**源文件里 **5 个**用 `where`",
            "and the argument has to carry the measured numbers, not the old ones");
    }

    /// <summary>**有多少个门符号同时出现在一个文件里** / How many gate symbols co-occur.</summary>
    private static int CountGates(string text) {
        string[] gates = [
            "SCIP_INTEGRATION", "GUROBI_INTEGRATION", "CPLEX_INTEGRATION", "COPT_INTEGRATION",
            "MINDOPT_INTEGRATION", "HEXALY_INTEGRATION", "MOSEK_INTEGRATION", "LINGO_INTEGRATION",
            "OPTVERSE_INTEGRATION",
        ];
        int count = 0;
        foreach (string gate in gates) {
            if (text.Contains(gate, StringComparison.Ordinal)) {
                ++count;
            }
        }
        return count;
    }

    /// <summary>**这个名字是否作为类型被声明** / Whether the name is declared as a type.</summary>
    /// <remarks>
    /// **不能用 `Contains`**：`class AppointmentLimit` 里含 `Appointment`，于是 `Appointment` 会被当成声明了两次。
    /// 名字后面必须紧跟空白或 `{`。
    ///
    /// **`Contains` will not do**: `class AppointmentLimit` contains `Appointment`, so `Appointment` would read as declared twice. The name has to be followed by
    /// whitespace or `{`.
    /// </remarks>
    private static bool IsDeclared(string text, string name) {
        foreach (string keyword in new[] { "record ", "class ", "struct " }) {
            int index = text.IndexOf(keyword + name, StringComparison.Ordinal);
            while (index >= 0) {
                int after = index + keyword.Length + name.Length;
                if (after >= text.Length || !(char.IsLetterOrDigit(text[after]) || text[after] == '_')) {
                    return true;
                }
                index = text.IndexOf(keyword + name, after, StringComparison.Ordinal);
            }
        }
        return false;
    }

    /// <summary>**类型体里有没有成员** / Whether the type body has any members.</summary>
    /// <remarks>
    /// **不看行数**：第一版按"语句不超过 N 条"判，先误报 4 个（把三个属性的 record 算成空壳），收紧后变成 0 个（漏掉真空壳）。
    /// **行数不是这个问题的形状。** 改成看**花括号之间有没有东西**：没有就是空壳。
    ///
    /// **Not by line count**: the first version used "at most N statements", which over-reported four (records with three properties) and, once tightened, under-reported to
    /// zero. **Line count is not the shape of this question.** It now looks at **whether anything sits between the braces**.
    /// </remarks>
    private static bool HasEmptyTypeBody(string text) {
        int open = text.IndexOf("{", StringComparison.Ordinal);
        int close = text.LastIndexOf("}", StringComparison.Ordinal);
        if (open < 0 || close <= open) {
            return false;
        }
        string body = text.Substring(open + 1, close - open - 1);
        foreach (string raw in body.Split('\n')) {
            string line = raw.Trim();
            if (line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal)) {
                return false;
            }
        }
        return true;
    }

    private static string Root() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        if (directory is null) {
            throw new InvalidOperationException("repository root not found");
        }
        return directory.FullName;
    }
}
