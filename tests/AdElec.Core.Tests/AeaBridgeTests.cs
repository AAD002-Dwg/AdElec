using System;
using System.Diagnostics;
using System.Linq;
using AdElec.Core.Aea;
using Xunit;

namespace AdElec.Core.Tests;

public class AeaBridgeTests
{
    [Fact]
    public void Test_CalcularGrado_RetornaValoresCorrectos()
    {
        var res = AeaBridge.CalcularGrado(75.0, 10.0);
        // 75 + 0.5 * 10 = 80 m2 -> Grado Medio
        Assert.Equal("medio", res.Grado);
        Assert.Equal(80.0, res.SuperficieLimiteM2);
        Assert.Equal("770.7.I", res.Referencia.Tabla);
    }

    [Fact]
    public void Test_CalcularPuntosMinimos_Ambiente()
    {
        var amb = new AeaAmbienteDto
        {
            Id = "A1",
            Nombre = "Cocina Principal",
            Tipo = "cocina",
            SuperficieM2 = 14.0
        };

        var p = AeaBridge.CalcularPuntosMinimos(amb, "medio");
        Assert.Equal(2, p.IugMinimo);
        Assert.Equal(3, p.TugMinimo);
        Assert.Equal(2, p.ModulosFijos);
    }

    [Fact]
    public void Test_ValidarUnidad_DetectaSobrecargaYEmiteQuickFix()
    {
        var unidad = new AeaUnidadDto
        {
            Id = "Vivienda 1",
            SuperficieCubiertaM2 = 80.0,
            SuperficieSemicubiertaM2 = 0.0,
            InCabeceraA = 32.0,
            IccOrigenKa = 3.0,
            Circuitos =
            {
                // Circuito 1: IUG correcto (1.5mm2, 10A, 8 bocas)
                new AeaCircuitoDto
                {
                    Id = "C1",
                    Tipo = "IUG",
                    SeccionMm2 = 1.5,
                    InA = 10.0,
                    Bocas = 8
                },
                // Circuito 2: TUG con térmica sobredimensionada (25A en cable 2.5mm2 -> Iz=21A)
                new AeaCircuitoDto
                {
                    Id = "C2",
                    Tipo = "TUG",
                    SeccionMm2 = 2.5,
                    InA = 25.0,
                    Bocas = 10
                }
            }
        };

        // Warmup para cargar DLL y desreferenciar JIT
        _ = AeaBridge.ValidarUnidad(unidad);

        var sw = Stopwatch.StartNew();
        var diags = AeaBridge.ValidarUnidad(unidad);
        sw.Stop();

        // Rendimiento en ejecución continua: debe ejecutarse en menos de 5 ms
        Assert.True(sw.ElapsedMilliseconds < 5, $"La validación tardó {sw.ElapsedMilliseconds} ms (debe ser < 5 ms)");

        // Debe detectar que TUG no puede tener In > 20A (770.6)
        Assert.Contains(diags, d => d.Codigo == "770.6/proteccion_max");

        // Debe detectar que In = 25A supera Iz = 21A (770.15.3)
        var coordDiag = diags.FirstOrDefault(d => d.Codigo == "770.15.3/in_mayor_iz");
        Assert.NotNull(coordDiag);
        Assert.NotNull(coordDiag.Correcciones);
        Assert.True(coordDiag.Correcciones.Count > 0);

        // Debe ofrecer bajar la térmica a <= 20A o subir la sección a >= 4mm2
        Assert.Contains(coordDiag.Correcciones, c => c.Accion == "cambiar_proteccion" && c.InA <= 20.0);
        Assert.Contains(coordDiag.Correcciones, c => c.Accion == "cambiar_seccion" && c.SeccionMm2 >= 4.0);
    }

    [Fact]
    public void Test_PanelExtension_ValidarNormativaAea_DirectamenteSobreModelo()
    {
        var panel = new Models.Panel
        {
            Name = "TSG-DEPTO1",
            SuperficieCubiertaM2 = 70.0,
            MainBreakerAmps = 25.0
        };

        // Circuito IUG correcto
        panel.Circuits.Add(new Models.Circuit
        {
            Name = "C1",
            Type = "IUG",
            BreakerAmps = 10,
            WireSectionMm2 = 1.5,
            ArtefactCount = 8
        });

        // Circuito TUG con cable insuficiente (1.5mm2 en vez de 2.5mm2)
        panel.Circuits.Add(new Models.Circuit
        {
            Name = "C2",
            Type = "TUG",
            BreakerAmps = 16,
            WireSectionMm2 = 1.5,
            ArtefactCount = 10
        });

        var diags = panel.ValidarNormativaAea();
        Assert.NotEmpty(diags);

        // Debe detectar sección mínima de TUG (770.11 exige 2.5mm2)
        var secDiag = diags.FirstOrDefault(d => d.Codigo == "770.11/seccion_min");
        Assert.NotNull(secDiag);
        Assert.Equal("C2", secDiag.Elemento);
        Assert.Contains(secDiag.Correcciones!, c => c.Accion == "cambiar_seccion" && c.SeccionMm2 == 2.5);

        // APLICAR CORRECCIÓN EN VIVO
        var corr = secDiag.Correcciones!.First(c => c.Accion == "cambiar_seccion");
        bool aplicada = panel.AplicarCorreccion(secDiag, corr);
        Assert.True(aplicada);

        // Comprobar que el circuito C2 ahora tiene sección 2.5 mm²
        var c2 = panel.Circuits.First(c => c.Name == "C2");
        Assert.Equal(2.5, c2.WireSectionMm2);

        // Revalidar: ya no debe existir error de sección mínima
        var diagsPost = panel.ValidarNormativaAea();
        Assert.DoesNotContain(diagsPost, d => d.Codigo == "770.11/seccion_min");
    }

    [Fact]
    public void Test_CalcularGradoComercial_AEA771()
    {
        // 50 m2 comercial -> Grado Medio (hasta 75 m2, máx 7.8 kVA)
        var res = AeaBridge.CalcularGradoComercial(50.0);
        Assert.Equal("medio", res.Grado);
        Assert.Equal(50.0, res.SuperficieLimiteM2);
        Assert.Equal("771.8.IV", res.Referencia.Tabla);
    }

    [Fact]
    public void Test_ValidarPanelComercial_ConCircuitoEspecialIUE()
    {
        var panel = new Models.Panel
        {
            Name = "TAB-LOCAL1",
            TipoTablero = "Tablero Local Comercial",
            SuperficieCubiertaM2 = 60.0,
            MainBreakerAmps = 40.0
        };

        panel.Circuits.Add(new Models.Circuit
        {
            Name = "C1",
            Type = "IUE",
            BreakerAmps = 25,
            WireSectionMm2 = 4.0,
            ArtefactCount = 10
        });

        // Con solo 1 circuito en 60 m² (grado medio), Tabla 771.8.V exige 3 circuitos (1 IUG + 1 TUG + 1 IUE)
        var diagsIncompleto = panel.ValidarNormativaAea();
        Assert.Contains(diagsIncompleto, d => d.Codigo == "771.8.5/circuitos_minimos");

        // Completando la variante a) de Tabla 771.8.V (1 IUG + 1 TUG + 1 IUE) -> 0 errores
        panel.Circuits.Add(new Models.Circuit { Name = "C2", Type = "IUG", BreakerAmps = 10, WireSectionMm2 = 1.5, ArtefactCount = 8 });
        panel.Circuits.Add(new Models.Circuit { Name = "C3", Type = "TUG", BreakerAmps = 16, WireSectionMm2 = 2.5, ArtefactCount = 10 });

        var diags = panel.ValidarNormativaAea();
        // IUE con 10 bocas y 25A en 4.0mm2 + 1 IUG + 1 TUG cumple plenamente AEA 771
        Assert.DoesNotContain(diags, d => d.Severidad == "error");
    }

    [Fact]
    public void Test_AplicarCorreccion_AgregarCircuitos_NivelTablero()
    {
        // Panel con 0 circuitos: exige mínimo 2 circuitos (1 IUG + 1 TUG)
        var panel = new Models.Panel
        {
            Name = "TD1",
            SuperficieCubiertaM2 = 50.0,
            MainBreakerAmps = 25.0
        };

        var diags = panel.ValidarNormativaAea();
        var diagMin = diags.FirstOrDefault(d => d.Codigo == "770.7.4/circuitos_minimos");
        Assert.NotNull(diagMin);
        Assert.NotNull(diagMin.Correcciones);
        Assert.NotEmpty(diagMin.Correcciones);

        // Aplicar corrección: auto-agrega el primer circuito faltante
        var corr = diagMin.Correcciones.First();
        bool ok = panel.AplicarCorreccion(diagMin, corr);
        Assert.True(ok);
        Assert.NotEmpty(panel.Circuits);
        Assert.Contains(panel.Circuits, c => c.Type == "IUG" || c.Type == "TUG");
    }

    [Fact]
    public void Test_PuntosMinimos_ConAcentos_EnCsharp()
    {
        // Baño en mayúsculas con virgulilla
        var ambBano = new AeaAmbienteDto
        {
            Id = "B1",
            Nombre = "Baño Suite",
            Tipo = "BAÑO",
            SuperficieM2 = 4.0
        };
        var pBano = AeaBridge.CalcularPuntosMinimos(ambBano, "medio");
        Assert.Equal(1, pBano.IugMinimo);
        Assert.Equal(1, pBano.TugMinimo);

        // Balcón en mayúsculas con tilde
        var ambBalcon = new AeaAmbienteDto
        {
            Id = "BAL1",
            Nombre = "Balcón Principal",
            Tipo = "BALCÓN",
            SuperficieM2 = 8.0,
            LongitudM = 4.0
        };
        var pBalcon = AeaBridge.CalcularPuntosMinimos(ambBalcon, "medio");
        Assert.Equal(1, pBalcon.IugMinimo);
        Assert.Equal(0, pBalcon.TugMinimo);
    }

    [Fact]
    public void Test_CaidaTension_Y_QuickFix_EnCsharp()
    {
        // 1. Test llamada directa FFI
        double dv = AeaBridge.CalcularCaidaTension(longitudM: 40.0, corrienteA: 10.0, seccionMm2: 1.5, cablesCargados: 2, cosPhi: 0.9);
        // ΔV = 2 * 0.0212 * 40 * 10 * 0.9 / 1.5 = 10.176 V -> 10.176 / 220 = 4.625%
        Assert.True(dv > 4.6 && dv < 4.7);

        // 2. Test integración con Panel y Quick-Fix
        var panel = new Models.Panel
        {
            Name = "TG",
            SuperficieCubiertaM2 = 50,
            PhaseCount = 1,
            PowerFactor = 0.9
        };

        var circLargo = new Models.Circuit
        {
            Name = "C1",
            Type = "IUG",
            BreakerAmps = 10,
            WireSectionMm2 = 1.5,
            ArtefactCount = 8,
            LengthMeters = 40.0, // Longitud de 40 metros
            LoadVA = 1980 // 10 A @ 220V con cos(phi)=0.9 (1980 / (220*0.9) = 10.0 A)
        };
        panel.Circuits.Add(circLargo);

        var diags = panel.ValidarNormativaAea();
        var diagDv = diags.FirstOrDefault(d => d.Codigo == "770.12/caida_tension");
        Assert.NotNull(diagDv);
        Assert.Contains("supera el límite reglamentario", diagDv.Mensaje);

        // Debe sugerir cambiar sección a 2.5 mm²
        var corr = diagDv.Correcciones?.FirstOrDefault(c => c.Accion == "cambiar_seccion");
        Assert.NotNull(corr);
        Assert.Equal(2.5, corr.SeccionMm2);

        // Aplicar Quick-Fix
        bool ok = panel.AplicarCorreccion(diagDv, corr);
        Assert.True(ok);
        Assert.Equal(2.5, circLargo.WireSectionMm2);

        // Revalidar: caída de tensión resuelta
        var diagsPost = panel.ValidarNormativaAea();
        Assert.DoesNotContain(diagsPost, d => d.Codigo == "770.12/caida_tension");
    }

    [Fact]
    public void Test_Cortocircuito_Termico_Y_PoderDeCorte_EnCsharp()
    {
        // 1. Verificación térmica analítica de cortocircuito en conductor:
        // Icc = 3 kA, t = 0.02 s, cable 1.5 mm2, PVC
        var res1 = AeaBridge.VerificarCortocircuito(iccKa: 3.0, tiempoS: 0.02, seccionMm2: 1.5, aislacionPvc: true);
        Assert.False(res1.Cumple);
        Assert.Equal(3.69, res1.SeccionMinimaMm2);

        // Con 4.0 mm2 -> Cumple
        var res2 = AeaBridge.VerificarCortocircuito(iccKa: 3.0, tiempoS: 0.02, seccionMm2: 4.0, aislacionPvc: true);
        Assert.True(res2.Cumple);

        // 2. Verificación de Icn en circuito vs Icc del tablero:
        var panel = new Models.Panel
        {
            Name = "TG-EDIFICIO",
            SuperficieCubiertaM2 = 80,
            PhaseCount = 3
        };
        panel.Circuits.Add(new Models.Circuit
        {
            Name = "C1",
            Type = "IUG",
            BreakerAmps = 10,
            WireSectionMm2 = 1.5,
            ArtefactCount = 8
        });

        var unidadDto = panel.ToAeaUnidad();
        unidadDto.IccOrigenKa = 4.5; // Tablero con Icc presunta de 4.5 kA
        unidadDto.Circuitos[0].IcnKa = 3.0; // Interruptor con Icn de 3.0 kA

        var diags = AeaBridge.ValidarUnidad(unidadDto);
        Assert.Contains(diags, d => d.Codigo == "770.16/poder_de_corte");
    }

    [Fact]
    public void Test_Dpms_Caneria_Pe_Y_PuntosComerciales()
    {
        // 1. Sección PE según Tabla 770.14.I
        Assert.Equal(2.5, AeaBridge.CalcularSeccionPe(1.5));
        Assert.Equal(16.0, AeaBridge.CalcularSeccionPe(25.0));

        // 2. Dimensionamiento de cañería al 35% según Tabla 770.10.VIII
        var cano = AeaBridge.DimensionarCaneria(seccionMm2: 6.0, conductoresTotales: 3, familiaODesignacion: "aislante_rigido");
        Assert.True(cano.Cumple);
        Assert.Equal("RSP 22", cano.Designacion);
        Assert.True(cano.OcupacionPct <= 35.0);

        // 3. Puntos mínimos comerciales bajo AEA 771 (Tabla 771.8.VI)
        var ambComercial = new AeaAmbienteDto
        {
            Id = "LOC1",
            Nombre = "Salón Comercial",
            Tipo = "salon_general",
            SuperficieM2 = 45.0
        };
        var ptsCom = AeaBridge.CalcularPuntosMinimos(ambComercial, "medio", "AEA-771");
        Assert.Equal(5, ptsCom.IugMinimo);
        Assert.Equal(5, ptsCom.TugMinimo);

        // 4. Cálculo de DPMS bajo AEA 770 (Tablas 770.8.I y 770.8.II)
        var unidad = new AeaUnidadDto
        {
            Id = "VIV1",
            SuperficieCubiertaM2 = 80.0,
            Circuitos = new()
            {
                new AeaCircuitoDto { Id = "C1", Tipo = "IUG", SeccionMm2 = 1.5, InA = 10, Bocas = 9 },
                new AeaCircuitoDto { Id = "C2", Tipo = "TUG", SeccionMm2 = 2.5, InA = 16, Bocas = 10 },
                new AeaCircuitoDto { Id = "C3", Tipo = "TUE", SeccionMm2 = 4.0, InA = 20, Bocas = 2 }
            }
        };
        var dpms = AeaBridge.CalcularDpms(unidad);
        Assert.Equal(0.8, dpms.CoeficienteSimultaneidad);
        Assert.Equal(4688.0, dpms.DpmsVa);
        Assert.Equal(25.0, dpms.ProteccionCabeceraSugeridaA);
    }

    [Fact]
    public void Test_GuiaAea770_ProyectoModeloReferencia()
    {
        // Caso de Estudio Oficial: Guía AEA 770 Edición 2018 (páginas 27-37, PDF 31-41)
        // Vivienda Unifamiliar Grado Medio (94 m² cubiertos)
        var panel = new Models.Panel
        {
            Name = "TS-VIVIENDA",
            SuperficieCubiertaM2 = 94.0,
            PhaseCount = 1,
            MainBreakerAmps = 25.0 // ITM Seccional 25A (Guía pág. 34)
        };

        // Circuito 1: IUG con 15 bocas, cable 1.5 mm², ITM 10A (Guía pág. 31 y 33)
        panel.Circuits.Add(new Models.Circuit
        {
            Name = "C1",
            Type = "IUG",
            BreakerAmps = 10.0,
            WireSectionMm2 = 1.5,
            ArtefactCount = 15,
            LengthMeters = 12.0
        });

        // Circuito 2: TUG con 9 bocas, cable 2.5 mm², ITM 16A (Guía pág. 31 y 33)
        panel.Circuits.Add(new Models.Circuit
        {
            Name = "C2",
            Type = "TUG",
            BreakerAmps = 16.0,
            WireSectionMm2 = 2.5,
            ArtefactCount = 9,
            LengthMeters = 15.0
        });

        // Circuito 3: TUG con 6 bocas, cable 2.5 mm², ITM 16A (Guía pág. 31 y 33)
        panel.Circuits.Add(new Models.Circuit
        {
            Name = "C3",
            Type = "TUG",
            BreakerAmps = 16.0,
            WireSectionMm2 = 2.5,
            ArtefactCount = 6,
            LengthMeters = 18.0
        });

        // 1. Verificación de Grado de Electrificación
        var grado = AeaBridge.CalcularGrado(94.0, 0.0);
        Assert.Equal("medio", grado.Grado);

        // 2. Cálculo de DPMS según Tabla 770.8.I / Guía pág. 31:
        // C1 (IUG): 15 bocas * 60 VA * 2/3 = 600 VA
        // C2 (TUG): 2200 VA
        // C3 (TUG): 2200 VA
        // Potencia Bruta = 600 + 2200 + 2200 = 5000 VA
        var unidadDto = panel.ToAeaUnidad();
        var dpms = AeaBridge.CalcularDpms(unidadDto);
        Assert.Equal(5000.0, dpms.PotenciaSinSimultaneidadVa);
        Assert.Equal(0.8, dpms.CoeficienteSimultaneidad);
        Assert.Equal(4000.0, dpms.DpmsVa);

        // 3. Verificación de la sección de PE según Tabla 770.14.I (Guía pág. 34)
        Assert.Equal(2.5, AeaBridge.CalcularSeccionPe(1.5)); // C1: Fase 1.5 -> PE 2.5 mm²
        Assert.Equal(2.5, AeaBridge.CalcularSeccionPe(2.5)); // C2, C3: Fase 2.5 -> PE 2.5 mm²
        Assert.Equal(4.0, AeaBridge.CalcularSeccionPe(4.0)); // Seccional: Fase 4.0 -> PE 4.0 mm²

        // 4. Validación normativa completa del panel: 0 violaciones
        var diags = panel.ValidarNormativaAea();
        Assert.Empty(diags);
    }
}
