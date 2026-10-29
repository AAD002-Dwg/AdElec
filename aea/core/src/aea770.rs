//! Reglas de AEA 90364-7-770 (Edición 2017).
//!
//! Cada regla toma sus valores del catálogo (verificado contra el PDF) y adjunta
//! la referencia normativa a su resultado. Ningún valor normativo está escrito
//! en este archivo, salvo los calibres normalizados de interruptores, que
//! provienen de IEC 60898-1 y no de la AEA (ver `CALIBRES_ITM_A`).

use crate::catalogo::{Catalogo770, Referencia};
use crate::diagnostico::{Correccion, Diagnostico, Severidad};
use crate::modelo::{Circuito, ResultadoCaneria, ResultadoDpms, Unidad};
use serde::Serialize;

/// Valores comerciales estándar de corriente asignada para pequeños interruptores automáticos
/// en el mercado argentino (IRAM 2169 / IEC 60898-1): 6, 10, 16, 20, 25, 32, 40, 50, 63 A.
pub const CALIBRES_ITM_A: [f64; 9] = [6.0, 10.0, 16.0, 20.0, 25.0, 32.0, 40.0, 50.0, 63.0];

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
    let sup = (cubierta_m2 + t.parametros.factor_semicubierta * semicubierta_m2).max(0.0);
    let rango = t
        .datos
        .iter()
        .find(|r| {
            let sobre_min = r.sup_mayor_que_m2.map_or(true, |m| sup > m + EPS);
            let bajo_max = r.sup_hasta_m2.map_or(true, |m| sup <= m + EPS);
            sobre_min && bajo_max
        })
        .unwrap_or_else(|| &t.datos[0]);
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

// ------------------------------------------------------------- PE / Caños - //

/// Determina la sección nominal mínima del conductor de protección PE según Tabla 770.14.I.
/// - S <= 16 mm² -> S (con mínimo absoluto de 2.5 mm² en canalizaciones según Tabla 770.11.I)
/// - 16 < S <= 35 mm² -> 16 mm²
/// - S > 35 mm² -> S / 2
pub fn seccion_minima_pe(cat: &Catalogo770, seccion_fase_mm2: f64) -> f64 {
    let cfg = &cat.conductor_pe.datos;
    let s = seccion_fase_mm2.max(0.0);
    let pe_regla = if s <= cfg.umbral_igual_fase_hasta_mm2 + EPS {
        s
    } else if s <= cfg.umbral_fijo_16_hasta_mm2 + EPS {
        16.0
    } else {
        s / 2.0
    };
    pe_regla.max(cfg.seccion_min_pe_mm2)
}

/// Dimensiona o verifica una cañería bajo la regla del 35% de ocupación máxima (770.10.3 / Tabla 770.10.VIII).
/// `conductores_totales`: cantidad total de conductores unipolares alojados (incluyendo PE).
/// `familia_o_designacion`: puede ser una designación exacta ("RS 19", "RP 20") o una familia ("metalico_semipesado", "aislante_rigido", "aislante_curvable").
pub fn dimensionar_caneria(
    cat: &Catalogo770,
    seccion_mm2: f64,
    conductores_totales: u32,
    familia_o_designacion: &str,
) -> Option<ResultadoCaneria> {
    let datos = &cat.canalizaciones.datos;
    let cable = datos
        .cables_unipolares
        .iter()
        .find(|c| (c.seccion_mm2 - seccion_mm2).abs() < EPS)
        .or_else(|| datos.cables_unipolares.iter().find(|c| c.seccion_mm2 >= seccion_mm2 - EPS))?;

    let area_cables = cable.seccion_total_mm2 * (conductores_totales.max(1) as f64);
    let clave = familia_o_designacion.trim();

    // 1. Si coincide con una designación exacta (ej. "RS 16", "RP 20", "CSP 19")
    if let Some(cano) = datos.canos.iter().find(|c| c.designacion.eq_ignore_ascii_case(clave)) {
        let ocupacion = area_cables / cano.seccion_interna_mm2;
        return Some(ResultadoCaneria {
            designacion: cano.designacion.clone(),
            tipo: cano.tipo.clone(),
            seccion_interna_mm2: cano.seccion_interna_mm2,
            area_cables_mm2: (area_cables * 100.0).round() / 100.0,
            ocupacion_pct: (ocupacion * 10000.0).round() / 100.0,
            cumple: ocupacion <= datos.factor_ocupacion_max + EPS,
            referencia: cat.canalizaciones.referencia(),
        });
    }

    // 2. Si es un tipo/familia, busca el menor caño que cumpla <= 35%
    let familia = if clave.is_empty() { "aislante_rigido" } else { clave };
    let cano = datos
        .canos
        .iter()
        .filter(|c| c.tipo.eq_ignore_ascii_case(familia))
        .find(|c| (area_cables / c.seccion_interna_mm2) <= datos.factor_ocupacion_max + EPS)
        .or_else(|| datos.canos.iter().filter(|c| c.tipo.eq_ignore_ascii_case(familia)).last())?;

    let ocupacion = area_cables / cano.seccion_interna_mm2;
    Some(ResultadoCaneria {
        designacion: cano.designacion.clone(),
        tipo: cano.tipo.clone(),
        seccion_interna_mm2: cano.seccion_interna_mm2,
        area_cables_mm2: (area_cables * 100.0).round() / 100.0,
        ocupacion_pct: (ocupacion * 10000.0).round() / 100.0,
        cumple: ocupacion <= datos.factor_ocupacion_max + EPS,
        referencia: cat.canalizaciones.referencia(),
    })
}

/// Determina la menor caja de embutir reglamentaria según el volumen ocupado por los cables (Tablas 770.10.X y 770.10.XI).
pub fn dimensionar_caja(cat: &Catalogo770, cables_secciones: &[(f64, u32)]) -> Option<String> {
    let mut vol_req = 0.0;
    for &(sec, cant) in cables_secciones {
        let v_unit = cat
            .cajas
            .datos
            .volumen_cable_cm3
            .iter()
            .find(|v| v.seccion_mm2 >= sec - EPS)
            .map(|v| v.volumen_cm3)
            .unwrap_or(25.0);
        vol_req += v_unit * (cant as f64);
    }
    let mut cajas_ordenadas = cat.cajas.datos.cajas.clone();
    cajas_ordenadas.sort_by(|a, b| a.volumen_utilizable_cm3.partial_cmp(&b.volumen_utilizable_cm3).unwrap());
    cajas_ordenadas
        .into_iter()
        .find(|c| c.volumen_utilizable_cm3 + EPS >= vol_req)
        .map(|c| c.tipo)
}

// ------------------------------------------------------------- DPMS ------- //

/// Calcula la Demanda de Potencia Máxima Simultánea (DPMS) bajo AEA 770.8 (Tablas 770.8.I y 770.8.II).
pub fn calcular_dpms_770(cat: &Catalogo770, u: &Unidad) -> ResultadoDpms {
    let cfg = &cat.demanda_potencia.datos;
    let grado_res = grado_electrificacion(cat, u.superficie_cubierta_m2, u.superficie_semicubierta_m2);
    let min_circs = cat
        .circuitos_minimos
        .datos
        .iter()
        .find(|m| m.grado == grado_res.grado)
        .map(|m| m.cantidad_minima)
        .unwrap_or(2);

    let coef = cfg
        .coeficientes_simultaneidad
        .iter()
        .find(|c| c.circuitos_minimos == min_circs)
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
                    (c.bocas.max(1) as f64) * cfg.iug_va_por_boca * cfg.iug_factor
                }
            }
            "TUG" => cfg.tug_va,
            "TUE" => cfg.tue_va,
            _ => 0.0,
        };
        let real_va = c.potencia_va.unwrap_or(0.0).max(min_va);
        if matches!(sigla.as_str(), "IUG" | "TUG" | "TUE") {
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
    let cab_sugerida = CALIBRES_ITM_A
        .iter()
        .copied()
        .find(|&cal| cal >= target_cab - EPS)
        .unwrap_or(63.0);

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

// ------------------------------------------------------------- circuito --- //

/// Valida un circuito contra 770.6, 770.10, 770.11, 770.14 y 770.15 (y circuitos específicos según Guía AEA 770 / AEA 771.7).
pub fn validar_circuito(cat: &Catalogo770, c: &Circuito) -> Vec<Diagnostico> {
    let mut d = Vec::new();
    let sigla_norm = c.tipo.trim().to_uppercase();

    let max_in_circuito: f64;
    if let Some(tipo) = cat.tipo(&sigla_norm) {
        max_in_circuito = tipo.max_in_a;
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
    } else if let Some(gen_esp) = cat
        .guia770
        .clasificacion_circuitos
        .datos
        .generales_y_especiales
        .iter()
        .find(|g| g.sigla.eq_ignore_ascii_case(&sigla_norm))
    {
        max_in_circuito = gen_esp.max_in_a;
        if c.bocas > gen_esp.max_bocas {
            d.push(Diagnostico {
                codigo: "770.6/bocas",
                severidad: Severidad::Error,
                elemento: c.id.clone(),
                mensaje: format!("{} bocas en circuito {}: el máximo es {}.", c.bocas, gen_esp.sigla, gen_esp.max_bocas),
                referencia: cat.guia770.clasificacion_circuitos.referencia(),
                correcciones: vec![],
            });
        }
        if c.in_a > gen_esp.max_in_a + EPS {
            d.push(Diagnostico {
                codigo: "770.6/proteccion_max",
                severidad: Severidad::Error,
                elemento: c.id.clone(),
                mensaje: format!("Protección de {} A en circuito {}: el máximo es {} A.", c.in_a, gen_esp.sigla, gen_esp.max_in_a),
                referencia: cat.guia770.clasificacion_circuitos.referencia(),
                correcciones: vec![Correccion::CambiarProteccion { in_a: gen_esp.max_in_a }],
            });
        }
    } else if let Some(esp) = cat
        .guia770
        .clasificacion_circuitos
        .datos
        .usos_especificos
        .iter()
        .find(|e| e.sigla.eq_ignore_ascii_case(&sigla_norm))
    {
        max_in_circuito = esp.max_in_a.unwrap_or(63.0);
        if !esp.permitido_en_viviendas {
            d.push(Diagnostico {
                codigo: "770.6/ite_prohibido_vivienda",
                severidad: Severidad::Error,
                elemento: c.id.clone(),
                mensaje: format!(
                    "El circuito {} ({}) no se puede utilizar en viviendas (Guía AEA 770 pág. 24 Nota 4).",
                    esp.sigla, esp.designacion
                ),
                referencia: cat.guia770.clasificacion_circuitos.referencia(),
                correcciones: vec![],
            });
        }
        if let Some(max_bocas) = esp.max_bocas {
            if c.bocas > max_bocas {
                d.push(Diagnostico {
                    codigo: "770.6/bocas",
                    severidad: Severidad::Error,
                    elemento: c.id.clone(),
                    mensaje: format!("{} bocas en circuito {}: el máximo es {}.", c.bocas, esp.sigla, max_bocas),
                    referencia: cat.guia770.clasificacion_circuitos.referencia(),
                    correcciones: vec![],
                });
            }
        }
        if let Some(max_in) = esp.max_in_a {
            if c.in_a > max_in + EPS {
                d.push(Diagnostico {
                    codigo: "770.6/proteccion_max",
                    severidad: Severidad::Error,
                    elemento: c.id.clone(),
                    mensaje: format!("Protección de {} A en circuito {}: el máximo es {} A.", c.in_a, esp.sigla, max_in),
                    referencia: cat.guia770.clasificacion_circuitos.referencia(),
                    correcciones: vec![Correccion::CambiarProteccion { in_a: max_in }],
                });
            }
        }
    } else {
        d.push(Diagnostico {
            codigo: "770.6/tipo",
            severidad: Severidad::Advertencia,
            elemento: c.id.clone(),
            mensaje: format!(
                "El tipo '{}' no figura en la Tabla 770.6.I (IUG, TUG, TUE) ni en los circuitos específicos de AEA 771.7.",
                c.tipo
            ),
            referencia: cat.tipos_circuito.referencia(),
            correcciones: vec![],
        });
        return d;
    }

    // 770.11 — sección mínima (incluyendo IUG con tomacorrientes derivados = 2.5 mm²)
    let clave_sec = if sigla_norm == "IUG" && c.incluye_tomacorrientes {
        "IUG_con_tomacorrientes"
    } else if matches!(sigla_norm.as_str(), "IUE" | "APM" | "ATE" | "ACU" | "MBTF" | "OCE") {
        "circuitos_especificos"
    } else {
        sigla_norm.as_str()
    };
    if let Some(min) = cat.seccion_minima(clave_sec) {
        if c.seccion_mm2 + EPS < min {
            d.push(Diagnostico {
                codigo: "770.11/seccion_min",
                severidad: Severidad::Error,
                elemento: c.id.clone(),
                mensaje: format!("Sección {} mm² en circuito {}: la mínima es {} mm².", c.seccion_mm2, clave_sec, min),
                referencia: cat.secciones_minimas.referencia(),
                correcciones: vec![Correccion::CambiarSeccion { seccion_mm2: min }],
            });
        }
    }

    // 770.14.I — sección mínima del conductor de protección PE (si fue informado)
    if let Some(sec_pe) = c.seccion_pe_mm2 {
        let min_pe = seccion_minima_pe(cat, c.seccion_mm2);
        if sec_pe + EPS < min_pe {
            d.push(Diagnostico {
                codigo: "770.14/seccion_pe",
                severidad: Severidad::Error,
                elemento: c.id.clone(),
                mensaje: format!(
                    "Conductor de protección PE de {} mm² en circuito {} (fase {} mm²): la Tabla 770.14.I exige al menos {} mm².",
                    sec_pe, c.id, c.seccion_mm2, min_pe
                ),
                referencia: cat.conductor_pe.referencia(),
                correcciones: vec![Correccion::CambiarSeccionPe { seccion_pe_mm2: min_pe }],
            });
        }
    }

    // 770.10.VIII — ocupación máxima del 35% de la cañería (si fue informada)
    if let Some(ref desig) = c.cano_designacion {
        if !desig.trim().is_empty() {
            let n_cond = (c.cables_cargados as u32) * c.circuitos_en_canalizacion.max(1) + 1; // +1 por PE
            if let Some(res_cano) = dimensionar_caneria(cat, c.seccion_mm2, n_cond, desig) {
                if !res_cano.cumple {
                    let mut corrs = Vec::new();
                    if let Some(sugerido) = dimensionar_caneria(cat, c.seccion_mm2, n_cond, &res_cano.tipo) {
                        if sugerido.cumple {
                            corrs.push(Correccion::CambiarCaneria { cano_designacion: sugerido.designacion });
                        }
                    }
                    d.push(Diagnostico {
                        codigo: "770.10/ocupacion_caneria",
                        severidad: Severidad::Error,
                        elemento: c.id.clone(),
                        mensaje: format!(
                            "Cañería '{}' en circuito {} tiene una ocupación de {:.1}% ({} conductores de {} mm²); el límite reglamentario es 35.0%.",
                            desig, c.id, res_cano.ocupacion_pct, n_cond, c.seccion_mm2
                        ),
                        referencia: cat.canalizaciones.referencia(),
                        correcciones: corrs,
                    });
                }
            }
        }
    }

    // 770.15.3 — coordinación Ib <= In <= Iz
    d.extend(validar_coordinacion(cat, c, max_in_circuito));

    // 770.15.6 — caída de tensión reglamentaria (3% iluminación y tomas; 5% motores en régimen)
    if let Some(l) = c.longitud_m {
        if l > 0.0 {
            d.extend(validar_caida_tension(cat, c, &sigla_norm));
        }
    }
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

/// Calcula la caída de tensión porcentual (ΔV%) según física eléctrica estándar
/// para conductores de cobre con aislación termoplástica (temperatura de régimen 70 °C,
/// resistividad rho = 0.0212 Ohm*mm²/m).
pub fn calcular_caida_tension(
    longitud_m: f64,
    corriente_a: f64,
    seccion_mm2: f64,
    cables_cargados: u8,
    cos_phi: f64,
) -> f64 {
    if seccion_mm2 <= 0.0 || longitud_m <= 0.0 || corriente_a <= 0.0 {
        return 0.0;
    }
    let rho = 0.0212; // Resistencia de Cu a 70°C [Ohm * mm^2 / m]
    let cos_p = if cos_phi > 0.0 && cos_phi <= 1.0 { cos_phi } else { 0.9 };
    if cables_cargados >= 3 {
        // Trifásico 380 V entre fases: ΔV = sqrt(3) * rho * L * I * cos(phi) / S
        let v_n = 380.0;
        let delta_v = 1.7320508 * rho * longitud_m * corriente_a * cos_p / seccion_mm2;
        (delta_v / v_n) * 100.0
    } else {
        // Monofásico 220 V fase-neutro: ΔV = 2 * rho * L * I * cos(phi) / S
        let v_n = 220.0;
        let delta_v = 2.0 * rho * longitud_m * corriente_a * cos_p / seccion_mm2;
        (delta_v / v_n) * 100.0
    }
}

pub fn validar_caida_tension(cat: &Catalogo770, c: &Circuito, tipo_sigla: &str) -> Vec<Diagnostico> {
    let l = match c.longitud_m {
        Some(val) if val > 0.0 => val,
        _ => return vec![],
    };
    let i_calc = c.ib_a.unwrap_or(c.in_a);
    let cos_p = c.cos_phi.unwrap_or(0.9);
    let delta_v_pct = calcular_caida_tension(l, i_calc, c.seccion_mm2, c.cables_cargados, cos_p);

    // Cláusula 770.15.6 / 771.19.7:
    // 1. Circuitos terminales, de uso general o especial, para iluminación y tomacorrientes: 3 %.
    // 2. Otros circuitos que alimentan sólo motores (APM, ACU, BOM, ASC, etc.): 5 % en régimen.
    let sigla_up = tipo_sigla.trim().to_uppercase();
    let es_solo_motor = matches!(sigla_up.as_str(), "APM" | "AP" | "ACU" | "BOM" | "ASC" | "MOTOR");
    let max_pct = if es_solo_motor {
        cat.caida_tension.datos.max_pct_motores_regimen
    } else {
        cat.caida_tension.datos.max_pct_iluminacion_y_tomas
    };

    if delta_v_pct > max_pct + EPS {
        let mut correcciones = Vec::new();
        let secciones_posibles = [1.5, 2.5, 4.0, 6.0, 10.0, 16.0, 25.0, 35.0];
        if let Some(&s_ok) = secciones_posibles.iter().find(|&&s| {
            s > c.seccion_mm2 && calcular_caida_tension(l, i_calc, s, c.cables_cargados, cos_p) <= max_pct + EPS
        }) {
            correcciones.push(Correccion::CambiarSeccion { seccion_mm2: s_ok });
        }

        vec![Diagnostico {
            codigo: "770.12/caida_tension",
            severidad: Severidad::Error,
            elemento: c.id.clone(),
            mensaje: format!(
                "Caída de tensión de {:.2}% en circuito {} (L = {:.1} m, S = {} mm², I = {:.1} A): supera el límite reglamentario de {:.1}% (770.15.6).",
                delta_v_pct, c.tipo, l, c.seccion_mm2, i_calc, max_pct
            ),
            referencia: cat.caida_tension.referencia(),
            correcciones,
        }]
    } else {
        vec![]
    }
}

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct ResultadoCortocircuito {
    pub seccion_minima_mm2: f64,
    pub energia_admisible_a2s: f64,
    pub energia_defecto_a2s: f64,
    pub cumple: bool,
}

/// Comprueba la condición térmica del conductor ante cortocircuito (I²·t ≤ k²·S²).
/// Para conductores de cobre aislados en PVC (IRAM NM 247-3), k = 115.
/// Para aislación termoestable (XLPE / IRAM 62266), k = 143.
/// t: tiempo de despeje del interruptor automático (por defecto 0.02 s = 1 ciclo).
pub fn verificar_cortocircuito_conductor(
    icc_ka: f64,
    tiempo_despeje_s: f64,
    seccion_mm2: f64,
    aislacion_pvc: bool,
) -> ResultadoCortocircuito {
    let k = if aislacion_pvc { 115.0 } else { 143.0 };
    let i_a = icc_ka.max(0.0) * 1000.0;
    let t = if tiempo_despeje_s > 0.0 { tiempo_despeje_s } else { 0.02 };
    let s_min = (i_a * t.sqrt()) / k;
    let energia_admisible = (k * seccion_mm2).powi(2);
    let energia_defecto = i_a.powi(2) * t;
    let cumple = seccion_mm2 + EPS >= s_min;

    ResultadoCortocircuito {
        seccion_minima_mm2: (s_min * 100.0).round() / 100.0,
        energia_admisible_a2s: energia_admisible,
        energia_defecto_a2s: energia_defecto,
        cumple,
    }
}

// ------------------------------------------------------------- unidad ----- //

/// Valida una vivienda completa: alcance (770.1), puesta a tierra (770.14), DPS (770.15.4),
/// circuitos mínimos (770.7.4), puntos mínimos (770.7.5) y cada circuito.
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

        // 770.16.1 — Poder de corte mínimo (Icn >= Icc en origen)
        for c in &u.circuitos {
            let icn = c.icn_ka.unwrap_or(3.0); // 3.0 kA por defecto residencial
            if icc > icn + EPS {
                d.push(Diagnostico {
                    codigo: "770.16/poder_de_corte",
                    severidad: Severidad::Error,
                    elemento: c.id.clone(),
                    mensaje: format!(
                        "Poder de corte asignado Icn = {:.1} kA del circuito {} es inferior a la Icc presunta de {:.1} kA en el origen. Debe ser Icn ≥ Icc.",
                        icn, c.id, icc
                    ),
                    referencia: alc.referencia(),
                    correcciones: vec![],
                });
            }
        }
    }

    // 770.14.4.1 — Resistencia máxima de puesta a tierra (Ra <= 40 Ohm)
    if let Some(ra) = u.resistencia_pat_ohm {
        let max_ra = cat.conductor_pe.datos.resistencia_pat_max_ohm;
        if ra > max_ra + EPS {
            d.push(Diagnostico {
                codigo: "770.14/resistencia_pat",
                severidad: Severidad::Error,
                elemento: u.id.clone(),
                mensaje: format!(
                    "Resistencia de puesta a tierra Ra = {:.1} Ω supera el máximo permanente admisible de {:.0} Ω (770.14.4.1).",
                    ra, max_ra
                ),
                referencia: cat.conductor_pe.referencia(),
                correcciones: vec![],
            });
        }
    }

    // 770.15.4 / Tabla 770.15.III — Protección contra sobretensiones (DPS)
    if let Some(ref alim) = u.alimentacion_red {
        let aq2 = u.aq_dias_tormenta.unwrap_or(30) > cat.dps.datos.umbral_aq2_dias_ano;
        if let Some(regla) = cat.dps.datos.reglas.iter().find(|r| r.alimentacion.eq_ignore_ascii_case(alim)) {
            let obligatorio = if aq2 { regla.aq2_obligatorio } else { regla.aq1_obligatorio };
            if obligatorio && u.tiene_dps == Some(false) {
                d.push(Diagnostico {
                    codigo: "770.15.4/dps_obligatorio",
                    severidad: Severidad::Error,
                    elemento: u.id.clone(),
                    mensaje: format!(
                        "La instalación con alimentación '{}' requiere obligatoriamente descargadores de sobretensión (DPS) según Tabla 770.15.III.",
                        alim
                    ),
                    referencia: cat.dps.referencia(),
                    correcciones: vec![Correccion::InstalarDps],
                });
            }
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
                .unwrap_or(&min.variantes[0]);
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

    // 770.7.5 — puntos mínimos de utilización por ambiente
    let ref_puntos = cat.puntos_minimos.referencia();
    for amb in &u.ambientes {
        let req = calcular_puntos_minimos_ambiente(cat, amb, &g.grado);
        if amb.bocas_iug > 0 && amb.bocas_iug < req.iug_minimo {
            d.push(Diagnostico {
                codigo: "770.7.5/bocas_iug",
                severidad: Severidad::Error,
                elemento: amb.id.clone(),
                mensaje: format!(
                    "Ambiente '{}' ({}, {:.1} m²): tiene {} boca(s) IUG; la Tabla 770.7.III exige al menos {}.",
                    amb.nombre, amb.tipo, amb.superficie_m2, amb.bocas_iug, req.iug_minimo
                ),
                referencia: ref_puntos.clone(),
                correcciones: vec![],
            });
        }
        if amb.bocas_tug > 0 && amb.bocas_tug < req.tug_minimo {
            d.push(Diagnostico {
                codigo: "770.7.5/bocas_tug",
                severidad: Severidad::Error,
                elemento: amb.id.clone(),
                mensaje: format!(
                    "Ambiente '{}' ({}, {:.1} m²): tiene {} boca(s) TUG; la Tabla 770.7.III exige al menos {}{}.",
                    amb.nombre, amb.tipo, amb.superficie_m2, amb.bocas_tug, req.tug_minimo,
                    if req.modulos_fijos > 0 { format!(" (+ {} módulos fijos)", req.modulos_fijos) } else { "".into() }
                ),
                referencia: ref_puntos.clone(),
                correcciones: vec![],
            });
        }
    }

    d
}

/// Calcula los puntos mínimos de utilización reglamentarios para un ambiente según Tabla 770.7.III.
pub fn calcular_puntos_minimos_ambiente(
    cat: &Catalogo770,
    amb: &crate::modelo::Ambiente,
    grado: &str,
) -> crate::modelo::PuntosMinimosAmbiente {
    let d = &cat.puntos_minimos.datos;
    let mut tipo = amb.tipo.to_lowercase();
    tipo = tipo
        .replace('á', "a")
        .replace('é', "e")
        .replace('í', "i")
        .replace('ó', "o")
        .replace('ú', "u")
        .replace('ñ', "n");

    let sup = amb.superficie_m2.max(0.0);

    let (iug, tug, modulos) = match tipo.as_str() {
        "sala_estar" | "living_dining" | "estar" | "comedor" | "escritorio" | "estudio" | "biblioteca" => {
            let r = &d.estar_comedor;
            let iug = ((sup / r.iug_cada_m2).ceil() as u32).max(r.iug_minimo);
            let tug = ((sup / r.tug_cada_m2).ceil() as u32).max(r.tug_minimo);
            (iug, tug, 0)
        }
        "dormitorio" | "bedroom" => {
            if sup < 10.0 {
                (d.dormitorio_menor_10.iug, d.dormitorio_menor_10.tug, 0)
            } else if sup <= 36.0 {
                (d.dormitorio_10_a_36.iug, d.dormitorio_10_a_36.tug, 0)
            } else {
                (d.dormitorio_mayor_36.iug, d.dormitorio_mayor_36.tug, 0)
            }
        }
        "cocina" | "kitchen" => {
            let det = match grado {
                "superior" => &d.cocina.superior,
                "elevado"  => &d.cocina.elevado,
                "medio"    => &d.cocina.medio,
                _          => &d.cocina.minimo,
            };
            (det.iug, det.tug, det.modulos_fijos)
        }
        "bano" | "toilette" | "bathroom" => {
            (d.bano.iug, d.bano.tug, 0)
        }
        "vestibulo" | "garaje" | "hall" | "vestidor" | "garage" | "hallway" => {
            if grado == "minimo" {
                (d.vestibulo_garaje.minimo.iug, d.vestibulo_garaje.minimo.tug, 0)
            } else {
                let r = &d.vestibulo_garaje.medio_o_superior;
                let iug = ((sup / r.iug_cada_m2).ceil() as u32).max(r.iug_minimo);
                let tug = ((sup / r.tug_cada_m2).ceil() as u32).max(r.tug_minimo);
                (iug, tug, 0)
            }
        }
        "pasillo" | "corredor" => {
            let long = amb.longitud_m.map(|l| l.max(0.0)).unwrap_or_else(|| sup.sqrt().max(1.0));
            let r = &d.pasillo_cubierto;
            let iug = ((long / r.iug_cada_m).ceil() as u32).max(r.iug_minimo);
            let tug = if grado != "minimo" && long > r.tug_longitud_min_m {
                ((long / r.tug_cada_m).ceil() as u32).max(1)
            } else {
                0
            };
            (iug, tug, 0)
        }
        "lavadero" | "laundry" => {
            if grado == "minimo" {
                (d.lavadero.minimo.iug, d.lavadero.minimo.tug, 0)
            } else {
                (d.lavadero.medio_o_superior.iug, d.lavadero.medio_o_superior.tug, 0)
            }
        }
        "balcon" | "galeria" | "atrio" => {
            let long = amb.longitud_m.map(|l| l.max(0.0)).unwrap_or_else(|| sup.sqrt().max(1.0));
            let iug = ((long / d.balcon_galeria.iug_cada_m).ceil() as u32).max(1);
            (iug, d.balcon_galeria.tug, 0)
        }
        _ => {
            let iug = if sup > 0.0 { 1 } else { 0 };
            let tug = if sup > 0.0 { 1 } else { 0 };
            (iug, tug, 0)
        }
    };

    crate::modelo::PuntosMinimosAmbiente {
        ambiente_id: amb.id.clone(),
        iug_minimo: iug,
        tug_minimo: tug,
        modulos_fijos: modulos,
    }
}
