using System;
using System.Text.Json;

namespace VineMix.Desktop;

internal static class StatusReasonVerification
{
    internal static void Run(Action<bool, string> require)
    {
        var node = new GraphNode { Kind = "appInput", App = new() { ExecutablePath = "example.exe" } };
        var status = new GraphNodeStatus { Path = new() { State = "Error", Reason = "access_denied", Message = "Reworded diagnostic" } };
        require(InputAvailability.Evaluate(node, status, [], [], true).Label == "ACCESS BLOCKED",
            "App capture access behaviour uses a stable reason independent of diagnostic wording.");
        status.Path.Reason = "audio_service_unavailable";
        require(InputAvailability.Evaluate(node, status, [], [], true).Label == "AUDIO SERVICE OFFLINE",
            "Audio-service recovery guidance uses its stable reason.");
        status.Path.State = "Waiting"; status.Path.Reason = "app_instance_ambiguous";
        require(InputAvailability.Evaluate(node, status, [], [], true).Label == "CHOOSE APP INSTANCE",
            "An ambiguous app reason selects the instance chooser without text matching.");
        status.Path.Reason = "future_reason"; status.Path.Message = "Several instances are running; select the app again";
        require(InputAvailability.Evaluate(node, status, [], [], true).Label != "CHOOSE APP INSTANCE",
            "Unknown explicit reason codes never fall back to misleading legacy wording.");
        status.Path.Reason = "";
        require(InputAvailability.Evaluate(node, status, [], [], true).Label == "CHOOSE APP INSTANCE",
            "An older engine's exact ambiguity diagnostic remains supported in one compatibility adapter.");
        status.Path = new() { State = "Running", Reason = "", Message = "" };
        require(InputAvailability.Evaluate(node, status, [], [], true).Kind == AvailabilityNoticeKind.None,
            "Recovery to Running clears obsolete problem presentation.");
        node.Kind = "output"; node.Device = new() { Id = "example-output" };
        status.Path = JsonSerializer.Deserialize<PathState>("""{"state":"Waiting","reason":"device_disabled","message":"Wording changed"}""", Protocol.Json)!;
        require(OutputAvailability.Evaluate(node, status, [], true).Label == "OUTPUT DISABLED",
            "Stable device reasons survive desktop deserialization and override stale endpoint inventory.");
        require(JsonSerializer.Deserialize<PathState>("""{"state":"Waiting"}""", Protocol.Json)!.Reason == "",
            "Legacy snapshots without reason codes continue to deserialize.");
        status.Path = new() { State = "AccessDenied", Reason = "access_denied", Message = "Check this example output's exclusive-access settings" };
        require(StatusReasons.Detail(node, status.Path) == status.Path.Message,
            "Typed output denials preserve their device-specific diagnostic guidance.");
        status.Path = new() { State = "AccessDenied", Message = "Microphone access blocked; check Windows microphone privacy settings" };
        require(StatusReasons.Detail(node, status.Path) == "Output access blocked; check Windows sound device settings",
            "Only the known legacy microphone diagnostic is corrected for output presentation.");
        require(StatusReasons.IsRevisionConflict(new() { ErrorCode = "revision_conflict", Error = "Different words" })
            && StatusReasons.IsRevisionConflict(new() { Error = "Configuration revision conflict. Refresh and retry your change." })
            && !StatusReasons.IsRevisionConflict(new() { Error = "The configuration revision cannot be advanced." })
            && !StatusReasons.IsRevisionConflict(new() { ErrorCode = "future_error", Error = "Configuration revision conflict. Refresh and retry your change." }),
            "Only typed conflicts or the exact legacy conflict retry; revision exhaustion and unknown failures stay permanent.");
    }
}
