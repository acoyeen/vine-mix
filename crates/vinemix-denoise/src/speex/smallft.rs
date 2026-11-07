//! Real FFT used by the Speex preprocessor: a line-for-line port of
//! SpeexDSP 1.2.1 `smallft.c` (itself a C translation of FFTPACK, via the
//! Ogg Vorbis project; BSD-style licence, see `licenses/SpeexDSP-COPYING.txt`).
//!
//! The control flow, index arithmetic and single-precision operation order
//! are kept exactly so this transform reproduces the C library bit for bit.
//! Index variables are `isize` because FFTPACK walks offsets through
//! negative intermediate values before indexing.

#![allow(clippy::too_many_arguments, clippy::many_single_char_names)]

macro_rules! at {
    ($slice:expr, $index:expr) => {
        $slice[($index) as usize]
    };
}

/// Precomputed twiddles and factorisation for one transform length.
#[derive(Clone, Debug, PartialEq)]
pub struct DrftLookup {
    n: usize,
    /// `[0, n)` is scratch, `[n, 2n)` holds the twiddles (3n allocated, as C).
    trigcache: Vec<f32>,
    splitcache: [i32; 32],
}

fn cosf(x: f32) -> f32 {
    f64::from(x).cos() as f32
}

fn sinf(x: f32) -> f32 {
    f64::from(x).sin() as f32
}

fn drfti1(n: isize, wa: &mut [f32], ifac: &mut [i32; 32]) {
    const NTRYH: [isize; 4] = [4, 2, 3, 5];
    let tpi: f32 = 6.283_185_307_179_586_48;
    let mut ntry: isize = 0;
    let mut j: isize = -1;
    let mut nl = n;
    let mut nf: isize = 0;
    'l101: loop {
        j += 1;
        if j < 4 {
            ntry = NTRYH[j as usize];
        } else {
            ntry += 2;
        }
        loop {
            // L104
            let nq = nl / ntry;
            let nr = nl - ntry * nq;
            if nr != 0 {
                continue 'l101;
            }
            nf += 1;
            ifac[(nf + 1) as usize] = ntry as i32;
            nl = nq;
            if ntry == 2 && nf != 1 {
                for i in 1..nf {
                    let ib = nf - i + 1;
                    ifac[(ib + 1) as usize] = ifac[ib as usize];
                }
                ifac[2] = 2;
            }
            // L107
            if nl != 1 {
                continue;
            }
            break 'l101;
        }
    }
    ifac[0] = n as i32;
    ifac[1] = nf as i32;
    let argh = tpi / n as f32;
    let mut is: isize = 0;
    let nfm1 = nf - 1;
    let mut l1: isize = 1;
    if nfm1 == 0 {
        return;
    }
    for k1 in 0..nfm1 {
        let ip = ifac[(k1 + 2) as usize] as isize;
        let mut ld: isize = 0;
        let l2 = l1 * ip;
        let ido = n / l2;
        let ipm = ip - 1;
        for _ in 0..ipm {
            ld += l1;
            let mut i = is;
            let argld = ld as f32 * argh;
            let mut fi = 0.0f32;
            let mut ii = 2;
            while ii < ido {
                fi += 1.0;
                let arg = fi * argld;
                at!(wa, i) = cosf(arg);
                i += 1;
                at!(wa, i) = sinf(arg);
                i += 1;
                ii += 2;
            }
            is += ido;
        }
        l1 = l2;
    }
}

fn dradf2(ido: isize, l1: isize, cc: &[f32], ch: &mut [f32], wa1: &[f32]) {
    let mut t1: isize = 0;
    let t0 = l1 * ido;
    let mut t2 = t0;
    let mut t3 = ido << 1;
    for _ in 0..l1 {
        at!(ch, t1 << 1) = at!(cc, t1) + at!(cc, t2);
        at!(ch, (t1 << 1) + t3 - 1) = at!(cc, t1) - at!(cc, t2);
        t1 += ido;
        t2 += ido;
    }
    if ido < 2 {
        return;
    }
    if ido != 2 {
        t1 = 0;
        t2 = t0;
        for _ in 0..l1 {
            t3 = t2;
            let mut t4 = (t1 << 1) + (ido << 1);
            let mut t5 = t1;
            let mut t6 = t1 + t1;
            let mut i = 2;
            while i < ido {
                t3 += 2;
                t4 -= 2;
                t5 += 2;
                t6 += 2;
                let tr2 = at!(wa1, i - 2) * at!(cc, t3 - 1) + at!(wa1, i - 1) * at!(cc, t3);
                let ti2 = at!(wa1, i - 2) * at!(cc, t3) - at!(wa1, i - 1) * at!(cc, t3 - 1);
                at!(ch, t6) = at!(cc, t5) + ti2;
                at!(ch, t4) = ti2 - at!(cc, t5);
                at!(ch, t6 - 1) = at!(cc, t5 - 1) + tr2;
                at!(ch, t4 - 1) = at!(cc, t5 - 1) - tr2;
                i += 2;
            }
            t1 += ido;
            t2 += ido;
        }
        if ido % 2 == 1 {
            return;
        }
    }
    // L105
    t1 = ido;
    t2 = t1 - 1;
    t3 = t2;
    t2 += t0;
    for _ in 0..l1 {
        at!(ch, t1) = -at!(cc, t2);
        at!(ch, t1 - 1) = at!(cc, t3);
        t1 += ido << 1;
        t2 += ido;
        t3 += ido;
    }
}

fn dradf4(ido: isize, l1: isize, cc: &[f32], ch: &mut [f32], wa1: &[f32], wa2: &[f32], wa3: &[f32]) {
    let hsqt2: f32 = 0.707_106_781_186_547_52;
    let t0 = l1 * ido;
    let mut t1 = t0;
    let mut t4 = t1 << 1;
    let mut t2 = t1 + (t1 << 1);
    let mut t3: isize = 0;
    for _ in 0..l1 {
        let tr1 = at!(cc, t1) + at!(cc, t2);
        let tr2 = at!(cc, t3) + at!(cc, t4);
        let mut t5 = t3 << 2;
        at!(ch, t5) = tr1 + tr2;
        at!(ch, (ido << 2) + t5 - 1) = tr2 - tr1;
        t5 += ido << 1;
        at!(ch, t5 - 1) = at!(cc, t3) - at!(cc, t4);
        at!(ch, t5) = at!(cc, t2) - at!(cc, t1);
        t1 += ido;
        t2 += ido;
        t3 += ido;
        t4 += ido;
    }
    if ido < 2 {
        return;
    }
    if ido != 2 {
        t1 = 0;
        for _ in 0..l1 {
            t2 = t1;
            t4 = t1 << 2;
            let t6 = ido << 1;
            let mut t5 = t6 + t4;
            let mut i = 2;
            while i < ido {
                t2 += 2;
                t3 = t2;
                t4 += 2;
                t5 -= 2;
                t3 += t0;
                let cr2 = at!(wa1, i - 2) * at!(cc, t3 - 1) + at!(wa1, i - 1) * at!(cc, t3);
                let ci2 = at!(wa1, i - 2) * at!(cc, t3) - at!(wa1, i - 1) * at!(cc, t3 - 1);
                t3 += t0;
                let cr3 = at!(wa2, i - 2) * at!(cc, t3 - 1) + at!(wa2, i - 1) * at!(cc, t3);
                let ci3 = at!(wa2, i - 2) * at!(cc, t3) - at!(wa2, i - 1) * at!(cc, t3 - 1);
                t3 += t0;
                let cr4 = at!(wa3, i - 2) * at!(cc, t3 - 1) + at!(wa3, i - 1) * at!(cc, t3);
                let ci4 = at!(wa3, i - 2) * at!(cc, t3) - at!(wa3, i - 1) * at!(cc, t3 - 1);
                let tr1 = cr2 + cr4;
                let tr4 = cr4 - cr2;
                let ti1 = ci2 + ci4;
                let ti4 = ci2 - ci4;
                let ti2 = at!(cc, t2) + ci3;
                let ti3 = at!(cc, t2) - ci3;
                let tr2 = at!(cc, t2 - 1) + cr3;
                let tr3 = at!(cc, t2 - 1) - cr3;
                at!(ch, t4 - 1) = tr1 + tr2;
                at!(ch, t4) = ti1 + ti2;
                at!(ch, t5 - 1) = tr3 - ti4;
                at!(ch, t5) = tr4 - ti3;
                at!(ch, t4 + t6 - 1) = ti4 + tr3;
                at!(ch, t4 + t6) = tr4 + ti3;
                at!(ch, t5 + t6 - 1) = tr2 - tr1;
                at!(ch, t5 + t6) = ti1 - ti2;
                i += 2;
            }
            t1 += ido;
        }
        if ido & 1 != 0 {
            return;
        }
    }
    // L105
    t1 = t0 + ido - 1;
    t2 = t1 + (t0 << 1);
    t3 = ido << 2;
    t4 = ido;
    let t5 = ido << 1;
    let mut t6 = ido;
    for _ in 0..l1 {
        let ti1 = -hsqt2 * (at!(cc, t1) + at!(cc, t2));
        let tr1 = hsqt2 * (at!(cc, t1) - at!(cc, t2));
        at!(ch, t4 - 1) = tr1 + at!(cc, t6 - 1);
        at!(ch, t4 + t5 - 1) = at!(cc, t6 - 1) - tr1;
        at!(ch, t4) = ti1 - at!(cc, t1 + t0);
        at!(ch, t4 + t5) = ti1 + at!(cc, t1 + t0);
        t1 += ido;
        t2 += ido;
        t4 += t3;
        t6 += ido;
    }
}

/// General radix forward pass. In C, `cc`, `c1` and `c2` alias one buffer and
/// `ch`, `ch2` alias the other; here each pair is the same slice.
fn dradfg(ido: isize, ip: isize, l1: isize, idl1: isize, cc: &mut [f32], ch: &mut [f32], wa: &[f32]) {
    let tpi: f32 = 6.283_185_307_179_586;
    let arg = tpi / ip as f32;
    let dcp = cosf(arg);
    let dsp = sinf(arg);
    let ipph = (ip + 1) >> 1;
    let ipp2 = ip;
    let idp2 = ido;
    let nbd = (ido - 1) >> 1;
    let t0 = l1 * ido;
    let t10 = ip * ido;
    let (mut t1, mut t2, mut t3, mut t4, mut t5, mut t6, mut t7, mut t8, mut t9);

    if ido != 1 {
        for ik in 0..idl1 {
            at!(ch, ik) = at!(cc, ik);
        }
        t1 = 0;
        for _ in 1..ip {
            t1 += t0;
            t2 = t1;
            for _ in 0..l1 {
                at!(ch, t2) = at!(cc, t2);
                t2 += ido;
            }
        }
        let mut is = -ido;
        t1 = 0;
        if nbd > l1 {
            for _ in 1..ip {
                t1 += t0;
                is += ido;
                t2 = -ido + t1;
                for _ in 0..l1 {
                    let mut idij = is - 1;
                    t2 += ido;
                    t3 = t2;
                    let mut i = 2;
                    while i < ido {
                        idij += 2;
                        t3 += 2;
                        at!(ch, t3 - 1) = at!(wa, idij - 1) * at!(cc, t3 - 1) + at!(wa, idij) * at!(cc, t3);
                        at!(ch, t3) = at!(wa, idij - 1) * at!(cc, t3) - at!(wa, idij) * at!(cc, t3 - 1);
                        i += 2;
                    }
                }
            }
        } else {
            for _ in 1..ip {
                is += ido;
                let mut idij = is - 1;
                t1 += t0;
                t2 = t1;
                let mut i = 2;
                while i < ido {
                    idij += 2;
                    t2 += 2;
                    t3 = t2;
                    for _ in 0..l1 {
                        at!(ch, t3 - 1) = at!(wa, idij - 1) * at!(cc, t3 - 1) + at!(wa, idij) * at!(cc, t3);
                        at!(ch, t3) = at!(wa, idij - 1) * at!(cc, t3) - at!(wa, idij) * at!(cc, t3 - 1);
                        t3 += ido;
                    }
                    i += 2;
                }
            }
        }
        t1 = 0;
        t2 = ipp2 * t0;
        if nbd < l1 {
            for _ in 1..ipph {
                t1 += t0;
                t2 -= t0;
                t3 = t1;
                t4 = t2;
                let mut i = 2;
                while i < ido {
                    t3 += 2;
                    t4 += 2;
                    t5 = t3 - ido;
                    t6 = t4 - ido;
                    for _ in 0..l1 {
                        t5 += ido;
                        t6 += ido;
                        at!(cc, t5 - 1) = at!(ch, t5 - 1) + at!(ch, t6 - 1);
                        at!(cc, t6 - 1) = at!(ch, t5) - at!(ch, t6);
                        at!(cc, t5) = at!(ch, t5) + at!(ch, t6);
                        at!(cc, t6) = at!(ch, t6 - 1) - at!(ch, t5 - 1);
                    }
                    i += 2;
                }
            }
        } else {
            for _ in 1..ipph {
                t1 += t0;
                t2 -= t0;
                t3 = t1;
                t4 = t2;
                for _ in 0..l1 {
                    t5 = t3;
                    t6 = t4;
                    let mut i = 2;
                    while i < ido {
                        t5 += 2;
                        t6 += 2;
                        at!(cc, t5 - 1) = at!(ch, t5 - 1) + at!(ch, t6 - 1);
                        at!(cc, t6 - 1) = at!(ch, t5) - at!(ch, t6);
                        at!(cc, t5) = at!(ch, t5) + at!(ch, t6);
                        at!(cc, t6) = at!(ch, t6 - 1) - at!(ch, t5 - 1);
                        i += 2;
                    }
                    t3 += ido;
                    t4 += ido;
                }
            }
        }
    }
    // L119
    for ik in 0..idl1 {
        at!(cc, ik) = at!(ch, ik);
    }
    t1 = 0;
    t2 = ipp2 * idl1;
    for _ in 1..ipph {
        t1 += t0;
        t2 -= t0;
        t3 = t1 - ido;
        t4 = t2 - ido;
        for _ in 0..l1 {
            t3 += ido;
            t4 += ido;
            at!(cc, t3) = at!(ch, t3) + at!(ch, t4);
            at!(cc, t4) = at!(ch, t4) - at!(ch, t3);
        }
    }
    let mut ar1: f32 = 1.0;
    let mut ai1: f32 = 0.0;
    t1 = 0;
    t2 = ipp2 * idl1;
    t3 = (ip - 1) * idl1;
    for _ in 1..ipph {
        t1 += idl1;
        t2 -= idl1;
        let ar1h = dcp * ar1 - dsp * ai1;
        ai1 = dcp * ai1 + dsp * ar1;
        ar1 = ar1h;
        t4 = t1;
        t5 = t2;
        t6 = t3;
        t7 = idl1;
        for ik in 0..idl1 {
            at!(ch, t4) = at!(cc, ik) + ar1 * at!(cc, t7);
            t4 += 1;
            t7 += 1;
            at!(ch, t5) = ai1 * at!(cc, t6);
            t5 += 1;
            t6 += 1;
        }
        let dc2 = ar1;
        let ds2 = ai1;
        let mut ar2 = ar1;
        let mut ai2 = ai1;
        t4 = idl1;
        t5 = (ipp2 - 1) * idl1;
        for _ in 2..ipph {
            t4 += idl1;
            t5 -= idl1;
            let ar2h = dc2 * ar2 - ds2 * ai2;
            ai2 = dc2 * ai2 + ds2 * ar2;
            ar2 = ar2h;
            t6 = t1;
            t7 = t2;
            t8 = t4;
            t9 = t5;
            for _ in 0..idl1 {
                at!(ch, t6) += ar2 * at!(cc, t8);
                t6 += 1;
                t8 += 1;
                at!(ch, t7) += ai2 * at!(cc, t9);
                t7 += 1;
                t9 += 1;
            }
        }
    }
    t1 = 0;
    for _ in 1..ipph {
        t1 += idl1;
        t2 = t1;
        for ik in 0..idl1 {
            at!(ch, ik) += at!(cc, t2);
            t2 += 1;
        }
    }
    if ido < l1 {
        // L132
        for i in 0..ido {
            t1 = i;
            t2 = i;
            for _ in 0..l1 {
                at!(cc, t2) = at!(ch, t1);
                t1 += ido;
                t2 += t10;
            }
        }
    } else {
        t1 = 0;
        t2 = 0;
        for _ in 0..l1 {
            t3 = t1;
            t4 = t2;
            for _ in 0..ido {
                at!(cc, t4) = at!(ch, t3);
                t4 += 1;
                t3 += 1;
            }
            t1 += ido;
            t2 += t10;
        }
    }
    // L135
    t1 = 0;
    t2 = ido << 1;
    t3 = 0;
    t4 = ipp2 * t0;
    for _ in 1..ipph {
        t1 += t2;
        t3 += t0;
        t4 -= t0;
        t5 = t1;
        t6 = t3;
        t7 = t4;
        for _ in 0..l1 {
            at!(cc, t5 - 1) = at!(ch, t6);
            at!(cc, t5) = at!(ch, t7);
            t5 += t10;
            t6 += ido;
            t7 += ido;
        }
    }
    if ido == 1 {
        return;
    }
    if nbd >= l1 {
        t1 = -ido;
        t3 = 0;
        t4 = 0;
        t5 = ipp2 * t0;
        for _ in 1..ipph {
            t1 += t2;
            t3 += t2;
            t4 += t0;
            t5 -= t0;
            t6 = t1;
            t7 = t3;
            t8 = t4;
            t9 = t5;
            for _ in 0..l1 {
                let mut i = 2;
                while i < ido {
                    let ic = idp2 - i;
                    at!(cc, i + t7 - 1) = at!(ch, i + t8 - 1) + at!(ch, i + t9 - 1);
                    at!(cc, ic + t6 - 1) = at!(ch, i + t8 - 1) - at!(ch, i + t9 - 1);
                    at!(cc, i + t7) = at!(ch, i + t8) + at!(ch, i + t9);
                    at!(cc, ic + t6) = at!(ch, i + t9) - at!(ch, i + t8);
                    i += 2;
                }
                t6 += t10;
                t7 += t10;
                t8 += ido;
                t9 += ido;
            }
        }
        return;
    }
    // L141
    t1 = -ido;
    t3 = 0;
    t4 = 0;
    t5 = ipp2 * t0;
    for _ in 1..ipph {
        t1 += t2;
        t3 += t2;
        t4 += t0;
        t5 -= t0;
        let mut i = 2;
        while i < ido {
            t6 = idp2 + t1 - i;
            t7 = i + t3;
            t8 = i + t4;
            t9 = i + t5;
            for _ in 0..l1 {
                at!(cc, t7 - 1) = at!(ch, t8 - 1) + at!(ch, t9 - 1);
                at!(cc, t6 - 1) = at!(ch, t8 - 1) - at!(ch, t9 - 1);
                at!(cc, t7) = at!(ch, t8) + at!(ch, t9);
                at!(cc, t6) = at!(ch, t9) - at!(ch, t8);
                t6 += t10;
                t7 += t10;
                t8 += ido;
                t9 += ido;
            }
            i += 2;
        }
    }
}

fn drftf1(n: isize, c: &mut [f32], ch: &mut [f32], wa: &[f32], ifac: &[i32; 32]) {
    let nf = ifac[1] as isize;
    let mut na: isize = 1;
    let mut l2 = n;
    let mut iw = n;
    for k1 in 0..nf {
        let kh = nf - k1;
        let ip = ifac[(kh + 1) as usize] as isize;
        let l1 = l2 / ip;
        let ido = n / l2;
        let idl1 = ido * l1;
        iw -= (ip - 1) * ido;
        na = 1 - na;
        let w = |offset: isize| &wa[(offset - 1) as usize..];
        if ip == 4 {
            let ix2 = iw + ido;
            let ix3 = ix2 + ido;
            if na != 0 {
                dradf4(ido, l1, ch, c, w(iw), w(ix2), w(ix3));
            } else {
                dradf4(ido, l1, c, ch, w(iw), w(ix2), w(ix3));
            }
        } else if ip == 2 {
            if na != 0 {
                dradf2(ido, l1, ch, c, w(iw));
            } else {
                dradf2(ido, l1, c, ch, w(iw));
            }
        } else {
            if ido == 1 {
                na = 1 - na;
            }
            if na != 0 {
                dradfg(ido, ip, l1, idl1, ch, c, w(iw));
                na = 0;
            } else {
                dradfg(ido, ip, l1, idl1, c, ch, w(iw));
                na = 1;
            }
        }
        l2 = l1;
    }
    if na == 1 {
        return;
    }
    c[..n as usize].copy_from_slice(&ch[..n as usize]);
}

fn dradb2(ido: isize, l1: isize, cc: &[f32], ch: &mut [f32], wa1: &[f32]) {
    let t0 = l1 * ido;
    let mut t1: isize = 0;
    let mut t2: isize = 0;
    let t3 = (ido << 1) - 1;
    for _ in 0..l1 {
        at!(ch, t1) = at!(cc, t2) + at!(cc, t3 + t2);
        at!(ch, t1 + t0) = at!(cc, t2) - at!(cc, t3 + t2);
        t1 += ido;
        t2 = t1 << 1;
    }
    if ido < 2 {
        return;
    }
    if ido != 2 {
        t1 = 0;
        t2 = 0;
        for _ in 0..l1 {
            let mut t3 = t1;
            let mut t4 = t2;
            let mut t5 = t4 + (ido << 1);
            let mut t6 = t0 + t1;
            let mut i = 2;
            while i < ido {
                t3 += 2;
                t4 += 2;
                t5 -= 2;
                t6 += 2;
                at!(ch, t3 - 1) = at!(cc, t4 - 1) + at!(cc, t5 - 1);
                let tr2 = at!(cc, t4 - 1) - at!(cc, t5 - 1);
                at!(ch, t3) = at!(cc, t4) - at!(cc, t5);
                let ti2 = at!(cc, t4) + at!(cc, t5);
                at!(ch, t6 - 1) = at!(wa1, i - 2) * tr2 - at!(wa1, i - 1) * ti2;
                at!(ch, t6) = at!(wa1, i - 2) * ti2 + at!(wa1, i - 1) * tr2;
                i += 2;
            }
            t1 += ido;
            t2 = t1 << 1;
        }
        if ido % 2 == 1 {
            return;
        }
    }
    // L105
    t1 = ido - 1;
    t2 = ido - 1;
    for _ in 0..l1 {
        at!(ch, t1) = at!(cc, t2) + at!(cc, t2);
        at!(ch, t1 + t0) = -(at!(cc, t2 + 1) + at!(cc, t2 + 1));
        t1 += ido;
        t2 += ido << 1;
    }
}

fn dradb3(ido: isize, l1: isize, cc: &[f32], ch: &mut [f32], wa1: &[f32], wa2: &[f32]) {
    let taur: f32 = -0.5;
    let taui: f32 = 0.866_025_403_784_438_6;
    let t0 = l1 * ido;
    let mut t1: isize = 0;
    let t2 = t0 << 1;
    let mut t3 = ido << 1;
    let t4 = ido + (ido << 1);
    let mut t5: isize = 0;
    for _ in 0..l1 {
        let tr2 = at!(cc, t3 - 1) + at!(cc, t3 - 1);
        let cr2 = at!(cc, t5) + (taur * tr2);
        at!(ch, t1) = at!(cc, t5) + tr2;
        let ci3 = taui * (at!(cc, t3) + at!(cc, t3));
        at!(ch, t1 + t0) = cr2 - ci3;
        at!(ch, t1 + t2) = cr2 + ci3;
        t1 += ido;
        t3 += t4;
        t5 += t4;
    }
    if ido == 1 {
        return;
    }
    t1 = 0;
    t3 = ido << 1;
    for _ in 0..l1 {
        let mut t7 = t1 + (t1 << 1);
        t5 = t7 + t3;
        let mut t6 = t5;
        let mut t8 = t1;
        let mut t9 = t1 + t0;
        let mut t10 = t9 + t0;
        let mut i = 2;
        while i < ido {
            t5 += 2;
            t6 -= 2;
            t7 += 2;
            t8 += 2;
            t9 += 2;
            t10 += 2;
            let tr2 = at!(cc, t5 - 1) + at!(cc, t6 - 1);
            let cr2 = at!(cc, t7 - 1) + (taur * tr2);
            at!(ch, t8 - 1) = at!(cc, t7 - 1) + tr2;
            let ti2 = at!(cc, t5) - at!(cc, t6);
            let ci2 = at!(cc, t7) + (taur * ti2);
            at!(ch, t8) = at!(cc, t7) + ti2;
            let cr3 = taui * (at!(cc, t5 - 1) - at!(cc, t6 - 1));
            let ci3 = taui * (at!(cc, t5) + at!(cc, t6));
            let dr2 = cr2 - ci3;
            let dr3 = cr2 + ci3;
            let di2 = ci2 + cr3;
            let di3 = ci2 - cr3;
            at!(ch, t9 - 1) = at!(wa1, i - 2) * dr2 - at!(wa1, i - 1) * di2;
            at!(ch, t9) = at!(wa1, i - 2) * di2 + at!(wa1, i - 1) * dr2;
            at!(ch, t10 - 1) = at!(wa2, i - 2) * dr3 - at!(wa2, i - 1) * di3;
            at!(ch, t10) = at!(wa2, i - 2) * di3 + at!(wa2, i - 1) * dr3;
            i += 2;
        }
        t1 += ido;
    }
}

fn dradb4(ido: isize, l1: isize, cc: &[f32], ch: &mut [f32], wa1: &[f32], wa2: &[f32], wa3: &[f32]) {
    let sqrt2: f32 = 1.414_213_562_373_095;
    let t0 = l1 * ido;
    let mut t1: isize = 0;
    let mut t2 = ido << 2;
    let mut t3: isize = 0;
    let t6 = ido << 1;
    for _ in 0..l1 {
        let mut t4 = t3 + t6;
        let mut t5 = t1;
        let tr3 = at!(cc, t4 - 1) + at!(cc, t4 - 1);
        let tr4 = at!(cc, t4) + at!(cc, t4);
        t4 += t6;
        let tr1 = at!(cc, t3) - at!(cc, t4 - 1);
        let tr2 = at!(cc, t3) + at!(cc, t4 - 1);
        at!(ch, t5) = tr2 + tr3;
        t5 += t0;
        at!(ch, t5) = tr1 - tr4;
        t5 += t0;
        at!(ch, t5) = tr2 - tr3;
        t5 += t0;
        at!(ch, t5) = tr1 + tr4;
        t1 += ido;
        t3 += t2;
    }
    if ido < 2 {
        return;
    }
    if ido != 2 {
        t1 = 0;
        for _ in 0..l1 {
            t2 = t1 << 2;
            t3 = t2 + t6;
            let mut t4 = t3;
            let mut t5 = t4 + t6;
            let mut t7 = t1;
            let mut i = 2;
            while i < ido {
                t2 += 2;
                t3 += 2;
                t4 -= 2;
                t5 -= 2;
                t7 += 2;
                let ti1 = at!(cc, t2) + at!(cc, t5);
                let ti2 = at!(cc, t2) - at!(cc, t5);
                let ti3 = at!(cc, t3) - at!(cc, t4);
                let tr4 = at!(cc, t3) + at!(cc, t4);
                let tr1 = at!(cc, t2 - 1) - at!(cc, t5 - 1);
                let tr2 = at!(cc, t2 - 1) + at!(cc, t5 - 1);
                let ti4 = at!(cc, t3 - 1) - at!(cc, t4 - 1);
                let tr3 = at!(cc, t3 - 1) + at!(cc, t4 - 1);
                at!(ch, t7 - 1) = tr2 + tr3;
                let cr3 = tr2 - tr3;
                at!(ch, t7) = ti2 + ti3;
                let ci3 = ti2 - ti3;
                let cr2 = tr1 - tr4;
                let cr4 = tr1 + tr4;
                let ci2 = ti1 + ti4;
                let ci4 = ti1 - ti4;
                let mut t8 = t7 + t0;
                at!(ch, t8 - 1) = at!(wa1, i - 2) * cr2 - at!(wa1, i - 1) * ci2;
                at!(ch, t8) = at!(wa1, i - 2) * ci2 + at!(wa1, i - 1) * cr2;
                t8 += t0;
                at!(ch, t8 - 1) = at!(wa2, i - 2) * cr3 - at!(wa2, i - 1) * ci3;
                at!(ch, t8) = at!(wa2, i - 2) * ci3 + at!(wa2, i - 1) * cr3;
                t8 += t0;
                at!(ch, t8 - 1) = at!(wa3, i - 2) * cr4 - at!(wa3, i - 1) * ci4;
                at!(ch, t8) = at!(wa3, i - 2) * ci4 + at!(wa3, i - 1) * cr4;
                i += 2;
            }
            t1 += ido;
        }
        if ido % 2 == 1 {
            return;
        }
    }
    // L105
    t1 = ido;
    t2 = ido << 2;
    t3 = ido - 1;
    let mut t4 = ido + (ido << 1);
    for _ in 0..l1 {
        let mut t5 = t3;
        let ti1 = at!(cc, t1) + at!(cc, t4);
        let ti2 = at!(cc, t4) - at!(cc, t1);
        let tr1 = at!(cc, t1 - 1) - at!(cc, t4 - 1);
        let tr2 = at!(cc, t1 - 1) + at!(cc, t4 - 1);
        at!(ch, t5) = tr2 + tr2;
        t5 += t0;
        at!(ch, t5) = sqrt2 * (tr1 - ti1);
        t5 += t0;
        at!(ch, t5) = ti2 + ti2;
        t5 += t0;
        at!(ch, t5) = -sqrt2 * (tr1 + ti1);
        t3 += ido;
        t1 += t2;
        t4 += t2;
    }
}

/// General radix backward pass (same aliasing note as [`dradfg`]).
fn dradbg(ido: isize, ip: isize, l1: isize, idl1: isize, cc: &mut [f32], ch: &mut [f32], wa: &[f32]) {
    let tpi: f32 = 6.283_185_307_179_586;
    let t10 = ip * ido;
    let t0 = l1 * ido;
    let arg = tpi / ip as f32;
    let dcp = cosf(arg);
    let dsp = sinf(arg);
    let nbd = (ido - 1) >> 1;
    let ipp2 = ip;
    let ipph = (ip + 1) >> 1;
    let (mut t1, mut t2, mut t3, mut t4, mut t5, mut t6, mut t7, mut t8, mut t9, mut t11, mut t12);
    if ido < l1 {
        // L103
        t1 = 0;
        for _ in 0..ido {
            t2 = t1;
            t3 = t1;
            for _ in 0..l1 {
                at!(ch, t2) = at!(cc, t3);
                t2 += ido;
                t3 += t10;
            }
            t1 += 1;
        }
    } else {
        t1 = 0;
        t2 = 0;
        for _ in 0..l1 {
            t3 = t1;
            t4 = t2;
            for _ in 0..ido {
                at!(ch, t3) = at!(cc, t4);
                t3 += 1;
                t4 += 1;
            }
            t1 += ido;
            t2 += t10;
        }
    }
    // L106
    t1 = 0;
    t2 = ipp2 * t0;
    t5 = ido << 1;
    t7 = t5;
    for _ in 1..ipph {
        t1 += t0;
        t2 -= t0;
        t3 = t1;
        t4 = t2;
        t6 = t5;
        for _ in 0..l1 {
            at!(ch, t3) = at!(cc, t6 - 1) + at!(cc, t6 - 1);
            at!(ch, t4) = at!(cc, t6) + at!(cc, t6);
            t3 += ido;
            t4 += ido;
            t6 += t10;
        }
        t5 += t7;
    }
    if ido != 1 {
        if nbd >= l1 {
            t1 = 0;
            t2 = ipp2 * t0;
            t7 = 0;
            for _ in 1..ipph {
                t1 += t0;
                t2 -= t0;
                t3 = t1;
                t4 = t2;
                t7 += ido << 1;
                t8 = t7;
                for _ in 0..l1 {
                    t5 = t3;
                    t6 = t4;
                    t9 = t8;
                    t11 = t8;
                    let mut i = 2;
                    while i < ido {
                        t5 += 2;
                        t6 += 2;
                        t9 += 2;
                        t11 -= 2;
                        at!(ch, t5 - 1) = at!(cc, t9 - 1) + at!(cc, t11 - 1);
                        at!(ch, t6 - 1) = at!(cc, t9 - 1) - at!(cc, t11 - 1);
                        at!(ch, t5) = at!(cc, t9) - at!(cc, t11);
                        at!(ch, t6) = at!(cc, t9) + at!(cc, t11);
                        i += 2;
                    }
                    t3 += ido;
                    t4 += ido;
                    t8 += t10;
                }
            }
        } else {
            // L112
            t1 = 0;
            t2 = ipp2 * t0;
            t7 = 0;
            for _ in 1..ipph {
                t1 += t0;
                t2 -= t0;
                t3 = t1;
                t4 = t2;
                t7 += ido << 1;
                t8 = t7;
                t9 = t7;
                let mut i = 2;
                while i < ido {
                    t3 += 2;
                    t4 += 2;
                    t8 += 2;
                    t9 -= 2;
                    t5 = t3;
                    t6 = t4;
                    t11 = t8;
                    t12 = t9;
                    for _ in 0..l1 {
                        at!(ch, t5 - 1) = at!(cc, t11 - 1) + at!(cc, t12 - 1);
                        at!(ch, t6 - 1) = at!(cc, t11 - 1) - at!(cc, t12 - 1);
                        at!(ch, t5) = at!(cc, t11) - at!(cc, t12);
                        at!(ch, t6) = at!(cc, t11) + at!(cc, t12);
                        t5 += ido;
                        t6 += ido;
                        t11 += t10;
                        t12 += t10;
                    }
                    i += 2;
                }
            }
        }
    }
    // L116
    let mut ar1: f32 = 1.0;
    let mut ai1: f32 = 0.0;
    t1 = 0;
    t2 = ipp2 * idl1;
    t9 = t2;
    t3 = (ip - 1) * idl1;
    for _ in 1..ipph {
        t1 += idl1;
        t2 -= idl1;
        let ar1h = dcp * ar1 - dsp * ai1;
        ai1 = dcp * ai1 + dsp * ar1;
        ar1 = ar1h;
        t4 = t1;
        t5 = t2;
        t6 = 0;
        t7 = idl1;
        t8 = t3;
        for _ in 0..idl1 {
            at!(cc, t4) = at!(ch, t6) + ar1 * at!(ch, t7);
            t4 += 1;
            t6 += 1;
            t7 += 1;
            at!(cc, t5) = ai1 * at!(ch, t8);
            t5 += 1;
            t8 += 1;
        }
        let dc2 = ar1;
        let ds2 = ai1;
        let mut ar2 = ar1;
        let mut ai2 = ai1;
        t6 = idl1;
        t7 = t9 - idl1;
        for _ in 2..ipph {
            t6 += idl1;
            t7 -= idl1;
            let ar2h = dc2 * ar2 - ds2 * ai2;
            ai2 = dc2 * ai2 + ds2 * ar2;
            ar2 = ar2h;
            t4 = t1;
            t5 = t2;
            t11 = t6;
            t12 = t7;
            for _ in 0..idl1 {
                at!(cc, t4) += ar2 * at!(ch, t11);
                t4 += 1;
                t11 += 1;
                at!(cc, t5) += ai2 * at!(ch, t12);
                t5 += 1;
                t12 += 1;
            }
        }
    }
    t1 = 0;
    for _ in 1..ipph {
        t1 += idl1;
        t2 = t1;
        for ik in 0..idl1 {
            at!(ch, ik) += at!(ch, t2);
            t2 += 1;
        }
    }
    t1 = 0;
    t2 = ipp2 * t0;
    for _ in 1..ipph {
        t1 += t0;
        t2 -= t0;
        t3 = t1;
        t4 = t2;
        for _ in 0..l1 {
            at!(ch, t3) = at!(cc, t3) - at!(cc, t4);
            at!(ch, t4) = at!(cc, t3) + at!(cc, t4);
            t3 += ido;
            t4 += ido;
        }
    }
    if ido != 1 {
        if nbd >= l1 {
            t1 = 0;
            t2 = ipp2 * t0;
            for _ in 1..ipph {
                t1 += t0;
                t2 -= t0;
                t3 = t1;
                t4 = t2;
                for _ in 0..l1 {
                    t5 = t3;
                    t6 = t4;
                    let mut i = 2;
                    while i < ido {
                        t5 += 2;
                        t6 += 2;
                        at!(ch, t5 - 1) = at!(cc, t5 - 1) - at!(cc, t6);
                        at!(ch, t6 - 1) = at!(cc, t5 - 1) + at!(cc, t6);
                        at!(ch, t5) = at!(cc, t5) + at!(cc, t6 - 1);
                        at!(ch, t6) = at!(cc, t5) - at!(cc, t6 - 1);
                        i += 2;
                    }
                    t3 += ido;
                    t4 += ido;
                }
            }
        } else {
            // L128
            t1 = 0;
            t2 = ipp2 * t0;
            for _ in 1..ipph {
                t1 += t0;
                t2 -= t0;
                t3 = t1;
                t4 = t2;
                let mut i = 2;
                while i < ido {
                    t3 += 2;
                    t4 += 2;
                    t5 = t3;
                    t6 = t4;
                    for _ in 0..l1 {
                        at!(ch, t5 - 1) = at!(cc, t5 - 1) - at!(cc, t6);
                        at!(ch, t6 - 1) = at!(cc, t5 - 1) + at!(cc, t6);
                        at!(ch, t5) = at!(cc, t5) + at!(cc, t6 - 1);
                        at!(ch, t6) = at!(cc, t5) - at!(cc, t6 - 1);
                        t5 += ido;
                        t6 += ido;
                    }
                    i += 2;
                }
            }
        }
    }
    // L132
    if ido == 1 {
        return;
    }
    for ik in 0..idl1 {
        at!(cc, ik) = at!(ch, ik);
    }
    t1 = 0;
    for _ in 1..ip {
        t1 += t0;
        t2 = t1;
        for _ in 0..l1 {
            at!(cc, t2) = at!(ch, t2);
            t2 += ido;
        }
    }
    let mut is;
    if nbd <= l1 {
        is = -ido - 1;
        t1 = 0;
        for _ in 1..ip {
            is += ido;
            t1 += t0;
            let mut idij = is;
            t2 = t1;
            let mut i = 2;
            while i < ido {
                t2 += 2;
                idij += 2;
                t3 = t2;
                for _ in 0..l1 {
                    at!(cc, t3 - 1) = at!(wa, idij - 1) * at!(ch, t3 - 1) - at!(wa, idij) * at!(ch, t3);
                    at!(cc, t3) = at!(wa, idij - 1) * at!(ch, t3) + at!(wa, idij) * at!(ch, t3 - 1);
                    t3 += ido;
                }
                i += 2;
            }
        }
        return;
    }
    // L139
    is = -ido - 1;
    t1 = 0;
    for _ in 1..ip {
        is += ido;
        t1 += t0;
        t2 = t1;
        for _ in 0..l1 {
            let mut idij = is;
            t3 = t2;
            let mut i = 2;
            while i < ido {
                idij += 2;
                t3 += 2;
                at!(cc, t3 - 1) = at!(wa, idij - 1) * at!(ch, t3 - 1) - at!(wa, idij) * at!(ch, t3);
                at!(cc, t3) = at!(wa, idij - 1) * at!(ch, t3) + at!(wa, idij) * at!(ch, t3 - 1);
                i += 2;
            }
            t2 += ido;
        }
    }
}

fn drftb1(n: isize, c: &mut [f32], ch: &mut [f32], wa: &[f32], ifac: &[i32; 32]) {
    let nf = ifac[1] as isize;
    let mut na: isize = 0;
    let mut l1: isize = 1;
    let mut iw: isize = 1;
    for k1 in 0..nf {
        let ip = ifac[(k1 + 2) as usize] as isize;
        let l2 = ip * l1;
        let ido = n / l2;
        let idl1 = ido * l1;
        let w = |offset: isize| &wa[(offset - 1) as usize..];
        if ip == 4 {
            let ix2 = iw + ido;
            let ix3 = ix2 + ido;
            if na != 0 {
                dradb4(ido, l1, ch, c, w(iw), w(ix2), w(ix3));
            } else {
                dradb4(ido, l1, c, ch, w(iw), w(ix2), w(ix3));
            }
            na = 1 - na;
        } else if ip == 2 {
            if na != 0 {
                dradb2(ido, l1, ch, c, w(iw));
            } else {
                dradb2(ido, l1, c, ch, w(iw));
            }
            na = 1 - na;
        } else if ip == 3 {
            let ix2 = iw + ido;
            if na != 0 {
                dradb3(ido, l1, ch, c, w(iw), w(ix2));
            } else {
                dradb3(ido, l1, c, ch, w(iw), w(ix2));
            }
            na = 1 - na;
        } else {
            // Radix five and above use the general pass (as upstream).
            if na != 0 {
                dradbg(ido, ip, l1, idl1, ch, c, w(iw));
            } else {
                dradbg(ido, ip, l1, idl1, c, ch, w(iw));
            }
            if ido == 1 {
                na = 1 - na;
            }
        }
        l1 = l2;
        iw += (ip - 1) * ido;
    }
    if na == 0 {
        return;
    }
    c[..n as usize].copy_from_slice(&ch[..n as usize]);
}

impl DrftLookup {
    pub fn new(n: usize) -> Self {
        let mut trigcache = vec![0.0f32; 3 * n];
        let mut splitcache = [0i32; 32];
        if n != 1 {
            drfti1(n as isize, &mut trigcache[n..], &mut splitcache);
        }
        Self { n, trigcache, splitcache }
    }

    pub fn len(&self) -> usize {
        self.n
    }

    pub fn is_empty(&self) -> bool {
        self.n == 0
    }

    /// In-place forward real transform (FFTPACK half-complex output order).
    pub fn forward(&mut self, data: &mut [f32]) {
        if self.n == 1 {
            return;
        }
        let n = self.n;
        let (scratch, rest) = self.trigcache.split_at_mut(n);
        drftf1(n as isize, data, scratch, rest, &self.splitcache);
    }

    /// In-place backward real transform (unscaled).
    pub fn backward(&mut self, data: &mut [f32]) {
        if self.n == 1 {
            return;
        }
        let n = self.n;
        let (scratch, rest) = self.trigcache.split_at_mut(n);
        drftb1(n as isize, data, scratch, rest, &self.splitcache);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn naive_forward(input: &[f32]) -> Vec<f64> {
        // FFTPACK order: r0, r1, i1, r2, i2, ..., r(n/2) for even n.
        let n = input.len();
        let mut out = vec![0.0; n];
        for k in 0..=n / 2 {
            let (mut re, mut im) = (0.0f64, 0.0f64);
            for (t, &x) in input.iter().enumerate() {
                let angle = -2.0 * std::f64::consts::PI * (k * t) as f64 / n as f64;
                re += f64::from(x) * angle.cos();
                im += f64::from(x) * angle.sin();
            }
            if k == 0 {
                out[0] = re;
            } else if 2 * k == n {
                out[n - 1] = re;
            } else {
                out[2 * k - 1] = re;
                out[2 * k] = im;
            }
        }
        out
    }

    #[test]
    fn forward_matches_dft_for_speex_sizes() {
        for n in [960usize, 640, 512, 30, 12] {
            let mut lookup = DrftLookup::new(n);
            let input: Vec<f32> = (0..n).map(|i| ((i * 7919) % 101) as f32 / 50.0 - 1.0).collect();
            let mut data = input.clone();
            lookup.forward(&mut data);
            let expected = naive_forward(&input);
            for (a, b) in data.iter().zip(&expected) {
                assert!((f64::from(*a) - b).abs() < 2e-3 * (n as f64).sqrt(), "n={n}: {a} vs {b}");
            }
        }
    }

    #[test]
    fn backward_inverts_forward_up_to_n() {
        for n in [960usize, 640, 30] {
            let mut lookup = DrftLookup::new(n);
            let input: Vec<f32> = (0..n).map(|i| ((i * 31) % 17) as f32 - 8.0).collect();
            let mut data = input.clone();
            lookup.forward(&mut data);
            lookup.backward(&mut data);
            for (a, b) in data.iter().zip(&input) {
                assert!((a / n as f32 - b).abs() < 1e-3, "n={n}");
            }
        }
    }
}
