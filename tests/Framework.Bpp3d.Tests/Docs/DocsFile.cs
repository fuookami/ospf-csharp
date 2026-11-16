// 文档判据读文件时不独占/ Documentation criteria never take an exclusive lock when reading.

using System;
using System.IO;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 文档判据的读文件入口 / The file-reading entry point for documentation criteria.
/// </summary>
/// <remarks>
/// **`File.ReadAllText` 要求独占**：日志或文档正被别的进程（并行跑的门禁脚本、索引服务、另一个测试项目）读着时会抛
/// `IOException`，把一条纯文档判据变成间歇性失败。 只修了 `PlanBatchNumbersTests.TotalPassed` 一处，
/// 结果同一类失败从隔壁的读取点又冒出来——**同一个洞要在唯一的入口上堵**，而不是见一个堵一个。
///
/// **`File.ReadAllText` demands exclusive access**: whenever a log or document is being read by another process (a parallel gate run, an indexer, another test project) it throws
/// `IOException`, turning a pure documentation criterion into an intermittent failure. fixed only `PlanBatchNumbersTests.TotalPassed`, and the same failure resurfaced at a
/// neighbouring read — **one hole is plugged at the single entry point**, not once per symptom.
/// </remarks>
public static class DocsFile {
    /// <summary>
    /// 以 `FileShare.ReadWrite` 读取全部文本 / Read all text with `FileShare.ReadWrite`.
    /// </summary>
    /// <param name="path">路径 / The path.</param>
    /// <returns>文本 / The text.</returns>
    public static string ReadAllText(string path) {
        using FileStream stream = Open(path);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 打开文件，被外部独占时短暂重试 / Open the file, retrying briefly while another process holds it exclusively.
    /// </summary>
    /// <param name="path">路径 / The path.</param>
    /// <returns>流 / The stream.</returns>
    /// <remarks>
    /// `FileShare.ReadWrite` 只说明**我们**不独占，挡不住别人以 `FileShare.None` 打开同一个文件（并行跑的门禁脚本、索引服务、
    /// 杀毒扫描）。 的默认全量测试就撞上过：`p8-batch330-test.log` 被别的进程独占，于是一条纯文档判据红了。
    ///
    /// **重试是这个入口该做的事**：锁是瞬时的，把一条文档判据变成间歇性失败才是真的错。重试仍失败就抛——**不要静默跳过一条判据**。
    ///
    /// `FileShare.ReadWrite` only says **we** do not lock exclusively; it cannot stop another process opening the same file with `FileShare.None` (a parallel gate run, an indexer,
    /// a virus scanner). the earlier default full suite hit exactly that: `p8-batch330-test.log` was held exclusively by another process and a pure documentation criterion went red.
    ///
    /// **Retrying is what this entry point should do**: the lock is transient, whereas turning a documentation criterion into an intermittent failure is a real defect. It still
    /// throws after the retries — **a criterion must never be silently skipped**.
    /// </remarks>
    private static FileStream Open(string path) {
        const int attempts = 10;
        for (int attempt = 0; ; ++attempt) {
            try {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            } catch (IOException) when (attempt < attempts) {
                System.Threading.Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    /// <summary>
    /// 以 `FileShare.ReadWrite` 读取全部行 / Read all lines with `FileShare.ReadWrite`.
    /// </summary>
    /// <param name="path">路径 / The path.</param>
    /// <returns>行 / The lines.</returns>
    public static string[] ReadAllLines(string path) {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new System.Collections.Generic.List<string>();
        while (reader.ReadLine() is { } line) {
            lines.Add(line);
        }
        return lines.ToArray();
    }

    /// <summary>
    /// 是否存在 / Whether the file exists.
    /// </summary>
    /// <param name="path">路径 / The path.</param>
    /// <returns>是否存在 / Whether it exists.</returns>
    public static bool Exists(string path) => File.Exists(path);

    /// <summary>
    /// 枚举目录下的文件 / Enumerate the files of a directory.
    /// </summary>
    /// <param name="path">目录 / The directory.</param>
    /// <param name="pattern">模式 / The pattern.</param>
    /// <param name="option">搜索选项 / The search option.</param>
    /// <returns>路径 / The paths.</returns>
    public static string[] EnumerateFiles(string path, string pattern, SearchOption option) =>
        Directory.GetFiles(path, pattern, option);
}
