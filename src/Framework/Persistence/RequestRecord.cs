#nullable enable

using System;

namespace Fuookami.Ospf.Framework.Persistence
{
    /// <summary>
    /// 请求记录持久化对象 / Request record persistence object.
    /// </summary>
    public sealed record RequestRecordPo(
        string Id,
        string App,
        string Version,
        string ServiceId,
        DateTime Time,
        byte[] Payload);

    /// <summary>
    /// 响应记录持久化对象 / Response record persistence object.
    /// </summary>
    public sealed record ResponseRecordPo(
        string Id,
        string App,
        string Version,
        string ServiceId,
        DateTime Time,
        int Code,
        string Msg,
        byte[] Payload);

    /// <summary>
    /// 请求记录 / Request record.
    /// </summary>
    public sealed record RequestRecord<T> where T : class
    {
        public required string Id { get; init; }
        public required string App { get; init; }
        public required string Version { get; init; }
        public required string ServiceId { get; init; }
        public required DateTime Time { get; init; }
        public required T Value { get; init; }

        /// <summary>转换为持久化对象 / Convert to persistence object.</summary>
        public RequestRecordPo ToPo(Func<T, byte[]> serializer) => new(
            Id, App, Version, ServiceId, Time, serializer(Value));
    }

    /// <summary>
    /// 响应记录 / Response record.
    /// </summary>
    public sealed record ResponseRecord<T> where T : class
    {
        public required string Id { get; init; }
        public required string App { get; init; }
        public required string Version { get; init; }
        public required string ServiceId { get; init; }
        public required DateTime Time { get; init; }
        public required int Code { get; init; }
        public required string Msg { get; init; }
        public required T Value { get; init; }

        /// <summary>转换为持久化对象 / Convert to persistence object.</summary>
        public ResponseRecordPo ToPo(Func<T, byte[]> serializer) => new(
            Id, App, Version, ServiceId, Time, Code, Msg, serializer(Value));
    }
}
