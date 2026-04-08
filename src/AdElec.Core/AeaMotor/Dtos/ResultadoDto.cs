using System.Text.Json.Serialization;

namespace AdElec.Core.AeaMotor.Dtos;

/// <summary>
/// Resultado completo de POST /api/v1/calcular.
/// Los nombres siguen exactamente el esquema Pydantic ResultadoProyecto de AEA-MOTOR.
/// </summary>
public class ResultadoProyectoDto
{
    [JsonPropertyName("nombre_proyecto")]
    public string NombreProyecto { get; set; } = string.Empty;

    [JsonPropertyName("version_norma")]
    public string VersionNorma { get; set; } = string.Empty;

    [JsonPropertyName("grado")]
    public ResultadoGradoDto Grado { get; set; } = new();

    [JsonPropertyName("bocas_por_ambiente")]
    public List<ResultadoBocasAmbienteDto> BocasPorAmbiente { get; set; } = [];

    [JsonPropertyName("circuitos")]
    public List<ResultadoCircuitoDto> Circuitos { get; set; } = [];

    [JsonPropertyName("demanda")]
    public ResultadoDemandaDto Demanda { get; set; } = new();

    [JsonPropertyName("montante")]
    public ResultadoMontanteDto Montante { get; set; } = new();

    [JsonPropertyName("validaciones")]
    public List<ValidacionDto> Validaciones { get; set; } = [];

    [JsonPropertyName("cumple_norma")]
    public bool CumpleNorma { get; set; }

    [JsonPropertyName("resumen_incumplimientos")]
    public List<string> ResumenIncumplimientos { get; set; } = [];
}

/// <summary>Grado de electrificación (Tabla 771.8.I).</summary>
public class ResultadoGradoDto
{
    [JsonPropertyName("superficie_computable_m2")]
    public double SuperficieComputableM2 { get; set; }

    /// <summary>Ej: "Mínimo", "Medio", "Elevado", "Superior".</summary>
    [JsonPropertyName("grado")]
    public string Grado { get; set; } = string.Empty;

    [JsonPropertyName("dpms_max_kVA")]
    public double? DpmsMaxKva { get; set; }

    [JsonPropertyName("circuitos_minimos")]
    public int CircuitosMinimos { get; set; }

    [JsonPropertyName("composicion_minima")]
    public string ComposicionMinima { get; set; } = string.Empty;
}

/// <summary>Bocas mínimas por ambiente calculadas por AEA-MOTOR.</summary>
public class ResultadoBocasAmbienteDto
{
    [JsonPropertyName("ambiente_id")]
    public string AmbienteId { get; set; } = string.Empty;

    [JsonPropertyName("ambiente_tipo")]
    public string AmbienteTipo { get; set; } = string.Empty;

    [JsonPropertyName("ambiente_nombre")]
    public string? AmbienteNombre { get; set; }

    [JsonPropertyName("IUG")]
    public int IUG { get; set; }

    [JsonPropertyName("TUG")]
    public int TUG { get; set; }

    [JsonPropertyName("TUE")]
    public int TUE { get; set; }

    [JsonPropertyName("IUE")]
    public int IUE { get; set; }
}

/// <summary>Resultado calculado para un circuito terminal.</summary>
public class ResultadoCircuitoDto
{
    [JsonPropertyName("circuito_id")]
    public string CircuitoId { get; set; } = string.Empty;

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = string.Empty;

    [JsonPropertyName("bocas")]
    public int Bocas { get; set; }

    /// <summary>Demanda del circuito en VA. Campo Python: demanda_VA (VA en mayúscula).</summary>
    [JsonPropertyName("demanda_VA")]
    public double DemandaVa { get; set; }

    [JsonPropertyName("corriente_calculo_A")]
    public double CorrienteCalculoA { get; set; }

    /// <summary>Sección de fase adoptada en mm².</summary>
    [JsonPropertyName("seccion_adoptada_mm2")]
    public double SeccionAdoptadaMm2 { get; set; }

    [JsonPropertyName("seccion_fase_mm2")]
    public double SeccionFaseMm2 { get; set; }

    [JsonPropertyName("seccion_pe_mm2")]
    public double SeccionPeMm2 { get; set; }

    [JsonPropertyName("intensidad_admisible_A")]
    public double? IntensidadAdmisibleA { get; set; }

    /// <summary>Calibre máximo de protección en A según tipo de circuito.</summary>
    [JsonPropertyName("proteccion_max_A")]
    public double? ProteccionMaxA { get; set; }

    [JsonPropertyName("caida_tension_pct")]
    public double? CaidaTensionPct { get; set; }

    [JsonPropertyName("caida_ok")]
    public bool? CaidaOk { get; set; }

    [JsonPropertyName("corriente_ok")]
    public bool? CorrienteOk { get; set; }

    /// <summary>Nomenclatura de conductores (ej: "2x2,5 mm² Cu PVC").</summary>
    [JsonPropertyName("nomenclatura")]
    public string? Nomenclatura { get; set; }

    /// <summary>Designación de cañería (ej: "PVC 20mm").</summary>
    [JsonPropertyName("caneria")]
    public string? Caneria { get; set; }
}

/// <summary>Demanda total y por unidad del tablero.</summary>
public class ResultadoDemandaDto
{
    [JsonPropertyName("demanda_unidad_VA")]
    public double DemandaUnidadVa { get; set; }

    [JsonPropertyName("demanda_unidad_kVA")]
    public double DemandaUnidadKva { get; set; }

    [JsonPropertyName("demanda_total_VA")]
    public double DemandaTotalVa { get; set; }

    [JsonPropertyName("demanda_total_kVA")]
    public double DemandaTotalKva { get; set; }

    [JsonPropertyName("coef_simultaneidad")]
    public double CoefSimultaneidad { get; set; } = 1.0;
}

/// <summary>Resultado del cálculo de montante / línea principal.</summary>
public class ResultadoMontanteDto
{
    [JsonPropertyName("seccion_adoptada_mm2")]
    public double SeccionAdoptadaMm2 { get; set; }

    [JsonPropertyName("corriente_calculo_A")]
    public double CorrienteCalculoA { get; set; }

    [JsonPropertyName("caida_tension_pct")]
    public double? CaidaTensionPct { get; set; }

    [JsonPropertyName("caida_ok")]
    public bool? CaidaOk { get; set; }
}

/// <summary>Resultado de una regla normativa del motor de validaciones.</summary>
public class ValidacionDto
{
    [JsonPropertyName("regla_id")]
    public string ReglaId { get; set; } = string.Empty;

    [JsonPropertyName("descripcion")]
    public string Descripcion { get; set; } = string.Empty;

    /// <summary>info | advertencia | incumplimiento | ambiguedad_normativa</summary>
    [JsonPropertyName("severidad")]
    public string Severidad { get; set; } = string.Empty;

    [JsonPropertyName("cumple")]
    public bool Cumple { get; set; }

    [JsonPropertyName("detalle")]
    public string Detalle { get; set; } = string.Empty;

    [JsonPropertyName("articulo_norma")]
    public string ArticuloNorma { get; set; } = string.Empty;
}
