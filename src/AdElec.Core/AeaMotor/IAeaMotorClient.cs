using AdElec.Core.AeaMotor.Dtos;

namespace AdElec.Core.AeaMotor;

/// <summary>
/// Contrato para comunicarse con el servicio AEA-MOTOR (FastAPI en localhost:8000).
/// </summary>
public interface IAeaMotorClient
{
    /// <summary>
    /// Cálculo normativo completo (secciones, caídas, validaciones AEA 90364).
    /// POST /api/v1/calcular
    /// </summary>
    Task<ResultadoProyectoDto> CalcularAsync(ProyectoDto proyecto, CancellationToken ct = default);

    /// <summary>
    /// Grado de electrificación por superficie (Tabla 771.8.I).
    /// POST /api/v1/grado?superficie_cubierta_m2=...
    /// </summary>
    Task<ResultadoGradoDto> ObtenerGradoAsync(double superficieCubiertaM2, double superficieSemicubiertaM2 = 0, CancellationToken ct = default);

    /// <summary>
    /// Genera propuesta automática de bocas (IUG, TUG, SWITCH) según AEA,
    /// a partir de los polígonos de los recintos.
    /// POST /api/v1/adelec/generate_proposal
    /// </summary>
    Task<ProposalResponse> GenerarPropuestaAsync(ProposalProjectInput input, CancellationToken ct = default);

    /// <summary>
    /// Crea un proyecto en el editor web de AEA-MOTOR para visualización en el canvas adelec.
    /// POST /proyectos
    /// </summary>
    Task<SyncProjectResponse> SincronizarProyectoAsync(SyncProjectRequest request, CancellationToken ct = default);

    /// <summary>Verifica si el servicio está corriendo y accesible.</summary>
    Task<bool> EstaDisponibleAsync(CancellationToken ct = default);
}
