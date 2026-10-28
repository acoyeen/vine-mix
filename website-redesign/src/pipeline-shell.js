// Shared by the static build and the custom-element fallback. Keeping the
// styled shell in the initial HTML reserves its final size before any fetch.
export function pipelineShell(styles = '<link rel="stylesheet" href="pipeline.css">') {
  return `${styles}
    <section class="pipeline" aria-labelledby="pipeline-heading">
      <header class="intro"><h2 id="pipeline-heading">A clear path for your voice.</h2></header>
      <p class="sr-only">Illustrated audio route: Physical input, 100 Hz high-pass filter, Denoiser, Expander, Compressor, Equaliser, Virtual output. The moving light represents signal flow. No audio is recorded or played.</p>
      <noscript><style>.canvas-scroll{display:none}</style><p>Physical input → High-pass filter → Denoiser → Expander → Compressor → Equaliser → Virtual output</p></noscript>
      <div class="canvas-scroll" tabindex="0" role="region" aria-label="Horizontal audio pipeline. Scroll sideways to follow the signal."><div class="stage" role="group" aria-label="Seven-node audio pipeline"><svg class="wires" aria-hidden="true"></svg></div></div>
    </section>`;
}
