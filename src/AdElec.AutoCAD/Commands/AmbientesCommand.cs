using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AdElec.AutoCAD.Managers;
using AdElec.AutoCAD.Repositories;
using AdElec.UI.ViewModels;
using AdElec.UI.Views;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Commands
{
    public class AmbientesCommand
    {
        private const string BLOCK_IDLOCALES = "ID_LOCALES";
        private const string BLOCK_FILE_IDLOCALES = "Bloques_CAD\\ID_LOCALES.dwg";

        [CommandMethod("ADE_AMBIENTES")]
        public void TagAmbiente()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            // ── 1. Seleccionar polilínea cerrada ────────────────────────────
            var peo = new PromptEntityOptions("\nSeleccioná el contorno del recinto (polilínea cerrada): ");
            peo.SetRejectMessage("\nDebe ser una polilínea.");
            peo.AddAllowedClass(typeof(Polyline), true);

            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            double area;
            Point3d centroid;

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var poly = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForRead);

                if (!poly.Closed)
                {
                    ed.WriteMessage("\nLa polilínea debe estar cerrada. Operación cancelada.");
                    tr.Commit();
                    return;
                }

                area = poly.Area; // m² si el dibujo está en metros
                var ext = poly.GeometricExtents;
                centroid = new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) / 2,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) / 2,
                    0);
                tr.Commit();
            }

            // ── 2. Cargar opciones de UF (desde tableros guardados en el DWG) ──
            var panelRepo = new DwgPanelRepository();
            var ambienteRepo = new DwgAmbienteRepository();

            // UF = Location de los tableros + UFs existentes en ID_LOCALES
            var ufDesdePaneles = panelRepo.GetAllPanels()
                .Select(p => p.Location)
                .Where(l => !string.IsNullOrWhiteSpace(l));
            var ufExistentes = ambienteRepo.GetUFsDisponibles();

            var ufOptions = ufDesdePaneles
                .Concat(ufExistentes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(u => u)
                .ToList();

            if (ufOptions.Count == 0)
                ufOptions.Add("UF1"); // fallback mínimo

            // ── 3. Mostrar diálogo WPF ──────────────────────────────────────
            var vm = new AmbienteDialogViewModel(area, ufOptions);
            var dialog = new AmbienteDialog(vm);

            // ShowModalWindow es el mecanismo correcto para WPF en AutoCAD
            Application.ShowModalWindow(Application.MainWindow.Handle, dialog, false);

            if (!vm.Confirmed || vm.ResultadoAmbiente is null) return;

            var amb = vm.ResultadoAmbiente;

            // ── 4. Cargar bloque ID_LOCALES ─────────────────────────────────
            string blockPath = ResolveBlockPath(db, BLOCK_FILE_IDLOCALES);
            if (!BlockManager.EnsureBlockExists(BLOCK_IDLOCALES, blockPath))
            {
                ed.WriteMessage($"\n[!] No se encontró '{BLOCK_IDLOCALES}'. Ruta: {blockPath}");
                ed.WriteMessage("\n    Asegurate de que ID_LOCALES.dwg esté en Bloques_CAD\\");
                return;
            }

            // ── 5. Insertar bloque con atributos ────────────────────────────
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                if (!bt.Has(BLOCK_IDLOCALES))
                {
                    ed.WriteMessage("\nBloque ID_LOCALES no encontrado en el dibujo tras carga. Abortando.");
                    tr.Abort();
                    return;
                }

                var blockId = bt[BLOCK_IDLOCALES];
                var br = new BlockReference(centroid, blockId);
                space.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);

                var blockDef = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                foreach (ObjectId entId in blockDef)
                {
                    var ent = tr.GetObject(entId, OpenMode.ForRead);
                    if (ent is not AttributeDefinition attDef || attDef.Constant) continue;

                    var attRef = new AttributeReference();
                    attRef.SetAttributeFromBlock(attDef, br.BlockTransform);
                    attRef.Position = attDef.Position.TransformBy(br.BlockTransform);

                    attRef.TextString = attDef.Tag.ToUpperInvariant() switch
                    {
                        "01"    => amb.UF,
                        "55"    => amb.Planta,
                        "LOCAL" => amb.TipoDisplay,
                        "AREA"  => $"{amb.AreaM2:F2}m²",
                        _       => attDef.TextString,
                    };

                    br.AttributeCollection.AppendAttribute(attRef);
                    tr.AddNewlyCreatedDBObject(attRef, true);
                }

                // Visibilidad "CON NUM" por defecto (muestra el número de UF)
                if (br.IsDynamicBlock)
                {
                    foreach (DynamicBlockReferenceProperty prop in br.DynamicBlockReferencePropertyCollection)
                    {
                        if (prop.PropertyName == "Visibilidad1" && !prop.ReadOnly)
                        {
                            try { prop.Value = "CON NUM"; } catch { /* ignorar */ }
                            break;
                        }
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\n✓ Recinto '{amb.TipoDisplay}' ({amb.AreaM2:F2} m²) → UF: {amb.UF} | {amb.Planta}");
            ed.WriteMessage("\n  Usá 'Calcular AEA 90364' en la paleta para enviar todos los recintos al motor.");
        }

        private static string ResolveBlockPath(Database db, string relativeBlockPath)
        {
            string dwgDir = Path.GetDirectoryName(db.Filename) ?? "";
            if (!string.IsNullOrEmpty(dwgDir))
            {
                string c = Path.Combine(dwgDir, relativeBlockPath);
                if (File.Exists(c)) return c;
            }

            string asmDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            string dir = asmDir;
            for (int i = 0; i < 6; i++)
            {
                string c = Path.Combine(dir, relativeBlockPath);
                if (File.Exists(c)) return c;
                string? parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            return Path.Combine(asmDir, relativeBlockPath);
        }
    }
}
