using System;
using System.Collections.Generic;
using System.Linq;
using AdElec.Core.AeaMotor.Dtos;
using AdElec.Core.Models;

namespace AdElec.Core.Utils
{
    public static class GeometryConverter
    {
        private const double TOLERANCE = 0.01; // 1cm

        private class EdgeInfo
        {
            public string Id { get; set; } = "";
            public string NodeA { get; set; } = "";
            public string NodeB { get; set; } = "";
            public double Thickness { get; set; }
            public List<int> RoomIndices { get; } = new();
            
            // Registramos la dirección original en que cada habitación usó esta arista
            // RoomIndex -> (isForward: true si usó NodeA->NodeB)
            public Dictionary<int, bool> RoomDirections { get; } = new();
        }

        public static SyncGraph BuildGraphFromAmbientes(IEnumerable<Ambiente> ambientes, bool useInteriorJustification = false)
        {
            var nodes = new Dictionary<string, SyncNode>();
            var edges = new Dictionary<string, EdgeInfo>();
            var ambienteList = ambientes.ToList();

            // 1. Recolección de Topología
            for (int i = 0; i < ambienteList.Count; i++)
            {
                var amb = ambienteList[i];
                if (amb.PolygonPoints == null || amb.PolygonPoints.Count < 2) continue;

                // Forzamos que el polígono sea CCW para que "derecha" siempre sea "afuera"
                var poly = EnsureCCW(amb.PolygonPoints);
                var roomNodes = new List<string>();

                foreach (var pt in poly)
                {
                    string key = GetCoordKey(pt.X, pt.Y);
                    if (!nodes.ContainsKey(key))
                    {
                        var nodeId = $"N_{nodes.Count:D4}";
                        nodes[key] = new SyncNode { Id = nodeId, X = Math.Round(pt.X, 3), Y = Math.Round(pt.Y, 3) };
                    }
                    roomNodes.Add(nodes[key].Id);
                }

                // Cerrar loop
                if (roomNodes.First() != roomNodes.Last()) roomNodes.Add(roomNodes.First());

                // Procesar aristas
                for (int j = 0; j < roomNodes.Count - 1; j++)
                {
                    string id1 = roomNodes[j];
                    string id2 = roomNodes[j + 1];
                    if (id1 == id2) continue;

                    var sorted = new[] { id1, id2 }.OrderBy(x => x).ToArray();
                    string edgeKey = $"{sorted[0]}|{sorted[1]}";

                    if (!edges.ContainsKey(edgeKey))
                    {
                        edges[edgeKey] = new EdgeInfo
                        {
                            Id = $"E_{edges.Count:D4}",
                            NodeA = sorted[0],
                            NodeB = sorted[1],
                            Thickness = amb.EspesorMuro
                        };
                    }

                    edges[edgeKey].RoomIndices.Add(i);
                    // ¿La habitación usó la arista en sentido A->B (foward) o B->A (backward)?
                    edges[edgeKey].RoomDirections[i] = (id1 == edges[edgeKey].NodeA);

                    if (amb.EspesorMuro > edges[edgeKey].Thickness)
                        edges[edgeKey].Thickness = amb.EspesorMuro;
                }
            }

            // 2. Cálculo de Justificación
            var syncEdges = new List<SyncEdge>();
            foreach (var e in edges.Values)
            {
                var syncEdge = new SyncEdge
                {
                    Id = e.Id,
                    NodeA = e.NodeA,
                    NodeB = e.NodeB,
                    Thickness = e.Thickness,
                    Type = "wall"
                };

                if (useInteriorJustification)
                {
                    if (e.RoomIndices.Count > 1)
                    {
                        // Muro compartido: El eje es el centro
                        syncEdge.Justification = "center";
                    }
                    else
                    {
                        // Muro exterior: 
                        // El polígono es CCW. El interior del local está a la IZQUIERDA del sentido de giro.
                        // Queremos que el muro crezca hacia AFUERA (DERECHA del sentido de giro).
                        // Si el sentido de giro coincide con NodeA -> NodeB, Justificación = "right".
                        // Si el sentido de giro es NodeB -> NodeA, Justificación = "left".
                        
                        int roomIdx = e.RoomIndices[0];
                        bool isForward = e.RoomDirections[roomIdx];
                        syncEdge.Justification = isForward ? "right" : "left";
                    }
                }
                else
                {
                    syncEdge.Justification = "center";
                }

                syncEdges.Add(syncEdge);
            }

            return new SyncGraph
            {
                Nodes = nodes.Values.ToList(),
                Edges = syncEdges
            };
        }

        private static List<Point2D> EnsureCCW(List<Point2D> points)
        {
            var poly = points.ToList();
            if (poly.Count < 3) return poly;

            // Shoelace area
            double area = 0;
            for (int i = 0; i < poly.Count - 1; i++)
                area += (poly[i + 1].X - poly[i].X) * (poly[i + 1].Y + poly[i].Y);

            // En CAD/GDI con Y hacia arriba, Area > 0 es Clockwise.
            // Nosotros queremos Counter-Clockwise (Area < 0).
            if (area > 0)
            {
                poly.Reverse();
            }
            return poly;
        }

        private static string GetCoordKey(double x, double y)
        {
            long ix = (long)Math.Round(x / TOLERANCE);
            long iy = (long)Math.Round(y / TOLERANCE);
            return $"{ix}_{iy}";
        }
    }
}
