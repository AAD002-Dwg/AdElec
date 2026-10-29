using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdElec.Core.Aea;

#region DTOs
public record AeaReferenciaDto(
    [property: JsonPropertyName("doc")] string Doc,
    [property: JsonPropertyName("tabla")] string Tabla,
    [property: JsonPropertyName("clausula")] string Clausula,
    [property: JsonPropertyName("pagina_pdf")] int PaginaPdf
);

public record AeaCorreccionDto(
    [property: JsonPropertyName("accion")] string Accion,
    [property: JsonPropertyName("in_a")] double? InA = null,
    [property: JsonPropertyName("seccion_mm2")] double? SeccionMm2 = null,
    [property: JsonPropertyName("seccion_pe_mm2")] double? SeccionPeMm2 = null,
    [property: JsonPropertyName("cano_designacion")] string? CanoDesignacion = null,
    [property: JsonPropertyName("norma")] string? Norma = null,
    [property: JsonPropertyName("tipo")] string? Tipo = null,
    [property: JsonPropertyName("cantidad")] int? Cantidad = null
);

public record AeaDiagnostic(
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("severidad")] string Severidad,
    [property: JsonPropertyName("elemento")] string Elemento,
    [property: JsonPropertyName("mensaje")] string Mensaje,
    [property: JsonPropertyName("referencia")] AeaReferenciaDto Referencia,
    [property: JsonPropertyName("correcciones")] List<AeaCorreccionDto>? Correcciones
);

public record AeaGradoResultadoDto(
    [property: JsonPropertyName("grado")] string Grado,
    [property: JsonPropertyName("superficie_limite_m2")] double SuperficieLimiteM2,
    [property: JsonPropertyName("max_demanda_kva")] double? MaxDemandaKva,
    [property: JsonPropertyName("referencia")] AeaReferenciaDto Referencia
);

public record AeaPuntosMinimosDto(
    [property: JsonPropertyName("ambiente_id")] string AmbienteId,
    [property: JsonPropertyName("iug_minimo")] int IugMinimo,
    [property: JsonPropertyName("tug_minimo")] int TugMinimo,
    [property: JsonPropertyName("modulos_fijos")] int ModulosFijos
);

public record AeaResultadoCortocircuitoDto(
    [property: JsonPropertyName("seccion_minima_mm2")] double SeccionMinimaMm2,
    [property: JsonPropertyName("energia_admisible_a2s")] double EnergiaAdmisibleA2s,
    [property: JsonPropertyName("energia_defecto_a2s")] double EnergiaDefectoA2s,
    [property: JsonPropertyName("cumple")] bool Cumple
);

public record AeaResultadoDpmsDto(
    [property: JsonPropertyName("potencia_sin_simultaneidad_va")] double PotenciaSinSimultaneidadVa,
    [property: JsonPropertyName("coeficiente_simultaneidad")] double CoeficienteSimultaneidad,
    [property: JsonPropertyName("dpms_va")] double DpmsVa,
    [property: JsonPropertyName("dpms_kva")] double DpmsKva,
    [property: JsonPropertyName("corriente_proyecto_ib_a")] double CorrienteProyectoIbA,
    [property: JsonPropertyName("proteccion_cabecera_sugerida_a")] double ProteccionCabeceraSugeridaA,
    [property: JsonPropertyName("referencia")] AeaReferenciaDto Referencia
);

public record AeaResultadoCaneriaDto(
    [property: JsonPropertyName("designacion")] string Designacion,
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("seccion_interna_mm2")] double SeccionInternaMm2,
    [property: JsonPropertyName("area_cables_mm2")] double AreaCablesMm2,
    [property: JsonPropertyName("ocupacion_pct")] double OcupacionPct,
    [property: JsonPropertyName("cumple")] bool Cumple,
    [property: JsonPropertyName("referencia")] AeaReferenciaDto Referencia
);

public class AeaCircuitoDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("tipo")] public string Tipo { get; set; } = "TUG";
    [JsonPropertyName("seccion_mm2")] public double SeccionMm2 { get; set; } = 2.5;
    [JsonPropertyName("in_a")] public double InA { get; set; } = 16.0;
    [JsonPropertyName("ib_a")] public double? IbA { get; set; }
    [JsonPropertyName("bocas")] public int Bocas { get; set; }
    [JsonPropertyName("cables_cargados")] public int CablesCargados { get; set; } = 2;
    [JsonPropertyName("circuitos_en_canalizacion")] public int CircuitosEnCanalizacion { get; set; } = 1;
    [JsonPropertyName("longitud_m")] public double? LongitudM { get; set; }
    [JsonPropertyName("cos_phi")] public double? CosPhi { get; set; }
    [JsonPropertyName("icn_ka")] public double? IcnKa { get; set; }
    [JsonPropertyName("seccion_pe_mm2")] public double? SeccionPeMm2 { get; set; }
    [JsonPropertyName("cano_designacion")] public string? CanoDesignacion { get; set; }
    [JsonPropertyName("potencia_va")] public double? PotenciaVa { get; set; }
    [JsonPropertyName("incluye_tomacorrientes")] public bool IncluyeTomacorrientes { get; set; }
}

public class AeaAmbienteDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("nombre")] public string Nombre { get; set; } = string.Empty;
    [JsonPropertyName("tipo")] public string Tipo { get; set; } = "sala_estar";
    [JsonPropertyName("superficie_m2")] public double SuperficieM2 { get; set; }
    [JsonPropertyName("longitud_m")] public double? LongitudM { get; set; }
    [JsonPropertyName("bocas_iug")] public int BocasIug { get; set; }
    [JsonPropertyName("bocas_tug")] public int BocasTug { get; set; }
    [JsonPropertyName("bocas_tue")] public int BocasTue { get; set; }
}

public class AeaUnidadDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "U1";
    [JsonPropertyName("superficie_cubierta_m2")] public double SuperficieCubiertaM2 { get; set; }
    [JsonPropertyName("superficie_semicubierta_m2")] public double SuperficieSemicubiertaM2 { get; set; }
    [JsonPropertyName("in_cabecera_a")] public double? InCabeceraA { get; set; }
    [JsonPropertyName("icc_origen_ka")] public double? IccOrigenKa { get; set; }
    [JsonPropertyName("norma")] public string? Norma { get; set; }
    [JsonPropertyName("cantidad_id")] public int? CantidadId { get; set; }
    [JsonPropertyName("resistencia_pat_ohm")] public double? ResistenciaPatOhm { get; set; }
    [JsonPropertyName("tiene_dps")] public bool? TieneDps { get; set; }
    [JsonPropertyName("alimentacion_red")] public string? AlimentacionRed { get; set; }
    [JsonPropertyName("aq_dias_tormenta")] public int? AqDiasTormenta { get; set; }
    [JsonPropertyName("circuitos")] public List<AeaCircuitoDto> Circuitos { get; set; } = new();
    [JsonPropertyName("ambientes")] public List<AeaAmbienteDto> Ambientes { get; set; } = new();
}
#endregion

/// <summary>
/// Puente P/Invoke de alto rendimiento hacia el núcleo normativo nativo en Rust (aea_core.dll).
/// </summary>
public static class AeaBridge
{
    private const string LibName = "aea_core";

    static AeaBridge()
    {
        NativeLibrary.SetDllImportResolver(typeof(AeaBridge).Assembly, DllResolver);
    }

    private static IntPtr DllResolver(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == LibName)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string? asmDir = null;
            try
            {
                if (!string.IsNullOrEmpty(assembly.Location))
                    asmDir = Path.GetDirectoryName(assembly.Location);
            }
            catch { }

            var possiblePaths = new List<string>();

            if (!string.IsNullOrEmpty(asmDir))
            {
                possiblePaths.Add(Path.Combine(asmDir, "aea_core.dll"));
                possiblePaths.Add(Path.Combine(asmDir, "runtimes", "win-x64", "native", "aea_core.dll"));
                possiblePaths.Add(Path.GetFullPath(Path.Combine(asmDir, @"..\..\..\..\aea\core\target\release\aea_core.dll")));
            }

            possiblePaths.Add(Path.Combine(baseDir, "aea_core.dll"));
            possiblePaths.Add(Path.Combine(baseDir, "runtimes", "win-x64", "native", "aea_core.dll"));
            possiblePaths.Add(Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\aea\core\target\release\aea_core.dll")));
            possiblePaths.Add(Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\aea\core\target\release\aea_core.dll")));
            possiblePaths.Add(@"G:\PROYECTOS AD\AD-ELEC\aea\core\target\release\aea_core.dll");

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                {
                    return handle;
                }
            }
        }
        return IntPtr.Zero;
    }

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "aea_free_string")]
    private static extern void NativeFreeString(IntPtr ptr);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "aea_grado_electrificacion")]
    private static extern IntPtr NativeGradoElectrificacion(double cubiertaM2, double semicubiertaM2);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "aea_grado_electrificacion_comercial")]
    private static extern IntPtr NativeGradoElectrificacionComercial(double superficieM2);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "aea_validar_unidad")]
    private static extern IntPtr NativeValidarUnidad(IntPtr jsonInput);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "aea_puntos_minimos_ambiente")]
    private static extern IntPtr NativePuntosMinimosAmbiente(IntPtr jsonInput);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "aea_calcular_caida_tension")]
    private static extern double NativeCalcularCaidaTension(double longitudM, double corrienteA, double seccionMm2, byte cablesCargados, double cosPhi);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "aea_verificar_cortocircuito_conductor")]
    private static extern IntPtr NativeVerificarCortocircuito(double iccKa, double tiempoS, double seccionMm2, bool aislacionPvc);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "aea_calcular_dpms")]
    private static extern IntPtr NativeCalcularDpms(IntPtr jsonInput);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "aea_dimensionar_caneria")]
    private static extern IntPtr NativeDimensionarCaneria(double seccionMm2, uint conductoresTotales, IntPtr familiaODesignacion);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "aea_seccion_pe")]
    private static extern double NativeSeccionPe(double seccionFaseMm2);

    public static double CalcularCaidaTension(double longitudM, double corrienteA, double seccionMm2, int cablesCargados = 2, double cosPhi = 0.9)
    {
        return NativeCalcularCaidaTension(longitudM, corrienteA, seccionMm2, (byte)cablesCargados, cosPhi);
    }

    public static double CalcularSeccionPe(double seccionFaseMm2)
    {
        return NativeSeccionPe(seccionFaseMm2);
    }

    public static AeaResultadoCortocircuitoDto VerificarCortocircuito(double iccKa, double tiempoS, double seccionMm2, bool aislacionPvc = true)
    {
        IntPtr ptr = NativeVerificarCortocircuito(iccKa, tiempoS, seccionMm2, aislacionPvc);
        try
        {
            string json = PtrToStringUtf8(ptr);
            return JsonSerializer.Deserialize<AeaResultadoCortocircuitoDto>(json)
                   ?? throw new InvalidOperationException("Respuesta nula del motor de cortocircuito");
        }
        finally
        {
            NativeFreeString(ptr);
        }
    }

    public static AeaResultadoDpmsDto CalcularDpms(AeaUnidadDto unidad)
    {
        string jsonIn = JsonSerializer.Serialize(unidad);
        IntPtr inPtr = StringToHGlobalUtf8(jsonIn);
        try
        {
            IntPtr outPtr = NativeCalcularDpms(inPtr);
            try
            {
                string jsonOut = PtrToStringUtf8(outPtr);
                return JsonSerializer.Deserialize<AeaResultadoDpmsDto>(jsonOut)
                       ?? throw new InvalidOperationException("Respuesta nula en cálculo de DPMS");
            }
            finally
            {
                NativeFreeString(outPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
        }
    }

    public static AeaResultadoCaneriaDto DimensionarCaneria(double seccionMm2, int conductoresTotales, string familiaODesignacion = "aislante_rigido")
    {
        IntPtr strPtr = StringToHGlobalUtf8(familiaODesignacion);
        try
        {
            IntPtr outPtr = NativeDimensionarCaneria(seccionMm2, (uint)Math.Max(1, conductoresTotales), strPtr);
            try
            {
                string jsonOut = PtrToStringUtf8(outPtr);
                return JsonSerializer.Deserialize<AeaResultadoCaneriaDto>(jsonOut)
                       ?? throw new InvalidOperationException("Respuesta nula en dimensionamiento de cañería");
            }
            finally
            {
                NativeFreeString(outPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(strPtr);
        }
    }

    public static AeaGradoResultadoDto CalcularGrado(double cubiertaM2, double semicubiertaM2)
    {
        IntPtr ptr = NativeGradoElectrificacion(cubiertaM2, semicubiertaM2);
        try
        {
            string json = PtrToStringUtf8(ptr);
            return JsonSerializer.Deserialize<AeaGradoResultadoDto>(json)
                   ?? throw new InvalidOperationException("Respuesta nula del motor AEA");
        }
        finally
        {
            NativeFreeString(ptr);
        }
    }

    public static AeaGradoResultadoDto CalcularGradoComercial(double superficieM2)
    {
        IntPtr ptr = NativeGradoElectrificacionComercial(superficieM2);
        try
        {
            string json = PtrToStringUtf8(ptr);
            return JsonSerializer.Deserialize<AeaGradoResultadoDto>(json)
                   ?? throw new InvalidOperationException("Respuesta nula del motor AEA");
        }
        finally
        {
            NativeFreeString(ptr);
        }
    }

    public static List<AeaDiagnostic> ValidarUnidad(AeaUnidadDto unidad)
    {
        string jsonIn = JsonSerializer.Serialize(unidad);
        IntPtr inPtr = StringToHGlobalUtf8(jsonIn);
        try
        {
            IntPtr outPtr = NativeValidarUnidad(inPtr);
            try
            {
                string jsonOut = PtrToStringUtf8(outPtr);
                return JsonSerializer.Deserialize<List<AeaDiagnostic>>(jsonOut) ?? new List<AeaDiagnostic>();
            }
            finally
            {
                NativeFreeString(outPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
        }
    }

    public static AeaPuntosMinimosDto CalcularPuntosMinimos(AeaAmbienteDto ambiente, string grado, string? norma = null)
    {
        var req = new { ambiente, grado, norma };
        string jsonIn = JsonSerializer.Serialize(req);
        IntPtr inPtr = StringToHGlobalUtf8(jsonIn);
        try
        {
            IntPtr outPtr = NativePuntosMinimosAmbiente(inPtr);
            try
            {
                string jsonOut = PtrToStringUtf8(outPtr);
                return JsonSerializer.Deserialize<AeaPuntosMinimosDto>(jsonOut)
                       ?? throw new InvalidOperationException("Respuesta nula en puntos mínimos");
            }
            finally
            {
                NativeFreeString(outPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
        }
    }

    private static string PtrToStringUtf8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return string.Empty;
        return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
    }

    private static IntPtr StringToHGlobalUtf8(string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        IntPtr ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr, bytes.Length, 0); // null-terminator
        return ptr;
    }
}
