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
    #[serde(default)]
    pub norma: Option<String>,
}

/// Calculates minimum IUG/TUG points according to Tabla 770.7.III (residential) or Tabla 771.8.VI (commercial).
/// Input JSON: `{"ambiente": {...}, "grado": "medio", "norma": "AEA-770"}`.
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

    let norma = req.norma.as_deref().unwrap_or("").trim().to_uppercase();
    let tipo_low = req.ambiente.tipo.to_lowercase();
    let es_comercial = norma.contains("771")
        || matches!(
            tipo_low.as_str(),
            "salon_general" | "local" | "comercio" | "oficina" | "sala_reuniones" | "despacho_privado" | "recepcion"
        );

    let res = if es_comercial {
        aea771::calcular_puntos_minimos_comercial(catalogo_771(), &req.ambiente, &req.grado)
    } else {
        aea770::calcular_puntos_minimos_ambiente(catalogo_770(), &req.ambiente, &req.grado)
    };
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

/// Calculates Simultaneous Maximum Power Demand (DPMS) according to AEA 770.8, 771.9 or 772.9.
/// Input JSON: `Unidad`. Returns JSON: `ResultadoDpms`.
#[no_mangle]
pub unsafe extern "C" fn aea_calcular_dpms(json_input: *const c_char) -> *mut c_char {
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
    let res = if norma.contains("771") || norma == "COMERCIAL" || norma == "OFICINA" {
        aea771::calcular_dpms_771(catalogo_771(), &unidad)
    } else if norma.contains("772") || norma == "COMUNES" || norma == "SERVICIOS_GENERALES" {
        aea772::calcular_dpms_772(catalogo_772(), &unidad)
    } else {
        aea770::calcular_dpms_770(catalogo_770(), &unidad)
    };
    to_c_string(&res)
}

/// Sizes or verifies conduit occupancy (≤ 35% fill according to Tabla 770.10.VIII).
/// `familia_o_designacion`: C-string (e.g., `"RS 19"`, `"RP 20"`, `"aislante_rigido"`).
#[no_mangle]
pub unsafe extern "C" fn aea_dimensionar_caneria(
    seccion_mm2: c_double,
    conductores_totales: u32,
    familia_o_designacion: *const c_char,
) -> *mut c_char {
    let clave = if familia_o_designacion.is_null() {
        "aislante_rigido"
    } else {
        CStr::from_ptr(familia_o_designacion).to_str().unwrap_or("aislante_rigido")
    };
    match aea770::dimensionar_caneria(catalogo_770(), seccion_mm2, conductores_totales, clave) {
        Some(res) => to_c_string(&res),
        None => error_json("conduit or cable section not found in catalog"),
    }
}

/// Returns the normative minimum protective conductor (PE) cross-section [mm²] according to Tabla 770.14.I.
#[no_mangle]
pub extern "C" fn aea_seccion_pe(seccion_fase_mm2: c_double) -> c_double {
    aea770::seccion_minima_pe(catalogo_770(), seccion_fase_mm2)
}

/// Calculates voltage drop percentage (ΔV%) using standard copper resistivity at 70°C.
#[no_mangle]
pub extern "C" fn aea_calcular_caida_tension(
    longitud_m: c_double,
    corriente_a: c_double,
    seccion_mm2: c_double,
    cables_cargados: u8,
    cos_phi: c_double,
) -> c_double {
    aea770::calcular_caida_tension(longitud_m, corriente_a, seccion_mm2, cables_cargados, cos_phi)
}

/// Verifies conductor thermal stress under short-circuit (I²·t ≤ k²·S²).
/// Returns JSON-serialized ResultadoCortocircuito.
#[no_mangle]
pub unsafe extern "C" fn aea_verificar_cortocircuito_conductor(
    icc_ka: c_double,
    tiempo_s: c_double,
    seccion_mm2: c_double,
    aislacion_pvc: bool,
) -> *mut c_char {
    let res = aea770::verificar_cortocircuito_conductor(icc_ka, tiempo_s, seccion_mm2, aislacion_pvc);
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
