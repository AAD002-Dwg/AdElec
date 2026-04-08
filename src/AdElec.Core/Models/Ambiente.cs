namespace AdElec.Core.Models;

/// <summary>
/// Recinto/ambiente leído desde un bloque ID_LOCALES del dibujo.
/// Se usa para construir el Proyecto que se envía a AEA-MOTOR.
/// </summary>
public class Ambiente
{
    /// <summary>Unidad Funcional — atributo "01" del bloque (ej: "DEPTO-1", "UF2").</summary>
    public string UF { get; set; } = "";

    /// <summary>Planta — atributo "55" del bloque (ej: "PB", "Primer Piso").</summary>
    public string Planta { get; set; } = "PB";

    /// <summary>Nombre visible del tipo de local (ej: "Dormitorio", "Cocina").</summary>
    public string TipoDisplay { get; set; } = "";

    /// <summary>
    /// Valor de enum para AEA-MOTOR. Valores válidos:
    /// sala_estar | escritorio | dormitorio | cocina | baño | pasillo | lavadero | garaje | otro
    /// </summary>
    public string TipoApi { get; set; } = "otro";

    /// <summary>Superficie en m² — calculada desde la polilínea o leída del atributo AREA.</summary>
    public double AreaM2 { get; set; }

    /// <summary>Espesor de muro perimetral en metros (ej: 0.15).</summary>
    public double EspesorMuro { get; set; } = 0.15;

    /// <summary>Vértices de la polilínea que define el perímetro interior del local.</summary>
    public List<Point2D> PolygonPoints { get; set; } = new();

    /// <summary>
    /// Handle del bloque ID_LOCALES en AutoCAD (hex string).
    /// Usado para generar IDs estables de room: "room_{Handle}".
    /// </summary>
    public string Handle { get; set; } = "";
}
