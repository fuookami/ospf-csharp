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
    /// 持久化 API 实现（立即返回同步响应，后台处理并异步回调）
    /// Persistence API implementation (return a sync response immediately, process in the background).
    /// </summary>
    /// <typeparam name="TReq">请求类型 / Request type</typeparam>
    /// <typeparam name="TRep">处理结果类型 / Processed response type</typeparam>
    /// <typeparam name="TSyncRep">同步响应类型 / Synchronous response type</typeparam>
    /// <param name="api">API 名称 / API name</param>
    /// <param name="app">应用名称 / Application name</param>
    /// <param name="requester">请求者标识 / Requester identifier</param>
    /// <param name="version">版本号 / Version</param>
    /// <param name="request">请求对象 / Request object</param>
    /// <param name="process">处理函数 / Processing function</param>
    /// <param name="asyncResponse">异步回调，收到处理结果 / The asynchronous callback receiving the processed response</param>
    /// <param name="syncResponse">立即返回的同步响应 / The synchronous response returned at once</param>
    /// <returns>同步响应 / The synchronous response</returns>
    /// <remarks>
    /// 与基线 `MongoPersistenceApiController.persistenceApiImpl` 的第二个重载对齐。三处照抄：
    ///
    /// 1. **同步响应与处理结果可以是不同类型**（`TSyncRep` 与 `TRep` 各一个）：
    ///    典型用法是先回一个"已受理"的轻响应，处理完再把完整结果回调出去。
    ///    把两者合成一个类型，就只能在"立刻回"和"回真结果"之间选一个。
    /// 2. **`process` 在后台跑，不阻塞返回**：请求落库、处理、结果落库、回调这一串都在后台，
    ///    调用方拿到的就是传进来的 `syncResponse`，与处理是否完成无关。
    /// 3. **落库仍是尽力而为**：Mongo 未配置时整条持久化路径静默跳过，处理与回调照常。
    ///    让持久化失败打断业务处理，是把日志问题升级成业务问题。
    ///
    /// Aligned with the second overload of the baseline's `MongoPersistenceApiController.persistenceApiImpl`. Three things copied: **the synchronous response and the processed result may be of
    /// different types** (`TSyncRep` and `TRep`), the typical use being to acknowledge at once and push the full result later, whereas merging them would force a choice between "answer immediately"
    /// and "answer with the real result"; **`process` runs in the background without blocking the return**, so the caller gets exactly the `syncResponse` it passed in regardless of whether
    /// processing has finished; and **persistence stays best-effort**, the whole path being skipped silently when Mongo is unconfigured while processing and the callback proceed, since letting a
    /// persistence failure break the business call would escalate a logging problem into a business one.
    /// </remarks>
    TSyncRep PersistenceApiImplAsync<TReq, TRep, TSyncRep>(
        string api,
        string app,
        string requester,
        string version,
        TReq request,
        Func<TReq, TRep> process,
        Action<TRep> asyncResponse,
        TSyncRep syncResponse)
        where TReq : class
        where TRep : class
        where TSyncRep : class {
        _ = FireAndForgetRequestAsync(api, requester, version, request);
        _ = Task.Run(async () => {
            TRep response = process(request);
            await FireAndForgetResponseAsync(api, app, requester, version, response).ConfigureAwait(false);
            asyncResponse(response);
        });
        return syncResponse;
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
