using System.Text.Json;
using System.Text.Json.Nodes;
using Corkboard.Core.ControlCenter;

namespace Corkboard.Core.Tests;

/// <summary>协议 v4 的帧形状与序列化往返（字段名必须与 <c>control-center-protocol-v4.md</c> 一致）。</summary>
public class ControlV4ProtocolTests
{
    [Fact]
    public void RequestFrame_RoundTrips()
    {
        var frame = ControlFrame.Request(
            "req-1",
            ControlTopics.CommandAck,
            new JsonObject { ["accepted"] = true });

        var json = ControlV4Protocol.Serialize(frame);
        Assert.Contains("\"type\":\"req\"", json);
        Assert.Contains("\"topic\":\"cmd.ack\"", json);

        var parsed = ControlV4Protocol.TryParse(json);
        Assert.NotNull(parsed);
        Assert.Equal(ControlFrameTypes.Request, parsed!.Type);
        Assert.Equal("req-1", parsed.Id);
        Assert.Equal(ControlTopics.CommandAck, parsed.Topic);
        Assert.True(parsed.Payload!["accepted"]!.GetValue<bool>());
    }

    [Fact]
    public void ResponseFrame_KeepsCodeAndMessage()
    {
        var frame = ControlFrame.Response("req-2", false, ControlErrorCodes.BadSecret);
        frame.Message = "secret rejected";

        var parsed = ControlV4Protocol.TryParse(ControlV4Protocol.Serialize(frame));

        Assert.NotNull(parsed);
        Assert.False(parsed!.Ok);
        Assert.Equal(ControlErrorCodes.BadSecret, parsed.Code);
        Assert.Equal("secret rejected", parsed.Message);
    }

    [Fact]
    public void TryParse_RejectsGarbageAndUnknownTypes()
    {
        Assert.Null(ControlV4Protocol.TryParse(null));
        Assert.Null(ControlV4Protocol.TryParse(""));
        Assert.Null(ControlV4Protocol.TryParse("not json at all"));
        Assert.Null(ControlV4Protocol.TryParse("{\"id\":\"x\",\"topic\":\"cmd\"}"));
        Assert.Null(ControlV4Protocol.TryParse("{\"type\":\"nope\"}"));
    }

    [Fact]
    public void HelloPayload_UsesProtocolFieldNames()
    {
        var hello = new ControlHelloPayload
        {
            Platform = ControlNodeConnectOptions.DefaultPlatform,
            Version = "0.1.0",
            DeviceName = "podium",
            DeviceId = "dev-1",
            Secret = "s3cret",
            Capabilities = [ControlV4Protocol.CapabilityStatusRead, "refreshNotice"],
            AppliedStateRevision = 42
        };

        var json = JsonSerializer.Serialize(hello, ControlV4Protocol.JsonOptions);

        Assert.Contains("\"protocol\":4", json);
        Assert.Contains("\"device_id\":\"dev-1\"", json);
        Assert.Contains("\"applied_state_revision\":42", json);
        Assert.Contains("\"capabilities\":[\"status.read\",\"refreshNotice\"]", json);
    }

    [Fact]
    public void HelloReply_DeserializesServerLimits()
    {
        var json = JsonSerializer.Serialize(FakeControlNodeTransportFactory.DefaultHelloReply());

        var reply = JsonSerializer.Deserialize<ControlHelloReply>(json, ControlV4Protocol.JsonOptions);

        Assert.NotNull(reply);
        Assert.Equal(ControlV4Protocol.Version, reply!.Protocol);
        Assert.Equal("dev-1", reply.Device?.Id);
        Assert.Equal("SC-AAAAAA", reply.Device?.SiteId);
        Assert.Equal(5000, reply.Limits?.CommandTimeoutMs);
        Assert.Equal(25, reply.HeartbeatSeconds);
        Assert.Equal(75, reply.OfflineAfterSeconds);
    }

    [Fact]
    public void CommandInvocation_FallsBackToIdAndKeepsArgs()
    {
        var payload = new JsonObject
        {
            ["id"] = "cmd_legacy",
            ["cmd"] = "refreshNotice",
            ["args"] = new JsonObject { ["title"] = "hello", ["body"] = "world" },
            ["deadline_ms"] = 4000,
            ["expires_at"] = 1_700_000_000_000,
            ["issuer"] = "web"
        };

        var invocation = ControlCommandInvocation.FromPayload(payload);

        Assert.Equal("cmd_legacy", invocation.CommandId);
        Assert.Equal("refreshNotice", invocation.Command);
        Assert.Equal("hello", invocation.Args["title"]!.GetValue<string>());
        Assert.Equal(4000, invocation.DeadlineMs);
        Assert.Equal(1_700_000_000_000, invocation.ExpiresAt);
        Assert.Equal("web", invocation.Issuer);
    }

    [Fact]
    public void AckAndResultPayloads_SerializeAcceptedAndOk()
    {
        var ack = JsonSerializer.SerializeToNode(
            new ControlCommandAckPayload { Accepted = false, Code = ControlErrorCodes.Expired },
            ControlV4Protocol.JsonOptions)!.AsObject();
        var result = JsonSerializer.SerializeToNode(
            new ControlCommandResultPayload
            {
                Ok = false,
                Code = ControlErrorCodes.Timeout,
                Data = new JsonObject { ["partial"] = true }
            },
            ControlV4Protocol.JsonOptions)!.AsObject();

        Assert.False(ack["accepted"]!.GetValue<bool>());
        Assert.Equal("expired", ack["code"]!.GetValue<string>());
        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.Equal("timeout", result["code"]!.GetValue<string>());
        Assert.True(result["data"]!["partial"]!.GetValue<bool>());
    }

    [Fact]
    public void DesiredState_ReadsSnakeAndCamelClassId()
    {
        var snake = ControlDesiredStatePayload.FromPayload(new JsonObject
        {
            ["revision"] = 100,
            ["class_id"] = "class-a",
            ["draw_locked"] = false
        });
        var camel = ControlDesiredStatePayload.FromPayload(new JsonObject
        {
            ["revision"] = 200,
            ["classId"] = "class-b"
        });

        Assert.Equal(100, snake.Revision);
        Assert.Equal("class-a", snake.ClassId);
        Assert.False(snake.DrawLocked);
        Assert.Equal("class-b", camel.ClassId);
        Assert.Null(camel.DrawLocked);
    }

    [Fact]
    public void Evicted_ReadsEitherClassIdSpelling()
    {
        var snake = JsonSerializer.Deserialize<ControlEvictedPayload>(
            "{\"class_id\":\"class-a\",\"reason\":\"superseded\"}", ControlV4Protocol.JsonOptions);
        var camel = JsonSerializer.Deserialize<ControlEvictedPayload>(
            "{\"classId\":\"class-b\",\"reason\":\"revoked\"}", ControlV4Protocol.JsonOptions);

        Assert.Equal("class-a", snake!.ResolveClassId());
        Assert.Equal("superseded", snake.Reason);
        Assert.Equal("class-b", camel!.ResolveClassId());
        Assert.Equal("revoked", camel.Reason);
    }

    [Fact]
    public void TreePayload_FlattensToGradeNodes()
    {
        var payload = JsonSerializer.Deserialize<ControlTreePayload>(
            """
            {
              "site": { "publicId": "SC-AAAAAA", "name": "site-a" },
              "grades": [
                { "id": "g1", "name": "grade-1",
                  "classes": [ { "id": "c1", "name": "class-1" }, { "id": "c2", "name": "class-2" } ] }
              ],
              "device": { "id": "dev-1", "name": "podium", "classId": "c1" }
            }
            """,
            ControlV4Protocol.JsonOptions);

        var grades = payload!.ToGradeNodes();

        var grade = Assert.Single(grades);
        Assert.Equal("g1", grade.Id);
        Assert.Equal(2, grade.Classes.Count);
        Assert.Equal("c1", grade.Classes[0].Id);
    }
}
