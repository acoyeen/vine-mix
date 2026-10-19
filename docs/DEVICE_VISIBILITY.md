# Audio device visibility

Physical input and output dropdowns normally show **Not assigned** and available,
compatible devices. Disabled, unplugged and missing devices are hidden unless
they are already assigned to that node. A saved assignment is retained while
unavailable, with its state shown; filtering never clears or replaces it.

**Settings → Audio device visibility** contains three independent preferences,
all off by default:

- **Show disabled devices** exposes Windows-disabled endpoints for inspection.
  Enable the device in Windows sound settings before using it.
- **Show disconnected or missing devices** exposes unavailable endpoints for
  troubleshooting. Reconnect the device before selecting it for a new route.
- **Show advanced inputs and outputs** exposes known line-level inputs, network
  and telephone endpoints, S/PDIF and digital passthrough outputs. These can
  connect an external mixer, remote audio endpoint or digital receiver.

Ordinary microphones, headsets, headphones, speakers and HDMI/display outputs
remain visible. Classification uses Windows endpoint form-factor metadata.
Unknown metadata remains visible rather than guessing from a device name.
Stereo Mix and other internal/streaming endpoints are not automatically hidden
as advanced when Windows cannot reliably identify their role; availability
filtering still applies. The mixer does not gain new driver or transport support.
Known VB-CABLE and VAC endpoints retain their dedicated virtual-node workflow.

Unavailable entries are informational and cannot create a new assignment. A
node's current unavailable selection remains displayed and can be cleared with
**Not assigned**. Distinct endpoints with the same name remain separate, with
hardware details and a device number to distinguish them. Hover provides the
full name and recovery guidance. If no available device is visible, the list
points to Settings.

Preferences are stored alongside the existing workspace preferences in
`%LOCALAPPDATA%\AudioMixer\ui.json`. Changes refresh closed lists immediately,
without audio-engine commands, routing edits or Undo entries. Open lists retain
their contents until closed to avoid moving items under the pointer; selection
rechecks the latest catalog to reject a device that has become unavailable.

The engine continues to enumerate every device state for recovery and diagnostics.
Optional metadata failures retain known data only for the exact endpoint identity.
Display resolution follows the engine: an active exact ID, then a unique active
stable ID. Saved IDs are never rewritten by filtering or reconnection display.

Verification uses fake Windows device catalogs, JSON preference round trips and
detached real WPF selectors. No computer use or synthetic audio is sent to real
devices. Native popup scrolling and physical-device reconnection remain manual
checks in the running application.

The combined build passed 8,178 detached checks with 159 renders and zero
binding errors. All 14 offline native suites passed, including 44 fake COM
catalog checks. Reports are under `artifacts/device-visibility-20261019/`.

Local activation on 19 October 2026 replaced the desktop and engine with the
verified binaries and reopened a responsive application. All nine previously
running paths recovered with advancing engine frames. The saved configuration,
settings file and workspace preferences were retained. Binary backups and the
activation report are under that report folder's `activation/` directory.
