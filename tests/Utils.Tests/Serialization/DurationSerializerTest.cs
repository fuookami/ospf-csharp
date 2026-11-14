#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils.Serialization;
using System;
using System.Text.Json;
using Xunit;

namespace Fuookami.Ospf.Utils.Tests.Serialization;

public class DurationSerializerTest {
    [Fact]
    public void IsoDurationRoundTrips() {
        var options = new JsonSerializerOptions { Converters = { new DurationSerializer() } };
        var duration = TimeSpan.FromMinutes(90);

        string json = JsonSerializer.Serialize(duration, options);
        TimeSpan parsed = JsonSerializer.Deserialize<TimeSpan>(json, options);

        json.Should().Be("\"PT1H30M\"");
        parsed.Should().Be(duration);
    }

    [Fact]
    public void DiscreteDurationTruncatesToConfiguredUnit() {
        var options = new JsonSerializerOptions {
            Converters = { new DiscreteDurationSerializer(TimeSpanUnit.Seconds) },
        };

        JsonSerializer.Serialize(TimeSpan.FromMilliseconds(1999), options).Should().Be("1");
        JsonSerializer.Deserialize<TimeSpan>("2", options).Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ContinuousDurationPreservesFractionalUnits() {
        var options = new JsonSerializerOptions {
            Converters = { new ContinuousDurationSerializer(TimeSpanUnit.Seconds) },
        };

        string json = JsonSerializer.Serialize(TimeSpan.FromMilliseconds(1500), options);
        TimeSpan parsed = JsonSerializer.Deserialize<TimeSpan>(json, options);

        json.Should().Be("1.5");
        parsed.Should().Be(TimeSpan.FromMilliseconds(1500));
    }
}
