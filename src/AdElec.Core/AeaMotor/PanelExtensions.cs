using AdElec.Core.AeaMotor.Dtos;
using AdElec.Core.Models;
using System.Text.RegularExpressions;

namespace AdElec.Core.AeaMotor;

/// <summary>
/// Extensiones para convertir modelos de AD-ELEC al formato que espera AEA-MOTOR.
/// </summary>
public static class PanelExtensions
{
    /// <summary>
    /// Convierte un <see cref="Panel"/> y su colección de circuitos a un <see cref="ProyectoDto"/>
    /// listo para enviar a POST /calcular de AEA-MOTOR.
    /// </summary>
    /// <param name="panel">Tablero de origen.</param>
    /// <param name="superficieCubiertaM2">
    /// Superficie cubierta total de la vivienda en m². Requerida por AEA 90364 para determinar
    /// el grado de electrificación. Si el Panel no la almacena aún, pasarla como parámetro.
    /// </param>
    /// <param name="ambientes">
    /// Lista de ambientes (locales) de la vivienda. Si se omite, el motor calcula solo
    /// la parte eléctrica sin validar puntos mínimos por ambiente.
    /// </param>
    public static ProyectoDto ToProyectoDto(
        this Panel panel,
        double superficieCubiertaM2,
        IEnumerable<AmbienteDto>? ambientes = null)
    {
        return new ProyectoDto
        {
            Nombre = panel.Name,
            SuperficieCubiertaM2 = superficieCubiertaM2,
            Ambientes = ambientes?.ToList() ?? [],
            Circuitos = panel.Circuits.Select(c => c.ToCircuitoDto()).ToList(),
            Montante = new MontanteDto
            {
                Trifasico = panel.PhaseCount == 3,
                Cosfi = 0.9,
            },
            DatosRed = new DatosRedDto
            {
                TensionV = 220,
                Trifasico = panel.PhaseCount == 3,
            },
        };
    }

    /// <summary>
    /// Convierte un <see cref="Circuit"/> de AD-ELEC a un <see cref="CircuitoDto"/> de AEA-MOTOR.
    /// </summary>
    public static CircuitoDto ToCircuitoDto(this Circuit circuit)
    {
        return new CircuitoDto
        {
            Id = string.IsNullOrWhiteSpace(circuit.Name)
                ? Guid.NewGuid().ToString("N")[..8]
                : circuit.Name.ToLowerInvariant().Replace(" ", "_"),
            Nombre = circuit.Name,
            Tipo = NormalizarTipo(circuit.Type),
            Bocas = 1, // valor mínimo; sin info de bocas en el modelo actual de Circuit
            SeccionMm2 = circuit.WireSectionMm2 > 0 ? circuit.WireSectionMm2 : null,
        };
    }

    /// <summary>
    /// Aplica los resultados calculados por AEA-MOTOR de vuelta sobre los circuitos del Panel,
    /// actualizando sección de cable, carga y corriente.
    /// </summary>
    public static void AplicarResultados(this Panel panel, ResultadoProyectoDto resultado)
    {
        foreach (var rc in resultado.Circuitos)
        {
            // Matchear por nombre (circuito_id generado desde circuit.Name)
            var circuit = panel.Circuits.FirstOrDefault(c =>
                string.Equals(c.Name, rc.Nombre, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Name.ToLowerInvariant().Replace(" ", "_"), rc.CircuitoId, StringComparison.OrdinalIgnoreCase));

            if (circuit is null) continue;

            circuit.WireSectionMm2 = rc.SeccionAdoptadaMm2 > 0 ? rc.SeccionAdoptadaMm2 : rc.SeccionFaseMm2;
            circuit.LoadVA = rc.DemandaVa;
            circuit.BreakerAmps = rc.ProteccionMaxA ?? rc.CorrienteCalculoA;
        }
    }

    /// <summary>
    /// Convierte un <see cref="Ambiente"/> del modelo de AD-ELEC a un <see cref="AmbienteDto"/>
    /// para enviar a AEA-MOTOR.
    /// </summary>
    public static AmbienteDto ToAmbienteDto(this Ambiente ambiente)
    {
        return new AmbienteDto
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Tipo = ambiente.TipoApi,
            Nombre = $"{ambiente.TipoDisplay} — {ambiente.Planta}",
            SuperficieM2 = ambiente.AreaM2 > 0 ? ambiente.AreaM2 : null,
        };
    }

    // AEA-MOTOR usa minúsculas: iug, tug, tue, etc.
    private static string NormalizarTipo(string tipo) => tipo.ToUpperInvariant() switch
    {
        "AP" or "APM" => "APM",
        _ => tipo.ToUpperInvariant(),
    };
}
