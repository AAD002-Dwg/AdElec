namespace AdElec.Core.Models;

/// <summary>
/// Ambiente detectado automáticamente por el motor de detección,
/// antes de la verificación del usuario. Se convierte en <see cref="Ambiente"/>
/// una vez confirmado e insertado como bloque ID_LOCALES.
/// </summary>
public class DetectedRoom
{
    /// <summary>Identificador secuencial asignado durante la detección (AMB-001, AMB-002...).</summary>
    public string Id { get; set; } = "";

    /// <summary>Tipo de ambiente display (ej: "Dormitorio", "Cocina").</summary>
    public string Tipo { get; set; } = "";

    /// <summary>Valor de API para AEA-MOTOR (ej: "dormitorio", "cocina").</summary>
    public string TipoApi { get; set; } = "otro";

    /// <summary>Superficie en m², calculada desde los vértices del polígono.</summary>
    public double AreaM2 { get; set; }

    /// <summary>Perímetro en metros.</summary>
    public double PerimetroM { get; set; }

    /// <summary>Vértices del polígono reconstruido/detectado.</summary>
    public List<Point2D> Vertices { get; set; } = new();

    /// <summary>Centroide geométrico del polígono.</summary>
    public Point2D Centroid { get; set; }

    /// <summary>Nivel de confianza de la clasificación: "alta", "media", "baja".</summary>
    public string Confianza { get; set; } = "baja";

    /// <summary>
    /// Si true, el usuario debe revisar este ambiente.
    /// Se activa cuando: la clasificación es heurística (Nivel 3),
    /// el polígono contiene segmentos con bulge, o el agrupamiento es incierto.
    /// </summary>
    public bool RequiereVerificacion { get; set; } = true;

    /// <summary>Unidad funcional asignada (ej: "UF-01", "SIN_CLASIFICAR").</summary>
    public string UnidadFuncional { get; set; } = "SIN_CLASIFICAR";

    /// <summary>Tipo de unidad funcional sugerido (ej: "Departamento", "Servicios Generales").</summary>
    public string TipoUnidadFuncional { get; set; } = "";

    /// <summary>Planta donde se encuentra el ambiente (ej: "PB", "Primer Piso").</summary>
    public string Planta { get; set; } = "PB";

    /// <summary>
    /// Handle hex-string del ObjectId de la polilínea/fuente original en AutoCAD.
    /// Se usa para resaltado bidireccional y para el vínculo XData al insertar el bloque.
    /// </summary>
    public string SourceHandle { get; set; } = "";

    /// <summary>
    /// True si algún segmento del polígono reconstruido tenía bulge != 0.
    /// Indica que el área calculada es aproximada (se trató el arco como recto en V1).
    /// </summary>
    public bool ContieneBulge { get; set; }

    /// <summary>
    /// Handle hex-string del bloque ID_LOCALES si este ambiente ya existe previamente en el dibujo.
    /// Si no está vacío, no se inserta un bloque duplicado sino que se actualizan sus atributos.
    /// </summary>
    public string ExistingBlockHandle { get; set; } = "";

    /// <summary>Indica si el ambiente ya existía previamente en el dibujo.</summary>
    public bool IsExisting => !string.IsNullOrEmpty(ExistingBlockHandle);

    // ── Mapeo de Artefactos Eléctricos contenidos en el recinto ──────

    /// <summary>Cantidad de bocas de iluminación (IUG / IUE) dentro del recinto.</summary>
    public int CantidadLuminarias { get; set; }

    /// <summary>Cantidad de tomacorrientes (TUG / TUE) dentro del recinto.</summary>
    public int CantidadTomas { get; set; }

    /// <summary>Cantidad de interruptores u otros elementos dentro del recinto.</summary>
    public int CantidadOtros { get; set; }

    /// <summary>Total de bocas en el recinto (luces + tomas + otros).</summary>
    public int TotalBocas => CantidadLuminarias + CantidadTomas + CantidadOtros;

    /// <summary>Potencia estimada total en VA de las cargas contenidas en el recinto.</summary>
    public double PotenciaTotalVA { get; set; }

    /// <summary>Circuitos presentes en el recinto separados por coma (ej: "C1, C2").</summary>
    public string CircuitosDetalle { get; set; } = "";

    /// <summary>Detalle textual de cada artefacto encontrado en el recinto.</summary>
    public List<string> ArtefactosDetalle { get; set; } = new();
}

