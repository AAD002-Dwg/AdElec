//! Reglas de AEA 90364-7-772 (Edición 2019) para partes comunes de edificios de propiedad horizontal.

use crate::catalogo::{Catalogo770, Catalogo772};
use crate::diagnostico::{Correccion, Diagnostico, Severidad};
use crate::modelo::{ResultadoDpms, Unidad};
use crate::aea770::{
    dimensionar_caneria, iz_corregida, seccion_minima_pe, validar_caida_tension, CALIBRES_ITM_A,
};

const EPS: f64 = 1e-9;

/// Calcula el coeficiente de simultaneidad para un conjunto de unidades funcionales (viviendas o locales)
/// integrantes de un mismo edificio según Tabla 772.9.III.
pub fn simultaneidad_unidades_funcionales(
    cat772: &Catalogo772,
    cantidad_unidades: u32,
    es_oficinas_o_locales: bool,
) -> f64 {
    if cantidad_unidades < 2 {
        return 1.0;
    }
    let fila = cat772
        .demanda_servicios
        .datos
        .simultaneidad_unidades_funcionales
        .iter()
        .find(|r| {
            cantidad_unidades >= r.desde_unidades
                && r.hasta_unidades.map_or(true, |h| cantidad_unidades <= h)
        })
        .unwrap_or_else(|| {
            cat772
                .demanda_servicios
                .datos
                .simultaneidad_unidades_funcionales
                .last()
                .unwrap()
        });
    if es_oficinas_o_locales {
        fila.oficinas_locales
    } else {
        fila.viviendas
    }
}

fn factor_por_cantidad(tabla: &[f64], n: usize) -> f64 {
    if n == 0 || tabla.is_empty() {
        return 1.0;
    }
    // Índices en JSON: [1, 2, 3, 4, 5, 6, 7, 8o9, >=10]
    let idx = match n {
        1 => 0,
        2 => 1,
        3 => 2,
        4 => 3,
        5 => 4,
        6 => 5,
        7 => 6,
        8 | 9 => 7,
        _ => 8,
    };
    tabla.get(idx).copied().unwrap_or(1.0)
}

/// Calcula la Demanda de Potencia Máxima Simultánea (DPMS) de servicios generales
/// bajo AEA 772.9 (Tablas 772.9.I y 772.9.II).
pub fn calcular_dpms_772(cat772: &Catalogo772, u: &Unidad) -> ResultadoDpms {
    let cfg = &cat772.demanda_servicios.datos;

    let mut sum_ilu = 0.0;
    let mut n_ilu = 0usize;
    let mut sum_tomas = 0.0;
    let mut n_tomas = 0usize;
    let mut sum_fm = 0.0;
    let mut n_fm = 0usize;

    for c in &u.circuitos {
        let sigla = c.tipo.trim().to_uppercase();
        match sigla.as_str() {
            "IUG" | "IUE" | "ITE" => {
                let min_va = if c.incluye_tomacorrientes {
                    cfg.iug_con_tomas_va
                } else {
                    (c.bocas.max(1) as f64) * cfg.iug_va_por_boca
                };
                sum_ilu += c.potencia_va.unwrap_or(0.0).max(min_va);
                n_ilu += 1;
            }
            "TUG" => {
                sum_tomas += c.potencia_va.unwrap_or(0.0).max(cfg.tug_va);
                n_tomas += 1;
            }
            "TUE" => {
                sum_tomas += c.potencia_va.unwrap_or(0.0).max(cfg.tue_va);
                n_tomas += 1;
            }
            _ => {
                // Fuerza motriz (ascensores, bombas, portones, extractores, aire acondicionado)
                let min_fm = c.potencia_va.unwrap_or(1500.0);
                sum_fm += min_fm;
                n_fm += 1;
            }
        }
    }

    let f_ilu = factor_por_cantidad(&cfg.simultaneidad_circuitos.iluminacion_viviendas, n_ilu);
    let f_tomas = factor_por_cantidad(&cfg.simultaneidad_circuitos.tomacorrientes, n_tomas);
    let f_fm = factor_por_cantidad(&cfg.simultaneidad_circuitos.fuerza_motriz, n_fm);

    let pot_bruta = sum_ilu + sum_tomas + sum_fm;
    let dpms_va = sum_ilu * f_ilu + sum_tomas * f_tomas + sum_fm * f_fm;
    let coef_efectivo = if pot_bruta > 0.0 {
        ((dpms_va / pot_bruta) * 100.0).round() / 100.0
    } else {
        1.0
    };

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
        coeficiente_simultaneidad: coef_efectivo,
        dpms_va: (dpms_va * 100.0).round() / 100.0,
        dpms_kva: ((dpms_va / 1000.0) * 100.0).round() / 100.0,
        corriente_proyecto_ib_a: (ib * 100.0).round() / 100.0,
        proteccion_cabecera_sugerida_a: cab_sugerida,
        referencia: cat772.demanda_servicios.referencia(),
    }
}

/// Valida los circuitos y dotaciones mínimas de áreas comunes bajo AEA 772.
pub fn validar_unidad_772(cat772: &Catalogo772, cat770: &Catalogo770, u: &Unidad) -> Vec<Diagnostico> {
    let mut d = Vec::new();
    let ref_tipos = cat772.tipos_circuito.referencia();
    let ref_minimos = cat772.circuitos_minimos.referencia();

    // 1. Validar tipos de circuitos, calibres máximos, coordinación, caída de tensión, PE y cañerías
    for c in &u.circuitos {
        if let Some(tipo) = cat772.tipo(&c.tipo) {
            if c.bocas > tipo.max_bocas {
                d.push(Diagnostico {
                    codigo: "772.7/max_bocas",
                    severidad: Severidad::Error,
                    elemento: c.id.clone(),
                    mensaje: format!(
                        "Circuito {} ({}) en partes comunes tiene {} bocas; el máximo normativo es {}.",
                        c.id, c.tipo, c.bocas, tipo.max_bocas
                    ),
                    referencia: ref_tipos.clone(),
                    correcciones: vec![Correccion::AgregarCircuitos {
                        tipo: c.tipo.clone(),
                        cantidad: ((c.bocas as f64) / (tipo.max_bocas as f64)).ceil() as u32 - 1,
                    }],
                });
            }

            if let Some(max_in) = tipo.max_in_a {
                if c.in_a > max_in + EPS {
                    let correcciones = CALIBRES_ITM_A
                        .iter()
                        .filter(|&&cal| cal <= max_in + EPS)
                        .map(|&cal| Correccion::CambiarProteccion { in_a: cal })
                        .collect();

                    d.push(Diagnostico {
                        codigo: "772.7/proteccion_max",
                        severidad: Severidad::Error,
                        elemento: c.id.clone(),
                        mensaje: format!(
                            "Circuito {} ({}) tiene protección de {:.0} A; el calibre máximo en servicios generales es {:.0} A.",
                            c.id, c.tipo, c.in_a, max_in
                        ),
                        referencia: ref_tipos.clone(),
                        correcciones,
                    });
                }
            }
        }

        // Coordinación térmica Ib <= In <= Iz (772.15 remite a 771.19)
        if let Ok(iz) = iz_corregida(cat770, c.seccion_mm2, c.cables_cargados, c.circuitos_en_canalizacion) {
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
                    codigo: "772.15/in_mayor_iz",
                    severidad: Severidad::Error,
                    elemento: c.id.clone(),
                    mensaje: format!(
                        "Circuito {} en servicios generales: In ({:.0} A) > Iz admisible ({:.1} A) para {} mm².",
                        c.id, c.in_a, iz.iz_a, c.seccion_mm2
                    ),
                    referencia: cat770.coordinacion.referencia(),
                    correcciones: corrs,
                });
            }
        }

        // Caída de tensión
        if let Some(l) = c.longitud_m {
            if l > 0.0 {
                d.extend(validar_caida_tension(cat770, c, &c.tipo));
            }
        }

        // Conductor PE
        if let Some(sec_pe) = c.seccion_pe_mm2 {
            let min_pe = seccion_minima_pe(cat770, c.seccion_mm2);
            if sec_pe + EPS < min_pe {
                d.push(Diagnostico {
                    codigo: "772.18/seccion_pe",
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

        // Ocupación de cañería
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
                            codigo: "772.12/ocupacion_caneria",
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

    // 2. Cantidad mínima de circuitos para servicios generales según superficie (Tabla 772.8.I)
    let sup = u.superficie_cubierta_m2;
    if sup > 0.0 {
        if let Some(rango) = cat772.circuitos_minimos.datos.rangos.iter().find(|r| {
            let sobre_min = r.sup_desde_m2.map_or(true, |m| sup > m + EPS);
            let bajo_max = r.sup_hasta_m2.map_or(true, |m| sup <= m + EPS);
            sobre_min && bajo_max
        }) {
            let cant = u.circuitos.len() as u32;
            if cant < rango.cantidad_minima {
                d.push(Diagnostico {
                    codigo: "772.8.1/circuitos_minimos",
                    severidad: Severidad::Error,
                    elemento: u.id.clone(),
                    mensaje: format!(
                        "Las partes comunes ({:.1} m²) cuentan con {} circuitos; la Tabla 772.8.I exige un mínimo de {}.",
                        sup, cant, rango.cantidad_minima
                    ),
                    referencia: ref_minimos.clone(),
                    correcciones: vec![Correccion::AgregarCircuitos {
                        tipo: "IUG".into(),
                        cantidad: rango.cantidad_minima - cant,
                    }],
                });
            }
        }
    }

    // 3. Recomendación de iluminación partida en "fija" y "automática" (sensores/pulsadores)
    let tiene_iug = u.circuitos.iter().any(|c| c.tipo.eq_ignore_ascii_case("IUG"));
    if tiene_iug && u.circuitos.iter().filter(|c| c.tipo.eq_ignore_ascii_case("IUG")).count() == 1 && sup > 30.0 {
        d.push(Diagnostico {
            codigo: "772.8.1/iluminacion_dividida",
            severidad: Severidad::Advertencia,
            elemento: u.id.clone(),
            mensaje: "En partes comunes de más de 30 m², la iluminación de circulación debe desdoblarse en circuitos de iluminación fija y automática (pulsadores o detectores de presencia).".into(),
            referencia: ref_minimos,
            correcciones: vec![Correccion::AgregarCircuitos { tipo: "IUG".into(), cantidad: 1 }],
        });
    }

    d
}
