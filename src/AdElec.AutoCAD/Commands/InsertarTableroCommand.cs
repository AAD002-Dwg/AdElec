using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AdElec.AutoCAD.Managers;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Commands
{
    public class InsertarTableroCommand
    {
        private const string BLOCK_TABLERO = "I.E-AD-04";
        private const string BLOCK_FILE_TABLERO = "Bloques_CAD\\I.E-AD-04.dwg";

        // Parámetros que puede fijar la paleta antes de ejecutar el comando
        public static string PendingPanelName { get; set; } = "";
        public static string PendingVisibilidad { get; set; } = "Tablero Seccional";

        [CommandMethod("ADE_INSERTAR_TABLERO")]
        public void InsertarTablero()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            string panelName = PendingPanelName;
            string visibilidad = PendingVisibilidad;

            // Si no viene de la paleta, pedirlo manualmente
            if (string.IsNullOrWhiteSpace(panelName))
            {
                var pso = new PromptStringOptions("\nNombre del tablero [TD1]: ");
                pso.DefaultValue = "TD1";
                pso.AllowSpaces = false;
                var psoRes = ed.GetString(pso);
                if (psoRes.Status != PromptStatus.OK) return;
                panelName = string.IsNullOrWhiteSpace(psoRes.StringResult) ? "TD1" : psoRes.StringResult.Trim().ToUpper();
            }

            // Pedir punto de inserción
            var ppo = new PromptPointOptions($"\nUbicá el tablero '{panelName}' en el plano: ");
            ppo.AllowNone = false;
            var pprRes = ed.GetPoint(ppo);
            if (pprRes.Status != PromptStatus.OK)
            {
                PendingPanelName = "";
                return;
            }

            Point3d insertPt = pprRes.Value;

            // Cargar bloque si no existe
            string blockPath = ResolveBlockPath(db, BLOCK_FILE_TABLERO);
            if (!BlockManager.EnsureBlockExists(BLOCK_TABLERO, blockPath))
            {
                ed.WriteMessage($"\n[AD-ELEC] No se pudo cargar '{BLOCK_TABLERO}'. Ruta: {blockPath}");
                ed.WriteMessage("\n          Insertando marcador como placeholder.\n");
            }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                if (bt.Has(BLOCK_TABLERO))
                {
                    ObjectId blockId = bt[BLOCK_TABLERO];
                    var br = new BlockReference(insertPt, blockId);
                    space.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);

                    // Asignar atributos
                    BlockTableRecord blockDef = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                    foreach (ObjectId entId in blockDef)
                    {
                        var ent = tr.GetObject(entId, OpenMode.ForRead);
                        if (ent is AttributeDefinition attDef && !attDef.Constant)
                        {
                            var attRef = new AttributeReference();
                            attRef.SetAttributeFromBlock(attDef, br.BlockTransform);
                            attRef.Position = attDef.Position.TransformBy(br.BlockTransform);

                            // TX-X recibe el nombre del tablero (ej: "TD1", "TS1")
                            if (attDef.Tag.Equals("TX-X", StringComparison.OrdinalIgnoreCase))
                                attRef.TextString = panelName;
                            else
                                attRef.TextString = attDef.TextString;

                            br.AttributeCollection.AppendAttribute(attRef);
                            tr.AddNewlyCreatedDBObject(attRef, true);
                        }
                    }

                    // Visibilidad dinámica según tipo de tablero
                    if (br.IsDynamicBlock)
                    {
                        foreach (DynamicBlockReferenceProperty prop in br.DynamicBlockReferencePropertyCollection)
                        {
                            if (prop.PropertyName == "Visibilidad1" && !prop.ReadOnly)
                            {
                                try { prop.Value = visibilidad; }
                                catch { /* visibilidad no válida, dejar default */ }
                                break;
                            }
                        }
                    }

                    ed.WriteMessage($"\n✓ Tablero '{panelName}' insertado ({visibilidad}).");
                }
                else
                {
                    // Fallback: texto simple como placeholder
                    var mtext = new MText();
                    mtext.Location = insertPt;
                    mtext.Contents = $"[TABLERO: {panelName}]";
                    mtext.TextHeight = 0.3;
                    space.AppendEntity(mtext);
                    tr.AddNewlyCreatedDBObject(mtext, true);
                    ed.WriteMessage($"\n[!] Bloque no disponible. Se insertó texto placeholder para '{panelName}'.");
                }

                tr.Commit();
            }

            // Limpiar parámetros pendientes
            PendingPanelName = "";
            PendingVisibilidad = "Tablero Seccional";
        }

        private string ResolveBlockPath(Database db, string relativeBlockPath)
        {
            string dwgDir = Path.GetDirectoryName(db.Filename) ?? "";
            if (!string.IsNullOrEmpty(dwgDir))
            {
                string candidate = Path.Combine(dwgDir, relativeBlockPath);
                if (File.Exists(candidate)) return candidate;
            }

            // Buscar desde el ensamblado actual (DLL de AD-ELEC)
            string assemblyDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            // Subir hasta encontrar la raíz con Bloques_CAD
            string dir = assemblyDir;
            for (int i = 0; i < 6; i++)
            {
                string candidate = Path.Combine(dir, relativeBlockPath);
                if (File.Exists(candidate)) return candidate;
                string? parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }

            return Path.Combine(assemblyDir, relativeBlockPath);
        }
    }
}
