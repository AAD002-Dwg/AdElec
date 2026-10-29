//! Modelo de entrada neutro: lo que cualquier CAD (AutoCAD, Revit, OCS…) debe
//! entregar al núcleo. Cada adaptador traduce sus entidades a estas estructuras.

use serde::{Deserialize, Serialize};
use crate::catalogo::Referencia;

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
    /// Sección del conductor de protección PE [mm²]. Si se indica, se valida contra Tabla 770.14.I.
    #[serde(default)]
    pub seccion_pe_mm2: Option<f64>,
    /// Designación de la cañería (ej. "RS 16", "RP 20", "CSP 19"). Si se indica, se valida factor de ocupación <= 35%.
    #[serde(default)]
    pub cano_designacion: Option<String>,
    /// Potencia declarada del circuito en [VA] (para cálculo de DPMS).
    #[serde(default)]
    pub potencia_va: Option<f64>,
    /// Indica si un circuito IUG incluye tomacorrientes derivados (exige 2.5 mm² y 2200 VA).
    #[serde(default)]
    pub incluye_tomacorrientes: bool,
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
            seccion_pe_mm2: None,
            cano_designacion: None,
            potencia_va: None,
            incluye_tomacorrientes: false,
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
    /// Cantidad de interruptores diferenciales (ID) en el tablero.
    #[serde(default)]
    pub cantidad_id: Option<u32>,
    /// Resistencia de puesta a tierra de protección medida/proyectada [Ohm] (máx 40 Ohm según 770.14.4.1).
    #[serde(default)]
    pub resistencia_pat_ohm: Option<f64>,
    /// Indica si cuenta con descargadores de sobretensión (DPS).
    #[serde(default)]
    pub tiene_dps: Option<bool>,
    /// Tipo de alimentación externa: "red_aerea", "con_pararrayos", "red_subterranea".
    #[serde(default)]
    pub alimentacion_red: Option<String>,
    /// Nivel ceráunico AQ (días de tormenta al año, umbral AQ2 > 25).
    #[serde(default)]
    pub aq_dias_tormenta: Option<u32>,
    #[serde(default)]
    pub circuitos: Vec<Circuito>,
    #[serde(default)]
    pub ambientes: Vec<Ambiente>,
}

impl Default for Unidad {
    fn default() -> Self {
        Self {
            id: "U1".into(),
            superficie_cubierta_m2: 0.0,
            superficie_semicubierta_m2: 0.0,
            in_cabecera_a: None,
            icc_origen_ka: None,
            norma: None,
            cantidad_id: None,
            resistencia_pat_ohm: None,
            tiene_dps: None,
            alimentacion_red: None,
            aq_dias_tormenta: None,
            circuitos: Vec::new(),
            ambientes: Vec::new(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Ambiente {
    pub id: String,
    pub nombre: String,
    /// Tipo normalizado: "sala_estar", "dormitorio", "cocina", "bano", "pasillo", "lavadero", "garaje", "balcon", "salon_general", "oficina", etc.
    pub tipo: String,
    pub superficie_m2: f64,
    #[serde(default)]
    pub longitud_m: Option<f64>,
    #[serde(default)]
    pub bocas_iug: u32,
    #[serde(default)]
    pub bocas_tug: u32,
    #[serde(default)]
    pub bocas_tue: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct PuntosMinimosAmbiente {
    pub ambiente_id: String,
    pub iug_minimo: u32,
    pub tug_minimo: u32,
    pub modulos_fijos: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct ResultadoDpms {
    pub potencia_sin_simultaneidad_va: f64,
    pub coeficiente_simultaneidad: f64,
    pub dpms_va: f64,
    pub dpms_kva: f64,
    pub corriente_proyecto_ib_a: f64,
    pub proteccion_cabecera_sugerida_a: f64,
    pub referencia: Referencia,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct ResultadoCaneria {
    pub designacion: String,
    pub tipo: String,
    pub seccion_interna_mm2: f64,
    pub area_cables_mm2: f64,
    pub ocupacion_pct: f64,
    pub cumple: bool,
    pub referencia: Referencia,
}

fn dos() -> u8 {
    2
}
fn uno() -> u32 {
    1
}
