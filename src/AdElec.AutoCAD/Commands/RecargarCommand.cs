using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AdElec.AutoCAD.Repositories;
using AdElec.Core.Models;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Commands
{
    public class RecargarCommand
    {
        // Mapa bloque → tipo de circuito
        private static readonly Dictionary<string, string> BlockTipoMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["I.E-AD-07"]    = "IUG",   // Iluminación techo
            ["I.E-AD-08"]    = "IUE",   // Iluminación pared
            ["I.E-AD-09"]    = "TUG",   // Toma general
            ["I.E-AD-09.02"] = "TUE",   // Toma especial
        };

        [CommandMethod("ADE_RECARGAR")]
        public void RecargarCircuitos()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            var panelRepo = new DwgPanelRepository();
            var panels = panelRepo.GetAllPanels();

            if (panels.Count == 0)
            {
                ed.WriteMessage("\n[AD-ELEC] No hay tableros guardados en este DWG. Usá ADE_PANEL primero.");
                return;
            }

            // Si hay más de un panel, pedir cuál recargar
            AdElec.Core.Models.Panel panel;
            if (panels.Count == 1)
            {
                panel = panels[0];
                ed.WriteMessage($"\nRecargando circuitos del tablero '{panel.Name}'...");
            }
            else
            {
                var nombres = string.Join(", ", panels.Select(p => p.Name));
                var pso = new PromptStringOptions($"\nTableros disponibles: {nombres}\nIngresá el tablero a recargar: ")
                {
                    AllowSpaces = true
                };
                var res = ed.GetString(pso);
                if (res.Status != PromptStatus.OK) return;

                string nombre = res.StringResult.Trim();
                panel = (AdElec.Core.Models.Panel)panels.FirstOrDefault(p =>
                    string.Equals(p.Name, nombre, StringComparison.OrdinalIgnoreCase))!;

                if (panel is null)
                {
                    ed.WriteMessage($"\nTablero '{nombre}' no encontrado.");
                    return;
                }
            }

            // Escanear bloques eléctricos con D = tablero
            var circuitMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // circuitId → tipo (IUG, TUG, TUE…)

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var bt    = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                foreach (ObjectId entId in space)
                {
                    var ent = tr.GetObject(entId, OpenMode.ForRead);
                    if (ent is not BlockReference br) continue;

                    // Resolver nombre real (bloques dinámicos)
                    string blockName = br.IsDynamicBlock
                        ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                        : ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;

                    if (!BlockTipoMap.TryGetValue(blockName, out string? tipo)) continue;

                    // Leer atributos D y CX
                    string attrD  = "";
                    string attrCX = "";
                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        var a = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                        if (string.Equals(a.Tag, "D",  StringComparison.OrdinalIgnoreCase)) attrD  = a.TextString?.Trim() ?? "";
                        if (string.Equals(a.Tag, "CX", StringComparison.OrdinalIgnoreCase)) attrCX = a.TextString?.Trim() ?? "";
                    }

                    if (!string.Equals(attrD, panel.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrEmpty(attrCX)) continue;

                    // Si el circuito ya existe con tipo IUG, no sobreescribir con TUG
                    if (!circuitMap.ContainsKey(attrCX))
                        circuitMap[attrCX] = tipo;
                    else if (tipo == "IUG" || tipo == "IUE")
                        circuitMap[attrCX] = tipo; // luz tiene prioridad
                }

                tr.Commit();
            }

            if (circuitMap.Count == 0)
            {
                ed.WriteMessage($"\nNo se encontraron bocas con D='{panel.Name}' en el dibujo.");
                return;
            }

            // Construir circuitos ordenados
            var circuits = circuitMap
                .OrderBy(kvp => kvp.Key)
                .Select(kvp =>
                {
                    bool esLuz = kvp.Value == "IUG" || kvp.Value == "IUE";
                    return new Circuit
                    {
                        Name           = kvp.Key,
                        Type           = kvp.Value,
                        BreakerAmps    = esLuz ? 10 : 16,
                        WireSectionMm2 = esLuz ? 1.5 : 2.5,
                    };
                })
                .ToList();

            panel.Circuits = new ObservableCollection<Circuit>(circuits);
            panelRepo.SavePanel(panel);

            ed.WriteMessage($"\n✓ {circuits.Count} circuito(s) recargados al tablero '{panel.Name}': " +
                            string.Join(", ", circuits.Select(c => $"{c.Name}({c.Type})")));
            ed.WriteMessage("\n  Refrescá la paleta o seleccioná el tablero nuevamente para ver los cambios.");
        }
    }
}
