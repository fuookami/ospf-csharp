#nullable enable

using Fuookami.Ospf.Utils.Functional;
using System;
using System.IO;
using System.Reflection;

namespace Fuookami.Ospf.Utils;
/// <summary>库辅助方法 / Library helper methods (mirrors ospf-kotlin Library).</summary>
public static class Library {
    /// <summary>从程序集资源加载原生库 / Load native library from assembly resource.</summary>
    public static Result<Success, Error.ErrorCode, Error.Error<Error.ErrorCode>> LoadInJar(string path, string toPath) {
        try {
            var assembly = Assembly.GetExecutingAssembly();
            using Stream? stream = assembly.GetManifestResourceStream(path);
            if (stream is null) {
                return new Failed<Success, Error.ErrorCode, Error.Error<Error.ErrorCode>>(
                    Error.ErrorCode.FileNotFound, $"Resource not found: {path}");
            }

            using var fileStream = new System.IO.FileStream(toPath, System.IO.FileMode.Create);
            stream.CopyTo(fileStream);
            return Results.OkInstance;
        }
        catch (Exception ex) {
            return new Failed<Success, Error.ErrorCode, Error.Error<Error.ErrorCode>>(
                Error.ErrorCode.ApplicationFailed, $"Failed to load resource: {ex.Message}");
        }
    }
}
