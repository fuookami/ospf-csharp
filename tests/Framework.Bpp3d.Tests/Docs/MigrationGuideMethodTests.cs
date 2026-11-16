#nullable enable

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 迁移指南里的方法教训 / The method lesson in the migration guide.
/// </summary>
/// <remarks>
/// **一个没有判据守护的教训会重新被人踩一遍。** 放弃逐文件比对时没留下数字，后来又有人想试； 量出 98.9% 才把它挡住。
/// 这一条把教训钉在**指南**上，而不只是矩阵上——**写代码的人先看指南，先看矩阵的是写文档的人。**
///
/// **A lesson with no criterion watching it gets trodden on again.** abandoned per-file comparison without leaving a number and people tried again; the earlier 98.9% is
/// what stops it. This pins the lesson in **the guide**, not only the matrix — **coders read the guide first; readers of the matrix are the ones writing documents.**
/// </remarks>
public class MigrationGuideMethodTests
{
    private static string Guide() {
        string root = Root();
        return File.ReadAllText(Path.Combine(root, "docs", "kotlin-migration-guide.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
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
