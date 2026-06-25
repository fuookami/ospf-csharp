#nullable enable

namespace Fuookami.Ospf.Framework.Persistence
{
    /// <summary>
    /// 请求 DTO 接口 / Request DTO interface.
    /// </summary>
    /// <typeparam name="T">自身类型 / Self type</typeparam>
    public interface IRequestDto<out T> where T : class
    {
        /// <summary>请求标识 / Request identifier</summary>
        string? Id { get; }
        /// <summary>响应码 / Response code</summary>
        int? Code { get; }
        /// <summary>响应消息 / Response message</summary>
        string? Msg { get; }
    }

    /// <summary>
    /// 响应 DTO 接口 / Response DTO interface.
    /// </summary>
    /// <typeparam name="T">自身类型 / Self type</typeparam>
    public interface IResponseDto<out T> where T : class
    {
        /// <summary>请求标识 / Request identifier</summary>
        string? Id { get; }
        /// <summary>响应码 / Response code</summary>
        int? Code { get; }
        /// <summary>响应消息 / Response message</summary>
        string? Msg { get; }
    }
}
