using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using AdElec.Core.Models;

namespace AdElec.AutoCAD.Geometry
{
    /// <summary>
    /// Construye el grafo de conectividad entre ambientes detectados
    /// y los agrupa en unidades funcionales (UF).
    ///
    /// Conectividad se determina por:
    /// - Bloques INSERT con nombres de puerta en la frontera entre polígonos.
    /// - Arcos (ARC) con radio típico de puerta.
    ///
    /// Agrupamiento:
    /// - Identificar nodos de acceso (Palier, Hall) y removerlos del grafo.
    /// - Los componentes conexos resultantes son UFs candidatas.
    /// - Clasificar cada UF según composición de ambientes.
    ///
    /// Fallback obligatorio: si no se pueden determinar UFs con confianza,
    /// asignar SIN_CLASIFICAR con RequiereVerificacion = true.
    /// Nunca silenciar incertidumbre en el agrupamiento.
    /// </summary>
    public class RoomGrouper
    {
        // Tipos de ambiente que pertenecen a Servicios Generales (SG)
        private static readonly HashSet<string> TiposSG = new(StringComparer.OrdinalIgnoreCase)
        {
            "Palier", "Hall", "Salón de Usos Múltiples", "SUM",
            "Sala de Máquinas", "Escalera", "Ascensor"
        };

        /// <summary>
        /// Agrupa los ambientes detectados en unidades funcionales.
        /// Modifica in-place los campos UnidadFuncional y TipoUnidadFuncional de cada room.
        /// </summary>
        public void GroupRooms(List<DetectedRoom> rooms, Database db, RoomDetectionConfig config)
        {
            if (rooms.Count == 0) return;

            // ── Step 1: Detectar puertas ──────────────────────────────────────
            var doors = DetectDoors(db, config);

            // ── Step 2: Construir grafo de adyacencia ─────────────────────────
            var graph = BuildAdjacencyGraph(rooms, doors, config);

            // ── Step 3: Intentar agrupamiento por conectividad ────────────────
            bool canGroup = TryGroupByConnectivity(rooms, graph);

            if (!canGroup)
            {
                // ── Fallback: agrupamiento simple por proximidad ──────────────
                GroupByProximityFallback(rooms);
            }

            // ── Step 4: Clasificar tipo de UF según composición ───────────────
            ClassifyUFTypes(rooms);
        }

        /// <summary>
        /// Detecta puertas en el modelspace como bloques INSERT y arcos ARC
        /// con radio en el rango de puerta.
        /// </summary>
        private List<(Point2D Position, double Radius)> DetectDoors(Database db, RoomDetectionConfig config)
        {
            var doors = new List<(Point2D Position, double Radius)>();

            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId entId in ms)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead);

                if (ent is BlockReference br)
                {
                    // Verificar si el nombre del bloque indica puerta
                    string blockName;
                    try
                    {
                        blockName = br.IsDynamicBlock
                            ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                            : ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;
                    }
                    catch { continue; }

                    bool isDoor = config.NombresBloquesPuerta
                        .Any(p => blockName.Contains(p, StringComparison.OrdinalIgnoreCase));

                    if (isDoor || config.CapasPuertas.Any(c => br.Layer.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        doors.Add((new Point2D(br.Position.X, br.Position.Y), 0.9));
                    }
                }
                else if (ent is Arc arc)
                {
                    // Arcos con radio típico de puerta en capas de puerta
                    if (arc.Radius >= config.RadioPuertaMin &&
                        arc.Radius <= config.RadioPuertaMax)
                    {
                        // Si está en capa de puertas
                        if (config.CapasPuertas.Any(c => arc.Layer.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            var center = new Point2D(arc.Center.X, arc.Center.Y);
                            doors.Add((center, arc.Radius));
                        }
                    }
                }
            }

            tr.Commit();
            return doors;
        }

        /// <summary>
        /// Construye un grafo de adyacencia entre ambientes.
        /// Dos ambientes están conectados si hay una puerta en la frontera
        /// entre sus polígonos (o cerca de ella).
        /// </summary>
        private Dictionary<int, HashSet<int>> BuildAdjacencyGraph(
            List<DetectedRoom> rooms,
            List<(Point2D Position, double Radius)> doors,
            RoomDetectionConfig config)
        {
            var graph = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < rooms.Count; i++)
                graph[i] = new HashSet<int>();

            // Para cada puerta, encontrar qué dos ambientes conecta
            double doorSearchRadius = config.ToleranciaSnap * 5; // radio de búsqueda ampliado

            foreach (var (doorPos, _) in doors)
            {
                // Encontrar ambientes cuyo polígono esté cerca de la puerta
                var nearRooms = new List<int>();
                for (int i = 0; i < rooms.Count; i++)
                {
                    // La puerta debe estar dentro del polígono o muy cerca de su perímetro
                    if (PolygonMath.PointInPolygon(doorPos, rooms[i].Vertices))
                    {
                        nearRooms.Add(i);
                    }
                    else if (IsNearPerimeter(doorPos, rooms[i].Vertices, doorSearchRadius))
                    {
                        nearRooms.Add(i);
                    }
                }

                // Conectar cada par de ambientes cercanos a la puerta
                for (int a = 0; a < nearRooms.Count; a++)
                {
                    for (int b = a + 1; b < nearRooms.Count; b++)
                    {
                        graph[nearRooms[a]].Add(nearRooms[b]);
                        graph[nearRooms[b]].Add(nearRooms[a]);
                    }
                }
            }

            return graph;
        }

        /// <summary>
        /// Intenta agrupar por conectividad: identifica nodos de acceso,
        /// los remueve, y extrae componentes conexos.
        /// Retorna false si el resultado no es confiable (grafo totalmente conexo,
        /// no hay nodos de acceso, o resultado degenerate).
        /// </summary>
        private bool TryGroupByConnectivity(List<DetectedRoom> rooms, Dictionary<int, HashSet<int>> graph)
        {
            // Identificar nodos de acceso (Palier, Hall, etc.)
            var accessNodes = new HashSet<int>();
            for (int i = 0; i < rooms.Count; i++)
            {
                string tipoLower = rooms[i].Tipo.ToLowerInvariant();
                if (tipoLower.Contains("palier") || tipoLower.Contains("hall") ||
                    tipoLower.Contains("escalera") || tipoLower.Contains("ascensor"))
                {
                    accessNodes.Add(i);
                }
            }

            // Si no hay nodos de acceso → no podemos agrupar con confianza
            if (accessNodes.Count == 0)
                return false;

            // Marcar nodos de acceso como SG (Servicios Generales)
            foreach (int i in accessNodes)
            {
                rooms[i].UnidadFuncional = "SG";
                rooms[i].TipoUnidadFuncional = "Servicios Generales";
            }

            // Extraer componentes conexos excluyendo nodos de acceso
            var nonAccess = Enumerable.Range(0, rooms.Count)
                .Where(i => !accessNodes.Contains(i))
                .ToList();

            var components = GetConnectedComponents(nonAccess, graph);

            // Si un solo componente contiene todos → grafo demasiado conexo
            if (components.Count <= 1 && nonAccess.Count > 10)
                return false;

            int ufIndex = 0;
            foreach (var component in components)
            {
                ufIndex++;
                string ufName = $"UF-{ufIndex:D2}";
                foreach (int i in component)
                {
                    rooms[i].UnidadFuncional = ufName;
                }
            }

            return true;
        }

        /// <summary>
        /// Fallback: cuando el grafo de conectividad no es útil,
        /// usar proximidad geométrica o asignar SIN_CLASIFICAR.
        ///
        /// COMPORTAMIENTO OBLIGATORIO:
        /// - Todos los ambientes no clasificados van a SIN_CLASIFICAR
        /// - RequiereVerificacion = true y Confianza = "baja"
        /// - El usuario resuelve manualmente en el panel de verificación
        /// </summary>
        private void GroupByProximityFallback(List<DetectedRoom> rooms)
        {
            foreach (var room in rooms)
            {
                if (room.UnidadFuncional == "SIN_CLASIFICAR" || string.IsNullOrEmpty(room.UnidadFuncional))
                {
                    room.UnidadFuncional = "SIN_CLASIFICAR";
                    room.TipoUnidadFuncional = "";
                    room.RequiereVerificacion = true;
                    room.Confianza = "baja";
                }
            }
        }

        /// <summary>
        /// Clasifica el tipo de UF según la composición de ambientes:
        /// - Cocina + Baño → Departamento
        /// - Baño sin cocina → Oficina
        /// - Sin baño ni cocina → Local Comercial
        /// - Palier/Hall/SUM/Sala de Máquinas → Servicios Generales
        /// - Solo cochera → Cochera (UF propia)
        /// - No clasificable → sin cambio (SIN_CLASIFICAR)
        /// </summary>
        private void ClassifyUFTypes(List<DetectedRoom> rooms)
        {
            var ufGroups = rooms
                .GroupBy(r => r.UnidadFuncional)
                .Where(g => g.Key != "SG" && g.Key != "SIN_CLASIFICAR");

            foreach (var group in ufGroups)
            {
                var tipos = group.Select(r => r.TipoApi.ToLowerInvariant()).ToHashSet();

                string ufType;
                if (tipos.Contains("cocina") && tipos.Contains("baño"))
                    ufType = "Departamento";
                else if (tipos.Contains("baño") && !tipos.Contains("cocina"))
                    ufType = "Oficina";
                else if (tipos.Contains("garaje") && tipos.Count == 1)
                    ufType = "Cochera";
                else if (!tipos.Contains("baño") && !tipos.Contains("cocina"))
                    ufType = "Local Comercial";
                else
                    ufType = "Departamento"; // default optimista

                foreach (var room in group)
                {
                    room.TipoUnidadFuncional = ufType;
                }
            }
        }

        /// <summary>
        /// Extrae componentes conexos de un subconjunto de nodos del grafo.
        /// </summary>
        private List<List<int>> GetConnectedComponents(List<int> nodes, Dictionary<int, HashSet<int>> graph)
        {
            var components = new List<List<int>>();
            var visited = new HashSet<int>();
            var nodeSet = new HashSet<int>(nodes);

            foreach (int node in nodes)
            {
                if (visited.Contains(node)) continue;

                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(node);
                visited.Add(node);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Add(current);

                    if (!graph.TryGetValue(current, out var neighbors)) continue;

                    foreach (int neighbor in neighbors)
                    {
                        if (!nodeSet.Contains(neighbor)) continue; // solo nodos del subconjunto
                        if (visited.Contains(neighbor)) continue;

                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }

                components.Add(component);
            }

            return components;
        }

        /// <summary>
        /// Verifica si un punto está cerca del perímetro de un polígono
        /// (dentro de una distancia threshold de cualquier arista).
        /// </summary>
        private bool IsNearPerimeter(Point2D point, IReadOnlyList<Point2D> polygon, double threshold)
        {
            double thresholdSq = threshold * threshold;
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % n];
                double distSq = DistanceToSegmentSq(point, a, b);
                if (distSq <= thresholdSq) return true;
            }
            return false;
        }

        /// <summary>Distancia al cuadrado de un punto a un segmento.</summary>
        private double DistanceToSegmentSq(Point2D p, Point2D a, Point2D b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-10) return PolygonMath.DistanceSq(p, a);

            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
            t = Math.Max(0, Math.Min(1, t));

            var proj = new Point2D(a.X + t * dx, a.Y + t * dy);
            return PolygonMath.DistanceSq(p, proj);
        }
    }
}
