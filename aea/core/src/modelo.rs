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
    /// Longitud en metros del circuito (desde tablero hasta la carga más alejada).
    #[serde(default)]
    pub longitud_m: Option<f64>,
    /// Factor de potencia (cos phi). Por defecto 0.9.
    #[serde(default)]
    pub cos_phi: Option<f64>,
    /// Poder de corte asignado de la protección (Icn) en [kA]. Por defecto 3.0 kA.
    #[serde(default)]
    pub icn_ka: Option<f64>,
}

impl Default for Circuito {
    fn default() -> Self {
        Self {
            id: String::new(),
            tipo: "TUG".into(),
            seccion_mm2: 2.5,
            in_a: 16.0,
            ib_a: None,
            bocas: 0,
            cables_cargados: 2,
            circuitos_en_canalizacion: 1,
            longitud_m: None,
            cos_phi: None,
            icn_ka: None,
        }
    }
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
    /// Norma o uso: "AEA-770" (residencial), "AEA-771" (comercial/oficinas), "AEA-772" (partes comunes de edificios).
    #[serde(default)]
    pub norma: Option<String>,
    #[serde(default)]
    pub circuitos: Vec<Circuito>,
    #[serde(default)]
    pub ambientes: Vec<Ambiente>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Ambiente {
    pub id: String,
    pub nombre: String,
    /// Tipo normalizado: "sala_estar", "dormitorio", "cocina", "bano", "pasillo", "lavadero", "garaje", "balcon", etc.
    pub tipo: String,
    pub superficie_m2: f64,
    #[serde(default)]
    pub longitud_m: Option<f64>,
    #[serde(default)]
    pub bocas_iug: u32,
    #[serde(default)]
    pub bocas_tug: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct PuntosMinimosAmbiente {
    pub ambiente_id: String,
    pub iug_minimo: u32,
    pub tug_minimo: u32,
    pub modulos_fijos: u32,
}

fn dos() -> u8 {
    2
}
fn uno() -> u32 {
    1
}
