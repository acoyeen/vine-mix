# Virtual node device selection

Implemented and verified on 18 October 2026.

Virtual input and virtual output nodes display a fixed endpoint name and visible
connection status. Physical microphone and listening-output nodes retain their
device dropdowns. Names truncate within the existing node width; hover shows the
full name. Connection changes do not resize nodes or move sockets.

An unassigned virtual input binds an active VB-CABLE recording endpoint. An
unassigned virtual output binds an active VAC Lite playback endpoint. There must
be exactly one matching active endpoint, with an identity, and no other node
may own that endpoint. New outputs remain muted. Binding also runs when a
previously absent driver becomes available. Existing saved identities always
take precedence, including while disconnected or after a driver reinstall.

Manual recovery is available through the node's right-click menu:
**Troubleshooting → Reassign device**. This lists only the appropriate transport
and direction, marks the current device, and disables devices used by other nodes.
Selecting a device preserves the node's levels, mute, cables and position.

## Verification

- Release desktop publish completed successfully.
- Detached WPF verification: **2,041 checks, 81 renders, zero binding errors**.
  Includes automatic assignment, ambiguous and missing devices, saved identities,
  duplicate ownership, pending-edit replay guards, real node labels/status,
  manual menu reassignment, stable dimensions and physical-selector preservation.
- Evidence: `docs/design/virtual-devices-20261018-194729/results.json`, including
  `virtualInput-fixed-device.png` and `virtualOutput-fixed-device.png`.
- Updated the running interface only. The existing engine process and binary
  remained unchanged, its frame counter advanced, and all 11 node paths reported
  Running. Configuration revision 3831 and the settings file were unchanged.
- Activation evidence: `artifacts/virtual-devices-update-20261018-194800/result.json`.

Checks used synthetic inventory and detached controls. Production save/rejection
handling was reviewed; no real driver reinstall, disconnection, audio injection
or desktop input was performed for this interface change.
