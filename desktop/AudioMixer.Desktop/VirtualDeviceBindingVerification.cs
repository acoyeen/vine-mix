using System;
using System.Linq;
using System.Text.Json;

namespace AudioMixer.Desktop;

/// <summary>Inert identity, reconnection and edit tests; never opens an audio endpoint.</summary>
internal static class VirtualDeviceBindingVerification
{
    internal static void Run(Action<bool, string> require)
    {
        static Endpoint Device(string id, string direction, string transport, string state = "Active") => new()
        {
            Id = id, StableId = "stable-" + id, Name = "Same user-visible name", Direction = direction,
            Transport = transport, IsVirtual = transport != "physical", State = state, Channels = 2, Rate = 48000
        };
        static GraphConfiguration Graph(params string[] kinds) => new()
            { Nodes = kinds.Select((kind, i) => GraphDefaults.NewNode(kind, i * 400, 0)).ToList() };
        static string Json(object value) => JsonSerializer.Serialize(value, Protocol.Json);
        var receive = Device("receive", "capture", "vbCable");
        var send = Device("send", "render", "vacLite");
        Endpoint[] inventory = [receive, send, Device("apps-playback", "render", "vbCable"),
            Device("voice-capture", "capture", "vacLite"), Device("physical", "capture", "physical")];
        var graph = Graph("microphone", "virtualInput", "output", "virtualOutput");
        var physicalBefore = Json(new[] { graph.Nodes[0], graph.Nodes[2] });
        var assignments = VirtualDeviceBindings.FindAutomaticAssignments(graph, inventory);
        require(assignments.Count == 2 && assignments.Single(a => a.Kind == "virtualInput").Id == receive.Id
            && assignments.Single(a => a.Kind == "virtualOutput").Id == send.Id,
            "Virtual defaults select VB-CABLE capture and VAC Lite render by transport and direction, regardless of their identical display names.");
        require(VirtualDeviceBindings.Apply(graph, assignments) == 2 && graph.Nodes[1].Device.Channel == -1
            && graph.Nodes[3].Device.Channel == -1 && graph.Nodes[3].Muted,
            "Automatic virtual bindings use stereo and retain the new output's protective mute.");
        require(Json(new[] { graph.Nodes[0], graph.Nodes[2] }) == physicalBefore,
            "Virtual auto-assignment leaves both physical input and physical output settings untouched.");
        require(VirtualDeviceBindings.FindAutomaticAssignments(graph, inventory).Count == 0
            && VirtualDeviceBindings.Apply(graph, assignments) == 0,
            "Already assigned virtual nodes do not produce repeated configuration changes.");

        foreach (string state in new[] { "Missing", "Unplugged", "Disabled" })
            require(VirtualDeviceBindings.FindAutomaticAssignments(Graph("virtualInput"), [Device("inactive", "capture", "vbCable", state)]).Count == 0,
                "An unassigned virtual node waits while its only matching endpoint is " + state + ".");
        require(VirtualDeviceBindings.FindAutomaticAssignments(Graph("virtualInput"),
            [receive, Device("second-receive", "capture", "vbCable")]).Count == 0,
            "Two active matching virtual endpoints require an explicit troubleshooting choice.");
        require(VirtualDeviceBindings.FindAutomaticAssignments(Graph("virtualInput"),
            [new Endpoint { Name = "CABLE Output", Direction = "capture", Transport = "vbCable", State = "Active" }]).Count == 0,
            "An endpoint with a familiar name but no identity is never auto-assigned.");
        require(VirtualDeviceBindings.FindAutomaticAssignments(Graph("virtualInput"),
            [Device("impostor", "capture", "physical")]).Count == 0,
            "A physical endpoint cannot impersonate a virtual transport through its friendly name.");

        var saved = Graph("virtualInput", "virtualOutput");
        saved.Nodes[0].Device = new() { Id = "missing-old-id", Name = receive.Name, Channel = 0 };
        saved.Nodes[1].Device = new() { StableId = "missing-old-stable-id", Name = send.Name, Channel = -1 };
        string savedBefore = Json(saved);
        require(VirtualDeviceBindings.FindAutomaticAssignments(saved, inventory).Count == 0 && Json(saved) == savedBefore,
            "Missing saved endpoint IDs and stable-only identities survive new similarly named driver endpoints unchanged.");

        var duplicates = Graph("virtualInput", "virtualInput", "virtualOutput", "virtualOutput");
        assignments = VirtualDeviceBindings.FindAutomaticAssignments(duplicates, inventory);
        require(assignments.Count == 3 && VirtualDeviceBindings.Apply(duplicates, assignments) == 3
            && duplicates.Nodes[0].Device.Id == receive.Id && duplicates.Nodes[1].Device.Id == receive.Id
            && !ReferenceEquals(duplicates.Nodes[0].Device, duplicates.Nodes[1].Device)
            && duplicates.Nodes[2].Device.Id == send.Id
            && !VirtualDeviceBindings.IsAssigned(duplicates.Nodes[3].Device),
            "Multiple virtual inputs share the capture source with separate bindings while only one virtual output reserves the render endpoint.");
        var occupied = Graph("virtualInput", "virtualOutput", "microphone", "output");
        occupied.Nodes[2].Device = new() { StableId = receive.StableId };
        occupied.Nodes[3].Device = new() { Id = send.Id };
        assignments = VirtualDeviceBindings.FindAutomaticAssignments(occupied, inventory);
        require(assignments.Count == 1 && assignments[0].NodeId == occupied.Nodes[0].Id
            && !VirtualDeviceBindings.IsUsed(occupied, occupied.Nodes[0].Id, receive)
            && !VirtualDeviceBindings.IsUsed(occupied, occupied.Nodes[2].Id, receive)
            && VirtualDeviceBindings.IsUsed(occupied, occupied.Nodes[1].Id, send),
            "Physical and virtual inputs can share a source, while a physical output still prevents assigning its render endpoint to a virtual output.");
        occupied.Nodes[3].Device = new() { StableId = send.StableId };
        require(VirtualDeviceBindings.IsUsed(occupied, occupied.Nodes[1].Id, send),
            "Output ownership also compares stable identity when an endpoint ID is absent.");

        var rebased = Graph("virtualInput");
        assignments = VirtualDeviceBindings.FindAutomaticAssignments(rebased, inventory);
        rebased.Nodes[0].Device = new() { Id = "manual-reassignment", Name = "Manually selected" };
        require(VirtualDeviceBindings.Apply(rebased, assignments) == 0 && rebased.Nodes[0].Device.Id == "manual-reassignment",
            "Rebasing queued auto-assignment preserves an intervening manual device selection.");
        rebased.Nodes[0].Device = new();
        var other = GraphDefaults.NewNode("virtualInput", 500, 0);
        other.Device = new() { StableId = receive.StableId }; rebased.Nodes.Add(other);
        require(VirtualDeviceBindings.Apply(rebased, assignments) == 1
            && rebased.Nodes[0].Device.Id == receive.Id && other.Device.StableId == receive.StableId,
            "Rebasing queued input assignment permits a source selected by another input without changing that input.");
        rebased.Nodes.Remove(other); rebased.Nodes[0].Kind = "microphone"; rebased.Nodes[0].Device = new();
        require(VirtualDeviceBindings.Apply(rebased, assignments) == 0,
            "A queued virtual binding cannot alter a node that has changed kind.");
        rebased.Nodes.Clear();
        require(VirtualDeviceBindings.Apply(rebased, assignments) == 0,
            "Removing a virtual node also makes its queued assignment harmless.");
        var rebasedOutput = Graph("virtualOutput");
        assignments = VirtualDeviceBindings.FindAutomaticAssignments(rebasedOutput, inventory);
        var interveningOutput = GraphDefaults.NewNode("output", 500, 0);
        interveningOutput.Device = new() { StableId = send.StableId }; rebasedOutput.Nodes.Add(interveningOutput);
        require(VirtualDeviceBindings.Apply(rebasedOutput, assignments) == 0
            && !VirtualDeviceBindings.IsAssigned(rebasedOutput.Nodes[0].Device),
            "Rebasing queued output assignment preserves an intervening output owner.");

        using var model = new MixerViewModel(preview: true);
        var snapshot = new MixerSnapshot { Configuration = new() { Version = 2, Graph = Graph("microphone", "output") }, Endpoints = inventory.ToList() };
        model.ApplyPreviewSnapshot(snapshot);
        var incoming = GraphDefaults.NewNode("virtualInput", 0, 0);
        var outgoing = GraphDefaults.NewNode("virtualOutput", 400, 0);
        model.Edit(c => { c.Graph.Nodes.Add(incoming.Copy()); c.Graph.Nodes.Add(outgoing.Copy()); });
        require(model.GraphConfig.Nodes.Single(n => n.Id == incoming.Id).Device.Id == receive.Id
            && model.GraphConfig.Nodes.Single(n => n.Id == outgoing.Id).Device.Id == send.Id
            && model.GraphConfig.Nodes.Single(n => n.Kind == "microphone").Device.Id == ""
            && model.GraphConfig.Nodes.Single(n => n.Kind == "output").Device.Id == "",
            "The normal editor mutation path automatically binds newly added virtual nodes while physical choices stay manual.");
        var pasted = GraphDefaults.NewNode("virtualInput", 800, 0);
        model.Edit(c => c.Graph.Nodes.Add(pasted.Copy()));
        require(model.GraphConfig.Nodes.Single(n => n.Id == pasted.Id).Device.Id == receive.Id
            && model.GraphConfig.Nodes.Single(n => n.Id == incoming.Id).Device.Id == receive.Id,
            "Adding or pasting a second unassigned virtual input binds the same available source automatically.");
        model.Edit(c => { var node = c.Graph.Nodes.Single(n => n.Id == pasted.Id); node.GainDb = -12; node.Muted = true; node.Device.Channel = 0; });
        require(model.GraphConfig.Nodes.Single(n => n.Id == incoming.Id) is { GainDb: 0, Muted: false, Device.Channel: -1 }
            && model.GraphConfig.Nodes.Single(n => n.Id == pasted.Id) is { GainDb: -12, Muted: true, Device.Channel: 0 },
            "Automatically shared virtual inputs keep independent level, mute and channel controls.");

        var waiting = new MixerSnapshot { Configuration = new() { Version = 2, Graph = Graph("virtualInput", "virtualOutput") } };
        model.ApplyPreviewSnapshot(waiting);
        require(model.GraphConfig.Nodes.All(n => !VirtualDeviceBindings.IsAssigned(n.Device)),
            "Starting with absent virtual drivers retains both waiting virtual nodes.");
        waiting = new MixerSnapshot { Configuration = waiting.Configuration.Copy(), Endpoints = inventory.ToList() };
        model.ApplyPreviewSnapshot(waiting);
        require(model.GraphConfig.Nodes[0].Device.Id == receive.Id && model.GraphConfig.Nodes[1].Device.Id == send.Id,
            "Inventory arrival binds waiting virtual nodes without requiring the user to re-add them.");
        require(waiting.Configuration.Graph.Nodes.All(n => !VirtualDeviceBindings.IsAssigned(n.Device)),
            "Automatic assignment modifies the desired copy, never the engine snapshot used for revision reconciliation.");
        var disconnected = new MixerSnapshot { Configuration = new() { Version = 2, Graph = model.GraphConfig.Copy() } };
        string boundBefore = Json(disconnected.Configuration.Graph);
        model.ApplyPreviewSnapshot(disconnected);
        require(Json(model.GraphConfig) == boundBefore,
            "Losing all virtual endpoints preserves the chosen identities and node settings for reconnection.");
    }
}
