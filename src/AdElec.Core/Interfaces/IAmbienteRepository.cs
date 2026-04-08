using AdElec.Core.Models;

namespace AdElec.Core.Interfaces;

/// <summary>
/// Lee ambientes/recintos desde los bloques ID_LOCALES del dibujo activo.
/// </summary>
public interface IAmbienteRepository
{
    /// <summary>
    /// Devuelve todos los ambientes que pertenecen a la Unidad Funcional indicada.
    /// Filtra bloques ID_LOCALES por el atributo "01" == <paramref name="uf"/>.
    /// </summary>
    List<Ambiente> GetAmbientesParaUF(string uf);

    /// <summary>
    /// Lista todas las Unidades Funcionales (valores únicos del atributo "01")
    /// encontradas en los bloques ID_LOCALES del dibujo.
    /// </summary>
    List<string> GetUFsDisponibles();

    /// <summary>
    /// Devuelve todos los ambientes registrados en el dibujo activo (todos los bloques ID_LOCALES).
    /// </summary>
    List<Ambiente> GetAllAmbientes();
}
