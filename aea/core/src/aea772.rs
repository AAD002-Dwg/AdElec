//! Reglas de AEA 90364-7-772 (Edición 2019) para partes comunes de edificios de propiedad horizontal.

use crate::catalogo::{Catalogo770, Catalogo772};
use crate::diagnostico::{Correccion, Diagnostico, Severidad};
use crate::modelo::Unidad;
use crate::aea770::CALIBRES_ITM_A;

const EPS: f64 = 1e-9;

/// Valida los circuitos y dotaciones mínimas de áreas comunes bajo AEA 772.
pub fn validar_unidad_772(cat772: &Catalogo772, _cat770: &Catalogo770, u: &Unidad) -> Vec<Diagnostico> {
    let mut d = Vec::new();
    let ref_tipos = cat772.tipos_circuito.referencia();
    let ref_minimos = cat772.circuitos_minimos.referencia();

    // 1. Validar tipos de circuitos y calibres máximos (Tabla 772.7.I)
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
