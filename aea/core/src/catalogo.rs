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
#[derive(Debug, Clone, Serialize, PartialEq)]
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

/// Catálogo completo de AEA 90364-7-770 (2017).
#[derive(Debug, Clone)]
pub struct Catalogo770 {
    pub alcance: Tabla<Alcance>,
    pub tipos_circuito: Tabla<Vec<TipoCircuito>>,
    pub grados: TablaGrados,
    pub circuitos_minimos: Tabla<Vec<MinimoCircuitos>>,
    pub secciones_minimas: Tabla<Vec<SeccionMinima>>,
    pub iz: Tabla<Vec<FilaIz>>,
    pub agrupamiento: Tabla<Vec<FactorAgrupamiento>>,
    pub coordinacion: Tabla<Coordinacion>,
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
            secciones_minimas: cargar!("secciones_minimas.json"),
            iz: cargar!("iz_cables_canalizacion.json"),
            agrupamiento: cargar!("factor_agrupamiento.json"),
            coordinacion: cargar!("coordinacion_protecciones.json"),
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
