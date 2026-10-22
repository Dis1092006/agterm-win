using System.Text.Json.Serialization;
using AgTerm.Core.Model;

namespace AgTerm.Core.Protocol;

// Non-required properties with initializers use set, not init: generated init-only constructors
// assign CLR defaults for absent fields, overwriting initializers such as HUD Frame = true.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ControlRequest))]
[JsonSerializable(typeof(ControlResponse))]
[JsonSerializable(typeof(ControlPickItem[]))]
[JsonSerializable(typeof(ControlEvent[]))]
[JsonSerializable(typeof(Snapshot))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(RecentClosedState))]
[JsonSerializable(typeof(BackgroundWatermark))]
[JsonSerializable(typeof(AgentStatus))]
[JsonSerializable(typeof(string))]
internal partial class ControlJsonContext : JsonSerializerContext;
