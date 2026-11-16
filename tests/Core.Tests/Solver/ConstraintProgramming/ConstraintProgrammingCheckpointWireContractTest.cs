#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.ConstraintProgramming;

public class ConstraintProgrammingCheckpointWireContractTest {
    private static readonly string FixtureRoot = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "analysis-fixtures");

    [Fact]
    public void EncodedEnvelopeShouldMatchTheSharedFieldContract() {
        ConstraintProgrammingCheckpointEnvelope decoded = DecodeFixture();
        string encoded = ConstraintProgrammingCheckpointCodec.Encode(decoded).Value;
        using var document = JsonDocument.Parse(encoded);
        JsonElement root = document.RootElement;

        string[] envelopeFields = FieldNames("envelope-field");
        envelopeFields.Should().HaveCount(25);
        envelopeFields[21].Should().Be("cancellationChain");
        envelopeFields[22].Should().Be("provenance");
        envelopeFields[23].Should().Be("metadata");
        envelopeFields[24].Should().Be("integritySha256");
        root.EnumerateObject().Select(property => property.Name)
            .Should().Equal(envelopeFields);
        root.GetProperty("schemaVersion").GetString().Should().Be(
            Rows("schema").Single()[1]);

        root.GetProperty("provenance").EnumerateObject()
            .Select(property => property.Name)
            .Should().Equal(FieldNames("provenance-field"));
        root.GetProperty("cancellationChain")[0].EnumerateObject()
            .Select(property => property.Name)
            .Should().Equal(FieldNames("cancellation-record"));

        foreach (string field in envelopeFields) {
            root.TryGetProperty(field, out _).Should().BeTrue(
                "every contract field, including nullable fields, must be emitted");
        }
        root.GetProperty("parentCheckpointId").ValueKind.Should()
            .Be(JsonValueKind.Null);
        root.GetProperty("incumbent").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("benders").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("cancellationChain").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void SharedCrossLanguageEnvelopeShouldDecodeReencodeAndRecomputeDigest() {
        string fixture = ReadFixture("checkpoint-envelope-v3.json").TrimEnd();
        ConstraintProgrammingCheckpointEnvelope decoded = DecodeFixture();

        decoded.SchemaVersion.Should().Be("3.0");
        decoded.SourceFormat.Should().Be("v2");
        decoded.IntegritySha256.Should().Be(
            "3f2d2f499f1eb6e7e4378d7a04c85c88896973000a2567c0b908fd24daf65637");
        string unsigned = fixture.Replace(
            $"\"integritySha256\":\"{decoded.IntegritySha256}\"",
            "\"integritySha256\":\"\"",
            StringComparison.Ordinal);
        SolveFingerprinting.Sha256(unsigned).Value
            .Should().Be(decoded.IntegritySha256);
        ConstraintProgrammingCheckpointCodec.Encode(decoded).Value
            .Should().Be(fixture);

        string legacySource = fixture.Replace(
            "\"sourceFormat\":\"v2\"",
            "\"sourceFormat\":\"legacy-v1\"",
            StringComparison.Ordinal);
        ConstraintProgrammingCheckpointCodec.Decode(legacySource)
            .IsFailed.Should().BeTrue();
        string tampered = fixture.Replace(
            "\"runId\":\"run-kotlin\"",
            "\"runId\":\"run-tampered\"",
            StringComparison.Ordinal);
        ConstraintProgrammingCheckpointCodec.Decode(tampered)
            .IsFailed.Should().BeTrue();
    }

    [Fact]
    public void DecodeShouldRejectMapsSignedInNonCanonicalOrder() {
        string fixture = ReadFixture("checkpoint-envelope-v3.json").TrimEnd();
        string canonical = SignWithRequestedConfiguration(
            fixture,
            """{"threads":"4","timeLimit":"30"}""");
        string unsorted = SignWithRequestedConfiguration(
            fixture,
            """{"timeLimit":"30","threads":"4"}""");

        unsorted.Should().NotBe(canonical);
        ConstraintProgrammingCheckpointCodec.Decode(canonical).IsOk.Should().BeTrue();
        ConstraintProgrammingCheckpointCodec.Decode(unsorted)
            .IsFailed.Should().BeTrue();
    }

    [Fact]
    public void CancellationCodesAndStringMapsShouldRoundTripCanonically() {
        string[] codes = {
            "user",
            "callback",
            "taskAbort",
            "future",
            "frameworkLoser",
            "remoteStop",
            "timeout",
            "backend",
            "external",
            "vendorSpecificStop",
        };
        CancellationSource[] expectedSources = {
            CancellationSource.Caller,
            CancellationSource.Callback,
            CancellationSource.Coroutine,
            CancellationSource.Future,
            CancellationSource.Combinatorial,
            CancellationSource.Remote,
            CancellationSource.Timeout,
            CancellationSource.Other,
            CancellationSource.Other,
            CancellationSource.Other,
        };
        PortableCancellationRecord[] portable = codes
            .Select((code, index) => new CancellationRecord(
                Source: CancellationSource.Other,
                RequestedAt: DateTimeOffset.FromUnixTimeMilliseconds(1_000 + index),
                Reason: $"reason-{index}",
                WireOrigin: code).ToPortableCancellationRecord())
            .ToArray();
        CancellationRecord[] parsed = portable
            .Select(record => record.ToCancellationRecord())
            .ToArray();
        parsed.Select(record => record.Source).Should().Equal(expectedSources);
        parsed.Select(record => record.WireOrigin).Should().Equal(
            new string?[] {
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "backend",
                "external",
                "vendorSpecificStop",
            });

        ConstraintProgrammingCheckpointEnvelope decoded = DecodeFixture();
        PortableSolverProvenance provenance = decoded.Provenance! with {
            RequestedConfiguration = new Dictionary<string, string> {
                ["threads"] = "4",
                ["seed"] = "7",
                ["mipGap"] = "0.01",
            },
            EffectiveConfiguration = new Dictionary<string, string> {
                ["zeta"] = "1",
                ["alpha"] = "2",
                ["mu"] = "3",
            },
            EnvironmentSummary = new Dictionary<string, string> {
                ["os"] = "linux",
                ["cpu"] = "arm64",
                ["jvm"] = "21",
            },
        };
        ConstraintProgrammingCheckpointEnvelope normalized =
            ConstraintProgrammingCheckpointCodec.WithIntegrity(decoded with {
                CancellationChain = portable,
                Provenance = provenance,
                Metadata = new Dictionary<string, string> {
                    ["zeta"] = "last",
                    ["alpha"] = "first",
                    ["mid"] = "middle",
                },
            });
        string encoded = ConstraintProgrammingCheckpointCodec.Encode(normalized).Value;
        ConstraintProgrammingCheckpointEnvelope roundTrip =
            ConstraintProgrammingCheckpointCodec.Decode(encoded).Value;

        roundTrip.CancellationChain!.Select(record => record.Origin)
            .Should().Equal(codes);
        roundTrip.Provenance!.RequestedConfiguration!.Keys
            .Should().Equal("mipGap", "seed", "threads");
        roundTrip.Provenance.EffectiveConfiguration!.Keys
            .Should().Equal("alpha", "mu", "zeta");
        roundTrip.Provenance.EnvironmentSummary!.Keys
            .Should().Equal("cpu", "jvm", "os");
        roundTrip.Metadata!.Keys.Should().Equal("alpha", "mid", "zeta");
    }

    private static ConstraintProgrammingCheckpointEnvelope DecodeFixture() {
        Result<
            ConstraintProgrammingCheckpointEnvelope,
            ErrorCode,
            Error<ErrorCode>> result =
            ConstraintProgrammingCheckpointCodec.Decode(
                ReadFixture("checkpoint-envelope-v3.json"));
        result.Should().BeOfType<
            Ok<
                ConstraintProgrammingCheckpointEnvelope,
                ErrorCode,
                Error<ErrorCode>>>(because: result is Failed<
                    ConstraintProgrammingCheckpointEnvelope,
                    ErrorCode,
                    Error<ErrorCode>> failed
                    ? failed.Error.Message
                    : string.Empty);
        return result.Value;
    }

    private static string SignWithRequestedConfiguration(
        string fixture,
        string configurationJson) {
        const string originalConfiguration =
            """{"threads":"4","timeLimit":"30"}""";
        ConstraintProgrammingCheckpointEnvelope decoded = DecodeFixture();
        string withConfiguration = fixture.Replace(
            $"\"requestedConfiguration\":{originalConfiguration}",
            $"\"requestedConfiguration\":{configurationJson}",
            StringComparison.Ordinal);
        string unsigned = withConfiguration.Replace(
            $"\"integritySha256\":\"{decoded.IntegritySha256}\"",
            "\"integritySha256\":\"\"",
            StringComparison.Ordinal);
        unsigned.Should().NotBe(withConfiguration);
        return unsigned.Replace(
            "\"integritySha256\":\"\"",
            $"\"integritySha256\":\"{SolveFingerprinting.Sha256(unsigned).Value}\"",
            StringComparison.Ordinal);
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(FixtureRoot, name));

    private static IReadOnlyList<string[]> Rows(string section) {
        var result = new List<string[]>();
        string? current = null;
        foreach (string raw in File.ReadLines(
                     Path.Combine(FixtureRoot, "checkpoint-wire-contract.tsv"))) {
            string line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line) ||
                line.TrimStart().StartsWith("#", StringComparison.Ordinal)) {
                continue;
            }
            if (line.StartsWith("[", StringComparison.Ordinal) &&
                line.EndsWith("]", StringComparison.Ordinal)) {
                current = line[1..^1];
                continue;
            }
            if (current != section) {
                continue;
            }
            string[] fields = line.Split('\t')
                .Select(field => field.Trim())
                .ToArray();
            if (fields.Length == 1 && fields[0].Contains('=', StringComparison.Ordinal)) {
                int separator = fields[0].IndexOf('=', StringComparison.Ordinal);
                fields = new[] {
                    fields[0][..separator],
                    fields[0][(separator + 1)..],
                };
            }
            result.Add(fields);
        }
        return result;
    }

    private static string[] FieldNames(string section) =>
        Rows(section).Select(row => row[1]).ToArray();
    /// <summary>**fixture 必须逐字节固定** / The fixture must be fixed byte for byte.</summary>
    /// <remarks>
    /// 之前，这一节只证明"C# 能解码、能重编码、能复算摘要"——**却没人核对过 fixture 本身是不是**。
    /// 一份被本地改过的 fixture 能让这三件事全部通过，同时证明不了任何跨语言事实。
    ///
    /// 现在钉住的是**来源与具体字节**：`checkpoint-envelope-v3.json` 必须是
    /// **一个固定的字节序列**：1555 字节、摘要 `3f2d2f49…`、LF 换行。**字节数也是判据的一部分**：
    /// 只比摘要会放过"改了内容但摘要字段没跟着改"的本地副本。
    ///
    /// Before this section only proved "the decoder, re-encoder and digest all agree among themselves" — **nobody checked the fixture against a fixed value**. A locally edited
    /// fixture would let all three pass while proving nothing. What is pinned now is the **exact content**: 1555 bytes with digest `3f2d2f49…`. **The byte count is part of the
    /// criterion**: comparing digests alone would let a local copy whose content changed but whose digest field did not.
    /// </remarks>
    [Fact]
    public void TheEnvelopeFixture_ShouldBeByteIdenticalToTheRecordedContent() {
        string path = Path.Combine(FixtureRoot, "checkpoint-envelope-v3.json");
        File.Exists(path).Should().BeTrue("the cross-language fixture must be present, not regenerated at test time");

        byte[] bytes = File.ReadAllBytes(path);
        bytes.Length.Should().Be(1555, "the recorded fixture is exactly 1555 bytes");
        bytes.Should().NotContain((byte)13, "the recorded fixture has LF endings; a CRLF copy is not byte identical");
        System.Text.Encoding.UTF8.GetString(bytes)
            .Should().Contain("\"integritySha256\":\"3f2d2f499f1eb6e7e4378d7a04c85c88896973000a2567c0b908fd24daf65637\"",
                "this digest is the recorded one; recomputing it here would defeat the purpose of a fixed value");
    }

}
