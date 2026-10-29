pub mod catalogo;
pub mod modelo;
pub mod diagnostico;
pub mod aea770;
pub mod aea771;
pub mod aea772;
pub mod ffi;

pub use catalogo::{Catalogo770, Catalogo771, Catalogo772};
pub use modelo::{Circuito, ResultadoCaneria, ResultadoDpms, Unidad};
pub use diagnostico::{Diagnostico, Correccion, Severidad};

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_catalogo_embebido_carga_ok() {
        let cat = Catalogo770::embebido();
        assert_eq!(cat.alcance.datos.max_in_cabecera_a, 63.0);
        assert_eq!(cat.alcance.datos.max_icc_origen_ka, 10.0);
        assert_eq!(cat.tipos_circuito.datos.len(), 3);
        assert_eq!(cat.demanda_potencia.datos.tug_va, 2200.0);
        assert_eq!(cat.canalizaciones.datos.factor_ocupacion_max, 0.35);

        let cat771 = Catalogo771::embebido();
        assert_eq!(cat771.circuitos_minimos_comercial.datos.len(), 4);
        assert_eq!(cat771.demanda_potencia.datos.iug_va_por_boca, 150.0);

        let cat772 = Catalogo772::embebido();
        assert_eq!(cat772.demanda_servicios.datos.tug_va, 2200.0);
    }

    #[test]
    fn test_grado_electrificacion_770() {
        let cat = Catalogo770::embebido();
        // 50m2 cubierta + 10m2 semicubierta (al 50% = 5m2) -> 55m2 = Mínimo
        let r1 = aea770::grado_electrificacion(&cat, 50.0, 10.0);
        assert_eq!(r1.grado, "minimo");
        assert_eq!(r1.superficie_limite_m2, 55.0);

        // 60m2 cubierta + 10m2 semicubierta = 65m2 -> Medio (>60 y <=130)
        let r2 = aea770::grado_electrificacion(&cat, 60.0, 10.0);
        assert_eq!(r2.grado, "medio");
        assert_eq!(r2.superficie_limite_m2, 65.0);

        // 140m2 cubierta -> Elevado (>130 y <=200)
        let r3 = aea770::grado_electrificacion(&cat, 140.0, 0.0);
        assert_eq!(r3.grado, "elevado");

        // 250m2 cubierta -> Superior (>200)
        let r4 = aea770::grado_electrificacion(&cat, 250.0, 0.0);
        assert_eq!(r4.grado, "superior");
    }

    #[test]
    fn test_iz_corregida_monofasico_agrupamiento() {
        let cat = Catalogo770::embebido();
        // Cable 2.5 mm2, monofásico (2x), 1 circuito -> Iz = 21A, factor = 1.0
        let r1 = aea770::iz_corregida(&cat, 2.5, 2, 1).unwrap();
        assert_eq!(r1.iz_tabla_a, 21.0);
        assert_eq!(r1.factor_agrupamiento, 1.0);
        assert_eq!(r1.iz_a, 21.0);

        // Cable 2.5 mm2, monofásico (2x), 2 circuitos agrupados -> factor 0.80 -> Iz = 16.8A
        let r2 = aea770::iz_corregida(&cat, 2.5, 2, 2).unwrap();
        assert_eq!(r2.iz_tabla_a, 21.0);
        assert_eq!(r2.factor_agrupamiento, 0.80);
        assert_eq!(r2.iz_a, 16.8);
    }

    #[test]
    fn test_coordinacion_y_quick_fixes() {
        let cat = Catalogo770::embebido();
        // Circuito TUG con cable 2.5 mm2, térmica 25A (supera Iz=21A)
        let c = Circuito {
            id: "C1".into(),
            tipo: "TUG".into(),
            seccion_mm2: 2.5,
            in_a: 25.0,
            ib_a: Some(14.0),
            bocas: 10,
            cables_cargados: 2,
            circuitos_en_canalizacion: 1,
            ..Default::default()
        };

        let diag = aea770::validar_circuito(&cat, &c);
        // Debe fallar 770.6/proteccion_max (TUG max 20A) e in_mayor_iz (25 > 21)
        assert!(diag.iter().any(|d| d.codigo == "770.6/proteccion_max"));
        assert!(diag.iter().any(|d| d.codigo == "770.15.3/in_mayor_iz"));

        // Verificar que propone corrección
        let in_diag = diag.iter().find(|d| d.codigo == "770.15.3/in_mayor_iz").unwrap();
        assert!(in_diag.correcciones.iter().any(|corr| matches!(corr, Correccion::CambiarProteccion { in_a } if *in_a <= 20.0)));
        assert!(in_diag.correcciones.iter().any(|corr| matches!(corr, Correccion::CambiarSeccion { seccion_mm2 } if *seccion_mm2 >= 4.0)));
    }

    #[test]
    fn test_puntos_minimos_ambientes() {
        let cat = Catalogo770::embebido();

        // 1. Estar/Comedor de 24 m2 -> 24 / 18 = 2 IUG, 24 / 6 = 4 TUG
        let estar = modelo::Ambiente {
            id: "A1".into(),
            nombre: "Living Principal".into(),
            tipo: "sala_estar".into(),
            superficie_m2: 24.0,
            longitud_m: None,
            bocas_iug: 0,
            bocas_tug: 0,
            bocas_tue: 0,
        };
        let p_estar = aea770::calcular_puntos_minimos_ambiente(&cat, &estar, "medio");
        assert_eq!(p_estar.iug_minimo, 2);
        assert_eq!(p_estar.tug_minimo, 4);

        // 2. Dormitorio < 10m2 -> 1 IUG, 2 TUG
        let dorm1 = modelo::Ambiente {
            id: "A2".into(),
            nombre: "Dormitorio Chico".into(),
            tipo: "dormitorio".into(),
            superficie_m2: 9.5,
            longitud_m: None,
            bocas_iug: 0,
            bocas_tug: 0,
            bocas_tue: 0,
        };
        let p_dorm1 = aea770::calcular_puntos_minimos_ambiente(&cat, &dorm1, "medio");
        assert_eq!(p_dorm1.iug_minimo, 1);
        assert_eq!(p_dorm1.tug_minimo, 2);

        // 3. Cocina grado medio -> 2 IUG, 3 TUG + 2 módulos
        let cocina = modelo::Ambiente {
            id: "A3".into(),
            nombre: "Cocina".into(),
            tipo: "cocina".into(),
            superficie_m2: 12.0,
            longitud_m: None,
            bocas_iug: 0,
            bocas_tug: 0,
            bocas_tue: 0,
        };
        let p_cocina = aea770::calcular_puntos_minimos_ambiente(&cat, &cocina, "medio");
        assert_eq!(p_cocina.iug_minimo, 2);
        assert_eq!(p_cocina.tug_minimo, 3);
        assert_eq!(p_cocina.modulos_fijos, 2);
    }

    #[test]
    fn test_puntos_minimos_con_acentos_y_mayusculas() {
        let cat = Catalogo770::embebido();

        // 1. "BAÑO" en mayúsculas con virgulilla
        let bano = modelo::Ambiente {
            id: "B1".into(),
            nombre: "Baño Suite".into(),
            tipo: "BAÑO".into(),
            superficie_m2: 4.5,
            longitud_m: None,
            bocas_iug: 0,
            bocas_tug: 0,
            bocas_tue: 0,
        };
        let p_bano = aea770::calcular_puntos_minimos_ambiente(&cat, &bano, "medio");
        assert_eq!(p_bano.iug_minimo, 1);
        assert_eq!(p_bano.tug_minimo, 1);

        // 2. "BALCÓN" con tilde
        let balcon = modelo::Ambiente {
            id: "BAL1".into(),
            nombre: "Balcón Frente".into(),
            tipo: "BALCÓN".into(),
            superficie_m2: 6.0,
            longitud_m: Some(4.0),
            bocas_iug: 0,
            bocas_tug: 0,
            bocas_tue: 0,
        };
        let p_balcon = aea770::calcular_puntos_minimos_ambiente(&cat, &balcon, "medio");
        assert_eq!(p_balcon.iug_minimo, 1);
        assert_eq!(p_balcon.tug_minimo, 0);

        // 3. Superficie 0 o negativa no causa panics
        let r_cero = aea770::grado_electrificacion(&cat, 0.0, 0.0);
        assert_eq!(r_cero.grado, "minimo");
    }

    #[test]
    fn test_aea771_comercial_y_circuitos_especiales() {
        let cat771 = Catalogo771::embebido();
        let cat770 = Catalogo770::embebido();

        // 1. Grados de oficinas y locales comerciales (Tabla 771.8.IV)
        let g1 = aea771::grado_electrificacion_comercial(&cat771, 25.0);
        assert_eq!(g1.grado, "minimo");
        assert_eq!(g1.max_demanda_kva, Some(4.5));

        let g2 = aea771::grado_electrificacion_comercial(&cat771, 60.0);
        assert_eq!(g2.grado, "medio");
        assert_eq!(g2.max_demanda_kva, Some(7.8));

        let g3 = aea771::grado_electrificacion_comercial(&cat771, 100.0);
        assert_eq!(g3.grado, "elevado");
        assert_eq!(g3.max_demanda_kva, Some(12.2));

        let g4 = aea771::grado_electrificacion_comercial(&cat771, 200.0);
        assert_eq!(g4.grado, "superior");

        // 2. Circuito IUE en 771: max 12 bocas, max 32 A
        let c_iue = Circuito {
            id: "C_IUE".into(),
            tipo: "IUE".into(),
            seccion_mm2: 2.5,
            in_a: 20.0,
            ib_a: Some(15.0),
            bocas: 10,
            cables_cargados: 2,
            circuitos_en_canalizacion: 1,
            ..Default::default()
        };
        let diags_iue = aea771::validar_circuito_771(&cat771, &c_iue);
        assert!(diags_iue.is_empty(), "IUE reglamentario debe tener 0 diagnósticos");

        // 3. Circuito IUE excedido en bocas (15 bocas > 12)
        let c_iue_exc = Circuito {
            id: "C_IUE2".into(),
            tipo: "IUE".into(),
            seccion_mm2: 2.5,
            in_a: 20.0,
            ib_a: None,
            bocas: 15,
            cables_cargados: 2,
            circuitos_en_canalizacion: 1,
            ..Default::default()
        };
        let diags_iue_exc = aea771::validar_circuito_771(&cat771, &c_iue_exc);
        assert!(diags_iue_exc.iter().any(|d| d.codigo == "771.7/max_bocas"));

        // 4. Puntos mínimos comerciales en salón general de 45 m² (Tabla 771.8.VI -> 1 boca cada 9m² = 5 IUG, 5 TUG)
        let salon = modelo::Ambiente {
            id: "LOC1".into(),
            nombre: "Salón Ventas".into(),
            tipo: "salon_general".into(),
            superficie_m2: 45.0,
            longitud_m: None,
            bocas_iug: 2, // menos que 5
            bocas_tug: 5,
            bocas_tue: 0,
        };
        let pts_com = aea771::calcular_puntos_minimos_comercial(&cat771, &salon, "medio");
        assert_eq!(pts_com.iug_minimo, 5);
        assert_eq!(pts_com.tug_minimo, 5);

        // 5. Validación integral de local comercial con pocos circuitos y pocas bocas IUG
        let u_com = Unidad {
            id: "LOCAL_1".into(),
            superficie_cubierta_m2: 60.0, // grado medio -> mín 3 circuitos (Tabla 771.8.V)
            norma: Some("AEA-771".into()),
            cantidad_id: Some(1),
            circuitos: vec![
                Circuito { id: "C1".into(), tipo: "IUG".into(), seccion_mm2: 1.5, in_a: 10.0, bocas: 6, ..Default::default() },
                Circuito { id: "C2".into(), tipo: "TUG".into(), seccion_mm2: 2.5, in_a: 16.0, bocas: 8, ..Default::default() },
            ],
            ambientes: vec![salon],
            ..Default::default()
        };
        let diags_u = aea771::validar_unidad_771(&cat771, &cat770, &u_com);
        assert!(diags_u.iter().any(|d| d.codigo == "771.8.5/circuitos_minimos"));
        assert!(diags_u.iter().any(|d| d.codigo == "771.8.6/bocas_iug"));
    }

    #[test]
    fn test_aea772_espacios_comunes() {
        let cat772 = Catalogo772::embebido();
        let cat770 = Catalogo770::embebido();

        // Unidad partes comunes de 50 m2: exige mín 4 circuitos (Tabla 772.8.I)
        let u = Unidad {
            id: "Servicios Generales".into(),
            superficie_cubierta_m2: 50.0,
            superficie_semicubierta_m2: 0.0,
            in_cabecera_a: Some(40.0),
            icc_origen_ka: Some(4.5),
            norma: Some("AEA-772".into()),
            circuitos: vec![
                Circuito {
                    id: "C1".into(),
                    tipo: "IUG".into(),
                    seccion_mm2: 1.5,
                    in_a: 10.0,
                    ib_a: None,
                    bocas: 8,
                    cables_cargados: 2,
                    circuitos_en_canalizacion: 1,
                    ..Default::default()
                },
                Circuito {
                    id: "C2".into(),
                    tipo: "TUG".into(),
                    seccion_mm2: 2.5,
                    in_a: 16.0,
                    ib_a: None,
                    bocas: 6,
                    cables_cargados: 2,
                    circuitos_en_canalizacion: 1,
                    ..Default::default()
                },
            ],
            ambientes: vec![],
            ..Default::default()
        };

        // Tiene 2 circuitos, requiere 4 -> diagnóstico 772.8.1/circuitos_minimos
        let diags = aea772::validar_unidad_772(&cat772, &cat770, &u);
        assert!(diags.iter().any(|d| d.codigo == "772.8.1/circuitos_minimos"));

        // Cálculo de DPMS de servicios generales (Tabla 772.9.I / II)
        let dpms = aea772::calcular_dpms_772(&cat772, &u);
        // C1 IUG: 8 bocas * 60 VA = 480 VA (1 circuito -> factor 1.0)
        // C2 TUG: 2200 VA (1 circuito -> factor 1.0)
        assert_eq!(dpms.dpms_va, 2680.0);

        // Simultaneidad para conjunto de 10 viviendas (Tabla 772.9.III: 5 a 15 -> 0.6)
        assert_eq!(aea772::simultaneidad_unidades_funcionales(&cat772, 10, false), 0.6);
        assert_eq!(aea772::simultaneidad_unidades_funcionales(&cat772, 10, true), 0.8);
    }

    #[test]
    fn test_caida_tension_y_quick_fix() {
        let cat = Catalogo770::embebido();
        // Circuito IUG monofásico con L = 40 m, Ib = 10 A, cable 1.5 mm2
        // ΔV = 2 * 0.0212 * 40 * 10 * 0.9 / 1.5 = 10.176 V -> 10.176 / 220 = 4.62% (> 3.0% máx para IUG)
        let c = Circuito {
            id: "C_LARGO".into(),
            tipo: "IUG".into(),
            seccion_mm2: 1.5,
            in_a: 10.0,
            ib_a: Some(10.0),
            bocas: 8,
            cables_cargados: 2,
            circuitos_en_canalizacion: 1,
            longitud_m: Some(40.0),
            cos_phi: Some(0.9),
            ..Default::default()
        };

        let diags = aea770::validar_circuito(&cat, &c);
        let diag_dv = diags.iter().find(|d| d.codigo == "770.12/caida_tension");
        assert!(diag_dv.is_some(), "Debe detectar caída de tensión excesiva");
        let d = diag_dv.unwrap();
        assert!(d.mensaje.contains("supera el límite reglamentario"));
        // Con 2.5 mm2 -> ΔV = 2.77% <= 3.0%
        assert!(d.correcciones.iter().any(|corr| matches!(corr, Correccion::CambiarSeccion { seccion_mm2 } if *seccion_mm2 == 2.5)));

        // Además en TUG también rige el 3.0% según Cláusula 770.15.6:
        // L = 35 m, Ib = 16 A, S = 2.5 mm2 -> ΔV = 2 * 0.0212 * 35 * 16 * 0.9 / 2.5 = 8.547 V -> 3.88% (> 3.0%)
        let c_tug = Circuito {
            id: "C_TUG_LARGO".into(),
            tipo: "TUG".into(),
            seccion_mm2: 2.5,
            in_a: 16.0,
            ib_a: Some(16.0),
            bocas: 10,
            longitud_m: Some(35.0),
            cos_phi: Some(0.9),
            ..Default::default()
        };
        let diags_tug = aea770::validar_circuito(&cat, &c_tug);
        assert!(diags_tug.iter().any(|d| d.codigo == "770.12/caida_tension"));
    }

    #[test]
    fn test_dpms_canerias_pe_y_dps_770() {
        let cat = Catalogo770::embebido();

        // 1. Sección mínima de PE (Tabla 770.14.I)
        assert_eq!(aea770::seccion_minima_pe(&cat, 1.5), 2.5);
        assert_eq!(aea770::seccion_minima_pe(&cat, 4.0), 4.0);
        assert_eq!(aea770::seccion_minima_pe(&cat, 25.0), 16.0);
        assert_eq!(aea770::seccion_minima_pe(&cat, 50.0), 25.0);

        // 2. Ocupación de cañería (Tabla 770.10.VIII): 3 cables de 6 mm² (3 * 31.17 = 93.51 mm²)
        // En RP 16 (127 mm²) -> 93.51 / 127 = 73.6% (> 35% -> NO cumple)
        let r_rp16 = aea770::dimensionar_caneria(&cat, 6.0, 3, "RP 16").unwrap();
        assert!(!r_rp16.cumple);
        // Buscando en familia "aislante_rigido" -> sugiere RSP 22 (302 mm², 30.96% <= 35%)
        let r_auto = aea770::dimensionar_caneria(&cat, 6.0, 3, "aislante_rigido").unwrap();
        assert!(r_auto.cumple);
        assert_eq!(r_auto.designacion, "RSP 22");

        // 3. DPMS en vivienda Grado Medio (80 m² -> 3 circuitos mín -> coef 0.80 según Tabla 770.8.II)
        let u = Unidad {
            id: "VIV_1".into(),
            superficie_cubierta_m2: 80.0,
            resistencia_pat_ohm: Some(55.0), // > 40 Ohm -> falla 770.14/resistencia_pat
            alimentacion_red: Some("con_pararrayos".into()),
            tiene_dps: Some(false),          // -> falla 770.15.4/dps_obligatorio
            circuitos: vec![
                Circuito {
                    id: "C1".into(),
                    tipo: "IUG".into(),
                    seccion_mm2: 1.5,
                    in_a: 10.0,
                    bocas: 9, // 9 * 60 * 2/3 = 360 VA
                    ..Default::default()
                },
                Circuito {
                    id: "C2".into(),
                    tipo: "TUG".into(),
                    seccion_mm2: 2.5,
                    in_a: 16.0,
                    bocas: 10, // 2200 VA
                    ..Default::default()
                },
                Circuito {
                    id: "C3".into(),
                    tipo: "TUE".into(),
                    seccion_mm2: 4.0,
                    seccion_pe_mm2: Some(2.5), // < 4.0 mm² -> falla 770.14/seccion_pe
                    cano_designacion: Some("RP 16".into()), // 3x4.0 = 54.3 mm² / 127 = 42.7% > 35% -> falla 770.10/ocupacion_caneria
                    in_a: 20.0,
                    bocas: 2, // 3300 VA
                    ..Default::default()
                },
            ],
            ..Default::default()
        };

        let dpms = aea770::calcular_dpms_770(&cat, &u);
        // Suma bruta = 360 + 2200 + 3300 = 5860 VA; Coef = 0.8 -> DPMS = 4688 VA
        assert_eq!(dpms.potencia_sin_simultaneidad_va, 5860.0);
        assert_eq!(dpms.coeficiente_simultaneidad, 0.8);
        assert_eq!(dpms.dpms_va, 4688.0);

        let diags = aea770::validar_unidad(&cat, &u);
        assert!(diags.iter().any(|d| d.codigo == "770.14/resistencia_pat"));
        assert!(diags.iter().any(|d| d.codigo == "770.15.4/dps_obligatorio"));
        assert!(diags.iter().any(|d| d.codigo == "770.14/seccion_pe"));
        assert!(diags.iter().any(|d| d.codigo == "770.10/ocupacion_caneria"));
    }

    #[test]
    fn test_cortocircuito_termico_y_poder_de_corte() {
        let cat = Catalogo770::embebido();

        // 1. Verificación analítica de cortocircuito en conductor:
        // Icc = 3 kA (3000 A), t = 0.02 s, cable 1.5 mm2, PVC (k=115)
        // S_min = 3000 * sqrt(0.02) / 115 = 3000 * 0.14142 / 115 = 3.69 mm2
        let res1 = aea770::verificar_cortocircuito_conductor(3.0, 0.02, 1.5, true);
        assert!(!res1.cumple);
        assert_eq!(res1.seccion_minima_mm2, 3.69);

        // Con 4.0 mm2 -> cumple
        let res2 = aea770::verificar_cortocircuito_conductor(3.0, 0.02, 4.0, true);
        assert!(res2.cumple);

        // 2. Verificación de Icn en circuito vs Icc presunta en tablero:
        let u = Unidad {
            id: "U_ICC".into(),
            superficie_cubierta_m2: 50.0,
            superficie_semicubierta_m2: 0.0,
            in_cabecera_a: Some(32.0),
            icc_origen_ka: Some(4.5), // Icc 4.5 kA
            norma: Some("AEA-770".into()),
            circuitos: vec![
                Circuito {
                    id: "C1".into(),
                    tipo: "IUG".into(),
                    seccion_mm2: 1.5,
                    in_a: 10.0,
                    icn_ka: Some(3.0), // 3.0 kA < 4.5 kA
                    ..Default::default()
                },
            ],
            ambientes: vec![],
            ..Default::default()
        };

        let diags = aea770::validar_unidad(&cat, &u);
        assert!(diags.iter().any(|d| d.codigo == "770.16/poder_de_corte"));
    }
}
