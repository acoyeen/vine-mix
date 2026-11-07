//! Ephemeral signing for tests only (feature `testing`): a throwaway
//! RSA-3072 key, RSA-PSS (SHA-256, 32-byte salt) signatures and signed
//! envelopes, so update fixtures never use the release key.

use windows::core::{w, PCWSTR};
use windows::Win32::Security::Cryptography::{
    BCryptCloseAlgorithmProvider, BCryptDestroyKey, BCryptExportKey, BCryptFinalizeKeyPair, BCryptGenerateKeyPair, BCryptOpenAlgorithmProvider, BCryptSignHash,
    BCRYPT_ALG_HANDLE, BCRYPT_FLAGS, BCRYPT_KEY_HANDLE, BCRYPT_OPEN_ALGORITHM_PROVIDER_FLAGS, BCRYPT_PAD_PSS, BCRYPT_PSS_PADDING_INFO, BCRYPT_RSAKEY_BLOB,
    BCRYPT_RSA_ALGORITHM, BCRYPT_SHA256_ALGORITHM,
};

use crate::crypto;

/// A throwaway signing key.
pub struct TestKey {
    algorithm: BCRYPT_ALG_HANDLE,
    key: BCRYPT_KEY_HANDLE,
    pub modulus: Vec<u8>,
    pub exponent: Vec<u8>,
}

impl std::fmt::Debug for TestKey {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("TestKey")
    }
}

impl TestKey {
    pub fn generate() -> Result<Self, String> {
        let mut algorithm = BCRYPT_ALG_HANDLE::default();
        // SAFETY: valid out pointer; closed in Drop.
        unsafe { BCryptOpenAlgorithmProvider(&mut algorithm, BCRYPT_RSA_ALGORITHM, PCWSTR::null(), BCRYPT_OPEN_ALGORITHM_PROVIDER_FLAGS(0)) }.ok().map_err(|e| e.message())?;
        let mut key = BCRYPT_KEY_HANDLE::default();
        // SAFETY: valid algorithm; key destroyed in Drop.
        unsafe { BCryptGenerateKeyPair(algorithm, &mut key, 3072, 0) }.ok().map_err(|e| e.message())?;
        // SAFETY: valid key.
        unsafe { BCryptFinalizeKeyPair(key, 0) }.ok().map_err(|e| e.message())?;
        let mut size = 0u32;
        // SAFETY: size query.
        unsafe { BCryptExportKey(key, None, w!("RSAPUBLICBLOB"), None, &mut size, 0) }.ok().map_err(|e| e.message())?;
        let mut blob = vec![0u8; size as usize];
        // SAFETY: buffer sized as reported.
        unsafe { BCryptExportKey(key, None, w!("RSAPUBLICBLOB"), Some(&mut blob), &mut size, 0) }.ok().map_err(|e| e.message())?;
        // SAFETY: the blob starts with a BCRYPT_RSAKEY_BLOB header.
        let header = unsafe { std::ptr::read_unaligned(blob.as_ptr().cast::<BCRYPT_RSAKEY_BLOB>()) };
        let start = std::mem::size_of::<BCRYPT_RSAKEY_BLOB>();
        let exponent = blob[start..start + header.cbPublicExp as usize].to_vec();
        let modulus = blob[start + header.cbPublicExp as usize..start + header.cbPublicExp as usize + header.cbModulus as usize].to_vec();
        Ok(Self { algorithm, key, modulus, exponent })
    }

    pub fn sign(&self, data: &[u8]) -> Result<Vec<u8>, String> {
        let digest = crypto::sha256(data)?;
        let padding = BCRYPT_PSS_PADDING_INFO { pszAlgId: BCRYPT_SHA256_ALGORITHM, cbSalt: 32 };
        let mut size = 0u32;
        // SAFETY: size query with valid key and digest.
        unsafe { BCryptSignHash(self.key, Some(std::ptr::from_ref(&padding).cast()), &digest, None, &mut size, BCRYPT_FLAGS(BCRYPT_PAD_PSS.0)) }.ok().map_err(|e| e.message())?;
        let mut signature = vec![0u8; size as usize];
        // SAFETY: buffer sized as reported.
        unsafe { BCryptSignHash(self.key, Some(std::ptr::from_ref(&padding).cast()), &digest, Some(&mut signature), &mut size, BCRYPT_FLAGS(BCRYPT_PAD_PSS.0)) }
            .ok()
            .map_err(|e| e.message())?;
        signature.truncate(size as usize);
        Ok(signature)
    }

    /// A trust store holding only this key under `key_id`.
    pub fn trust(&self, key_id: &str) -> crate::Trust {
        crate::Trust::from_key(key_id, self.modulus.clone(), self.exponent.clone())
    }

    /// Signs `payload` into an update envelope.
    pub fn envelope(&self, key_id: &str, payload: &[u8]) -> Result<Vec<u8>, String> {
        let signature = self.sign(payload)?;
        let envelope = serde_json::json!({
            "formatVersion": 1,
            "keyId": key_id,
            "algorithm": "RSA-PSS-SHA256",
            "payload": crypto::base64_encode(payload),
            "signature": crypto::base64_encode(&signature),
        });
        serde_json::to_vec(&envelope).map_err(|e| e.to_string())
    }
}

impl Drop for TestKey {
    fn drop(&mut self) {
        // SAFETY: created in `generate`.
        unsafe {
            let _ = BCryptDestroyKey(self.key);
            let _ = BCryptCloseAlgorithmProvider(self.algorithm, 0);
        }
    }
}

/// A manifest payload for `version` whose two assets have the given sizes
/// and digests (the shape `parse_manifest` accepts).
pub fn manifest_payload(version: &str, numeric_version: &str, installer: (u64, &str), standalone: (u64, &str)) -> Vec<u8> {
    let asset = |kind: &str, (size, sha256): (u64, &str)| {
        let name = format!("VineMix-{version}-{kind}-x64.exe");
        serde_json::json!({ "fileName": name, "url": crate::asset_url(version, &name), "size": size, "sha256": sha256 })
    };
    serde_json::to_vec(&serde_json::json!({
        "formatVersion": 1,
        "product": "VineMix",
        "version": version,
        "numericVersion": numeric_version,
        "architecture": "x64",
        "minimumWindowsBuild": 22000,
        "settingsSchema": 2,
        "protocolVersion": 1,
        "notes": "Test release.",
        "installer": asset("Setup", installer),
        "standalone": asset("Standalone", standalone),
    }))
    .unwrap_or_default()
}
