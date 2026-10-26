# VineMix name compatibility

The current application, native engine, desktop project, build targets and user
documentation use **VineMix**. The development project, checkout folder and
GitHub repository use **`vine-mix`**. C# projects, executable names and CMake
identifiers use their language-appropriate forms, such as `VineMix.Desktop` and
`VINEMIX_OFFLINE_TESTS`. The product website remains `vinemix.app`.

The naming convention was consolidated on 26 October 2026. A local folder move
can retain a temporary `audio-mixer` directory junction to `vine-mix` so existing
chats, cached build paths and older links continue to work. It is a compatibility
alias, not a second checkout. New development should use the `vine-mix` path.

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
- Use `VINEMIX_OFFLINE_TESTS` and `VINEMIX_HARDWARE_TESTS` for new builds.
  `AUDIO_MIXER_*` options remain fallback aliases when the corresponding new
  option has not been set. Explicit `VINEMIX_*` values take precedence, including
  explicit `OFF`; use the new options when reconfiguring an existing build.
  The shared build properties also tolerate the previous desktop project name.
- Historical research, verification records and old release records keep their
  original identifiers. They are not current installation instructions or shipped
  application data.

Settings schema version 2 and control protocol version 1 remain separate from
release `0.3.0-beta.1`. Optional `autoAssignDevice` node metadata defaults to true
when absent, preserving legacy matching behavior. Explicitly clearing an assigned
virtual device saves false and prevents reassignment on reopening.
