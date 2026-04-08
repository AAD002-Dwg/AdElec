using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AdElec.AutoCAD.Geometry;
using AdElec.AutoCAD.Managers;
using AdElec.Core.Algorithms;
using System;
using System.IO;

namespace AdElec.AutoCAD.Commands
{
    public class AutoPlacementCommands
    {
        // Nombre del bloque real de luminarias y su ruta relativa
        private const string BLOCK_LUMINARIA = "I.E-AD-07";
        private const string BLOCK_FILE_LUMINARIA = "Bloques_CAD\\I.E-AD-07.dwg";

        [CommandMethod("ADE_LUMINARIAS")]
        public void AutoSembrarLuminarias()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            // ──────────────────────────────────────────────────
            // 1. Solicitar el nombre del circuito (CX)
            // ──────────────────────────────────────────────────
            PromptStringOptions pso = new PromptStringOptions("\nIngrese el Circuito [C1]: ");
            pso.DefaultValue = "C1";
            pso.AllowSpaces = false;
            var psoRes = ed.GetString(pso);
            if (psoRes.Status != PromptStatus.OK) return;
            string circuitName = string.IsNullOrWhiteSpace(psoRes.StringResult)
                ? "C1"
                : psoRes.StringResult.Trim().ToUpper();

            // ──────────────────────────────────────────────────
            // 2. Seleccionar la polilínea cerrada (habitación)
            // ──────────────────────────────────────────────────
            PromptEntityOptions peo = new PromptEntityOptions("\nSeleccione la Polilínea cerrada (contorno de la habitación): ");
            peo.SetRejectMessage("\nEntidad no válida. Debe ser una polilínea.");
            peo.AddAllowedClass(typeof(Polyline), true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            // ──────────────────────────────────────────────────
            // 3. Asegurar que el bloque I.E-AD-07 existe
            // ──────────────────────────────────────────────────
            string blockFilePath = ResolveBlockPath(db, BLOCK_FILE_LUMINARIA);
            if (!BlockManager.EnsureBlockExists(BLOCK_LUMINARIA, blockFilePath))
            {
                ed.WriteMessage($"\n[AD-ELEC] No se pudo cargar el bloque '{BLOCK_LUMINARIA}'.");
                ed.WriteMessage($"\n          Ruta buscada: {blockFilePath}");
                ed.WriteMessage("\n          Insertaremos círculos amarillos como placeholder.\n");
            }

            // ──────────────────────────────────────────────────
            // 4. Obtener dimensiones de la polilínea
            // ──────────────────────────────────────────────────
            Point3d minPt;
            double width, length;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline poly = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                (minPt, width, length) = poly.GetBoundingBoxDimensions();
                tr.Commit();
            }

            if (width < 0.1 || length < 0.1)
            {
                ed.WriteMessage("\nLa polilínea es demasiado pequeña. Operación cancelada.");
                return;
            }

            // ──────────────────────────────────────────────────
            // 5. Lanzar el DrawJig interactivo
            // ──────────────────────────────────────────────────
            int initialCols = Math.Max(1, (int)Math.Round(width / 2.5));
            int initialRows = Math.Max(1, (int)Math.Round(length / 2.5));

            var jig = new LuminariasJig(minPt, width, length, initialCols, initialRows);

            ed.WriteMessage("\n──────────────────────────────────────────────");
            ed.WriteMessage("\n  MODO INTERACTIVO - Distribución de Luminarias");
            ed.WriteMessage("\n  Mouse: mueva el cursor para ajustar la densidad.");
            ed.WriteMessage("\n  Teclado: W(+fila) S(-fila) A(+col) D(-col)");
            ed.WriteMessage("\n  Click o Enter: confirmar distribución.");
            ed.WriteMessage("\n  Escape: cancelar.");
            ed.WriteMessage("\n──────────────────────────────────────────────\n");

            PromptResult jigResult = ed.Drag(jig);

            if (jig.Cancelled || jigResult.Status == PromptStatus.Cancel)
            {
                ed.WriteMessage("\nOperación cancelada por el usuario.");
                return;
            }

            int finalCols = jig.FinalColumns;
            int finalRows = jig.FinalRows;

            // ──────────────────────────────────────────────────
            // 6. Insertar los bloques definitivos con atributos
            // ──────────────────────────────────────────────────
            var gridPoints = GridCalculator.CalculateUniformGrid(width, length, finalCols, finalRows);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                bool useBlock = bt.Has(BLOCK_LUMINARIA);
                ObjectId blockId = useBlock ? bt[BLOCK_LUMINARIA] : ObjectId.Null;

                int insertedCount = 0;

                foreach (var gPt in gridPoints)
                {
                    Point3d insertionPt = new Point3d(minPt.X + gPt.X, minPt.Y + gPt.Y, 0);

                    if (useBlock)
                    {
                        InsertBlockWithAttributes(tr, btr, blockId, insertionPt, circuitName);
                    }
                    else
                    {
                        // Fallback: círculo amarillo como placeholder
                        Circle circle = new Circle(insertionPt, Vector3d.ZAxis, 0.15);
                        circle.ColorIndex = 2; // Yellow
                        btr.AppendEntity(circle);
                        tr.AddNewlyCreatedDBObject(circle, true);
                    }
                    insertedCount++;
                }

                tr.Commit();
                ed.WriteMessage($"\n✓ Se insertaron {insertedCount} luminarias ({finalCols}x{finalRows}) en circuito {circuitName}.");
            }
        }

        /// <summary>
        /// Inserta una BlockReference con sus AttributeReferences,
        /// y asigna el valor del circuito al atributo "CX".
        /// </summary>
        private void InsertBlockWithAttributes(
            Transaction tr,
            BlockTableRecord space,
            ObjectId blockDefId,
            Point3d insertionPoint,
            string circuitValue)
        {
            // Crear la referencia al bloque
            BlockReference br = new BlockReference(insertionPoint, blockDefId);
            space.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);

            // Leer la definición del bloque para extraer los AttributeDefinitions
            BlockTableRecord blockDef = (BlockTableRecord)tr.GetObject(blockDefId, OpenMode.ForRead);

            foreach (ObjectId entId in blockDef)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead);
                if (ent is AttributeDefinition attDef && !attDef.Constant)
                {
                    // Crear el AttributeReference correspondiente
                    AttributeReference attRef = new AttributeReference();
                    attRef.SetAttributeFromBlock(attDef, br.BlockTransform);
                    attRef.Position = attDef.Position.TransformBy(br.BlockTransform);

                    // Asignar el valor del circuito al tag "CX"
                    if (attDef.Tag.Equals("CX", StringComparison.OrdinalIgnoreCase))
                    {
                        attRef.TextString = circuitValue;
                    }
                    else
                    {
                        attRef.TextString = attDef.TextString;
                    }

                    br.AttributeCollection.AppendAttribute(attRef);
                    tr.AddNewlyCreatedDBObject(attRef, true);
                }
            }
        }

        /// <summary>
        /// Resuelve la ruta del archivo de bloque buscando primero junto al DWG actual
        /// y luego en la carpeta del proyecto.
        /// </summary>
        private string ResolveBlockPath(Database db, string relativeBlockPath)
        {
            // Intentar primero relativo al directorio del archivo DWG activo
            string dwgDir = Path.GetDirectoryName(db.Filename);
            if (!string.IsNullOrEmpty(dwgDir))
            {
                string candidate = Path.Combine(dwgDir, relativeBlockPath);
                if (File.Exists(candidate)) return candidate;
            }

            // Intentar en G:\AD-ELEC (raíz del proyecto)
            string projectRoot = @"G:\AD-ELEC";
            string projectCandidate = Path.Combine(projectRoot, relativeBlockPath);
            if (File.Exists(projectCandidate)) return projectCandidate;

            // Devolver la ruta del proyecto como fallback
            return projectCandidate;
        }

        // ═══════════════════════════════════════════════════════
        //  ADE_TOMAS (sin cambios por ahora)
        // ═══════════════════════════════════════════════════════

        [CommandMethod("ADE_TOMAS")]
        public void AutoSembrarTomas()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            PromptEntityOptions peo = new PromptEntityOptions("\nSeleccione la Polilínea de la pared: ");
            peo.SetRejectMessage("\nDebe ser una polilínea.");
            peo.AddAllowedClass(typeof(Polyline), true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            PromptDoubleOptions pdo = new PromptDoubleOptions("\nDistancia entre tomacorrientes (metros): ");
            pdo.DefaultValue = 3.0;
            var pdoRes = ed.GetDouble(pdo);
            if (pdoRes.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline poly = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                var pointsAndAngles = poly.GetPointsAlongPerimeter(pdoRes.Value);

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                foreach (var tuple in pointsAndAngles)
                {
                    Point3d pt = tuple.Point;
                    double angleRad = tuple.AngleRad;

                    if (bt.Has("TOMA"))
                    {
                        var blockId = bt["TOMA"];
                        BlockReference br = new BlockReference(pt, blockId);
                        br.Rotation = angleRad;
                        btr.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);
                    }
                    else
                    {
                        DBPoint dbPt = new DBPoint(pt);
                        dbPt.ColorIndex = 1; // Red
                        btr.AppendEntity(dbPt);
                        tr.AddNewlyCreatedDBObject(dbPt, true);
                    }
                }

                tr.Commit();
                ed.WriteMessage($"\nSe calcularon {pointsAndAngles.Count} posiciones para tomacorrientes a lo largo de {poly.Length:F2}m.");
            }
        }
    }
}

