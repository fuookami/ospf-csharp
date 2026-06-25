#nullable enable

using System;
using System.Text.Json;
using FluentAssertions;
using Fuookami.Ospf.Utils.Serialization;
using Xunit;

namespace Fuookami.Ospf.Utils.Tests.Serialization
{
    public class LocalMonthSerializerTest
    {
        [Fact]
        public void Serialize_DateOnly_ProducesYearMonth()
        {
            var date = new DateOnly(2024, 6, 1);
            var options = new JsonSerializerOptions();
            options.Converters.Add(new LocalMonthSerializer());
            var json = JsonSerializer.Serialize(date, options);
            json.Should().Contain("2024-06");
        }

        [Fact]
        public void Deserialize_YearMonth_ProducesDateOnly()
        {
            var json = "\"2024-06\"";
            var options = new JsonSerializerOptions();
            options.Converters.Add(new LocalMonthSerializer());
            var date = JsonSerializer.Deserialize<DateOnly>(json, options);
            date.Should().Be(new DateOnly(2024, 6, 1));
        }
    }
}
