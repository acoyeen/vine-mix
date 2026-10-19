using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioMixer.Desktop;

internal enum InputNoticeKind { None, Muted, Setup, Pending, Problem }

// Presentation only: never changes an assignment, retries a stream, or infers
// availability from signal level. Inventory may lag the worker's path status.
internal sealed record InputAvailability(InputNoticeKind Kind, string Label, string Detail)
{
    private static readonly InputAvailability Available = new(InputNoticeKind.None, "", "");

    internal static InputAvailability Evaluate(GraphNode node, GraphNodeStatus? status,
        IReadOnlyList<Endpoint> endpoints, IReadOnlyList<AudioApplication> applications, bool engineAvailable)
    {
        if (node.Kind is not ("microphone" or "virtualInput" or "appInput")) return Available;
        if (!engineAvailable)
            return new(InputNoticeKind.Problem, "ENGINE OFFLINE", "Waiting for the audio engine");

        bool app = node.Kind == "appInput";
        bool assigned = app ? node.App.ExecutablePath.Length > 0 : VirtualDeviceBindings.IsAssigned(node.Device);
        if (!assigned)
        {
            if (app) return new(InputNoticeKind.Setup, "CHOOSE AN APP", "Select an application below");
            if (node.Kind == "virtualInput") return UnassignedVirtual(endpoints);
            return new(InputNoticeKind.Setup, "CHOOSE MICROPHONE", "Connect a microphone, then select it");
        }

        var path = status?.Path;
        string state = path?.State ?? "";
        // A working stream is stronger evidence than the periodically refreshed
        // chooser inventory, including an app with no visible window/session.
        if (state == "Running")
            return node.Muted ? new(InputNoticeKind.Muted, "MUTED", "Unmute this input to pass audio") : Available;

        switch (state)
        {
            case "AccessDenied":
                return new(InputNoticeKind.Problem, "ACCESS BLOCKED", "Check Windows sound and privacy settings");
            case "Busy":
                return new(InputNoticeKind.Problem, "INPUT BUSY", "Close other capture apps, then retry");
            case "ServiceUnavailable":
                return new(InputNoticeKind.Problem, "AUDIO SERVICE OFFLINE", "Check the Windows Audio service");
            case "Unsupported":
                return new(InputNoticeKind.Problem, app ? "APP CAPTURE UNAVAILABLE" : "INPUT UNAVAILABLE",
                    "Choose another capture method");
            case "Error":
                // App capture currently reports these reasons as Error rather
                // than separate typed states. Use only its known exact reasons.
                if (app && path?.Message == "Windows denied app audio capture")
                    return new(InputNoticeKind.Problem, "ACCESS BLOCKED", "Check app access, then retry");
                if (app && path?.Message == "Windows Audio service unavailable")
                    return new(InputNoticeKind.Problem, "AUDIO SERVICE OFFLINE", "Check the Windows Audio service");
                return new(InputNoticeKind.Problem, app ? "APP CAPTURE ERROR" : "INPUT ERROR",
                    "Check this input; retry assigned devices");
            case "Recovering":
                return new(InputNoticeKind.Pending, "RECONNECTING", "Waiting for this input to reconnect");
            case "Resolving":
            case "Opening":
            case "Priming":
            case "Unassigned": // A new optimistic assignment can precede its worker.
                return new(InputNoticeKind.Pending, "CONNECTING", "Waiting for the input to connect");
            case "Waiting":
                return app ? WaitingForApp(node, path, applications) : WaitingForDevice(node, path, endpoints);
            default:
                return new(InputNoticeKind.Pending, "CHECKING INPUT", "Waiting for input status");
        }
    }

    private static InputAvailability WaitingForDevice(GraphNode node, PathState? path, IReadOnlyList<Endpoint> endpoints)
    {
        // The worker can observe disablement before the UI inventory refresh.
        if (path?.Message == "Device disabled in Windows Sound settings")
            return new(InputNoticeKind.Problem, "INPUT DISABLED", "Enable in Windows sound settings");
        // Mirror AudioWorker::resolve: exact active ID, then one unique active
        // stable-ID match, then the exact inactive endpoint. Never choose an
        // arbitrary alias or use a stale Path.EndpointId from a former stream.
        var exact = endpoints.FirstOrDefault(endpoint => endpoint.Direction == "capture"
            && node.Device.Id.Length > 0 && endpoint.Id == node.Device.Id);
        if (exact?.State == "Active") return WaitingForAvailableDevice();
        if (node.Device.StableId.Length > 0)
        {
            var active = endpoints.Where(endpoint => endpoint.Direction == "capture" && endpoint.State == "Active"
                && endpoint.StableId == node.Device.StableId).Take(2).ToArray();
            if (active.Length > 1)
                return new(InputNoticeKind.Setup, "CHOOSE INPUT DEVICE", "Select the intended input again");
            if (active.Length == 1) return WaitingForAvailableDevice();
        }
        return exact?.State switch
        {
            "Disabled" => new(InputNoticeKind.Problem, "INPUT DISABLED", "Enable in Windows sound settings"),
            "Unplugged" => new(InputNoticeKind.Problem, "INPUT UNPLUGGED", "Reconnect or choose another input"),
            null or "Missing" => new(InputNoticeKind.Problem, "INPUT MISSING", "Reconnect or choose another input"),
            _ => new(InputNoticeKind.Problem, "INPUT UNAVAILABLE", "Check Windows sound settings")
        };
    }

    private static InputAvailability WaitingForAvailableDevice()
        => new(InputNoticeKind.Pending, "RECONNECTING", "Device detected; waiting for capture");

    private static InputAvailability WaitingForApp(GraphNode node, PathState? path, IReadOnlyList<AudioApplication> applications)
    {
        bool ambiguous = path?.Message == "Several instances are running; select the app again";
        // Empty legacy/test reasons can use the same identity-safe chooser rule.
        // An explicit worker reason wins over the less complete app inventory.
        if (string.IsNullOrWhiteSpace(path?.Message))
        {
            bool SamePath(AudioApplication application) => string.Equals(application.ExecutablePath,
                node.App.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            bool exact = node.App.ProcessId != 0 && applications.Any(application => application.ProcessId == node.App.ProcessId && SamePath(application));
            ambiguous = !exact && applications.Where(SamePath).Select(application => application.ProcessId).Distinct().Take(2).Count() > 1;
        }
        return ambiguous
            ? new(InputNoticeKind.Setup, "CHOOSE APP INSTANCE", "Select the intended application below")
            : new(InputNoticeKind.Problem, "APP NOT DETECTED", "Open the app or select another below");
    }

    private static InputAvailability UnassignedVirtual(IReadOnlyList<Endpoint> endpoints)
    {
        var matches = endpoints.Where(endpoint => VirtualDeviceBindings.Matches("virtualInput", endpoint)).ToArray();
        if (matches.Length == 0)
            return new(InputNoticeKind.Problem, "VB-CABLE NOT DETECTED", "Check VB-CABLE in Windows sound settings");
        if (matches.Any(endpoint => endpoint.State == "Active" && (endpoint.Id.Length > 0 || endpoint.StableId.Length > 0)))
            return new(InputNoticeKind.Setup, "CHOOSE VB-CABLE", "Right-click → Troubleshooting");
        if (matches.Length == 1)
        {
            if (matches[0].State == "Disabled")
                return new(InputNoticeKind.Problem, "VB-CABLE DISABLED", "Enable VB-CABLE in sound settings");
            if (matches[0].State == "Unplugged")
                return new(InputNoticeKind.Problem, "VB-CABLE UNPLUGGED", "Check VB-CABLE in Windows sound settings");
            if (matches[0].State == "Missing")
                return new(InputNoticeKind.Problem, "VB-CABLE NOT DETECTED", "Check VB-CABLE in Windows sound settings");
        }
        return new(InputNoticeKind.Problem, "VB-CABLE UNAVAILABLE", "Check VB-CABLE in Windows sound settings");
    }

}
