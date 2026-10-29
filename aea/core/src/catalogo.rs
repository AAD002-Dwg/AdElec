//! Carga del catálogo normativo.
//!
//! Los JSON de `aea/catalogo/` se embeben en el binario en tiempo de compilación,
//! así el núcleo funciona igual en escritorio, WASM o dentro de un plugin CAD,
//! sin depender de archivos externos. El verificador Python garantiza que esos
//! mismos JSON coinciden con el PDF de la norma.

use serde::{Deserialize, Serialize};

/// Ubicación de un dato dentro de la norma. Viaja con cada diagnóstico para que
/// el proyectista pueda comprobarlo en segundos.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Fuente {
    pub doc: String,
    pub pagina_pdf: u32,
    pub clausula: String,
}

#[derive(Debug, Clone, Deserialize)]
pub struct Tabla<T> {
    pub id: String,
    pub titulo: String,
    pub fuente: Fuente,
    pub estado: String,
    pub datos: T,
}

impl<T> Tabla<T> {
    pub fn referencia(&self) -> Referencia {
        Referencia {
            tabla: self.id.clone(),
            clausula: self.fuente.clausula.clone(),
            doc: self.fuente.doc.clone(),
            pagina_pdf: self.fuente.pagina_pdf,
        }
    }
}

/// Referencia normativa adjunta a un resultado o diagnóstico.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Referencia {
    pub doc: String,
    pub tabla: String,
    pub clausula: String,
    pub pagina_pdf: u32,
}

// ---------------------------------------------------------------- 770 ----- //

#[derive(Debug, Clone, Deserialize)]
pub struct Alcance {
    pub max_in_cabecera_a: f64,
    pub max_icc_origen_ka: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct TipoCircuito {
    pub sigla: String,
    pub max_bocas: u32,
    pub max_in_a: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct RangoGrado {
    pub grado: String,
    pub sup_mayor_que_m2: Option<f64>,
    pub sup_hasta_m2: Option<f64>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct TablaGrados {
    pub id: String,
    pub titulo: String,
    pub fuente: Fuente,
    pub estado: String,
    pub parametros: ParametrosGrado,
    pub datos: Vec<RangoGrado>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ParametrosGrado {
    pub factor_semicubierta: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct VarianteCircuitos {
    pub id: String,
    pub iug: u32,
    pub tug: u32,
    pub libre: Option<u32>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct MinimoCircuitos {
    pub grado: String,
    pub cantidad_minima: u32,
    pub variantes: Vec<VarianteCircuitos>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct SeccionMinima {
    pub uso: String,
    pub seccion_mm2: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct FilaIz {
    pub seccion_mm2: f64,
    pub iz_2x_a: f64,
    pub iz_3x_a: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct FactorAgrupamiento {
    pub circuitos: u32,
    pub tipo: String,
    pub hasta_cables_cargados: u32,
    pub factor: f64,
    pub columna: u32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct Coordinacion {
    pub condicion: String,
    pub factor_i2: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ReglaEstarComedor {
    pub iug_cada_m2: f64,
    pub iug_minimo: u32,
    pub tug_cada_m2: f64,
    pub tug_minimo: u32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ReglaFija {
    pub iug: u32,
    pub tug: u32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CocinaDetalle {
    pub iug: u32,
    pub tug: u32,
    pub modulos_fijos: u32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ReglaCocinaGrados {
    pub minimo: CocinaDetalle,
    pub medio: CocinaDetalle,
    pub elevado: CocinaDetalle,
    pub superior: CocinaDetalle,
}

#[derive(Debug, Clone, Deserialize)]
pub struct VestibuloMedioSuperior {
    pub iug_cada_m2: f64,
    pub iug_minimo: u32,
    pub tug_cada_m2: f64,
    pub tug_minimo: u32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ReglaVestibulo {
    pub minimo: ReglaFija,
    pub medio_o_superior: VestibuloMedioSuperior,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ReglaPasillo {
    pub iug_cada_m: f64,
    pub iug_minimo: u32,
    pub tug_longitud_min_m: f64,
    pub tug_cada_m: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ReglaLavadero {
    pub minimo: ReglaFija,
    pub medio_o_superior: ReglaFija,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ReglaBalcon {
    pub iug_cada_m: f64,
    pub tug: u32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct PuntosMinimosDatos {
    pub estar_comedor: ReglaEstarComedor,
    pub dormitorio_menor_10: ReglaFija,
    pub dormitorio_10_a_36: ReglaFija,
    pub dormitorio_mayor_36: ReglaFija,
    pub cocina: ReglaCocinaGrados,
    pub bano: ReglaFija,
    pub vestibulo_garaje: ReglaVestibulo,
    pub pasillo_cubierto: ReglaPasillo,
    pub lavadero: ReglaLavadero,
    pub balcon_galeria: ReglaBalcon,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CoefSimultaneidad770 {
    pub circuitos_minimos: u32,
    pub coeficiente: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct DemandaPotencia770 {
    pub iug_va_por_boca: f64,
    pub iug_factor: f64,
    pub iug_con_tomas_va: f64,
    pub tug_va: f64,
    pub tue_va: f64,
    pub coeficientes_simultaneidad: Vec<CoefSimultaneidad770>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CableUnipolarDimension {
    pub seccion_mm2: f64,
    pub diametro_ext_mm: f64,
    pub seccion_total_mm2: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CanoDimension {
    pub designacion: String,
    pub tipo: String,
    pub seccion_interna_mm2: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct Canalizaciones770 {
    pub factor_ocupacion_max: f64,
    pub diametro_interno_min_terminal_mm: f64,
    pub diametro_interno_min_seccional_mm: f64,
    pub cables_unipolares: Vec<CableUnipolarDimension>,
    pub canos: Vec<CanoDimension>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CajaEmbutir {
    pub tipo: String,
    pub volumen_total_cm3: f64,
    pub volumen_utilizable_cm3: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct VolumenCable {
    pub seccion_mm2: f64,
    pub volumen_cm3: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CajasVolumen770 {
    pub cajas: Vec<CajaEmbutir>,
    pub volumen_cable_cm3: Vec<VolumenCable>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ConductorProteccion770 {
    pub resistencia_pat_max_ohm: f64,
    pub seccion_min_pe_mm2: f64,
    pub seccion_min_pat_mm2: f64,
    pub umbral_igual_fase_hasta_mm2: f64,
    pub umbral_fijo_16_hasta_mm2: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ReglaDps770 {
    pub alimentacion: String,
    pub aq1_obligatorio: bool,
    pub aq2_obligatorio: bool,
}

#[derive(Debug, Clone, Deserialize)]
pub struct Dps770 {
    pub umbral_aq2_dias_ano: u32,
    pub reglas: Vec<ReglaDps770>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CaidaUnitariaFila {
    pub seccion_mm2: f64,
    pub v_a_km: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CaidaTension770 {
    pub max_pct_iluminacion_y_tomas: f64,
    pub max_pct_motores_regimen: f64,
    pub max_pct_motores_arranque: f64,
    pub max_pct_seccional: f64,
    pub caida_unitaria_monofasica: Vec<CaidaUnitariaFila>,
}

/// Catálogo completo de AEA 90364-7-770 (2017).
#[derive(Debug, Clone)]
pub struct Catalogo770 {
    pub alcance: Tabla<Alcance>,
    pub tipos_circuito: Tabla<Vec<TipoCircuito>>,
    pub grados: TablaGrados,
    pub circuitos_minimos: Tabla<Vec<MinimoCircuitos>>,
    pub puntos_minimos: Tabla<PuntosMinimosDatos>,
    pub secciones_minimas: Tabla<Vec<SeccionMinima>>,
    pub iz: Tabla<Vec<FilaIz>>,
    pub agrupamiento: Tabla<Vec<FactorAgrupamiento>>,
    pub coordinacion: Tabla<Coordinacion>,
    pub demanda_potencia: Tabla<DemandaPotencia770>,
    pub canalizaciones: Tabla<Canalizaciones770>,
    pub cajas: Tabla<CajasVolumen770>,
    pub conductor_pe: Tabla<ConductorProteccion770>,
    pub dps: Tabla<Dps770>,
    pub caida_tension: Tabla<CaidaTension770>,
}

macro_rules! cargar {
    ($ruta:literal) => {
        serde_json::from_str(include_str!(concat!("../../catalogo/aea-770-2017/", $ruta)))
            .unwrap_or_else(|e| panic!("catálogo {} inválido: {}", $ruta, e))
    };
}

impl Catalogo770 {
    /// Catálogo embebido en el binario.
    pub fn embebido() -> Self {
        Self {
            alcance: cargar!("alcance.json"),
            tipos_circuito: cargar!("tipos_circuito.json"),
            grados: cargar!("grados_electrificacion.json"),
            circuitos_minimos: cargar!("circuitos_minimos.json"),
            puntos_minimos: cargar!("puntos_minimos.json"),
            secciones_minimas: cargar!("secciones_minimas.json"),
            iz: cargar!("iz_cables_canalizacion.json"),
            agrupamiento: cargar!("factor_agrupamiento.json"),
            coordinacion: cargar!("coordinacion_protecciones.json"),
            demanda_potencia: cargar!("demanda_potencia.json"),
            canalizaciones: cargar!("canalizaciones_ocupacion.json"),
            cajas: cargar!("cajas_volumen.json"),
            conductor_pe: cargar!("conductor_proteccion_pe.json"),
            dps: cargar!("dps_sobretensiones.json"),
            caida_tension: cargar!("caida_tension.json"),
        }
    }

    pub fn tipo(&self, sigla: &str) -> Option<&TipoCircuito> {
        self.tipos_circuito.datos.iter().find(|t| t.sigla.eq_ignore_ascii_case(sigla))
    }

    pub fn seccion_minima(&self, uso: &str) -> Option<f64> {
        self.secciones_minimas
            .datos
            .iter()
            .find(|s| s.uso.eq_ignore_ascii_case(uso))
            .map(|s| s.seccion_mm2)
    }
}

impl TablaGrados {
    pub fn referencia(&self) -> Referencia {
        Referencia {
            tabla: self.id.clone(),
            clausula: self.fuente.clausula.clone(),
            doc: self.fuente.doc.clone(),
            pagina_pdf: self.fuente.pagina_pdf,
        }
    }
}

// ---------------------------------------------------------------- 771 ----- //

#[derive(Debug, Clone, Deserialize)]
pub struct TipoCircuito771 {
    pub sigla: String,
    pub max_bocas: u32,
    pub max_in_a: Option<f64>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct RangoGrado771 {
    pub grado: String,
    pub desde_m2: Option<f64>,
    pub hasta_m2: Option<f64>,
    pub max_kva: Option<f64>,
    pub min_kva: Option<f64>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct VarianteCircuitos771 {
    pub id: String,
    pub iug: u32,
    pub tug: u32,
    #[serde(default)]
    pub iue: u32,
    #[serde(default)]
    pub tue: u32,
    #[serde(default)]
    pub libre: u32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct MinimoCircuitos771 {
    pub grado: String,
    pub cantidad_minima: u32,
    pub variantes: Vec<VarianteCircuitos771>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ReglaSalonComercial771 {
    pub iug_cada_m2: f64,
    pub iug_minimo: u32,
    pub tug_cada_m2: f64,
    pub tug_minimo: u32,
    #[serde(default)]
    pub tue_cada_m_perimetro_elevado_sup: Option<f64>,
    #[serde(default)]
    pub tue_minimo_elevado_sup: Option<u32>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CocinaComercialDetalle771 {
    pub iug: u32,
    pub tug: u32,
    pub tue: u32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ReglaCocinaComercial771 {
    pub minimo_medio: CocinaComercialDetalle771,
    pub elevado_superior: CocinaComercialDetalle771,
}

#[derive(Debug, Clone, Deserialize)]
pub struct BanoElevadoComercial771 {
    pub iug_cada_m2: f64,
    pub iug_minimo: u32,
    pub tug: u32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ReglaBanoComercial771 {
    pub minimo_medio: ReglaFija,
    pub elevado_superior: BanoElevadoComercial771,
}

#[derive(Debug, Clone, Deserialize)]
pub struct PuntosMinimosComercial771 {
    pub salon_general: ReglaSalonComercial771,
    pub sala_reuniones: ReglaSalonComercial771,
    pub despacho_privado: ReglaFija,
    pub cocina: ReglaCocinaComercial771,
    pub bano: ReglaBanoComercial771,
    pub vestibulo_recepcion: ReglaSalonComercial771,
    pub pasillo: ReglaPasillo,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CoefGrado771 {
    pub grado: String,
    pub coeficiente: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct DemandaPotencia771 {
    pub iug_va_por_boca: f64,
    pub iug_factor_viviendas: f64,
    pub iug_factor_oficinas_locales: f64,
    pub iug_con_tomas_va: f64,
    pub tug_va: f64,
    pub iue_va_por_boca: f64,
    pub iue_factor_viviendas: f64,
    pub iue_factor_oficinas_locales: f64,
    pub tue_va: f64,
    pub coeficientes_por_grado: Vec<CoefGrado771>,
}

#[derive(Debug, Clone)]
pub struct Catalogo771 {
    pub tipos_circuito: Tabla<Vec<TipoCircuito771>>,
    pub grados_oficinas_locales: Tabla<Vec<RangoGrado771>>,
    pub circuitos_minimos_comercial: Tabla<Vec<MinimoCircuitos771>>,
    pub puntos_minimos_comercial: Tabla<PuntosMinimosComercial771>,
    pub demanda_potencia: Tabla<DemandaPotencia771>,
}

macro_rules! cargar771 {
    ($ruta:literal) => {
        serde_json::from_str(include_str!(concat!("../../catalogo/aea-771-2006/", $ruta)))
            .unwrap_or_else(|e| panic!("catálogo 771 {} inválido: {}", $ruta, e))
    };
}

impl Catalogo771 {
    pub fn embebido() -> Self {
        Self {
            tipos_circuito: cargar771!("tipos_circuito.json"),
            grados_oficinas_locales: cargar771!("grados_oficinas_locales.json"),
            circuitos_minimos_comercial: cargar771!("circuitos_minimos_comercial.json"),
            puntos_minimos_comercial: cargar771!("puntos_minimos_comercial.json"),
            demanda_potencia: cargar771!("demanda_potencia_771.json"),
        }
    }

    pub fn tipo(&self, sigla: &str) -> Option<&TipoCircuito771> {
        self.tipos_circuito.datos.iter().find(|t| t.sigla.eq_ignore_ascii_case(sigla))
    }
}

// ---------------------------------------------------------------- 772 ----- //

#[derive(Debug, Clone, Deserialize)]
pub struct TipoCircuito772 {
    pub sigla: String,
    pub max_bocas: u32,
    pub max_in_a: Option<f64>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct RangoCircuitosComunes772 {
    pub sup_desde_m2: Option<f64>,
    pub sup_hasta_m2: Option<f64>,
    pub cantidad_minima: u32,
    pub variantes: Vec<serde_json::Value>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct DatosEspaciosComunes772 {
    pub rangos: Vec<RangoCircuitosComunes772>,
    pub servicios_obligatorios_independientes: Vec<String>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct SimultaneidadCircuitos772 {
    pub fuerza_motriz: Vec<f64>,
    pub iluminacion_viviendas: Vec<f64>,
    pub iluminacion_no_viviendas: Vec<f64>,
    pub tomacorrientes: Vec<f64>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct SimultaneidadUnidades772 {
    pub desde_unidades: u32,
    pub hasta_unidades: Option<u32>,
    pub oficinas_locales: f64,
    pub viviendas: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct DemandaServicios772 {
    pub iug_va_por_boca: f64,
    pub iug_con_tomas_va: f64,
    pub tug_va: f64,
    pub tue_va: f64,
    pub factor_min_tablero_servicios_generales: f64,
    pub simultaneidad_circuitos: SimultaneidadCircuitos772,
    pub simultaneidad_unidades_funcionales: Vec<SimultaneidadUnidades772>,
}

#[derive(Debug, Clone)]
pub struct Catalogo772 {
    pub tipos_circuito: Tabla<Vec<TipoCircuito772>>,
    pub circuitos_minimos: Tabla<DatosEspaciosComunes772>,
    pub demanda_servicios: Tabla<DemandaServicios772>,
}

macro_rules! cargar772 {
    ($ruta:literal) => {
        serde_json::from_str(include_str!(concat!("../../catalogo/aea-772-2019/", $ruta)))
            .unwrap_or_else(|e| panic!("catálogo 772 {} inválido: {}", $ruta, e))
    };
}

impl Catalogo772 {
    pub fn embebido() -> Self {
        Self {
            tipos_circuito: cargar772!("tipos_circuito.json"),
            circuitos_minimos: cargar772!("circuitos_minimos_espacios_comunes.json"),
            demanda_servicios: cargar772!("demanda_servicios_generales.json"),
        }
    }

    pub fn tipo(&self, sigla: &str) -> Option<&TipoCircuito772> {
        self.tipos_circuito.datos.iter().find(|t| t.sigla.eq_ignore_ascii_case(sigla))
    }
}
