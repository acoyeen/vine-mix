# Audio device visibility

Input and output dropdowns normally show **Not assigned** and available,
compatible devices. **Physical input** accepts Windows recording endpoints,
including microphones supplied by streamed-headset and virtual-audio drivers.
Disabled, unplugged and missing devices are hidden unless
they are already assigned to that node. A saved assignment is retained while
unavailable, with its state shown; filtering never clears or replaces it.

**Settings → Audio device visibility** contains three independent preferences,
all off by default:

- **Show disabled devices** exposes Windows-disabled endpoints for inspection.
  Enable the device in Windows sound settings before using it.
- **Show disconnected or missing devices** exposes unavailable endpoints for
  troubleshooting. Reconnect the device before selecting it for a new route.
- **Show advanced inputs and outputs** exposes known line-level inputs, network
  outputs and telephone endpoints, S/PDIF and digital passthrough outputs. These can
  connect an external mixer, remote audio endpoint or digital receiver.

Ordinary microphones, headsets, network microphones, headphones, speakers and
HDMI/display outputs remain visible. Classification uses Windows endpoint form-factor metadata.
Unknown metadata remains visible rather than guessing from a device name.
Stereo Mix and other internal/streaming endpoints are not automatically hidden
as advanced when Windows cannot reliably identify their role; availability
filtering still applies. A capture endpoint's software or network origin does not
prevent manual selection. Opening still depends on Windows allowing capture and
the driver supporting a compatible audio format. VB-CABLE receive and VAC Lite
send nodes retain their explicit transport checks and conservative automatic
assignment; unrelated streamed microphones never become cable defaults.

Unavailable entries are informational and cannot create a new assignment. A
node's current unavailable selection remains displayed and can be cleared with
**Not assigned**. Distinct endpoints with the same name remain separate, with
hardware details and a device number to distinguish them. Hover provides the
full name and recovery guidance. If no available device is visible, the list
points to Settings.

Preferences are stored alongside the existing workspace preferences in
`%LOCALAPPDATA%\VineMix\ui.json`. Changes refresh closed lists immediately,
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

For a streamed microphone such as Virtual Desktop, connect the headset and
enable the provider's microphone forwarding, verify that the microphone appears
under **Windows Settings → System → Sound → Input**, then select it on a
**Physical input** node. The optional bundled routing cables are not required
for an existing Windows capture endpoint. If it is disabled or disconnected,
the visibility switches expose it for diagnosis without making it selectable.

Research checked 21 October 2026: Microsoft's [EndpointFormFactor reference](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/ne-mmdeviceapi-endpointformfactor)
defines `RemoteNetworkDevice` as a remotely accessed endpoint, and the
[form-factor property](https://learn.microsoft.com/en-us/windows/win32/coreaudio/pkey-audioendpoint-formfactor)
describes endpoint attributes, rather than a vendor capability check. Virtual
Desktop's developer [support response](https://steamcommunity.com/app/382110/discussions/0/2961642818650950183/)
identifies **Microphone passthrough** as the forwarding control (the response
dates from 2020, so current interface wording may differ). The policy fix follows
those API semantics; no particular Virtual Desktop endpoint metadata or actual
headset session has been measured. Real headset capture, disconnect/reconnect
and saved-workspace reopening remain tester checks.

Historical results (19 October 2026): the combined build passed 8,178 detached checks with 159 renders and zero
binding errors. All 14 offline native suites passed, including 44 fake COM
catalog checks. Reports are retained privately outside the repository under the
historical `artifacts/device-visibility-20261019/` identifier.

Local activation on 19 October 2026 replaced the desktop and engine with the
verified binaries and reopened a responsive application. All nine previously
running paths recovered with advancing engine frames. The saved configuration,
settings file and workspace preferences were retained. Binary backups and the
activation report are under that report folder's `activation/` directory.
