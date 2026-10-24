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
using Panel = AdElec.Core.Models.Panel;

namespace AdElec.AutoCAD.Commands
{
    public class RecargarCommand
    {
        // Mapa de bloques conocidos a tipo de circuito
        private static readonly Dictionary<string, string> BlockTipoMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["I.E-AD-07"]    = "IUG",   // Iluminación techo
            ["I.E-AD-08"]    = "IUE",   // Iluminación pared
            ["I.E-AD-09"]    = "TUG",   // Toma general
            ["I.E-AD-09.02"] = "TUE",   // Toma especial
            ["I.E-AD-01"]    = "SWITCH",// Llave / Interruptor
        };

        [CommandMethod("ADE_RECARGAR")]
        public void RecargarCircuitos()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed  = doc.Editor;
            var db  = doc.Database;

            var panelRepo = new DwgPanelRepository();

            ed.WriteMessage("\n══ ADE_RECARGAR: Sincronizando Tableros y Circuitos del DWG ══");

            // Sincronizar e interpretar todos los tableros y circuitos del dibujo
            var panels = SincronizarTablerosDesdeDwg(db, panelRepo);

            if (panels.Count == 0)
            {
                ed.WriteMessage("\n[!] No se encontraron tableros ni bocas con atributo 'D' en el dibujo.");
                ed.WriteMessage("\n    Asegurate de colocar bocas con tablero asignado o un bloque ID_TABLERO.");
                return;
            }

            ed.WriteMessage($"\n✓ Sincronizados {panels.Count} tablero(s) desde el plano:");
            foreach (var p in panels)
            {
                double kva = Math.Round(p.TotalLoadVA() / 1000.0, 2);
                ed.WriteMessage($"\n  - {p.Name} ({p.Location}): {p.Circuits.Count} circuito(s), Carga: {p.TotalLoadVA():F0} VA ({kva:F2} kVA)");
                foreach (var c in p.Circuits)
                {
                    ed.WriteMessage($"\n      * {c.Name} [{c.Type}]: {c.LoadVA:F0} VA, {c.BreakerAmps}A ({c.WireSectionMm2} mm²)");
                }
            }

            ed.WriteMessage("\n✓ Datos actualizados en la paleta lateral 'ADE_PANEL'.");
        }

        /// <summary>
        /// Interpreta y sincroniza de manera 100% autónoma todos los tableros, circuitos,
        /// bocas y potencias existentes en el dibujo sin requerir conexión a AEA-MOTOR.
        /// </summary>
        public static List<Panel> SincronizarTablerosDesdeDwg(Database db, DwgPanelRepository panelRepo)
        {
            var existingPanels = panelRepo.GetAllPanels();
            var panelDict = existingPanels.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

            // Estructura para acumular bocas por tablero y circuito:
            // tableroName -> circuitName -> lista de bocas (tipo, potencia)
            var tableroBocas = new Dictionary<string, Dictionary<string, List<(string Tipo, double Potencia)>>>(StringComparer.OrdinalIgnoreCase);

            // Tableros explícitos encontrados como bloques (ej. ID_TABLERO, I.E-AD-04)
            var tablerosEncontrados = new Dictionary<string, (string Ubicacion, int Fases, double Sup)>(StringComparer.OrdinalIgnoreCase);

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                foreach (ObjectId entId in space)
                {
                    var ent = tr.GetObject(entId, OpenMode.ForRead);
                    if (ent is not BlockReference br) continue;

                    string bName = br.IsDynamicBlock
                        ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                        : ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;

                    // 1. Detectar bloques de tablero en el plano (ID_TABLERO o I.E-AD-04)
                    if (string.Equals(bName, "ID_TABLERO", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(bName, "I.E-AD-04", StringComparison.OrdinalIgnoreCase) ||
                        bName.IndexOf("TABLERO", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string tabName = "";
                        string tabUbic = "";
                        int tabFases = 1;
                        double tabSup = 0;

                        foreach (ObjectId attId in br.AttributeCollection)
                        {
                            var a = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                            string tag = a.Tag?.ToUpperInvariant() ?? "";
                            string val = a.TextString?.Trim() ?? "";

                            if (tag == "TABLERO" || tag == "NOMBRE" || tag == "NAME" || tag == "ID") tabName = val;
                            else if (tag == "UBICACION" || tag == "UF" || tag == "LOCAL") tabUbic = val;
                            else if (tag == "FASES" || tag == "FASE") { if (val.Contains("3")) tabFases = 3; }
                            else if (tag == "AREA" || tag == "SUPERFICIE") { double.TryParse(val.Replace("m²", "").Trim(), out tabSup); }
                        }

                        if (!string.IsNullOrEmpty(tabName) && !tablerosEncontrados.ContainsKey(tabName))
                        {
                            tablerosEncontrados[tabName] = (tabUbic, tabFases, tabSup);
                        }
                    }

                    // 2. Detectar bocas eléctricas con atributos D y CX
                    string tipo = "";
                    if (BlockTipoMap.TryGetValue(bName, out string? mappedTipo)) tipo = mappedTipo;
                    else if (bName.IndexOf("TOMA", StringComparison.OrdinalIgnoreCase) >= 0) tipo = "TUG";
                    else if (bName.IndexOf("LUM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             bName.IndexOf("CENTRO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             bName.IndexOf("BRAZO", StringComparison.OrdinalIgnoreCase) >= 0) tipo = "IUG";

                    string attrD = "";
                    string attrCX = "";
                    double pot = 0;

                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        var a = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                        string tag = a.Tag?.ToUpperInvariant() ?? "";
                        string val = a.TextString?.Trim() ?? "";

                        if (tag == "D" || tag == "TABLERO") attrD = val;
                        else if (tag == "CX" || tag == "CIRCUITO") attrCX = val;
                        else if (tag == "POT" || tag == "POTENCIA")
                        {
                            string clean = val.Replace("VA", "").Replace("va", "").Replace("W", "").Trim();
                            double.TryParse(clean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pot);
                        }
                    }

                    if (string.IsNullOrEmpty(attrD) || string.IsNullOrEmpty(attrCX)) continue;

                    // Si no determinó tipo por nombre de bloque, inferir por prefijo de CX o default
                    if (string.IsNullOrEmpty(tipo))
                    {
                        if (attrCX.IndexOf("L", StringComparison.OrdinalIgnoreCase) >= 0 || attrCX.IndexOf("IL", StringComparison.OrdinalIgnoreCase) >= 0) tipo = "IUG";
                        else if (attrCX.IndexOf("T", StringComparison.OrdinalIgnoreCase) >= 0) tipo = "TUG";
                        else tipo = "IUG";
                    }

                    // Potencias reglamentarias mínimas según AEA 90364
                    if (pot <= 0)
                    {
                        if (tipo == "IUG" || tipo == "IUE") pot = 60;   // 60 VA por boca
                        else if (tipo == "TUG") pot = 150;              // 150 VA estimado por boca
                        else if (tipo == "TUE") pot = 500;              // 500 VA
                        else pot = 100;
                    }

                    if (!tableroBocas.TryGetValue(attrD, out var circDict))
                    {
                        circDict = new Dictionary<string, List<(string Tipo, double Potencia)>>(StringComparer.OrdinalIgnoreCase);
                        tableroBocas[attrD] = circDict;
                    }

                    if (!circDict.TryGetValue(attrCX, out var bocasList))
                    {
                        bocasList = new List<(string Tipo, double Potencia)>();
                        circDict[attrCX] = bocasList;
                    }

                    bocasList.Add((tipo, pot));
                }

                tr.Commit();
            }

            // 3. Unificar todos los tableros encontrados
            var todosLosTablerosNombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in panelDict.Keys) todosLosTablerosNombres.Add(k);
            foreach (var k in tableroBocas.Keys) todosLosTablerosNombres.Add(k);
            foreach (var k in tablerosEncontrados.Keys) todosLosTablerosNombres.Add(k);

            var resultadoPanels = new List<Panel>();

            foreach (var tName in todosLosTablerosNombres)
            {
                if (!panelDict.TryGetValue(tName, out var p))
                {
                    p = new Panel
                    {
                        Name = tName,
                        Location = tablerosEncontrados.TryGetValue(tName, out var info) && !string.IsNullOrEmpty(info.Ubicacion) ? info.Ubicacion : tName,
                        PhaseCount = tablerosEncontrados.TryGetValue(tName, out var infoF) ? infoF.Fases : 1,
                        SuperficieCubiertaM2 = tablerosEncontrados.TryGetValue(tName, out var infoS) ? infoS.Sup : 0,
                        MainBreakerAmps = 32
                    };
                    panelDict[tName] = p;
                }

                // Si encontramos circuitos en el plano para este tablero, actualizarlos con precisión
                if (tableroBocas.TryGetValue(tName, out var circDict) && circDict.Count > 0)
                {
                    var updatedCircuits = new ObservableCollection<Circuit>();

                    foreach (var kvp in circDict.OrderBy(c => c.Key))
                    {
                        string cName = kvp.Key;
                        var bocas = kvp.Value;

                        // Determinar tipo predominante del circuito
                        bool esEspecial = bocas.Any(b => b.Tipo == "TUE" || b.Tipo == "IUE");
                        bool esLuz = bocas.Count(b => b.Tipo == "IUG" || b.Tipo == "IUE") >= bocas.Count(b => b.Tipo == "TUG" || b.Tipo == "TUE");

                        string finalTipo;
                        if (esEspecial) finalTipo = esLuz ? "IUE" : "TUE";
                        else finalTipo = esLuz ? "IUG" : "TUG";

                        // Sumar potencia total
                        double loadVa = bocas.Sum(b => b.Potencia);

                        // Si es TUG y la suma dio baja, el mínimo simultáneo de circuito TUG según norma es 2200 VA
                        if (finalTipo == "TUG" && loadVa < 2200 && bocas.Count > 0)
                        {
                            loadVa = Math.Max(loadVa, 2200);
                        }
                        else if (finalTipo == "IUG" && loadVa < 150 && bocas.Count > 0)
                        {
                            loadVa = Math.Max(loadVa, 150);
                        }

                        double breaker = (finalTipo == "IUG") ? 10 : (finalTipo == "TUG" ? 16 : 20);
                        double wire = (finalTipo == "IUG") ? 1.5 : (finalTipo == "TUG" ? 2.5 : 4.0);

                        updatedCircuits.Add(new Circuit
                        {
                            Name = cName,
                            Type = finalTipo,
                            LoadVA = loadVa,
                            BreakerAmps = breaker,
                            WireSectionMm2 = wire,
                            Phase = 'R'
                        });
                    }

                    p.Circuits = updatedCircuits;
                }

                // Guardar en el DWG
                panelRepo.SavePanel(p);
                resultadoPanels.Add(p);
            }

            return resultadoPanels;
        }
    }
}
