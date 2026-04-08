using System.Text.Json.Serialization;

namespace AdElec.Core.AeaMotor.Dtos;

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
