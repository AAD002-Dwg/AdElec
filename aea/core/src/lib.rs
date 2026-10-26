pub mod catalogo;
pub mod modelo;
pub mod diagnostico;
pub mod aea770;
pub mod aea771;
pub mod aea772;
pub mod ffi;

pub use catalogo::{Catalogo770, Catalogo771, Catalogo772};
pub use modelo::{Circuito, Unidad};
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
        let _cat770 = Catalogo770::embebido();

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
        };
        let diags_iue_exc = aea771::validar_circuito_771(&cat771, &c_iue_exc);
        assert!(diags_iue_exc.iter().any(|d| d.codigo == "771.7/max_bocas"));
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
                },
            ],
            ambientes: vec![],
        };

        // Tiene 2 circuitos, requiere 4 -> diagnóstico 772.8.1/circuitos_minimos
        let diags = aea772::validar_unidad_772(&cat772, &cat770, &u);
        assert!(diags.iter().any(|d| d.codigo == "772.8.1/circuitos_minimos"));
    }
}


