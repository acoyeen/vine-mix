/* Allocation instrumentation through SpeexDSP's documented OS override hook. */
#ifndef AM_SPEEX_OS_SUPPORT_CUSTOM_H
#define AM_SPEEX_OS_SUPPORT_CUSTOM_H
void* am_speex_alloc(int size);
void* am_speex_realloc(void* pointer, int size);
void am_speex_free(void* pointer);
#define OVERRIDE_SPEEX_ALLOC
#define OVERRIDE_SPEEX_ALLOC_SCRATCH
#define OVERRIDE_SPEEX_REALLOC
#define OVERRIDE_SPEEX_FREE
#define OVERRIDE_SPEEX_FREE_SCRATCH
#define speex_alloc am_speex_alloc
#define speex_alloc_scratch am_speex_alloc
#define speex_realloc am_speex_realloc
#define speex_free am_speex_free
#define speex_free_scratch am_speex_free
#endif
