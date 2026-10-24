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
            // tableroName -> circuitName -> lista de bocas (tipo, potencia, local)
            var tableroBocas = new Dictionary<string, Dictionary<string, List<(string Tipo, double Potencia, string Local)>>>(StringComparer.OrdinalIgnoreCase);

            // Tableros explícitos encontrados como bloques (ej. ID_TABLERO, I.E-AD-04)
            var tablerosEncontrados = new Dictionary<string, (string Ubicacion, int Fases, double Sup)>(StringComparer.OrdinalIgnoreCase);

            // Locales para inferir destinos de circuitos
            var locales = new List<(Autodesk.AutoCAD.Geometry.Point2d Pos, string Nombre)>();

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                // Paso 1: Recolectar locales y tableros
                foreach (ObjectId entId in space)
                {
                    var ent = tr.GetObject(entId, OpenMode.ForRead);
                    if (ent is not BlockReference br) continue;

                    string bName = br.IsDynamicBlock
                        ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                        : ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;

                    if (string.Equals(bName, "ID_LOCALES", StringComparison.OrdinalIgnoreCase))
                    {
                        string locName = "";
                        foreach (ObjectId attId in br.AttributeCollection)
                        {
                            var a = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                            if (string.Equals(a.Tag, "LOCAL", StringComparison.OrdinalIgnoreCase))
                                locName = a.TextString?.Trim() ?? "";
                        }
                        if (!string.IsNullOrEmpty(locName))
                            locales.Add((new Autodesk.AutoCAD.Geometry.Point2d(br.Position.X, br.Position.Y), locName));
                    }
                    else if (string.Equals(bName, "ID_TABLERO", StringComparison.OrdinalIgnoreCase) ||
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

                            if (tag == "TABLERO" || tag == "NOMBRE" || tag == "NAME" || tag == "ID" || tag == "TX-X") tabName = val;
                            else if (tag == "UBICACION" || tag == "UF" || tag == "LOCAL") tabUbic = val;
                            else if (tag == "FASES" || tag == "FASE") { if (val.Contains("3")) tabFases = 3; }
                            else if (tag == "AREA" || tag == "SUPERFICIE") { double.TryParse(val.Replace("m²", "").Trim(), out tabSup); }
                        }

                        if (string.IsNullOrEmpty(tabName)) tabName = "TD1";

                        if (!tablerosEncontrados.ContainsKey(tabName))
                        {
                            tablerosEncontrados[tabName] = (tabUbic, tabFases, tabSup);
                        }
                    }
                }

                // Paso 2: Recolectar bocas eléctricas
                foreach (ObjectId entId in space)
                {
                    var ent = tr.GetObject(entId, OpenMode.ForRead);
                    if (ent is not BlockReference br) continue;

                    string bName = br.IsDynamicBlock
                        ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                        : ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;

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

                    if (string.IsNullOrEmpty(attrCX)) continue;

                    // Si no tiene D asignado, asociar al tablero existente o default TD1
                    if (string.IsNullOrEmpty(attrD))
                    {
                        attrD = tablerosEncontrados.Keys.FirstOrDefault() ?? "TD1";
                    }

                    if (string.IsNullOrEmpty(tipo))
                    {
                        if (attrCX.IndexOf("L", StringComparison.OrdinalIgnoreCase) >= 0 || attrCX.IndexOf("IL", StringComparison.OrdinalIgnoreCase) >= 0) tipo = "IUG";
                        else if (attrCX.IndexOf("T", StringComparison.OrdinalIgnoreCase) >= 0) tipo = "TUG";
                        else tipo = "IUG";
                    }

                    if (pot <= 0)
                    {
                        if (tipo == "IUG" || tipo == "IUE") pot = 60;
                        else if (tipo == "TUG") pot = 150;
                        else if (tipo == "TUE") pot = 500;
                        else pot = 100;
                    }

                    // Encontrar local más cercano
                    string localBoca = "";
                    if (locales.Count > 0)
                    {
                        double bx = br.Position.X;
                        double by = br.Position.Y;
                        localBoca = locales
                            .OrderBy(l => { double dx = l.Pos.X - bx; double dy = l.Pos.Y - by; return dx * dx + dy * dy; })
                            .First().Nombre;
                    }

                    if (!tableroBocas.TryGetValue(attrD, out var circDict))
                    {
                        circDict = new Dictionary<string, List<(string Tipo, double Potencia, string Local)>>(StringComparer.OrdinalIgnoreCase);
                        tableroBocas[attrD] = circDict;
                    }

                    if (!circDict.TryGetValue(attrCX, out var bocasList))
                    {
                        bocasList = new List<(string Tipo, double Potencia, string Local)>();
                        circDict[attrCX] = bocasList;
                    }

                    bocasList.Add((tipo, pot, localBoca));
                }

                tr.Commit();
            }

            // 3. Unificar todos los tableros encontrados
            var todosLosTablerosNombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in panelDict.Keys) todosLosTablerosNombres.Add(k);
            foreach (var k in tableroBocas.Keys) todosLosTablerosNombres.Add(k);
            foreach (var k in tablerosEncontrados.Keys) todosLosTablerosNombres.Add(k);

            // Garantizar al menos un tablero base TD1 para que la interfaz nunca quede vacía
            if (todosLosTablerosNombres.Count == 0)
            {
                todosLosTablerosNombres.Add("TD1");
            }

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

                // Si encontramos circuitos en el plano para este tablero, actualizarlos preservando configuraciones previas
                if (tableroBocas.TryGetValue(tName, out var circDict) && circDict.Count > 0)
                {
                    var updatedCircuits = new ObservableCollection<Circuit>();

                    foreach (var kvp in circDict.OrderBy(c => c.Key))
                    {
                        string cName = kvp.Key;
                        var bocas = kvp.Value;

                        bool esEspecial = bocas.Any(b => b.Tipo == "TUE" || b.Tipo == "IUE");
                        bool esLuz = bocas.Count(b => b.Tipo == "IUG" || b.Tipo == "IUE") >= bocas.Count(b => b.Tipo == "TUG" || b.Tipo == "TUE");

                        string finalTipo;
                        if (esEspecial) finalTipo = esLuz ? "IUE" : "TUE";
                        else finalTipo = esLuz ? "IUG" : "TUG";

                        double loadVa = bocas.Sum(b => b.Potencia);

                        if (finalTipo == "TUG" && loadVa < 2200 && bocas.Count > 0)
                        {
                            loadVa = Math.Max(loadVa, 2200);
                        }
                        else if (finalTipo == "IUG" && loadVa < 150 && bocas.Count > 0)
                        {
                            loadVa = Math.Max(loadVa, 150);
                        }

                        // Buscar si el usuario ya tenía configurado este circuito previamente
                        var existingCircuit = p.Circuits.FirstOrDefault(c => string.Equals(c.Name, cName, StringComparison.OrdinalIgnoreCase));

                        double breaker = existingCircuit?.BreakerAmps ?? ((finalTipo == "IUG") ? 10 : (finalTipo == "TUG" ? 16 : 20));
                        double wire = existingCircuit?.WireSectionMm2 ?? ((finalTipo == "IUG") ? 1.5 : (finalTipo == "TUG" ? 2.5 : 4.0));
                        char phase = existingCircuit?.Phase ?? 'R';
                        string destinations = string.Join("\n", bocas.Select(b => b.Local).Where(l => !string.IsNullOrEmpty(l)).Distinct());

                        updatedCircuits.Add(new Circuit
                        {
                            Name = cName,
                            Type = finalTipo,
                            LoadVA = loadVa,
                            BreakerAmps = breaker,
                            WireSectionMm2 = wire,
                            Phase = phase,
                            PhaseString = p.PhaseCount == 3 ? phase.ToString() : "SN",
                            ArtefactCount = bocas.Count,
                            Destinations = !string.IsNullOrEmpty(destinations) ? destinations : (existingCircuit?.Destinations ?? "LOCAL"),
                            Curve = existingCircuit?.Curve ?? "C",
                            Poles = p.PhaseCount == 3 ? 4 : 2,
                            Norma = "IRAM 62267",
                            Description = (finalTipo == "IUG" || finalTipo == "IUE") ? "ILUMINACION" : "TOMAS"
                        });
                    }

                    p.Circuits = updatedCircuits;
                }

                // Garantizar reserva normativa
                AdElec.Core.Algorithms.AeaRules.EnsureReserveCircuit(p);

                // Guardar en el DWG
                panelRepo.SavePanel(p);
                resultadoPanels.Add(p);
            }

            return resultadoPanels;
        }
    }
}
