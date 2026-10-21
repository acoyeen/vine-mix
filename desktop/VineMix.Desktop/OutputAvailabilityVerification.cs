using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace VineMix.Desktop;

// Pure fixtures: no model, UI, control pipe, endpoint enumeration or audio I/O.
internal static class OutputAvailabilityVerification
{
    internal static void Run(Action<bool, string> require)
    {
        static GraphNode Node(string kind = "output") => new()
        {
            Id = "output", Kind = kind, Name = "Saved output",
            Device = new() { Id = "saved", StableId = "stable", Name = "Saved device", Channel = -1 }
        };
        static Endpoint Device(string id = "saved", string state = "Active", string stable = "stable",
            string direction = "render", string transport = "physical") => new()
            { Id = id, StableId = stable, Name = "Same name", State = state, Direction = direction, Transport = transport, IsVirtual = transport != "physical" };
        static GraphNodeStatus Status(string state, string message = "") => new()
            { Id = "output", Path = new() { State = state, Message = message, EndpointId = "stale-former-output" }, Peak = 0, PeakLeft = 0, PeakRight = 0 };
        var observed = new List<AudioAvailability>();
        void Check(GraphNode node, GraphNodeStatus? status, Endpoint[] endpoints,
            bool connected, AvailabilityNoticeKind kind, string label, string reason)
        {
            string before = JsonSerializer.Serialize(new { node, status, endpoints }, Protocol.Json);
            var result = OutputAvailability.Evaluate(node, status, endpoints, connected);
            observed.Add(result);
            require(result.Kind == kind && result.Label == label, reason + $" Got {result.Kind}: {result.Label}.");
            require(JsonSerializer.Serialize(new { node, status, endpoints }, Protocol.Json) == before,
                reason + " Classification leaves assignments, mute, gain, routing and telemetry untouched.");
        }

        foreach (string kind in GraphDefaults.Kinds.Where(kind => kind is not ("output" or "virtualOutput")))
            Check(Node(kind), Status("Waiting"), [], false, AvailabilityNoticeKind.None, "",
                kind + " receives no output warning, even when the engine is offline.");

        foreach (string kind in new[] { "output", "virtualOutput" })
        {
            var node = Node(kind);
            string transport = kind == "virtualOutput" ? "vacLite" : "physical";
            foreach (string state in new[] { "Disabled", "Unplugged", "Missing" })
            {
                foreach (bool muted in new[] { false, true })
                {
                    node.Muted = muted;
                    Check(node, Status("Waiting"), [Device(state: state, transport: transport)], true,
                        AvailabilityNoticeKind.Problem, "OUTPUT " + state.ToUpperInvariant(),
                        kind + " reports its selected endpoint's " + state + " state even when muted=" + muted + ".");
                }
            }
            node.Muted = false;
            Check(node, Status("Waiting"), [], true, AvailabilityNoticeKind.Problem, "OUTPUT MISSING",
                kind + " with an absent saved endpoint is missing despite a stale path endpoint ID.");
            Check(node, Status("Waiting", "Device disabled in Windows Sound settings"), [], true,
                AvailabilityNoticeKind.Problem, "OUTPUT DISABLED", kind + " preserves the worker's explicit disabled diagnosis.");
            Check(node, Status("Waiting", "Device disabled in Windows Sound settings"), [Device(transport: transport)], true,
                AvailabilityNoticeKind.Problem, "OUTPUT DISABLED", kind + " does not erase a worker-disabled result with stale active inventory.");

            Check(node, Status("Running"), [], true, AvailabilityNoticeKind.None, "",
                kind + " with a silent running stream stays healthy even if inventory has not caught up.");
            Check(node, Status("Running"), [Device(state: "Disabled", transport: transport)], true, AvailabilityNoticeKind.None, "",
                kind + " running telemetry outranks stale disabled inventory.");
            var signal = Status("Running"); signal.Peak = .8f; signal.PeakLeft = .8f; signal.PeakRight = .8f;
            Check(node, signal, [], true, AvailabilityNoticeKind.None, "", kind + " availability does not depend on signal level.");
            node.Muted = true;
            Check(node, Status("Running"), [], true, AvailabilityNoticeKind.Muted, "MUTED", kind + " healthy mute is intentional.");
            Check(node, Status("Waiting"), [], true, AvailabilityNoticeKind.Problem, "OUTPUT MISSING", kind + " missing assignment still warns while muted.");
            Check(node, Status("Running"), [], false, AvailabilityNoticeKind.Problem, "ENGINE OFFLINE", kind + " disconnected engine cannot promise current device availability.");
            foreach (string state in new[] { "Resolving", "Opening", "Priming", "Unassigned" })
                Check(node, Status(state), [Device(state: "Disabled", transport: transport)], true,
                    AvailabilityNoticeKind.Pending, "CONNECTING", kind + " " + state + " remains transitional before a definitive worker result.");
            Check(node, Status("Recovering"), [], true, AvailabilityNoticeKind.Pending, "RECONNECTING", kind + " active recovery does not invent a confirmed missing diagnosis.");
            Check(node, null, [], true, AvailabilityNoticeKind.Pending, "CHECKING OUTPUT", kind + " without telemetry waits for status.");
            Check(node, Status("NewProtocolState"), [], true, AvailabilityNoticeKind.Pending, "CHECKING OUTPUT", kind + " unknown status does not invent a device failure.");
            Check(node, Status("Waiting"), [Device(transport: transport)], true, AvailabilityNoticeKind.Pending, "RECONNECTING", kind + " detected endpoint waiting for playback is not missing.");

            foreach (var error in new[]
            {
                (State: "AccessDenied", Label: "ACCESS BLOCKED"), (State: "Busy", Label: "OUTPUT BUSY"),
                (State: "ServiceUnavailable", Label: "AUDIO SERVICE OFFLINE"), (State: "Error", Label: "OUTPUT ERROR"),
                (State: "Unsupported", Label: "OUTPUT UNAVAILABLE")
            })
                Check(node, Status(error.State, "Specific native recovery guidance"), [], true,
                    AvailabilityNoticeKind.Problem, error.Label, kind + " " + error.State + " remains distinct from hardware absence, including when muted.");
            Check(node, Status("Error", "Select the VAC Lite send endpoint in setup"), [], true,
                AvailabilityNoticeKind.Problem, "OUTPUT ERROR", "A wrong transport is an output setup error rather than a missing driver.");

            // Recovery clears presentation without altering the saved assignment.
            Check(node, Status("Waiting"), [], true, AvailabilityNoticeKind.Problem, "OUTPUT MISSING", kind + " removal raises the warning.");
            Check(node, Status("Waiting"), [Device(transport: transport)], true, AvailabilityNoticeKind.Pending, "RECONNECTING", kind + " reappearance clears the missing warning while playback opens.");
            Check(node, Status("Running"), [], true, AvailabilityNoticeKind.Muted, "MUTED", kind + " reconnect returns to intentional mute despite stale inventory.");
            node.Muted = false;
            Check(node, Status("Running"), [], true, AvailabilityNoticeKind.None, "", kind + " unmuting a recovered stream returns to healthy presentation.");
        }

        var output = Node();
        Check(output, Status("Waiting"), [Device(direction: "capture", state: "Disabled")], true,
            AvailabilityNoticeKind.Problem, "OUTPUT MISSING", "A same-ID capture counterpart cannot diagnose a render output as disabled.");
        Check(output, Status("Waiting"), [Device(id: "unrelated", stable: "other", state: "Disabled")], true,
            AvailabilityNoticeKind.Problem, "OUTPUT MISSING", "An unrelated output with the same name is never selected by display name.");
        Check(output, Status("Waiting"), [Device(state: "Unknown")], true,
            AvailabilityNoticeKind.Problem, "OUTPUT UNAVAILABLE", "An unknown endpoint state does not invent a disabled diagnosis.");
        Check(output, Status("Waiting"), [Device(state: "Disabled"), Device(id: "replacement")], true,
            AvailabilityNoticeKind.Pending, "RECONNECTING", "A unique active stable-ID replacement wins over the disabled former ID.");
        Check(output, Status("Waiting"), [Device(id: "replacement")], true,
            AvailabilityNoticeKind.Pending, "RECONNECTING", "A unique active stable-ID replacement resolves an absent former ID.");
        Check(output, Status("Waiting"), [Device(), Device(id: "alias-one"), Device(id: "alias-two")], true,
            AvailabilityNoticeKind.Pending, "RECONNECTING", "An exact active render ID wins before stable-ID ambiguity, matching the native resolver.");
        Check(output, Status("Waiting"), [Device(state: "Disabled"), Device(id: "alias-one"), Device(id: "alias-two")], true,
            AvailabilityNoticeKind.Setup, "CHOOSE OUTPUT DEVICE", "Ambiguous active stable IDs need reassignment instead of a false disabled warning.");
        Check(output, Status("Waiting"), [Device(id: "alias-one"), Device(id: "alias-two")], true,
            AvailabilityNoticeKind.Setup, "CHOOSE OUTPUT DEVICE", "Ambiguous active stable IDs do not become a false missing warning.");
        var stableOnly = output.Copy(); stableOnly.Device.Id = "";
        Check(stableOnly, Status("Waiting"), [Device(id: "alias", state: "Disabled")], true,
            AvailabilityNoticeKind.Problem, "OUTPUT MISSING", "An inactive stable-only alias is not guessed as the native resolver's selected device.");
        Check(stableOnly, Status("Waiting"), [Device(id: "alias")], true,
            AvailabilityNoticeKind.Pending, "RECONNECTING", "A stable-only assignment resolves one active render alias.");
        Check(stableOnly, Status("Waiting"), [Device(id: "alias", direction: "capture")], true,
            AvailabilityNoticeKind.Problem, "OUTPUT MISSING", "Stable identity cannot resolve a capture counterpart for a render output.");

        var unassigned = Node(); unassigned.Device = new();
        Check(unassigned, Status("Unassigned"), [], true, AvailabilityNoticeKind.Setup, "CHOOSE OUTPUT DEVICE", "An unused physical output prompts selection without claiming missing hardware.");
        Check(unassigned, Status("Unassigned"), [Device(state: "Disabled")], true, AvailabilityNoticeKind.Setup, "CHOOSE OUTPUT DEVICE", "An unselected output does not adopt an arbitrary detected device's state.");
        Check(unassigned, Status("Unassigned"), [], false, AvailabilityNoticeKind.Problem, "ENGINE OFFLINE", "An offline engine is reported even while an output awaits assignment.");

        var cable = Node("virtualOutput"); cable.Device = new(); cable.Muted = true;
        Check(cable, Status("Unassigned"), [], true, AvailabilityNoticeKind.Problem, "VAC LITE NOT DETECTED", "An unassigned virtual output reports absent VAC Lite even when muted, without claiming it is uninstalled.");
        Check(cable, Status("Unassigned"), [Device(transport: "vacLite", direction: "capture"), Device(transport: "vbCable"), Device()], true,
            AvailabilityNoticeKind.Problem, "VAC LITE NOT DETECTED", "VAC Lite capture, VB-CABLE and physical render endpoints are not supported virtual send candidates.");
        foreach (var state in new[] { ("Disabled", "VAC LITE DISABLED"), ("Unplugged", "VAC LITE UNPLUGGED"), ("Missing", "VAC LITE NOT DETECTED") })
            Check(cable, Status("Unassigned"), [Device(state: state.Item1, transport: "vacLite")], true,
                AvailabilityNoticeKind.Problem, state.Item2, "A sole known VAC Lite send endpoint retains its " + state.Item1 + " diagnosis.");
        Check(cable, Status("Unassigned"), [Device(transport: "vacLite")], true, AvailabilityNoticeKind.Setup, "CHOOSE VAC LITE", "A detected virtual output offers an explicit selection action.");
        Check(cable, Status("Unassigned"), [Device(transport: "vacLite"), Device(id: "second", stable: "second", transport: "vacLite")], true,
            AvailabilityNoticeKind.Setup, "CHOOSE VAC LITE", "Several active virtual sends require selection rather than being called missing.");
        Check(cable, Status("Unassigned"), [Device(state: "Disabled", transport: "vacLite"), Device(id: "second", state: "Unplugged", transport: "vacLite")], true,
            AvailabilityNoticeKind.Problem, "VAC LITE UNAVAILABLE", "Mixed inactive virtual candidates do not invent one specific failure cause.");
        Check(cable, Status("Unassigned"), [Device(id: "", stable: "", transport: "vacLite")], true,
            AvailabilityNoticeKind.Problem, "VAC LITE UNAVAILABLE", "An anonymous endpoint cannot promise a usable virtual assignment.");

        var explained = OutputAvailability.Evaluate(output, Status("AccessDenied", "Microphone access blocked; check Windows microphone privacy settings"), [], true);
        require(explained.Detail == "Check Windows sound device settings", "Render access failure uses output-specific guidance despite the native worker's legacy microphone wording.");
        require(observed.All(value => value.Label.Length <= 24 && value.Label == value.Label.ToUpperInvariant() && value.Detail.Length <= 40
            && (value.Kind == AvailabilityNoticeKind.None ? value.Label.Length == 0 && value.Detail.Length == 0 : !string.IsNullOrWhiteSpace(value.Detail))),
            "Every output notice has a short uppercase label and at most 40 characters of actionable detail; healthy outputs have no warning text.");
    }
}
