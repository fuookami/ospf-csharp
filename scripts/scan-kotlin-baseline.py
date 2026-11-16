#!/usr/bin/env python3
"""基线能力扫描：把"还缺什么"从印象变成列表。

A baseline capability scan: turns "what is still missing" from an impression into a list.

## 为什么要有这个脚本 / Why this script exists

B1059—B1067 那几批的缺口**全部来自这个扫描**，而那几批的扫描是**每批临时写一次的 Python**。
临时脚本的结局是：**下一会话不知道它存在，于是要么重写一遍，要么相信"已经全搬完了"。**

**而"相信已经全搬完了"是这里最贵的错**：它会让人跳过整个核对环节。

The gaps in B1059—B1067 **all came from this scan**, and each batch's scan was **throwaway Python**.
The fate of a throwaway script is that **the next session does not know it exists, and either rewrites it or believes everything is already ported.**
**Believing that is the expensive error here**: it makes people skip the whole check.

## 四个维度 / Four dimensions

| 维度 | 抓什么 |
| --- | --- |
| `functions` | 每个 `fun` 名字 |
| `classes` | 每个顶层 `class` 声明 |
| `enums` | 每个 `enum class` 成员 |
| `properties` | 每个成员 `val` / `var` |

## 已知改名 / Known renames

匹配按**名字**做，而 C# 侧的命名习惯不同：`DerivedConstraintProgrammingModel` → `DerivedModel`、
`inferredSupport` → `InferSupport`、`EffectiveAt` → `FirstEffective`。
**这些不是缺口，但必须写下来**——**不写，扫描每次都会把它们重新报一遍，而报多了就没人看了。**

Matching is by **name**, and the C# side names things differently. **These are not gaps, but they have to be written down**: **unwritten, every run re-reports them, and a report that cries wolf is a report nobody reads.**

## 用法 / Usage

    python scripts/scan-kotlin-baseline.py [--kotlin ../ospf-kotlin] [--commit 966cafbc]

未解释的发现会让退出码非零 / Unexplained findings make the exit code non-zero.
"""

import argparse
import io
import os
import re
import subprocess
import sys

# **已知改名 / Known renames**：基线名字 -> C# 名字。不是缺口。
KNOWN_RENAMES = {
    "DerivedConstraintProgrammingModel": "DerivedModel",
    "inferredSupport": "InferSupport",
    "EffectiveAt": "FirstEffective",
    "tryBuildCriticalityProfile": "TryBuild",
    "buildCriticalityProfile": "Build",
    "collectConstraintVariables": "Variables",
    "collectExpressionVariables": "Variables",
    "collectLiteralVariable": "Variables",
    # **行为由别的成员承担，而不是同名 / Carried by a differently-named member.**
    "analyzeAndExplain": "BlockingSummary",
    "criticalityClassifications": "Classify",
    "mapOriginalDuals": "MapRanges",
    "rebindExpression": "RebindDomain",
    "rebindInterval": "RebindDomain",
    "rebindConstraint": "RebindDomain",
    "propagateBuilderFailure": "PropagateBackendFailure",
}

# **已知的"写不了"，不是"还没写" / Known un-portable, not un-ported.**
# 每一项都必须在台账里有一条绊线判据：**卡点解除时它要红。**
# Every entry needs a tripwire criterion, **so lifting the blocker turns something red**.
KNOWN_BLOCKERS = {
    "LinearSolverFixedIntegerLpBackend": (
        "the solver output layer carries no duals; see FixedIntegerLpBlockerTests"
    ),
    # **弹性过滤要反复解可行性问题**，而这条路径需要能真求解的后端；
    # 本仓的 `FixedIntegerLpGates` 只做"什么情况下不许下结论"的判定。
    # Elastic filtering re-solves feasibility; that path needs a backend that really solves, and the
    # gates here only decide when a conclusion must be withheld.
    "solveTriad": (
        "elastic filtering re-solves feasibility; only the gates that withhold conclusions are here"
    ),
}

# **函数内局部 `val` 会被属性扫描误抓**：这些是已知的局部量，不是成员。
LOCAL_PROPERTIES = {
    "funnelById",
    "targetStatus",
    "rowsBySource",
}


def baseline_files(kotlin_repo: str, commit: str) -> list[str]:
    listing = subprocess.run(
        ["git", "-C", kotlin_repo, "ls-tree", "-r", "--name-only", commit],
        capture_output=True,
        text=True,
        check=True,
    ).stdout.split("\n")
    return [
        path
        for path in listing
        if "/core/analysis/" in path
        and path.endswith(".kt")
        and "/src/main/" in path
    ]


def read(kotlin_repo: str, commit: str, path: str) -> str:
    return subprocess.run(
        ["git", "-C", kotlin_repo, "cat-file", "-p", f"{commit}:{path}"],
        capture_output=True,
        text=True,
        check=True,
    ).stdout


def csharp_corpus(csharp_root: str) -> str:
    directory = os.path.join(csharp_root, "src", "Core", "Analysis")
    return "".join(
        io.open(os.path.join(directory, name), encoding="utf-8", errors="ignore").read()
        for name in sorted(os.listdir(directory))
        if name.endswith(".cs")
    )


def scan(files: dict[str, str], corpus: str) -> dict[str, list[tuple[str, str, str]]]:
    """Return unmatched names per dimension."""

    found: dict[str, list[tuple[str, str, str]]] = {
        "functions": [],
        "classes": [],
        "enums": [],
        "properties": [],
    }

    def missing(name: str) -> bool:
        if name in KNOWN_RENAMES or name in KNOWN_BLOCKERS:
            return False
        return name not in corpus and (name[0].upper() + name[1:]) not in corpus

    for path, source in files.items():
        for match in re.finditer(r"\bfun\s+(?:<[^>]*>\s*)?([A-Za-z_][A-Za-z0-9_]*)", source):
            name = match.group(1)
            if missing(name) and name not in KNOWN_RENAMES:
                found["functions"].append((path, "", name))

        for match in re.finditer(
            r"^(?:public\s+|internal\s+|private\s+)?"
            r"(?:sealed\s+|abstract\s+|open\s+|data\s+|enum\s+|value\s+)*"
            r"class\s+([A-Za-z_][A-Za-z0-9_]*)",
            source,
            re.M,
        ):
            name = match.group(1)
            if missing(name) and name not in KNOWN_RENAMES:
                found["classes"].append((path, "", name))

        for match in re.finditer(r"enum class (\w+)\s*(?:\([^)]*\))?\s*\{([^}]*)\}", source, re.S):
            enum_name, body = match.group(1), match.group(2)
            for member in re.finditer(r"^\s*([A-Z][A-Za-z0-9_]*)\s*(?:\(|,|;|$)", body, re.M):
                name = member.group(1)
                if missing(name) and name not in KNOWN_RENAMES:
                    found["enums"].append((path, enum_name, name))

        for match in re.finditer(r"^    (?:override )?(?:val|var) ([a-z]\w*)", source, re.M):
            name = match.group(1)
            if name in LOCAL_PROPERTIES:
                continue
            if missing(name) and name not in KNOWN_RENAMES:
                found["properties"].append((path, "", name))

    return found


def main() -> int:
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--kotlin", default=os.path.join(root, "..", "ospf-kotlin"))
    parser.add_argument("--commit", default="966cafbc")
    parser.add_argument("--csharp", default=root)
    args = parser.parse_args()

    files = {
        os.path.basename(path): read(args.kotlin, args.commit, path)
        for path in baseline_files(args.kotlin, args.commit)
    }
    corpus = csharp_corpus(args.csharp)

    print(f"baseline analysis files: {len(files)}")

    unexplained = 0
    for dimension, rows in scan(files, corpus).items():
        print(f"{dimension}: {len(rows)} unexplained")
        for path, container, name in rows:
            where = f"{path}:{container}" if container else path
            print(f"  {where} -> {name}")
        unexplained += len(rows)

    if unexplained:
        print(f"\n{unexplained} unexplained name(s). Judge each one, then either port it or add it to KNOWN_RENAMES.")
        return 1

    print("\nEvery baseline name resolves.")
    print("Renames: " + ", ".join(sorted(KNOWN_RENAMES)))
    for name, reason in sorted(KNOWN_BLOCKERS.items()):
        print(f"Blocked: {name} — {reason}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
