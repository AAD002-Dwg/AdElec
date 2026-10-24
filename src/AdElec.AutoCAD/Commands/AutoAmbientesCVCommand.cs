using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AdElec.AutoCAD.Geometry;
using AdElec.AutoCAD.Managers;
using AdElec.Core.Models;
using AdElec.UI.ViewModels;
using AdElec.UI.Views;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Commands
{
    public class AutoAmbientesCVCommand
    {
        private const string BLOCK_IDLOCALES = "ID_LOCALES";
        private const string BLOCK_FILE_IDLOCALES = "blocks\\ID_LOCALES.dwg";
        private const string APP_ID = "ADE_SYNC_LINK";

        private readonly Dictionary<ObjectId, Autodesk.AutoCAD.Colors.Color> _coloresOriginales = new();
        private ObjectId _highlightedPolyId = ObjectId.Null;

        [CommandMethod("ADE_AUTO_AMBIENTES_CV")]
        public void EjecutarAutoAmbientesCV()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                var config = new RoomDetectionConfig();
                ed.WriteMessage("\n══ ADE_AUTO_AMBIENTES_CV (EXPERIMENTAL) ══");
                ed.WriteMessage($"\n  Capas de muro: {string.Join(", ", config.CapasMuros)}");

                SemanticLayout aiLayout = null;
                string sourcePath = db.Filename;
                bool isTemporaryDxf = false;
                string dxfToProcess = sourcePath;

                bool isUnsavedTemplate = sourcePath.EndsWith(".dwt", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath) || isUnsavedTemplate)
                {
                    ed.WriteMessage("\n[!] Debe guardar el dibujo (DWG) al menos una vez antes de usar la extracción OpenCV.");
                    return;
                }

                string scriptPath = FindPythonScriptPath();
                string modelPath = FindAiModelPath();

                if (!File.Exists(scriptPath))
                {
                    ed.WriteMessage($"\n[!] No se encontró predict_cv.py en {scriptPath}");
                    return;
                }

                ed.WriteMessage("\n  Iniciando Extractor de Habitaciones OpenCV + IA YOLO...");

                using (var pm = new ProgressMeter())
                {
                    pm.Start("Generando mapa raster B&W y detectando...");
                    pm.SetLimit(100);

                    try
                    {
                        if (!sourcePath.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
                        {
                            dxfToProcess = Path.Combine(Path.GetTempPath(), "ADE_Temp_Inference_CV.dxf");
                            db.DxfOut(dxfToProcess, 16, DwgVersion.Current);
                            isTemporaryDxf = true;
                        }

                        string outJson = Path.Combine(Path.GetTempPath(), "layout_ai_cv_temp.json");
                        
                        string pythonArgs = $"\"{scriptPath}\" --dxf \"{dxfToProcess}\" --out \"{outJson}\"";
                        if (File.Exists(modelPath))
                        {
                            pythonArgs += $" --model \"{modelPath}\"";
                        }

                        string pythonExe = @"C:\Users\alan0\AppData\Local\Programs\Python\Python310\python.exe";
                        if (!File.Exists(pythonExe)) pythonExe = "python";

                        var psi = new ProcessStartInfo
                        {
                            FileName = pythonExe,
                            Arguments = pythonArgs,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = Path.GetDirectoryName(scriptPath)
                        };

                        var fullOutput = new System.Text.StringBuilder();
                        var fullError = new System.Text.StringBuilder();

                        using (var proc = Process.Start(psi))
                        {
                            proc.OutputDataReceived += (s, e) => {
                                if (!string.IsNullOrEmpty(e.Data)) fullOutput.AppendLine(e.Data);
                            };
                            proc.ErrorDataReceived += (s, e) => {
                                if (!string.IsNullOrEmpty(e.Data)) fullError.AppendLine(e.Data);
                            };
                            proc.BeginOutputReadLine();
                            proc.BeginErrorReadLine();

                            bool finished = false;
                            for (int t = 0; t < 180; t++)
                            {
                                if (proc.WaitForExit(1000)) { finished = true; break; }
                                pm.MeterProgress();
                                System.Windows.Forms.Application.DoEvents(); 
                            }

                            if (finished && proc.ExitCode == 0 && File.Exists(outJson))
                            {
                                string jsonStr = File.ReadAllText(outJson);
                                aiLayout = JsonSerializer.Deserialize<SemanticLayout>(jsonStr);
                                ed.WriteMessage($"\n  ✓ Segmentación lista. Habitaciones halladas: {aiLayout?.Ambientes?.Count ?? 0}");
                            }
                            else
                            {
                                ed.WriteMessage($"\n  ⚠ Inferencia CV falló (Code {proc.ExitCode}).");
                                ed.WriteMessage($"\n    OUT: {fullOutput.ToString().Replace("\n", " ").Trim()}");
                                ed.WriteMessage($"\n    ERR: {fullError.ToString().Replace("\n", " ").Trim()}");
                                return;
                            }
                        }

                        pm.Stop();
                        if (File.Exists(outJson)) try { File.Delete(outJson); } catch { }
                        if (isTemporaryDxf && File.Exists(dxfToProcess)) try { File.Delete(dxfToProcess); } catch { }
                    }
                    catch (System.Exception ex)
                    {
                        pm.Stop();
                        ed.WriteMessage($"\n  ⚠ Error en puente CV: {ex.Message}");
                        return;
                    }
                }

                if (aiLayout == null || aiLayout.Ambientes == null || aiLayout.Ambientes.Count == 0)
                {
                    ed.WriteMessage("\n[!] No se extrajeron ambientes desde el mapa OpenCV.");
                    return;
                }

                var rooms = new List<DetectedRoom>();
                
                // Parse rooms from JSON polygons directly
                foreach(var r in aiLayout.Ambientes)
                {
                    if (r.Polygon == null || r.Polygon.Count < 3) continue;

                    var verts = new List<Point2D>();
                    foreach(var p in r.Polygon)
                    {
                        verts.Add(new Point2D(p[0], p[1]));
                    }

                    double rawArea = PolygonMath.CalculateArea(verts);
                    if (rawArea < config.AreaMinimaM2) continue; // Basic filter

                    double perim = 0;
                    for (int i = 0; i < verts.Count; i++) {
                        perim += PolygonMath.Distance(verts[i], verts[(i+1)%verts.Count]);
                    }

                    // Ensure clockwise or something if needed, but not strictly required
                    var dr = new DetectedRoom
                    {
                        Vertices = verts,
                        Centroid = PolygonMath.CalculateCentroid(verts),
                        AreaM2 = rawArea,
                        PerimetroM = perim,
                        Confianza = "alta",
                        Tipo = "SIN_CLASIFICAR",
                        UnidadFuncional = "SIN_CLASIFICAR"
                    };
                    rooms.Add(dr);
                }

                if (rooms.Count == 0)
                {
                    ed.WriteMessage("\n[!] Los ambientes extraídos no cumplen el área mínima.");
                    return;
                }

                ed.WriteMessage("\n  Clasificando ambientes (IA -> Heurísticas)...");
                var classifier = new RoomClassifier();
                classifier.ClassifyRooms(rooms, db, config);

                ed.WriteMessage("\n  Agrupando en unidades funcionales...");
                var grouper = new RoomGrouper();
                grouper.GroupRooms(rooms, db, config);

                ed.WriteMessage("\n  Abriendo panel de verificación OpenCV...");
                Action<string> onHighlight = (handle) => HighlightPolyline(db, handle);
                Action<string> onZoom = (handle) => ZoomToPolyline(db, ed, handle);

                var vm = new AutoAmbientesViewModel(rooms, onHighlight, onZoom);
                var dialog = new AutoAmbientesDialog(vm);

                Application.ShowModalWindow(Application.MainWindow.Handle, dialog, false);

                RestaurarColores(db);

                if (!vm.Confirmed)
                {
                    ed.WriteMessage("\n  Operación cancelada por el usuario.");
                    return;
                }

                var confirmed = vm.GetConfirmedRooms();
                if (confirmed.Count == 0) return;

                ed.WriteMessage($"\n  Insertando {confirmed.Count} bloques ID_LOCALES y Polilíneas OpenCV...");

                string blockRefPath = "Bloques_CAD\\ID_LOCALES.dwg";
                string pluginDir2 = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string fullBlockPath = Path.Combine(pluginDir2, "..", "..", blockRefPath);
                fullBlockPath = Path.GetFullPath(fullBlockPath);

                if (!BlockManager.EnsureBlockExists(BLOCK_IDLOCALES, fullBlockPath))
                {
                    ed.WriteMessage($"\n[!] No se encontró '{BLOCK_IDLOCALES}' en {fullBlockPath}");
                    return;
                }

                int inserted = 0;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    var regAppTable = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                    if (!regAppTable.Has(APP_ID))
                    {
                        var rat = new RegAppTableRecord { Name = APP_ID };
                        regAppTable.UpgradeOpen();
                        regAppTable.Add(rat);
                        tr.AddNewlyCreatedDBObject(rat, true);
                    }

                    var blockId = bt[BLOCK_IDLOCALES];

                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    string targetLayer = "ADELEC_AMBIENTES_CV";
                    if (!lt.Has(targetLayer))
                    {
                        lt.UpgradeOpen();
                        var lr = new LayerTableRecord { Name = targetLayer, Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 4) }; // Cyan para distinguir CV
                        lt.Add(lr);
                        tr.AddNewlyCreatedDBObject(lr, true);
                    }

                    foreach (var room in confirmed)
                    {
                        // 1. Dibujar el polígono exportado por OpenCV
                        var poly = new Polyline();
                        poly.SetDatabaseDefaults();
                        poly.Layer = targetLayer;
                        for (int i = 0; i < room.Vertices.Count; i++)
                        {
                            poly.AddVertexAt(i, new Point2d(room.Vertices[i].X, room.Vertices[i].Y), 0, 0, 0);
                        }
                        poly.Closed = true;
                        space.AppendEntity(poly);
                        tr.AddNewlyCreatedDBObject(poly, true);

                        // 2. Insertar Bloque
                        var centroid = new Point3d(room.Centroid.X, room.Centroid.Y, 0);
                        var br = new BlockReference(centroid, blockId);
                        space.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);

                        var tipoInfo = TipoAmbienteInfo.DesdeNombre(room.Tipo);
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
                                "01"    => room.UnidadFuncional,
                                "55"    => room.Planta,
                                "LOCAL" => tipoInfo.Nombre,
                                "AREA"  => $"{room.AreaM2:F2}m²",
                                "ESP"   => "0.15", 
                                _       => attDef.TextString,
                            };

                            br.AttributeCollection.AppendAttribute(attRef);
                            tr.AddNewlyCreatedDBObject(attRef, true);
                        }

                        // Vincular XData
                        try
                        {
                            br.XData = new ResultBuffer(
                                new TypedValue((int)DxfCode.ExtendedDataRegAppName, APP_ID),
                                new TypedValue((int)DxfCode.ExtendedDataHandle, poly.Handle)
                            );
                        } catch { }

                        if (br.IsDynamicBlock)
                        {
                            foreach (DynamicBlockReferenceProperty prop in br.DynamicBlockReferencePropertyCollection)
                            {
                                if (prop.PropertyName == "Visibilidad1" && !prop.ReadOnly)
                                {
                                    try { prop.Value = "CON NUM"; } catch { }
                                    break;
                                }
                            }
                        }

                        inserted++;
                    }

                    tr.Commit();
                }

                ed.WriteMessage($"\n\n✓ {inserted} ambientes extraídos vía OpenCV Vision e insertados exitosamente.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ERROR] ADE_AUTO_AMBIENTES_CV: {ex.Message}\n{ex.StackTrace}");
                RestaurarColores(db);
            }
        }

        private void HighlightPolyline(Database db, string handle)
        {
            try
            {
                if (_highlightedPolyId != ObjectId.Null)
                {
                    RestoreColor(db, _highlightedPolyId);
                    _highlightedPolyId = ObjectId.Null;
                }

                if (string.IsNullOrEmpty(handle)) return;

                using var tr = db.TransactionManager.StartTransaction();
                if (!TryGetPolyline(db, tr, handle, out Polyline poly)) return;

                _highlightedPolyId = poly.ObjectId;
                _coloresOriginales[_highlightedPolyId] = poly.Color;

                poly.UpgradeOpen();
                poly.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 1);
                
                tr.Commit();
            }
            catch { }
        }

        private void RestoreColor(Database db, ObjectId id)
        {
            if (!_coloresOriginales.TryGetValue(id, out var originalColor)) return;
            using var tr = db.TransactionManager.StartTransaction();
            var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
            if (ent != null) ent.Color = originalColor;
            tr.Commit();
        }

        private void RestaurarColores(Database db)
        {
            if (_coloresOriginales.Count == 0 && _highlightedPolyId == ObjectId.Null) return;
            
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                foreach (var kvp in _coloresOriginales)
                {
                    var id = kvp.Key;
                    if (id.IsErased || id.IsEffectivelyErased) continue;
                    var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent != null) ent.Color = kvp.Value;
                }
                tr.Commit();
            }
            catch { }
            finally
            {
                _coloresOriginales.Clear();
                _highlightedPolyId = ObjectId.Null;
            }
        }

        private void ZoomToPolyline(Database db, Editor ed, string handle)
        {
            try
            {
                if (string.IsNullOrEmpty(handle)) return;

                using var tr = db.TransactionManager.StartTransaction();
                if (!TryGetPolyline(db, tr, handle, out Polyline poly)) return;

                var ext = poly.GeometricExtents;
                var view = new ViewTableRecord();
                
                double width = ext.MaxPoint.X - ext.MinPoint.X;
                double height = ext.MaxPoint.Y - ext.MinPoint.Y;
                
                view.CenterPoint = new Point2d(
                    ext.MinPoint.X + width / 2.0,
                    ext.MinPoint.Y + height / 2.0
                );
                
                view.Height = height * 1.5; 
                view.Width = width * 1.5;

                ed.SetCurrentView(view);
                tr.Commit();
            }
            catch { }
        }

        private bool TryGetPolyline(Database db, Transaction tr, string handleStr, out Polyline polyline)
        {
            polyline = null;
            try
            {
                var handle = new Handle(Convert.ToInt64(handleStr, 16));
                if (!db.TryGetObjectId(handle, out ObjectId id)) return false;
                
                var obj = tr.GetObject(id, OpenMode.ForRead);
                polyline = obj as Polyline;
                return polyline != null;
            }
            catch { return false; }
        }

        private static string FindAiModelPath()
        {
            try
            {
                string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string dir = Path.GetDirectoryName(dllPath);
                
                while (!string.IsNullOrEmpty(dir))
                {
                    string pUser = @"g:\AD-ELEC\DATASET\DXF\best (1).pt";
                    if (File.Exists(pUser)) return pUser;

                    string p1 = Path.Combine(dir, "src", "AdElec.AiSegmentation", "best.pt");
                    if (File.Exists(p1)) return p1;
                    
                    string p2 = Path.Combine(dir, "AdElec.AiSegmentation", "best.pt");
                    if (File.Exists(p2)) return p2;

                    var parent = Directory.GetParent(dir);
                    if (parent == null) break;
                    dir = parent.FullName;
                }
            }
            catch { }
            return null;
        }

        private static string FindPythonScriptPath()
        {
            try
            {
                string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                while (!string.IsNullOrEmpty(dir))
                {
                    string p = Path.Combine(dir, "src", "AdElec.AiSegmentation", "predict_cv.py");
                    if (File.Exists(p)) return p;
                    p = Path.Combine(dir, "AdElec.AiSegmentation", "predict_cv.py");
                    if (File.Exists(p)) return p;
                    
                    var parent = Directory.GetParent(dir);
                    if (parent == null) break;
                    dir = parent.FullName;
                }
            }
            catch { }
            return null;
        }
    }
}
