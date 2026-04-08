namespace AdElec.Core.Interfaces;

public interface IProjectRepository
{
    /// <summary>Recupera el ID de proyecto vinculado a este dibujo. Retorna 0 si no hay vínculo.</summary>
    int GetProjectId();
    void SaveProjectId(int id);

    /// <summary>Nombre del proyecto definido por el usuario (ej: "Edificio Belgrano").</summary>
    string GetProjectName();
    void SaveProjectName(string name);

    string GetSyncMode();
    void SaveSyncMode(string mode);
}
