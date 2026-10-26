/* VineMix extension around the pinned, unmodified SpeexDSP 1.2.1 source.
 * Including the implementation keeps private layout access local to this one
 * bridge. Upstream's copyright/BSD notice remains in preprocess.c and COPYING.
 * The copy routine duplicates mutable arrays into already initialized states;
 * FFT/filter-bank plans remain separately owned and identically configured.
 */
#include "preprocess.c"
#include <string.h>

void am_speex_copy(SpeexPreprocessState *destination, const SpeexPreprocessState *source)
{
   const SpeexPreprocessState owned = *destination;
   const int n = source->ps_size, m = source->nbands;
   const int overlap = 2*n - source->frame_size;
   *destination = *source;
   destination->bank = owned.bank;
   destination->fft_lookup = owned.fft_lookup;
   destination->echo_state = NULL; /* This application never attaches AEC. */
#define COPY_ARRAY(field, count) do { \
   destination->field = owned.field; \
   memcpy(destination->field, source->field, (count)*sizeof(*source->field)); \
} while (0)
   COPY_ARRAY(frame, 2*n); COPY_ARRAY(ft, 2*n); COPY_ARRAY(window, 2*n);
   COPY_ARRAY(ps, n+m); COPY_ARRAY(gain2, n+m); COPY_ARRAY(gain_floor, n+m);
   COPY_ARRAY(noise, n+m); COPY_ARRAY(reverb_estimate, n+m); COPY_ARRAY(old_ps, n+m);
   COPY_ARRAY(gain, n+m); COPY_ARRAY(prior, n+m); COPY_ARRAY(post, n+m);
   COPY_ARRAY(zeta, n+m); COPY_ARRAY(echo_noise, n+m); COPY_ARRAY(residual_echo, n+m);
   COPY_ARRAY(S, n); COPY_ARRAY(Smin, n); COPY_ARRAY(Stmp, n); COPY_ARRAY(update_prob, n);
   COPY_ARRAY(inbuf, overlap); COPY_ARRAY(outbuf, overlap);
#ifndef FIXED_POINT
   COPY_ARRAY(loudness_weight, n);
#endif
#undef COPY_ARRAY
}
