/* Test-only adapter. Keep the upstream DSP and detected CPU features, and
   optionally select the baseline dispatch table even on an AVX2 host. */
#include "denoise.c"

DenoiseState *am_oracle_rnnoise_create_baseline(void) {
    DenoiseState *state = rnnoise_create(NULL);
    if (state && state->arch > 1) state->arch = 1;
    return state;
}
