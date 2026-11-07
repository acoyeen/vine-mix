//! SHA-256 and RSA-PSS (SHA-256) verification through Windows CNG.

use windows::core::PCWSTR;
use windows::Win32::Security::Cryptography::{
    BCryptCloseAlgorithmProvider, BCryptCreateHash, BCryptDestroyHash, BCryptDestroyKey, BCryptFinishHash, BCryptHashData,
    BCryptImportKeyPair, BCryptOpenAlgorithmProvider, BCryptVerifySignature, BCRYPT_ALG_HANDLE, BCRYPT_FLAGS, BCRYPT_HASH_HANDLE,
    BCRYPT_KEY_HANDLE, BCRYPT_OPEN_ALGORITHM_PROVIDER_FLAGS, BCRYPT_PAD_PSS, BCRYPT_PSS_PADDING_INFO, BCRYPT_RSAKEY_BLOB,
    BCRYPT_RSAPUBLIC_MAGIC, BCRYPT_RSA_ALGORITHM, BCRYPT_SHA256_ALGORITHM,
};

struct Algorithm(BCRYPT_ALG_HANDLE);

impl Algorithm {
    fn open(name: PCWSTR) -> Result<Self, String> {
        let mut handle = BCRYPT_ALG_HANDLE::default();
        // SAFETY: valid out pointer; closed by Drop.
        unsafe { BCryptOpenAlgorithmProvider(&mut handle, name, PCWSTR::null(), BCRYPT_OPEN_ALGORITHM_PROVIDER_FLAGS(0)) }
            .ok()
            .map_err(|e| e.message())?;
        Ok(Self(handle))
    }
}

impl Drop for Algorithm {
    fn drop(&mut self) {
        // SAFETY: opened by `open`.
        unsafe {
            let _ = BCryptCloseAlgorithmProvider(self.0, 0);
        }
    }
}

/// Incremental SHA-256.
pub struct Sha256 {
    _algorithm: Algorithm,
    hash: BCRYPT_HASH_HANDLE,
}

impl std::fmt::Debug for Sha256 {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("Sha256")
    }
}

impl Sha256 {
    pub fn new() -> Result<Self, String> {
        let algorithm = Algorithm::open(BCRYPT_SHA256_ALGORITHM)?;
        let mut hash = BCRYPT_HASH_HANDLE::default();
        // SAFETY: valid algorithm; CNG allocates the object.
        unsafe { BCryptCreateHash(algorithm.0, &mut hash, None, None, 0) }.ok().map_err(|e| e.message())?;
        Ok(Self { _algorithm: algorithm, hash })
    }

    pub fn update(&mut self, data: &[u8]) -> Result<(), String> {
        // SAFETY: valid hash handle and input.
        unsafe { BCryptHashData(self.hash, data, 0) }.ok().map_err(|e| e.message())
    }

    pub fn finish(self) -> Result<[u8; 32], String> {
        let mut digest = [0u8; 32];
        // SAFETY: valid handle; 32-byte output for SHA-256.
        unsafe { BCryptFinishHash(self.hash, &mut digest, 0) }.ok().map_err(|e| e.message())?;
        Ok(digest)
    }
}

impl Drop for Sha256 {
    fn drop(&mut self) {
        // SAFETY: created by `new`.
        unsafe {
            let _ = BCryptDestroyHash(self.hash);
        }
    }
}

pub fn sha256(data: &[u8]) -> Result<[u8; 32], String> {
    let mut hash = Sha256::new()?;
    hash.update(data)?;
    hash.finish()
}

pub fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}

pub fn from_hex(text: &str) -> Option<Vec<u8>> {
    if text.len() % 2 != 0 {
        return None;
    }
    (0..text.len()).step_by(2).map(|i| u8::from_str_radix(&text[i..i + 2], 16).ok()).collect()
}

/// Standard padded base64 (as System.Text.Json writes byte arrays).
pub fn base64_encode(data: &[u8]) -> String {
    const ALPHABET: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = String::with_capacity(data.len().div_ceil(3) * 4);
    for chunk in data.chunks(3) {
        let b = [chunk[0], chunk.get(1).copied().unwrap_or(0), chunk.get(2).copied().unwrap_or(0)];
        let n = (u32::from(b[0]) << 16) | (u32::from(b[1]) << 8) | u32::from(b[2]);
        for i in 0..4 {
            if i <= chunk.len() {
                out.push(char::from(ALPHABET[((n >> (18 - 6 * i)) & 63) as usize]));
            } else {
                out.push('=');
            }
        }
    }
    out
}

pub fn base64_decode(text: &str) -> Option<Vec<u8>> {
    let value = |c: u8| -> Option<u32> {
        Some(match c {
            b'A'..=b'Z' => u32::from(c - b'A'),
            b'a'..=b'z' => u32::from(c - b'a') + 26,
            b'0'..=b'9' => u32::from(c - b'0') + 52,
            b'+' => 62,
            b'/' => 63,
            _ => return None,
        })
    };
    let bytes: Vec<u8> = text.bytes().filter(|b| !b.is_ascii_whitespace()).collect();
    if bytes.len() % 4 != 0 {
        return None;
    }
    let mut out = Vec::with_capacity(bytes.len() / 4 * 3);
    for chunk in bytes.chunks(4) {
        let padding = chunk.iter().rev().take_while(|&&b| b == b'=').count();
        if padding > 2 || chunk[..4 - padding].contains(&b'=') {
            return None;
        }
        let mut n = 0u32;
        for &c in &chunk[..4 - padding] {
            n = (n << 6) | value(c)?;
        }
        n <<= 6 * padding as u32;
        let decoded = [(n >> 16) as u8, (n >> 8) as u8, n as u8];
        out.extend_from_slice(&decoded[..3 - padding]);
    }
    Some(out)
}

/// Minimal DER reader for SubjectPublicKeyInfo / RSAPublicKey.
struct Der<'a> {
    data: &'a [u8],
}

impl<'a> Der<'a> {
    fn read(&mut self, tag: u8) -> Option<&'a [u8]> {
        let (&found, rest) = self.data.split_first()?;
        if found != tag {
            return None;
        }
        let (&first, mut rest) = rest.split_first()?;
        let length = if first & 0x80 == 0 {
            usize::from(first)
        } else {
            let count = usize::from(first & 0x7F);
            if count == 0 || count > 4 || rest.len() < count {
                return None;
            }
            let mut length = 0usize;
            for &b in &rest[..count] {
                length = (length << 8) | usize::from(b);
            }
            rest = &rest[count..];
            length
        };
        if rest.len() < length {
            return None;
        }
        let (value, remaining) = rest.split_at(length);
        self.data = remaining;
        Some(value)
    }
}

/// Modulus and exponent from a PEM `PUBLIC KEY` (SubjectPublicKeyInfo).
pub fn rsa_public_key(pem: &str) -> Result<(Vec<u8>, Vec<u8>), String> {
    let body: String = pem
        .lines()
        .map(str::trim)
        .filter(|l| !l.is_empty() && !l.starts_with("-----"))
        .collect();
    if !pem.contains("-----BEGIN PUBLIC KEY-----") {
        return Err("Update trust key is not a public key.".into());
    }
    let der = base64_decode(&body).ok_or("Update trust key is not valid Base64.")?;
    let invalid = || "Update trust key is malformed.".to_owned();
    let mut outer = Der { data: &der };
    let spki = outer.read(0x30).ok_or_else(invalid)?;
    let mut spki = Der { data: spki };
    let algorithm = spki.read(0x30).ok_or_else(invalid)?;
    // rsaEncryption OID 1.2.840.113549.1.1.1
    const RSA_OID: [u8; 9] = [0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01];
    let mut algorithm = Der { data: algorithm };
    if algorithm.read(0x06).ok_or_else(invalid)? != RSA_OID {
        return Err("Update trust key is not an RSA key.".into());
    }
    let bits = spki.read(0x03).ok_or_else(invalid)?;
    let (&unused, key) = bits.split_first().ok_or_else(invalid)?;
    if unused != 0 {
        return Err(invalid());
    }
    let mut key = Der { data: key };
    let sequence = key.read(0x30).ok_or_else(invalid)?;
    let mut sequence = Der { data: sequence };
    let strip = |v: &[u8]| -> Vec<u8> {
        let mut v = v;
        while v.len() > 1 && v[0] == 0 {
            v = &v[1..];
        }
        v.to_vec()
    };
    let modulus = strip(sequence.read(0x02).ok_or_else(invalid)?);
    let exponent = strip(sequence.read(0x02).ok_or_else(invalid)?);
    Ok((modulus, exponent))
}

/// Key size in bits.
pub fn modulus_bits(modulus: &[u8]) -> usize {
    modulus.first().map_or(0, |&b| (modulus.len() - 1) * 8 + (8 - b.leading_zeros() as usize))
}

/// Verifies an RSA-PSS signature (SHA-256, salt = hash length) over `data`.
pub fn verify_rsa_pss(modulus: &[u8], exponent: &[u8], data: &[u8], signature: &[u8]) -> Result<bool, String> {
    let algorithm = Algorithm::open(BCRYPT_RSA_ALGORITHM)?;
    // BCRYPT_RSAKEY_BLOB header, then exponent and modulus (big-endian).
    let header = BCRYPT_RSAKEY_BLOB {
        Magic: BCRYPT_RSAPUBLIC_MAGIC,
        BitLength: u32::try_from(modulus_bits(modulus)).map_err(|_| "key too large")?,
        cbPublicExp: u32::try_from(exponent.len()).map_err(|_| "key too large")?,
        cbModulus: u32::try_from(modulus.len()).map_err(|_| "key too large")?,
        cbPrime1: 0,
        cbPrime2: 0,
    };
    let mut blob = Vec::with_capacity(std::mem::size_of::<BCRYPT_RSAKEY_BLOB>() + exponent.len() + modulus.len());
    // SAFETY: plain-old-data header copied byte for byte.
    blob.extend_from_slice(unsafe {
        std::slice::from_raw_parts(std::ptr::from_ref(&header).cast::<u8>(), std::mem::size_of::<BCRYPT_RSAKEY_BLOB>())
    });
    blob.extend_from_slice(exponent);
    blob.extend_from_slice(modulus);
    let mut key = BCRYPT_KEY_HANDLE::default();
    let blob_type = windows::core::w!("RSAPUBLICBLOB");
    // SAFETY: valid algorithm and blob; the key is destroyed below.
    unsafe { BCryptImportKeyPair(algorithm.0, None, blob_type, &mut key, &blob, 0) }.ok().map_err(|e| e.message())?;
    let digest = sha256(data)?;
    let padding = BCRYPT_PSS_PADDING_INFO { pszAlgId: BCRYPT_SHA256_ALGORITHM, cbSalt: 32 };
    // SAFETY: valid key, digest and padding info.
    let status = unsafe { BCryptVerifySignature(key, Some(std::ptr::from_ref(&padding).cast()), &digest, signature, BCRYPT_FLAGS(BCRYPT_PAD_PSS.0)) };
    // SAFETY: imported above.
    unsafe {
        let _ = BCryptDestroyKey(key);
    }
    Ok(status.is_ok())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn hashing_and_encoding() {
        assert_eq!(hex(&sha256(b"abc").unwrap()), "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        assert_eq!(base64_decode("aGVsbG8=").unwrap(), b"hello");
        assert!(base64_decode("aGVsbG8").is_none());
        let trust = include_str!("../../../updates/release-trust.json");
        let value: serde_json::Value = serde_json::from_str(trust).unwrap();
        let (modulus, exponent) = rsa_public_key(value["keys"][0]["publicKeyPem"].as_str().unwrap()).unwrap();
        assert_eq!(modulus_bits(&modulus), 3072);
        assert_eq!(exponent, [1, 0, 1]);
        assert!(!verify_rsa_pss(&modulus, &exponent, b"data", &[0u8; 384]).unwrap());
    }
}
