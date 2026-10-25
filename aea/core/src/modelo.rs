//! Modelo de entrada neutro: lo que cualquier CAD (AutoCAD, Revit, OCS…) debe
//! entregar al núcleo. Cada adaptador traduce sus entidades a estas estructuras.

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Circuito {
    pub id: String,
    /// Sigla del tipo de circuito: IUG, TUG, TUE…
    pub tipo: String,
    pub seccion_mm2: f64,
    /// Corriente nominal de la protección (In).
    pub in_a: f64,
    /// Corriente de proyecto (Ib). Si se omite no se verifica Ib <= In.
    #[serde(default)]
    pub ib_a: Option<f64>,
    #[serde(default)]
    pub bocas: u32,
    /// Conductores cargados: 2 (monofásico) o 3 (trifásico).
    #[serde(default = "dos")]
    pub cables_cargados: u8,
    /// Cantidad de circuitos que comparten la canalización (incluido este).
    #[serde(default = "uno")]
    pub circuitos_en_canalizacion: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Unidad {
    pub id: String,
    pub superficie_cubierta_m2: f64,
    #[serde(default)]
    pub superficie_semicubierta_m2: f64,
    /// In del dispositivo de cabecera del tablero principal.
    #[serde(default)]
    pub in_cabecera_a: Option<f64>,
    /// Icc presunta en el origen de la instalación [kA].
    #[serde(default)]
    pub icc_origen_ka: Option<f64>,
    #[serde(default)]
    pub circuitos: Vec<Circuito>,
}

fn dos() -> u8 {
    2
}
fn uno() -> u32 {
    1
}
