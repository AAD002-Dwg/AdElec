namespace AdElec.Core.Models;

/// <summary>
/// Configuración para el motor de detección automática de ambientes.
/// Define las capas a escanear, tolerancias geométricas y parámetros de escala.
/// </summary>
public class RoomDetectionConfig
{
    /// <summary>
    /// Nombres de capas que contienen muros/paredes (case-insensitive).
    /// Se buscan polilíneas y segmentos en estas capas para reconstruir perímetros.
    /// </summary>
    public List<string> CapasMuros { get; set; } =
        ["MUROS", "MURO", "PARED", "PAREDES", "WALL", "WALLS", "A-WALL", "A-WALL-FULL", "ADELEC_MUROS"];

    /// <summary>
    /// Nombres de capas que contienen polilíneas de recintos/ambientes cerrados directamente.
    /// </summary>
    public List<string> CapasAmbientes { get; set; } =
        ["AMBIENTES", "AMBIENTE", "RECINTOS", "RECINTO", "LOCALES", "LOCAL", "AREAS", "AREA", "ADELEC_AMBIENTES", "0", "ARQUITECTURA"];

    /// <summary>
    /// Nombres de capas que contienen puertas/aberturas.
    /// Se buscan bloques INSERT y arcos para detectar conexiones entre ambientes.
    /// </summary>
    public List<string> CapasPuertas { get; set; } =
        ["PUERTAS", "PUERTA", "ABERTURAS", "ABERTURA", "CARPINTERIA", "CARPINTERIAS", "A-DOOR", "DOOR", "DOORS", "ADELEC_PUERTAS", "ADELEC_VENTANAS"];

    /// <summary>
    /// Nombres de capas que contienen etiquetas de texto de ambiente.
    /// Se buscan TEXT/MTEXT para clasificación de Nivel 1 (alta confianza).
    /// </summary>
    public List<string> CapasTexto { get; set; } =
        ["TEXTO", "TEXTOS", "TXT", "NOTAS", "A-TEXT", "TEXT", "ANNOTATIONS"];

    /// <summary>
    /// Tolerancia de snap en unidades del dibujo.
    /// Endpoints dentro de esta distancia se consideran el mismo punto.
    /// Es el parámetro más crítico del sistema: muy bajo → pierde ambientes,
    /// muy alto → fusiona ambientes adyacentes.
    /// Default: 0.005 (asumiendo metros como unidad de dibujo = 5mm).
    /// </summary>
    public double ToleranciaSnap { get; set; } = 0.005;

    /// <summary>
    /// Área mínima en m² para considerar un polígono como ambiente válido.
    /// Polígonos con área menor se descartan (probablemente artefactos de dibujo).
    /// </summary>
    public double AreaMinimaM2 { get; set; } = 1.0;

    /// <summary>
    /// Área máxima en m² para considerar un polígono como ambiente individual.
    /// Polígonos mayores probablemente son el perímetro exterior del edificio.
    /// </summary>
    public double AreaMaximaM2 { get; set; } = 500.0;

    /// <summary>
    /// Rango de radio (en unidades del dibujo) para detectar arcos de puerta.
    /// Los arcos con radio fuera de este rango no se consideran puertas.
    /// Default: 0.5-1.5 (asumiendo metros → 0.5m a 1.5m).
    /// </summary>
    public double RadioPuertaMin { get; set; } = 0.5;
    public double RadioPuertaMax { get; set; } = 1.5;

    /// <summary>
    /// Nombres parciales de bloques que indican puertas.
    /// Se hace matching case-insensitive con Contains().
    /// </summary>
    public List<string> NombresBloquesPuerta { get; set; } =
        ["PUERTA", "DOOR", "ABERTURA", "VENTANA"];

    /// <summary>
    /// Planta por defecto asignada a los ambientes detectados.
    /// El usuario puede cambiarla en el panel de verificación.
    /// </summary>
    public string PlantaDefault { get; set; } = "PB";
}
