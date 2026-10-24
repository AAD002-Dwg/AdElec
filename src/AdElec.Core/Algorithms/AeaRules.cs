using System;
using System.Collections.Generic;
using System.Linq;
using AdElec.Core.Models;

namespace AdElec.Core.Algorithms
{
    public record AeaValidationResult(bool IsValid, List<string> Warnings, List<string> Errors);

    /// <summary>
    /// Reglas normativas de la Reglamentación AEA 90364-771
    /// para instalaciones eléctricas en inmuebles (Viviendas y Oficinas).
    /// </summary>
    public static class AeaRules
    {
        public static readonly Dictionary<string, (string Descripcion, int MaxBocas, double MinWire, double DefaultBreaker, double MinLoadVA)> CircuitTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["IUG"] = ("ILUMINACION", 15, 1.5, 10.0, 150.0),
                ["TUG"] = ("TOMAS", 15, 2.5, 16.0, 2200.0),
                ["IUE"] = ("ILUMINACION ESPECIAL", 12, 2.5, 16.0, 500.0),
                ["TUE"] = ("TOMAS ESPECIALES", 12, 2.5, 20.0, 3300.0),
                ["ACU"] = ("AIRE ACONDICIONADO", 1, 2.5, 16.0, 1500.0),
                ["AP"]  = ("ALIMENTACION PEQUEÑOS MOTORES", 1, 2.5, 16.0, 1500.0),
                ["RESERVA"] = ("RESERVA EQUIPADA", 0, 2.5, 10.0, 0.0),
            };

        /// <summary>
        /// Valida un circuito individual contra los límites de AEA 90364-771.
        /// </summary>
        public static AeaValidationResult ValidateCircuit(Circuit c)
        {
            var warnings = new List<string>();
            var errors = new List<string>();

            string type = c.Type.ToUpperInvariant();
            if (!CircuitTypes.TryGetValue(type, out var def))
            {
                warnings.Add($"Tipo de circuito '{c.Type}' no estándar según AEA 771.");
                return new AeaValidationResult(true, warnings, errors);
            }

            // Validación de bocas
            if (def.MaxBocas > 0 && c.ArtefactCount > def.MaxBocas)
            {
                errors.Add($"Circuito {c.Name} ({type}): supera el máximo reglamentario de {def.MaxBocas} bocas ({c.ArtefactCount} detectadas).");
            }

            // Sección mínima de conductor de fase/neutro
            if (c.WireSectionMm2 < def.MinWire)
            {
                errors.Add($"Circuito {c.Name} ({type}): sección de {c.WireSectionMm2} mm² inferior a la mínima reglamentaria de {def.MinWire} mm².");
            }

            // Coordinación protección / sección (Ib <= In <= Iz)
            // Capacidad admisible estimada en cañería embutida IRAM 2183 / 62267 (2 conductores cargados + PE)
            double iz = c.WireSectionMm2 switch
            {
                >= 10.0 => 50.0,
                >= 6.0  => 36.0,
                >= 4.0  => 28.0,
                >= 2.5  => 21.0,
                >= 1.5  => 15.0,
                _       => 10.0
            };

            if (c.BreakerAmps > iz)
            {
                errors.Add($"Circuito {c.Name}: ITM {c.BreakerAmps}A supera la corriente admisible del conductor {c.WireSectionMm2} mm² (Iz ≈ {iz:F0}A). Peligro de sobrecarga.");
            }

            // Cargas mínimas de cálculo
            if (c.ArtefactCount > 0 && c.LoadVA < def.MinLoadVA)
            {
                warnings.Add($"Circuito {c.Name} ({type}): potencia {c.LoadVA:F0} VA inferior al mínimo simultáneo reglamentario de {def.MinLoadVA:F0} VA.");
            }

            return new AeaValidationResult(errors.Count == 0, warnings, errors);
        }

        /// <summary>
        /// Valida el tablero completo (cantidad de circuitos por diferencial, reserva obligatoria).
        /// </summary>
        public static AeaValidationResult ValidatePanel(Panel panel)
        {
            var warnings = new List<string>();
            var errors = new List<string>();

            if (panel.Circuits.Count == 0)
            {
                errors.Add($"El tablero {panel.Name} no tiene circuitos asignados.");
                return new AeaValidationResult(false, warnings, errors);
            }

            // Regla AEA 771.8: Reserva obligatoria en tableros
            bool hasReserva = panel.Circuits.Any(c => c.Type.Equals("RESERVA", StringComparison.OrdinalIgnoreCase) ||
                                                      c.Name.IndexOf("RESERVA", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!hasReserva)
            {
                warnings.Add($"Tablero {panel.Name}: AEA 771.8 exige prever espacio o circuito de reserva (mínimo 20% o 1 circuito).");
            }

            // Regla AEA 771.18: Máximo 3 circuitos por cada interruptor diferencial general o seccional recomendado
            if (panel.Circuits.Count > 3)
            {
                warnings.Add($"Tablero {panel.Name}: Tiene {panel.Circuits.Count} circuitos. AEA 771 recomienda no superar 3 circuitos por cada ID para evitar desconexiones por corrientes de fuga acumuladas.");
            }

            return new AeaValidationResult(errors.Count == 0, warnings, errors);
        }

        /// <summary>
        /// Asegura que el tablero tenga al menos un circuito de RESERVA según AEA 771.8.
        /// Si no existe, lo agrega.
        /// </summary>
        public static void EnsureReserveCircuit(Panel panel)
        {
            bool hasReserva = panel.Circuits.Any(c => c.Type.Equals("RESERVA", StringComparison.OrdinalIgnoreCase) ||
                                                      c.Name.IndexOf("RESERVA", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!hasReserva)
            {
                int nextIndex = panel.Circuits.Count + 1;
                panel.Circuits.Add(new Circuit
                {
                    Name = $"C{nextIndex}",
                    Type = "RESERVA",
                    Description = "RESERVA EQUIPADA",
                    BreakerAmps = 10,
                    WireSectionMm2 = 2.5,
                    LoadVA = 0,
                    ArtefactCount = 0,
                    Destinations = "RESERVA\nEQUIPADA",
                    Curve = "C",
                    Poles = panel.PhaseCount == 3 ? 4 : 2,
                    PhaseString = panel.PhaseCount == 3 ? "RSTN" : "SN"
                });
            }
        }

        /// <summary>
        /// Normaliza los textos para la tabla UNIFV.3 del diagrama unifilar.
        /// </summary>
        public static (string Header, string BreakerStr, string WireStr, string PhaseStr, string DescStr) FormatForUnifilar(Circuit c, int phaseCount)
        {
            string type = c.Type.ToUpperInvariant();
            string desc = c.Description;
            if (string.IsNullOrEmpty(desc))
            {
                desc = type switch
                {
                    "IUG" or "IUE" => "ILUMINACION",
                    "TUG" or "TUE" => "TOMAS",
                    "RESERVA"      => "RESERVA EQUIPADA",
                    _              => type
                };
            }

            // Header: e.g. "I.U.G - C1", "T.U.G - C2", "RESERVA - C4"
            string typeFormatted = type switch
            {
                "IUG" => "I.U.G",
                "TUG" => "T.U.G",
                "IUE" => "I.U.E",
                "TUE" => "T.U.E",
                _     => type
            };
            string header = $"{typeFormatted} - {c.Name}";

            // ITM: e.g. "2x10", "2x16", "4x25"
            int poles = c.Poles > 0 ? c.Poles : (phaseCount == 3 ? 4 : 2);
            string breakerStr = $"{poles}x{c.BreakerAmps:F0}";

            // RAMAL: e.g. "2x1,5", "2x2,5", "4x4"
            string wireStr = $"{poles}x{c.WireSectionMm2:0.#}".Replace('.', ',');

            // FASE: e.g. "SN", "R", "S", "T"
            string phaseStr = !string.IsNullOrEmpty(c.PhaseString) ? c.PhaseString : (phaseCount == 3 ? c.Phase.ToString() : "SN");

            return (header, breakerStr, wireStr, phaseStr, desc);
        }
    }
}
