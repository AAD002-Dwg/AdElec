using System.Text.Json.Serialization;

namespace AdElec.Core.AeaMotor.Dtos;

/// <summary>
/// Proyecto eléctrico para enviar a POST /calcular de AEA-MOTOR.
/// Los nombres de propiedad JSON siguen exactamente el esquema Pydantic de AEA-MOTOR.
/// </summary>
public class ProyectoDto
{
    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [JsonPropertyName("version_norma")]
    public string VersionNorma { get; set; } = "2006";

    [JsonPropertyName("superficie_cubierta_m2")]
    public double SuperficieCubiertaM2 { get; set; }

    [JsonPropertyName("superficie_semicubierta_m2")]
    public double SuperficieSemicubiertaM2 { get; set; } = 0;

    [JsonPropertyName("ambientes")]
    public List<AmbienteDto> Ambientes { get; set; } = [];

    [JsonPropertyName("circuitos")]
    public List<CircuitoDto> Circuitos { get; set; } = [];

    [JsonPropertyName("montante")]
    public MontanteDto Montante { get; set; } = new();

    [JsonPropertyName("datos_red")]
    public DatosRedDto DatosRed { get; set; } = new();

    [JsonPropertyName("es_edificio")]
    public bool EsEdificio { get; set; } = false;

    [JsonPropertyName("protecciones")]
    public List<object> Protecciones { get; set; } = [];

    [JsonPropertyName("unidades")]
    public List<object> Unidades { get; set; } = [];
}

/// <summary>Local/ambiente. El campo <c>id</c> es requerido por Pydantic.</summary>
public class AmbienteDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Enum AEA: sala_estar | escritorio | dormitorio | cocina | baño | pasillo | lavadero | garaje | otro
    /// </summary>
    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = "otro";

    [JsonPropertyName("nombre")]
    public string? Nombre { get; set; }

    [JsonPropertyName("superficie_m2")]
    public double? SuperficieM2 { get; set; }

    [JsonPropertyName("longitud_m")]
    public double? LongitudM { get; set; }
}

/// <summary>Circuito terminal. El campo <c>id</c> y <c>bocas</c> son requeridos por Pydantic.</summary>
public class CircuitoDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Enum AEA: IUG | TUG | TUE | IUE | APM | ACU | MBTF | ATE | MBTS | OCE | ITE</summary>
    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = "TUG";

    /// <summary>Cantidad de bocas. El campo se llama <c>bocas</c> en AEA-MOTOR.</summary>
    [JsonPropertyName("bocas")]
    public int Bocas { get; set; } = 1;

    [JsonPropertyName("longitud_m")]
    public double LongitudM { get; set; } = 10;

    [JsonPropertyName("seccion_mm2")]
    public double? SeccionMm2 { get; set; }

    [JsonPropertyName("incluye_tomacorrientes")]
    public bool IncluyeTomacorrientes { get; set; } = false;
}

/// <summary>Línea principal / montante. <c>cosfi</c> y <c>trifasico</c> son los nombres exactos de AEA-MOTOR.</summary>
public class MontanteDto
{
    [JsonPropertyName("longitud_m")]
    public double LongitudM { get; set; } = 10;

    /// <summary>Factor de potencia. Se llama <c>cosfi</c> en AEA-MOTOR (no cos_phi).</summary>
    [JsonPropertyName("cosfi")]
    public double Cosfi { get; set; } = 0.9;

    /// <summary>Se llama <c>trifasico</c> en AEA-MOTOR (no es_trifasico).</summary>
    [JsonPropertyName("trifasico")]
    public bool Trifasico { get; set; } = false;

    [JsonPropertyName("seccion_mm2")]
    public double? SeccionMm2 { get; set; }
}

/// <summary>Datos de red. <c>tension_V</c> con V mayúscula, impedancia en mΩ.</summary>
public class DatosRedDto
{
    /// <summary>Tensión nominal. El campo en Pydantic es <c>tension_V</c> (V mayúscula).</summary>
    [JsonPropertyName("tension_V")]
    public double TensionV { get; set; } = 220;

    /// <summary>Impedancia en miliohms (no ohms). Null = no disponible.</summary>
    [JsonPropertyName("impedancia_red_mohm")]
    public double? ImpedanciaRedMohm { get; set; } = null;

    [JsonPropertyName("trifasico")]
    public bool Trifasico { get; set; } = false;
}
