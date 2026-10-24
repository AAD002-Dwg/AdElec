using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AdElec.AutoCAD.Repositories;
using AdElec.AutoCAD.Services;
using AdElec.Core.Models;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Panel = AdElec.Core.Models.Panel;

namespace AdElec.AutoCAD.Commands
{
    public class UnifilarCommand
    {
        [CommandMethod("ADE_UNIFILAR")]
        [CommandMethod("ADE_UNIF")]
        public void GenerarUnifilar()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n══ Generador Automático de Diagramas Unifilares (ADE_UNIFILAR) ══");

            var panelRepo = new DwgPanelRepository();
            var panels = panelRepo.GetAllPanels();

            // Si no hay tableros o no tienen circuitos, sincronizar automáticamente desde el DWG
            if (panels.Count == 0 || panels.All(p => p.Circuits.Count == 0))
            {
                ed.WriteMessage("\n[i] Sincronizando tableros y circuitos existentes en el plano...");
                panels = RecargarCommand.SincronizarTablerosDesdeDwg(db, panelRepo);
            }

            if (panels.Count == 0)
            {
                ed.WriteMessage("\n[!] No se encontraron tableros en el dibujo. Inserte un tablero con ADE_PANEL o coloque bocas con atributo 'D'.");
                return;
            }

            // Seleccionar tablero
            Panel selectedPanel;
            if (panels.Count == 1)
            {
                selectedPanel = panels[0];
                ed.WriteMessage($"\nTablero seleccionado: {selectedPanel.Name} ({selectedPanel.Circuits.Count} circuitos)");
            }
            else
            {
                var pko = new PromptKeywordOptions("\nSeleccione tablero a generar: ");
                foreach (var p in panels)
                {
                    // AutoCAD keywords no admiten espacios
                    string kw = p.Name.Replace(" ", "_");
                    pko.Keywords.Add(kw);
                }
                pko.Keywords.Default = panels[0].Name.Replace(" ", "_");

                var kRes = ed.GetKeywords(pko);
                if (kRes.Status != PromptStatus.OK) return;

                string chosen = kRes.StringResult.Replace("_", " ");
                selectedPanel = panels.FirstOrDefault(p => string.Equals(p.Name, chosen, StringComparison.OrdinalIgnoreCase)) ?? panels[0];
            }

            // Opciones de proyecto para el cuadro de carga
            var pioUf = new PromptIntegerOptions($"\nCantidad de Unidades Funcionales (UF) <{selectedPanel.FunctionalUnitsCount}>: ")
            {
                LowerLimit = 1,
                DefaultValue = selectedPanel.FunctionalUnitsCount,
                UseDefaultValue = true
            };
            var resUf = ed.GetInteger(pioUf);
            if (resUf.Status == PromptStatus.OK) selectedPanel.FunctionalUnitsCount = resUf.Value;

            var pdoSim = new PromptDoubleOptions($"\nCoeficiente de Simultaneidad (%) <{(selectedPanel.SimultaneityFactor * 100):F0}>: ")
            {
                DefaultValue = selectedPanel.SimultaneityFactor * 100,
                UseDefaultValue = true
            };
            var resSim = ed.GetDouble(pdoSim);
            if (resSim.Status == PromptStatus.OK && resSim.Value > 0)
            {
                selectedPanel.SimultaneityFactor = Math.Clamp(resSim.Value, 1.0, 100.0) / 100.0;
            }

            // Solicitar punto de inserción
            var ppo = new PromptPointOptions("\nPunto de inserción para el Diagrama Unifilar (esquina inferior izquierda): ");
            var pRes = ed.GetPoint(ppo);
            if (pRes.Status != PromptStatus.OK) return;

            Point3d origin = pRes.Value;

            ed.WriteMessage("\n[i] Generando diagrama unifilar con bloques reglamentarios...");

            bool ok = UnifilarGeneratorService.Generate(selectedPanel, origin, out string msg);
            ed.WriteMessage($"\n{msg}\n");

            // Guardar cambios en el repositorio del DWG
            panelRepo.SavePanel(selectedPanel);
        }
    }
}
