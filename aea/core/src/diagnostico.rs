//! Diagnósticos y correcciones propuestas.
//!
//! El núcleo nunca modifica el proyecto: devuelve diagnósticos y, cuando puede,
//! correcciones concretas. Aplicarlas (con deshacer, transacciones, etc.) es
//! responsabilidad del adaptador CAD o del agente.

use crate::catalogo::Referencia;
use serde::Serialize;

#[derive(Debug, Clone, Copy, Serialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum Severidad {
    /// Incumplimiento normativo.
    Error,
    /// No se pudo verificar o requiere criterio del proyectista.
    Advertencia,
    Info,
}

#[derive(Debug, Clone, Serialize, PartialEq)]
#[serde(tag = "accion", rename_all = "snake_case")]
pub enum Correccion {
    CambiarProteccion { in_a: f64 },
    CambiarSeccion { seccion_mm2: f64 },
    CambiarNorma { norma: String },
    AgregarCircuitos { tipo: String, cantidad: u32 },
}

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct Diagnostico {
    /// Código estable de la regla, p. ej. "770.15.3/coordinacion".
    pub codigo: &'static str,
    pub severidad: Severidad,
    /// Elemento afectado (id de circuito o unidad).
    pub elemento: String,
    pub mensaje: String,
    pub referencia: Referencia,
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub correcciones: Vec<Correccion>,
}
