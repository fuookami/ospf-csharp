#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Proto;

/// <summary>
/// **本插件依赖的字段号，以及"为什么不需要按版本分支"** / The field numbers this plugin relies on, and why no version branch is needed.
/// </summary>
/// <remarks>
/// `ir_version` 处理方式。**结论是**不分支**，理由不是"懒得写"，而是 protobuf 的规则本身：**
/// **字段号一旦发布就不可改，** **新版本只**追加字段**，不会给既有字段换号。**
///
/// **于是"按版本分支"这件事没有对象：** **同一个字段号在任何版本里都是同一个含义。** '
/// **真正需要版本判定的情形只有一种——**字段语义被改变**（例如同一个号从 int 变成 message），**而 protobuf 明令禁止这种改动。**
///
/// **那这一层还要做什么：** **把"我们依赖哪些字段号"写成可核对的表，** **并如实报告"这个版本号我们验证过没有"，而不是假装验证过。**
///
/// Syncs the `ir_version` handling. **The conclusion is no branch, and the reason is protobuf's own rule rather than laziness: field numbers are immutable once published, and a new
/// version only appends fields, never renumbers existing ones.**
///
/// **So "branch by version" has no subject: the same field number means the same thing in every version.** **The only case that would need a version check is a changed field
/// meaning (say a number moving from int to message), which protobuf forbids.**
///
/// **What this layer still owes: a checkable table of the field numbers we rely on, and an honest report of whether a given version was verified — not a pretence that it was.**
/// </remarks>
public static class OnnxSchemaVersion
{
    /// <summary>
    /// 一条字段号记录 / One field-number record.
    /// </summary>
    /// <param name="Message">消息名 / The message name.</param>
    /// <param name="Number">字段号 / The field number.</param>
    /// <param name="Meaning">含义 / What it carries.</param>
    public sealed record FieldNumber(string Message, int Number, string Meaning);

    /// <summary>
    /// 本插件解析时实际分派的字段号 / The field numbers the parser actually dispatches on.
    /// </summary>
    /// <remarks>
    /// **这张表是判据的对照物：** **表里的号与代码里 `case` 的号必须一致，** **否则"我们依赖哪些字段"这句话就只是注释。**
    ///
 /// **This table is what the criteria check against: the numbers here must match the `case` labels in the implementation, or "these are the fields we rely on" stays a comment.**
    /// </remarks>
    public static readonly IReadOnlyList<FieldNumber> FieldNumbers = new[] {
        new FieldNumber("ModelProto", 1, "ir_version"),
        new FieldNumber("ModelProto", 7, "graph"),
        new FieldNumber("ModelProto", 8, "opset_import"),
        new FieldNumber("GraphProto", 1, "node"),
        new FieldNumber("GraphProto", 2, "name"),
        new FieldNumber("GraphProto", 5, "initializer"),
        new FieldNumber("GraphProto", 11, "input"),
        new FieldNumber("GraphProto", 12, "output"),
        new FieldNumber("NodeProto", 1, "input"),
        new FieldNumber("NodeProto", 2, "output"),
        new FieldNumber("NodeProto", 3, "name"),
        new FieldNumber("NodeProto", 4, "op_type"),
        new FieldNumber("NodeProto", 5, "attribute"),
        new FieldNumber("AttributeProto", 1, "name"),
        new FieldNumber("AttributeProto", 2, "f"),
        new FieldNumber("AttributeProto", 3, "i"),
        new FieldNumber("AttributeProto", 4, "s"),
        new FieldNumber("AttributeProto", 5, "t"),
        new FieldNumber("AttributeProto", 6, "g（If／Loop／Scan 的分支体）"),
        new FieldNumber("AttributeProto", 7, "floats"),
        new FieldNumber("AttributeProto", 8, "ints"),
        new FieldNumber("AttributeProto", 9, "strings"),
        new FieldNumber("AttributeProto", 20, "type"),
        new FieldNumber("TensorProto", 1, "dims"),
        new FieldNumber("TensorProto", 2, "data_type"),
        new FieldNumber("TensorProto", 4, "float_data"),
        new FieldNumber("TensorProto", 7, "int64_data"),
        new FieldNumber("TensorProto", 9, "raw_data"),
        new FieldNumber("TensorProto", 10, "double_data"),
        new FieldNumber("ValueInfoProto", 2, "type"),
        new FieldNumber("OperatorSetIdProto", 2, "version"),
    };

    /// <summary>本插件实际验证过的 `ir_version` / The ir_version this plugin was actually verified against.</summary>
    public const long VerifiedIrVersion = 8;

    /// <summary>
    /// 描述一个模型头部的版本信息 / Describe a model header's versioning.
    /// </summary>
    /// <param name="irVersion">`ModelProto.ir_version`。</param>
    /// <param name="opset">`opset_import` 里的版本 / The version from `opset_import`.</param>
    /// <returns>描述 / The description.</returns>
    /// <remarks>
    /// **"另一个版本"不等于"不能解析"：** **字段号不可改，所以更高版本通常仍能读，只是**我们不认识新增字段**（它们被 `SkipField` 跳过）。**
    /// **如实报告"没验证过"，比声称"都支持"有用得多。**
    ///
    /// **A different version is not the same as unparsable: field numbers cannot change, so a higher version usually still reads, with new fields skipped by SkipField.**
    /// **Reporting "not verified" honestly is far more useful than claiming "all supported".**
    /// </remarks>
    public static string Describe(long irVersion, long opset) {
        if (irVersion == VerifiedIrVersion) {
            return "ir_version " + irVersion + "（已验证）/ verified，opset " + opset;
        }
        return "ir_version " + irVersion + "（未验证，按字段号前向读取；新增字段会被跳过）/ not verified, read forward by field number; new fields are skipped，opset "
               + opset;
    }

    /// <summary>
    /// 是否需要按版本分支 / Whether a version branch is needed.
    /// </summary>
    /// <param name="irVersion">`ModelProto.ir_version`。</param>
    /// <returns>是否需要分支 / Whether a branch is needed.</returns>
    /// <remarks>
    /// **永远返回 `false`，而这个 `false` 是可以解释的：** **它成立的前提是"解析只按字段号分派、未知字段被跳过"，**
    /// **而这两件事各有一条判据钉着。** **若哪天有人引入了按版本改语义的做法，那两条判据会先响。**
    ///
    /// **It always returns false, and that false is defensible: its premise is "parsing dispatches by field number only and unknown fields are skipped", and each of those has its own
    /// criterion.** **If someone later makes semantics depend on the version, those criteria fire first.**
    /// </remarks>
    public static bool NeedsVersionBranch(long irVersion) => false;
}