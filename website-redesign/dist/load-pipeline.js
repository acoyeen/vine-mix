// The declarative shadow tree already reserves the diagram's final size.
const pipeline = document.querySelector('vine-mix-pipeline');
if (pipeline && 'IntersectionObserver' in window) {
  const observer = new IntersectionObserver(entries => {
    if (entries.some(entry => entry.isIntersecting)) {
      observer.disconnect();
      import('./pipeline.js');
    }
  }, { rootMargin: '640px' });
  observer.observe(pipeline);
} else if (pipeline) {
  import('./pipeline.js');
}
