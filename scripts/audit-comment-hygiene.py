"""扫描仓库里残留的迁移叙述。

判为叙述的形态见 docs/comment-hygiene-audit.md 第一节；命中数必须为 0。
"""
import io
import os
import re
import sys

PATTERNS = [
    ("批次编号", re.compile(r"(?<![0-9A-Fa-fx])B[0-9]{3,4}(?![0-9A-Fa-f])")),
    ("相对批次", re.compile(r"\b(?:this|earlier|last|next|previous)\s+batch", re.IGNORECASE)),
    ("Batch N", re.compile(r"\bbatch\s+\d+", re.IGNORECASE)),
    ("第 N 批", re.compile(r"第\s*[0-9一二三四五六七八九十百]+\s*批")),
    ("中文相对批次", re.compile(r"这一批|本批|上一批|下一批")),
    ("第一人称过程", re.compile(r"我(第一版|又|当时|最初)")),
    ("迁移动作", re.compile(r"迁移（|迁移了")),
    ("会话坐标", re.compile(r"本会话|本批次")),
    ("迁移阶段", re.compile(r"\bphase\s+\d+", re.IGNORECASE)),
    ("as of Batch", re.compile(r"as of batch|ported in b[0-9]", re.IGNORECASE)),
]

SKIP_DIRS = {"bin", "obj", ".git", "logs", "node_modules"}

# 说明文件本身要举反例，所以不参与扫描。
SKIP_FILES = {"docs/comment-hygiene-audit.md", "scripts/audit-comment-hygiene.py"}


def hits():
    for root, dirs, names in os.walk("."):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in names:
            if not name.endswith((".cs", ".md")):
                continue
            path = os.path.join(root, name)
            if path.replace(os.sep, "/").lstrip("./") in SKIP_FILES:
                continue
            try:
                lines = io.open(path, encoding="utf-8").read().replace("\r\n", "\n").split("\n")
            except (OSError, UnicodeDecodeError):
                continue
            for index, line in enumerate(lines, 1):
                stripped = line.strip()
                is_comment = stripped.startswith("//")
                if not (is_comment or name.endswith(".md")):
                    continue
                for label, pattern in PATTERNS:
                    if pattern.search(line):
                        yield label, path.replace(os.sep, "/"), index, stripped[:110]
                        break


def main():
    found = list(hits())
    for label, path, index, text in found:
        print("[%s] %s:%d %s" % (label, path, index, text))
    print("命中: %d" % len(found))
    return 0 if not found else 1


if __name__ == "__main__":
    sys.exit(main())
