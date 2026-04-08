using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AdElec.Core.Interfaces;
using AdElec.Core.Models;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Repositories
{
    /// <summary>
    /// Lee los bloques ID_LOCALES del dibujo activo y los convierte en objetos <see cref="Ambiente"/>.
    /// El bloque tiene 4 atributos: 01 (UF), 55 (Planta), LOCAL (tipo display), AREA (m²).
    /// </summary>
    public class DwgAmbienteRepository : IAmbienteRepository
    {
        private const string BLOCK_NAME = "ID_LOCALES";

        public List<Ambiente> GetAmbientesParaUF(string uf)
        {
            return LeerTodosLosAmbientes()
                .Where(a => string.Equals(a.UF, uf, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public List<string> GetUFsDisponibles()
        {
            return LeerTodosLosAmbientes()
                .Select(a => a.UF)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(u => u)
                .ToList();
        }

        public List<Ambiente> GetAllAmbientes()
        {
            return LeerTodosLosAmbientes();
        }

        private List<Ambiente> LeerTodosLosAmbientes()
        {
            var result = new List<Ambiente>();
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return result;

            var db = doc.Database;

            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

            foreach (ObjectId entId in space)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead);
                if (ent is not BlockReference br) continue;

                // Resolver nombre real (dinámico o estático)
                string blockName = br.IsDynamicBlock
                    ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                    : ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;

                if (!string.Equals(blockName, BLOCK_NAME, StringComparison.OrdinalIgnoreCase))
                    continue;

                var atts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    var attRef = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                    atts[attRef.Tag] = attRef.TextString?.Trim() ?? "";
                }

                var ambiente = new Ambiente
                {
                    Handle     = br.Handle.ToString(),
                    UF         = atts.GetValueOrDefault("01", ""),
                    Planta     = atts.GetValueOrDefault("55", "PB"),
                    TipoDisplay = atts.GetValueOrDefault("LOCAL", ""),
                    TipoApi    = TipoAmbienteInfo.DesdeNombre(atts.GetValueOrDefault("LOCAL", "")).ApiValue,
                    AreaM2     = ParseArea(atts.GetValueOrDefault("AREA", "0")),
                    EspesorMuro = ParseDouble(atts.GetValueOrDefault("ESP", "0.15")),
                };

                // ── Recuperar Poligono vía XData ──────────────────────────────
                const string APP_ID = "ADE_SYNC_LINK";
                var xdata = br.GetXDataForApplication(APP_ID);
                if (xdata != null)
                {
                    var values = xdata.AsArray();
                    if (values.Length > 1 && values[1].TypeCode == (short)DxfCode.ExtendedDataHandle)
                    {
                        string handleStr = (string)values[1].Value;
                        if (db.TryGetObjectId(new Handle(Convert.ToInt64(handleStr, 16)), out ObjectId polyId))
                        {
                            var poly = tr.GetObject(polyId, OpenMode.ForRead) as Polyline;
                            if (poly != null)
                            {
                                ambiente.PolygonPoints = ExtractVertices(poly);
                            }
                        }
                    }
                }

                result.Add(ambiente);
            }

            tr.Commit();
            return result;
        }

        /// <summary>
        /// Parsea el atributo AREA desde el formato "12.50m²" o "12,50 m2" a double.
        /// </summary>
        private static double ParseArea(string areaText)
        {
            if (string.IsNullOrWhiteSpace(areaText)) return 0;
            // Eliminar todo excepto dígitos, punto y coma
            string clean = Regex.Replace(areaText, @"[^\d.,]", "").Replace(',', '.');
            return double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }

        private static double ParseDouble(string text)
        {
            string clean = text.Replace(',', '.');
            return double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0.15;
        }

        private static List<Point2D> ExtractVertices(Polyline poly)
        {
            var points = new List<Point2D>();
            int count = poly.NumberOfVertices;
            for (int i = 0; i < count; i++)
            {
                var p = poly.GetPoint2dAt(i);
                points.Add(new Point2D(p.X, p.Y));
            }
            return points;
        }
    }
}
