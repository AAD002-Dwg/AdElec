using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AdElec.AutoCAD.Helpers;
using AdElec.AutoCAD.Repositories;
using AdElec.AutoCAD.Managers;
using AdElec.Core.AeaMotor;
using AdElec.Core.AeaMotor.Dtos;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Commands
{
    public class SyncReceiverCommand
    {
        [CommandMethod("ADE_PULL")]
        public async void PullFromWeb()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                var projectRepo = new DwgProjectRepository();
                int projectId = projectRepo.GetProjectId();

                if (projectId <= 0)
                {
                    ed.WriteMessage("\n[Error] El dibujo no está vinculado a ningún proyecto de AEA-MOTOR.");
                    return;
                }

                ed.WriteMessage($"\nSincronizando con Proyecto #{projectId}...");

                var motorClient = new AeaMotorClient();
                var project = await motorClient.GetProjectAsync(projectId);

                if (project?.DataJson?.AdElec?.Rooms == null)
                {
                    ed.WriteMessage("\n[Error] No se encontraron datos eléctricos (AdElec) en el servidor.");
                    return;
                }

                // Obtener todos los puntos de la web
                var webPoints = project.DataJson.AdElec.Rooms
                    .SelectMany(r => r.Points)
                    .ToList();

                ed.WriteMessage($"\n✓ Datos recibidos: {webPoints.Count} puntos en la Web.");

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    // 1. Escanear bloques existentes con WebID
                    var cadBlocks = ScanBlocksWithWebId(db, tr);
                    ed.WriteMessage($"\n✓ Bloques con vínculo encontrados en CAD: {cadBlocks.Count}");

                    int updated = 0, created = 0, deleted = 0;

                    // 2. Procesar puntos de la Web
                    foreach (var pt in webPoints)
                    {
                        var pos = new Point3d(pt.X, pt.Y, 0);

                        if (cadBlocks.TryGetValue(pt.Id, out var existingBlockId))
                        {
                            // ACTUALIZAR EXISTENTE
                            var br = (BlockReference)tr.GetObject(existingBlockId, OpenMode.ForWrite);
                            if (br.Position.DistanceTo(pos) > 0.001)
                            {
                                br.Position = pos;
                                updated++;
                            }
                            // Aquí se podrían actualizar también los atributos (CX, etc) si vienen en el DTO
                        }
                        else
                        {
                            // CREAR NUEVO
                            string blockName = GetBlockName(pt.Type);
                            if (!string.IsNullOrEmpty(blockName) && bt.Has(blockName))
                            {
                                InsertarNuevaBoca(tr, space, bt[blockName], pos, pt, ed);
                                created++;
                            }
                        }
                    }

                    // 3. Manejar Huérfanos (están en CAD pero NO en Web)
                    var webIds = new Set<string>(webPoints.Select(p => p.Id));
                    foreach (var kvp in cadBlocks)
                    {
                        if (!webIds.Contains(kvp.Key))
                        {
                            var br = (BlockReference)tr.GetObject(kvp.Value, OpenMode.ForWrite);
                            // Por ahora: Marcarlos en otro color o capa en lugar de borrar
                            br.ColorIndex = 1; // Rojo = Alerta de borrado
                            deleted++;
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage($"\n\nSincronización finalizada:");
                    ed.WriteMessage($"\n  - Actualizados: {updated}");
                    ed.WriteMessage($"\n  - Creados:     {created}");
                    ed.WriteMessage($"\n  - Huérfanos:   {deleted} (marcados en rojo)");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error Crítico] ADE_PULL: {ex.Message}");
            }
        }

        private Dictionary<string, ObjectId> ScanBlocksWithWebId(Database db, Transaction tr)
        {
            var result = new Dictionary<string, ObjectId>();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

            foreach (ObjectId id in space)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead);
                if (ent is BlockReference br)
                {
                    string webId = XDataHelper.GetWebId(br);
                    if (!string.IsNullOrEmpty(webId))
                    {
                        result[webId] = id;
                    }
                }
            }
            return result;
        }

        private string GetBlockName(string type)
        {
            return type switch
            {
                "IUG" or "IUE" => "I.E-AD-07",
                "TUG" => "I.E-AD-09",
                "TUE" => "I.E-AD-09.02",
                "SWITCH" => "I.E-AD-01",
                _ => ""
            };
        }

        private void InsertarNuevaBoca(Transaction tr, BlockTableRecord space, ObjectId blockId, Point3d pos, ProposalPoint pt, Editor ed)
        {
            var br = new BlockReference(pos, blockId);
            space.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);

            // Sellar con el ID de la web
            XDataHelper.SetWebId(br, pt.Id, tr);

            // Poblar atributos básicos
            var blockDef = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
            foreach (ObjectId eid in blockDef)
            {
                var ent = tr.GetObject(eid, OpenMode.ForRead);
                if (ent is AttributeDefinition attDef && !attDef.Constant)
                {
                    var attRef = new AttributeReference();
                    attRef.SetAttributeFromBlock(attDef, br.BlockTransform);
                    attRef.Position = attDef.Position.TransformBy(br.BlockTransform);
                    
                    // Mapeo simple de atributos
                    if (attDef.Tag == "CX") attRef.TextString = pt.CircuitId ?? "";
                    if (attDef.Tag == "POT") attRef.TextString = pt.PowerVa.ToString("F0");
                    if (attDef.Tag == "LLAVE-N°0" && pt.Type == "SWITCH") attRef.TextString = pt.SwitchId ?? "";

                    br.AttributeCollection.AppendAttribute(attRef);
                    tr.AddNewlyCreatedDBObject(attRef, true);
                }
            }
        }
    }

    /// <summary>Set polifill for older .NET versions if needed, though Set exists in modern C#.</summary>
    internal class Set<T> : HashSet<T> { public Set(IEnumerable<T> items) : base(items) { } }
}
