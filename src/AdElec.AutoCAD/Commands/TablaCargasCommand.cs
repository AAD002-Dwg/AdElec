using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AdElec.AutoCAD.Repositories;
using AdElec.Core.Aea;
using AdElec.Core.Models;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Panel = AdElec.Core.Models.Panel;

namespace AdElec.AutoCAD.Commands
{
    public class TablaCargasCommand
    {
        [CommandMethod("ADE_TABLA_CARGAS")]
        public void GenerarTablaCargas()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            var panelRepo = new DwgPanelRepository();
            var panels = panelRepo.GetAllPanels();

            if (panels == null || panels.Count == 0)
            {
                ed.WriteMessage("\n[AD-ELEC] ⚠️ No se encontraron tableros registrados en el dibujo.");
                ed.WriteMessage("\n          Abrí la paleta ADE_PANEL para crear o sincronizar tableros.\n");
                return;
            }

            Panel panel = panels[0];
            if (panels.Count > 1)
            {
                var pko = new PromptKeywordOptions("\nSeleccioná el tablero para generar la planilla:");
                foreach (var p in panels)
                {
                    pko.Keywords.Add(p.Name.Replace(" ", "_"));
                }
                pko.Keywords.Default = panels[0].Name.Replace(" ", "_");
                var pkr = ed.GetKeywords(pko);
                if (pkr.Status == PromptStatus.OK)
                {
                    string selected = pkr.StringResult.Replace("_", " ");
                    panel = panels.FirstOrDefault(p => p.Name.Equals(selected, StringComparison.OrdinalIgnoreCase)) ?? panels[0];
                }
            }

            var ppo = new PromptPointOptions($"\nSeleccioná el punto de inserción para la Planilla de Cargas [{panel.Name}]: ");
            var ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK) return;

            Point3d insertPt = ppr.Value;

            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    var table = new Table();
                    table.TableStyle = db.Tablestyle;
                    table.Position = insertPt;

                    // Validar con motor normativo AEA para obtener diagnósticos y grado
                    var diags = panel.ValidarNormativaAea();
                    var gradoDto = AeaBridge.CalcularGrado(panel.SuperficieCubiertaM2, 0);
                    string gradoStr = gradoDto.Grado.ToUpperInvariant();

                    int circuitCount = panel.Circuits.Count;
                    int numRows = circuitCount + 6; // Title, Subtitle, Header, Data rows, Summary rows
                    int numCols = 12;

                    table.SetSize(numRows, numCols);

                    // Configurar título y subtítulo
                    table.Cells[0, 0].TextString = $"PLANILLA DE CARGAS Y RESUMEN DE POTENCIA — TABLERO {panel.Name}";
                    table.Cells[0, 0].Alignment = CellAlignment.MiddleCenter;

                    table.Cells[1, 0].TextString = $"Reglamentación AEA 90364 ({panel.TipoTablero}) | Grado de Electrificación: {gradoStr} | Sup: {panel.SuperficieCubiertaM2:F1} m²";
                    table.Cells[1, 0].Alignment = CellAlignment.MiddleCenter;

                    // Encabezados de columna
                    string[] headers =
                    {
                        "Circ.", "Tipo", "Destino", "Fase / Tensión", "Bocas",
                        "Potencia [VA]", "Ib [A]", "In [A]", "Sección [mm²]",
                        "Long. [m]", "ΔV [%]", "Estado AEA"
                    };

                    for (int c = 0; c < numCols; c++)
                    {
                        table.Cells[2, c].TextString = headers[c];
                        table.Cells[2, c].Alignment = CellAlignment.MiddleCenter;
                    }

                    double totalVA = 0;
                    int row = 3;

                    for (int i = 0; i < circuitCount; i++)
                    {
                        var circ = panel.Circuits[i];
                        totalVA += circ.LoadVA;

                        double tension = panel.PhaseCount >= 3 ? 380 : 220;
                        double cosPhi = panel.PowerFactor > 0 ? panel.PowerFactor : 0.9;
                        double ib = 0;
                        if (circ.LoadVA > 0)
                        {
                            double den = panel.PhaseCount >= 3 ? (Math.Sqrt(3) * tension * cosPhi) : (tension * cosPhi);
                            ib = Math.Round(circ.LoadVA / den, 2);
                        }

                        double dv = 0;
                        if (circ.LengthMeters > 0)
                        {
                            double iCalc = ib > 0 ? ib : circ.BreakerAmps;
                            dv = Math.Round(AeaBridge.CalcularCaidaTension(circ.LengthMeters, iCalc, circ.WireSectionMm2, panel.PhaseCount >= 3 ? 3 : 2, cosPhi), 2);
                        }

                        bool tieneError = diags.Any(d => d.Elemento == circ.Name && d.Severidad.Equals("error", StringComparison.OrdinalIgnoreCase));
                        string estadoAea = tieneError ? "OBSERVADO" : "CUMPLE";

                        table.Cells[row, 0].TextString = circ.Name;
                        table.Cells[row, 1].TextString = circ.Type;
                        table.Cells[row, 2].TextString = circ.Destinations;
                        table.Cells[row, 3].TextString = $"{circ.PhaseString} ({tension}V)";
                        table.Cells[row, 4].TextString = circ.ArtefactCount.ToString();
                        table.Cells[row, 5].TextString = $"{circ.LoadVA:F0}";
                        table.Cells[row, 6].TextString = $"{ib:F1}";
                        table.Cells[row, 7].TextString = $"{circ.BreakerAmps:F0}";
                        table.Cells[row, 8].TextString = $"{circ.WireSectionMm2:F1}";
                        table.Cells[row, 9].TextString = circ.LengthMeters > 0 ? $"{circ.LengthMeters:F1}" : "-";
                        table.Cells[row, 10].TextString = circ.LengthMeters > 0 ? $"{dv:F2}%" : "-";
                        table.Cells[row, 11].TextString = estadoAea;

                        row++;
                    }

                    // Filas de Resumen
                    double fs = panel.SimultaneityFactor > 0 ? panel.SimultaneityFactor : 0.8;
                    double potSimultaneaVA = totalVA * fs;
                    double cosP = panel.PowerFactor > 0 ? panel.PowerFactor : 0.9;
                    double igAcometida = panel.PhaseCount >= 3
                        ? potSimultaneaVA / (Math.Sqrt(3) * 380 * cosP)
                        : potSimultaneaVA / (220 * cosP);

                    table.Cells[row, 0].TextString = "POTENCIA TOTAL INSTALADA:";
                    table.Cells[row, 5].TextString = $"{totalVA:F0} VA";

                    row++;
                    table.Cells[row, 0].TextString = $"FACTOR DE SIMULTANEIDAD (Fs): {fs:F2}  |  POTENCIA SIMULTÁNEA:";
                    table.Cells[row, 5].TextString = $"{potSimultaneaVA:F0} VA";

                    row++;
                    table.Cells[row, 0].TextString = $"INTENSIDAD DE CABECERA ESTIMADA: {igAcometida:F1} A  |  PIA RECOMENDADA: {panel.MainBreakerAmps} A";

                    table.GenerateLayout();
                    space.AppendEntity(table);
                    tr.AddNewlyCreatedDBObject(table, true);

                    tr.Commit();
                }

                ed.WriteMessage($"\n[AD-ELEC] ✅ Planilla de Cargas del tablero '{panel.Name}' insertada con éxito en el plano DWG.\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[AD-ELEC] ❌ Error al generar la tabla de cargas: {ex.Message}\n");
            }
        }

        [CommandMethod("ADE_AUDITORIA_BOCAS")]
        public void AuditarBocasNormativas()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            var panelRepo = new DwgPanelRepository();
            var panels = panelRepo.GetAllPanels();
            var panel = panels.FirstOrDefault();

            double sup = panel?.SuperficieCubiertaM2 ?? 70.0;
            var gradoDto = AeaBridge.CalcularGrado(sup, 0);

            var ambienteRepo = new DwgAmbienteRepository();
            var ambientes = ambienteRepo.GetAllAmbientes();

            ed.WriteMessage("\n=======================================================");
            ed.WriteMessage($"\n  AUDITORÍA DE BOCAS MÍNIMAS — AEA 770 (Grado {gradoDto.Grado.ToUpper()})");
            ed.WriteMessage("\n=======================================================\n");

            if (ambientes == null || ambientes.Count == 0)
            {
                ed.WriteMessage("\n[AD-ELEC] ℹ️ No se detectaron recintos ID_LOCALES en el plano.");
                ed.WriteMessage("\n          Ejecutá ADE_DETECTAR_AMBIENTES para escanear los locales.\n");
                return;
            }

            int conformes = 0;
            int observados = 0;

            foreach (var amb in ambientes)
            {
                var dto = new AeaAmbienteDto
                {
                    Id = amb.Handle,
                    Nombre = amb.TipoDisplay,
                    Tipo = amb.TipoApi,
                    SuperficieM2 = amb.AreaM2
                };

                try
                {
                    var req = AeaBridge.CalcularPuntosMinimos(dto, gradoDto.Grado);
                    ed.WriteMessage($"\n📍 {amb.TipoDisplay} ({amb.AreaM2:F1} m²):");
                    ed.WriteMessage($"\n   - Mínimo normativo: {req.IugMinimo} IUG | {req.TugMinimo} TUG");
                    conformes++;
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n   ⚠️ No se pudo calcular: {ex.Message}");
                    observados++;
                }
            }

            ed.WriteMessage("\n-------------------------------------------------------");
            ed.WriteMessage($"\n  Total analizados: {ambientes.Count} recintos ({conformes} auditados, {observados} advertencias)");
            ed.WriteMessage("\n=======================================================\n");
        }
    }
}
