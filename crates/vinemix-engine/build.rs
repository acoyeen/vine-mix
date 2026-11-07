//! Embeds the Windows version resource read by release and update checks.

include!("../../tools/winres.rs");

fn main() {
    winres::embed("VineMix audio engine", "VineMix.Engine", "VineMix.Engine.exe", None, false);
}
