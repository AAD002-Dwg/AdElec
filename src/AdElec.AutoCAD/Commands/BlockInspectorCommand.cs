using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

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

                // 1. Read Attributes
                foreach (ObjectId attId in blockRef.AttributeCollection)
                {
                    var attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (attRef != null)
                    {
                        blockInfo.Attributes.Add(new AttributeInfo
                        {
                            Tag = attRef.Tag,
                            Value = attRef.TextString
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
    }

    public class BlockInfoData
    {
        public string BlockName { get; set; }
        public string Layer { get; set; }
        public List<AttributeInfo> Attributes { get; set; }
        public List<DynamicPropInfo> DynamicProperties { get; set; }
    }

    public class AttributeInfo
    {
        public string Tag { get; set; }
        public string Value { get; set; }
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
