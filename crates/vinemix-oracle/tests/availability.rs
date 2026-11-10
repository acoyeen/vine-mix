//! Makes the state of the parity oracle visible in every test run
//! (RUST-REV-020).
//!
//! The comparison suites in this directory are compiled only when the C/C++
//! reference sources are present. Without this file a checkout that lacks
//! them would print "0 tests" for each suite and still finish green. With it,
//! the run shows one ignored test whose reason says what did not happen. A
//! qualification run sets `VINEMIX_REQUIRE_ORACLE=1` instead, and then the
//! missing sources fail the build.

#[cfg(oracle_available)]
#[test]
fn reference_sources_are_present_and_the_comparisons_are_built() {
    // This function exists only when the build script found the sources and
    // built the oracle; the library must agree.
    let available = std::hint::black_box(vinemix_oracle::AVAILABLE);
    assert!(available);
}

#[cfg(not(oracle_available))]
#[test]
#[ignore = "C/C++ reference sources are missing (scripts/fetch-reference-sources.ps1): no parity comparison ran"]
fn reference_sources_are_present_and_the_comparisons_are_built() {
    panic!("the parity oracle was not built; run scripts/fetch-reference-sources.ps1");
}
