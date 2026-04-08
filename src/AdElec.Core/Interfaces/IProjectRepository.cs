namespace AdElec.Core.Interfaces;

public interface IProjectRepository
{
    /// <summary>
    /// Recupera el ID de proyecto vinculado a este dibujo.
    /// Retorna 0 si no hay vínculo.
    /// </summary>
    int GetProjectId();

    /// <summary>
    /// Guarda el ID de proyecto en el dibujo actual.
    /// </summary>
    void SaveProjectId(int id);
    string GetSyncMode();
    void SaveSyncMode(string mode);
}
