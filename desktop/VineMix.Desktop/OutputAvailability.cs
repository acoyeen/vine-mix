using System.Collections.Generic;
using System.Linq;

namespace VineMix.Desktop;

// Presentation only: the worker owns device resolution and stream recovery.
// A silent or muted output is not evidence that its device is unavailable.
internal static class OutputAvailability
{
    private static readonly AudioAvailability Available = new(AvailabilityNoticeKind.None, "", "");

    internal static AudioAvailability Evaluate(GraphNode node, GraphNodeStatus? status,
        IReadOnlyList<Endpoint> endpoints, bool engineAvailable)
    {
        if (node.Kind is not ("output" or "virtualOutput")) return Available;
        if (!engineAvailable)
            return new(AvailabilityNoticeKind.Problem, "ENGINE OFFLINE", "Waiting for the audio engine");

        if (!VirtualDeviceBindings.IsAssigned(node.Device))
            return node.Kind == "virtualOutput" ? UnassignedVirtual(endpoints)
                : new(AvailabilityNoticeKind.Setup, "CHOOSE OUTPUT DEVICE", "Connect an output, then select it");

        var path = status?.Path;
        // Running telemetry is fresher than the periodically refreshed chooser.
        // Keep intentional muting separate from an actual device failure.
        if (path?.State == "Running")
            return node.Muted ? new(AvailabilityNoticeKind.Muted, "MUTED", "Unmute this output to pass audio") : Available;

        return path?.State switch
        {
            "AccessDenied" => new(AvailabilityNoticeKind.Problem, "ACCESS BLOCKED", "Check Windows sound device settings"),
            "Busy" => new(AvailabilityNoticeKind.Problem, "OUTPUT BUSY", "Close other playback apps, then retry"),
            "ServiceUnavailable" => new(AvailabilityNoticeKind.Problem, "AUDIO SERVICE OFFLINE", "Check the Windows Audio service"),
            "Unsupported" => new(AvailabilityNoticeKind.Problem, "OUTPUT UNAVAILABLE", "Check the output format and settings"),
            "Error" => new(AvailabilityNoticeKind.Problem, "OUTPUT ERROR", "Check output; retry assigned devices"),
            "Recovering" => new(AvailabilityNoticeKind.Pending, "RECONNECTING", "Waiting for this output to reconnect"),
            "Resolving" or "Opening" or "Priming" or "Unassigned"
                => new(AvailabilityNoticeKind.Pending, "CONNECTING", "Waiting for the output to connect"),
            "Waiting" => WaitingForDevice(node, path, endpoints),
            _ => new(AvailabilityNoticeKind.Pending, "CHECKING OUTPUT", "Waiting for output status")
        };
    }

    private static AudioAvailability WaitingForDevice(GraphNode node, PathState? path, IReadOnlyList<Endpoint> endpoints)
    {
        // The worker can report disablement before the next chooser refresh.
        if (path?.Message == "Device disabled in Windows Sound settings")
            return new(AvailabilityNoticeKind.Problem, "OUTPUT DISABLED", "Enable in Windows sound settings");

        // Mirror AudioWorker::resolve: exact active render ID, unique active
        // stable render ID, then the exact inactive endpoint. Never use the
        // display name, capture counterpart, or stale Path.EndpointId as identity.
        var exact = endpoints.FirstOrDefault(endpoint => endpoint.Direction == "render"
            && node.Device.Id.Length > 0 && endpoint.Id == node.Device.Id);
        if (exact?.State == "Active") return WaitingForAvailableDevice();
        if (node.Device.StableId.Length > 0)
        {
            var active = endpoints.Where(endpoint => endpoint.Direction == "render" && endpoint.State == "Active"
                && endpoint.StableId == node.Device.StableId).Take(2).ToArray();
            if (active.Length > 1)
                return new(AvailabilityNoticeKind.Setup, "CHOOSE OUTPUT DEVICE", "Select the intended output again");
            if (active.Length == 1) return WaitingForAvailableDevice();
        }
        return exact?.State switch
        {
            "Disabled" => new(AvailabilityNoticeKind.Problem, "OUTPUT DISABLED", "Enable in Windows sound settings"),
            "Unplugged" => new(AvailabilityNoticeKind.Problem, "OUTPUT UNPLUGGED", "Reconnect or choose another output"),
            null or "Missing" => new(AvailabilityNoticeKind.Problem, "OUTPUT MISSING", "Reconnect or choose another output"),
            _ => new(AvailabilityNoticeKind.Problem, "OUTPUT UNAVAILABLE", "Check Windows sound settings")
        };
    }

    private static AudioAvailability WaitingForAvailableDevice()
        => new(AvailabilityNoticeKind.Pending, "RECONNECTING", "Device detected; waiting for playback");

    private static AudioAvailability UnassignedVirtual(IReadOnlyList<Endpoint> endpoints)
    {
        var matches = endpoints.Where(endpoint => VirtualDeviceBindings.Matches("virtualOutput", endpoint)).ToArray();
        if (matches.Length == 0)
            return new(AvailabilityNoticeKind.Problem, "VAC LITE NOT DETECTED", "Check VAC Lite in Windows sound settings");
        if (matches.Any(endpoint => endpoint.State == "Active" && (endpoint.Id.Length > 0 || endpoint.StableId.Length > 0)))
            return new(AvailabilityNoticeKind.Setup, "CHOOSE VAC LITE", "Virtual output needs assignment");
        if (matches.Length == 1)
        {
            if (matches[0].State == "Disabled")
                return new(AvailabilityNoticeKind.Problem, "VAC LITE DISABLED", "Enable VAC Lite in sound settings");
            if (matches[0].State == "Unplugged")
                return new(AvailabilityNoticeKind.Problem, "VAC LITE UNPLUGGED", "Check VAC Lite in Windows sound settings");
            if (matches[0].State == "Missing")
                return new(AvailabilityNoticeKind.Problem, "VAC LITE NOT DETECTED", "Check VAC Lite in Windows sound settings");
        }
        return new(AvailabilityNoticeKind.Problem, "VAC LITE UNAVAILABLE", "Check VAC Lite in Windows sound settings");
    }
}
