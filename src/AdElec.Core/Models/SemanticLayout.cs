using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AdElec.Core.Models
{
    public class SemanticLayout
    {
        [JsonPropertyName("origen_ai")]
        public string OrigenAi { get; set; }

        [JsonPropertyName("ambientes")]
        public List<SemanticRoom> Ambientes { get; set; } = new();

        [JsonPropertyName("puertas")]
        public List<SemanticOpening> Puertas { get; set; } = new();

        [JsonPropertyName("ventanas")]
        public List<SemanticOpening> Ventanas { get; set; } = new();

        // Reservado para la detección futura de mobiliario para análisis eléctrico
        [JsonPropertyName("mobiliario")]
        public List<SemanticFurniture> Mobiliario { get; set; } = new(); 
    }

    public class SemanticRoom
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("centroide")]
        public double[] Centroide { get; set; } // [X, Y]

        [JsonPropertyName("tipo")]
        public string Tipo { get; set; }

        [JsonPropertyName("confianza")]
        public double Confianza { get; set; }

        [JsonPropertyName("polygon")]
        public List<double[]> Polygon { get; set; } // List of [X, Y]
    }

    public class SemanticOpening
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("bbox")]
        public double[] Bbox { get; set; } // [minX, minY, maxX, maxY]

        [JsonPropertyName("confianza")]
        public double Confianza { get; set; }
    }

    public class SemanticFurniture
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("bbox")]
        public double[] Bbox { get; set; } // [minX, minY, maxX, maxY]
        
        [JsonPropertyName("tipo")]
        public string Tipo { get; set; } // ej: "Cama", "Mesa", "Inodoro"

        [JsonPropertyName("confianza")]
        public double Confianza { get; set; }
    }
}
