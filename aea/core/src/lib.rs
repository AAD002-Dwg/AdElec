pub mod catalogo;
pub mod modelo;
pub mod diagnostico;
pub mod aea770;

pub use catalogo::Catalogo770;
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
}
