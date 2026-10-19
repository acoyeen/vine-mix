#include <stdatomic.h>
#include <stdint.h>
#include <stdlib.h>
static atomic_uint_fast64_t operations;
void* am_speex_alloc(int size) { atomic_fetch_add_explicit(&operations, 1, memory_order_relaxed); return calloc(1, (size_t)size); }
void* am_speex_realloc(void* pointer, int size) { atomic_fetch_add_explicit(&operations, 1, memory_order_relaxed); return realloc(pointer, (size_t)size); }
void am_speex_free(void* pointer) { atomic_fetch_add_explicit(&operations, 1, memory_order_relaxed); free(pointer); }
uint64_t am_speex_memory_operations(void) { return atomic_load_explicit(&operations, memory_order_relaxed); }
