# Detached node UI benchmark

> Historical development record. Results and procedures below belong to their
> named versions; they do not qualify the current beta. See
> [current qualification](BETA_TESTING_CURRENT.md) for the active release checks.

Evidence note: raw reports, historical renders and reference captures cited
here are retained privately outside the repository. Their labels below identify
archived evidence, not downloadable files; recorded results apply only to the
versions and checks described.

Measured on 17 October 2026. This compares the frozen pre-fix AudioMixer assembly with the final zoom-fix-v2 candidate, including the 1e-6 world-DIP cable-anchor tolerance, using identical in-memory fixtures. These are synchronous UI-thread timings and allocations, not displayed FPS, acoustic tests, desktop interaction, or GPU presentation latency.

The harness used actual App resources, MainWindow content, GraphWorkspace, and MixerViewModel(preview: true). WPF constructor startup operations were aborted before pumping the private dispatcher. Every scene checked that no native window or PresentationSource existed. Engine connection and supervisor fields remained null. No personal settings were opened. The bundled Inter font was extracted from each assembly into a private output directory and resolved explicitly; no fonts were installed.

Baseline SHA256: 80159DC504F866573345BB7ABC90300FAF1E5383C215B01F13440AD8A9062075
Final candidate SHA256: E38CC0A2B0B521018D0CD251DD4B9ECA42FC2646DA0FF0154B2AF40921C9AA44

## Telemetry refresh and layout

Each measured operation updates the private in-memory snapshot then calls the real RefreshPreviewTelemetry method, performs layout, and pumps pending binding/layout callbacks. Deserializing the fixture snapshot is excluded. 12 warm iterations precede 70 measured iterations per scene.

| Nodes | Zoom | Median before ms | Median after ms | Allocations before bytes | Allocations after bytes | Reduction |
|---:|---:|---:|---:|---:|---:|---:|
| migrated-8 | 100% | 1.512 | 1.284 | 257,992 | 53,096 | 79.4% |
| migrated-8 | 50% | 2.784 | 1.230 | 1,110,797 | 53,096 | 95.2% |
| migrated-8 | 30% | 2.325 | 1.042 | 3,280,957 | 53,960 | 98.4% |
| expanded-24 | 100% | 4.701 | 1.590 | 1,294,968 | 138,896 | 89.3% |
| expanded-24 | 50% | 3.716 | 1.460 | 1,881,089 | 137,088 | 92.7% |
| expanded-24 | 30% | 3.828 | 0.880 | 4,050,881 | 132,736 | 96.7% |

## Same update plus offscreen rasterization

The render measurement also clears and renders a 1280×800 RenderTargetBitmap at 96 DPI. Three warm iterations precede 15 measured iterations. This is an offscreen rendering workload; it is not a measurement of live display FPS.

| Nodes | Zoom | Median before ms | Median after ms |
|---:|---:|---:|---:|
| migrated-8 | 100% | 16.737 | 17.572 |
| migrated-8 | 50% | 23.889 | 18.517 |
| migrated-8 | 30% | 42.079 | 17.286 |
| expanded-24 | 100% | 30.026 | 39.743 |
| expanded-24 | 50% | 44.922 | 50.264 |
| expanded-24 | 30% | 52.604 | 38.713 |

The zoom-dependent allocation increase is removed in the final candidate. Telemetry update/layout improves at every measured zoom. At 30% zoom both offscreen rendering workloads improve. At 100% and for the 24-node scene at 50%, final-candidate offscreen raster timings are slower in this run despite lower update/layout costs. This benchmark cannot attribute those differences uniquely to rendering changes versus concurrent system/GPU workload. Do not claim all rendering cases are faster.

Full ApplyPreviewSnapshot results are also retained in the JSON. Preview intentionally always replaces configuration, so this is a conservative full-refresh workload and does not measure the optimized live snapshot config-clone skip. Production clone-skipping behavior is covered separately by regression assertions.

Final evidence: baseline-results-v2/results.json, after-v2-results/results.json, comparison.json, and six PNGs in each result folder. The frozen baseline remains unchanged. Earlier after-results and comparison-first-candidate.json are retained as historical candidate results. The initial baseline-results folder is exploratory and superseded by baseline-results-v2, which explicitly verifies the bundled font and adds the telemetry-only workload.

Archived raw measurements: baseline, candidate, comparison. The reusable private harness and frozen assemblies remain under build/node-ui-benchmark; those paths above are relative to that directory.
