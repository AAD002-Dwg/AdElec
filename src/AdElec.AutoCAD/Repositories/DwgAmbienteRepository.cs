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
                    UF = atts.GetValueOrDefault("01", ""),
                    Planta = atts.GetValueOrDefault("55", "PB"),
                    TipoDisplay = atts.GetValueOrDefault("LOCAL", ""),
                    TipoApi = TipoAmbienteInfo.DesdeNombre(atts.GetValueOrDefault("LOCAL", "")).ApiValue,
                    AreaM2 = ParseArea(atts.GetValueOrDefault("AREA", "0")),
                };

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
    }
}
