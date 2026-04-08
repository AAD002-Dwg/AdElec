using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdElec.Core.AeaMotor.Dtos;

namespace AdElec.Core.AeaMotor;

/// <summary>
/// Cliente HTTP para el servicio AEA-MOTOR (FastAPI, puerto 8000 por defecto).
/// Instanciar una vez y reusar (HttpClient es thread-safe).
/// </summary>
public sealed class AeaMotorClient : IAeaMotorClient
{
    private readonly HttpClient _http;

    // Los DTOs ya tienen [JsonPropertyName] explícitos, por eso solo necesitamos
    // ignorar nulls y ser case-insensitive en la deserialización.
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public AeaMotorClient(string baseUrl = "http://localhost:8000")
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    /// <inheritdoc/>
    public async Task<ResultadoProyectoDto> CalcularAsync(ProyectoDto proyecto, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/calcular", proyecto, _jsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"AEA-MOTOR respondió {(int)response.StatusCode}: {error}");
        }

        var resultado = await response.Content.ReadFromJsonAsync<ResultadoProyectoDto>(_jsonOptions, ct);
        return resultado ?? throw new InvalidOperationException("AEA-MOTOR devolvió una respuesta vacía.");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// El endpoint /grado de AEA-MOTOR recibe query params (no body).
    /// Ejemplo: POST /grado?superficie_cubierta_m2=80&amp;superficie_semicubierta_m2=0
    /// </remarks>
    public async Task<ResultadoGradoDto> ObtenerGradoAsync(
        double superficieCubiertaM2,
        double superficieSemicubiertaM2 = 0,
        CancellationToken ct = default)
    {
        string url = $"/api/v1/grado?superficie_cubierta_m2={superficieCubiertaM2}" +
                     $"&superficie_semicubierta_m2={superficieSemicubiertaM2}";

        var response = await _http.PostAsync(url, content: null, ct);

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"AEA-MOTOR /grado error {(int)response.StatusCode}: {error}");
        }

        var resultado = await response.Content.ReadFromJsonAsync<ResultadoGradoDto>(_jsonOptions, ct);
        return resultado ?? throw new InvalidOperationException("AEA-MOTOR /grado devolvió vacío.");
    }

    /// <inheritdoc/>
    public async Task<ProposalResponse> GenerarPropuestaAsync(ProposalProjectInput input, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/adelec/generate_proposal", input, _jsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"AEA-MOTOR /adelec/generate_proposal error {(int)response.StatusCode}: {error}");
        }

        var resultado = await response.Content.ReadFromJsonAsync<ProposalResponse>(_jsonOptions, ct);
        return resultado ?? throw new InvalidOperationException("generate_proposal devolvió vacío.");
    }

    /// <inheritdoc/>
    public async Task<SyncProjectResponse> SincronizarProyectoAsync(SyncProjectRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/proyectos", request, _jsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"AEA-MOTOR /proyectos error {(int)response.StatusCode}: {error}");
        }

        var resultado = await response.Content.ReadFromJsonAsync<SyncProjectResponse>(_jsonOptions, ct);
        return resultado ?? throw new InvalidOperationException("/proyectos devolvió vacío.");
    }

    /// <inheritdoc/>
    public async Task<bool> EstaDisponibleAsync(CancellationToken ct = default)
    {
        try
        {
            // /docs es el endpoint de Swagger de FastAPI, siempre está disponible
            var response = await _http.GetAsync("/docs", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<SyncProjectResponse> GetProjectAsync(int id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/proyectos/{id}", ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"AEA-MOTOR no encontró el proyecto {id}.");
        }

        var resultado = await response.Content.ReadFromJsonAsync<SyncProjectResponse>(_jsonOptions, ct);
        return resultado ?? throw new InvalidOperationException($"/proyectos/{id} devolvió vacío.");
    }

    /// <inheritdoc/>
    public async Task<SyncProjectResponse> ActualizarProyectoAsync(int id, SyncProjectRequest request, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/api/v1/proyectos/{id}", request, _jsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Error al actualizar proyecto {id}: {body}");
        }

        var resultado = await response.Content.ReadFromJsonAsync<SyncProjectResponse>(_jsonOptions, ct);
        return resultado ?? throw new InvalidOperationException($"PUT /proyectos/{id} devolvió vacío.");
    }
}
