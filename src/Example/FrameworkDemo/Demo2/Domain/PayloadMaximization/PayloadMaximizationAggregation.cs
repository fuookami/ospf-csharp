#nullable enable

using System;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization;

/// <summary>
/// 业载最大化聚合根 / The payload-maximization aggregation.
/// </summary>
/// <remarks>
/// 与对齐：**只有两个字段**——飞机型号与业载模型。整个子域只有一条管线（`MaxPayloadLimit`），
/// 没有任何校验，也没有自建的中间符号。
///
/// 为什么只有两个字段：业载最大化的目标就是 `payload.estimatePayload` 本身，而那个符号由 `Payload.Register` 在配载域里建好。
/// 这里再翻译一遍 DTO 会造出第二份 `Payload`，而它的符号与配载域的那份**不是同一个**——目标函数会指向一组没人约束的变量。
///
/// **two fields only** — the aircraft model and the payload model. The whole sub-domain has one pipeline (`MaxPayloadLimit`), no validations and no
/// self-built intermediate symbols. Why only two: the objective is `payload.estimatePayload` itself, a symbol `Payload.Register` already builds in the stowage domain; translating
/// the DTO again would produce a second `Payload` whose symbols are **not** the stowage domain's, so the objective would point at a set of unconstrained variables.
/// </remarks>
public sealed class PayloadMaximizationAggregation
{
    /// <summary>飞机型号 / The aircraft model</summary>
    public AircraftModel AircraftModel { get; }

    /// <summary>业载模型 / The payload model</summary>
    public Payload Payload { get; }

    /// <summary>
    /// 构造聚合 / Construct the aggregation.
    /// </summary>
    /// <param name="aircraftModel">飞机型号 / The aircraft model.</param>
    /// <param name="payload">业载模型 / The payload model.</param>
    public PayloadMaximizationAggregation(AircraftModel aircraftModel, Payload payload)
    {
        ArgumentNullException.ThrowIfNull(aircraftModel);
        ArgumentNullException.ThrowIfNull(payload);
        AircraftModel = aircraftModel;
        Payload = payload;
    }
}
