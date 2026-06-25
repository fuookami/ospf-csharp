#nullable enable

using System;
using System.Security.Cryptography;

namespace Fuookami.Ospf.Utils;
/// <summary>
/// UUIDv7 生成器 / UUIDv7 generator (mirrors ospf-kotlin UUIDv7).
/// Generates time-ordered UUIDv7 values per RFC 9562.
/// </summary>
public static class UuidV7 {
    /// <summary>生成 UUIDv7 字节数组 / Generate UUIDv7 byte array.</summary>
    public static byte[] Generate() {
        byte[] bytes = new byte[16];

        // 48-bit timestamp in milliseconds since Unix epoch
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(timestamp >> 40);
        bytes[1] = (byte)(timestamp >> 32);
        bytes[2] = (byte)(timestamp >> 24);
        bytes[3] = (byte)(timestamp >> 16);
        bytes[4] = (byte)(timestamp >> 8);
        bytes[5] = (byte)timestamp;

        // 4-bit version (0111) + 12-bit random
        RandomNumberGenerator.Fill(bytes.AsSpan(6, 2));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70); // version 7

        // 2-bit variant (10) + 62-bit random
        RandomNumberGenerator.Fill(bytes.AsSpan(8, 8));
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variant RFC 4122

        return bytes;
    }

    /// <summary>生成 UUIDv7 Guid / Generate UUIDv7 Guid.</summary>
    public static Guid GenerateGuid() {
        byte[] bytes = Generate();
        return new Guid(bytes);
    }
}
