//! Read-only diagnostic: how long one catalog enumeration takes on this
//! machine when every endpoint's audio client is activated (a cold cache, as
//! every enumeration did before) and with the mix-format cache warm. Opens no
//! stream and changes nothing.

use std::time::Instant;

use windows::Win32::Media::Audio::{IMMDeviceEnumerator, MMDeviceEnumerator};
use windows::Win32::System::Com::{CoCreateInstance, CLSCTX_ALL};

fn main() {
    let apartment = vinemix_wasapi::win::Apartment::enter();
    assert!(apartment.ok());
    // SAFETY: COM initialised above.
    let enumerator: IMMDeviceEnumerator = unsafe { CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL) }.expect("enumerator");
    let mut previous = Vec::new();
    let mut warm = vinemix_wasapi::catalog::MixFormatCache::new();
    let mut agree = true;
    for pass in 0..8 {
        let mut cold = vinemix_wasapi::catalog::MixFormatCache::new();
        let start = Instant::now();
        let reference = vinemix_wasapi::catalog::collect_endpoints(&enumerator, &previous, &mut cold).expect("inventory");
        let cold_ms = start.elapsed().as_secs_f64() * 1000.0;
        let start = Instant::now();
        let cached = vinemix_wasapi::catalog::collect_endpoints(&enumerator, &previous, &mut warm).expect("inventory");
        let warm_ms = start.elapsed().as_secs_f64() * 1000.0;
        agree &= reference == cached;
        let active = reference.iter().filter(|e| e.state == "Active").count();
        println!("pass {pass}: activate all {cold_ms:>7.2} ms | cached {warm_ms:>7.2} ms | {} endpoints ({active} active)", reference.len());
        previous = reference;
    }
    println!("cached inventory identical to fully activated inventory: {agree}");
}
