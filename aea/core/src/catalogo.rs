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
    pub guia770: CatalogoGuia770,
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
            guia770: CatalogoGuia770::embebido(),
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
    pub max_bocas: Option<u32>,
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

// ------------------------------------------------------- GUÍA AEA 770 ---- //

#[derive(Debug, Clone, Deserialize)]
pub struct FilaCalibreDomiciliarioGuia770 {
    pub circuitos_por_cano: u32,
    pub calibres_max_a: Vec<f64>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CalibresSubterraneoGuia770 {
    pub sobre_bandejas_al_aire_a: Vec<f64>,
    pub dentro_de_canerias_a: Vec<f64>,
    pub directamente_enterrado_a: Vec<f64>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CalibreMaxProteccionesGuia770 {
    pub secciones_mm2: Vec<f64>,
    pub domiciliario_iram_nm_247_3: Vec<FilaCalibreDomiciliarioGuia770>,
    pub subterraneo_iram_2178_1: CalibresSubterraneoGuia770,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CriteriosInterruptoresGuia770 {
    pub altura_min_m: f64,
    pub altura_max_m: f64,
    pub altura_recomendada_m: f64,
    pub distancia_max_marco_m: f64,
    pub pasillo_longitud_min_combinacion_m: f64,
    pub distancia_max_entre_combinados_m: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CriteriosTomacorrientesGuia770 {
    pub altura_estandar_min_m: f64,
    pub altura_estandar_max_m: f64,
    pub altura_garajes_min_m: f64,
    pub altura_mesada_min_m: f64,
    pub exclusion_mesada_zonas: Vec<u32>,
    pub zona_1_mesada_lateral_m: f64,
    pub zona_1_mesada_vertical_m: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CriteriosBanosGuia770 {
    pub zona_1_altura_m: f64,
    pub zona_2_ancho_m: f64,
    pub ip_minimo_luminaria_zona_2: String,
    pub zona_permitida_tug: u32,
    pub hidromasaje_id_exclusivo_max_ma: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CriteriosTablerosPatGuia770 {
    pub distancia_max_medidor_tp_m: f64,
    pub espacio_libre_frontal_min_m: f64,
    pub ip_minimo_tp_intemperie: String,
    pub seccion_min_pat_mm2: f64,
    pub separacion_min_jabalinas_factor_l: f64,
    pub largo_bucle_caja_mm: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CriteriosIntemperieGuia770 {
    pub ip_minimo_semicubiertos: String,
    pub ip_minimo_chorro_agua: String,
    pub ip_minimo_empalmes_subterraneos: String,
    pub profundidad_min_enterrado_m: f64,
    pub diametro_min_conducto_enterrado_mm: f64,
    pub distancia_max_camaras_inspeccion_m: f64,
    pub profundidad_cinta_advertencia_m: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CriteriosInstalacionGuia770 {
    pub interruptores: CriteriosInterruptoresGuia770,
    pub tomacorrientes: CriteriosTomacorrientesGuia770,
    pub banos: CriteriosBanosGuia770,
    pub tableros_y_pat: CriteriosTablerosPatGuia770,
    pub intemperie_y_subterraneo: CriteriosIntemperieGuia770,
}

#[derive(Debug, Clone, Deserialize)]
pub struct AmbienteProyectoGuia770 {
    pub nombre: String,
    pub tipo: String,
    pub superficie_m2: f64,
    pub iug: u32,
    pub tug: u32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CircuitoProyectoGuia770 {
    pub numero: u32,
    pub tipo: String,
    pub bocas: u32,
    pub dpms_va: f64,
    pub tension_v: f64,
    pub ib_a: f64,
    pub seccion_fase_mm2: f64,
    pub seccion_pe_mm2: f64,
    pub iz_a: f64,
    pub in_a: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct SeccionalProyectoGuia770 {
    pub dpms_total_va: f64,
    pub tension_v: f64,
    pub ib_a: f64,
    pub seccion_fase_mm2: f64,
    pub seccion_pe_mm2: f64,
    pub seccion_pat_mm2: f64,
    pub iz_a: f64,
    pub itm_in_a: f64,
    pub id_in_a: f64,
    pub id_sensibilidad_ma: f64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ModeloProyectoGuia770 {
    pub grado_electrificacion: String,
    pub superficie_cubierta_m2: f64,
    pub superficie_semicubierta_m2: f64,
    pub sla_m2: f64,
    pub ambientes: Vec<AmbienteProyectoGuia770>,
    pub circuitos: Vec<CircuitoProyectoGuia770>,
    pub circuito_seccional: SeccionalProyectoGuia770,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CircuitoGeneralGuia770 {
    pub sigla: String,
    pub designacion: String,
    pub categoria: String,
    pub alimentacion: String,
    pub ambito_uso: String,
    pub cargas: String,
    pub conexion: String,
    pub max_bocas: u32,
    pub potencia_estimada_va: f64,
    pub factor_simultaneidad_circuito: f64,
    pub max_in_a: f64,
    pub seccion_min_mm2: f64,
    pub max_corriente_boca_a: f64,
    pub permitido_en_viviendas: bool,
}

#[derive(Debug, Clone, Deserialize)]
pub struct CircuitoEspecificoGuia770 {
    pub sigla: String,
    pub designacion: String,
    pub alimentacion: String,
    pub cargas: String,
    pub conexion: String,
    pub max_bocas: Option<u32>,
    pub max_in_a: Option<f64>,
    pub max_corriente_boca_a: Option<f64>,
    pub usos_tipicos: String,
    pub permitido_en_viviendas: bool,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ClasificacionCircuitosGuia770 {
    pub generales_y_especiales: Vec<CircuitoGeneralGuia770>,
    pub usos_especificos: Vec<CircuitoEspecificoGuia770>,
}

#[derive(Debug, Clone)]
pub struct CatalogoGuia770 {
    pub calibre_maximo: Tabla<CalibreMaxProteccionesGuia770>,
    pub clasificacion_circuitos: Tabla<ClasificacionCircuitosGuia770>,
    pub criterios_instalacion: Tabla<CriteriosInstalacionGuia770>,
    pub proyecto_referencia: Tabla<ModeloProyectoGuia770>,
}

macro_rules! cargar_guia770 {
    ($ruta:literal) => {
        serde_json::from_str(include_str!(concat!("../../catalogo/guia-aea-770-2018/", $ruta)))
            .unwrap_or_else(|e| panic!("catálogo Guía 770 {} inválido: {}", $ruta, e))
    };
}

impl CatalogoGuia770 {
    pub fn embebido() -> Self {
        Self {
            calibre_maximo: cargar_guia770!("calibre_maximo_protecciones.json"),
            clasificacion_circuitos: cargar_guia770!("clasificacion_circuitos.json"),
            criterios_instalacion: cargar_guia770!("criterios_instalacion.json"),
            proyecto_referencia: cargar_guia770!("modelo_proyecto_referencia.json"),
        }
    }
}

