using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AdElec.AutoCAD.Geometry;
using AdElec.Core.Algorithms;
using System;

namespace AdElec.AutoCAD.Commands
{
    public class AutoPlacementCommands
    {
        [CommandMethod("ADE_LUMINARIAS")]
        public void AutoSembrarLuminarias()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            // Prompt user to select a polyline
            PromptEntityOptions peo = new PromptEntityOptions("\nSeleccione la Polilínea (contorno de la habitación): ");
            peo.SetRejectMessage("\nEntidad no válida. Debe ser una polilínea.");
            peo.AddAllowedClass(typeof(Polyline), true);
            
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            // Ask for grid rows and columns
            PromptIntegerOptions pioCols = new PromptIntegerOptions("\nIngrese cantidad de filas (X): ");
            pioCols.DefaultValue = 2;
            var pioColsRes = ed.GetInteger(pioCols);
            if (pioColsRes.Status != PromptStatus.OK) return;

            PromptIntegerOptions pioRows = new PromptIntegerOptions("\nIngrese cantidad de columnas (Y): ");
            pioRows.DefaultValue = 2;
            var pioRowsRes = ed.GetInteger(pioRows);
            if (pioRowsRes.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline poly = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                
                // 1. Get Bounding Box Dimensions
                var (minPt, width, length) = poly.GetBoundingBoxDimensions();

                // 2. Calculate abstract grid points
                var gridPoints = GridCalculator.CalculateUniformGrid(width, length, pioColsRes.Value, pioRowsRes.Value);

                // 3. Insert blocks or circles
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                foreach (var gPt in gridPoints)
                {
                    // Global point = BoundingBoxMinObject + Local Grid Point
                    Point3d insertionPt = new Point3d(minPt.X + gPt.X, minPt.Y + gPt.Y, 0);

                    // Check if point is inside the polyline (simple bounding box might generate points outside complex L-shaped rooms)
                    // For a robust plugin, point-in-polygon algorithm is used, but for now we insert blindly.
                    
                    // Default to creating a Circle if the "LUMINARIA" block doesn't exist
                    if (bt.Has("LUMINARIA"))
                    {
                        var blockId = bt["LUMINARIA"];
                        BlockReference br = new BlockReference(insertionPt, blockId);
                        btr.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);
                    }
                    else
                    {
                        Circle circle = new Circle(insertionPt, Vector3d.ZAxis, 0.2); // 20cm radius
                        circle.ColorIndex = 2; // Yellow
                        btr.AppendEntity(circle);
                        tr.AddNewlyCreatedDBObject(circle, true);
                    }
                }
                
                tr.Commit();
                ed.WriteMessage($"\nSe insertaron {gridPoints.Count} luminarias.");
            }
        }

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
            pdo.DefaultValue = 3.0; // Typical 3 meters rule
            var pdoRes = ed.GetDouble(pdo);
            if (pdoRes.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline poly = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                
                // Get perimeter points
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
                        // Rotate block to align with wall (tangent)
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
