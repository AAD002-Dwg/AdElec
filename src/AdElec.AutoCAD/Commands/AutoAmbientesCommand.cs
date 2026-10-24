using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
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
    /// <summary>
    /// Comando ADE_AUTO_AMBIENTES — detección automática de ambientes.
    ///
    /// Flujo:
    /// 1. Detectar perímetros de ambientes (polilíneas cerradas + reconstrucción desde segmentos)
    /// 2. Clasificar cada ambiente según tipo (texto > bloques > heurísticas)
    /// 3. Agrupar en unidades funcionales
    /// 4. Presentar panel de verificación WPF
    /// 5. Insertar bloques ID_LOCALES para los ambientes confirmados
    /// </summary>
    public class AutoAmbientesCommand
    {
        private const string BLOCK_IDLOCALES = "ID_LOCALES";
        private const string BLOCK_FILE_IDLOCALES = "blocks\\ID_LOCALES.dwg";
        private const string APP_ID = "ADE_SYNC_LINK";

        // ── Resaltado bidireccional ──────────────────────────────────────────
        // Almacena el color original para restauración al cerrar el diálogo.
        // Usamos el alias AcColor porque System.Drawing.Color también está en scope (UseWindowsForms).
        private readonly Dictionary<ObjectId, Autodesk.AutoCAD.Colors.Color> _coloresOriginales = new();
        private ObjectId _highlightedPolyId = ObjectId.Null;

        [CommandMethod("ADE_AUTOAMBIENTES")]
        [CommandMethod("ADE_AUTO_AMBIENTES")]
        public void EjecutarAutoAmbientes()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                // ── 1. Configuración ─────────────────────────────────────────
                var config = new RoomDetectionConfig();
                ed.WriteMessage("\n══ ADE_AUTO_AMBIENTES ══");

                // Modo de detección interactivo
                var pko = new PromptKeywordOptions("\nModo de detección [Automatico todo el dibujo/Seleccionar entidades] <Automatico>: ");
                pko.Keywords.Add("Automatico");
                pko.Keywords.Add("Seleccionar");
                pko.Keywords.Default = "Automatico";
                pko.AllowNone = true;
                var pr = ed.GetKeywords(pko);
                if (pr.Status == PromptStatus.Cancel) return;

                bool modoSeleccion = (pr.StringResult == "Seleccionar");
                List<ObjectId> selectedIds = null;

                if (modoSeleccion)
                {
                    var pso = new PromptSelectionOptions
                    {
                        MessageForAdding = "\nSeleccione polilíneas o muros de los ambientes (o Enter para terminar): "
                    };
                    var psr = ed.GetSelection(pso);
                    if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
                    {
                        ed.WriteMessage("\nNo se seleccionaron entidades.");
                        return;
                    }
                    selectedIds = psr.Value.GetObjectIds().ToList();
                    ed.WriteMessage($"\n  {selectedIds.Count} entidades seleccionadas.");
                }
                else
                {
                    ed.WriteMessage($"\n  Capas de muro: {string.Join(", ", config.CapasMuros)}");
                    ed.WriteMessage($"\n  Tolerancia snap: {config.ToleranciaSnap} unidades");
                }

                // ── 1.5. Segmentación Semántica IA (Python Bridge) ───────────────
                SemanticLayout aiLayout = null;
                string sourcePath = db.Filename;
                bool isTemporaryDxf = false;
                string dxfToProcess = sourcePath;

                if (!modoSeleccion && !string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath))
                {
                    string scriptPath = FindPythonScriptPath();
                    string modelPath = FindAiModelPath();

                    if (scriptPath != null)
                    {
                        ed.WriteMessage("\n  Conectando motor IA de segmentación semántica (Segundo Plano)...");
                        
                        using (var pm = new ProgressMeter())
                        {
                            pm.Start("Procesando Segmentación IA...");
                            pm.SetLimit(100);

                            try
                            {
                                // Soporte para DWG -> DXF temporal
                                if (sourcePath.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                                {
                                    dxfToProcess = Path.Combine(Path.GetTempPath(), "ADE_Temp_Inference.dxf");
                                    db.DxfOut(dxfToProcess, 16, DwgVersion.Current);
                                    isTemporaryDxf = true;
                                }

                                string outJson = Path.Combine(Path.GetTempPath(), "layout_ai_temp.json");
                                
                                // Construir argumentos
                                string pythonArgs = $"\"{scriptPath}\" --dxf \"{dxfToProcess}\" --out \"{outJson}\"";
                                if (!string.IsNullOrEmpty(modelPath))
                                {
                                    pythonArgs += $" --model \"{modelPath}\"";
                                    ed.WriteMessage($"\n    Modelo detectado: {Path.GetFileName(modelPath)}");
                                }

                                string pythonExe = @"C:\Users\alan0\AppData\Local\Programs\Python\Python310\python.exe";
                                if (!File.Exists(pythonExe)) pythonExe = "python"; // Fallback

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

                                pm.MeterProgress(); // 10%
                                var fullError = new System.Text.StringBuilder();

                                using (var proc = Process.Start(psi))
                                {
                                    proc.ErrorDataReceived += (s, e) => {
                                        if (!string.IsNullOrEmpty(e.Data)) fullError.AppendLine(e.Data);
                                    };
                                    proc.BeginErrorReadLine();

                                    // Aumentamos timeout a 180s para escaneos detallados
                                    bool finished = false;
                                    for (int t = 0; t < 180; t++)
                                    {
                                        if (proc.WaitForExit(1000)) { finished = true; break; }
                                        pm.MeterProgress();
                                        System.Windows.Forms.Application.DoEvents(); // Mantener AutoCAD vivo
                                    }

                                    if (finished && proc.ExitCode == 0 && File.Exists(outJson))
                                    {
                                        string jsonStr = File.ReadAllText(outJson);
                                        aiLayout = JsonSerializer.Deserialize<SemanticLayout>(jsonStr);
                                        ed.WriteMessage($"\n  ✓ Semántica recibida: {aiLayout?.Ambientes?.Count ?? 0} ambientes y {(aiLayout?.Puertas?.Count ?? 0) + (aiLayout?.Ventanas?.Count ?? 0)} aberturas.");
                                    }
                                    else
                                    {
                                        ed.WriteMessage($"\n  ⚠ Inferencia IA falló (Code {proc.ExitCode}). Error:");
                                        ed.WriteMessage($"\n    {fullError.ToString().Replace("\n", " ").Trim()}");
                                    }
                                }

                                pm.Stop();
                                if (File.Exists(outJson)) try { File.Delete(outJson); } catch { }
                                if (isTemporaryDxf && File.Exists(dxfToProcess)) try { File.Delete(dxfToProcess); } catch { }
                            }
                            catch (System.Exception ex)
                            {
                                pm.Stop();
                                ed.WriteMessage($"\n  ⚠ Omisión de IA segmentadora: {ex.Message}");
                            }
                        }
                    }
                }
                else if (!modoSeleccion)
                {
                    ed.WriteMessage("\n  ⚠ Dibujo sin guardar. Se omite puente IA semántico.");
                }

                // ── 1.5. Visualizar predicciones IA (Modo Debug) ─────────────
                if (aiLayout != null && (aiLayout.Puertas?.Count > 0 || aiLayout.Ventanas?.Count > 0))
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                        string debugLayer = "ADELEC_DEBUG_AIBOX";
                        if (!layerTable.Has(debugLayer))
                        {
                            layerTable.UpgradeOpen();
                            var newLayer = new LayerTableRecord { Name = debugLayer, Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3) }; // Green
                            layerTable.Add(newLayer);
                            tr.AddNewlyCreatedDBObject(newLayer, true);
                        }
                        
                        var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                        
                        var allOpenings = new List<AdElec.Core.Models.SemanticOpening>();
                        if (aiLayout.Puertas != null) allOpenings.AddRange(aiLayout.Puertas);
                        if (aiLayout.Ventanas != null) allOpenings.AddRange(aiLayout.Ventanas);

                        foreach (var elem in allOpenings)
                        {
                            if (elem.Bbox == null || elem.Bbox.Length < 4) continue;
                            var poly = new Polyline();
                            poly.AddVertexAt(0, new Point2d(elem.Bbox[0], elem.Bbox[1]), 0, 0, 0);
                            poly.AddVertexAt(1, new Point2d(elem.Bbox[2], elem.Bbox[1]), 0, 0, 0);
                            poly.AddVertexAt(2, new Point2d(elem.Bbox[2], elem.Bbox[3]), 0, 0, 0);
                            poly.AddVertexAt(3, new Point2d(elem.Bbox[0], elem.Bbox[3]), 0, 0, 0);
                            poly.Closed = true;
                            poly.Layer = debugLayer;
                            btr.AppendEntity(poly);
                            tr.AddNewlyCreatedDBObject(poly, true);
                        }
                        tr.Commit();
                    }
                    ed.WriteMessage($"\n  [DEBUG] Dibujadas {aiLayout.Puertas?.Count + aiLayout.Ventanas?.Count} cajas verdes de IA en capa 'ADELEC_DEBUG_AIBOX'.");
                }

                // ── 2. Detectar ambientes ────────────────────────────────────
                ed.WriteMessage("\n  Extrayendo ciclos vectoriales (Graph Reconstructor)...");
                var detector = new RoomDetector();
                var rooms = detector.DetectRooms(db, config, aiLayout, msg => ed.WriteMessage(msg), selectedIds);

                // ── 3. Clasificar ambientes ──────────────────────────────────
                if (rooms.Count > 0)
                {
                    ed.WriteMessage("\n  Clasificando ambientes detectados...");
                    var classifier = new RoomClassifier();
                    classifier.ClassifyRooms(rooms, db, config);

                    var confianzaAlta = rooms.Count(r => r.Confianza == "alta");
                    var confianzaMedia = rooms.Count(r => r.Confianza == "media");
                    var confianzaBaja = rooms.Count(r => r.Confianza == "baja");
                    ed.WriteMessage($"\n  Clasificación: {confianzaAlta} alta, {confianzaMedia} media, {confianzaBaja} baja.");

                    // ── 4. Agrupar en UFs ────────────────────────────────────────
                    ed.WriteMessage("\n  Agrupando en unidades funcionales...");
                    var grouper = new RoomGrouper();
                    grouper.GroupRooms(rooms, db, config);
                }

                // ── 4.5. Sincronizar e integrar ambientes preexistentes (ADE_AMBIENTES) ──
                var repo = new AdElec.AutoCAD.Repositories.DwgAmbienteRepository();
                var ambientesExistentes = repo.GetAllAmbientes();

                if (ambientesExistentes.Count > 0)
                {
                    ed.WriteMessage($"\n  Sincronizando con {ambientesExistentes.Count} ambientes preexistentes (ID_LOCALES)...");

                    foreach (var amb in ambientesExistentes)
                    {
                        // Buscar si ya coincide con alguno de los detectados (por bloque o por polilínea)
                        var matchingRoom = rooms.FirstOrDefault(r =>
                            (!string.IsNullOrEmpty(r.ExistingBlockHandle) && string.Equals(r.ExistingBlockHandle, amb.Handle, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(amb.PolylineHandle) && !string.IsNullOrEmpty(r.SourceHandle) && string.Equals(r.SourceHandle, amb.PolylineHandle, StringComparison.OrdinalIgnoreCase)));

                        if (matchingRoom != null)
                        {
                            matchingRoom.ExistingBlockHandle = amb.Handle;
                            if (!string.IsNullOrEmpty(amb.TipoDisplay)) matchingRoom.Tipo = amb.TipoDisplay;
                            if (!string.IsNullOrEmpty(amb.UF)) matchingRoom.UnidadFuncional = amb.UF;
                            if (!string.IsNullOrEmpty(amb.Planta)) matchingRoom.Planta = amb.Planta;
                            matchingRoom.Confianza = "alta";
                            matchingRoom.RequiereVerificacion = false;
                            continue;
                        }

                        // Si no coincidió con ninguna polilínea detectada en esta pasada
                        bool polyFound = false;
                        if (!string.IsNullOrEmpty(amb.PolylineHandle))
                        {
                            try
                            {
                                var handleVal = new Handle(Convert.ToInt64(amb.PolylineHandle, 16));
                                if (db.TryGetObjectId(handleVal, out ObjectId polyId))
                                {
                                    using var trP = db.TransactionManager.StartOpenCloseTransaction();
                                    if (trP.GetObject(polyId, OpenMode.ForRead) is Polyline poly && poly.Closed)
                                    {
                                        var (verts, hasBulge) = RoomDetector.ExtractPolylineVertices(poly);
                                        var centroid = PolygonMath.CalculateCentroid(verts);
                                        var newRoom = new DetectedRoom
                                        {
                                            Id = $"R{rooms.Count + 1:D2}",
                                            SourceHandle = amb.PolylineHandle,
                                            ExistingBlockHandle = amb.Handle,
                                            Tipo = !string.IsNullOrEmpty(amb.TipoDisplay) ? amb.TipoDisplay : "Ambiente",
                                            UnidadFuncional = !string.IsNullOrEmpty(amb.UF) ? amb.UF : "01",
                                            Planta = !string.IsNullOrEmpty(amb.Planta) ? amb.Planta : "PB",
                                            AreaM2 = amb.AreaM2 > 0 ? amb.AreaM2 : poly.Area,
                                            PerimetroM = poly.Length,
                                            Centroid = centroid,
                                            Vertices = verts,
                                            ContieneBulge = hasBulge,
                                            Confianza = "alta",
                                            RequiereVerificacion = false
                                        };
                                        rooms.Add(newRoom);
                                        polyFound = true;
                                    }
                                    trP.Commit();
                                }
                            }
                            catch { }
                        }

                        if (!polyFound)
                        {
                            var centroid = amb.PolygonPoints != null && amb.PolygonPoints.Count > 0
                                ? PolygonMath.CalculateCentroid(amb.PolygonPoints)
                                : new Point2D(0, 0);

                            var orphanRoom = new DetectedRoom
                            {
                                Id = $"R{rooms.Count + 1:D2}",
                                ExistingBlockHandle = amb.Handle,
                                Tipo = !string.IsNullOrEmpty(amb.TipoDisplay) ? amb.TipoDisplay : "Ambiente",
                                UnidadFuncional = !string.IsNullOrEmpty(amb.UF) ? amb.UF : "01",
                                Planta = !string.IsNullOrEmpty(amb.Planta) ? amb.Planta : "PB",
                                AreaM2 = amb.AreaM2,
                                Centroid = centroid,
                                Confianza = "alta",
                                RequiereVerificacion = false
                            };
                            rooms.Add(orphanRoom);
                        }
                    }
                }

                if (rooms.Count == 0)
                {
                    ed.WriteMessage("\n[!] No se detectaron ni encontraron ambientes.");
                    ed.WriteMessage("\n    Verificá que las polilíneas/segmentos de muro estén en las capas configuradas.");
                    ed.WriteMessage($"\n    Capas buscadas: {string.Join(", ", config.CapasMuros)}");
                    return;
                }

                ed.WriteMessage($"\n  Total de ambientes para verificación: {rooms.Count}.");

                var ufCount = rooms.Select(r => r.UnidadFuncional).Distinct().Count();
                var sinClasificar = rooms.Count(r => r.UnidadFuncional == "SIN_CLASIFICAR");
                ed.WriteMessage($"\n  {ufCount} unidades funcionales detectadas.");
                if (sinClasificar > 0)
                    ed.WriteMessage($"\n  ⚠ {sinClasificar} ambientes sin UF asignada (requieren verificación manual).");

                // ── 5. Validación heurística de escala ───────────────────────
                double avgArea = rooms.Average(r => r.AreaM2);
                if (avgArea < 1.0)
                {
                    ed.WriteMessage($"\n  ⚠ Área promedio = {avgArea:F2} m² — las unidades del dibujo podrían ser incorrectas.");
                }
                else if (avgArea > 10000.0)
                {
                    ed.WriteMessage($"\n  ⚠ Área promedio = {avgArea:F0} m² — verificar escala del dibujo.");
                }

                // ── 5.5. Mapear artefactos eléctricos dentro de cada ambiente ────
                ed.WriteMessage("\n  Mapeando artefactos y circuitos dentro de los recintos...");
                MapearArtefactosEnAmbientes(db, rooms);

                // ── 6. Mostrar panel de verificación ─────────────────────────
                ed.WriteMessage("\n  Abriendo gestor de ambientes...");

                Action<string> onHighlight = (handle) => HighlightPolyline(db, handle);
                Action<string> onZoom = (handle) => ZoomToPolyline(db, ed, handle);

                var vm = new AutoAmbientesViewModel(rooms, onHighlight, onZoom);
                var dialog = new AutoAmbientesDialog(vm);

                Application.ShowModalWindow(Application.MainWindow.Handle, dialog, false);

                // Restaurar colores al cerrar
                RestaurarColores(db);

                if (!vm.Confirmed)
                {
                    ed.WriteMessage("\n  Operación cancelada por el usuario.");
                    return;
                }

                var confirmed = vm.GetConfirmedRooms();
                if (confirmed.Count == 0)
                {
                    ed.WriteMessage("\n  No se seleccionaron ambientes para insertar o actualizar.");
                    return;
                }

                // ── 7. Insertar o actualizar bloques ID_LOCALES ──────────────
                string blockPath = ResolveBlockPath(db, BLOCK_FILE_IDLOCALES);
                bool blockDefExists = BlockManager.EnsureBlockExists(BLOCK_IDLOCALES, blockPath);

                int inserted = 0;
                int updated = 0;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    // Registrar AppID para XData si no existe
                    var regAppTable = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                    if (!regAppTable.Has(APP_ID))
                    {
                        regAppTable.UpgradeOpen();
                        var rat = new RegAppTableRecord { Name = APP_ID };
                        regAppTable.Add(rat);
                        tr.AddNewlyCreatedDBObject(rat, true);
                    }

                    ObjectId blockId = ObjectId.Null;
                    if (bt.Has(BLOCK_IDLOCALES))
                    {
                        blockId = bt[BLOCK_IDLOCALES];
                    }

                    foreach (var room in confirmed)
                    {
                        var tipoInfo = TipoAmbienteInfo.DesdeNombre(room.Tipo);

                        // CASO A: Actualizar bloque existente (creado por ADE_AMBIENTES o corrida previa)
                        if (!string.IsNullOrEmpty(room.ExistingBlockHandle))
                        {
                            try
                            {
                                var handleVal = new Handle(Convert.ToInt64(room.ExistingBlockHandle, 16));
                                if (db.TryGetObjectId(handleVal, out ObjectId existingBlockId))
                                {
                                    if (tr.GetObject(existingBlockId, OpenMode.ForWrite) is BlockReference existingBr)
                                    {
                                        foreach (ObjectId attId in existingBr.AttributeCollection)
                                        {
                                            if (tr.GetObject(attId, OpenMode.ForWrite) is AttributeReference attRef)
                                            {
                                                switch (attRef.Tag.ToUpperInvariant())
                                                {
                                                    case "01":
                                                        attRef.TextString = room.UnidadFuncional;
                                                        break;
                                                    case "55":
                                                        attRef.TextString = room.Planta;
                                                        break;
                                                    case "LOCAL":
                                                        attRef.TextString = tipoInfo.Nombre;
                                                        break;
                                                    case "AREA":
                                                        attRef.TextString = $"{room.AreaM2:F2}m²";
                                                        break;
                                                }
                                            }
                                        }
                                        updated++;
                                        continue;
                                    }
                                }
                            }
                            catch { /* si falla, intenta inserción */ }
                        }

                        // CASO B: Insertar nuevo bloque ID_LOCALES
                        if (blockId == ObjectId.Null)
                        {
                            ed.WriteMessage("\n[!] Bloque ID_LOCALES no disponible en el dibujo para insertar nuevos recintos.");
                            continue;
                        }

                        var centroid = new Point3d(room.Centroid.X, room.Centroid.Y, 0);
                        var br = new BlockReference(centroid, blockId);
                        space.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);

                        // Insertar atributos
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
                                "ESP"   => "0.15", // default espesor
                                _       => attDef.TextString,
                            };

                            br.AttributeCollection.AppendAttribute(attRef);
                            tr.AddNewlyCreatedDBObject(attRef, true);
                        }

                        // Vínculo XData con la polilínea fuente o generada
                        Handle sourceHandle = default;
                        
                        // Si SourceHandle está vacío (fue reconstruido desde segmentos)
                        if (string.IsNullOrEmpty(room.SourceHandle))
                        {
                            var poly = new Polyline();
                            poly.SetDatabaseDefaults();
                            poly.ColorIndex = 256; // ByLayer
                            
                            string targetLayer = "ADELEC_AMBIENTES";
                            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                            if (!lt.Has(targetLayer))
                            {
                                lt.UpgradeOpen();
                                var lr = new LayerTableRecord { Name = targetLayer, Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3) };
                                lt.Add(lr);
                                tr.AddNewlyCreatedDBObject(lr, true);
                            }
                            poly.Layer = targetLayer;

                            for (int i = 0; i < room.Vertices.Count; i++)
                            {
                                poly.AddVertexAt(i, new Point2d(room.Vertices[i].X, room.Vertices[i].Y), 0, 0, 0);
                            }
                            poly.Closed = true;

                            space.AppendEntity(poly);
                            tr.AddNewlyCreatedDBObject(poly, true);
                            
                            sourceHandle = poly.Handle;
                            room.SourceHandle = poly.Handle.ToString();
                        }
                        else
                        {
                            try { sourceHandle = new Handle(Convert.ToInt64(room.SourceHandle, 16)); }
                            catch { /* handle inválido */ }
                        }

                        // Escribir el XData en el bloque referenciando a la polilínea
                        if (sourceHandle != default)
                        {
                            try
                            {
                                br.XData = new ResultBuffer(
                                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, APP_ID),
                                    new TypedValue((int)DxfCode.ExtendedDataHandle, sourceHandle)
                                );
                            }
                            catch { }
                        }

                        // Visibilidad "CON NUM" si es bloque dinámico
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

                // ── 8. Resumen ───────────────────────────────────────────────
                var ufConfirmed = confirmed.Select(r => r.UnidadFuncional).Distinct().Count();
                ed.WriteMessage($"\n\n✓ Proceso completado: {inserted} nuevos insertados, {updated} existentes actualizados en {ufConfirmed} UFs.");
                ed.WriteMessage("\n  Usá 'ADE_PANEL' para sincronizar los recintos con el motor AEA.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ERROR] ADE_AUTO_AMBIENTES: {ex.Message}");
                ed.WriteMessage($"\n  Stack: {ex.StackTrace}");
                RestaurarColores(db);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // Resaltado bidireccional
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Resalta una polilínea en el dibujo cambiando su color a rojo.
        /// Si handle es vacío, restaura el color anterior.
        /// </summary>
        private void HighlightPolyline(Database db, string handle)
        {
            try
            {
                // Restaurar el resaltado anterior
                if (_highlightedPolyId != ObjectId.Null)
                {
                    RestoreColor(db, _highlightedPolyId);
                    _highlightedPolyId = ObjectId.Null;
                }

                if (string.IsNullOrEmpty(handle)) return;

                var handleValue = new Handle(Convert.ToInt64(handle, 16));
                if (!db.TryGetObjectId(handleValue, out ObjectId polyId)) return;

                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var ent = tr.GetObject(polyId, OpenMode.ForWrite) as Entity;
                if (ent == null) { tr.Commit(); return; }

                // Guardar color original
                if (!_coloresOriginales.ContainsKey(polyId))
                    _coloresOriginales[polyId] = ent.Color;

                // Aplicar color de resaltado (rojo, ACI index 1)
                ent.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, 1);
                tr.Commit();

                _highlightedPolyId = polyId;
            }
            catch { /* ignorar errores de resaltado — no es crítico */ }
        }

        /// <summary>
        /// Zoom a la polilínea indicada por handle.
        /// </summary>
        private void ZoomToPolyline(Database db, Editor ed, string handle)
        {
            try
            {
                if (string.IsNullOrEmpty(handle)) return;

                var handleValue = new Handle(Convert.ToInt64(handle, 16));
                if (!db.TryGetObjectId(handleValue, out ObjectId polyId)) return;

                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var ent = tr.GetObject(polyId, OpenMode.ForRead) as Entity;
                if (ent == null) { tr.Commit(); return; }

                var ext = ent.GeometricExtents;
                tr.Commit();

                // Zoom con un margen del 20%
                var min = ext.MinPoint;
                var max = ext.MaxPoint;
                double marginX = (max.X - min.X) * 0.2;
                double marginY = (max.Y - min.Y) * 0.2;

                var zoomMin = new Point3d(min.X - marginX, min.Y - marginY, 0);
                var zoomMax = new Point3d(max.X + marginX, max.Y + marginY, 0);

                // Usar ViewTableRecord para zoom
                var view = ed.GetCurrentView();
                view.CenterPoint = new Point2d(
                    (zoomMin.X + zoomMax.X) / 2,
                    (zoomMin.Y + zoomMax.Y) / 2);
                view.Width = zoomMax.X - zoomMin.X;
                view.Height = zoomMax.Y - zoomMin.Y;
                ed.SetCurrentView(view);
            }
            catch { /* ignorar errores de zoom */ }
        }

        /// <summary>Restaura el color original de una entidad específica.</summary>
        private void RestoreColor(Database db, ObjectId entId)
        {
            if (!_coloresOriginales.TryGetValue(entId, out var originalColor)) return;

            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var ent = tr.GetObject(entId, OpenMode.ForWrite) as Entity;
                if (ent != null)
                    ent.Color = originalColor;
                tr.Commit();
                _coloresOriginales.Remove(entId);
            }
            catch { }
        }

        /// <summary>Restaura todos los colores originales de entidades resaltadas.</summary>
        private void RestaurarColores(Database db)
        {
            foreach (var kvp in _coloresOriginales.ToList())
            {
                try
                {
                    using var tr = db.TransactionManager.StartOpenCloseTransaction();
                    var ent = tr.GetObject(kvp.Key, OpenMode.ForWrite) as Entity;
                    if (ent != null)
                        ent.Color = kvp.Value;
                    tr.Commit();
                }
                catch { }
            }
            _coloresOriginales.Clear();
            _highlightedPolyId = ObjectId.Null;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Helpers (compartidos con AmbientesCommand)
        // ══════════════════════════════════════════════════════════════════════

        // ══════════════════════════════════════════════════════════════════════
        // Utilidades de Rutas
        // ══════════════════════════════════════════════════════════════════════
        private static string FindAiModelPath()
        {
            try
            {
                string dllPath = Assembly.GetExecutingAssembly().Location;
                string dir = Path.GetDirectoryName(dllPath);
                
                while (!string.IsNullOrEmpty(dir))
                {
                    // Prioridad 1: Modelo descargado por el usuario
                    string pUser = @"g:\AD-ELEC\DATASET\DXF\best (1).pt";
                    if (File.Exists(pUser)) return pUser;

                    // Prioridad 2: Modelo en carpeta AiSegmentation
                    string p1 = Path.Combine(dir, "src", "AdElec.AiSegmentation", "best.pt");
                    if (File.Exists(p1)) return p1;
                    
                    // Buscar en AdElec.AiSegmentation/best.pt (estructura publicada)
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
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                while (!string.IsNullOrEmpty(dir))
                {
                    string p = Path.Combine(dir, "src", "AdElec.AiSegmentation", "predict.py");
                    if (File.Exists(p)) return p;
                    // También sube directamente
                    p = Path.Combine(dir, "AdElec.AiSegmentation", "predict.py");
                    if (File.Exists(p)) return p;
                    
                    var parent = Directory.GetParent(dir);
                    if (parent == null) break;
                    dir = parent.FullName;
                }
            }
            catch { }
            return null;
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

        /// <summary>
        /// Escanea las bocas y artefactos eléctricos (IUG, IUE, TUG, TUE, etc.) en el espacio modelo
        /// y los asigna al recinto cuyo polígono los contiene mediante Point-in-Polygon.
        /// </summary>
        private static void MapearArtefactosEnAmbientes(Database db, List<DetectedRoom> rooms)
        {
            if (rooms == null || rooms.Count == 0) return;

            var artefactos = new List<(Point2D Pos, string Tipo, string CX, double Potencia, string NombreBloque)>();

            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var space = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId entId in space)
                {
                    var ent = tr.GetObject(entId, OpenMode.ForRead);
                    if (ent is not BlockReference br) continue;

                    string bName = br.IsDynamicBlock
                        ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                        : ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;

                    if (string.Equals(bName, "ID_LOCALES", StringComparison.OrdinalIgnoreCase)) continue;

                    string tipo = "";
                    if (string.Equals(bName, "I.E-AD-07", StringComparison.OrdinalIgnoreCase)) tipo = "IUG";
                    else if (string.Equals(bName, "I.E-AD-08", StringComparison.OrdinalIgnoreCase)) tipo = "IUE";
                    else if (string.Equals(bName, "I.E-AD-09", StringComparison.OrdinalIgnoreCase)) tipo = "TUG";
                    else if (string.Equals(bName, "I.E-AD-09.02", StringComparison.OrdinalIgnoreCase)) tipo = "TUE";
                    else if (string.Equals(bName, "I.E-AD-01", StringComparison.OrdinalIgnoreCase)) tipo = "SWITCH";
                    else if (bName.IndexOf("TOMA", StringComparison.OrdinalIgnoreCase) >= 0) tipo = "TUG";
                    else if (bName.IndexOf("LUM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             bName.IndexOf("CENTRO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             bName.IndexOf("BRAZO", StringComparison.OrdinalIgnoreCase) >= 0) tipo = "IUG";

                    // Leer atributos
                    string cx = "";
                    double pot = 0;
                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        if (tr.GetObject(attId, OpenMode.ForRead) is AttributeReference att)
                        {
                            if (string.Equals(att.Tag, "CX", StringComparison.OrdinalIgnoreCase)) cx = att.TextString?.Trim() ?? "";
                            if (string.Equals(att.Tag, "POT", StringComparison.OrdinalIgnoreCase))
                            {
                                string potStr = att.TextString?.Replace("VA", "")?.Replace("va", "")?.Trim() ?? "";
                                double.TryParse(potStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pot);
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(tipo) && string.IsNullOrEmpty(cx) && pot <= 0)
                        continue;

                    if (string.IsNullOrEmpty(tipo)) tipo = "BOCA";

                    if (pot <= 0)
                    {
                        if (tipo == "IUG" || tipo == "IUE") pot = 60;
                        else if (tipo == "TUG") pot = 150;
                        else if (tipo == "TUE") pot = 500;
                    }

                    artefactos.Add((new Point2D(br.Position.X, br.Position.Y), tipo, cx, pot, bName));
                }

                tr.Commit();
            }
            catch { }

            // Mapear cada artefacto al recinto cuyo polígono lo contiene
            foreach (var room in rooms)
            {
                if (room.Vertices == null || room.Vertices.Count < 3) continue;

                var circuitosSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int luces = 0;
                int tomas = 0;
                int otros = 0;
                double potTotal = 0;
                var detalle = new List<string>();

                foreach (var art in artefactos)
                {
                    if (PolygonMath.PointInPolygon(art.Pos, room.Vertices))
                    {
                        if (art.Tipo == "IUG" || art.Tipo == "IUE") luces++;
                        else if (art.Tipo == "TUG" || art.Tipo == "TUE") tomas++;
                        else otros++;

                        if (!string.IsNullOrEmpty(art.CX)) circuitosSet.Add(art.CX);
                        potTotal += art.Potencia;
                        detalle.Add($"{art.Tipo} ({art.NombreBloque}) [{(string.IsNullOrEmpty(art.CX) ? "S/C" : art.CX)}]: {art.Potencia:F0}VA");
                    }
                }

                room.CantidadLuminarias = luces;
                room.CantidadTomas = tomas;
                room.CantidadOtros = otros;
                room.PotenciaTotalVA = potTotal;
                room.CircuitosDetalle = circuitosSet.Count > 0 ? string.Join(", ", circuitosSet.OrderBy(c => c)) : "—";
                room.ArtefactosDetalle = detalle;
            }
        }
    }
}
