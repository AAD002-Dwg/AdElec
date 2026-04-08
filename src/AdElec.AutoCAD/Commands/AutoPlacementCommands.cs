using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AdElec.AutoCAD.Geometry;
using AdElec.AutoCAD.Managers;
using AdElec.Core.Algorithms;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Commands
{
    public class AutoPlacementCommands
    {
        // ── Bloques ──────────────────────────────────────────────────────────
        private const string BLOCK_IUG  = "I.E-AD-07";
        private const string BLOCK_TUG  = "I.E-AD-09";
        private const string FILE_IUG   = "Bloques_CAD\\I.E-AD-07.dwg";
        private const string FILE_TUG   = "Bloques_CAD\\I.E-AD-09.dwg";

        // ════════════════════════════════════════════════════════════════════
        //  ADE_LUMINARIAS
        // ════════════════════════════════════════════════════════════════════

        [CommandMethod("ADE_LUMINARIAS")]
        public void AutoSembrarLuminarias()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            // 1. Circuito
            var pso = new PromptStringOptions("\nCircuito [C1]: ") { DefaultValue = "C1", AllowSpaces = false };
            var psoRes = ed.GetString(pso);
            if (psoRes.Status != PromptStatus.OK) return;
            string cx = string.IsNullOrWhiteSpace(psoRes.StringResult) ? "C1" : psoRes.StringResult.Trim().ToUpper();

            // 2. Tablero (D)
            var psoD = new PromptStringOptions("\nTablero (D) [TD1]: ") { DefaultValue = "TD1", AllowSpaces = false };
            var psoDRes = ed.GetString(psoD);
            if (psoDRes.Status != PromptStatus.OK) return;
            string tablero = string.IsNullOrWhiteSpace(psoDRes.StringResult) ? "TD1" : psoDRes.StringResult.Trim().ToUpper();

            // 3. Polilínea del recinto
            var peo = new PromptEntityOptions("\nSeleccioná el contorno del recinto (polilínea cerrada): ");
            peo.SetRejectMessage("\nDebe ser una polilínea cerrada.");
            peo.AddAllowedClass(typeof(Polyline), true);
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            // 4. Extraer bounding box + vértices del polígono
            Point3d minPt;
            double width, length;
            var polygon = new List<GridPoint>();

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var poly = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                if (!poly.Closed)
                {
                    ed.WriteMessage("\nLa polilínea debe estar cerrada."); tr.Commit(); return;
                }
                (minPt, width, length) = poly.GetBoundingBoxDimensions();

                for (int i = 0; i < poly.NumberOfVertices; i++)
                {
                    var v = poly.GetPoint2dAt(i);
                    polygon.Add(new GridPoint(v.X, v.Y));
                }
                tr.Commit();
            }

            if (width < 0.1 || length < 0.1) { ed.WriteMessage("\nRecinto demasiado pequeño."); return; }

            // 5. Cargar bloque
            BlockManager.EnsureBlockExists(BLOCK_IUG, ResolveBlockPath(db, FILE_IUG));

            // 6. Distribución interactiva (sin Enter para W/S/A/D)
            int initCols = Math.Max(1, (int)Math.Round(width  / 2.5));
            int initRows = Math.Max(1, (int)Math.Round(length / 2.5));

            ed.WriteMessage("\n── ADE_LUMINARIAS ───────────────────────────────");
            ed.WriteMessage("\n  W +fila  S -fila  A +col  D -col  Enter=confirmar");
            ed.WriteMessage("\n  Los puntos fuera del recinto se omiten automáticamente.");
            ed.WriteMessage("\n─────────────────────────────────────────────────\n");

            var interactive = new LuminariasInteractive(minPt, width, length, initCols, initRows, polygon);
            interactive.Run();

            if (!interactive.Confirmed) { ed.WriteMessage("\nCancelado."); return; }

            // 7. Insertar bloques filtrando por polígono
            var gridPoints = GridCalculator.CalculateUniformGrid(width, length, interactive.FinalColumns, interactive.FinalRows);
            bool filterPoly = polygon.Count >= 3;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt    = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                bool useBlock = bt.Has(BLOCK_IUG);
                ObjectId blockId = useBlock ? bt[BLOCK_IUG] : ObjectId.Null;

                int inserted = 0;
                foreach (var gPt in gridPoints)
                {
                    double wx = minPt.X + gPt.X;
                    double wy = minPt.Y + gPt.Y;

                    if (filterPoly && !GridCalculator.IsPointInPolygon(wx, wy, polygon)) continue;

                    var pos = new Point3d(wx, wy, 0);
                    if (useBlock)
                        InsertBlock(tr, space, blockId, pos, new() { ["CX"] = cx, ["D"] = tablero });
                    else
                    {
                        var c = new Circle(pos, Vector3d.ZAxis, 0.15) { ColorIndex = 2 };
                        space.AppendEntity(c); tr.AddNewlyCreatedDBObject(c, true);
                    }
                    inserted++;
                }
                tr.Commit();
                ed.WriteMessage($"\n✓ {inserted} luminarias insertadas ({interactive.FinalColumns}x{interactive.FinalRows}, dentro del recinto) — CX={cx} D={tablero}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  ADE_TOMAS
        // ════════════════════════════════════════════════════════════════════

        [CommandMethod("ADE_TOMAS")]
        public void AutoSembrarTomas()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            // Modo
            var pko = new PromptKeywordOptions(
                "\nModo [Perimetro/Manual/Desde]: ");
            pko.Keywords.Add("Perimetro");
            pko.Keywords.Add("Manual");
            pko.Keywords.Add("Desde");
            pko.Keywords.Default = "Perimetro";
            pko.AllowNone = true;
            var pkRes = ed.GetKeywords(pko);
            if (pkRes.Status == PromptStatus.Cancel) return;
            string modo = (pkRes.Status == PromptStatus.None || string.IsNullOrEmpty(pkRes.StringResult))
                ? "PERIMETRO"
                : pkRes.StringResult.ToUpperInvariant();

            // Circuito y tablero comunes
            var pso = new PromptStringOptions("\nCircuito [C1]: ") { DefaultValue = "C1" };
            var cxRes = ed.GetString(pso);
            if (cxRes.Status != PromptStatus.OK) return;
            string cx = string.IsNullOrWhiteSpace(cxRes.StringResult) ? "C1" : cxRes.StringResult.Trim().ToUpper();

            var psoD = new PromptStringOptions("\nTablero (D) [TD1]: ") { DefaultValue = "TD1" };
            var dRes = ed.GetString(psoD);
            if (dRes.Status != PromptStatus.OK) return;
            string tablero = string.IsNullOrWhiteSpace(dRes.StringResult) ? "TD1" : dRes.StringResult.Trim().ToUpper();

            // Cargar bloque
            BlockManager.EnsureBlockExists(BLOCK_TUG, ResolveBlockPath(db, FILE_TUG));

            switch (modo)
            {
                case "PERIMETRO": TomasPerimetro(doc, ed, db, cx, tablero); break;
                case "MANUAL":    TomasManual(doc, ed, db, cx, tablero);    break;
                case "DESDE":     TomasDesde(doc, ed, db, cx, tablero);     break;
            }
        }

        /// <summary>Distribuye tomas a lo largo del perímetro de una polilínea.</summary>
        private static void TomasPerimetro(Document doc, Editor ed, Database db, string cx, string tablero)
        {
            var peo = new PromptEntityOptions("\nSeleccioná la polilínea de pared: ");
            peo.SetRejectMessage("\nDebe ser una polilínea.");
            peo.AddAllowedClass(typeof(Polyline), true);
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            var pdo = new PromptDoubleOptions("\nDistancia entre tomas [3.0]: ") { DefaultValue = 3.0, AllowNegative = false };
            var spacingRes = ed.GetDouble(pdo);
            if (spacingRes.Status != PromptStatus.OK) return;

            using var tr = db.TransactionManager.StartTransaction();
            var poly  = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForRead);
            var bt    = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            var pts = poly.GetPointsAlongPerimeter(spacingRes.Value);
            bool useBlock = bt.Has(BLOCK_TUG);
            ObjectId blockId = useBlock ? bt[BLOCK_TUG] : ObjectId.Null;

            foreach (var (pt, angle) in pts)
            {
                if (useBlock)
                    InsertBlockRotated(tr, space, blockId, pt, angle + Math.PI / 2,
                        new() { ["CX"] = cx, ["D"] = tablero });
                else
                    PlaceDot(tr, space, pt);
            }
            tr.Commit();
            ed.WriteMessage($"\n✓ {pts.Count} tomas en perímetro — CX={cx} D={tablero}");
        }

        /// <summary>Colocación manual consecutiva: cada click inserta una toma. ESC para terminar.</summary>
        private static void TomasManual(Document doc, Editor ed, Database db, string cx, string tablero)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var bt    = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            bool useBlock = bt.Has(BLOCK_TUG);
            ObjectId blockId = useBlock ? bt[BLOCK_TUG] : ObjectId.Null;

            int count = 0;
            ed.WriteMessage("\nClick para colocar tomas. ESC para terminar.");

            while (true)
            {
                var ppo = new PromptPointOptions($"\nUbicación toma #{count + 1} [ESC para terminar]: ");
                ppo.AllowNone = false;
                var ptRes = ed.GetPoint(ppo);
                if (ptRes.Status != PromptStatus.OK) break;

                // Pedir ángulo de orientación
                var pao = new PromptAngleOptions("\nÁngulo de orientación [0]: ")
                    { DefaultValue = 0, AllowNone = true, UseDefaultValue = true };
                var angRes = ed.GetAngle(pao);
                double angle = angRes.Status == PromptStatus.OK ? angRes.Value : 0;

                if (useBlock)
                    InsertBlockRotated(tr, space, blockId, ptRes.Value, angle,
                        new() { ["CX"] = cx, ["D"] = tablero });
                else
                    PlaceDot(tr, space, ptRes.Value);
                count++;
            }
            tr.Commit();
            ed.WriteMessage($"\n✓ {count} tomas colocadas manualmente — CX={cx} D={tablero}");
        }

        /// <summary>Inserta una toma a una distancia dada desde un punto de referencia sobre una pared.</summary>
        private static void TomasDesde(Document doc, Editor ed, Database db, string cx, string tablero)
        {
            // Seleccionar segmento de pared (línea o polilínea)
            var peo = new PromptEntityOptions("\nSeleccioná la pared (línea o polilínea): ");
            peo.SetRejectMessage("\nDebe ser una línea o polilínea.");
            peo.AddAllowedClass(typeof(Line), true);
            peo.AddAllowedClass(typeof(Polyline), true);
            var perRes = ed.GetEntity(peo);
            if (perRes.Status != PromptStatus.OK) return;

            // Punto de referencia sobre la pared
            var ppoRef = new PromptPointOptions("\nPunto de referencia en la pared: ");
            var refRes = ed.GetPoint(ppoRef);
            if (refRes.Status != PromptStatus.OK) return;

            // Distancia
            var pdo = new PromptDoubleOptions("\nDistancia desde el punto de referencia: ")
                { DefaultValue = 1.0, AllowNegative = true };
            var distRes = ed.GetDouble(pdo);
            if (distRes.Status != PromptStatus.OK) return;

            using var tr = db.TransactionManager.StartTransaction();
            var ent   = tr.GetObject(perRes.ObjectId, OpenMode.ForRead);
            var bt    = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            // Calcular punto en la curva + dirección de la pared
            Point3d insertPt;
            double  wallAngle;

            if (ent is Line line)
            {
                Vector3d dir = (line.EndPoint - line.StartPoint).GetNormal();
                // Proyectar punto de referencia sobre la línea
                double t = (refRes.Value - line.StartPoint).DotProduct(dir);
                Point3d baseOnWall = line.StartPoint + dir * t;
                insertPt = baseOnWall + dir * distRes.Value;
                wallAngle = dir.AngleOnPlane(new Plane(Point3d.Origin, Vector3d.ZAxis));
            }
            else // Polyline
            {
                var poly = (Polyline)ent;
                double closestParam = poly.GetParameterAtPoint(poly.GetClosestPointTo(refRes.Value, false));
                double distOnPoly   = poly.GetDistanceAtParameter(closestParam) + distRes.Value;
                distOnPoly = Math.Max(0, Math.Min(distOnPoly, poly.Length));
                insertPt   = poly.GetPointAtDist(distOnPoly);
                Vector3d tangent = poly.GetFirstDerivative(poly.GetParameterAtDistance(distOnPoly));
                wallAngle = tangent.AngleOnPlane(new Plane(Point3d.Origin, Vector3d.ZAxis));
            }

            bool useBlock = bt.Has(BLOCK_TUG);
            if (useBlock)
                InsertBlockRotated(tr, space, bt[BLOCK_TUG], insertPt, wallAngle + Math.PI / 2,
                    new() { ["CX"] = cx, ["D"] = tablero });
            else
                PlaceDot(tr, space, insertPt);

            tr.Commit();
            ed.WriteMessage($"\n✓ Toma insertada en {insertPt.X:F3},{insertPt.Y:F3} — CX={cx} D={tablero}");
        }

        // ════════════════════════════════════════════════════════════════════
        //  ADE_CAÑERIAS
        // ════════════════════════════════════════════════════════════════════

        [CommandMethod("ADE_CAÑERIAS")]
        public void TrazarCanerias()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            var pko = new PromptKeywordOptions("\nModo [Luminarias/Tomas]: ");
            pko.Keywords.Add("Luminarias");
            pko.Keywords.Add("Tomas");
            pko.Keywords.Default = "Luminarias";
            pko.AllowNone = true;
            var mRes = ed.GetKeywords(pko);
            if (mRes.Status == PromptStatus.Cancel) return;
            string modo = (mRes.Status == PromptStatus.None || string.IsNullOrEmpty(mRes.StringResult))
                ? "LUMINARIAS"
                : mRes.StringResult.ToUpperInvariant();

            if (modo == "LUMINARIAS")
                CaneriasLuminarias(doc, ed, db);
            else
                CanerasTomas(doc, ed, db);
        }

        // ── ADE_CAÑERIAS LUMINARIAS ──────────────────────────────────────────

        /// <summary>
        /// Traza arcos de 45° entre bloques de luminaria.
        /// Modo Manual: el usuario selecciona los bloques uno a uno.
        /// Modo Auto:   filtra todos los I.E-AD-07 por CX y los encadena ordenados por posición.
        /// </summary>
        private static void CaneriasLuminarias(Document doc, Editor ed, Database db)
        {
            var pko = new PromptKeywordOptions("\nModo arcos [Manual/Auto]: ");
            pko.Keywords.Add("Manual");
            pko.Keywords.Add("Auto");
            pko.Keywords.Default = "Manual";
            pko.AllowNone = true;
            var mRes = ed.GetKeywords(pko);
            if (mRes.Status == PromptStatus.Cancel) return;
            bool autoMode = mRes.Status == PromptStatus.OK &&
                            mRes.StringResult.StartsWith("A", StringComparison.OrdinalIgnoreCase);

            // Capa de destino
            var psoLayer = new PromptStringOptions("\nCapa para arcos [Cañerias_Luz]: ") { DefaultValue = "Cañerias_Luz" };
            var layRes   = ed.GetString(psoLayer);
            if (layRes.Status != PromptStatus.OK) return;
            string layer = string.IsNullOrWhiteSpace(layRes.StringResult) ? "Cañerias_Luz" : layRes.StringResult.Trim();

            List<Point3d> points;

            if (autoMode)
            {
                var psoC = new PromptStringOptions("\nCircuito a cablear [C1]: ") { DefaultValue = "C1" };
                var cRes = ed.GetString(psoC);
                if (cRes.Status != PromptStatus.OK) return;
                string cx = string.IsNullOrWhiteSpace(cRes.StringResult) ? "C1" : cRes.StringResult.Trim().ToUpper();

                points = RecolectarPuntosPorCX(db, BLOCK_IUG, cx);
                if (points.Count < 2)
                { ed.WriteMessage($"\nMenos de 2 luminarias con CX={cx} en el dibujo."); return; }

                // Ordenar: fila por fila (Y desc, luego X asc) — recorre como texto
                points = points
                    .OrderByDescending(p => Math.Round(p.Y, 2))
                    .ThenBy(p => p.X)
                    .ToList();
                ed.WriteMessage($"\n✓ {points.Count} luminarias encontradas con CX={cx}.");
            }
            else
            {
                // Manual: el usuario selecciona bloques uno a uno
                points = new List<Point3d>();
                ed.WriteMessage("\nSeleccioná los bloques de luminaria en orden. ENTER para terminar.");

                while (true)
                {
                    var peo = new PromptEntityOptions(
                        $"\nLuminaria #{points.Count + 1} [ENTER para terminar]: ");
                    peo.AllowNone = true;
                    peo.AddAllowedClass(typeof(BlockReference), false);
                    var perR = ed.GetEntity(peo);
                    if (perR.Status == PromptStatus.None || perR.Status == PromptStatus.Cancel) break;
                    if (perR.Status != PromptStatus.OK) continue;

                    using var tr0 = db.TransactionManager.StartOpenCloseTransaction();
                    if (tr0.GetObject(perR.ObjectId, OpenMode.ForRead) is BlockReference br)
                        points.Add(br.Position);
                    tr0.Commit();
                }

                if (points.Count < 2) { ed.WriteMessage("\nSe necesitan al menos 2 bloques."); return; }
            }

            // Dibujar arcos encadenados
            EnsureLayer(db, layer, colorIndex: 4 /* cyan */);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                for (int i = 0; i < points.Count - 1; i++)
                    DrawArc45(tr, space, points[i], points[i + 1], layer);

                tr.Commit();
            }
            ed.WriteMessage($"\n✓ {points.Count - 1} arcos trazados en capa '{layer}'.");
        }

        /// <summary>
        /// Dibuja un arco de 45° entre dos puntos usando la geometría del LISP base:
        /// arco cuya cuerda va de p1 a p2 con ángulo de apertura de 45° (aprox).
        /// </summary>
        private static void DrawArc45(Transaction tr, BlockTableRecord space,
                                       Point3d p1, Point3d p2, string layer)
        {
            // Calcular el arco como en el LISP: ARC start Fwd end Angle 45°
            // Centro del arco: desplazar el punto medio en la dirección perpendicular
            double dx   = p2.X - p1.X;
            double dy   = p2.Y - p1.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 1e-6) return;

            // Chord length / 2 = dist/2
            // Para arco de 45° central angle: r = (dist/2) / sin(22.5°)
            const double halfAngle = 22.5 * Math.PI / 180.0;
            double r = (dist / 2.0) / Math.Sin(halfAngle);

            // Punto medio de la cuerda
            double mx = (p1.X + p2.X) / 2.0;
            double my = (p1.Y + p2.Y) / 2.0;

            // Dirección perpendicular (hacia arriba del arco)
            double perpX = -dy / dist;
            double perpY =  dx / dist;

            // Distancia del centro al punto medio de la cuerda
            double d = Math.Sqrt(r * r - (dist / 2.0) * (dist / 2.0));

            // Centro del arco (el arco "sube" hacia la perpendicular)
            double cx = mx + perpX * d;
            double cy = my + perpY * d;
            var center = new Point3d(cx, cy, 0);

            double startAngle = Math.Atan2(p1.Y - cy, p1.X - cx);
            double endAngle   = Math.Atan2(p2.Y - cy, p2.X - cx);

            // Asegurar que el arco va en sentido anti-horario
            if (endAngle < startAngle) endAngle += 2 * Math.PI;
            if (endAngle - startAngle > Math.PI) { startAngle += 2 * Math.PI; }

            var arc = new Arc(center, r, startAngle, endAngle)
            {
                Layer = layer,
                ColorIndex = 256 // ByLayer
            };
            space.AppendEntity(arc);
            tr.AddNewlyCreatedDBObject(arc, true);
        }

        // ── ADE_CAÑERIAS TOMAS ───────────────────────────────────────────────

        /// <summary>
        /// Traza polilíneas ortogonales (H+V con chamfer) entre bloques de toma.
        /// Modo Manual: el usuario selecciona los bloques en orden.
        /// Modo Auto:   filtra todos los I.E-AD-09 por CX y los encadena.
        /// </summary>
        private static void CanerasTomas(Document doc, Editor ed, Database db)
        {
            var pko = new PromptKeywordOptions("\nModo trazado [Manual/Auto]: ");
            pko.Keywords.Add("Manual");
            pko.Keywords.Add("Auto");
            pko.Keywords.Default = "Manual";
            pko.AllowNone = true;
            var mRes = ed.GetKeywords(pko);
            if (mRes.Status == PromptStatus.Cancel) return;
            bool autoMode = mRes.Status == PromptStatus.OK &&
                            mRes.StringResult.StartsWith("A", StringComparison.OrdinalIgnoreCase);

            string cx = "C1";
            if (autoMode)
            {
                var psoC = new PromptStringOptions("\nCircuito a cablear [C1]: ") { DefaultValue = "C1" };
                var cRes = ed.GetString(psoC);
                if (cRes.Status != PromptStatus.OK) return;
                cx = string.IsNullOrWhiteSpace(cRes.StringResult) ? "C1" : cRes.StringResult.Trim().ToUpper();
            }

            // Capa de destino
            var psoLayer = new PromptStringOptions($"\nCapa para canalización [Canalizacion_{cx}]: ")
                { DefaultValue = $"Canalizacion_{cx}" };
            var layRes = ed.GetString(psoLayer);
            if (layRes.Status != PromptStatus.OK) return;
            string layer = string.IsNullOrWhiteSpace(layRes.StringResult)
                ? $"Canalizacion_{cx}" : layRes.StringResult.Trim();

            List<Point3d> points;

            if (autoMode)
            {
                points = RecolectarPuntosPorCX(db, BLOCK_TUG, cx);
                if (points.Count < 2)
                { ed.WriteMessage($"\nMenos de 2 tomas con CX={cx} en el dibujo."); return; }

                // Ordenar por X luego Y
                points = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
                ed.WriteMessage($"\n✓ {points.Count} tomas encontradas con CX={cx}.");
            }
            else
            {
                points = new List<Point3d>();
                ed.WriteMessage("\nSeleccioná los bloques de toma en orden. ENTER para terminar.");

                while (true)
                {
                    var peo = new PromptEntityOptions(
                        $"\nToma #{points.Count + 1} [ENTER para terminar]: ");
                    peo.AllowNone = true;
                    peo.AddAllowedClass(typeof(BlockReference), false);
                    var perR = ed.GetEntity(peo);
                    if (perR.Status == PromptStatus.None || perR.Status == PromptStatus.Cancel) break;
                    if (perR.Status != PromptStatus.OK) continue;

                    using var tr0 = db.TransactionManager.StartOpenCloseTransaction();
                    if (tr0.GetObject(perR.ObjectId, OpenMode.ForRead) is BlockReference br)
                        points.Add(br.Position);
                    tr0.Commit();

                    // Puntos intermedios opcionales
                    ed.WriteMessage("\n  Punto intermedio? (ENTER para saltear)");
                    while (true)
                    {
                        var ppoMid = new PromptPointOptions("  Intermedio [ENTER=saltar]: ") { AllowNone = true };
                        var midRes = ed.GetPoint(ppoMid);
                        if (midRes.Status != PromptStatus.OK) break;
                        points.Add(midRes.Value);
                    }
                }

                if (points.Count < 2) { ed.WriteMessage("\nSe necesitan al menos 2 puntos."); return; }
            }

            EnsureLayer(db, layer, colorIndex: 1 /* rojo */);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                for (int i = 0; i < points.Count - 1; i++)
                    DrawOrthoRoute(tr, space, points[i], points[i + 1], layer);

                tr.Commit();
            }
            ed.WriteMessage($"\n✓ Canalización trazada en capa '{layer}' ({points.Count - 1} tramos).");
        }

        /// <summary>
        /// Dibuja una polilínea ortogonal (H → V o V → H) entre dos puntos.
        /// Si los puntos no están alineados genera un codo con chamfer de 45°.
        /// </summary>
        private static void DrawOrthoRoute(Transaction tr, BlockTableRecord space,
                                            Point3d p1, Point3d p2, string layer)
        {
            const double TOL = 1e-4;
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;

            var pline = new Polyline { Layer = layer, ColorIndex = 256 };

            if (Math.Abs(dx) < TOL || Math.Abs(dy) < TOL)
            {
                // Segmento recto (horizontal o vertical)
                pline.AddVertexAt(0, new Point2d(p1.X, p1.Y), 0, 0, 0);
                pline.AddVertexAt(1, new Point2d(p2.X, p2.Y), 0, 0, 0);
            }
            else
            {
                // Codo: va horizontal hasta x2, luego vertical; chamfer = min(|dx|,|dy|)
                double chamfer = Math.Min(Math.Abs(dx), Math.Abs(dy)) * 0.5;
                double signX   = dx > 0 ? 1 : -1;
                double signY   = dy > 0 ? 1 : -1;

                // Punto intermedio: esquina del codo
                double kx = p2.X;
                double ky = p1.Y;

                // Insertar vertices: p1 → antes_de_codo → codo_45 → despues_de_codo → p2
                var before = new Point2d(kx - signX * chamfer, ky);
                var after  = new Point2d(kx, ky + signY * chamfer);

                pline.AddVertexAt(0, new Point2d(p1.X, p1.Y), 0, 0, 0);
                pline.AddVertexAt(1, before, 0, 0, 0);
                pline.AddVertexAt(2, after, 0, 0, 0);
                pline.AddVertexAt(3, new Point2d(p2.X, p2.Y), 0, 0, 0);
            }

            space.AppendEntity(pline);
            tr.AddNewlyCreatedDBObject(pline, true);
        }

        // ════════════════════════════════════════════════════════════════════
        //  Helpers compartidos
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Escanea el espacio modelo buscando bloques con nombre dado y atributo CX = cx.
        /// Devuelve las posiciones de inserción.
        /// </summary>
        private static List<Point3d> RecolectarPuntosPorCX(Database db, string blockName, string cx)
        {
            var result = new List<Point3d>();
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

            foreach (ObjectId entId in space)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead);
                if (ent is not BlockReference br) continue;

                string bName = br.IsDynamicBlock
                    ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                    : ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;

                if (!string.Equals(bName, blockName, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (ObjectId attId in br.AttributeCollection)
                {
                    var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                    if (string.Equals(att.Tag, "CX", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(att.TextString?.Trim(), cx, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(br.Position);
                        break;
                    }
                }
            }
            tr.Commit();
            return result;
        }

        /// <summary>Inserta un bloque con atributos y rotación.</summary>
        private static void InsertBlockRotated(Transaction tr, BlockTableRecord space,
            ObjectId blockId, Point3d pos, double rotation,
            Dictionary<string, string> attrValues)
        {
            var br = new BlockReference(pos, blockId) { Rotation = rotation };
            space.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
            PopulateAttributes(tr, br, blockId, attrValues);
        }

        /// <summary>Inserta un bloque con atributos sin rotación.</summary>
        private static void InsertBlock(Transaction tr, BlockTableRecord space,
            ObjectId blockId, Point3d pos,
            Dictionary<string, string> attrValues)
            => InsertBlockRotated(tr, space, blockId, pos, 0, attrValues);

        private static void PopulateAttributes(Transaction tr, BlockReference br,
            ObjectId blockId, Dictionary<string, string> values)
        {
            var blockDef = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
            foreach (ObjectId eid in blockDef)
            {
                var ent = tr.GetObject(eid, OpenMode.ForRead);
                if (ent is not AttributeDefinition attDef || attDef.Constant) continue;

                var attRef = new AttributeReference();
                attRef.SetAttributeFromBlock(attDef, br.BlockTransform);
                attRef.Position = attDef.Position.TransformBy(br.BlockTransform);
                attRef.TextString = values.TryGetValue(attDef.Tag, out var v) ? v : attDef.TextString;
                br.AttributeCollection.AppendAttribute(attRef);
                tr.AddNewlyCreatedDBObject(attRef, true);
            }
        }

        private static void PlaceDot(Transaction tr, BlockTableRecord space, Point3d pt)
        {
            var d = new DBPoint(pt) { ColorIndex = 1 };
            space.AppendEntity(d);
            tr.AddNewlyCreatedDBObject(d, true);
        }

        /// <summary>Crea una capa si no existe.</summary>
        private static void EnsureLayer(Database db, string layerName, short colorIndex = 7)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                var ltr = new LayerTableRecord { Name = layerName };
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
            tr.Commit();
        }

        private static string ResolveBlockPath(Database db, string relPath)
        {
            string dwgDir = Path.GetDirectoryName(db.Filename) ?? "";
            if (!string.IsNullOrEmpty(dwgDir))
            {
                string c = Path.Combine(dwgDir, relPath);
                if (File.Exists(c)) return c;
            }
            string asmDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            string dir = asmDir;
            for (int i = 0; i < 6; i++)
            {
                string c = Path.Combine(dir, relPath);
                if (File.Exists(c)) return c;
                string? parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            return Path.Combine(asmDir, relPath);
        }
    }
}
