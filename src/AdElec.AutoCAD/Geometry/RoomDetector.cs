using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AdElec.Core.Models;

namespace AdElec.AutoCAD.Geometry
{
    /// <summary>
    /// Motor de detección automática de ambientes.
    /// Escanea el modelspace buscando polilíneas cerradas y segmentos de muro,
    /// reconstruye perímetros cerrados desde segmentos sueltos, y filtra
    /// polígonos anidados/inválidos.
    ///
    /// La reconstrucción desde segmentos es el caso predominante y el componente
    /// más complejo del sistema. La tolerancia de snap es el parámetro más
    /// crítico: controla cómo se fusionan endpoints próximos para cerrar perímetros
    /// en planos donde los segmentos no se tocan exactamente.
    /// </summary>
    public class RoomDetector
    {
        /// <summary>
        /// Detecta ambientes en la base de datos del dibujo activo.
        /// Combina dos estrategias: polilíneas cerradas existentes (caso rápido)
        /// y reconstrucción de perímetros desde segmentos sueltos (caso predominante).
        /// </summary>
        public List<DetectedRoom> DetectRooms(
            Database db,
            RoomDetectionConfig config,
            AdElec.Core.Models.SemanticLayout layout = null,
            Action<string> logger = null,
            IEnumerable<ObjectId> selectedEntities = null)
        {
            var rooms = new List<DetectedRoom>();

            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            // ── Pass 1: Polilíneas cerradas existentes (caso rápido) ──────────
            var closedPolys = new List<(List<Point2D> Vertices, string Handle, bool HasBulge)>();
            // ── Pass 2: Segmentos para reconstrucción ─────────────────────────
            var segments = new List<(Point2D Start, Point2D End)>();
            var segmentsWithBulge = new HashSet<int>(); // indices de segmentos con bulge

            bool isManualSelection = selectedEntities != null;
            var entitiesToProcess = selectedEntities ?? ms.Cast<ObjectId>();

            foreach (ObjectId entId in entitiesToProcess)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead);

                if (ent is Polyline poly)
                {
                    string layerName = poly.Layer;
                    bool isMuro = config.CapasMuros.Any(c => layerName.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0);
                    bool isAmbiente = config.CapasAmbientes.Any(c => layerName.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0);
                    bool hasSyncXData = poly.GetXDataForApplication("ADE_SYNC_LINK") != null;

                    // Si fue seleccionada manualmente por el usuario o cumple capas/XData
                    if (!isManualSelection && !isMuro && !isAmbiente && !hasSyncXData) continue;

                    if (poly.Closed && poly.NumberOfVertices >= 3)
                    {
                        // Polilínea cerrada → candidato directo
                        var (vertices, hasBulge) = ExtractPolylineVertices(poly);
                        closedPolys.Add((vertices, entId.Handle.ToString(), hasBulge));
                    }
                    else if (isMuro && poly.NumberOfVertices >= 2)
                    {
                        // Polilínea abierta de muro → extraer segmentos individuales
                        ExtractPolylineSegments(poly, segments, segmentsWithBulge);
                    }
                }
                else if (ent is Line line)
                {
                    string layerName = line.Layer;
                    if (!config.CapasMuros.Any(c => layerName.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0)) continue;

                    segments.Add((
                        new Point2D(line.StartPoint.X, line.StartPoint.Y),
                        new Point2D(line.EndPoint.X, line.EndPoint.Y)
                    ));
                }
            }

            if (logger != null) logger($"\n  [DEBUG] Segmentos libres: {segments.Count}, Polilíneas cerradas: {closedPolys.Count}");

            int roomIndex = 0;

            // ── Crear rooms a partir de polilíneas cerradas ───────────────────
            foreach (var (vertices, handle, hasBulge) in closedPolys)
            {
                double rawArea = PolygonMath.CalculateArea(vertices);
                double sFact = DeduceScaleFactorFromRawArea(rawArea);
                double area = rawArea * sFact * sFact;
                
                if (area < config.AreaMinimaM2 || area > config.AreaMaximaM2) continue;

                roomIndex++;
                rooms.Add(CreateRoom(roomIndex, vertices, area, sFact, handle, hasBulge, config));
            }

            // Determinamos el factor de escala global antes de procesar para ajustar Snap e Inflation
            double scaleFactor = 1.0;
            if (segments.Count > 0)
            {
                // Calculamos la longitud promedio de las líneas de muros detectadas
                double avgLengthSq = segments.Average(s => PolygonMath.DistanceSq(s.Start, s.End));
                double avgLength = Math.Sqrt(avgLengthSq);
                
                // Si las líneas miden en promedio > 500 unidades, es 99% probable que sea MM
                if (avgLength > 500) scaleFactor = 0.001; // Milímetros
                else if (avgLength > 15) scaleFactor = 0.01;  // Centímetros
                else scaleFactor = 1.0; // Metros
            }

            // ── Pass 2.5: Inyectar segmentos para sellar puertas inteligentemente ───
            if (layout != null)
            {
                double baseInflation = 0.20; 
                double inflation = baseInflation / scaleFactor; 
                var wallPoints = segments.SelectMany(s => new[] { s.Start, s.End }).ToList();
                
                foreach (var puerta in layout.Puertas.Concat(layout.Ventanas))
                {
                    if (puerta.Bbox == null || puerta.Bbox.Length < 4) continue;
                    double xmin = puerta.Bbox[0] - inflation;
                    double ymin = puerta.Bbox[1] - inflation;
                    double xmax = puerta.Bbox[2] + inflation;
                    double ymax = puerta.Bbox[3] + inflation;

                    var ptsInBox = wallPoints.Where(p => p.X >= xmin && p.X <= xmax && p.Y >= ymin && p.Y <= ymax).ToList();

                    if (logger != null) logger($"\n  [DEBUG] IA BBox ({xmin:F1},{ymin:F1})-({xmax:F1},{ymax:F1}) contiene {ptsInBox.Count} endpoints");

                    int bridgesCreated = 0;
                    for (int i = 0; i < ptsInBox.Count; i++)
                    {
                        Point2D? bestMatch = null;
                        double bestDist = double.MaxValue;
                        for (int j = 0; j < ptsInBox.Count; j++)
                        {
                            if (i == j) continue;
                            double dist = Math.Sqrt(PolygonMath.DistanceSq(ptsInBox[i], ptsInBox[j]));
                            // Buscamos el punto más cercano que cruce la puerta (> 0.4m) pero no otra habitación (< 1.5m)
                            if (dist >= 0.4 && dist <= 1.5 && dist < bestDist)
                            {
                                bestDist = dist;
                                bestMatch = ptsInBox[j];
                            }
                        }
                        if (bestMatch.HasValue)
                        {
                            segments.Add((ptsInBox[i], bestMatch.Value));
                            bridgesCreated++;
                            if (logger != null) logger($"\n    [DEBUG] -> Puente creado ({bestDist:F2}m)");
                        }
                    }
                    if (logger != null && bridgesCreated == 0) logger("\n    [DEBUG] -> NINGÚN puente creado (no se encontraron pares válidos).");
                }
            }

            // ── Reconstruir perímetros desde segmentos sueltos ────────────────
            if (segments.Count > 0)
            {
                // Un snap de 5mm (0.005) suele ser muy estricto para planos en metros que tienen errores de dibujo.
                // Lo forzamos a 5cm mínimo para metros.
                double baseSnap = Math.Max(config.ToleranciaSnap, 0.05); 
                double effectiveSnap = baseSnap / scaleFactor;
                if (logger != null) logger($"\n  [DEBUG] Tolerancia final: {effectiveSnap:F4} (Scale: {scaleFactor})");

                var reconstructed = ReconstructPolygonsFromSegments(segments, effectiveSnap);
                if (logger != null) logger($"\n  [DEBUG] Polígonos base extraídos: {reconstructed.Count}");

                foreach (var polygon in reconstructed)
                {
                    double rawArea = PolygonMath.CalculateArea(polygon);
                    double area = Math.Abs(rawArea * scaleFactor * scaleFactor);
                    double perimeter = PolygonMath.CalculatePerimeter(polygon) * scaleFactor;
                    if (logger != null) logger($"\n  [DEBUG] Evaluando polígono: RawArea={rawArea:F2}, Area={area:F2}m2, Perim={perimeter:F2}m");

                    if (area < config.AreaMinimaM2 || area > config.AreaMaximaM2)
                    {
                        if (logger != null) logger(" -> Descartado por Area limits");
                        continue;
                    }

                    if (area > 500.0)
                    {
                        if (logger != null) logger(" -> Descartado por ser demasiado grande");
                        continue;
                    }

                    // Filtro de Esbeltez: Una forma muy alargada probablemente es un pasillo o pared,
                    // pero si tiene un area coherente de habitacion (> 3.0), no la descartamos.
                    if (area < 3.0 && IsSkinnyPolygon(area, perimeter)) 
                    {
                        if (logger != null) logger(" -> Descartado por Esbeltez (Muro Flaco)");
                        continue;
                    }

                    string customRoomType = null;
                    if (layout != null && layout.Ambientes.Count > 0)
                    {
                        var aiHit = layout.Ambientes.FirstOrDefault(ai => 
                            ai.Centroide != null && ai.Centroide.Length >= 2 &&
                            PolygonMath.PointInPolygon(new Point2D(ai.Centroide[0], ai.Centroide[1]), polygon));
                        
                        // Si la IA está activa, exigimos que el polígono tenga un centroide para mayor seguridad
                        // en ambientes de menos de 10m2 (donde los muros flacos mas confunden).
                        if (aiHit == null && area < 10.0) continue; 
                        
                        customRoomType = aiHit?.Tipo;
                    }

                    // Verificar si hay bulge en algún segmento fuente
                    // (simplificación V1: marcamos si hay segmentos con bulge en el pool general)
                    bool hasBulge = segmentsWithBulge.Count > 0;

                    roomIndex++;
                    var newRoom = CreateRoom(roomIndex, polygon, area, scaleFactor, "", hasBulge, config);
                    if (!string.IsNullOrEmpty(customRoomType))
                    {
                        newRoom.Tipo = customRoomType;
                    }
                    rooms.Add(newRoom);
                }
            }

            // ── Filtrar polígonos anidados ────────────────────────────────────
            rooms = FilterNestedPolygons(rooms);

            tr.Commit();
            return rooms;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Reconstrucción de perímetros desde segmentos
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Reconstruye polígonos cerrados a partir de segmentos sueltos.
        ///
        /// Estrategia:
        /// 1. Merge de endpoints dentro de la tolerancia de snap (clustering)
        /// 2. Construcción de grafo planar sobre endpoints mergeados
        /// 3. Extracción de ciclos mínimos (minimal cycle basis)
        /// 4. Validación de polígonos resultantes
        ///
        /// La tolerancia de snap es crítica: en plantas reales hay gaps de 1-2mm,
        /// solapamientos, y esquinas que no cierran. El clustering por tolerancia
        /// resuelve estos problemas sin comparar todos contra todos.
        /// </summary>
        private List<List<Point2D>> ReconstructPolygonsFromSegments(
            List<(Point2D Start, Point2D End)> segments, double tolerance)
        {
            if (segments.Count == 0) return new List<List<Point2D>>();

            double toleranceSq = tolerance * tolerance;

            // ── Step 1: Extraer todos los endpoints únicos ────────────────────
            var allPoints = new List<Point2D>(segments.Count * 2);
            foreach (var (start, end) in segments)
            {
                allPoints.Add(start);
                allPoints.Add(end);
            }

            // ── Step 2: Clustering por tolerancia ─────────────────────────────
            // Agrupa puntos cercanos y los reemplaza por su centroide.
            int[] parent = new int[allPoints.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;

            for (int i = 0; i < allPoints.Count; i++)
            {
                for (int j = i + 1; j < allPoints.Count; j++)
                {
                    if (PolygonMath.DistanceSq(allPoints[i], allPoints[j]) <= toleranceSq)
                    {
                        Union(parent, i, j);
                    }
                }
            }

            // Calcular centroide de cada cluster
            var clusterPoints = new Dictionary<int, List<Point2D>>();
            for (int i = 0; i < allPoints.Count; i++)
            {
                int root = Find(parent, i);
                if (!clusterPoints.TryGetValue(root, out var list))
                {
                    list = new List<Point2D>();
                    clusterPoints[root] = list;
                }
                list.Add(allPoints[i]);
            }

            var clusterCentroids = new Dictionary<int, Point2D>();
            foreach (var kvp in clusterPoints)
            {
                double cx = 0, cy = 0;
                foreach (var p in kvp.Value) { cx += p.X; cy += p.Y; }
                clusterCentroids[kvp.Key] = new Point2D(cx / kvp.Value.Count, cy / kvp.Value.Count);
            }

            // Mapear cada punto original a su cluster centroide
            var pointToCluster = new int[allPoints.Count];
            for (int i = 0; i < allPoints.Count; i++)
                pointToCluster[i] = Find(parent, i);

            // ── Step 3: Construir grafo sobre clusters ────────────────────────
            // Cada cluster es un nodo. Cada segmento es una arista.
            var adjacency = new Dictionary<int, HashSet<int>>();

            for (int s = 0; s < segments.Count; s++)
            {
                int startCluster = pointToCluster[s * 2];
                int endCluster = pointToCluster[s * 2 + 1];

                if (startCluster == endCluster) continue; // segmento degenerado

                if (!adjacency.ContainsKey(startCluster))
                    adjacency[startCluster] = new HashSet<int>();
                if (!adjacency.ContainsKey(endCluster))
                    adjacency[endCluster] = new HashSet<int>();

                adjacency[startCluster].Add(endCluster);
                adjacency[endCluster].Add(startCluster);
            }

            // ── Step 4: Extraer ciclos mínimos usando recorrido por ángulo ───
            // Para un grafo planar (que es el caso de un plano de planta),
            // los ciclos mínimos se extraen recorriendo en orden angular.
            var polygons = ExtractMinimalCycles(adjacency, clusterCentroids);

            return polygons;
        }

        /// <summary>
        /// Extrae ciclos mínimos de un grafo planar recorriendo aristas
        /// en orden angular (el ciclo más pequeño a la izquierda de cada arista dirigida).
        ///
        /// Algoritmo:
        /// Para cada arista dirigida (u→v) no visitada:
        /// 1. Desde v, elegir la siguiente arista que forme el menor ángulo a la izquierda.
        /// 2. Repetir hasta volver a u o exceder un límite de nodos.
        /// 3. Si el ciclo cierra y tiene orientación antihoraria, es un ambiente candidato.
        /// </summary>
        private List<List<Point2D>> ExtractMinimalCycles(
            Dictionary<int, HashSet<int>> adjacency,
            Dictionary<int, Point2D> nodePositions)
        {
            var result = new List<List<Point2D>>();
            var visitedEdges = new HashSet<(int, int)>();

            foreach (var node in adjacency.Keys)
            {
                if (!adjacency.TryGetValue(node, out var neighbors)) continue;

                foreach (var neighbor in neighbors)
                {
                    if (visitedEdges.Contains((node, neighbor))) continue;

                    var cycle = TraceCycle(node, neighbor, adjacency, nodePositions, visitedEdges);
                    if (cycle != null && cycle.Count >= 3)
                    {
                        // Verificar orientación: solo los ciclos antihorarios son ambientes
                        // (los horarios son el "exterior" de la face)
                        var polygon = cycle.Select(id => nodePositions[id]).ToList();
                        double signedArea = CalculateSignedArea(polygon);
                        if (signedArea > 0) // antihorario = interior
                        {
                            result.Add(polygon);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Traza un ciclo mínimo empezando desde la arista dirigida (startNode → currentNode),
        /// siempre girando lo más a la izquierda posible.
        /// </summary>
        private List<int>? TraceCycle(
            int startNode, int secondNode,
            Dictionary<int, HashSet<int>> adjacency,
            Dictionary<int, Point2D> nodePositions,
            HashSet<(int, int)> visitedEdges)
        {
            var cycle = new List<int> { startNode };
            int prevNode = startNode;
            int currentNode = secondNode;
            int maxSteps = 100; // límite razonable para un ambiente

            for (int step = 0; step < maxSteps; step++)
            {
                cycle.Add(currentNode);
                visitedEdges.Add((prevNode, currentNode));

                if (currentNode == startNode)
                {
                    // Ciclo cerrado
                    cycle.RemoveAt(cycle.Count - 1); // quitar el duplicado del start
                    return cycle;
                }

                if (!adjacency.TryGetValue(currentNode, out var neighbors) || neighbors.Count == 0)
                    return null; // dead end

                // Encontrar la siguiente arista girando lo más a la izquierda
                var incomingDir = new Point2D(
                    nodePositions[currentNode].X - nodePositions[prevNode].X,
                    nodePositions[currentNode].Y - nodePositions[prevNode].Y
                );
                double incomingAngle = Math.Atan2(incomingDir.Y, incomingDir.X);

                int bestNext = -1;
                double bestAngle = double.MaxValue;

                foreach (var next in neighbors)
                {
                    if (next == prevNode && neighbors.Count > 1) continue; // no volver atrás si hay alternativa

                    var outDir = new Point2D(
                        nodePositions[next].X - nodePositions[currentNode].X,
                        nodePositions[next].Y - nodePositions[currentNode].Y
                    );
                    double outAngle = Math.Atan2(outDir.Y, outDir.X);

                    // Ángulo de giro relativo (a la izquierda = menor ángulo clockwise desde incoming invertido)
                    double reverseIncoming = incomingAngle + Math.PI;
                    double turnAngle = outAngle - reverseIncoming;

                    // Normalizar a [0, 2π)
                    while (turnAngle < 0) turnAngle += 2 * Math.PI;
                    while (turnAngle >= 2 * Math.PI) turnAngle -= 2 * Math.PI;

                    if (turnAngle < bestAngle)
                    {
                        bestAngle = turnAngle;
                        bestNext = next;
                    }
                }

                if (bestNext < 0) return null;

                prevNode = currentNode;
                currentNode = bestNext;
            }

            return null; // no cerró en maxSteps
        }

        // ══════════════════════════════════════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Extrae los vértices de una polilínea como Point2D.
        /// V1: segmentos con bulge != 0 se tratan como rectos (punto inicio → punto fin).
        /// Devuelve también si algún segmento tenía bulge.
        /// </summary>
        public static (List<Point2D> Vertices, bool HasBulge) ExtractPolylineVertices(Polyline poly)
        {
            var vertices = new List<Point2D>(poly.NumberOfVertices);
            bool hasBulge = false;

            for (int i = 0; i < poly.NumberOfVertices; i++)
            {
                var p = poly.GetPoint2dAt(i);
                vertices.Add(new Point2D(p.X, p.Y));

                if (poly.GetBulgeAt(i) != 0)
                    hasBulge = true;
            }

            return (vertices, hasBulge);
        }

        /// <summary>
        /// Extrae segmentos individuales de una polilínea abierta.
        /// Cada par de vértices consecutivos genera un segmento.
        /// </summary>
        private void ExtractPolylineSegments(
            Polyline poly,
            List<(Point2D Start, Point2D End)> segments,
            HashSet<int> segmentsWithBulge)
        {
            for (int i = 0; i < poly.NumberOfVertices - 1; i++)
            {
                var p0 = poly.GetPoint2dAt(i);
                var p1 = poly.GetPoint2dAt(i + 1);

                int segIndex = segments.Count;
                segments.Add((new Point2D(p0.X, p0.Y), new Point2D(p1.X, p1.Y)));

                if (poly.GetBulgeAt(i) != 0)
                    segmentsWithBulge.Add(segIndex);
            }
        }

        /// <summary>
        /// Crea un DetectedRoom a partir de los vértices de un polígono detectado.
        /// </summary>
        private DetectedRoom CreateRoom(
            int index, List<Point2D> vertices, double areaM2, double scaleFactor,
            string sourceHandle, bool hasBulge, RoomDetectionConfig config)
        {
            var centroid = PolygonMath.CalculateCentroid(vertices);
            double perimeter = PolygonMath.CalculatePerimeter(vertices) * scaleFactor;

            return new DetectedRoom
            {
                Id = $"AMB-{index:D3}",
                Vertices = vertices,
                Centroid = centroid,
                AreaM2 = Math.Round(areaM2, 2),
                PerimetroM = Math.Round(perimeter, 2),
                Planta = config.PlantaDefault,
                SourceHandle = sourceHandle,
                ContieneBulge = hasBulge,
                RequiereVerificacion = hasBulge, // V1: bulge → verificación requerida
            };
        }

        /// <summary>
        /// Filtra polígonos anidados: si un polígono A contiene completamente
        /// a un polígono B, se descarta A (se conserva el interior, que es el
        /// ambiente real — A probablemente es el perímetro exterior del edificio).
        /// </summary>
        private List<DetectedRoom> FilterNestedPolygons(List<DetectedRoom> rooms)
        {
            if (rooms.Count <= 1) return rooms;

            // Ordenar por área descendente para que los contenedores estén primero
            var sorted = rooms.OrderByDescending(r => r.AreaM2).ToList();
            var toRemove = new HashSet<int>();

            for (int i = 0; i < sorted.Count; i++)
            {
                if (toRemove.Contains(i)) continue;

                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (toRemove.Contains(j)) continue;

                    if (PolygonMath.PolygonContainsPolygon(sorted[i].Vertices, sorted[j].Vertices))
                    {
                        // i contiene a j → marcar i para remoción
                        toRemove.Add(i);
                        break; // i ya está marcado, no necesitamos seguir
                    }
                }
            }

            return sorted.Where((_, idx) => !toRemove.Contains(idx)).ToList();
        }

        /// Determina el factor de escala de las unidades del DWG a metros.
        /// Deduce el factor de escala a metros analizando las áreas crudas de las polilíneas.
        /// Ignora db.Insunits porque el 90% de los planos de arquitectura vienen con
        /// la plantilla acadiso.dwt (INSUNITS=4/mm) aunque el usuario dibuje en metros.
        /// </summary>
        private double DeduceScaleFactorFromRawArea(double rawArea)
        {
            // Si el área cruda ya parece estar en el rango de metros cuadrados típicos (ej: 0.5 a 5000), asumimos metros
            if (rawArea >= 0.1 && rawArea <= 10000) return 1.0;
            
            // Si el área cruda es enorme (ej: 1,000,000), es porque dibujaron en milímetros reales (3000x3000mm)
            if (rawArea >= 100000) return 0.001;
            
            // Si es intermedia o ínfima (ej: dibujado en centímetros), asume cm
            if (rawArea >= 10 && rawArea < 100000) return 0.01;

            return 1.0; // fallback a metros
        }

        /// <summary>Area con signo (positivo = antihorario).</summary>
        private double CalculateSignedArea(IReadOnlyList<Point2D> vertices)
        {
            double area = 0;
            int n = vertices.Count;
            for (int i = 0; i < n; i++)
            {
                var p0 = vertices[i];
                var p1 = vertices[(i + 1) % n];
                area += p0.X * p1.Y - p1.X * p0.Y;
            }
            return area / 2.0;
        }

        // ── Union-Find para clustering de endpoints ──────────────────────────

        /// <summary>
        /// Determina si un polígono es excesivamente delgado (esbelto), 
        /// lo cual indica que es un muro y no una habitación.
        /// </summary>
        private bool IsSkinnyPolygon(double area, double perimeter)
        {
            if (area <= 0) return true;
            // Ratio isoperimétrico: P^2 / A. 
            // Cuadrado = 16. Rectangulo 10:1 = 48.
            // Muro típico con espesor 0.15m suele dar ratios > 60-80.
            double ratio = (perimeter * perimeter) / area;
            return ratio > 60.0; // Bajamos el umbral para ser mas agresivos contra muros
        }

        private int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]]; // path compression
                i = parent[i];
            }
            return i;
        }

        private void Union(int[] parent, int a, int b)
        {
            int rootA = Find(parent, a);
            int rootB = Find(parent, b);
            if (rootA != rootB) parent[rootA] = rootB;
        }
    }
}
