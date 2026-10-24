using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AdElec.Core.Models
{
    public enum NamingStyle
    {
        SeccionFisica, // ej: "2x1,5+T", "3x1,5+T", "4x1,5+T" (Estilo preferido por el usuario)
        Funcional      // ej: "F+R+T", "F+2R+T"
    }

    /// <summary>
    /// Preferencias y aprendizaje automático de hábitos del proyectista.
    /// Se persiste localmente en AppData y dentro del DWG para que el sistema
    /// se adapte dinámicamente a la forma de trabajar del usuario.
    /// </summary>
    public class UserPreferences
    {
        public NamingStyle EstiloNomenclatura { get; set; } = NamingStyle.SeccionFisica;

        // Nomenclaturas por defecto según el hábito del usuario
        public string Retorno1Efecto { get; set; } = "2x1,5+T";
        public string Retorno2Efectos { get; set; } = "3x1,5+T";
        public string Retorno3Efectos { get; set; } = "4x1,5+T";
        public string RetornoCombinacion { get; set; } = "3x1,5+T";

        public string LuzTramoPrincipal { get; set; } = "2x1,5+T";
        public string TomasTramoPrincipal { get; set; } = "2x2,5+T";
        public string TomasEspeciales { get; set; } = "2x4+T";

        public string CaneriaDefault { get; set; } = "RS";

        public double PotenciaBocaLuzDefault { get; set; } = 60.0;
        public double PotenciaBocaTomaDefault { get; set; } = 150.0;

        // Historial de decisiones aprendidas del usuario: clave de contexto -> último valor usado
        public Dictionary<string, string> DecisionesAprendidas { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registra una decisión manual del usuario para aprender de ella en futuras operaciones.
        /// </summary>
        public void AprenderDecision(string contexto, string valor)
        {
            if (string.IsNullOrWhiteSpace(contexto) || string.IsNullOrWhiteSpace(valor)) return;
            DecisionesAprendidas[contexto.Trim()] = valor.Trim();
            SaveGlobal();
        }

        /// <summary>
        /// Obtiene la nomenclatura para un interruptor según la cantidad de efectos.
        /// Respeta las decisiones previas del usuario.
        /// </summary>
        public string ObtenerNomenclaturaRetorno(int numEfectos, bool esCombinacion = false)
        {
            string key = esCombinacion ? "retorno_combinacion" : $"retorno_{numEfectos}_efectos";
            if (DecisionesAprendidas.TryGetValue(key, out string? aprendida) && !string.IsNullOrEmpty(aprendida))
            {
                return aprendida;
            }

            if (esCombinacion) return RetornoCombinacion;
            return numEfectos switch
            {
                1 => Retorno1Efecto,
                2 => Retorno2Efectos,
                3 => Retorno3Efectos,
                _ => $"{numEfectos + 1}x1,5+T"
            };
        }

        public string ObtenerNomenclaturaParaEfectos(int numEfectos, bool esCombinacion = false) =>
            ObtenerNomenclaturaRetorno(numEfectos, esCombinacion);

        // ── Persistencia Global (AppData) ────────────────────────────────────

        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AdElec");

        private static readonly string PrefsFilePath = Path.Combine(AppDataFolder, "user_preferences.json");

        private static UserPreferences? _instance;

        public static UserPreferences Instance => _instance ??= LoadGlobal();

        public static UserPreferences Cargar() => Instance;

        public static UserPreferences LoadGlobal()
        {
            try
            {
                if (File.Exists(PrefsFilePath))
                {
                    string json = File.ReadAllText(PrefsFilePath);
                    var prefs = JsonSerializer.Deserialize<UserPreferences>(json);
                    if (prefs != null) return prefs;
                }
            }
            catch { }

            return new UserPreferences();
        }

        public void SaveGlobal()
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PrefsFilePath, json);
            }
            catch { }
        }
    }
}
