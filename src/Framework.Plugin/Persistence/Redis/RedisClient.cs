#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StackExchange.Redis;

namespace Fuookami.Ospf.Framework.Plugin.Persistence.Redis
{
    /// <summary>
    /// Redis 客户端键 / Redis client cache key.
    /// </summary>
    /// <param name="Name">客户端名称 / Client name</param>
    /// <param name="Database">数据库编号 / Database number</param>
    public sealed record RedisClientKey(string Name, int Database);

    /// <summary>
    /// Redis 配置数据 / Redis configuration data.
    /// </summary>
    /// <param name="Urls">Redis 地址列表 / Redis URL list</param>
    /// <param name="Name">客户端名称 / Client name</param>
    /// <param name="MasterName">主节点名称（可选）/ Master name (optional)</param>
    /// <param name="Database">数据库编号 / Database number</param>
    /// <param name="Password">密码 / Password</param>
    public sealed record RedisConfig(
        IReadOnlyList<string> Urls,
        string Name,
        string? MasterName,
        int Database,
        string Password)
    {
        /// <summary>客户端键 / Client key.</summary>
        public RedisClientKey Key => new(Name, Database);

        /// <summary>序列化为 JSON / Serialize to JSON.</summary>
        public string ToJson() => JsonSerializer.Serialize(this);

        /// <summary>从 JSON 反序列化 / Deserialize from JSON.</summary>
        public static RedisConfig? FromJson(string json) =>
            string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<RedisConfig>(json);
    }

    /// <summary>
    /// Redis 客户端 / Redis client wrapping a StackExchange.Redis multiplexer database.
    /// </summary>
    public sealed class RedisClient : IDisposable
    {
        private readonly IConnectionMultiplexer _multiplexer;
        private readonly IDatabase _database;

        internal RedisClient(IConnectionMultiplexer multiplexer, int database)
        {
            _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
            _database = multiplexer.GetDatabase(database);
        }

        /// <summary>底层 IDatabase / Underlying StackExchange.Redis database.</summary>
        public IDatabase Database => _database;

        /// <summary>底层连接多路复用器 / Underlying connection multiplexer.</summary>
        public IConnectionMultiplexer Multiplexer => _multiplexer;

        /// <summary>
        /// 设置字符串键值 / Set string key-value.
        /// </summary>
        public void Set(string name, string value, TimeSpan? ex = null)
        {
            _database.StringSet(name, value, ex);
        }

        /// <summary>
        /// 设置列表键值 / Set list key-value.
        /// </summary>
        public void Set(string name, IReadOnlyList<string> value, TimeSpan? ex = null)
        {
            _database.KeyDelete(name);
            _database.ListRightPush(name, value.Select(v => (RedisValue)v).ToArray());
            if (ex is not null) { _database.KeyExpire(name, ex); }
        }

        /// <summary>
        /// 设置集合键值 / Set set key-value.
        /// </summary>
        public void Set(string name, IReadOnlySet<string> value, TimeSpan? ex = null)
        {
            _database.KeyDelete(name);
            _database.SetAdd(name, value.Select(v => (RedisValue)v).ToArray());
            if (ex is not null) { _database.KeyExpire(name, ex); }
        }

        /// <summary>
        /// 设置哈希表键值 / Set hash map key-value.
        /// </summary>
        public void Set(string name, IReadOnlyDictionary<string, string> value, TimeSpan? ex = null)
        {
            _database.KeyDelete(name);
            _database.HashSet(name, value.Select(kv => new HashEntry(kv.Key, kv.Value)).ToArray());
            if (ex is not null) { _database.KeyExpire(name, ex); }
        }

        /// <summary>
        /// 设置 JSON 序列化对象键值 / Set JSON-serialized object key-value.
        /// </summary>
        public void Set<T>(string name, T value, TimeSpan? ex = null)
        {
            var json = JsonSerializer.Serialize(value);
            _database.StringSet(name, json, ex);
        }

        /// <summary>
        /// 获取字符串值 / Get string value.
        /// </summary>
        public string? Get(string name) => _database.StringGet(name);

        /// <summary>
        /// 获取列表值 / Get list value.
        /// </summary>
        public List<string>? GetList(string name)
        {
            var values = _database.ListRange(name);
            return values.Length == 0 ? null : values.Select(v => v.ToString()).ToList();
        }

        /// <summary>
        /// 获取集合值 / Get set value.
        /// </summary>
        public HashSet<string>? GetSet(string name)
        {
            var values = _database.SetMembers(name);
            return values.Length == 0 ? null : values.Select(v => v.ToString()).ToHashSet();
        }

        /// <summary>
        /// 获取哈希表值 / Get hash map value.
        /// </summary>
        public Dictionary<string, string>? GetMap(string name)
        {
            var entries = _database.HashGetAll(name);
            return entries.Length == 0
                ? null
                : entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        }

        /// <summary>
        /// 获取 JSON 反序列化对象 / Get JSON-deserialized object.
        /// </summary>
        public T? GetObject<T>(string name)
        {
            var value = _database.StringGet(name);
            return value.HasValue ? JsonSerializer.Deserialize<T>((string)value!) : default;
        }

        /// <summary>释放资源 / Dispose.</summary>
        public void Dispose() => _multiplexer.Dispose();
    }

    /// <summary>
    /// Redis 静态客户端管理器 / Redis static client manager.
    ///
    /// 管理多个 Redis 连接实例，按名称和数据库编号索引。
    /// Manages multiple Redis connection instances, indexed by name and database number.
    /// </summary>
    public static class Redis
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<RedisClientKey, IConnectionMultiplexer> _clients = new();

        /// <summary>
        /// 初始化并获取 Redis 客户端 / Initialize and get Redis client.
        /// </summary>
        /// <param name="config">Redis 配置 / Redis configuration</param>
        /// <returns>Redis 客户端实例或 null / Redis client instance or null</returns>
        public static RedisClient? Init(RedisConfig config)
        {
            lock (_lock)
            {
                if (_clients.TryGetValue(config.Key, out var existing))
                {
                    return new RedisClient(existing, config.Database);
                }

                try
                {
                    var configOptions = new ConfigurationOptions
                    {
                        Password = config.Password,
                        DefaultDatabase = config.Database,
                    };
                    foreach (var url in config.Urls)
                    {
                        configOptions.EndPoints.Add(url);
                    }
                    var multiplexer = ConnectionMultiplexer.Connect(configOptions);
                    _clients[config.Key] = multiplexer;
                    return new RedisClient(multiplexer, config.Database);
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// 按键获取已注册的 Redis 客户端 / Get registered Redis client by key.
        /// </summary>
        public static RedisClient? Get(RedisClientKey key)
        {
            lock (_lock)
            {
                return _clients.TryGetValue(key, out var multiplexer)
                    ? new RedisClient(multiplexer, key.Database)
                    : null;
            }
        }

        /// <summary>
        /// 按名称获取已注册的 Redis 客户端 / Get registered Redis client by name.
        /// </summary>
        public static RedisClient? GetByName(string name, int? database = null)
        {
            lock (_lock)
            {
                var match = _clients.FirstOrDefault(kv =>
                    kv.Key.Name == name && (database is null || kv.Key.Database == database));
                return match.Value is not null ? new RedisClient(match.Value, match.Key.Database) : null;
            }
        }
    }
}
