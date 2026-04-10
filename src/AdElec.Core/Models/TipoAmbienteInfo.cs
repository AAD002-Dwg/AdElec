namespace AdElec.Core.Models;

/// <summary>
/// Mapeo entre el nombre visible y el valor de enum que acepta AEA-MOTOR (TipoAmbiente).
/// Centralizado aquí para usarse tanto en la UI como en el comando AutoCAD.
/// </summary>
public sealed record TipoAmbienteInfo(string Nombre, string ApiValue, string RoomTypeName = "OTHER")
{
    /// <summary>
    /// Lista completa de tipos de ambiente.
    /// Tres valores por tipo: Nombre display, ApiValue (aea_motor), RoomTypeName (adelec enum name).
    /// </summary>
    public static readonly IReadOnlyList<TipoAmbienteInfo> Todos =
    [
        new("Estar / Comedor / Living", "sala_estar",  "LIVING_DINING"),
        new("Dormitorio",               "dormitorio",  "BEDROOM"),
        new("Cocina",                   "cocina",      "KITCHEN"),
        new("Baño / Toilette",          "baño",        "BATHROOM"),
        new("Pasillo / Balcón / Hall",  "pasillo",     "HALLWAY"),
        new("Escritorio / Estudio",     "escritorio",  "STUDY"),
        new("Lavadero",                 "lavadero",    "LAUNDRY"),
        new("Cochera / Garaje",         "garaje",      "GARAGE"),
        new("Otro / Depósito",          "otro",        "OTHER"),
    ];

    /// <summary>Busca por ApiValue, devuelve "Otro" como fallback.</summary>
    public static TipoAmbienteInfo DesdaApiValue(string apiValue) =>
        Todos.FirstOrDefault(t => t.ApiValue == apiValue) ?? Todos[^1];

    /// <summary>Busca por nombre display (case-insensitive), devuelve "Otro" como fallback.</summary>
    public static TipoAmbienteInfo DesdeNombre(string nombre) =>
        Todos.FirstOrDefault(t =>
            string.Equals(t.Nombre, nombre, StringComparison.OrdinalIgnoreCase))
        ?? Todos[^1];

    /// <summary>Busca por RoomTypeName (e.g. "BEDROOM"), devuelve "Otro" como fallback.</summary>
    public static TipoAmbienteInfo DesdeRoomTypeName(string roomTypeName) =>
        Todos.FirstOrDefault(t =>
            string.Equals(t.RoomTypeName, roomTypeName, StringComparison.OrdinalIgnoreCase))
        ?? Todos[^1];
}
