namespace VineMix.Desktop;

// Additive protocol fields: current engines supply stable reasons; only an absent
// code uses these exact legacy diagnostics. Unknown codes never fall back to text.
internal static class StatusReasons
{
    internal static string For(PathState? path)
    {
        if (path is null) return "";
        if (!string.IsNullOrEmpty(path.Reason)) return path.Reason;
        return path.Message switch
        {
            "Device disabled in Windows Sound settings" => "device_disabled",
            "Several instances are running; select the app again" => "app_instance_ambiguous",
            "Windows denied app audio capture" => "access_denied",
            "Windows Audio service unavailable" => "audio_service_unavailable",
            _ => ""
        };
    }

    internal static bool IsRevisionConflict(ProtocolResponse response)
        => response.ErrorCode == "revision_conflict" || (string.IsNullOrEmpty(response.ErrorCode)
            && response.Error == "Configuration revision conflict. Refresh and retry your change.");

    internal static string Detail(GraphNode node, PathState? path)
    {
        // Display-only compatibility for one old diagnostic. Current worker
        // guidance (including device-specific help) must remain verbatim.
        if (node.Kind is "output" or "virtualOutput" && path?.State == "AccessDenied"
            && string.IsNullOrEmpty(path.Reason)
            && path.Message == "Microphone access blocked; check Windows microphone privacy settings")
            return "Output access blocked; check Windows sound device settings";
        return path?.Message ?? "";
    }
}
