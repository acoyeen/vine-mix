//! Runs one read-only update check against the public release history and
//! prints the result: `cargo run -p vinemix-update --example check_live -- 0.4.0-beta`.

fn main() {
    let current = std::env::args().nth(1).unwrap_or_else(|| "0.5.0-beta".into());
    let beta = vinemix_update::SemanticVersion::parse(&current).is_some_and(|v| v.is_prerelease());
    let started = std::time::Instant::now();
    let result = vinemix_update::Trust::production()
        .map_err(vinemix_update::Failure::Invalid)
        .and_then(|trust| vinemix_update::check(&trust, &current, beta, vinemix_update::Compatibility::current(), &|| false));
    match result {
        Ok(vinemix_update::CheckResult::Available(offer)) => println!("{current}: AVAILABLE {} ({:.1} s)", offer.manifest.version, started.elapsed().as_secs_f32()),
        Ok(vinemix_update::CheckResult::UpToDate(message)) => println!("{current}: UP TO DATE: {message} ({:.1} s)", started.elapsed().as_secs_f32()),
        Err(failure) => println!("{current}: FAILED: {failure:?} ({:.1} s)", started.elapsed().as_secs_f32()),
    }
}
