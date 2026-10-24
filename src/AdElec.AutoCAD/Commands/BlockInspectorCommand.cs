using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Commands
{
    public class BlockInspectorCommand
    {
        [CommandMethod("ADE_INSPECT")]
        public void InspectBlock()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var editor = doc.Editor;
            var db = doc.Database;

            // Prompt user to select a block
            var peo = new PromptEntityOptions("\nSeleccione un bloque para inspeccionar: ");
            peo.SetRejectMessage("\nEl objeto seleccionado debe ser un bloque.");
            peo.AddAllowedClass(typeof(BlockReference), true);

            var result = editor.GetEntity(peo);
            if (result.Status != PromptStatus.OK) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blockRef = tr.GetObject(result.ObjectId, OpenMode.ForRead) as BlockReference;
                if (blockRef == null) return;

                // Resolving real name (dynamic blocks use anonymous names like *U4)
                string blockName = blockRef.Name;
                if (blockRef.IsDynamicBlock)
                {
                    var btr = (BlockTableRecord)tr.GetObject(blockRef.DynamicBlockTableRecord, OpenMode.ForRead);
                    blockName = btr.Name;
                }

                var blockInfo = new BlockInfoData
                {
                    BlockName = blockName,
                    Layer = blockRef.Layer,
                    Attributes = new List<AttributeInfo>(),
                    DynamicProperties = new List<DynamicPropInfo>()
                };

                // 1. Read Geometric Bounds and Position
                try
                {
                    var ext = blockRef.GeometricExtents;
                    blockInfo.Position = new[] { blockRef.Position.X, blockRef.Position.Y, blockRef.Position.Z };
                    blockInfo.MinPoint = new[] { ext.MinPoint.X, ext.MinPoint.Y, ext.MinPoint.Z };
                    blockInfo.MaxPoint = new[] { ext.MaxPoint.X, ext.MaxPoint.Y, ext.MaxPoint.Z };
                    blockInfo.Width = Math.Round(ext.MaxPoint.X - ext.MinPoint.X, 4);
                    blockInfo.Height = Math.Round(ext.MaxPoint.Y - ext.MinPoint.Y, 4);
                }
                catch { /* Puede fallar si está vacío */ }

                // 2. Read Attributes
                foreach (ObjectId attId in blockRef.AttributeCollection)
                {
                    var attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (attRef != null)
                    {
                        blockInfo.Attributes.Add(new AttributeInfo
                        {
                            Tag = attRef.Tag,
                            Value = attRef.TextString,
                            Position = new[] { Math.Round(attRef.Position.X - blockRef.Position.X, 4), Math.Round(attRef.Position.Y - blockRef.Position.Y, 4) }
                        });
                    }
                }

                // 2. Read Dynamic Properties (Visibility States, etc.)
                if (blockRef.IsDynamicBlock)
                {
                    foreach (DynamicBlockReferenceProperty prop in blockRef.DynamicBlockReferencePropertyCollection)
                    {
                        var dynPropInfo = new DynamicPropInfo
                        {
                            PropertyName = prop.PropertyName,
                            Value = prop.Value?.ToString() ?? "",
                            Description = prop.Description,
                            ReadOnly = prop.ReadOnly,
                            AllowedValues = new List<string>()
                        };

                        try
                        {
                            object[] allowed = prop.GetAllowedValues();
                            if (allowed != null)
                            {
                                foreach (var val in allowed)
                                {
                                    dynPropInfo.AllowedValues.Add(val?.ToString() ?? "");
                                }
                            }
                        }
                        catch { /* Ignorar si no permite obtener valores permitidos */ }

                        blockInfo.DynamicProperties.Add(dynPropInfo);
                    }
                }

                tr.Commit();

                // 3. Serialize and Output to JSON
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string jsonString = JsonSerializer.Serialize(blockInfo, options);
                    
                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    string filePath = Path.Combine(desktopPath, $"ADE_INSPECT_{blockName}_{DateTime.Now:HHmmss}.json");

                    File.WriteAllText(filePath, jsonString);
                editor.WriteMessage($"\n[Éxito] Inspección de bloque guardada en:\n{filePath}\n");
                }
                catch (System.Exception ex)
                {
                    editor.WriteMessage($"\n[Error] No se pudo guardar el archivo JSON: {ex.Message}\n");
                }
            }
        }

        [CommandMethod("ADE_INSPECT_FILE")]
        public void InspectDwgFile()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var editor = doc.Editor;

            var pfo = new PromptStringOptions("\nRuta del archivo DWG a inspeccionar (o Enter para UNIFV.3): ");
            pfo.AllowSpaces = true;
            var res = editor.GetString(pfo);
            if (res.Status != PromptStatus.OK) return;

            string path = res.StringResult?.Trim() ?? "";
            if (string.IsNullOrEmpty(path))
            {
                path = @"g:\PROYECTOS AD\AD-ELEC\blocks\UNIFILARES\UNIFV.3.dwg";
            }

            if (!File.Exists(path))
            {
                editor.WriteMessage($"\n[Error] Archivo no encontrado: {path}");
                return;
            }

            try
            {
                using var sideDb = new Database(false, true);
                sideDb.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
                using var tr = sideDb.TransactionManager.StartTransaction();

                var bt = (BlockTable)tr.GetObject(sideDb.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                editor.WriteMessage($"\n══ Inspección de: {Path.GetFileName(path)} ══");
                editor.WriteMessage($"\nEntidades en ModelSpace:");
                int count = 0;
                Extents3d? totalExt = null;

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    count++;
                    try
                    {
                        var ext = ent.GeometricExtents;
                        if (!totalExt.HasValue) totalExt = ext;
                        else totalExt = new Extents3d(
                            new Autodesk.AutoCAD.Geometry.Point3d(
                                Math.Min(totalExt.Value.MinPoint.X, ext.MinPoint.X),
                                Math.Min(totalExt.Value.MinPoint.Y, ext.MinPoint.Y),
                                Math.Min(totalExt.Value.MinPoint.Z, ext.MinPoint.Z)),
                            new Autodesk.AutoCAD.Geometry.Point3d(
                                Math.Max(totalExt.Value.MaxPoint.X, ext.MaxPoint.X),
                                Math.Max(totalExt.Value.MaxPoint.Y, ext.MaxPoint.Y),
                                Math.Max(totalExt.Value.MaxPoint.Z, ext.MaxPoint.Z)));
                    }
                    catch { }

                    if (ent is AttributeDefinition att)
                    {
                        editor.WriteMessage($"\n  AttDef: Tag='{att.Tag}', Prompt='{att.Prompt}', Pos=({att.Position.X:F3}, {att.Position.Y:F3})");
                    }
                }

                if (totalExt.HasValue)
                {
                    double w = totalExt.Value.MaxPoint.X - totalExt.Value.MinPoint.X;
                    double h = totalExt.Value.MaxPoint.Y - totalExt.Value.MinPoint.Y;
                    editor.WriteMessage($"\nTotal entidades: {count}");
                    editor.WriteMessage($"\nExtents Min: ({totalExt.Value.MinPoint.X:F3}, {totalExt.Value.MinPoint.Y:F3})");
                    editor.WriteMessage($"\nExtents Max: ({totalExt.Value.MaxPoint.X:F3}, {totalExt.Value.MaxPoint.Y:F3})");
                    editor.WriteMessage($"\nDimensiones: Ancho={w:F3}, Alto={h:F3}");
                }
                tr.Commit();
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\n[Error] Al inspeccionar archivo: {ex.Message}");
            }
        }
    }

    public class BlockInfoData
    {
        public string BlockName { get; set; } = "";
        public string Layer { get; set; } = "";
        public double[]? Position { get; set; }
        public double[]? MinPoint { get; set; }
        public double[]? MaxPoint { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public List<AttributeInfo> Attributes { get; set; } = new();
        public List<DynamicPropInfo> DynamicProperties { get; set; } = new();
    }

    public class AttributeInfo
    {
        public string Tag { get; set; } = "";
        public string Value { get; set; } = "";
        public double[]? Position { get; set; }
    }

    public class DynamicPropInfo
    {
        public string PropertyName { get; set; }
        public string Value { get; set; }
        public List<string> AllowedValues { get; set; }
        public string Description { get; set; }
        public bool ReadOnly { get; set; }
    }
}
