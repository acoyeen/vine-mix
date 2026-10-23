# Denoiser algorithm options

Implemented and verified 19 October 2026.

Choose **SpeexDSP · 20 ms** or **RNNoise · 30 ms** in the Denoiser node's
**Algorithm** dropdown. The delay labels were added in the local candidate on
23 October 2026 after confirming the current 960/1440-frame implementation at
48 kHz and rerunning its offline signal checks. These are node delays including
bypass, not algorithm minimums, CPU measurements or total system latency.
Both algorithms are embedded and process locally. The node shows only the
selected algorithm's control and retains the other algorithm's setting.

| Algorithm | Control | Delay, including bypass |
|---|---|---|
| SpeexDSP | Reduction: 0–40 dB, default 18 dB | 20 ms |
| RNNoise | Mix: 0–100%, default 100% | 30 ms |

RNNoise Mix blends the time-aligned original sound with the neural output.
Zero mix is original sound with the same delay as processed audio. It is not
a dB suppression target. SpeexDSP's existing Reduction behavior is unchanged.
Existing saved nodes and older clipboard groups keep SpeexDSP and their saved
reduction. Selection and both settings support saving, Undo/Redo, copy/paste,
Bypass and node Reset. Node dimensions stay fixed when selecting an algorithm.

RNNoise v0.2 and matching model 0b50c45 are pinned and bundled during the build;
there is no runtime model download. See [implementation and transition details](EFFECTS_RESEARCH.md#denoiser)
and [third-party notices](../THIRD_PARTY_NOTICES.md).

## Verification

The separate build is in `artifacts/AudioMixer-Denoiser`. The existing personal
controller and engine were left running throughout. Shared project source edits
were limited to denoiser integration and its documentation/checks. Native builds
and final desktop build used `build/denoiser-native` and
`build/denoiser-dotnet`; the other chats' work was not reset or committed.

- All **14** silent native test programs passed. Results:
  `artifacts/denoiser-verification-20261019-173507/native-results.json`.
- The dedicated denoiser checks passed **22 assertions**, including actual
  embedded-model noise reduction, three measured wet-pulse positions at exactly
  1440 frames, exact zero-Mix dry audio, bypass alignment, independent stereo,
  saved selection, legacy defaults, invalid values, recurrent-state copying,
  rapid switching and zero C++/vendor heap operations during processing,
  switching, snapshot adoption and recovery.
- All **5,138** detached desktop assertions passed, with **146 renders** and
  **zero binding errors**. Results:
  `artifacts/denoiser-ui-20261019-173739/results.json`. Both algorithm screenshots
  were visually inspected for dropdown, labels, values, complete controls and
  unchanged geometry.
- Stereo RNNoise processing p99 measured about **1.01 ms** for a 5 ms quantum in
  the final dedicated check, while another build was running. This measures one
  effect's synthetic processing work, not full device latency or a guarantee
  about arbitrary multi-denoiser graphs.

## Listening still needed

Matched speech, fan/keyboard noise and sustained humming/singing need a listening
comparison on the actual microphone. These algorithms are speech focused; no
claim is made that RNNoise matches Shure or eliminates every tonal artifact.
Disable Shure's hardware denoiser for a software-only comparison, since the USB
signal already contains its processing. See [Shure setup](SHURE_DENOISER.md).
No microphone recordings, test playback, device changes or live process restarts
were performed for this implementation.
