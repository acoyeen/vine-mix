//! Builds the 0.5 C/C++ DSP with the 0.5 release flags (-O3 -DNDEBUG, the
//! same defines and per-file ISA options as CMakeLists.txt).

use std::path::{Path, PathBuf};

fn main() {
    println!("cargo::rustc-check-cfg=cfg(oracle_available)");
    let root = PathBuf::from(std::env::var("CARGO_MANIFEST_DIR").unwrap()).join("../..");
    let third = root.join("third_party/src");
    let speex = third.join("speexdsp-1.2.1");
    let rnnoise = third.join("rnnoise-0.2");
    let json = third.join("json-3.12.0/json");
    let samplerate = third.join("libsamplerate-0.2.2");
    let native = root.join("native");
    for path in [&speex, &rnnoise, &json, &samplerate, &third.join("src/rnnoise_data.c")] {
        println!("cargo::rerun-if-changed={}", path.display());
        if !path.exists() {
            println!("cargo::warning=vinemix-oracle: reference sources missing; parity tests are skipped");
            return;
        }
    }
    println!("cargo::rerun-if-changed=cpp");
    println!("cargo::rerun-if-changed={}", native.join("src").display());
    println!("cargo::rerun-if-changed={}", native.join("include").display());

    let release = |build: &mut cc::Build| {
        build.opt_level(3).debug(false).define("NDEBUG", None).warnings(false);
    };

    let mut speex_build = cc::Build::new();
    release(&mut speex_build);
    let libspeex = speex.join("libspeexdsp");
    speex_build
        .include(speex.join("include"))
        .include(&libspeex)
        .include(native.join("vendor"))
        .define("FLOATING_POINT", None)
        .define("USE_SMALLFT", None)
        .define("OS_SUPPORT_CUSTOM", None)
        .define("EXPORT", Some(""))
        .file(native.join("vendor/speex_preprocess_bridge.c"))
        .file(native.join("vendor/speex_memory.c"))
        .file(libspeex.join("fftwrap.c"))
        .file(libspeex.join("smallft.c"))
        .file(libspeex.join("filterbank.c"))
        .file(libspeex.join("mdf.c"))
        .compile("oracle_speex");

    let src = rnnoise.join("src");
    let rnnoise_defines = |build: &mut cc::Build| {
        release(build);
        build
            .include(rnnoise.join("include"))
            .include(&src)
            .include(third.join("src"))
            .define("RNNOISE_BUILD", None)
            .define("RNN_ENABLE_X86_RTCD", None)
            .define("CPU_INFO_BY_C", None)
            .define("malloc", Some("am_rnnoise_malloc"))
            .define("calloc", Some("am_rnnoise_calloc"))
            .define("free", Some("am_rnnoise_free"));
    };
    let mut rn = cc::Build::new();
    rnnoise_defines(&mut rn);
    for file in [
        "denoise.c",
        "rnn.c",
        "pitch.c",
        "kiss_fft.c",
        "celt_lpc.c",
        "nnet.c",
        "nnet_default.c",
        "parse_lpcnet_weights.c",
        "rnnoise_tables.c",
        "x86/x86cpu.c",
        "x86/x86_dnn_map.c",
    ] {
        rn.file(src.join(file));
    }
    rn.file(third.join("src/rnnoise_data.c"));
    rn.compile("oracle_rnnoise");
    let mut sse = cc::Build::new();
    rnnoise_defines(&mut sse);
    sse.flag("-msse4.1").file(src.join("x86/nnet_sse4_1.c")).compile("oracle_rnnoise_sse41");
    let mut avx = cc::Build::new();
    rnnoise_defines(&mut avx);
    avx.flag("-mavx2").flag("-mfma").file(src.join("x86/nnet_avx2.c")).compile("oracle_rnnoise_avx2");
    let mut memory = cc::Build::new();
    release(&mut memory);
    memory.file(native.join("vendor/rnnoise_memory.c")).compile("oracle_rnnoise_memory");

    // libsamplerate 0.2.2 sinc converters, as its own CMake build configures
    // them (all three sinc qualities enabled).
    let mut resampler = cc::Build::new();
    release(&mut resampler);
    resampler
        .include(samplerate.join("include"))
        .include(Path::new("cpp"))
        .define("HAVE_CONFIG_H", None)
        .define("ENABLE_SINC_FAST_CONVERTER", None)
        .define("ENABLE_SINC_MEDIUM_CONVERTER", None)
        .define("ENABLE_SINC_BEST_CONVERTER", None)
        .file(samplerate.join("src/samplerate.c"))
        .file(samplerate.join("src/src_sinc.c"))
        .file(samplerate.join("src/src_linear.c"))
        .file(samplerate.join("src/src_zoh.c"))
        .compile("oracle_samplerate");

    let mut cpp = cc::Build::new();
    release(&mut cpp);
    cpp.cpp(true)
        .std("c++20")
        .cpp_link_stdlib("c++")
        .include(native.join("include"))
        .include(json.join("include"))
        .include(speex.join("include"))
        .include(rnnoise.join("include"))
        .define("UNICODE", None)
        .define("_UNICODE", None)
        .define("NOMINMAX", None)
        .define("WIN32_LEAN_AND_MEAN", None)
        .define("_WIN32_WINNT", Some("0x0A00"))
        .define("WINVER", Some("0x0A00"))
        .file(native.join("src/graph.cpp"))
        .file(native.join("src/advanced_effects.cpp"))
        .file(Path::new("cpp/oracle.cpp"))
        .compile("oracle_graph");
    println!("cargo::rustc-cfg=oracle_available");
}
