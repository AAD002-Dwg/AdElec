//! Reglas de AEA 90364-7-771 (Edición 2006) para oficinas, locales y usos terciarios.

use crate::catalogo::{Catalogo770, Catalogo771, Referencia};
use crate::diagnostico::{Correccion, Diagnostico, Severidad};
use crate::modelo::{Ambiente, Circuito, PuntosMinimosAmbiente, ResultadoDpms, Unidad};
use crate::aea770::{
    dimensionar_caneria, iz_corregida, seccion_minima_pe, validar_caida_tension, CALIBRES_ITM_A,
};
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

/// Calcula los puntos mínimos de utilización en oficinas y locales comerciales según Tabla 771.8.VI.
pub fn calcular_puntos_minimos_comercial(
    cat: &Catalogo771,
    amb: &Ambiente,
    grado: &str,
) -> PuntosMinimosAmbiente {
    let d = &cat.puntos_minimos_comercial.datos;
    let mut tipo = amb.tipo.to_lowercase();
    tipo = tipo
        .replace('á', "a")
        .replace('é', "e")
        .replace('í', "i")
        .replace('ó', "o")
        .replace('ú', "u")
        .replace('ñ', "n");

    let sup = amb.superficie_m2.max(0.0);
    let es_elevado_o_sup = matches!(grado, "elevado" | "superior");

    let (iug, tug, modulos) = match tipo.as_str() {
        "salon_general" | "salon" | "local" | "comercio" | "oficina" | "open_space" | "sala_estar" => {
            let r = &d.salon_general;
            let iug = ((sup / r.iug_cada_m2).ceil() as u32).max(r.iug_minimo);
            let tug = ((sup / r.tug_cada_m2).ceil() as u32).max(r.tug_minimo);
            let tue = if es_elevado_o_sup {
                let perim = 4.0 * sup.sqrt().max(1.0);
                let cada_m = r.tue_cada_m_perimetro_elevado_sup.unwrap_or(18.0);
                ((perim / cada_m).ceil() as u32).max(1)
            } else {
                0
            };
            (iug, tug, tue)
        }
        "sala_reuniones" | "conferencias" | "microcine" => {
            let r = &d.sala_reuniones;
            let iug = ((sup / r.iug_cada_m2).ceil() as u32).max(r.iug_minimo);
            let tug = ((sup / r.tug_cada_m2).ceil() as u32).max(r.tug_minimo);
            let tue = if es_elevado_o_sup { r.tue_minimo_elevado_sup.unwrap_or(1) } else { 0 };
            (iug, tug, tue)
        }
        "despacho_privado" | "despacho" | "escritorio" | "privado" => {
            (d.despacho_privado.iug, d.despacho_privado.tug, 0)
        }
        "cocina" | "kitchenette" | "office" => {
            let det = if es_elevado_o_sup { &d.cocina.elevado_superior } else { &d.cocina.minimo_medio };
            (det.iug, det.tug, det.tue)
        }
        "bano" | "toilette" | "sanitario" => {
            if es_elevado_o_sup {
                let r = &d.bano.elevado_superior;
                let iug = ((sup / r.iug_cada_m2).ceil() as u32).max(r.iug_minimo);
                (iug, r.tug, 0)
            } else {
                (d.bano.minimo_medio.iug, d.bano.minimo_medio.tug, 0)
            }
        }
        "vestibulo" | "recepcion" | "hall" => {
            let r = &d.vestibulo_recepcion;
            let iug = ((sup / r.iug_cada_m2).ceil() as u32).max(r.iug_minimo);
            let tug = ((sup / r.tug_cada_m2).ceil() as u32).max(r.tug_minimo);
            let tue = if es_elevado_o_sup { r.tue_minimo_elevado_sup.unwrap_or(1) } else { 0 };
            (iug, tug, tue)
        }
        "pasillo" | "corredor" | "circulacion" => {
            let long = amb.longitud_m.map(|l| l.max(0.0)).unwrap_or_else(|| sup.sqrt().max(1.0));
            let r = &d.pasillo;
            let iug = ((long / r.iug_cada_m).ceil() as u32).max(r.iug_minimo);
            let tug = if long > r.tug_longitud_min_m {
                ((long / r.tug_cada_m).ceil() as u32).max(1)
            } else {
                0
            };
            (iug, tug, 0)
        }
        _ => {
            let r = &d.salon_general;
            let iug = ((sup / r.iug_cada_m2).ceil() as u32).max(1);
            let tug = ((sup / r.tug_cada_m2).ceil() as u32).max(1);
            (iug, tug, 0)
        }
    };

    PuntosMinimosAmbiente {
        ambiente_id: amb.id.clone(),
        iug_minimo: iug,
        tug_minimo: tug,
        modulos_fijos: modulos,
    }
}

/// Calcula la Demanda Máxima de Potencia Simultánea (DPMS) bajo AEA 771.9 (Tablas 771.9.I y 771.9.II)
/// para oficinas y locales comerciales.
pub fn calcular_dpms_771(cat: &Catalogo771, u: &Unidad) -> ResultadoDpms {
    let cfg = &cat.demanda_potencia.datos;
    let grado_res = grado_electrificacion_comercial(cat, u.superficie_cubierta_m2);

    let coef = cfg
        .coeficientes_por_grado
        .iter()
        .find(|c| c.grado == grado_res.grado)
        .map(|c| c.coeficiente)
        .unwrap_or(1.0);

    let mut suma_general_especial_va = 0.0;
    let mut suma_especificos_va = 0.0;

    for c in &u.circuitos {
        let sigla = c.tipo.trim().to_uppercase();
        let min_va = match sigla.as_str() {
            "IUG" => {
                if c.incluye_tomacorrientes {
                    cfg.iug_con_tomas_va
                } else {
                    (c.bocas.max(1) as f64) * cfg.iug_va_por_boca * cfg.iug_factor_oficinas_locales
                }
            }
            "TUG" => cfg.tug_va,
            "IUE" => (c.bocas.max(1) as f64) * cfg.iue_va_por_boca * cfg.iue_factor_oficinas_locales,
            "TUE" => cfg.tue_va,
            _ => 0.0,
        };
        let real_va = c.potencia_va.unwrap_or(0.0).max(min_va);
        if matches!(sigla.as_str(), "IUG" | "TUG" | "IUE" | "TUE") {
            suma_general_especial_va += real_va;
        } else {
            suma_especificos_va += real_va;
        }
    }

    let pot_bruta = suma_general_especial_va + suma_especificos_va;
    let dpms_va = suma_general_especial_va * coef + suma_especificos_va;
    let es_trifasico = u.circuitos.iter().any(|c| c.cables_cargados >= 3);
    let v_ref = if es_trifasico { 1.7320508 * 380.0 } else { 220.0 };
    let ib = if v_ref > 0.0 { dpms_va / v_ref } else { 0.0 };

    let target_cab = (ib * 1.15).max(25.0);
    let calibres_comerciales = [25.0, 32.0, 40.0, 50.0, 63.0, 80.0, 100.0, 125.0];
    let cab_sugerida = calibres_comerciales
        .iter()
        .copied()
        .find(|&cal| cal >= target_cab - EPS)
        .unwrap_or(125.0);

    ResultadoDpms {
        potencia_sin_simultaneidad_va: (pot_bruta * 100.0).round() / 100.0,
        coeficiente_simultaneidad: coef,
        dpms_va: (dpms_va * 100.0).round() / 100.0,
        dpms_kva: ((dpms_va / 1000.0) * 100.0).round() / 100.0,
        corriente_proyecto_ib_a: (ib * 100.0).round() / 100.0,
        proteccion_cabecera_sugerida_a: cab_sugerida,
        referencia: cat.demanda_potencia.referencia(),
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
    let grado_res = grado_electrificacion_comercial(cat771, u.superficie_cubierta_m2);

    // 1. Cantidad mínima de circuitos comerciales según grado (Tabla 771.8.V)
    if u.superficie_cubierta_m2 > 0.0 {
        if let Some(min) = cat771
            .circuitos_minimos_comercial
            .datos
            .iter()
            .find(|m| m.grado == grado_res.grado)
        {
            let contar = |sigla: &str| {
                u.circuitos
                    .iter()
                    .filter(|c| c.tipo.eq_ignore_ascii_case(sigla))
                    .count() as u32
            };
            let (iug, tug, iue, tue) = (contar("IUG"), contar("TUG"), contar("IUE"), contar("TUE"));
            let total = u.circuitos.len() as u32;
            let variante_ok = min.variantes.iter().any(|v| {
                iug >= v.iug && tug >= v.tug && iue >= v.iue && tue >= v.tue
            });
            if !variante_ok || total < min.cantidad_minima {
                let mejor = min
                    .variantes
                    .iter()
                    .min_by_key(|v| {
                        v.iug.saturating_sub(iug)
                            + v.tug.saturating_sub(tug)
                            + v.iue.saturating_sub(iue)
                            + v.tue.saturating_sub(tue)
                    })
                    .unwrap_or(&min.variantes[0]);
                let mut correcciones = Vec::new();
                if mejor.iug > iug {
                    correcciones.push(Correccion::AgregarCircuitos { tipo: "IUG".into(), cantidad: mejor.iug - iug });
                }
                if mejor.tug > tug {
                    correcciones.push(Correccion::AgregarCircuitos { tipo: "TUG".into(), cantidad: mejor.tug - tug });
                }
                if mejor.iue > iue {
                    correcciones.push(Correccion::AgregarCircuitos { tipo: "IUE".into(), cantidad: mejor.iue - iue });
                }
                if mejor.tue > tue {
                    correcciones.push(Correccion::AgregarCircuitos { tipo: "TUE".into(), cantidad: mejor.tue - tue });
                }
                d.push(Diagnostico {
                    codigo: "771.8.5/circuitos_minimos",
                    severidad: Severidad::Error,
                    elemento: u.id.clone(),
                    mensaje: format!(
                        "Local/oficina grado '{}' ({:.1} m²) exige al menos {} circuitos según Tabla 771.8.V; cuenta con {} (IUG: {}, TUG: {}, IUE: {}, TUE: {}).",
                        grado_res.grado, u.superficie_cubierta_m2, min.cantidad_minima, total, iug, tug, iue, tue
                    ),
                    referencia: cat771.circuitos_minimos_comercial.referencia(),
                    correcciones,
                });
            }
        }
    }

    // 2. Validar circuitos individuales contra Tabla 771.7.I, coordinación, caída de tensión, PE y cañerías
    for c in &u.circuitos {
        d.extend(validar_circuito_771(cat771, c));

        // Coordinación de conductores Ib <= In <= Iz
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

        // Caída de tensión (771.19.7)
        if let Some(l) = c.longitud_m {
            if l > 0.0 {
                d.extend(validar_caida_tension(cat770, c, &c.tipo));
            }
        }

        // Conductor de protección PE (Tabla 771.18.III / 770.14.I)
        if let Some(sec_pe) = c.seccion_pe_mm2 {
            let min_pe = seccion_minima_pe(cat770, c.seccion_mm2);
            if sec_pe + EPS < min_pe {
                d.push(Diagnostico {
                    codigo: "771.18/seccion_pe",
                    severidad: Severidad::Error,
                    elemento: c.id.clone(),
                    mensaje: format!(
                        "Conductor PE de {} mm² en circuito {} (fase {} mm²): se exige al menos {} mm².",
                        sec_pe, c.id, c.seccion_mm2, min_pe
                    ),
                    referencia: cat770.conductor_pe.referencia(),
                    correcciones: vec![Correccion::CambiarSeccionPe { seccion_pe_mm2: min_pe }],
                });
            }
        }

        // Ocupación de cañería <= 35%
        if let Some(ref desig) = c.cano_designacion {
            if !desig.trim().is_empty() {
                let n_cond = (c.cables_cargados as u32) * c.circuitos_en_canalizacion.max(1) + 1;
                if let Some(res_cano) = dimensionar_caneria(cat770, c.seccion_mm2, n_cond, desig) {
                    if !res_cano.cumple {
                        let mut corrs = Vec::new();
                        if let Some(sug) = dimensionar_caneria(cat770, c.seccion_mm2, n_cond, &res_cano.tipo) {
                            if sug.cumple {
                                corrs.push(Correccion::CambiarCaneria { cano_designacion: sug.designacion });
                            }
                        }
                        d.push(Diagnostico {
                            codigo: "771.12/ocupacion_caneria",
                            severidad: Severidad::Error,
                            elemento: c.id.clone(),
                            mensaje: format!(
                                "Cañería '{}' en circuito {} tiene ocupación de {:.1}% (máximo admisible 35.0%).",
                                desig, c.id, res_cano.ocupacion_pct
                            ),
                            referencia: cat770.canalizaciones.referencia(),
                            correcciones: corrs,
                        });
                    }
                }
            }
        }
    }

    // 3. Puntos mínimos comerciales por ambiente (Tabla 771.8.VI)
    let ref_pts = cat771.puntos_minimos_comercial.referencia();
    for amb in &u.ambientes {
        let req = calcular_puntos_minimos_comercial(cat771, amb, &grado_res.grado);
        if amb.bocas_iug > 0 && amb.bocas_iug < req.iug_minimo {
            d.push(Diagnostico {
                codigo: "771.8.6/bocas_iug",
                severidad: Severidad::Error,
                elemento: amb.id.clone(),
                mensaje: format!(
                    "Ambiente comercial '{}' ({}, {:.1} m²): tiene {} boca(s) IUG; la Tabla 771.8.VI exige al menos {}.",
                    amb.nombre, amb.tipo, amb.superficie_m2, amb.bocas_iug, req.iug_minimo
                ),
                referencia: ref_pts.clone(),
                correcciones: vec![],
            });
        }
        if amb.bocas_tug > 0 && amb.bocas_tug < req.tug_minimo {
            d.push(Diagnostico {
                codigo: "771.8.6/bocas_tug",
                severidad: Severidad::Error,
                elemento: amb.id.clone(),
                mensaje: format!(
                    "Ambiente comercial '{}' ({}, {:.1} m²): tiene {} boca(s) TUG; la Tabla 771.8.VI exige al menos {}.",
                    amb.nombre, amb.tipo, amb.superficie_m2, amb.bocas_tug, req.tug_minimo
                ),
                referencia: ref_pts.clone(),
                correcciones: vec![],
            });
        }
    }

    // 4. Selectividad / continuidad de servicio por diferencial (771.18.4: máx 3 circuitos por ID)
    if let Some(cant_id) = u.cantidad_id {
        let n_circs = u.circuitos.len() as u32;
        let id_min = ((n_circs as f64) / 3.0).ceil() as u32;
        if n_circs > 0 && cant_id < id_min {
            d.push(Diagnostico {
                codigo: "771.18.4/circuitos_por_id",
                severidad: Severidad::Advertencia,
                elemento: u.id.clone(),
                mensaje: format!(
                    "El tablero cuenta con {} circuitos y {} interruptor(es) diferencial(es); se recomienda no agrupar más de 3 circuitos por ID (mínimo {} ID).",
                    n_circs, cant_id, id_min
                ),
                referencia: cat771.tipos_circuito.referencia(),
                correcciones: vec![Correccion::AgregarDiferencial { cantidad: id_min - cant_id }],
            });
        }
    }

    d
}
