/* The pinned vendor library's allocation hooks, for silent realtime checks. */
#include <stdlib.h>
#include <stdint.h>
#include <stdatomic.h>
static _Atomic uint64_t operations;
void* am_rnnoise_malloc(size_t size) { atomic_fetch_add_explicit(&operations, 1, memory_order_relaxed); return malloc(size); }
void* am_rnnoise_calloc(size_t count, size_t size) { atomic_fetch_add_explicit(&operations, 1, memory_order_relaxed); return calloc(count, size); }
void am_rnnoise_free(void* pointer) { atomic_fetch_add_explicit(&operations, 1, memory_order_relaxed); free(pointer); }
uint64_t am_rnnoise_memory_operations(void) { return atomic_load_explicit(&operations, memory_order_relaxed); }
