using System;
using System.Collections.Generic;
using System.Linq;
using AdElec.Core.Models;

namespace AdElec.Core.Aea;

/// <summary>
/// Métodos de extensión para integrar los modelos CAD (Panel, Circuit, Ambiente) con el motor AEA.
/// </summary>
public static class PanelAeaExtensions
{
    public static AeaUnidadDto ToAeaUnidad(this Panel panel, IEnumerable<Ambiente>? ambientes = null)
    {
        string? norma = null;
        var tipo = (panel.TipoTablero ?? "").ToLowerInvariant();
        if (tipo.Contains("comercial") || tipo.Contains("oficina") || tipo.Contains("local"))
            norma = "AEA-771";
        else if (tipo.Contains("comun") || tipo.Contains("servicios generales") || tipo.Contains("partes comunes"))
            norma = "AEA-772";

        var dto = new AeaUnidadDto
        {
            Id = panel.Name,
            SuperficieCubiertaM2 = Math.Max(0.0, panel.SuperficieCubiertaM2),
            InCabeceraA = panel.MainBreakerAmps > 0 ? panel.MainBreakerAmps : null,
            Norma = norma,
            Circuitos = panel.Circuits.Select(c =>
            {
                double? ib = null;
                if (c.LoadVA > 0 && panel.PowerFactor > 0)
                {
                    double v = panel.PhaseCount >= 3 ? (Math.Sqrt(3) * 380 * panel.PowerFactor) : (220 * panel.PowerFactor);
                    if (v > 0) ib = Math.Round(c.LoadVA / v, 2);
                }

                return new AeaCircuitoDto
                {
                    Id = c.Name,
                    Tipo = string.IsNullOrWhiteSpace(c.Type) ? "TUG" : c.Type.Trim().ToUpperInvariant(),
                    SeccionMm2 = Math.Max(0.5, c.WireSectionMm2),
                    InA = Math.Max(0.0, c.BreakerAmps),
                    IbA = ib,
                    Bocas = Math.Max(0, c.ArtefactCount),
                    CablesCargados = panel.PhaseCount >= 3 ? 3 : 2,
                    CircuitosEnCanalizacion = 1
                };
            }).ToList()
        };

        if (ambientes != null)
        {
            dto.Ambientes = ambientes.Select(a => new AeaAmbienteDto
            {
                Id = string.IsNullOrEmpty(a.Handle) ? Guid.NewGuid().ToString("N")[..8] : a.Handle,
                Nombre = !string.IsNullOrEmpty(a.TipoDisplay) ? a.TipoDisplay : (a.TipoApi ?? "Ambiente"),
                Tipo = string.IsNullOrEmpty(a.TipoApi) ? "sala_estar" : a.TipoApi,
                SuperficieM2 = Math.Max(0.0, a.AreaM2)
            }).ToList();
        }

        return dto;
    }

    /// <summary>
    /// Valida el panel en vivo contra el núcleo normativo de alto rendimiento AEA (Rust).
    /// </summary>
    public static List<AeaDiagnostic> ValidarNormativaAea(this Panel panel, IEnumerable<Ambiente>? ambientes = null)
    {
        var unidad = panel.ToAeaUnidad(ambientes);
        return AeaBridge.ValidarUnidad(unidad);
    }

    /// <summary>
    /// Aplica una corrección normativa sugerida directamente sobre el circuito o el tablero del Panel.
    /// </summary>
    public static bool AplicarCorreccion(this Panel panel, AeaDiagnostic diagnostico, AeaCorreccionDto correccion)
    {
        if (correccion == null) return false;
        var circuit = panel.Circuits.FirstOrDefault(c => string.Equals(c.Name, diagnostico.Elemento, StringComparison.OrdinalIgnoreCase));

        if (circuit != null)
        {
            switch (correccion.Accion?.ToLowerInvariant())
            {
                case "cambiar_proteccion":
                    if (correccion.InA.HasValue)
                    {
                        circuit.BreakerAmps = correccion.InA.Value;
                        return true;
                    }
                    break;

                case "cambiar_seccion":
                    if (correccion.SeccionMm2.HasValue)
                    {
                        circuit.WireSectionMm2 = correccion.SeccionMm2.Value;
                        return true;
                    }
                    break;
            }
        }
        else if (string.Equals(panel.Name, diagnostico.Elemento, StringComparison.OrdinalIgnoreCase))
        {
            // Corrección a nivel Tablero
            switch (correccion.Accion?.ToLowerInvariant())
            {
                case "cambiar_norma":
                    if (!string.IsNullOrEmpty(correccion.Norma))
                    {
                        panel.TipoTablero = $"Tablero General / Terciario ({correccion.Norma})";
                        return true;
                    }
                    break;

                case "agregar_circuitos":
                    int cant = correccion.Cantidad ?? 1;
                    string tipo = string.IsNullOrWhiteSpace(correccion.Tipo) || correccion.Tipo == "libre_eleccion"
                        ? "TUG"
                        : correccion.Tipo.ToUpperInvariant();
                    double seccion = tipo == "IUG" ? 1.5 : (tipo == "TUG" ? 2.5 : 4.0);
                    double inA = tipo == "IUG" ? 10.0 : (tipo == "TUG" ? 16.0 : 20.0);

                    for (int i = 0; i < cant; i++)
                    {
                        int num = 1;
                        while (panel.Circuits.Any(c => string.Equals(c.Name, $"C{num}", StringComparison.OrdinalIgnoreCase)))
                            num++;

                        panel.Circuits.Add(new Circuit
                        {
                            Name = $"C{num}",
                            Type = tipo,
                            BreakerAmps = inA,
                            WireSectionMm2 = seccion,
                            ArtefactCount = 1
                        });
                    }
                    return true;
            }
        }

        return false;
    }
}
