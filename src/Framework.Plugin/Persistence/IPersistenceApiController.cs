#nullable enable

using MongoDB.Driver;
using System;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Plugin.Persistence;
/// <summary>
/// 持久化 API 控制器接口 / Persistence API controller interface.
///
/// 实现此接口的控制器可自动将 API 请求和响应持久化到 MongoDB。
/// Controllers implementing this interface can automatically persist API
/// requests and responses to MongoDB.
/// </summary>
public interface IPersistenceApiController {
    /// <summary>
    /// 当前控制器绑定的 Mongo 数据库实例
    /// The Mongo database bound to this controller (null when persistence is disabled).
    /// </summary>
    IMongoDatabase? MongoClient { get; }

    /// <summary>
    /// 持久化 API 实现（同步处理，异步持久化）
    /// Persistence API implementation (sync processing, async persistence).
    /// </summary>
    /// <typeparam name="TReq">请求类型 / Request type</typeparam>
    /// <typeparam name="TRep">响应类型 / Response type</typeparam>
    /// <param name="api">API 名称 / API name</param>
    /// <param name="app">应用名称 / Application name</param>
    /// <param name="requester">请求者标识 / Requester identifier</param>
    /// <param name="version">版本号 / Version</param>
    /// <param name="request">请求对象 / Request object</param>
    /// <param name="process">处理函数 / Processing function</param>
    /// <returns>响应对象 / Response object</returns>
    TRep PersistenceApiImpl<TReq, TRep>(
        string api,
        string app,
        string requester,
        string version,
        TReq request,
        Func<TReq, TRep> process)
        where TReq : class
        where TRep : class {
        // fire-and-forget request persistence
        _ = FireAndForgetRequestAsync(api, requester, version, request);
        TRep response = process(request);
        _ = FireAndForgetResponseAsync(api, app, requester, version, response);
        return response;
    }

    /// <summary>
    /// 异步保存请求记录 / Fire-and-forget request persistence.
    /// </summary>
    Task FireAndForgetRequestAsync<TReq>(
        string api, string requester, string version, TReq request)
        where TReq : class {
        if (MongoClient is null) { return Task.CompletedTask; }
        try {
            IMongoCollection<TReq> collection = MongoClient.GetCollection<TReq>("api_requests");
            return collection.InsertOneAsync(request);
        }
        catch {
            // best-effort: never propagate persistence failures into the API path
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 异步保存响应记录 / Fire-and-forget response persistence.
    /// </summary>
    Task FireAndForgetResponseAsync<TRep>(
        string api, string app, string requester, string version, TRep response)
        where TRep : class {
        if (MongoClient is null) { return Task.CompletedTask; }
        try {
            IMongoCollection<TRep> collection = MongoClient.GetCollection<TRep>("api_responses");
            return collection.InsertOneAsync(response);
        }
        catch {
            // best-effort
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// MongoDB 集合扩展 / MongoDB collection extensions.
/// </summary>
internal static class MongoCollectionExtensions {
    public static IMongoCollection<T> GetCollection<T>(this IMongoDatabase database, string name) => database.GetCollection<T>(name);
}
