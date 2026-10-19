# Using Shure's Real-time Denoiser

Researched 18 October 2026. This research did not open an audio device, record
speech, change Shure settings, install software, or restart a live process.

## The useful finding

The MV7+ already contains the denoiser the user likes. Shure identifies
Real-time Denoiser as **on-board DSP**; MOTIV Mix provides its controls. The MV7+
stores its last-used device settings. Its passive XLR output is unaffected by
those settings. [MV7+ user guide](https://www.shure.com/en-US/docs/guide/MV7plus).

This is different from distributing a Shure denoising library inside our mixer.
We can receive the microphone's processed USB audio through the existing
Microphone node:

```text
MV7+ capsule → Shure hardware denoiser → MV7+ USB recording endpoint
    → Audio Mixer Microphone node → chosen effects and outputs
```

The inference from Shure's documented hardware architecture is that this path
continues with MOTIV Mix closed. Shure's newer MOTIV explanation explicitly
separates app control from microphone processing and says stored settings remain
active across other applications and devices. This corroborates the MV7+
guide's own description; it is not a claim that we measured this particular
microphone's firmware during this task.
[MOTIV architecture in the MV6 Gen 2 guide](https://www.shure.com/en-US/docs/guide/mv6g2).

## How to use it here

These are setup instructions for the user, not changes already applied:

1. Use the MV7+'s **USB** connection.
2. In MOTIV Mix, open the MV7+'s device settings and enable **Real-time Denoiser**.
3. In our Microphone node, select the physical **Shure MV7+** recording device.
4. Keep the node connected to the desired outputs. No separate Shure effect node
   or additional virtual cable is required.
5. If our software Denoiser is also in that route, compare with its **Bypass**
   enabled first, so two denoisers do not obscure the comparison.

The gear icon and Real-time Denoiser control are documented in the
[MV7+ setup guide](https://www.shure.com/en-US/docs/guide/MV7plus).
MOTIV Mix's virtual driver serves its own mixing and routing paths; it is not
required for direct capture of the physical microphone. Selecting MOTIV Mix's
virtual output instead makes our input depend on MOTIV Mix providing that mix.
[MOTIV Mix user guide](https://www.shure.com/en-US/docs/guide/MOTIV-Mix).

Our existing capture code uses shared-mode WASAPI on the selected endpoint
(`native/src/windows_audio.cpp`). It does not disable, replace, or query the
MV7+'s internal DSP. A Windows endpoint being **Running** therefore proves an
open audio path, not that Shure denoising is enabled. The app must not display an
on/off status for that hardware setting unless a supported readback is added.

Hardware processing happens before the signal enters our graph. It consequently
applies to every route originating from that USB microphone. Our node's Bypass
cannot undo it, and it cannot be placed after an EQ node or moved to another
microphone. XLR capture through another interface does not carry the MV7+'s
software-controlled processing. [MV7+ outputs](https://www.shure.com/en-US/docs/guide/MV7plus).

MOTIV Mix's own mixer levels, routing and virtual output are separate from
device settings. Do not assume an entire MOTIV Mix session is reproduced by
choosing the physical USB input. In particular, comparisons need the same
gain and processing chain; changing apps while also changing levels is not an
isolated denoiser comparison.

## Can the exact algorithm become an ordinary effect node?

**No supported public source, SDK, or redistribution package for the MV7+
denoiser was found in this research.** This is a scoped finding, not proof that
Shure has no private licensing arrangements.

We checked Shure's MV7+ and MOTIV Mix manuals, product documentation, public API
entry points, and public licence page. Shure's System API lists supported
network/conferencing products, but does not list the MV7+ as compatible hardware.
That API is not evidence of a supported USB denoiser control interface.
[System API product and compatibility list](https://www.shure.com/en-US/products/software/systemapi),
[Shure REST API documentation](https://shure.stoplight.io/).

The installed MOTIV Mix directory was also checked for ordinary documentation
and licence files. It contains Electron/Chromium and Phoenix gRPC attribution
texts; none of the inspected texts documents a public MV7+ denoiser SDK. A
third-party dependency's open-source licence does not establish permission to
redistribute Shure's DSP. No application archive, executable, firmware or private
protocol was extracted or reverse-engineered. Shure lists MOTIV Apps under its
software EULA, which is separate from its third-party component licences.
[Shure legal resources](https://www.shure.com/en-US/legal).

IntelliMix Room is a different Shure product. No primary source found here says
its AI Denoiser is the MV7+'s algorithm, so it should not be presented as an
interchangeable or free implementation.

For this application, the supported integration is therefore **reuse the
existing Shure hardware through USB**, with clear setup help. A future embedded
Shure toggle or generic Shure effect requires a documented interface and any
necessary permission from Shure. This task does not invent an ineffective
toggle or relabel SpeexDSP as Shure.

## A free option for other microphones

**Update, 19 October 2026:** RNNoise is now embedded alongside SpeexDSP in our
Denoiser node's **Algorithm** dropdown. See [implementation, controls and delay](EFFECTS_RESEARCH.md#denoiser).
This does not integrate or control Shure's hardware algorithm.

RNNoise combines DSP and a recurrent neural network, exposes
a C library, and uses the BSD-3-Clause licence. Its example operates at 48 kHz.
Current builds obtain model data separately from the source, so an integration
would pin both code and model and include the required notices.
[RNNoise source and build information](https://github.com/xiph/rnnoise),
[RNNoise licence](https://github.com/xiph/rnnoise/blob/main/COPYING),
[author's algorithm paper](https://arxiv.org/abs/1709.08243).

That makes it a candidate for a measured speech-quality improvement over our
current SpeexDSP node, not proof of parity with Shure. Before replacing a saved
effect, compare real speech mixed with fan noise, keyboard clicks and changing
noise, and measure latency/CPU. Keep a repeatable reference, preserve input
level, listen for lost consonants and speech artifacts, and retain music tests
to expose unsuitable use. The original research did not integrate it; the later
dropdown implementation provides a separate software option, with perceived
quality and the user's actual humming artifact still requiring listening tests.

## Later verification on this PC

When a local listening comparison is convenient:

1. Keep the physical USB microphone selected and keep levels/other processing
   fixed. Compare the Shure denoiser off/on using consistent speech and room
   noise. Keep any software denoiser bypassed for this comparison.
2. Leave Shure denoising enabled, close MOTIV Mix, and confirm the same reduction
   through the physical USB input. Check that no path depends on the MOTIV Mix
   virtual output.
3. At a convenient interruption, reconnect the microphone and repeat. Record
   the actual firmware/app versions and whether its setting persisted.
4. If direct USB and MOTIV Mix sound different, compare the complete gain/EQ/
   dynamics/monitoring paths before attributing the difference to denoising.

These checks remain unperformed. The user's current hardware denoiser state and
its measured quality are unknown to this application; this research supports
the integration path, not an audible-quality certification.
