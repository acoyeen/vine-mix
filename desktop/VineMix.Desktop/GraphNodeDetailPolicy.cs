namespace VineMix.Desktop;

internal enum NodeDetailLevel { Full, Compact, Overview }

// No WPF controls, model, subscriptions or audio access. A view supplies only
// its scale and whether it owns an edit; the policy cannot change geometry.
internal static class GraphNodeDetailPolicy
{
    internal static NodeDetailLevel At(double scale, NodeDetailLevel previous, bool editing = false)
    {
        NodeDetailLevel next;
        if (scale >= .30) next = NodeDetailLevel.Full;
        else if (previous == NodeDetailLevel.Full && scale >= .28) next = previous;
        else if (scale < .16) next = NodeDetailLevel.Overview;
        else if (previous == NodeDetailLevel.Overview && scale < .20) next = previous;
        else next = NodeDetailLevel.Compact;
        return editing && next > previous ? previous : next;
    }
}
