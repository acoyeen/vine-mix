# Changelog

## Unreleased

- Reduce avoidable audio delay with shorter supported shared WASAPI periods,
  device-aware queue targets, capture pre-roll and resampler-aware playback
  priming. Preserve effect and converter quality, with bounded buffering growth
  and default-timing fallback for repeatedly interrupted streams.
- Preserve mixer cadence across short scheduling delays with bounded catch-up.
- Add timing diagnostics and offline shared-device, asynchronous buffering and
  mixer-clock regression checks. Hardware latency remains to be measured; see
  [audio latency](docs/AUDIO_LATENCY.md).

## 0.2.0 — initial repository publication

- Windows node workspace and separate audio engine, with 23 creatable node types,
  local effects, per-application capture, independent outputs and saved-graph
  compatibility.
- Synthetic native and detached WPF verification tools, build and packaging scripts,
  and Windows continuous integration.
- Publication review: relative-path persistence fixes, reduced desktop history
  allocation, pinned download hashes, and complete third-party distribution notices.

Hardware and audible qualification remain incomplete. This entry records the
initial source publication; it does not declare a fully qualified binary release.
