//! C-ABI / FFI exports for .NET (AutoCAD / Revit / C#) and native hosts.
//! All functions take null-terminated UTF-8 C strings and return newly allocated
//! null-terminated UTF-8 C strings that must be freed using `aea_free_string`.

use std::ffi::{CStr, CString};
use std::os::raw::{c_char, c_double};
use std::sync::OnceLock;
use serde::{Deserialize, Serialize};

use crate::catalogo::{Catalogo770, Catalogo771, Catalogo772};
use crate::modelo::{Ambiente, Unidad};
use crate::{aea770, aea771, aea772};

static CAT_770: OnceLock<Catalogo770> = OnceLock::new();
static CAT_771: OnceLock<Catalogo771> = OnceLock::new();
static CAT_772: OnceLock<Catalogo772> = OnceLock::new();

fn catalogo_770() -> &'static Catalogo770 {
    CAT_770.get_or_init(Catalogo770::embebido)
}

fn catalogo_771() -> &'static Catalogo771 {
    CAT_771.get_or_init(Catalogo771::embebido)
}

fn catalogo_772() -> &'static Catalogo772 {
    CAT_772.get_or_init(Catalogo772::embebido)
}

fn to_c_string<T: Serialize>(val: &T) -> *mut c_char {
    let json = match serde_json::to_string(val) {
        Ok(s) => s,
        Err(e) => format!("{{\"error\":\"serialization failed: {}\"}}", e),
    };
    CString::new(json).unwrap_or_default().into_raw()
}

fn error_json(msg: &str) -> *mut c_char {
    let s = format!("{{\"error\":\"{}\"}}", msg.replace('\"', "\\\""));
    CString::new(s).unwrap_or_default().into_raw()
}

/// Frees a string allocated by `aea-core` FFI.
#[no_mangle]
pub unsafe extern "C" fn aea_free_string(ptr: *mut c_char) {
    if !ptr.is_null() {
        drop(CString::from_raw(ptr));
    }
}

/// Validates an electrical unit (circuits + rooms) against AEA 770, 771 or 772 according to `norma`.
/// Takes JSON-serialized `Unidad` and returns JSON-serialized `Vec<Diagnostico>`.
#[no_mangle]
pub unsafe extern "C" fn aea_validar_unidad(json_input: *const c_char) -> *mut c_char {
    if json_input.is_null() {
        return error_json("null input pointer");
    }
    let c_str = match CStr::from_ptr(json_input).to_str() {
        Ok(s) => s,
        Err(_) => return error_json("invalid utf-8 input"),
    };
    let unidad: Unidad = match serde_json::from_str(c_str) {
        Ok(u) => u,
        Err(e) => return error_json(&format!("JSON deserialization failed: {}", e)),
    };

    let norma = unidad.norma.as_deref().unwrap_or("").trim().to_uppercase();
    let diags = if norma.contains("771") || norma == "COMERCIAL" || norma == "OFICINA" {
        aea771::validar_unidad_771(catalogo_771(), catalogo_770(), &unidad)
    } else if norma.contains("772") || norma == "COMUNES" || norma == "SERVICIOS_GENERALES" {
        aea772::validar_unidad_772(catalogo_772(), catalogo_770(), &unidad)
    } else {
        aea770::validar_unidad(catalogo_770(), &unidad)
    };

    to_c_string(&diags)
}

#[derive(Deserialize)]
struct ReqPuntosMinimos {
    pub ambiente: Ambiente,
    pub grado: String,
}

/// Calculates minimum IUG/TUG points according to Tabla 770.7.III.
/// Input JSON: `{"ambiente": {...}, "grado": "medio"}`.
#[no_mangle]
pub unsafe extern "C" fn aea_puntos_minimos_ambiente(json_input: *const c_char) -> *mut c_char {
    if json_input.is_null() {
        return error_json("null input pointer");
    }
    let c_str = match CStr::from_ptr(json_input).to_str() {
        Ok(s) => s,
        Err(_) => return error_json("invalid utf-8 input"),
    };
    let req: ReqPuntosMinimos = match serde_json::from_str(c_str) {
        Ok(r) => r,
        Err(e) => return error_json(&format!("JSON deserialization failed: {}", e)),
    };

    let res = aea770::calcular_puntos_minimos_ambiente(catalogo_770(), &req.ambiente, &req.grado);
    to_c_string(&res)
}

/// Calculates electrification degree for residential units (Tabla 770.7.I).
#[no_mangle]
pub extern "C" fn aea_grado_electrificacion(cubierta_m2: c_double, semicubierta_m2: c_double) -> *mut c_char {
    let res = aea770::grado_electrificacion(catalogo_770(), cubierta_m2, semicubierta_m2);
    to_c_string(&res)
}

/// Calculates electrification degree for commercial offices and premises (Tabla 771.8.IV).
#[no_mangle]
pub extern "C" fn aea_grado_electrificacion_comercial(superficie_m2: c_double) -> *mut c_char {
    let res = aea771::grado_electrificacion_comercial(catalogo_771(), superficie_m2);
    to_c_string(&res)
}

#[cfg(test)]
mod ffi_tests {
    use super::*;

    #[test]
    fn test_ffi_grado_y_liberacion() {
        unsafe {
            let ptr = aea_grado_electrificacion(80.0, 10.0);
            assert!(!ptr.is_null());
            let s = CStr::from_ptr(ptr).to_str().unwrap();
            assert!(s.contains("\"grado\":\"medio\""));
            aea_free_string(ptr);
        }
    }

    #[test]
    fn test_ffi_validar_unidad_json() {
        let json = r#"{
            "id": "U1",
            "superficie_cubierta_m2": 50.0,
            "superficie_semicubierta_m2": 0.0,
            "in_cabecera_a": 25.0,
            "icc_origen_ka": 3.0,
            "circuitos": [
                {
                    "id": "C1",
                    "tipo": "IUG",
                    "seccion_mm2": 1.5,
                    "in_a": 10.0,
                    "bocas": 8
                },
                {
                    "id": "C2",
                    "tipo": "TUG",
                    "seccion_mm2": 2.5,
                    "in_a": 16.0,
                    "bocas": 12
                }
            ],
            "ambientes": []
        }"#;
        let c_in = CString::new(json).unwrap();
        unsafe {
            let ptr = aea_validar_unidad(c_in.as_ptr());
            assert!(!ptr.is_null());
            let s = CStr::from_ptr(ptr).to_str().unwrap();
            // Circuito IUG 10A 1.5mm2 y TUG 16A 2.5mm2 son 100% reglamentarios en 770 -> 0 diagnósticos de error
            assert_eq!(s, "[]");
            aea_free_string(ptr);
        }
    }
}
