# VineMix name compatibility

The current application, native engine, desktop project, build targets and user
documentation use **VineMix**. The development checkout folder and GitHub URL
need not match the executable name.

These older identifiers are intentionally retained:

- `%LOCALAPPDATA%\AudioMixer` and the Codex redirected counterpart are read-only
  migration sources. Originals remain available after migration.
- `AudioMixer.Desktop`, `AudioMixer.Engine` and `AudioMixer.Show` Windows object
  identities are reserved or detected alongside VineMix identities to prevent
  competing sessions and bring a supported existing window forward.
- `AudioMixer.exe`, `AudioMixer.Engine.exe` and the old startup registry value
  appear in upgrade, process safety and ownership checks. Removal or replacement
  requires proof that the value belongs to the copy being upgraded.
- `AudioMixer.NodeGroup.v1` clipboard data remains readable. Newly copied groups
  use `VineMix.NodeGroup.v1`.
- `AUDIO_MIXER_*` CMake options remain valid for existing developer invocations.
  The shared build properties also tolerate the previous desktop project name.
- Historical research, verification records and old release records keep their
  original identifiers. They are not current installation instructions or shipped
  application data.

Settings schema version 2 and control protocol version 1 remain separate from
release `0.3.0-beta.1`. Optional `autoAssignDevice` node metadata defaults to true
when absent, preserving legacy matching behavior. Explicitly clearing an assigned
virtual device saves false and prevents reassignment on reopening.
