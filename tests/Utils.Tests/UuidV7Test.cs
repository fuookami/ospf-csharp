#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils;
using System;
using Xunit;

namespace Fuookami.Ospf.Utils.Tests;

public class UuidV7Test {
    [Fact]
    public void GeneratedBytesContainTimestampVersionAndVariant() {
        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        byte[] bytes = UuidV7.Generate();
        long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        bytes.Should().HaveCount(16);
        (bytes[6] >> 4).Should().Be(7);
        (bytes[8] & 0xC0).Should().Be(0x80);

        long timestamp =
            ((long)bytes[0] << 40) |
            ((long)bytes[1] << 32) |
            ((long)bytes[2] << 24) |
            ((long)bytes[3] << 16) |
            ((long)bytes[4] << 8) |
            bytes[5];
        timestamp.Should().BeInRange(before, after);
    }

    [Fact]
    public void GeneratedGuidUsesRfcByteOrdering() {
        Guid guid = UuidV7.GenerateGuid();
        string text = guid.ToString();

        text[14].Should().Be('7');
        "89ab".Should().Contain(text[19].ToString());
        UuidV7.GenerateSync().Should().HaveCount(16);
    }
}
