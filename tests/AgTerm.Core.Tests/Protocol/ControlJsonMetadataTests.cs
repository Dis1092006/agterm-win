using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Protocol;

public class ControlJsonMetadataTests
{
    public static TheoryData<Type, string> Documents => new()
    {
        { typeof(ControlRequest), """{"cmd":"session.status","target":"active","args":{"status":"active","blink":true}}""" },
        { typeof(ControlResponse), """{"ok":true,"result":{"text":"<ready> & \u041f\u0440\u0438\u0432\u0435\u0442","tree":{"workspaces":[]}}}""" },
        { typeof(ControlPickItem[]), """[{"id":"a","label":"A","subtitle":"<one>"}]""" },
        { typeof(ControlPickResult), """{"result":"picked","id":"a","label":"A","index":0}""" },
        { typeof(ControlAskResult), """{"result":"escaped"}""" },
        { typeof(ControlEvent[]), "[]" },
        { typeof(ControlEvent), """{"seq":1,"ts":0,"kind":"status"}""" },
        { typeof(ControlEvent), """{"seq":1,"ts":0,"kind":"status","payload":null}""" },
        { typeof(ControlHudNode), """{"message":"m","spinner":"none","position":"center","hideAfter":0}""" },
        { typeof(Snapshot), """{"version":1,"selectedSessionID":"abcdefab-1234-1234-1234-abcdefabcdef","workspaces":[{"id":"abcdefab-1234-1234-1234-abcdefabcdef","name":"work","sessions":[{"id":"abcdefab-1234-1234-1234-abcdefabcdef","cwd":"C:\\work","backgroundWatermark":{"kind":"text","text":"<note>"}}]}],"sessionRecency":["abcdefab-1234-1234-1234-abcdefabcdef"]}""" },
        { typeof(AppSettings), """{"fontSize":14,"theme":"<theme>"}""" },
        { typeof(RecentClosedState), """{"version":1,"items":[{"id":"abcdefab-1234-1234-1234-abcdefabcdef","kind":"session","title":"<closed>","closedAt":123.5,"session":{"workspaceID":"abcdefab-1234-1234-1234-abcdefabcdef","workspaceName":"work","workspaceIndex":0,"sessionIndex":0,"snapshot":{"id":"abcdefab-1234-1234-1234-abcdefabcdef","cwd":"C:\\work"}}}]}""" },
        { typeof(RecentClosedState), "{}" },
        { typeof(RecentClosedState), """{"items":null}""" },
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public void ExistingRootsUseGeneratedMetadataAndMatchReflection(Type type, string json)
    {
        var reflection = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        var expected = JsonSerializer.Serialize(JsonSerializer.Deserialize(json, type, reflection), type, reflection);
        var value = JsonSerializer.Deserialize(json, type, ControlJson.Options);

        Assert.Equal(expected, JsonSerializer.Serialize(value, type, ControlJson.Options));
        Assert.IsAssignableFrom<JsonSerializerContext>(ControlJson.Options.GetTypeInfo(type).OriginatingResolver);
    }

    [Fact]
    public void AbsentClosedAtKeepsTheCreationTimeDefault()
    {
        var before = DateTimeOffset.UtcNow;
        var item = ControlJson.Deserialize<RecentClosedItem>(
            """{"id":"abcdefab-1234-1234-1234-abcdefabcdef","kind":"session","title":"closed"}""")!;

        Assert.InRange(item.ClosedAt, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void AppSpecificRootsRemainSupportedOutsideNativeAot()
    {
        var document = new PrivateDocument { Name = "<window>", Optional = null };

        Assert.Equal("""{"name":"<window>"}""", ControlJson.Serialize(document));
        Assert.Equal(document, ControlJson.Deserialize<PrivateDocument>("""{"name":"<window>"}"""));
    }

    private sealed record PrivateDocument
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("optional")]
        public string? Optional { get; init; }
    }
}
