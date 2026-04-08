using System.Text.Json.Serialization;

namespace AdElec.Core.AeaMotor.Dtos;

// ── SYNC: POST /proyectos  ────────────────────────────────────────────────────

/// <summary>Payload para crear/actualizar un proyecto en el editor web de AEA-MOTOR.</summary>
public class SyncProjectRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("data_json")]
    public SyncProjectData DataJson { get; set; } = new();
}

/// <summary>
/// Envoltura de data_json: el frontend lee data_json.ad_elec.rooms
/// </summary>
public class SyncProjectData
{
    [JsonPropertyName("ad_elec")]
    public SyncProjectCanvas AdElec { get; set; } = new();

    [JsonPropertyName("ad_cad")]
    public SyncAdCadData AdCad { get; set; } = new();
}

public class SyncAdCadData
{
    [JsonPropertyName("plantas")]
    public List<SyncPlanta> Plantas { get; set; } = [];
}

public class SyncPlanta
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "PL1";

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = "Planta Baja";

    [JsonPropertyName("nivel")]
    public double Nivel { get; set; } = 0;

    [JsonPropertyName("graph")]
    public SyncGraph Graph { get; set; } = new();

    [JsonPropertyName("recintosMeta")]
    public List<SyncRecintoMeta> RecintosMeta { get; set; } = [];

    /// <summary>
    /// Agrupación de recintos por Unidad Funcional (departamento, local, etc.).
    /// AD-CAD usa esta lista para colorear y filtrar recintos por UF.
    /// </summary>
    [JsonPropertyName("unidades_funcionales")]
    public List<SyncUnidadFuncional> UnidadesFuncionales { get; set; } = [];
}

public class SyncUnidadFuncional
{
    /// <summary>Identificador de la UF (ej: "UF1", "DEPTO-A", "OFICINA 1").</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Nombre visible en AD-CAD.</summary>
    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    /// <summary>IDs de los SyncRoom que pertenecen a esta UF.</summary>
    [JsonPropertyName("room_ids")]
    public List<string> RoomIds { get; set; } = [];
}

public class SyncGraph
{
    [JsonPropertyName("nodes")]
    public List<SyncNode> Nodes { get; set; } = [];

    [JsonPropertyName("edges")]
    public List<SyncEdge> Edges { get; set; } = [];
}

public class SyncNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public class SyncEdge
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("a")]
    public string NodeA { get; set; } = string.Empty;

    [JsonPropertyName("b")]
    public string NodeB { get; set; } = string.Empty;

    [JsonPropertyName("thickness")]
    public double Thickness { get; set; } = 0.15;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "wall";

    [JsonPropertyName("justification")]
    public string Justification { get; set; } = "center";
}

public class SyncRecintoMeta
{
    [JsonPropertyName("faceKey")]
    public string FaceKey { get; set; } = string.Empty;

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = "otro";

    /// <summary>
    /// Centroide del recinto en coordenadas mundo.
    /// Cuando faceKey está vacío, AD-CAD usa este punto para hacer
    /// point-in-polygon y asignar el tipo al face del grafo correspondiente.
    /// </summary>
    [JsonPropertyName("coord")]
    public Dictionary<string, double>? Coord { get; set; }
}

public class SyncProjectCanvas
{
    [JsonPropertyName("rooms")]
    public List<SyncRoom> Rooms { get; set; } = [];

    [JsonPropertyName("board")]
    public SyncBoard Board { get; set; } = new();
}

public class SyncRoom
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "OTHER";

    [JsonPropertyName("area")]
    public double Area { get; set; }

    [JsonPropertyName("polygon_points")]
    public List<Dictionary<string, double>> PolygonPoints { get; set; } = [];

    [JsonPropertyName("wall_thickness")]
    public double WallThickness { get; set; } = 0.15;

    [JsonPropertyName("centroid")]
    public Dictionary<string, double> Centroid { get; set; } = new();

    [JsonPropertyName("points")]
    public List<ProposalPoint> Points { get; set; } = [];

    [JsonPropertyName("openings")]
    public List<object> Openings { get; set; } = [];
}

public class SyncBoard
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "board_001";

    [JsonPropertyName("main_switch")]
    public string MainSwitch { get; set; } = "TM 32A";

    [JsonPropertyName("rcd")]
    public string Rcd { get; set; } = "ID 40A/30mA";

    [JsonPropertyName("circuits")]
    public List<SyncCircuit> Circuits { get; set; } = [];
}

public class SyncCircuit
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("amperage")]
    public int Amperage { get; set; }

    [JsonPropertyName("protection")]
    public string Protection { get; set; } = string.Empty;
}

public class SyncProjectResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("data_json")]
    public SyncProjectData DataJson { get; set; } = new();
}

// ── INPUT: POST /api/v1/adelec/generate_proposal ─────────────────────────────

public class ProposalProjectInput
{
    [JsonPropertyName("project_name")]
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>"Mínimo" | "Medio" | "Elevado" | "Superior"</summary>
    [JsonPropertyName("electrification_level")]
    public string ElectrificationLevel { get; set; } = "Mínimo";

    [JsonPropertyName("sla_area")]
    public double SlaArea { get; set; }

    [JsonPropertyName("rooms")]
    public List<ProposalRoomInput> Rooms { get; set; } = [];

    [JsonPropertyName("board")]
    public ProposalBoardInput Board { get; set; } = new();

    /// <summary>
    /// "ALL" | "LUMINAIRES" | "OUTLETS" — qué tipo de bocas regenerar.
    /// </summary>
    [JsonPropertyName("target_layer")]
    public string TargetLayer { get; set; } = "ALL";
}

public class ProposalRoomInput
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Enum NAME del RoomType en adelec (e.g. "BEDROOM", "KITCHEN", "LIVING_DINING").
    /// Se hace lookup por nombre: RoomType[type].
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "OTHER";

    [JsonPropertyName("area")]
    public double Area { get; set; }

    /// <summary>Puntos existentes (vacío = el motor genera desde cero).</summary>
    [JsonPropertyName("points")]
    public List<object> Points { get; set; } = [];

    /// <summary>Vértices del polígono del recinto en coordenadas del dibujo.</summary>
    [JsonPropertyName("polygon_points")]
    public List<Dictionary<string, double>> PolygonPoints { get; set; } = [];

    [JsonPropertyName("centroid")]
    public Dictionary<string, double> Centroid { get; set; } = new();

    [JsonPropertyName("openings")]
    public List<object> Openings { get; set; } = [];
}

public class ProposalBoardInput
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "board_001";

    [JsonPropertyName("main_switch")]
    public string MainSwitch { get; set; } = "TM 32A";

    [JsonPropertyName("rcd")]
    public string Rcd { get; set; } = "ID 40A/30mA";

    [JsonPropertyName("circuits")]
    public List<object> Circuits { get; set; } = [];
}

// ── OUTPUT: generate_proposal response ───────────────────────────────────────

public class ProposalResponse
{
    [JsonPropertyName("rooms_update")]
    public List<ProposalRoomUpdate> RoomsUpdate { get; set; } = [];

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public class ProposalRoomUpdate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("points")]
    public List<ProposalPoint> Points { get; set; } = [];
}

public class ProposalPoint
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    /// <summary>IUG | IUE | TUG | TUE | SWITCH | BOARD</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>Circuito asignado (ej: "C1").</summary>
    [JsonPropertyName("circuit_id")]
    public string? CircuitId { get; set; }

    /// <summary>Identificador de llave (ej: "A", "B") — vincula IUG con su SWITCH.</summary>
    [JsonPropertyName("switch_id")]
    public string? SwitchId { get; set; }

    [JsonPropertyName("power_va")]
    public double PowerVa { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("is_fixed")]
    public bool IsFixed { get; set; }
}
