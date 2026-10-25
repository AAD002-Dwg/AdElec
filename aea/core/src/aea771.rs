//! Reglas de AEA 90364-7-771 (Edición 2006) para oficinas, locales y usos terciarios.

use crate::catalogo::{Catalogo770, Catalogo771, Referencia};
use crate::diagnostico::{Correccion, Diagnostico, Severidad};
use crate::modelo::{Circuito, Unidad};
use crate::aea770::{iz_corregida, CALIBRES_ITM_A};
use serde::Serialize;

const EPS: f64 = 1e-9;

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct ResultadoGrado771 {
    pub grado: String,
    pub superficie_limite_m2: f64,
    pub max_demanda_kva: Option<f64>,
    pub referencia: Referencia,
}

/// Grado de electrificación para oficinas y locales comerciales (Tabla 771.8.IV).
pub fn grado_electrificacion_comercial(cat: &Catalogo771, superficie_m2: f64) -> ResultadoGrado771 {
    let t = &cat.grados_oficinas_locales;
    let rango = t
        .datos
        .iter()
        .find(|r| {
            let sobre_min = r.desde_m2.map_or(true, |m| superficie_m2 > m + EPS);
            let bajo_max = r.hasta_m2.map_or(true, |m| superficie_m2 <= m + EPS);
            sobre_min && bajo_max
        })
        .unwrap_or_else(|| &t.datos[t.datos.len() - 1]);

    ResultadoGrado771 {
        grado: rango.grado.clone(),
        superficie_limite_m2: superficie_m2,
        max_demanda_kva: rango.max_kva.or(rango.min_kva),
        referencia: t.referencia(),
    }
}

/// Valida un circuito bajo las reglas de AEA 771 (Tabla 771.7.I).
pub fn validar_circuito_771(cat: &Catalogo771, c: &Circuito) -> Vec<Diagnostico> {
    let mut d = Vec::new();
    let ref_tabla = cat.tipos_circuito.referencia();

    let Some(tipo) = cat.tipo(&c.tipo) else {
        d.push(Diagnostico {
            codigo: "771.7/tipo_desconocido",
            severidad: Severidad::Advertencia,
            elemento: c.id.clone(),
            mensaje: format!("El tipo '{}' no figura en la Tabla 771.7.I de AEA 771.", c.tipo),
            referencia: ref_tabla,
            correcciones: vec![],
        });
        return d;
    };

    // Máximo de bocas
    if c.bocas > tipo.max_bocas {
        d.push(Diagnostico {
            codigo: "771.7/max_bocas",
            severidad: Severidad::Error,
            elemento: c.id.clone(),
            mensaje: format!(
                "Circuito {} ({}) tiene {} bocas; el máximo normativo es {}.",
                c.id, c.tipo, c.bocas, tipo.max_bocas
            ),
            referencia: ref_tabla.clone(),
            correcciones: vec![Correccion::AgregarCircuitos {
                tipo: c.tipo.clone(),
                cantidad: ((c.bocas as f64) / (tipo.max_bocas as f64)).ceil() as u32 - 1,
            }],
        });
    }

    // Máxima corriente asignada de protección
    if let Some(max_in) = tipo.max_in_a {
        if c.in_a > max_in + EPS {
            let correcciones = CALIBRES_ITM_A
                .iter()
                .filter(|&&cal| cal <= max_in + EPS)
                .map(|&cal| Correccion::CambiarProteccion { in_a: cal })
                .collect();

            d.push(Diagnostico {
                codigo: "771.7/proteccion_max",
                severidad: Severidad::Error,
                elemento: c.id.clone(),
                mensaje: format!(
                    "Circuito {} ({}) tiene protección de {:.0} A; el calibre máximo permitido por la Tabla 771.7.I es {:.0} A.",
                    c.id, c.tipo, c.in_a, max_in
                ),
                referencia: ref_tabla,
                correcciones,
            });
        }
    }

    d
}

/// Valida una unidad terciaria/comercial completa bajo AEA 771.
pub fn validar_unidad_771(cat771: &Catalogo771, cat770: &Catalogo770, u: &Unidad) -> Vec<Diagnostico> {
    let mut d = Vec::new();

    // 1. Validar circuitos individuales contra Tabla 771.7.I
    for c in &u.circuitos {
        d.extend(validar_circuito_771(cat771, c));

        // 2. Coordinación de conductores Ib <= In <= Iz
        let ref_coord = cat770.coordinacion.referencia();
        match iz_corregida(cat770, c.seccion_mm2, c.cables_cargados, c.circuitos_en_canalizacion) {
            Ok(iz) => {
                if c.in_a > iz.iz_a + EPS {
                    let mut corrs = Vec::new();
                    if let Some(&nuevo_in) = CALIBRES_ITM_A.iter().rev().find(|&&cal| cal <= iz.iz_a + EPS) {
                        corrs.push(Correccion::CambiarProteccion { in_a: nuevo_in });
                    }
                    if let Some(s_sug) = cat770.iz.datos.iter().find(|fila| {
                        let base = if c.cables_cargados == 3 { fila.iz_3x_a } else { fila.iz_2x_a };
                        base * iz.factor_agrupamiento >= c.in_a - EPS
                    }) {
                        corrs.push(Correccion::CambiarSeccion { seccion_mm2: s_sug.seccion_mm2 });
                    }

                    d.push(Diagnostico {
                        codigo: "771.19/in_mayor_iz",
                        severidad: Severidad::Error,
                        elemento: c.id.clone(),
                        mensaje: format!(
                            "Circuito {}: In ({:.0} A) > Iz admisible ({:.1} A) para conductor de {:.1} mm² (agrupamiento factor {:.2}).",
                            c.id, c.in_a, iz.iz_a, c.seccion_mm2, iz.factor_agrupamiento
                        ),
                        referencia: ref_coord.clone(),
                        correcciones: corrs,
                    });
                }
            }
            Err(_) => {
                d.push(Diagnostico {
                    codigo: "771.19/seccion_desconocida",
                    severidad: Severidad::Advertencia,
                    elemento: c.id.clone(),
                    mensaje: format!("Circuito {}: sección de {:.1} mm² fuera de las tablas de intensidad admisible.", c.id, c.seccion_mm2),
                    referencia: ref_coord,
                    correcciones: vec![],
                });
            }
        }
    }

    d
}
