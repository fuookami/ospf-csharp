#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Framework.Solver.Remote.Port;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Solver.Remote.Adapter
{
    /// <summary>
    /// 本地文件系统对象存储端口 / Local filesystem object storage port.
    /// </summary>
    public sealed class LocalFileObjectStoragePort : IObjectStoragePort
    {
        private readonly string _rootPath;

        public LocalFileObjectStoragePort(string rootPath)
        {
            _rootPath = Path.GetFullPath(rootPath);
            Directory.CreateDirectory(_rootPath);
        }

        public async Task<Result<ObjectRef, ErrorCode, Error<ErrorCode>>> PutAsync(
            ObjectPath path, byte[] data, string? contentType = null,
            CancellationToken cancellationToken = default)
        {
            var fullPath = GetFullPath(path.Value);
            if (fullPath is null)
            {
                return new Failed<ObjectRef, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, "Path traversal detected"));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, data, cancellationToken);

            var etag = ComputeEtag(data);
            var metadataPath = fullPath + ".meta";
            var metadata = JsonSerializer.Serialize(new { contentType, etag });
            await File.WriteAllTextAsync(metadataPath, metadata, cancellationToken);

            return Results.Ok(new ObjectRef(path, new ObjectVersion("1"), new ObjectEtag(etag)));
        }

        public async Task<Result<StoredObject, ErrorCode, Error<ErrorCode>>> GetAsync(
            ObjectRef @ref, CancellationToken cancellationToken = default)
        {
            var fullPath = GetFullPath(@ref.Path.Value);
            if (fullPath is null || !File.Exists(fullPath))
            {
                return new Failed<StoredObject, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.FileNotFound, $"Object not found: {@ref.Path}"));
            }

            var data = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            return Results.Ok(new StoredObject(@ref, data));
        }

        public Task<Result<bool, ErrorCode, Error<ErrorCode>>> DeleteAsync(
            ObjectRef @ref, CancellationToken cancellationToken = default)
        {
            var fullPath = GetFullPath(@ref.Path.Value);
            if (fullPath is null || !File.Exists(fullPath))
            {
                return Task.FromResult(Results.Ok(false));
            }

            File.Delete(fullPath);
            var metaPath = fullPath + ".meta";
            if (File.Exists(metaPath)) File.Delete(metaPath);

            return Task.FromResult(Results.Ok(true));
        }

        public Task<Result<bool, ErrorCode, Error<ErrorCode>>> ExistsAsync(
            ObjectRef @ref, CancellationToken cancellationToken = default)
        {
            var fullPath = GetFullPath(@ref.Path.Value);
            return Task.FromResult(Results.Ok(fullPath is not null && File.Exists(fullPath)));
        }

        private string? GetFullPath(string relativePath)
        {
            var combined = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
            return combined.StartsWith(_rootPath) ? combined : null;
        }

        private static string ComputeEtag(byte[] data)
        {
            var hash = SHA256.HashData(data);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
