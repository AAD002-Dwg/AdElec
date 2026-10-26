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
using System.IO;
using System.Text;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Panel = AdElec.Core.Models.Panel;
using Color = Autodesk.AutoCAD.Colors.Color;

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

            var elecRepo = new DwgElectricoRepository();
            var syncRooms = elecRepo.GetRoomsConPuntos(panel?.Name ?? "");

            ed.WriteMessage("\n=======================================================================");
            ed.WriteMessage($"\n  AUDITORÍA ESPACIAL DE BOCAS — AEA 770 (Grado {gradoDto.Grado.ToUpper()})");
            ed.WriteMessage("\n=======================================================================\n");

            if (syncRooms == null || syncRooms.Count == 0)
            {
                ed.WriteMessage("\n[AD-ELEC] ℹ️ No se detectaron recintos con bloques ID_LOCALES en el plano.");
                ed.WriteMessage("\n          Ejecutá ADE_DETECTAR_AMBIENTES para escanear y vincular los locales.\n");
                return;
            }

            int conformes = 0;
            int observados = 0;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                EnsureLayer(db, tr, "ELEC-REVISIONES", 1); // 1 = Rojo

                foreach (var r in syncRooms)
                {
                    var dto = new AeaAmbienteDto
                    {
                        Id = r.Id,
                        Nombre = r.Name,
                        Tipo = r.Type,
                        SuperficieM2 = r.Area
                    };

                    try
                    {
                        var req = AeaBridge.CalcularPuntosMinimos(dto, gradoDto.Grado);
                        int cantIug = r.Points.Count(p => string.Equals(p.Type, "IUG", StringComparison.OrdinalIgnoreCase));
                        int cantTug = r.Points.Count(p => string.Equals(p.Type, "TUG", StringComparison.OrdinalIgnoreCase));

                        int faltaIug = Math.Max(0, req.IugMinimo - cantIug);
                        int faltaTug = Math.Max(0, req.TugMinimo - cantTug);

                        if (faltaIug == 0 && faltaTug == 0)
                        {
                            ed.WriteMessage($"\n✅ {r.Name} ({r.Area:F1} m²): IUG = {cantIug}/{req.IugMinimo} | TUG = {cantTug}/{req.TugMinimo} [OK]");
                            conformes++;
                        }
                        else
                        {
                            ed.WriteMessage($"\n❌ {r.Name} ({r.Area:F1} m²):");
                            ed.WriteMessage($"\n   - IUG: tiene {cantIug}, exige mín {req.IugMinimo} " + (faltaIug > 0 ? $"(Faltan {faltaIug})" : "✅"));
                            ed.WriteMessage($"\n   - TUG: tiene {cantTug}, exige mín {req.TugMinimo} " + (faltaTug > 0 ? $"(Faltan {faltaTug})" : "✅"));
                            observados++;

                            // Si tiene centroide, dibujar marcador en layer ELEC-REVISIONES
                            if (r.Centroid.TryGetValue("x", out double cx) && r.Centroid.TryGetValue("y", out double cy))
                            {
                                var circle = new Circle(new Point3d(cx, cy, 0), Vector3d.ZAxis, 0.4);
                                circle.Layer = "ELEC-REVISIONES";
                                circle.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
                                space.AppendEntity(circle);
                                tr.AddNewlyCreatedDBObject(circle, true);

                                var txt = new MText();
                                txt.Location = new Point3d(cx, cy + 0.5, 0);
                                txt.Contents = $"\\C1;⚠️ {r.Name}\\P" +
                                               (faltaIug > 0 ? $"Falta {faltaIug} IUG\\P" : "") +
                                               (faltaTug > 0 ? $"Falta {faltaTug} TUG" : "");
                                txt.TextHeight = 0.25;
                                txt.Layer = "ELEC-REVISIONES";
                                space.AppendEntity(txt);
                                tr.AddNewlyCreatedDBObject(txt, true);
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n⚠️ {r.Name}: error al auditar: {ex.Message}");
                        observados++;
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage("\n-----------------------------------------------------------------------");
            ed.WriteMessage($"\n  Resultado: {conformes} recintos conformes | {observados} observados con marcas de revisión");
            ed.WriteMessage("\n=======================================================================\n");
        }

        [CommandMethod("ADE_EXPORTAR_PLANILLA")]
        public void ExportarPlanillaCsv()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            var panelRepo = new DwgPanelRepository();
            var panels = panelRepo.GetAllPanels();
            if (panels == null || panels.Count == 0)
            {
                ed.WriteMessage("\n[AD-ELEC] ⚠️ No hay tableros registrados para exportar.");
                return;
            }

            Panel panel = panels[0];
            if (panels.Count > 1)
            {
                var pko = new PromptKeywordOptions("\nSeleccioná el tablero a exportar:");
                foreach (var p in panels) pko.Keywords.Add(p.Name.Replace(" ", "_"));
                pko.Keywords.Default = panels[0].Name.Replace(" ", "_");
                var pkr = ed.GetKeywords(pko);
                if (pkr.Status == PromptStatus.OK)
                {
                    string sel = pkr.StringResult.Replace("_", " ");
                    panel = panels.FirstOrDefault(p => p.Name.Equals(sel, StringComparison.OrdinalIgnoreCase)) ?? panels[0];
                }
            }

            string dir = Path.GetDirectoryName(doc.Name) ?? "";
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                dir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
            string defaultPath = Path.Combine(dir, $"{panel.Name}_Planilla_Cargas_AEA.csv");

            try
            {
                var diags = panel.ValidarNormativaAea();
                var gradoDto = AeaBridge.CalcularGrado(panel.SuperficieCubiertaM2, 0);

                var sb = new StringBuilder();
                sb.AppendLine($"# PLANILLA DE CARGAS Y RESUMEN DE POTENCIA - TABLERO: {panel.Name}");
                sb.AppendLine($"# Reglamentación: AEA 90364 ({panel.TipoTablero}) | Grado: {gradoDto.Grado.ToUpper()} | Superficie: {panel.SuperficieCubiertaM2:F1} m2");
                sb.AppendLine("Circuito,Tipo,Destino,Tension_V,Bocas,Potencia_VA,Ib_A,In_A,Seccion_mm2,Longitud_m,DeltaV_pct,Estado_AEA");

                double totalVA = 0;
                foreach (var circ in panel.Circuits)
                {
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
                    string estado = tieneError ? "OBSERVADO" : "CUMPLE";

                    sb.AppendLine($"\"{circ.Name}\",\"{circ.Type}\",\"{circ.Destinations}\",{tension},{circ.ArtefactCount},{circ.LoadVA:F0},{ib:F2},{circ.BreakerAmps:F0},{circ.WireSectionMm2:F1},{(circ.LengthMeters > 0 ? circ.LengthMeters.ToString("F1") : "-")},{(circ.LengthMeters > 0 ? dv.ToString("F2") : "-")},\"{estado}\"");
                }

                double fs = panel.SimultaneityFactor > 0 ? panel.SimultaneityFactor : 0.8;
                double potSimultanea = totalVA * fs;
                double ig = panel.PhaseCount >= 3
                    ? potSimultanea / (Math.Sqrt(3) * 380 * (panel.PowerFactor > 0 ? panel.PowerFactor : 0.9))
                    : potSimultanea / (220 * (panel.PowerFactor > 0 ? panel.PowerFactor : 0.9));

                sb.AppendLine();
                sb.AppendLine($"# TOTAL INSTALADO: {totalVA:F0} VA");
                sb.AppendLine($"# FACTOR SIMULTANEIDAD: {fs:F2}");
                sb.AppendLine($"# POTENCIA SIMULTANEA: {potSimultanea:F0} VA");
                sb.AppendLine($"# CORRIENTE DE ACOMETIDA: {ig:F1} A");

                File.WriteAllText(defaultPath, sb.ToString(), Encoding.UTF8);
                ed.WriteMessage($"\n[AD-ELEC] ✅ Planilla exportada con éxito a:\n          {defaultPath}\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[AD-ELEC] ❌ Error al exportar planilla: {ex.Message}\n");
            }
        }

        private static void EnsureLayer(Database db, Transaction tr, string layerName, short colorIndex)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                var ltr = new LayerTableRecord
                {
                    Name = layerName,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex)
                };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }
    }
}
