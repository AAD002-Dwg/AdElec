//! Reglas de AEA 90364-7-770 (Edición 2017).
//!
//! Cada regla toma sus valores del catálogo (verificado contra el PDF) y adjunta
//! la referencia normativa a su resultado. Ningún valor normativo está escrito
//! en este archivo, salvo los calibres normalizados de interruptores, que
//! provienen de IEC 60898-1 y no de la AEA (ver `CALIBRES_ITM_A`).

use crate::catalogo::{Catalogo770, Referencia};
use crate::diagnostico::{Correccion, Diagnostico, Severidad};
use crate::modelo::{Circuito, Unidad};
use serde::Serialize;

/// Valores preferidos de corriente asignada para interruptores automáticos
/// (IEC 60898-1). No es una tabla AEA: se usa solo para proponer correcciones.
pub const CALIBRES_ITM_A: [f64; 10] = [6.0, 10.0, 13.0, 16.0, 20.0, 25.0, 32.0, 40.0, 50.0, 63.0];

const EPS: f64 = 1e-9;

// ------------------------------------------------------------- grado ------ //

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct ResultadoGrado {
    pub grado: String,
    /// Superficie cubierta + factor × semicubierta.
    pub superficie_limite_m2: f64,
    pub referencia: Referencia,
}

/// Grado de electrificación según 770.7.3 / Tabla 770.7.I.
pub fn grado_electrificacion(cat: &Catalogo770, cubierta_m2: f64, semicubierta_m2: f64) -> ResultadoGrado {
    let t = &cat.grados;
    let sup = cubierta_m2 + t.parametros.factor_semicubierta * semicubierta_m2;
    let rango = t
        .datos
        .iter()
        .find(|r| {
            let sobre_min = r.sup_mayor_que_m2.map_or(true, |m| sup > m + EPS);
            let bajo_max = r.sup_hasta_m2.map_or(true, |m| sup <= m + EPS);
            sobre_min && bajo_max
        })
        .expect("la Tabla 770.7.I cubre todo el rango de superficies");
    ResultadoGrado { grado: rango.grado.clone(), superficie_limite_m2: sup, referencia: t.referencia() }
}

// ------------------------------------------------------------- Iz --------- //

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct ResultadoIz {
    pub iz_tabla_a: f64,
    pub factor_agrupamiento: f64,
    pub iz_a: f64,
}

#[derive(Debug, Clone, PartialEq)]
pub enum ErrorIz {
    SeccionFueraDeTabla(f64),
    CablesCargadosInvalidos(u8),
    AgrupamientoFueraDeTabla(u32),
}

/// Corriente admisible corregida por agrupamiento (Tablas 770.12.I y 770.12.II).
pub fn iz_corregida(cat: &Catalogo770, seccion_mm2: f64, cables_cargados: u8, circuitos: u32) -> Result<ResultadoIz, ErrorIz> {
    let fila = cat
        .iz
        .datos
        .iter()
        .find(|f| (f.seccion_mm2 - seccion_mm2).abs() < EPS)
        .ok_or(ErrorIz::SeccionFueraDeTabla(seccion_mm2))?;
    let (iz_tabla, tipo) = match cables_cargados {
        2 => (fila.iz_2x_a, "monofasico"),
        3 => (fila.iz_3x_a, "trifasico"),
        n => return Err(ErrorIz::CablesCargadosInvalidos(n)),
    };
    let factor = if circuitos <= 1 {
        1.0
    } else {
        cat.agrupamiento
            .datos
            .iter()
            .find(|f| f.tipo == tipo && f.circuitos == circuitos)
            .map(|f| f.factor)
            .ok_or(ErrorIz::AgrupamientoFueraDeTabla(circuitos))?
    };
    Ok(ResultadoIz { iz_tabla_a: iz_tabla, factor_agrupamiento: factor, iz_a: iz_tabla * factor })
}

// ------------------------------------------------------------- circuito --- //

/// Valida un circuito contra 770.6, 770.11 y 770.15.3.
pub fn validar_circuito(cat: &Catalogo770, c: &Circuito) -> Vec<Diagnostico> {
    let mut d = Vec::new();

    let Some(tipo) = cat.tipo(&c.tipo) else {
        d.push(Diagnostico {
            codigo: "770.6/tipo",
            severidad: Severidad::Advertencia,
            elemento: c.id.clone(),
            mensaje: format!(
                "El tipo '{}' no figura en la Tabla 770.6.I (IUG, TUG, TUE). Los circuitos de uso específico se rigen por AEA 90364-7-771.",
                c.tipo
            ),
            referencia: cat.tipos_circuito.referencia(),
            correcciones: vec![],
        });
        return d;
    };

    // 770.6 — bocas máximas
    if c.bocas > tipo.max_bocas {
        d.push(Diagnostico {
            codigo: "770.6/bocas",
            severidad: Severidad::Error,
            elemento: c.id.clone(),
            mensaje: format!("{} bocas en circuito {}: el máximo es {}.", c.bocas, tipo.sigla, tipo.max_bocas),
            referencia: cat.tipos_circuito.referencia(),
            correcciones: vec![],
        });
    }

    // 770.6 — calibre máximo de protección
    if c.in_a > tipo.max_in_a + EPS {
        d.push(Diagnostico {
            codigo: "770.6/proteccion_max",
            severidad: Severidad::Error,
            elemento: c.id.clone(),
            mensaje: format!("Protección de {} A en circuito {}: el máximo es {} A.", c.in_a, tipo.sigla, tipo.max_in_a),
            referencia: cat.tipos_circuito.referencia(),
            correcciones: vec![Correccion::CambiarProteccion { in_a: tipo.max_in_a }],
        });
    }

    // 770.11 — sección mínima
    if let Some(min) = cat.seccion_minima(&tipo.sigla) {
        if c.seccion_mm2 + EPS < min {
            d.push(Diagnostico {
                codigo: "770.11/seccion_min",
                severidad: Severidad::Error,
                elemento: c.id.clone(),
                mensaje: format!("Sección {} mm² en circuito {}: la mínima es {} mm².", c.seccion_mm2, tipo.sigla, min),
                referencia: cat.secciones_minimas.referencia(),
                correcciones: vec![Correccion::CambiarSeccion { seccion_mm2: min }],
            });
        }
    }

    // 770.15.3 — coordinación Ib <= In <= Iz
    d.extend(validar_coordinacion(cat, c, tipo.max_in_a));
    d
}

fn validar_coordinacion(cat: &Catalogo770, c: &Circuito, max_in_tipo: f64) -> Vec<Diagnostico> {
    let referencia = cat.coordinacion.referencia();
    let iz = match iz_corregida(cat, c.seccion_mm2, c.cables_cargados, c.circuitos_en_canalizacion) {
        Ok(r) => r,
        Err(e) => {
            let mensaje = match e {
                ErrorIz::SeccionFueraDeTabla(s) => format!("Sección {s} mm² no figura en la Tabla 770.12.I; no se verificó la coordinación."),
                ErrorIz::CablesCargadosInvalidos(n) => format!("{n} conductores cargados no es válido (2 o 3); no se verificó la coordinación."),
                ErrorIz::AgrupamientoFueraDeTabla(n) => format!(
                    "{n} circuitos en la misma canalización exceden la Tabla 770.12.II; calcular el factor según AEA 90364-5-52. No se verificó la coordinación."
                ),
            };
            return vec![Diagnostico {
                codigo: "770.15.3/no_verificable",
                severidad: Severidad::Advertencia,
                elemento: c.id.clone(),
                mensaje,
                referencia,
                correcciones: vec![],
            }];
        }
    };

    let mut d = Vec::new();
    let ib = c.ib_a;

    if c.in_a > iz.iz_a + EPS {
        let mut correcciones = Vec::new();
        // a) bajar la protección al mayor calibre que respete Ib <= In <= Iz
        if let Some(in_ok) = CALIBRES_ITM_A
            .iter()
            .rev()
            .copied()
            .find(|&x| x <= iz.iz_a + EPS && x <= max_in_tipo + EPS && ib.map_or(true, |ib| ib <= x + EPS))
        {
            correcciones.push(Correccion::CambiarProteccion { in_a: in_ok });
        }
        // b) subir la sección a la menor que soporte la In actual
        if let Some(s) = menor_seccion_para(cat, c, c.in_a) {
            correcciones.push(Correccion::CambiarSeccion { seccion_mm2: s });
        }
        d.push(Diagnostico {
            codigo: "770.15.3/in_mayor_iz",
            severidad: Severidad::Error,
            elemento: c.id.clone(),
            mensaje: format!(
                "In = {} A supera Iz = {:.1} A ({} mm², Iz tabla {} A × factor agrupamiento {}). No se cumple In ≤ Iz.",
                c.in_a, iz.iz_a, c.seccion_mm2, iz.iz_tabla_a, iz.factor_agrupamiento
            ),
            referencia: referencia.clone(),
            correcciones,
        });
    }

    if let Some(ib) = ib {
        if ib > c.in_a + EPS {
            let mut correcciones = Vec::new();
            if let Some(in_ok) = CALIBRES_ITM_A.iter().copied().find(|&x| x + EPS >= ib && x <= max_in_tipo + EPS) {
                if in_ok <= iz.iz_a + EPS {
                    correcciones.push(Correccion::CambiarProteccion { in_a: in_ok });
                } else if let Some(s) = menor_seccion_para(cat, c, in_ok) {
                    correcciones.push(Correccion::CambiarProteccion { in_a: in_ok });
                    correcciones.push(Correccion::CambiarSeccion { seccion_mm2: s });
                }
            }
            d.push(Diagnostico {
                codigo: "770.15.3/ib_mayor_in",
                severidad: Severidad::Error,
                elemento: c.id.clone(),
                mensaje: format!("Ib = {ib} A supera In = {} A. No se cumple Ib ≤ In.", c.in_a),
                referencia,
                correcciones,
            });
        }
    }
    d
}

/// Menor sección de la Tabla 770.12.I cuya Iz corregida es >= `in_a`.
fn menor_seccion_para(cat: &Catalogo770, c: &Circuito, in_a: f64) -> Option<f64> {
    cat.iz
        .datos
        .iter()
        .map(|f| f.seccion_mm2)
        .filter(|&s| s + EPS >= c.seccion_mm2)
        .find(|&s| {
            iz_corregida(cat, s, c.cables_cargados, c.circuitos_en_canalizacion)
                .map_or(false, |r| r.iz_a + EPS >= in_a)
        })
}

// ------------------------------------------------------------- unidad ----- //

/// Valida una vivienda completa: alcance (770.1), circuitos mínimos (770.7.4)
/// y cada circuito.
pub fn validar_unidad(cat: &Catalogo770, u: &Unidad) -> Vec<Diagnostico> {
    let mut d = Vec::new();
    let alc = &cat.alcance;

    if let Some(in_cab) = u.in_cabecera_a {
        if in_cab > alc.datos.max_in_cabecera_a + EPS {
            d.push(Diagnostico {
                codigo: "770.1/alcance_corriente",
                severidad: Severidad::Error,
                elemento: u.id.clone(),
                mensaje: format!(
                    "La protección de cabecera es de {in_cab} A; la Sección 770 aplica hasta {} A. Corresponde AEA 90364-7-771.",
                    alc.datos.max_in_cabecera_a
                ),
                referencia: alc.referencia(),
                correcciones: vec![Correccion::CambiarNorma { norma: "AEA-771-2006".into() }],
            });
        }
    }
    if let Some(icc) = u.icc_origen_ka {
        if icc > alc.datos.max_icc_origen_ka + EPS {
            d.push(Diagnostico {
                codigo: "770.1/alcance_icc",
                severidad: Severidad::Error,
                elemento: u.id.clone(),
                mensaje: format!(
                    "Icc presunta en el origen de {icc} kA; la Sección 770 aplica hasta {} kA. Corresponde AEA 90364-7-771.",
                    alc.datos.max_icc_origen_ka
                ),
                referencia: alc.referencia(),
                correcciones: vec![Correccion::CambiarNorma { norma: "AEA-771-2006".into() }],
            });
        }
    }

    // 770.7.4 — número mínimo de circuitos
    let g = grado_electrificacion(cat, u.superficie_cubierta_m2, u.superficie_semicubierta_m2);
    if let Some(min) = cat.circuitos_minimos.datos.iter().find(|m| m.grado == g.grado) {
        let contar = |sigla: &str| u.circuitos.iter().filter(|c| c.tipo.eq_ignore_ascii_case(sigla)).count() as u32;
        let (iug, tug) = (contar("IUG"), contar("TUG"));
        let total = u.circuitos.iter().filter(|c| cat.tipo(&c.tipo).is_some()).count() as u32;
        let variante_ok = min.variantes.iter().any(|v| iug >= v.iug && tug >= v.tug);
        if !variante_ok || total < min.cantidad_minima {
            // Proponer la variante que requiere menos circuitos adicionales.
            let mejor = min
                .variantes
                .iter()
                .min_by_key(|v| v.iug.saturating_sub(iug) + v.tug.saturating_sub(tug))
                .expect("cada grado tiene al menos una variante");
            let mut correcciones = Vec::new();
            let falta_iug = mejor.iug.saturating_sub(iug);
            let falta_tug = mejor.tug.saturating_sub(tug);
            if falta_iug > 0 {
                correcciones.push(Correccion::AgregarCircuitos { tipo: "IUG".into(), cantidad: falta_iug });
            }
            if falta_tug > 0 {
                correcciones.push(Correccion::AgregarCircuitos { tipo: "TUG".into(), cantidad: falta_tug });
            }
            let resto = min.cantidad_minima.saturating_sub(total + falta_iug + falta_tug);
            if resto > 0 {
                correcciones.push(Correccion::AgregarCircuitos { tipo: "libre_eleccion".into(), cantidad: resto });
            }
            d.push(Diagnostico {
                codigo: "770.7.4/circuitos_minimos",
                severidad: Severidad::Error,
                elemento: u.id.clone(),
                mensaje: format!(
                    "Grado '{}' ({:.1} m²) exige al menos {} circuitos con alguna de las variantes de la Tabla 770.7.II; hay {} (IUG: {}, TUG: {}).",
                    g.grado, g.superficie_limite_m2, min.cantidad_minima, total, iug, tug
                ),
                referencia: cat.circuitos_minimos.referencia(),
                correcciones,
            });
        }
    }

    for c in &u.circuitos {
        d.extend(validar_circuito(cat, c));
    }
    d
}
