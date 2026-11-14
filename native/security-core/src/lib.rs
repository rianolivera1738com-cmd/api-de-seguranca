use argon2::{
    password_hash::{PasswordHash, SaltString},
    Argon2, PasswordHasher, PasswordVerifier,
};
use rand_core::OsRng;
use std::{
    ffi::{c_char, CStr, CString},
    panic::{catch_unwind, AssertUnwindSafe},
};

fn hash(password: &str) -> Option<String> {
    if !(12..=128).contains(&password.len()) {
        return None;
    }
    Argon2::default()
        .hash_password(password.as_bytes(), &SaltString::generate(&mut OsRng))
        .ok()
        .map(|value| value.to_string())
}

fn verify(password: &str, encoded: &str) -> bool {
    if password.len() > 128 {
        return false;
    }
    PasswordHash::new(encoded).ok().is_some_and(|value| {
        Argon2::default()
            .verify_password(password.as_bytes(), &value)
            .is_ok()
    })
}

/// # Safety
/// Input must be a valid NUL-terminated UTF-8 string, or null.
/// The returned allocation must be released exactly once with security_free.
#[no_mangle]
pub unsafe extern "C" fn security_hash(password: *const c_char) -> *mut c_char {
    catch_unwind(AssertUnwindSafe(|| {
        if password.is_null() {
            return std::ptr::null_mut();
        }
        CStr::from_ptr(password)
            .to_str()
            .ok()
            .and_then(hash)
            .and_then(|value| CString::new(value).ok())
            .map_or(std::ptr::null_mut(), CString::into_raw)
    }))
    .unwrap_or(std::ptr::null_mut())
}

/// # Safety
/// Both inputs must be valid NUL-terminated UTF-8 strings, or null.
#[no_mangle]
pub unsafe extern "C" fn security_verify(password: *const c_char, encoded: *const c_char) -> i32 {
    catch_unwind(AssertUnwindSafe(|| {
        if password.is_null() || encoded.is_null() {
            return 0;
        }
        match (
            CStr::from_ptr(password).to_str(),
            CStr::from_ptr(encoded).to_str(),
        ) {
            (Ok(password), Ok(encoded)) => i32::from(verify(password, encoded)),
            _ => 0,
        }
    }))
    .unwrap_or(0)
}

/// # Safety
/// Pointer must be null or an allocation returned by security_hash, freed once.
#[no_mangle]
pub unsafe extern "C" fn security_free(value: *mut c_char) {
    if !value.is_null() {
        drop(CString::from_raw(value));
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn passwords_are_salted_and_verified() {
        let first = hash("senha-segura-123").unwrap();
        assert!(first.starts_with("$argon2id$"));
        assert_ne!(first, hash("senha-segura-123").unwrap());
        assert!(verify("senha-segura-123", &first));
        assert!(!verify("senha-incorreta", &first));
        assert!(!verify("senha-segura-123", "invalid"));
        assert!(hash("curta").is_none());
    }
    #[test]
    fn ffi_roundtrip_and_null_inputs() {
        unsafe {
            assert!(security_hash(std::ptr::null()).is_null());
            let password = CString::new("senha-segura-123").unwrap();
            let encoded = security_hash(password.as_ptr());
            assert!(!encoded.is_null());
            assert_eq!(security_verify(password.as_ptr(), encoded), 1);
            assert_eq!(security_verify(std::ptr::null(), encoded), 0);
            security_free(encoded);
            security_free(std::ptr::null_mut());
        }
    }
}
