//! Embeds the Windows version resource and manifest.

include!("../../tools/winres.rs");

fn main() {
    winres::embed("VineMix setup detection helper", "VineMix.SetupHelper", "VineMix.SetupHelper.exe", None, true);
}
