cmake_minimum_required(VERSION 3.24)
file(READ "${CMAKE_CURRENT_SOURCE_DIR}/release.json" VINEMIX_RELEASE)
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/release.json")
string(JSON VINEMIX_VERSION GET "${VINEMIX_RELEASE}" version)
string(JSON VINEMIX_NUMERIC_VERSION GET "${VINEMIX_RELEASE}" numericVersion)
string(JSON VINEMIX_DISPLAY_VERSION GET "${VINEMIX_RELEASE}" displayVersion)
project(VineMix VERSION ${VINEMIX_NUMERIC_VERSION} LANGUAGES C CXX RC)
string(REPLACE "." "," VINEMIX_RESOURCE_VERSION "${VINEMIX_NUMERIC_VERSION}")
configure_file(native/include/am/version.hpp.in generated/am/version.hpp @ONLY)
configure_file(native/src/version.rc.in generated/version.rc @ONLY)
if(NOT WIN32)
  message(FATAL_ERROR "VineMix currently targets Windows 11 x64.")
endif()
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
include(FetchContent)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(BUILD_TESTING OFF CACHE BOOL "Disable vendor test suites; mixer checks use separate opt-in options" FORCE)
set(LIBSAMPLERATE_EXAMPLES OFF CACHE BOOL "" FORCE)
set(LIBSAMPLERATE_INSTALL OFF CACHE BOOL "" FORCE)
set(JSON_BuildTests OFF CACHE BOOL "" FORCE)
set(JSON_Install OFF CACHE BOOL "" FORCE)
FetchContent_Declare(samplerate
  URL https://github.com/libsndfile/libsamplerate/archive/refs/tags/0.2.2.tar.gz
  URL_HASH SHA256=16e881487f184250deb4fcb60432d7556ab12cb58caea71ef23960aec6c0405a
  DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
FetchContent_Declare(json
  URL https://github.com/nlohmann/json/releases/download/v3.12.0/json.tar.xz
  URL_HASH SHA256=42f6e95cad6ec532fd372391373363b62a14af6d771056dbfc86160e6dfff7aa
  DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
FetchContent_MakeAvailable(samplerate json)

# Official stable SpeexDSP release. The preprocessor is a spectral estimator,
# not a noise gate. Pin both source version and archive digest.
FetchContent_Declare(speexdsp
  URL https://downloads.xiph.org/releases/speex/speexdsp-1.2.1.tar.gz
  URL_HASH SHA256=8c777343e4a6399569c72abc38a95b24db56882c83dbdb6c6424a5f4aeb54d3d
  DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
FetchContent_MakeAvailable(speexdsp)
add_library(VineMix.Speex STATIC
  native/vendor/speex_preprocess_bridge.c
  native/vendor/speex_memory.c
  ${speexdsp_SOURCE_DIR}/libspeexdsp/fftwrap.c
  ${speexdsp_SOURCE_DIR}/libspeexdsp/smallft.c
  ${speexdsp_SOURCE_DIR}/libspeexdsp/filterbank.c
  ${speexdsp_SOURCE_DIR}/libspeexdsp/mdf.c)
target_include_directories(VineMix.Speex PUBLIC ${speexdsp_SOURCE_DIR}/include
  PRIVATE ${speexdsp_SOURCE_DIR}/libspeexdsp native/vendor)
target_compile_definitions(VineMix.Speex PRIVATE FLOATING_POINT USE_SMALLFT OS_SUPPORT_CUSTOM EXPORT=)
# RNNoise v0.2 and its matching upstream model are embedded at build time.
# Neither playback nor first use downloads a model. Pin both archives.
FetchContent_Declare(rnnoise
  URL https://codeload.github.com/xiph/rnnoise/tar.gz/refs/tags/v0.2
  URL_HASH SHA256=e4a17eb1493b7551f9f7781d2acc67b8847d6ed7c4b64db0fd62c017021ae1ec
  DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
FetchContent_Declare(rnnoise_model
  URL https://media.xiph.org/rnnoise/models/rnnoise_data-0b50c45.tar.gz
  URL_HASH SHA256=4ac81c5c0884ec4bd5907026aaae16209b7b76cd9d7f71af582094a2f98f4b43
  DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
FetchContent_MakeAvailable(rnnoise rnnoise_model)
add_library(VineMix.RNNoise STATIC
  ${rnnoise_SOURCE_DIR}/src/denoise.c
  ${rnnoise_SOURCE_DIR}/src/rnn.c
  ${rnnoise_SOURCE_DIR}/src/pitch.c
  ${rnnoise_SOURCE_DIR}/src/kiss_fft.c
  ${rnnoise_SOURCE_DIR}/src/celt_lpc.c
  ${rnnoise_SOURCE_DIR}/src/nnet.c
  ${rnnoise_SOURCE_DIR}/src/nnet_default.c
  ${rnnoise_SOURCE_DIR}/src/parse_lpcnet_weights.c
  ${rnnoise_SOURCE_DIR}/src/rnnoise_tables.c
  ${rnnoise_model_SOURCE_DIR}/src/rnnoise_data.c
  ${rnnoise_SOURCE_DIR}/src/x86/x86cpu.c
  ${rnnoise_SOURCE_DIR}/src/x86/x86_dnn_map.c
  ${rnnoise_SOURCE_DIR}/src/x86/nnet_sse4_1.c
  ${rnnoise_SOURCE_DIR}/src/x86/nnet_avx2.c)
target_include_directories(VineMix.RNNoise PUBLIC ${rnnoise_SOURCE_DIR}/include
  PRIVATE ${rnnoise_SOURCE_DIR}/src ${rnnoise_model_SOURCE_DIR}/src)
target_compile_definitions(VineMix.RNNoise PRIVATE RNNOISE_BUILD
  RNN_ENABLE_X86_RTCD CPU_INFO_BY_C
  malloc=am_rnnoise_malloc calloc=am_rnnoise_calloc free=am_rnnoise_free)
if(MSVC)
  set_source_files_properties(${rnnoise_SOURCE_DIR}/src/x86/nnet_avx2.c PROPERTIES COMPILE_OPTIONS /arch:AVX2)
else()
  set_source_files_properties(${rnnoise_SOURCE_DIR}/src/x86/nnet_sse4_1.c PROPERTIES COMPILE_OPTIONS "-msse4.1")
  set_source_files_properties(${rnnoise_SOURCE_DIR}/src/x86/nnet_avx2.c PROPERTIES COMPILE_OPTIONS "-mavx2;-mfma")
endif()
add_library(VineMix.Effects STATIC native/src/advanced_effects.cpp native/vendor/rnnoise_memory.c)
target_include_directories(VineMix.Effects PUBLIC native/include)
target_link_libraries(VineMix.Effects PRIVATE VineMix.Speex VineMix.RNNoise)
install(FILES ${speexdsp_SOURCE_DIR}/COPYING DESTINATION licenses RENAME SpeexDSP-COPYING.txt)
install(FILES ${rnnoise_SOURCE_DIR}/COPYING DESTINATION licenses RENAME RNNoise-COPYING.txt)

add_executable(VineMix.Engine
  ${CMAKE_CURRENT_BINARY_DIR}/generated/version.rc
  native/src/main.cpp
  native/src/configuration.cpp
  native/src/engine.cpp
  native/src/control_server.cpp
  native/src/windows_audio.cpp
  native/src/process_capture.cpp
  native/src/graph.cpp
  native/src/dsp.cpp)
target_include_directories(VineMix.Engine PRIVATE native/include ${CMAKE_CURRENT_BINARY_DIR}/generated)
target_compile_definitions(VineMix.Engine PRIVATE
  UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN _WIN32_WINNT=0x0A00 WINVER=0x0A00)
target_link_libraries(VineMix.Engine PRIVATE
  samplerate nlohmann_json::nlohmann_json
  ole32 uuid avrt propsys advapi32 winmm user32)
if(MSVC)
  target_compile_options(VineMix.Engine PRIVATE /W4 /permissive- /EHsc)
else()
  target_compile_options(VineMix.Engine PRIVATE -Wall -Wextra -Wpedantic)
  target_link_options(VineMix.Engine PRIVATE -static-libgcc -static-libstdc++ -municode)
endif()

install(TARGETS VineMix.Engine RUNTIME DESTINATION .)
install(FILES LICENSE ATTRIBUTION.md THIRD_PARTY_NOTICES.md DESTINATION .)
install(DIRECTORY licenses/ DESTINATION licenses)
install(FILES docs/DISTRIBUTION.md DESTINATION docs)

function(vinemix_verification_target target)
  target_include_directories(${target} PRIVATE native/include ${CMAKE_CURRENT_BINARY_DIR}/generated)
  target_compile_definitions(${target} PRIVATE
    UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN _WIN32_WINNT=0x0A00 WINVER=0x0A00)
  if(MSVC)
    target_compile_options(${target} PRIVATE /W4 /permissive- /EHsc)
  else()
    target_compile_options(${target} PRIVATE -Wall -Wextra -Wpedantic)
    target_link_options(${target} PRIVATE -static-libgcc -static-libstdc++)
  endif()
endfunction()

# Pure synthetic verification: no WASAPI devices or user settings are used.
# The lifecycle target links stub I/O with the actual engine. Opt in separately
# from the vendor library test suites.
# Accept the legacy spelling only when the canonical option is not set.
if(DEFINED AUDIO_MIXER_OFFLINE_TESTS AND NOT DEFINED VINEMIX_OFFLINE_TESTS)
  set(VINEMIX_OFFLINE_TESTS "${AUDIO_MIXER_OFFLINE_TESTS}" CACHE BOOL "Legacy build-option compatibility")
endif()
option(VINEMIX_OFFLINE_TESTS "Build silent graph, settings, lifecycle, drift and queue verification" OFF)
if(VINEMIX_OFFLINE_TESTS)
  add_executable(VineMix.GraphTests
    native/tests/graph_tests.cpp native/src/graph.cpp native/src/configuration.cpp native/src/dsp.cpp)
  target_include_directories(VineMix.GraphTests PRIVATE native/include)
  target_compile_definitions(VineMix.GraphTests PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
  target_link_libraries(VineMix.GraphTests PRIVATE samplerate nlohmann_json::nlohmann_json advapi32)
  if(MSVC)
    target_compile_options(VineMix.GraphTests PRIVATE /W4 /permissive- /EHsc)
  else()
    target_compile_options(VineMix.GraphTests PRIVATE -Wall -Wextra -Wpedantic)
    target_link_options(VineMix.GraphTests PRIVATE -static-libgcc -static-libstdc++)
  endif()
  add_executable(VineMix.EngineLifecycleTests
    native/tests/engine_lifecycle_tests.cpp native/src/engine.cpp native/src/control_server.cpp native/src/graph.cpp native/src/configuration.cpp native/src/dsp.cpp)
  target_include_directories(VineMix.EngineLifecycleTests PRIVATE native/include ${CMAKE_CURRENT_BINARY_DIR}/generated)
  target_compile_definitions(VineMix.EngineLifecycleTests PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
  target_link_libraries(VineMix.EngineLifecycleTests PRIVATE samplerate nlohmann_json::nlohmann_json advapi32 avrt)
  if(MSVC)
    target_compile_options(VineMix.EngineLifecycleTests PRIVATE /W4 /permissive- /EHsc)
  else()
    target_compile_options(VineMix.EngineLifecycleTests PRIVATE -Wall -Wextra -Wpedantic)
    target_link_options(VineMix.EngineLifecycleTests PRIVATE -static-libgcc -static-libstdc++)
  endif()

  # Uses a caller-supplied new fixture directory, never the user's settings.
  # GraphProcessor is self-contained; no legacy mixer or DSP helper stubs exist
  # in this test, and dsp.cpp/libsamplerate are not needed for this target.
  add_executable(VineMix.QualificationTests
    native/tests/qualification_tests.cpp native/src/graph.cpp native/src/configuration.cpp)
  vinemix_verification_target(VineMix.QualificationTests)
  target_link_libraries(VineMix.QualificationTests PRIVATE nlohmann_json::nlohmann_json advapi32)

  # Memory-only queues and resampling; no configuration or device APIs linked.
  add_executable(VineMix.DriftQueueTests native/tests/drift_queue_tests.cpp native/src/dsp.cpp)
  vinemix_verification_target(VineMix.DriftQueueTests)
  target_link_libraries(VineMix.DriftQueueTests PRIVATE samplerate)

  # Production inventory collector with fake COM objects only. Does not
  # construct live device catalogs/workers or open any real audio endpoint.
  add_executable(VineMix.WindowsCatalogTests
    native/tests/windows_catalog_tests.cpp native/src/windows_audio.cpp native/src/dsp.cpp)
  vinemix_verification_target(VineMix.WindowsCatalogTests)
  target_link_libraries(VineMix.WindowsCatalogTests PRIVATE samplerate nlohmann_json::nlohmann_json ole32 uuid avrt propsys)

  # Fake WASAPI clients exercise exclusive virtual-output initialization and
  # failure handling without opening endpoints or changing personal audio.
  add_executable(VineMix.ExclusiveRenderTests
    native/tests/exclusive_render_tests.cpp native/src/windows_audio.cpp native/src/dsp.cpp)
  vinemix_verification_target(VineMix.ExclusiveRenderTests)
  target_link_libraries(VineMix.ExclusiveRenderTests PRIVATE samplerate ole32 uuid avrt propsys)

  add_executable(VineMix.SharedAudioTests
    native/tests/shared_audio_tests.cpp native/src/windows_audio.cpp native/src/dsp.cpp)
  vinemix_verification_target(VineMix.SharedAudioTests)
  target_link_libraries(VineMix.SharedAudioTests PRIVATE samplerate ole32 uuid avrt propsys)

  add_executable(VineMix.AudioTimingTests native/tests/audio_timing_tests.cpp native/src/dsp.cpp)
  vinemix_verification_target(VineMix.AudioTimingTests)
  target_link_libraries(VineMix.AudioTimingTests PRIVATE samplerate)

  add_executable(VineMix.MixClockTests native/tests/mix_clock_tests.cpp)
  vinemix_verification_target(VineMix.MixClockTests)
endif()

# These are diagnostics for real endpoints, deliberately separate from silent
# offline verification. Building never executes them, installs drivers, alters
# Windows defaults, or adds them to the personal application package.
# Accept the legacy spelling only when the canonical option is not set.
if(DEFINED AUDIO_MIXER_HARDWARE_TESTS AND NOT DEFINED VINEMIX_HARDWARE_TESTS)
  set(VINEMIX_HARDWARE_TESTS "${AUDIO_MIXER_HARDWARE_TESTS}" CACHE BOOL "Legacy build-option compatibility")
endif()
option(VINEMIX_HARDWARE_TESTS "Build explicit opt-in real-endpoint diagnostics (never run automatically)" OFF)
if(VINEMIX_HARDWARE_TESTS)
  # Read-only endpoint/default/session inventory; never initializes a stream.
  add_executable(VineMix.EndpointInventory native/tests/endpoint_inventory.cpp)
  vinemix_verification_target(VineMix.EndpointInventory)
  target_link_libraries(VineMix.EndpointInventory PRIVATE nlohmann_json::nlohmann_json ole32 uuid propsys)

  # Requires --run-muted and explicit endpoint IDs. Uses private settings,
  # captures only the selected virtual path, and mutes every graph node.
  add_executable(VineMix.HardwareTests
    native/tests/engine_hardware_tests.cpp native/src/engine.cpp native/src/windows_audio.cpp
    native/src/process_capture.cpp native/src/configuration.cpp native/src/graph.cpp native/src/dsp.cpp)
  vinemix_verification_target(VineMix.HardwareTests)
  target_link_libraries(VineMix.HardwareTests PRIVATE samplerate nlohmann_json::nlohmann_json
    ole32 uuid avrt propsys advapi32 winmm user32 psapi)

  # Requires --run for streams; refuses physical endpoints. Its quiet stimulus
  # can still follow another app's virtual routes: inspect routing before use.
  # Does not link the engine/control server or configuration persistence.
  add_executable(VineMix.LiveAudioProbe
    native/tests/live_audio_probe.cpp native/src/windows_audio.cpp native/src/process_capture.cpp native/src/dsp.cpp)
  vinemix_verification_target(VineMix.LiveAudioProbe)
  target_link_libraries(VineMix.LiveAudioProbe PRIVATE samplerate nlohmann_json::nlohmann_json
    ole32 uuid avrt propsys advapi32 winmm user32)

  # Requires --run and exactly one discovered application name. Opens only a
  # process-loopback capture client and saves aggregate statistics, never audio.
  # Existing application playback and Windows routing remain unchanged.
  add_executable(VineMix.ApplicationObserve
    native/tests/application_observe.cpp native/src/process_capture.cpp native/src/dsp.cpp)
  vinemix_verification_target(VineMix.ApplicationObserve)
  target_link_libraries(VineMix.ApplicationObserve PRIVATE samplerate nlohmann_json::nlohmann_json
    ole32 uuid avrt user32)

  # Explicit opt-in capture-isolation diagnostics. Observation mode opens no
  # renderer and stores only aggregate levels, never microphone audio files.
  add_executable(VineMix.CaptureIsolationProbe
    native/tests/capture_isolation_probe.cpp native/src/windows_audio.cpp native/src/dsp.cpp)
  vinemix_verification_target(VineMix.CaptureIsolationProbe)
  target_link_libraries(VineMix.CaptureIsolationProbe PRIVATE samplerate nlohmann_json::nlohmann_json
    ole32 uuid avrt propsys winmm user32)
  if(NOT MSVC)
    target_link_options(VineMix.CaptureIsolationProbe PRIVATE -municode)
    # application_observe.cpp uses wmain to preserve Unicode CLI paths/names.
    target_link_options(VineMix.ApplicationObserve PRIVATE -municode)
  endif()
endif()

# Every target using graph.cpp needs the same effect implementation; linking
# the library never executes it or creates an audio endpoint.
if(VINEMIX_OFFLINE_TESTS)
  add_executable(VineMix.AdvancedEffectsTests native/tests/advanced_effects_tests.cpp native/src/graph.cpp native/src/configuration.cpp)
  vinemix_verification_target(VineMix.AdvancedEffectsTests)
  target_link_libraries(VineMix.AdvancedEffectsTests PRIVATE nlohmann_json::nlohmann_json advapi32)
  add_executable(VineMix.DenoiserTests native/tests/denoiser_tests.cpp native/src/graph.cpp native/src/configuration.cpp)
  vinemix_verification_target(VineMix.DenoiserTests)
  target_link_libraries(VineMix.DenoiserTests PRIVATE nlohmann_json::nlohmann_json advapi32 VineMix.Effects)
  add_executable(VineMix.SpectrumTests native/tests/spectrum_tests.cpp native/src/graph.cpp)
  vinemix_verification_target(VineMix.SpectrumTests)
  add_executable(VineMix.DeEsserQualityTests native/tests/deesser_quality_tests.cpp)
  vinemix_verification_target(VineMix.DeEsserQualityTests)
  add_executable(VineMix.EffectsQualityTests native/tests/effects_quality_tests.cpp native/src/graph.cpp)
  vinemix_verification_target(VineMix.EffectsQualityTests)
  add_executable(VineMix.GraphPerformanceTests native/tests/graph_performance_tests.cpp native/src/graph.cpp)
  vinemix_verification_target(VineMix.GraphPerformanceTests)
  add_executable(VineMix.VoiceColorTests native/tests/voice_color_tests.cpp native/src/graph.cpp native/src/configuration.cpp)
  vinemix_verification_target(VineMix.VoiceColorTests)
  target_link_libraries(VineMix.VoiceColorTests PRIVATE nlohmann_json::nlohmann_json advapi32)
  add_executable(VineMix.CoreDynamicsTests native/tests/core_dynamics_tests.cpp native/src/graph.cpp native/src/configuration.cpp)
  vinemix_verification_target(VineMix.CoreDynamicsTests)
  target_link_libraries(VineMix.CoreDynamicsTests PRIVATE nlohmann_json::nlohmann_json advapi32)
endif()
foreach(graph_target VineMix.Engine VineMix.GraphTests VineMix.EngineLifecycleTests
    VineMix.QualificationTests VineMix.HardwareTests VineMix.AdvancedEffectsTests VineMix.SpectrumTests
    VineMix.DeEsserQualityTests VineMix.EffectsQualityTests VineMix.GraphPerformanceTests VineMix.VoiceColorTests VineMix.CoreDynamicsTests)
  if(TARGET ${graph_target})
    target_link_libraries(${graph_target} PRIVATE VineMix.Effects)
  endif()
endforeach()
