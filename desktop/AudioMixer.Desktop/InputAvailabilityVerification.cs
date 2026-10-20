using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AudioMixer.Desktop;

// Pure fixtures only: no model, UI, control pipe, process inventory or audio I/O.
internal static class InputAvailabilityVerification
{
    internal static void Run(Action<bool, string> require)
    {
        static GraphNode Node(string kind) => new()
        {
            Id = "input", Kind = kind, Name = "Saved input",
            Device = new() { Id = "saved", StableId = "stable", Name = "Saved device", Channel = -1 },
            App = new() { ProcessId = 12, ExecutablePath = @"C:\Apps\player.exe", Name = "Player" }
        };
        static Endpoint Device(string id = "saved", string state = "Active", string stable = "stable",
            string direction = "capture", string transport = "physical") => new()
            { Id = id, StableId = stable, Name = "Same name", State = state, Direction = direction, Transport = transport, IsVirtual = transport != "physical" };
        static GraphNodeStatus Status(string state, string message = "") => new()
            { Id = "input", Path = new() { State = state, Message = message, EndpointId = "stale-former-device" }, Peak = 0, PeakLeft = 0, PeakRight = 0 };
        var observed = new List<AudioAvailability>();
        void Check(GraphNode node, GraphNodeStatus? status, Endpoint[] endpoints, AudioApplication[] apps,
            bool connected, AvailabilityNoticeKind kind, string label, string reason)
        {
            string before = JsonSerializer.Serialize(new { node, status, endpoints, apps }, Protocol.Json);
            var result = InputAvailability.Evaluate(node, status, endpoints, apps, connected);
            observed.Add(result);
            require(result.Kind == kind && result.Label == label, reason + $" Got {result.Kind}: {result.Label}.");
            require(JsonSerializer.Serialize(new { node, status, endpoints, apps }, Protocol.Json) == before,
                reason + " Classification leaves configuration and telemetry untouched.");
        }

        foreach (string kind in GraphDefaults.Kinds.Where(kind => kind is not ("microphone" or "virtualInput" or "appInput")))
            Check(Node(kind), Status("Waiting"), [], [], false, AvailabilityNoticeKind.None, "", kind + " receives no input warning, even when the engine is offline.");

        var mic = Node("microphone");
        foreach (string state in new[] { "Disabled", "Unplugged", "Missing" })
        {
            string label = "INPUT " + state.ToUpperInvariant();
            Check(mic, Status("Waiting"), [Device(state: state)], [], true, AvailabilityNoticeKind.Problem, label,
                "The exact selected capture endpoint reports " + state + " distinctly.");
            mic.Muted = true;
            Check(mic, Status("Waiting"), [Device(state: state)], [], true, AvailabilityNoticeKind.Problem, label,
                state + " remains visible when that input is also muted.");
            mic.Muted = false;
        }
        Check(mic, Status("Waiting"), [], [], true, AvailabilityNoticeKind.Problem, "INPUT MISSING", "An absent saved input is missing, regardless of a stale path endpoint ID.");
        Check(mic, Status("Waiting", "Device disabled in Windows Sound settings"), [], [], true,
            AvailabilityNoticeKind.Problem, "INPUT DISABLED", "The worker's explicit disabled result wins before the UI inventory catches up.");
        Check(mic, Status("Waiting", "Device disabled in Windows Sound settings"), [Device()], [], true,
            AvailabilityNoticeKind.Problem, "INPUT DISABLED", "An old active inventory entry cannot erase a directly reported disabled result.");
        Check(mic, Status("Waiting"), [Device(direction: "render", state: "Disabled")], [], true,
            AvailabilityNoticeKind.Problem, "INPUT MISSING", "A same-ID render endpoint cannot label a capture input disabled.");
        Check(mic, Status("Waiting"), [Device(id: "unrelated", stable: "unrelated", state: "Disabled")], [], true,
            AvailabilityNoticeKind.Problem, "INPUT MISSING", "An unrelated endpoint with the same display name cannot supply its disabled state.");
        Check(mic, Status("Waiting"), [Device(state: "Unknown")], [], true,
            AvailabilityNoticeKind.Problem, "INPUT UNAVAILABLE", "An unknown endpoint state does not become a fabricated disabled diagnosis.");
        Check(mic, Status("Waiting"), [Device()], [], true, AvailabilityNoticeKind.Pending, "RECONNECTING", "A detected active endpoint awaiting capture is not missing.");

        var aliases = new[] { Device(state: "Disabled"), Device(id: "replacement") };
        Check(mic, Status("Waiting"), aliases, [], true, AvailabilityNoticeKind.Pending, "RECONNECTING", "One active stable-ID replacement takes precedence over the disabled old ID.");
        Check(mic, Status("Waiting"), [Device(id: "replacement")], [], true,
            AvailabilityNoticeKind.Pending, "RECONNECTING", "A unique active stable-ID replacement resolves a missing old ID.");
        Check(mic, Status("Waiting"), [Device(), Device(id: "alias-one"), Device(id: "alias-two")], [], true,
            AvailabilityNoticeKind.Pending, "RECONNECTING", "The exact active ID wins before stable-ID ambiguity, matching the native resolver.");
        Check(mic, Status("Waiting"), [Device(state: "Disabled"), Device(id: "alias-one"), Device(id: "alias-two")], [], true,
            AvailabilityNoticeKind.Setup, "CHOOSE INPUT DEVICE", "Multiple active stable-ID replacements require a choice instead of a false disabled or missing warning.");
        var stableOnly = mic.Copy(); stableOnly.Device.Id = "";
        Check(stableOnly, Status("Waiting"), [Device(id: "alias", state: "Disabled")], [], true,
            AvailabilityNoticeKind.Problem, "INPUT MISSING", "An inactive stable-only alias is not guessed as the endpoint selected by the native resolver.");
        Check(stableOnly, Status("Waiting"), [Device(id: "alias")], [], true,
            AvailabilityNoticeKind.Pending, "RECONNECTING", "A stable-only assignment resolves one active capture alias.");

        foreach (string kind in new[] { "microphone", "virtualInput", "appInput" })
        {
            var node = Node(kind);
            Check(node, Status("Running"), [], [], true, AvailabilityNoticeKind.None, "", kind + " with a silent working stream stays healthy despite empty inventory.");
            Check(node, Status("Running"), [Device(state: "Disabled")], [], true, AvailabilityNoticeKind.None, "", kind + " working telemetry outranks stale disabled inventory.");
            node.Muted = true;
            Check(node, Status("Running"), [], [], true, AvailabilityNoticeKind.Muted, "MUTED", kind + " connected mute is intentional and distinct from device failure.");
            Check(node, Status("Running"), [], [], false, AvailabilityNoticeKind.Problem, "ENGINE OFFLINE", kind + " has unknown current availability when the engine is offline.");
            foreach (string state in new[] { "Resolving", "Opening", "Priming", "Unassigned" })
                Check(node, Status(state), [Device(state: "Disabled")], [], true,
                    AvailabilityNoticeKind.Pending, "CONNECTING", kind + " " + state + " remains transitional before a definitive worker result.");
            Check(node, null, [], [], true, AvailabilityNoticeKind.Pending, "CHECKING INPUT", kind + " without telemetry is checking rather than missing.");
            Check(node, Status("NewProtocolState"), [], [], true, AvailabilityNoticeKind.Pending, "CHECKING INPUT", kind + " unknown path state does not invent a device diagnosis.");
        }
        foreach (var error in new[]
        {
            (State: "AccessDenied", Label: "ACCESS BLOCKED"), (State: "Busy", Label: "INPUT BUSY"),
            (State: "ServiceUnavailable", Label: "AUDIO SERVICE OFFLINE"), (State: "Error", Label: "INPUT ERROR"),
            (State: "Unsupported", Label: "INPUT UNAVAILABLE")
        })
            Check(mic, Status(error.State, "Specific native recovery guidance"), [], [], true,
                AvailabilityNoticeKind.Problem, error.Label, error.State + " remains distinguishable from missing hardware.");
        var explained = InputAvailability.Evaluate(mic, Status("Error", "Specific native recovery guidance"), [], [], true);
        require(explained.Detail == "Check this input; retry assigned devices",
            "The visible recovery detail stays compact; the presentation keeps raw native guidance separately in its tooltip.");
        Check(mic, Status("Recovering"), [], [], true, AvailabilityNoticeKind.Pending, "RECONNECTING", "Worker recovery is distinct from confirmed missing hardware.");
        var unassignedMic = Node("microphone"); unassignedMic.Device = new();
        Check(unassignedMic, Status("Unassigned"), [Device(state: "Disabled")], [], true,
            AvailabilityNoticeKind.Setup, "CHOOSE MICROPHONE", "An unselected microphone does not adopt the state of an arbitrary detected device.");
        Check(unassignedMic, Status("Unassigned"), [], [], false, AvailabilityNoticeKind.Problem, "ENGINE OFFLINE",
            "An offline engine is reported even for a source awaiting assignment.");

        var app = Node("appInput");
        var unassignedApp = app.Copy(); unassignedApp.App.ExecutablePath = "";
        Check(unassignedApp, Status("Unassigned"), [], [], true, AvailabilityNoticeKind.Setup, "CHOOSE AN APP", "A PID without executable identity does not make an app input assigned.");
        Check(app, Status("Waiting", "Waiting for application"), [], [], true, AvailabilityNoticeKind.Problem, "APP NOT DETECTED", "Waiting capture describes detection, without claiming the app has stopped.");
        app.Muted = true;
        Check(app, Status("Waiting", "Waiting for application"), [], [], true, AvailabilityNoticeKind.Problem, "APP NOT DETECTED", "App detection failure remains visible when its node is also muted.");
        app.Muted = false;
        var instances = new[]
        {
            new AudioApplication { ProcessId = 20, ExecutablePath = app.App.ExecutablePath.ToUpperInvariant() },
            new AudioApplication { ProcessId = 21, ExecutablePath = app.App.ExecutablePath },
            new AudioApplication { ProcessId = app.App.ProcessId, ExecutablePath = @"C:\Other\player.exe" }
        };
        Check(app, Status("Waiting", "Several instances are running; select the app again"), [], [], true,
            AvailabilityNoticeKind.Setup, "CHOOSE APP INSTANCE", "The worker's ambiguity reason is sufficient even when chooser inventory lags.");
        Check(app, Status("Waiting"), [], instances, true, AvailabilityNoticeKind.Setup, "CHOOSE APP INSTANCE", "Legacy empty reasons use executable identity and reject recycled PIDs when detecting ambiguity.");
        Check(app, Status("Waiting", "Waiting for application"), [], instances, true,
            AvailabilityNoticeKind.Problem, "APP NOT DETECTED", "A specific worker waiting reason takes precedence over stale chooser ambiguity.");
        Check(app, Status("Running"), [], instances, true, AvailabilityNoticeKind.None, "", "Chooser ambiguity cannot override a working app capture.");
        var exactApp = instances.Append(new AudioApplication { ProcessId = app.App.ProcessId, ExecutablePath = app.App.ExecutablePath }).ToArray();
        Check(app, Status("Waiting"), [], exactApp, true, AvailabilityNoticeKind.Problem, "APP NOT DETECTED", "An exact PID and executable match prevents a false ambiguous-app diagnosis.");
        Check(app, Status("Error", "Windows denied app audio capture"), [], [], true, AvailabilityNoticeKind.Problem, "ACCESS BLOCKED", "The app worker's exact access-denied reason receives an access label.");
        Check(app, Status("Error", "Windows Audio service unavailable"), [], [], true, AvailabilityNoticeKind.Problem, "AUDIO SERVICE OFFLINE", "The app worker's exact service failure stays distinct from app absence.");
        Check(app, Status("Error", "App capture unavailable (0x80000000)"), [], [], true, AvailabilityNoticeKind.Problem, "APP CAPTURE ERROR", "Unknown app capture failures are not misreported as stopped or disabled apps.");
        Check(app, Status("Unsupported"), [], [], true, AvailabilityNoticeKind.Problem, "APP CAPTURE UNAVAILABLE", "Unsupported app capture is a capability problem.");

        var cable = Node("virtualInput"); cable.Device = new();
        Check(cable, Status("Unassigned"), [], [], true, AvailabilityNoticeKind.Problem, "VB-CABLE NOT DETECTED", "An unassigned virtual input identifies the undetected transport without claiming it is uninstalled.");
        Check(cable, Status("Unassigned"), [Device(transport: "vbCable", direction: "render"), Device()], [], true,
            AvailabilityNoticeKind.Problem, "VB-CABLE NOT DETECTED", "VB-CABLE playback and ordinary microphones are not virtual receive candidates.");
        foreach (var state in new[] { ("Disabled", "VB-CABLE DISABLED"), ("Unplugged", "VB-CABLE UNPLUGGED"), ("Missing", "VB-CABLE NOT DETECTED") })
            Check(cable, Status("Unassigned"), [Device(state: state.Item1, transport: "vbCable")], [], true,
                AvailabilityNoticeKind.Problem, state.Item2, "A sole known VB-CABLE endpoint retains its " + state.Item1 + " reason.");
        Check(cable, Status("Unassigned"), [Device(transport: "vbCable")], [], true, AvailabilityNoticeKind.Setup, "CHOOSE VB-CABLE", "A detected virtual input has an explicit selection action.");
        Check(cable, Status("Unassigned"), [Device(transport: "vbCable"), Device(id: "second", stable: "second", transport: "vbCable")], [], true,
            AvailabilityNoticeKind.Setup, "CHOOSE VB-CABLE", "Several active virtual receive devices require selection rather than being called missing.");
        Check(cable, Status("Unassigned"), [Device(state: "Disabled", transport: "vbCable"), Device(id: "second", state: "Unplugged", transport: "vbCable")], [], true,
            AvailabilityNoticeKind.Problem, "VB-CABLE UNAVAILABLE", "Mixed inactive virtual candidates do not invent one specific failure cause.");
        Check(cable, Status("Unassigned"), [Device(id: "", stable: "", transport: "vbCable")], [], true,
            AvailabilityNoticeKind.Problem, "VB-CABLE UNAVAILABLE", "An anonymous endpoint cannot promise a usable virtual assignment.");
        require(observed.All(value => value.Label.Length <= 24 && value.Label == value.Label.ToUpperInvariant() && value.Detail.Length <= 40
            && (value.Kind == AvailabilityNoticeKind.None ? value.Label.Length == 0 && value.Detail.Length == 0 : !string.IsNullOrWhiteSpace(value.Detail))),
            "Every input notice has a short uppercase label and at most 40 characters of actionable detail; healthy nodes have no warning text.");
    }
}
