// 二维套裁裁切工艺 / 2D cutting stock process.

using System.Globalization;

namespace Fuookami.Ospf.Framework.Csp2d.Infrastructure;

/// <summary>
/// 裁切工艺 / Cutting process.
/// </summary>
/// <remarks>
/// **工艺与模式正交**：模式决定料怎么来（卷材条带排样 / 定尺板材），工艺决定怎么下刀。
/// 把两者合成一个枚举，就会出现"卷材 + 断头台三级的组合到底合不合法"这种**没人能一句话答的问题**。
///
/// **The process is orthogonal to the mode**: the mode decides where the material comes from (coil strip packing or fixed-size sheets), while the process decides how the
/// cuts are made. Merging them into one enum invites the question "is coil plus a three-stage guillotine legal?", which **nobody can answer in one sentence**.
/// </remarks>
public enum CuttingProcess2 {
    /// <summary>
    /// 任意排样：设备按轮廓切（激光/水刀/CNC），只需要放置坐标 / Arbitrary nesting: the machine cuts along contours (laser, waterjet, CNC), needing coordinates only.
    /// </summary>
    Freeform,

    /// <summary>断头台 2 级：先把料切成条，再把条切成件 / Guillotine with two stages: strips, then pieces.</summary>
    GuillotineTwoStage,

    /// <summary>断头台 3 级：切条 → 切块 → 切件 / Guillotine with three stages: strips, blocks, then pieces.</summary>
    GuillotineThreeStage,
}

/// <summary>工艺属性与判断 / Process properties and checks.</summary>
public static class CuttingProcess2Extensions {
    /// <summary>
    /// 允许的最大工序级数 / The maximum allowed process stage.
    /// </summary>
    /// <param name="process">工艺 / The process.</param>
    /// <returns>最大级数 / The maximum stage.</returns>
    /// <remarks>
    /// 任意排样**不受级数约束**，所以返回 `int.MaxValue` 而不是 0 或 -1——**返回 0 会让"一级工序"被判成越界**。
    ///
    /// Arbitrary nesting **has no stage limit**, so `int.MaxValue` is returned rather than 0 or -1; **returning 0 would reject even a single-stage process**.
    /// </remarks>
    public static int MaxStage(this CuttingProcess2 process) => process switch {
        CuttingProcess2.GuillotineTwoStage => 2,
        CuttingProcess2.GuillotineThreeStage => 3,
        _ => int.MaxValue,
    };

    /// <summary>
    /// 是否要求输出断头台切分树 / Whether a guillotine cut tree is required.
    /// </summary>
    /// <param name="process">工艺 / The process.</param>
    /// <returns>是否要求 / Whether required.</returns>
    public static bool RequiresCutTree(this CuttingProcess2 process) =>
        process is CuttingProcess2.GuillotineTwoStage or CuttingProcess2.GuillotineThreeStage;

    /// <summary>
    /// 判断工序级数是否被本工艺允许 / Check whether a process stage is allowed by this process.
    /// </summary>
    /// <param name="process">工艺 / The process.</param>
    /// <param name="stage">工序级数 / The process stage.</param>
    /// <returns>是否允许 / Whether it is allowed.</returns>
    /// <remarks>
    /// **级数从 1 起算**，0 与负数都非法——`stage &gt;= 1` 这一半不能省。
    ///
    /// **Stages start at 1**; 0 and negatives are illegal, so the `stage &gt;= 1` half cannot be dropped.
    /// </remarks>
    public static bool AllowsStage(this CuttingProcess2 process, int stage) =>
        stage >= 1 && stage <= process.MaxStage();

    /// <summary>
    /// 工序级数的中英双语标签 / The bilingual label of a process stage.
    /// </summary>
    /// <param name="process">工艺 / The process.</param>
    /// <param name="stage">工序级数 / The process stage.</param>
    /// <returns>标签 / The label.</returns>
    public static string StageLabel(this CuttingProcess2 process, int stage) => stage switch {
        1 => "切条 / strip",
        2 => "切块 / block",
        3 => "切件 / piece",
        _ => "第 " + stage.ToString(CultureInfo.InvariantCulture) + " 级 / stage "
            + stage.ToString(CultureInfo.InvariantCulture),
    };
}
