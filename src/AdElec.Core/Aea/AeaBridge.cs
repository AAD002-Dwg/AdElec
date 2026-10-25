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
    [property: JsonPropertyName("referencia")] AeaReferenciaDto Referencia
);

public record AeaPuntosMinimosDto(
    [property: JsonPropertyName("ambiente_id")] string AmbienteId,
    [property: JsonPropertyName("iug_minimo")] int IugMinimo,
    [property: JsonPropertyName("tug_minimo")] int TugMinimo,
    [property: JsonPropertyName("modulos_fijos")] int ModulosFijos
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
}

public class AeaUnidadDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "U1";
    [JsonPropertyName("superficie_cubierta_m2")] public double SuperficieCubiertaM2 { get; set; }
    [JsonPropertyName("superficie_semicubierta_m2")] public double SuperficieSemicubiertaM2 { get; set; }
    [JsonPropertyName("in_cabecera_a")] public double? InCabeceraA { get; set; }
    [JsonPropertyName("icc_origen_ka")] public double? IccOrigenKa { get; set; }
    [JsonPropertyName("norma")] public string? Norma { get; set; }
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
            // 1. Probar ruta relativa directa de compilación
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] possiblePaths =
            {
                Path.Combine(baseDir, "aea_core.dll"),
                Path.Combine(baseDir, "runtimes", "win-x64", "native", "aea_core.dll"),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\aea\core\target\release\aea_core.dll")),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\aea\core\target\release\aea_core.dll")),
                @"G:\PROYECTOS AD\AD-ELEC\aea\core\target\release\aea_core.dll"
            };

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

    public static AeaPuntosMinimosDto CalcularPuntosMinimos(AeaAmbienteDto ambiente, string grado)
    {
        var req = new { ambiente, grado };
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
        int len = 0;
        while (Marshal.ReadByte(ptr, len) != 0) len++;
        byte[] buffer = new byte[len];
        Marshal.Copy(ptr, buffer, 0, len);
        return Encoding.UTF8.GetString(buffer);
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
