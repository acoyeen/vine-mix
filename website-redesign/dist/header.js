import { mountFloatingBrand } from './floating-brand.js';
import { mountPageNavigation } from './page-navigation.js';

// Section links and Docs remain visible at every width; there is no menu to open.
export function mountSiteHeader(header) {
  if (!header) return null;
  let floatingBrand = null;
  let startupFrame = 0;
  let destroyed = false;
  function enhanceBrand() {
    if (destroyed || floatingBrand || startupFrame) return;
    // Chrome can briefly report an old/intermediate scroll offset on refresh.
    // Keep the original left-aligned header until pageshow's native scroll
    // restoration has completed, then snap to that settled position once.
    startupFrame = requestAnimationFrame(() => {
      startupFrame = 0;
      if (!destroyed) floatingBrand = mountFloatingBrand(header);
    });
  }
  if (document.readyState === 'complete') enhanceBrand();
  else window.addEventListener('pageshow', enhanceBrand, { once: true });
  const navigation = mountPageNavigation();
  return {
    destroy() {
      destroyed = true;
      window.removeEventListener('pageshow', enhanceBrand);
      if (startupFrame) cancelAnimationFrame(startupFrame);
      startupFrame = 0;
      floatingBrand?.destroy();
      navigation.destroy();
    },
  };
}

mountSiteHeader(document.querySelector('[data-site-header]'));
