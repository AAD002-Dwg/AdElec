using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AdElec.Core.Models;

namespace AdElec.AutoCAD.Geometry
{
    /// <summary>
    /// Clasifica cada <see cref="DetectedRoom"/> según su tipo de ambiente.
    /// Opera en tres niveles de confianza, aplicados en orden:
    ///
    /// Nivel 1 — Texto explícito (alta confianza): TEXT/MTEXT dentro del polígono.
    /// Nivel 2 — Bloques contenidos (media confianza): INSERT con nombre indicador.
    /// Nivel 3 — Heurísticas geométricas (baja confianza): área + aspect ratio.
    /// </summary>
    public class RoomClassifier
    {
        // ── Nivel 1: Diccionario de normalización de texto ────────────────────
        private static readonly Dictionary<string, (string Display, string Api)> TextMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["baño"]           = ("Baño / Toilette",          "baño"),
                ["bano"]           = ("Baño / Toilette",          "baño"),
                ["toilette"]       = ("Baño / Toilette",          "baño"),
                ["toilet"]         = ("Baño / Toilette",          "baño"),
                ["cocina"]         = ("Cocina",                   "cocina"),
                ["estar"]          = ("Estar / Comedor / Living", "sala_estar"),
                ["comedor"]        = ("Estar / Comedor / Living", "sala_estar"),
                ["estar comedor"]  = ("Estar / Comedor / Living", "sala_estar"),
                ["estar-comedor"]  = ("Estar / Comedor / Living", "sala_estar"),
                ["living"]         = ("Estar / Comedor / Living", "sala_estar"),
                ["dormitorio"]     = ("Dormitorio",               "dormitorio"),
                ["habitacion"]     = ("Dormitorio",               "dormitorio"),
                ["habitación"]     = ("Dormitorio",               "dormitorio"),
                ["balcon"]         = ("Pasillo / Balcón / Hall",  "pasillo"),
                ["balcón"]         = ("Pasillo / Balcón / Hall",  "pasillo"),
                ["terraza"]        = ("Pasillo / Balcón / Hall",  "pasillo"),
                ["palier"]         = ("Pasillo / Balcón / Hall",  "pasillo"),
                ["hall"]           = ("Pasillo / Balcón / Hall",  "pasillo"),
                ["sum"]            = ("Otro / Depósito",          "otro"),
                ["salon"]          = ("Otro / Depósito",          "otro"),
                ["salón"]          = ("Otro / Depósito",          "otro"),
                ["estudio"]        = ("Escritorio / Estudio",     "escritorio"),
                ["escritorio"]     = ("Escritorio / Estudio",     "escritorio"),
                ["pasillo"]        = ("Pasillo / Balcón / Hall",  "pasillo"),
                ["paso"]           = ("Pasillo / Balcón / Hall",  "pasillo"),
                ["cochera"]        = ("Cochera / Garaje",         "garaje"),
                ["garage"]         = ("Cochera / Garaje",         "garaje"),
                ["garaje"]         = ("Cochera / Garaje",         "garaje"),
                ["lavadero"]       = ("Lavadero",                 "lavadero"),
                ["escalera"]       = ("Otro / Depósito",          "otro"),
                ["ascensor"]       = ("Otro / Depósito",          "otro"),
                ["sala de maquinas"] = ("Otro / Depósito",        "otro"),
                ["sala máquinas"]  = ("Otro / Depósito",          "otro"),
                ["deposito"]       = ("Otro / Depósito",          "otro"),
                ["depósito"]       = ("Otro / Depósito",          "otro"),
                ["monoambiente"]   = ("Estar / Comedor / Living", "sala_estar"),
            };

        // ── Nivel 2: Bloques indicadores de tipo ──────────────────────────────
        // Clave = substring del nombre de bloque (case-insensitive), Valor = tipo inferido.
        private static readonly List<(string Pattern, string Display, string Api)> BlockIndicators = new()
        {
            ("inodoro",  "Baño / Toilette",  "baño"),
            ("wc",       "Baño / Toilette",  "baño"),
            ("bañera",   "Baño / Toilette",  "baño"),
            ("banera",   "Baño / Toilette",  "baño"),
            ("ducha",    "Baño / Toilette",  "baño"),
            ("bidet",    "Baño / Toilette",  "baño"),
            ("lavatorio","Baño / Toilette",  "baño"),
            ("pileta",   "Cocina",           "cocina"),
            ("heladera", "Cocina",           "cocina"),
            ("cocina",   "Cocina",           "cocina"),
            ("horno",    "Cocina",           "cocina"),
            ("escalera", "Otro / Depósito",  "otro"),
            ("ascensor", "Otro / Depósito",  "otro"),
            ("lavarropas","Lavadero",        "lavadero"),
        };

        /// <summary>
        /// Clasifica todos los rooms detectados usando las entidades del modelspace
        /// para buscar texto y bloques contenidos en cada polígono.
        /// </summary>
        public void ClassifyRooms(List<DetectedRoom> rooms, Database db, RoomDetectionConfig config)
        {
            if (rooms.Count == 0) return;

            // Recolectar entidades TEXT/MTEXT e INSERT del modelspace
            var textEntities = new List<(Point2D Position, string Content)>();
            var blockEntities = new List<(Point2D Position, string BlockName)>();
            var idLocalesBlocks = new List<(Point2D Position, string Local, string UF, string Planta, string Handle)>();

            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId entId in ms)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead);

                if (ent is DBText text)
                {
                    if (config.CapasTexto.Any(c => text.Layer.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        textEntities.Add((
                            new Point2D(text.Position.X, text.Position.Y),
                            text.TextString?.Trim() ?? ""
                        ));
                    }
                }
                else if (ent is MText mtext)
                {
                    if (config.CapasTexto.Any(c => mtext.Layer.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        textEntities.Add((
                            new Point2D(mtext.Location.X, mtext.Location.Y),
                            mtext.Contents?.Trim() ?? ""
                        ));
                    }
                }
                else if (ent is BlockReference br)
                {
                    string blockName = br.IsDynamicBlock
                        ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                        : ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;

                    if (string.Equals(blockName, "ID_LOCALES", StringComparison.OrdinalIgnoreCase))
                    {
                        string localVal = "";
                        string ufVal = "";
                        string plantaVal = "PB";
                        foreach (ObjectId attId in br.AttributeCollection)
                        {
                            var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                            if (att == null) continue;
                            if (string.Equals(att.Tag, "LOCAL", StringComparison.OrdinalIgnoreCase)) localVal = att.TextString?.Trim() ?? "";
                            else if (string.Equals(att.Tag, "01", StringComparison.OrdinalIgnoreCase)) ufVal = att.TextString?.Trim() ?? "";
                            else if (string.Equals(att.Tag, "55", StringComparison.OrdinalIgnoreCase)) plantaVal = att.TextString?.Trim() ?? "";
                        }
                        idLocalesBlocks.Add((
                            new Point2D(br.Position.X, br.Position.Y),
                            localVal,
                            ufVal,
                            plantaVal,
                            br.Handle.ToString()
                        ));
                    }
                    else
                    {
                        blockEntities.Add((
                            new Point2D(br.Position.X, br.Position.Y),
                            blockName
                        ));
                    }
                }
            }

            tr.Commit();

            // Clasificar cada room
            foreach (var room in rooms)
            {
                // ── Nivel 0: Bloque ID_LOCALES existente dentro del recinto ──
                var existingIdBlock = idLocalesBlocks
                    .FirstOrDefault(b => PolygonMath.PointInPolygon(b.Position, room.Vertices));

                if (!string.IsNullOrEmpty(existingIdBlock.Handle))
                {
                    if (!string.IsNullOrEmpty(existingIdBlock.Local))
                    {
                        room.Tipo = existingIdBlock.Local;
                        room.TipoApi = TipoAmbienteInfo.DesdeNombre(existingIdBlock.Local).ApiValue;
                    }
                    if (!string.IsNullOrEmpty(existingIdBlock.UF))
                        room.UnidadFuncional = existingIdBlock.UF;
                    if (!string.IsNullOrEmpty(existingIdBlock.Planta))
                        room.Planta = existingIdBlock.Planta;

                    room.ExistingBlockHandle = existingIdBlock.Handle;
                    room.Confianza = "alta";
                    room.RequiereVerificacion = false;
                    continue;
                }

                // ── Nivel 1: Texto explícito ──────────────────────────────────
                var textsInside = textEntities
                    .Where(t => PolygonMath.PointInPolygon(t.Position, room.Vertices))
                    .ToList();

                bool classified = false;
                foreach (var (_, content) in textsInside)
                {
                    string normalized = content.ToLowerInvariant().Trim();
                    if (TextMap.TryGetValue(normalized, out var match))
                    {
                        room.Tipo = match.Display;
                        room.TipoApi = match.Api;
                        room.Confianza = "alta";
                        room.RequiereVerificacion = room.ContieneBulge; // solo por bulge
                        classified = true;
                        break;
                    }

                    // Intentar matching parcial (el texto contiene la clave)
                    foreach (var kvp in TextMap)
                    {
                        if (normalized.Contains(kvp.Key))
                        {
                            room.Tipo = kvp.Value.Display;
                            room.TipoApi = kvp.Value.Api;
                            room.Confianza = "alta";
                            room.RequiereVerificacion = room.ContieneBulge;
                            classified = true;
                            break;
                        }
                    }
                    if (classified) break;
                }
                if (classified) continue;

                // ── Nivel 2: Bloques contenidos ───────────────────────────────
                var blocksInside = blockEntities
                    .Where(b => PolygonMath.PointInPolygon(b.Position, room.Vertices))
                    .ToList();

                foreach (var (_, blockName) in blocksInside)
                {
                    string nameLower = blockName.ToLowerInvariant();
                    foreach (var (pattern, display, api) in BlockIndicators)
                    {
                        if (nameLower.Contains(pattern))
                        {
                            room.Tipo = display;
                            room.TipoApi = api;
                            room.Confianza = "media";
                            room.RequiereVerificacion = true;
                            classified = true;
                            break;
                        }
                    }
                    if (classified) break;
                }
                if (classified) continue;

                // ── Nivel 3: Heurísticas geométricas ──────────────────────────
                ClassifyByGeometry(room);
            }
        }

        /// <summary>
        /// Clasificación heurística basada en área y aspect ratio.
        /// Siempre marca RequiereVerificacion = true.
        /// </summary>
        private void ClassifyByGeometry(DetectedRoom room)
        {
            double area = room.AreaM2;
            double aspect = PolygonMath.AspectRatio(room.Vertices);

            room.Confianza = "baja";
            room.RequiereVerificacion = true;

            if (area < 4.0 && aspect > 2.5)
            {
                // Estrecho y pequeño → Pasillo
                room.Tipo = "Pasillo / Balcón / Hall";
                room.TipoApi = "pasillo";
            }
            else if (area < 6.0 && aspect < 2.0)
            {
                // Pequeño y compacto → Baño
                room.Tipo = "Baño / Toilette";
                room.TipoApi = "baño";
            }
            else if (area >= 6.0 && area < 12.0 && aspect < 2.0)
            {
                // Mediano y compacto → Dormitorio
                room.Tipo = "Dormitorio";
                room.TipoApi = "dormitorio";
            }
            else if (area >= 12.0 && area < 35.0)
            {
                // Grande → Estar/Comedor
                room.Tipo = "Estar / Comedor / Living";
                room.TipoApi = "sala_estar";
            }
            else if (area >= 100.0)
            {
                // Muy grande → SUM o área común
                room.Tipo = "Otro / Depósito";
                room.TipoApi = "otro";
            }
            else
            {
                // No clasificable → Otro
                room.Tipo = "Otro / Depósito";
                room.TipoApi = "otro";
            }
        }
    }
}
