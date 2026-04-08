using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AdElec.Core.AeaMotor.Dtos;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Repositories
{
    /// <summary>
    /// Escanea los bloques eléctricos del dibujo (I.E-AD-07, I.E-AD-09, I.E-AD-09.02, I.E-AD-01)
    /// y devuelve los puntos eléctricos agrupados por room (room_id del bloque ID_LOCALES más cercano).
    /// </summary>
    public class DwgElectricoRepository
    {
        // Mapa bloque → tipo ProposalPoint
        private static readonly Dictionary<string, string> BlockTipoMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["I.E-AD-07"]    = "IUG",
            ["I.E-AD-08"]    = "IUE",
            ["I.E-AD-09"]    = "TUG",
            ["I.E-AD-09.02"] = "TUE",
            ["I.E-AD-01"]    = "SWITCH",
        };

        /// <summary>
        /// Devuelve la lista de SyncRoom con sus puntos eléctricos para el tablero indicado.
        /// Los rooms vienen de los bloques ID_LOCALES; los puntos de los bloques eléctricos con D=tablero.
        /// </summary>
        public List<SyncRoom> GetRoomsConPuntos(string tableroName)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return [];

            var db = doc.Database;
            using var tr = db.TransactionManager.StartOpenCloseTransaction();

            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

            // ── 1. Leer todos los bloques ID_LOCALES (rooms) ─────────────────
            var rooms = new List<(string RoomId, string UF, string Name, string Type, double Area,
                                   List<Dictionary<string, double>> Poly,
                                   Dictionary<string, double> Centroid,
                                   Point2d InsertPt)>();

            foreach (ObjectId entId in space)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead);
                if (ent is not BlockReference br) continue;

                string bName = br.IsDynamicBlock
                    ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                    : ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;

                if (!string.Equals(bName, "ID_LOCALES", StringComparison.OrdinalIgnoreCase)) continue;

                var atts = ReadAtts(tr, br);
                var insertPt = new Point2d(br.Position.X, br.Position.Y);

                // Recuperar polígono vía XData
                var polyPoints = new List<Dictionary<string, double>>();
                double cx = br.Position.X, cy = br.Position.Y;

                const string APP_ID = "ADE_SYNC_LINK";
                var xdata = br.GetXDataForApplication(APP_ID);
                if (xdata != null)
                {
                    var vals = xdata.AsArray();
                    if (vals.Length > 1 && vals[1].TypeCode == (short)DxfCode.ExtendedDataHandle)
                    {
                        string handleStr = (string)vals[1].Value;
                        if (db.TryGetObjectId(new Handle(Convert.ToInt64(handleStr, 16)), out ObjectId polyId))
                        {
                            var poly = tr.GetObject(polyId, OpenMode.ForRead) as Polyline;
                            if (poly != null)
                            {
                                int n = poly.NumberOfVertices;
                                for (int i = 0; i < n; i++)
                                {
                                    var v = poly.GetPoint2dAt(i);
                                    polyPoints.Add(new Dictionary<string, double>
                                        { ["x"] = Math.Round(v.X, 3), ["y"] = Math.Round(v.Y, 3) });
                                }

                                // Centroide shoelace (igual que AmbientesCommand)
                                double scx = 0, scy = 0, signedArea = 0;
                                for (int i = 0; i < n; i++)
                                {
                                    var p0 = poly.GetPoint2dAt(i);
                                    var p1 = poly.GetPoint2dAt((i + 1) % n);
                                    double cross = p0.X * p1.Y - p1.X * p0.Y;
                                    signedArea += cross;
                                    scx += (p0.X + p1.X) * cross;
                                    scy += (p0.Y + p1.Y) * cross;
                                }
                                signedArea /= 2.0;
                                if (Math.Abs(signedArea) > 1e-10)
                                {
                                    double factor = 1.0 / (6.0 * signedArea);
                                    cx = scx * factor;
                                    cy = scy * factor;
                                }
                                else if (n > 0)
                                {
                                    cx = polyPoints.Average(p => p["x"]);
                                    cy = polyPoints.Average(p => p["y"]);
                                }
                            }
                        }
                    }
                }

                string tipoDisplay = atts.GetValueOrDefault("LOCAL", "Otro");
                string roomTypeName = AdElec.Core.Models.TipoAmbienteInfo.DesdeNombre(tipoDisplay).ApiValue;
                double area = ParseDouble(atts.GetValueOrDefault("AREA", "0"));
                string uf = atts.GetValueOrDefault("01", "");
                string planta = atts.GetValueOrDefault("55", "PB");

                rooms.Add((
                    RoomId: $"room_{br.Handle}",    // ID estable basado en Handle del bloque
                    UF: uf,
                    Name: $"{tipoDisplay} — {planta}",
                    Type: roomTypeName,
                    Area: area,
                    Poly: polyPoints,
                    Centroid: new Dictionary<string, double>
                        { ["x"] = Math.Round(cx, 3), ["y"] = Math.Round(cy, 3) },
                    InsertPt: insertPt
                ));
            }

            // ── 2. Leer todos los bloques eléctricos con D=tablero ───────────
            var puntos = new List<(Point2d Pos, string Tipo, string CX, double POT, string? SwitchId)>();

            foreach (ObjectId entId in space)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead);
                if (ent is not BlockReference br) continue;

                string bName = br.IsDynamicBlock
                    ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                    : ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;

                if (!BlockTipoMap.TryGetValue(bName, out string? tipo)) continue;

                var atts = ReadAtts(tr, br);
                if (!string.Equals(atts.GetValueOrDefault("D", ""), tableroName, StringComparison.OrdinalIgnoreCase))
                    continue;

                string cx2 = atts.GetValueOrDefault("CX", "");
                if (string.IsNullOrEmpty(cx2)) continue;

                double.TryParse(atts.GetValueOrDefault("POT", "0").Replace(',', '.'),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double pot);

                string? switchId = atts.GetValueOrDefault("PX", null);
                if (string.IsNullOrEmpty(switchId)) switchId = atts.GetValueOrDefault("LLAVE-N°0", null);

                puntos.Add((new Point2d(br.Position.X, br.Position.Y), tipo, cx2, pot, switchId));
            }

            tr.Commit();

            // ── 3. Asignar cada punto al room más cercano ────────────────────
            var roomPuntos = rooms.ToDictionary(r => r.RoomId, _ => new List<ProposalPoint>());

            foreach (var (pos, tipo, cxId, pot, switchId) in puntos)
            {
                if (rooms.Count == 0) break;

                // Encontrar room cuyo centroide es más cercano
                string nearest = rooms
                    .OrderBy(r =>
                    {
                        double dx = r.Centroid["x"] - pos.X;
                        double dy = r.Centroid["y"] - pos.Y;
                        return dx * dx + dy * dy;
                    })
                    .First().RoomId;

                roomPuntos[nearest].Add(new ProposalPoint
                {
                    X = pos.X,
                    Y = pos.Y,
                    Type = tipo,
                    CircuitId = cxId,
                    SwitchId = string.IsNullOrEmpty(switchId) ? null : switchId,
                    PowerVa = pot,
                });
            }

            // ── 4. Construir SyncRoom list ────────────────────────────────────
            return rooms.Select(r => new SyncRoom
            {
                Id            = r.RoomId,
                Name          = r.Name,
                Type          = r.Type,
                Area          = r.Area,
                PolygonPoints = r.Poly,
                Centroid      = r.Centroid,
                Points        = roomPuntos[r.RoomId],
            }).ToList();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static Dictionary<string, string> ReadAtts(Transaction tr, BlockReference br)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId attId in br.AttributeCollection)
            {
                var a = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                dict[a.Tag] = a.TextString?.Trim() ?? "";
            }
            return dict;
        }

        private static double ParseDouble(string text)
        {
            string clean = System.Text.RegularExpressions.Regex.Replace(text, @"[^\d.,]", "").Replace(',', '.');
            return double.TryParse(clean, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
        }
    }
}
