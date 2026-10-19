cmake_minimum_required(VERSION 3.24)
project(AudioMixer VERSION 0.2.0 LANGUAGES C CXX)
if(NOT WIN32)
  message(FATAL_ERROR "Audio Mixer currently targets Windows 11 x64.")
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
add_library(AudioMixer.Speex STATIC
  native/vendor/speex_preprocess_bridge.c
  native/vendor/speex_memory.c
  ${speexdsp_SOURCE_DIR}/libspeexdsp/fftwrap.c
  ${speexdsp_SOURCE_DIR}/libspeexdsp/smallft.c
  ${speexdsp_SOURCE_DIR}/libspeexdsp/filterbank.c
  ${speexdsp_SOURCE_DIR}/libspeexdsp/mdf.c)
target_include_directories(AudioMixer.Speex PUBLIC ${speexdsp_SOURCE_DIR}/include
  PRIVATE ${speexdsp_SOURCE_DIR}/libspeexdsp native/vendor)
target_compile_definitions(AudioMixer.Speex PRIVATE FLOATING_POINT USE_SMALLFT OS_SUPPORT_CUSTOM EXPORT=)
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
add_library(AudioMixer.RNNoise STATIC
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
target_include_directories(AudioMixer.RNNoise PUBLIC ${rnnoise_SOURCE_DIR}/include
  PRIVATE ${rnnoise_SOURCE_DIR}/src ${rnnoise_model_SOURCE_DIR}/src)
target_compile_definitions(AudioMixer.RNNoise PRIVATE RNNOISE_BUILD
  RNN_ENABLE_X86_RTCD CPU_INFO_BY_C
  malloc=am_rnnoise_malloc calloc=am_rnnoise_calloc free=am_rnnoise_free)
if(MSVC)
  set_source_files_properties(${rnnoise_SOURCE_DIR}/src/x86/nnet_avx2.c PROPERTIES COMPILE_OPTIONS /arch:AVX2)
else()
  set_source_files_properties(${rnnoise_SOURCE_DIR}/src/x86/nnet_sse4_1.c PROPERTIES COMPILE_OPTIONS "-msse4.1")
  set_source_files_properties(${rnnoise_SOURCE_DIR}/src/x86/nnet_avx2.c PROPERTIES COMPILE_OPTIONS "-mavx2;-mfma")
endif()
add_library(AudioMixer.Effects STATIC native/src/advanced_effects.cpp native/vendor/rnnoise_memory.c)
target_include_directories(AudioMixer.Effects PUBLIC native/include)
target_link_libraries(AudioMixer.Effects PRIVATE AudioMixer.Speex AudioMixer.RNNoise)
install(FILES ${speexdsp_SOURCE_DIR}/COPYING DESTINATION licenses RENAME SpeexDSP-COPYING.txt)
install(FILES ${rnnoise_SOURCE_DIR}/COPYING DESTINATION licenses RENAME RNNoise-COPYING.txt)

add_executable(AudioMixer.Engine
  native/src/main.cpp
  native/src/configuration.cpp
  native/src/engine.cpp
  native/src/control_server.cpp
  native/src/windows_audio.cpp
  native/src/process_capture.cpp
  native/src/graph.cpp
  native/src/dsp.cpp)
target_include_directories(AudioMixer.Engine PRIVATE native/include)
target_compile_definitions(AudioMixer.Engine PRIVATE
  UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN _WIN32_WINNT=0x0A00 WINVER=0x0A00)
target_link_libraries(AudioMixer.Engine PRIVATE
  samplerate nlohmann_json::nlohmann_json
  ole32 uuid avrt propsys advapi32 winmm user32)
if(MSVC)
  target_compile_options(AudioMixer.Engine PRIVATE /W4 /permissive- /EHsc)
else()
  target_compile_options(AudioMixer.Engine PRIVATE -Wall -Wextra -Wpedantic)
  target_link_options(AudioMixer.Engine PRIVATE -static-libgcc -static-libstdc++ -municode)
endif()

install(TARGETS AudioMixer.Engine RUNTIME DESTINATION .)
install(FILES LICENSE THIRD_PARTY_NOTICES.md DESTINATION .)
install(DIRECTORY licenses/ DESTINATION licenses)
install(FILES docs/DISTRIBUTION.md DESTINATION docs)

function(audio_mixer_verification_target target)
  target_include_directories(${target} PRIVATE native/include)
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
option(AUDIO_MIXER_OFFLINE_TESTS "Build silent graph, settings, lifecycle, drift and queue verification" OFF)
if(AUDIO_MIXER_OFFLINE_TESTS)
  add_executable(AudioMixer.GraphTests
    native/tests/graph_tests.cpp native/src/graph.cpp native/src/configuration.cpp native/src/dsp.cpp)
  target_include_directories(AudioMixer.GraphTests PRIVATE native/include)
  target_compile_definitions(AudioMixer.GraphTests PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
  target_link_libraries(AudioMixer.GraphTests PRIVATE samplerate nlohmann_json::nlohmann_json advapi32)
  if(MSVC)
    target_compile_options(AudioMixer.GraphTests PRIVATE /W4 /permissive- /EHsc)
  else()
    target_compile_options(AudioMixer.GraphTests PRIVATE -Wall -Wextra -Wpedantic)
    target_link_options(AudioMixer.GraphTests PRIVATE -static-libgcc -static-libstdc++)
  endif()
  add_executable(AudioMixer.EngineLifecycleTests
    native/tests/engine_lifecycle_tests.cpp native/src/engine.cpp native/src/graph.cpp native/src/configuration.cpp native/src/dsp.cpp)
  target_include_directories(AudioMixer.EngineLifecycleTests PRIVATE native/include)
  target_compile_definitions(AudioMixer.EngineLifecycleTests PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
  target_link_libraries(AudioMixer.EngineLifecycleTests PRIVATE samplerate nlohmann_json::nlohmann_json advapi32 avrt)
  if(MSVC)
    target_compile_options(AudioMixer.EngineLifecycleTests PRIVATE /W4 /permissive- /EHsc)
  else()
    target_compile_options(AudioMixer.EngineLifecycleTests PRIVATE -Wall -Wextra -Wpedantic)
    target_link_options(AudioMixer.EngineLifecycleTests PRIVATE -static-libgcc -static-libstdc++)
  endif()

  # Uses a caller-supplied new fixture directory, never the user's settings.
  # GraphProcessor is self-contained; no legacy mixer or DSP helper stubs exist
  # in this test, and dsp.cpp/libsamplerate are not needed for this target.
  add_executable(AudioMixer.QualificationTests
    native/tests/qualification_tests.cpp native/src/graph.cpp native/src/configuration.cpp)
  audio_mixer_verification_target(AudioMixer.QualificationTests)
  target_link_libraries(AudioMixer.QualificationTests PRIVATE nlohmann_json::nlohmann_json advapi32)

  # Memory-only queues and resampling; no configuration or device APIs linked.
  add_executable(AudioMixer.DriftQueueTests native/tests/drift_queue_tests.cpp native/src/dsp.cpp)
  audio_mixer_verification_target(AudioMixer.DriftQueueTests)
  target_link_libraries(AudioMixer.DriftQueueTests PRIVATE samplerate)

  # Production inventory collector with fake COM objects only. Does not
  # construct live device catalogs/workers or open any real audio endpoint.
  add_executable(AudioMixer.WindowsCatalogTests
    native/tests/windows_catalog_tests.cpp native/src/windows_audio.cpp native/src/dsp.cpp)
  audio_mixer_verification_target(AudioMixer.WindowsCatalogTests)
  target_link_libraries(AudioMixer.WindowsCatalogTests PRIVATE samplerate nlohmann_json::nlohmann_json ole32 uuid avrt propsys)

  # Fake WASAPI clients exercise exclusive virtual-output initialization and
  # failure handling without opening endpoints or changing personal audio.
  add_executable(AudioMixer.ExclusiveRenderTests
    native/tests/exclusive_render_tests.cpp native/src/windows_audio.cpp native/src/dsp.cpp)
  audio_mixer_verification_target(AudioMixer.ExclusiveRenderTests)
  target_link_libraries(AudioMixer.ExclusiveRenderTests PRIVATE samplerate ole32 uuid avrt propsys)

  add_executable(AudioMixer.SharedAudioTests
    native/tests/shared_audio_tests.cpp native/src/windows_audio.cpp native/src/dsp.cpp)
  audio_mixer_verification_target(AudioMixer.SharedAudioTests)
  target_link_libraries(AudioMixer.SharedAudioTests PRIVATE samplerate ole32 uuid avrt propsys)

  add_executable(AudioMixer.AudioTimingTests native/tests/audio_timing_tests.cpp native/src/dsp.cpp)
  audio_mixer_verification_target(AudioMixer.AudioTimingTests)
  target_link_libraries(AudioMixer.AudioTimingTests PRIVATE samplerate)

  add_executable(AudioMixer.MixClockTests native/tests/mix_clock_tests.cpp)
  audio_mixer_verification_target(AudioMixer.MixClockTests)
endif()

# These are diagnostics for real endpoints, deliberately separate from silent
# offline verification. Building never executes them, installs drivers, alters
# Windows defaults, or adds them to the personal application package.
option(AUDIO_MIXER_HARDWARE_TESTS "Build explicit opt-in real-endpoint diagnostics (never run automatically)" OFF)
if(AUDIO_MIXER_HARDWARE_TESTS)
  # Read-only endpoint/default/session inventory; never initializes a stream.
  add_executable(AudioMixer.EndpointInventory native/tests/endpoint_inventory.cpp)
  audio_mixer_verification_target(AudioMixer.EndpointInventory)
  target_link_libraries(AudioMixer.EndpointInventory PRIVATE nlohmann_json::nlohmann_json ole32 uuid propsys)

  # Requires --run-muted and explicit endpoint IDs. Uses private settings,
  # captures only the selected virtual path, and mutes every graph node.
  add_executable(AudioMixer.HardwareTests
    native/tests/engine_hardware_tests.cpp native/src/engine.cpp native/src/windows_audio.cpp
    native/src/process_capture.cpp native/src/configuration.cpp native/src/graph.cpp native/src/dsp.cpp)
  audio_mixer_verification_target(AudioMixer.HardwareTests)
  target_link_libraries(AudioMixer.HardwareTests PRIVATE samplerate nlohmann_json::nlohmann_json
    ole32 uuid avrt propsys advapi32 winmm user32 psapi)

  # Requires --run for streams; refuses physical endpoints. Its quiet stimulus
  # can still follow another app's virtual routes: inspect routing before use.
  # Does not link the engine/control server or configuration persistence.
  add_executable(AudioMixer.LiveAudioProbe
    native/tests/live_audio_probe.cpp native/src/windows_audio.cpp native/src/process_capture.cpp native/src/dsp.cpp)
  audio_mixer_verification_target(AudioMixer.LiveAudioProbe)
  target_link_libraries(AudioMixer.LiveAudioProbe PRIVATE samplerate nlohmann_json::nlohmann_json
    ole32 uuid avrt propsys advapi32 winmm user32)

  # Requires --run and exactly one discovered application name. Opens only a
  # process-loopback capture client and saves aggregate statistics, never audio.
  # Existing application playback and Windows routing remain unchanged.
  add_executable(AudioMixer.ApplicationObserve
    native/tests/application_observe.cpp native/src/process_capture.cpp native/src/dsp.cpp)
  audio_mixer_verification_target(AudioMixer.ApplicationObserve)
  target_link_libraries(AudioMixer.ApplicationObserve PRIVATE samplerate nlohmann_json::nlohmann_json
    ole32 uuid avrt user32)

  # Explicit opt-in capture-isolation diagnostics. Observation mode opens no
  # renderer and stores only aggregate levels, never microphone audio files.
  add_executable(AudioMixer.CaptureIsolationProbe
    native/tests/capture_isolation_probe.cpp native/src/windows_audio.cpp native/src/dsp.cpp)
  audio_mixer_verification_target(AudioMixer.CaptureIsolationProbe)
  target_link_libraries(AudioMixer.CaptureIsolationProbe PRIVATE samplerate nlohmann_json::nlohmann_json
    ole32 uuid avrt propsys winmm user32)
  if(NOT MSVC)
    target_link_options(AudioMixer.CaptureIsolationProbe PRIVATE -municode)
    # application_observe.cpp uses wmain to preserve Unicode CLI paths/names.
    target_link_options(AudioMixer.ApplicationObserve PRIVATE -municode)
  endif()
endif()

# Every target using graph.cpp needs the same effect implementation; linking
# the library never executes it or creates an audio endpoint.
if(AUDIO_MIXER_OFFLINE_TESTS)
  add_executable(AudioMixer.AdvancedEffectsTests native/tests/advanced_effects_tests.cpp native/src/graph.cpp native/src/configuration.cpp)
  audio_mixer_verification_target(AudioMixer.AdvancedEffectsTests)
  target_link_libraries(AudioMixer.AdvancedEffectsTests PRIVATE nlohmann_json::nlohmann_json advapi32)
  add_executable(AudioMixer.DenoiserTests native/tests/denoiser_tests.cpp native/src/graph.cpp native/src/configuration.cpp)
  audio_mixer_verification_target(AudioMixer.DenoiserTests)
  target_link_libraries(AudioMixer.DenoiserTests PRIVATE nlohmann_json::nlohmann_json advapi32 AudioMixer.Effects)
  add_executable(AudioMixer.SpectrumTests native/tests/spectrum_tests.cpp native/src/graph.cpp)
  audio_mixer_verification_target(AudioMixer.SpectrumTests)
  add_executable(AudioMixer.DeEsserQualityTests native/tests/deesser_quality_tests.cpp)
  audio_mixer_verification_target(AudioMixer.DeEsserQualityTests)
  add_executable(AudioMixer.EffectsQualityTests native/tests/effects_quality_tests.cpp native/src/graph.cpp)
  audio_mixer_verification_target(AudioMixer.EffectsQualityTests)
  add_executable(AudioMixer.GraphPerformanceTests native/tests/graph_performance_tests.cpp native/src/graph.cpp)
  audio_mixer_verification_target(AudioMixer.GraphPerformanceTests)
  add_executable(AudioMixer.VoiceColorTests native/tests/voice_color_tests.cpp native/src/graph.cpp native/src/configuration.cpp)
  audio_mixer_verification_target(AudioMixer.VoiceColorTests)
  target_link_libraries(AudioMixer.VoiceColorTests PRIVATE nlohmann_json::nlohmann_json advapi32)
  add_executable(AudioMixer.CoreDynamicsTests native/tests/core_dynamics_tests.cpp native/src/graph.cpp native/src/configuration.cpp)
  audio_mixer_verification_target(AudioMixer.CoreDynamicsTests)
  target_link_libraries(AudioMixer.CoreDynamicsTests PRIVATE nlohmann_json::nlohmann_json advapi32)
endif()
foreach(graph_target AudioMixer.Engine AudioMixer.GraphTests AudioMixer.EngineLifecycleTests
    AudioMixer.QualificationTests AudioMixer.HardwareTests AudioMixer.AdvancedEffectsTests AudioMixer.SpectrumTests
    AudioMixer.DeEsserQualityTests AudioMixer.EffectsQualityTests AudioMixer.GraphPerformanceTests AudioMixer.VoiceColorTests AudioMixer.CoreDynamicsTests)
  if(TARGET ${graph_target})
    target_link_libraries(${graph_target} PRIVATE AudioMixer.Effects)
  endif()
endforeach()
