using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AdElec.AutoCAD.Managers;
using AdElec.AutoCAD.Commands;
using AdElec.Core.Algorithms;
using AdElec.Core.Models;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = Autodesk.AutoCAD.Colors.Color;
using Panel = AdElec.Core.Models.Panel;

namespace AdElec.AutoCAD.Services
{
    /// <summary>
    /// Generador automático de Diagramas Unifilares bajo norma AEA 90364-771
    /// utilizando los bloques dinámicos oficiales UNIFV.3, I.E-U-2A, I.E-U-1A e INFO E.I-1.1.
    /// </summary>
    public static class UnifilarGeneratorService
    {
        private const string LAYER_UNIFILAR = "I.E._UNIFILARES";
        private const string LAYER_PE = "I.E._PE";
        private const string LAYER_BORDE = "I.E._TABLERO_BORDE";

        public static bool Generate(Panel panel, Point3d origin, out string message)
        {
            message = "";
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                message = "No hay documento activo.";
                return false;
            }

            var db = doc.Database;

            // 1. Asegurar reserva obligatoria según norma AEA 771.8
            AeaRules.EnsureReserveCircuit(panel);

            // 2. Resolver rutas de bloques requeridos
            string blocksDir = ResolveBlocksDir();
            if (string.IsNullOrEmpty(blocksDir))
            {
                message = "No se encontró el directorio de bloques 'blocks/UNIFILARES'.";
                return false;
            }

            string pathUnifv3  = Path.Combine(blocksDir, "UNIFV.3.dwg");
            string pathItm     = Path.Combine(blocksDir, "I.E-U-2A.dwg");
            string pathDiff    = Path.Combine(blocksDir, "I.E-U-1A.dwg");
            string pathInfo    = Path.Combine(blocksDir, "INFO E.I-1.1.dwg");

            // 3. Importar bloques si no existen
            BlockManager.EnsureBlockExists("UNIFV.3", pathUnifv3);
            BlockManager.EnsureBlockExists("I.E-U-2A", pathItm);
            BlockManager.EnsureBlockExists("I.E-U-1A", pathDiff);
            BlockManager.EnsureBlockExists("INFO E.I-1.1", pathInfo);

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayers(db, tr);

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                if (!bt.Has("UNIFV.3") || !bt.Has("I.E-U-2A") || !bt.Has("I.E-U-1A") || !bt.Has("INFO E.I-1.1"))
                {
                    message = "Faltan bloques requeridos en el dibujo (UNIFV.3, I.E-U-2A, I.E-U-1A, INFO E.I-1.1).";
                    return false;
                }

                ObjectId btrColId  = bt["UNIFV.3"];
                ObjectId btrItmId  = bt["I.E-U-2A"];
                ObjectId btrDiffId = bt["I.E-U-1A"];
                ObjectId btrInfoId = bt["INFO E.I-1.1"];

                // 4. Medir columna patrón o usar valor canónico
                // Ancho de columna en bloques del usuario es 2.16 (como se deduce de Distancia1=8.64 para 4 cols)
                double colWidth = 2.16;
                double colHeight = 8.64;
                double tableTopY = origin.Y + colHeight;
                double tableBottomY = origin.Y;

                int numCols = panel.Circuits.Count;
                var colCenters = new List<double>();
                BlockReference? firstColBr = null;

                // 5. Insertar columnas UNIFV.3
                for (int i = 0; i < numCols; i++)
                {
                    var c = panel.Circuits[i];
                    double xCol = origin.X + (i * colWidth);
                    double cx = xCol + (colWidth / 2.0);
                    colCenters.Add(cx);

                    var colPos = new Point3d(xCol, origin.Y, 0);
                    var brCol = new BlockReference(colPos, btrColId)
                    {
                        Layer = LAYER_UNIFILAR
                    };
                    space.AppendEntity(brCol);
                    tr.AddNewlyCreatedDBObject(brCol, true);

                    if (i == 0) firstColBr = brCol;

                    // Formatear valores para unifilar según AEA
                    var (header, breakerStr, wireStr, phaseStr, descStr) = AeaRules.FormatForUnifilar(c, panel.PhaseCount);

                    var attValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["I.U.G"] = header,
                        ["REC"] = c.ArtefactCount.ToString(),
                        ["POT"] = (c.LoadVA / 1000.0).ToString("F2", CultureInfo.InvariantCulture),
                        ["TERMOMAGNÉTICA"] = breakerStr,
                        ["TERMOMAGNETICA"] = breakerStr,
                        ["RAMAL"] = wireStr,
                        ["NORMA IRAM DEL RAMAL"] = c.Norma,
                        ["NORMA"] = c.Norma,
                        ["FASE"] = phaseStr,
                        ["LOCAL01"] = !string.IsNullOrEmpty(c.Destinations) ? c.Destinations : "LOCAL",
                        ["ILUMINACIÓN"] = descStr,
                        ["ILUMINACION"] = descStr
                    };

                    AppendAttributes(tr, brCol, btrColId, attValues);
                }

                // Ajustar cotas exactas si el bloque arrojó GeometricExtents
                try
                {
                    if (firstColBr != null)
                    {
                        var ext = firstColBr.GeometricExtents;
                        double measuredW = ext.MaxPoint.X - ext.MinPoint.X;
                        if (measuredW > 0.5 && Math.Abs(measuredW - colWidth) > 0.05)
                        {
                            colWidth = measuredW;
                            tableTopY = ext.MaxPoint.Y;
                            tableBottomY = ext.MinPoint.Y;
                        }
                    }
                }
                catch { }

                // 6. Insertar Disyuntores Termomagnéticos (ITM - I.E-U-2A) para cada circuito
                double yBreaker = tableTopY + 3.0;
                double yBusbar  = yBreaker + 2.5;

                for (int i = 0; i < numCols; i++)
                {
                    var c = panel.Circuits[i];
                    double cx = colCenters[i];

                    var itmPos = new Point3d(cx, yBreaker, 0);
                    var brItm = new BlockReference(itmPos, btrItmId)
                    {
                        Layer = LAYER_UNIFILAR
                    };
                    space.AppendEntity(brItm);
                    tr.AddNewlyCreatedDBObject(brItm, true);

                    var itmAtts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["In:"] = $"{c.BreakerAmps:F0}A",
                        ["CURVA"] = c.Curve
                    };
                    AppendAttributes(tr, brItm, btrItmId, itmAtts);

                    // Dynamic Visibility (Bipolar / Tetrapolar)
                    string visItm = (c.Poles == 4 || panel.PhaseCount == 3) ? "Tetrapolar" : "Bipolar";
                    EfectoCommand.SetDynamicProperty(brItm, "Visibilidad1", visItm);

                    // Línea vertical: del ITM hacia la tabla
                    DrawLine(space, tr, new Point3d(cx, yBreaker - 0.5, 0), new Point3d(cx, tableTopY, 0), LAYER_UNIFILAR);

                    // Flecha downward apuntando a la celda del circuito
                    DrawArrow(space, tr, new Point3d(cx, tableTopY, 0), 0.25, LAYER_UNIFILAR);

                    // Conexión PE en la bajada
                    double yPeTap = tableTopY + 1.0;
                    DrawCircle(space, tr, new Point3d(cx, yPeTap, 0), 0.04, LAYER_PE);
                    DrawMText(space, tr, "PE", new Point3d(cx + 0.1, yPeTap - 0.05, 0), 0.18, LAYER_PE, 3);

                    // Línea vertical: de la barra general al ITM
                    DrawLine(space, tr, new Point3d(cx, yBusbar, 0), new Point3d(cx, yBreaker + 0.5, 0), LAYER_UNIFILAR);
                }

                // 7. Peine de distribución / Barra Colectora general
                double xBusStart = colCenters.First() - 0.2;
                double xBusEnd   = colCenters.Last() + 0.2;
                DrawThickLine(space, tr, new Point3d(xBusStart, yBusbar, 0), new Point3d(xBusEnd, yBusbar, 0), 0.08, LAYER_UNIFILAR);

                // 8. Interruptor Diferencial General (ID - I.E-U-1A)
                double xDiff = colCenters.First() + (colWidth * 0.5);
                double yDiff = yBusbar + 2.5;

                var diffPos = new Point3d(xDiff, yDiff, 0);
                var brDiff = new BlockReference(diffPos, btrDiffId)
                {
                    Layer = LAYER_UNIFILAR
                };
                space.AppendEntity(brDiff);
                tr.AddNewlyCreatedDBObject(brDiff, true);

                var diffAtts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["In:"] = $"{panel.MainDiffAmps:F0}A",
                    ["IΔn:"] = $"{panel.MainDiffSensitivityMa:F0}mA",
                    ["Un:"] = panel.PhaseCount == 3 ? "380" : "220"
                };
                AppendAttributes(tr, brDiff, btrDiffId, diffAtts);

                string visDiff = panel.PhaseCount == 3 ? "Tetrapolar" : "Bipolar";
                EfectoCommand.SetDynamicProperty(brDiff, "Visibilidad1", visDiff);

                // Bajada del ID hacia la barra
                DrawLine(space, tr, new Point3d(xDiff, yDiff - 0.5, 0), new Point3d(xDiff, yBusbar, 0), LAYER_UNIFILAR);

                // 9. Alimentador principal desde medidores
                double yFeeder = yDiff + 2.5;
                DrawLine(space, tr, new Point3d(xDiff, yDiff + 0.5, 0), new Point3d(xDiff, yFeeder, 0), LAYER_UNIFILAR);
                DrawFeederSymbol(space, tr, new Point3d(xDiff, yFeeder, 0), 0.35, LAYER_UNIFILAR);

                string feederText = !string.IsNullOrEmpty(panel.FeederFrom) ? panel.FeederFrom : "DESDE\nT.P - SALA DE MEDIDORES";
                DrawMText(space, tr, feederText, new Point3d(xDiff - 1.2, yFeeder + 1.2, 0), 0.24, LAYER_UNIFILAR, 7);

                // 10. Conductor de Protección (PE) y Puesta a Tierra
                double xPeRight = origin.X + (numCols * colWidth) + 0.8;
                double yPeTop   = yFeeder - 0.4;
                double yPeMain  = tableTopY + 1.0;

                // Derivación en alimentador
                DrawCircle(space, tr, new Point3d(xDiff, yPeTop, 0), 0.05, LAYER_PE);
                DrawMText(space, tr, "PE", new Point3d(xDiff + 0.1, yPeTop + 0.05, 0), 0.20, LAYER_PE, 3);

                // Recorrido PE superior y bajada derecha
                DrawLine(space, tr, new Point3d(xDiff, yPeTop, 0), new Point3d(xPeRight, yPeTop, 0), LAYER_PE, 3);
                DrawLine(space, tr, new Point3d(xPeRight, yPeTop, 0), new Point3d(xPeRight, yPeMain, 0), LAYER_PE, 3);

                // Línea colectora horizontal de PE que une todas las bajadas
                DrawLine(space, tr, new Point3d(colCenters.First(), yPeMain, 0), new Point3d(xPeRight + 1.5, yPeMain, 0), LAYER_PE, 3);
                DrawCircle(space, tr, new Point3d(xPeRight, yPeMain, 0), 0.05, LAYER_PE);

                // Símbolo puesta a tierra a la derecha
                double xEarth = xPeRight + 1.5;
                DrawEarthGroundSymbol(space, tr, new Point3d(xEarth, yPeMain, 0), 0.4, LAYER_PE);
                DrawMText(space, tr, "SISTEMA\nPUESTA\nA TIERRA", new Point3d(xEarth - 0.3, yPeMain + 0.9, 0), 0.18, LAYER_PE, 3);

                // 11. Cuadro de Resumen de Cargas (INFO E.I-1.1)
                double totalWidth = numCols * colWidth;
                var infoPos = new Point3d(origin.X + totalWidth, tableBottomY, 0);

                var brInfo = new BlockReference(infoPos, btrInfoId)
                {
                    Layer = LAYER_UNIFILAR
                };
                space.AppendEntity(brInfo);
                tr.AddNewlyCreatedDBObject(brInfo, true);

                var infoAtts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["KVA"]  = panel.TotalLoadKVA().ToString("F2", CultureInfo.InvariantCulture),
                    ["%"]    = (panel.SimultaneityFactor * 100.0).ToString("F0"),
                    ["KW1"]  = panel.TotalLoadKW().ToString("F2", CultureInfo.InvariantCulture),
                    ["KW2"]  = panel.SimultaneousKW().ToString("F2", CultureInfo.InvariantCulture),
                    ["UF"]   = panel.FunctionalUnitsCount.ToString(),
                    ["KW2.1"] = panel.SimultaneousKW().ToString("F2", CultureInfo.InvariantCulture),
                    ["KW3"]  = panel.TotalProjectKW().ToString("F2", CultureInfo.InvariantCulture)
                };
                AppendAttributes(tr, brInfo, btrInfoId, infoAtts);

                // Propiedades dinámicas de INFO E.I-1.1
                EfectoCommand.SetDynamicProperty(brInfo, "Distancia1", totalWidth);
                string infoVis = panel.FunctionalUnitsCount > 1 ? "Con total" : "Sin Total";
                EfectoCommand.SetDynamicProperty(brInfo, "Visibilidad1", infoVis);

                // 12. Encabezado de filas a la izquierda (Grid de Nombres de Fila)
                double headerW = 3.2;
                double xHeader = origin.X - headerW;
                DrawTableHeaders(space, tr, xHeader, headerW, tableBottomY, tableTopY);

                // 13. Caja perimetral discontinua y rótulo del tablero
                double pad = 1.2;
                double boxMinX = xHeader - pad;
                double boxMaxX = xEarth + pad;
                double boxMinY = tableBottomY - 1.2;
                double boxMaxY = yFeeder + 1.8;

                DrawDashedBox(space, tr, boxMinX, boxMinY, boxMaxX, boxMaxY, LAYER_BORDE);

                // Título del tablero en esquina superior izquierda
                string title = $"Tablero Seccional // {(!string.IsNullOrEmpty(panel.Location) ? panel.Location : "General")}\n" +
                               $"{panel.Name}\n" +
                               $"Total: {panel.FunctionalUnitsCount} UF | P.Simult: {panel.SimultaneousKW():F2} kW";
                DrawMText(space, tr, title, new Point3d(boxMinX + 0.5, boxMaxY - 0.5, 0), 0.28, LAYER_UNIFILAR, 7);

                tr.Commit();
            }

            message = $"✓ Diagrama Unifilar generado exitosamente para el tablero '{panel.Name}' ({panel.Circuits.Count} circuitos).";
            return true;
        }

        private static string ResolveBlocksDir()
        {
            var candidates = new List<string>
            {
                @"g:\PROYECTOS AD\AD-ELEC\blocks\UNIFILARES",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blocks", "UNIFILARES"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "blocks", "UNIFILARES"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "blocks", "UNIFILARES"),
            };

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                string dwgDir = Path.GetDirectoryName(doc.Name) ?? "";
                if (!string.IsNullOrEmpty(dwgDir))
                {
                    candidates.Insert(0, Path.Combine(dwgDir, "blocks", "UNIFILARES"));
                }
            }

            return candidates.FirstOrDefault(Directory.Exists) ?? "";
        }

        private static void AppendAttributes(Transaction tr, BlockReference br, ObjectId btrId, Dictionary<string, string> values)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead);
                if (ent is AttributeDefinition attDef && !attDef.Constant)
                {
                    var attRef = new AttributeReference();
                    attRef.SetAttributeFromBlock(attDef, br.BlockTransform);
                    if (values.TryGetValue(attDef.Tag, out string? val))
                    {
                        attRef.TextString = val;
                    }
                    br.AttributeCollection.AppendAttribute(attRef);
                    tr.AddNewlyCreatedDBObject(attRef, true);
                }
            }
        }

        private static void DrawTableHeaders(BlockTableRecord space, Transaction tr, double x, double w, double minY, double maxY)
        {
            // Dibuja el marco de la columna de encabezados a la izquierda
            var rect = new Polyline(4);
            rect.AddVertexAt(0, new Point2d(x, minY), 0, 0, 0);
            rect.AddVertexAt(1, new Point2d(x + w, minY), 0, 0, 0);
            rect.AddVertexAt(2, new Point2d(x + w, maxY), 0, 0, 0);
            rect.AddVertexAt(3, new Point2d(x, maxY), 0, 0, 0);
            rect.Closed = true;
            rect.Layer = LAYER_UNIFILAR;
            space.AppendEntity(rect);
            tr.AddNewlyCreatedDBObject(rect, true);

            // Nombres reglamentarios de las filas alineados verticalmente
            string[] headers =
            {
                "NUMERO DE CIRCUITO",
                "Nº DE ARTEFACTOS",
                "POTENCIA (kVA)",
                "TERMOMAGNETICA Icu4.5kA (A)",
                "RAMAL (mm²)",
                "NORMA IRAM DEL RAMAL",
                "FASE",
                "DESTINO"
            };

            double h = maxY - minY;
            double step = h / (headers.Length + 1);

            for (int i = 0; i < headers.Length; i++)
            {
                double y = maxY - ((i + 0.7) * step);
                DrawMText(space, tr, headers[i], new Point3d(x + 0.15, y, 0), 0.16, LAYER_UNIFILAR, 7);
                if (i > 0)
                {
                    DrawLine(space, tr, new Point3d(x, maxY - (i * step), 0), new Point3d(x + w, maxY - (i * step), 0), LAYER_UNIFILAR);
                }
            }

            // Fila inferior de POTENCIA TOTAL (kW)
            DrawMText(space, tr, "POTENCIA TOTAL (kW)", new Point3d(x + 0.15, minY - 0.25, 0), 0.18, LAYER_UNIFILAR, 7);
        }

        private static void DrawLine(BlockTableRecord space, Transaction tr, Point3d p1, Point3d p2, string layer, short colorIndex = 256)
        {
            var line = new Line(p1, p2) { Layer = layer };
            if (colorIndex != 256) line.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            space.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        private static void DrawThickLine(BlockTableRecord space, Transaction tr, Point3d p1, Point3d p2, double width, string layer)
        {
            var poly = new Polyline(2) { Layer = layer };
            poly.AddVertexAt(0, new Point2d(p1.X, p1.Y), 0, width, width);
            poly.AddVertexAt(1, new Point2d(p2.X, p2.Y), 0, width, width);
            space.AppendEntity(poly);
            tr.AddNewlyCreatedDBObject(poly, true);
        }

        private static void DrawCircle(BlockTableRecord space, Transaction tr, Point3d center, double radius, string layer)
        {
            var circle = new Circle(center, Vector3d.ZAxis, radius) { Layer = layer };
            space.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
        }

        private static void DrawArrow(BlockTableRecord space, Transaction tr, Point3d target, double size, string layer)
        {
            // Triángulo apuntando hacia abajo a target
            var poly = new Polyline(3) { Layer = layer, Closed = true };
            poly.AddVertexAt(0, new Point2d(target.X, target.Y), 0, 0, 0);
            poly.AddVertexAt(1, new Point2d(target.X - (size / 2.0), target.Y + size), 0, 0, 0);
            poly.AddVertexAt(2, new Point2d(target.X + (size / 2.0), target.Y + size), 0, 0, 0);
            space.AppendEntity(poly);
            tr.AddNewlyCreatedDBObject(poly, true);
        }

        private static void DrawFeederSymbol(BlockTableRecord space, Transaction tr, Point3d pos, double size, string layer)
        {
            // Símbolo de acometida / alimentador (triángulo invertido)
            var poly = new Polyline(3) { Layer = layer, Closed = true };
            poly.AddVertexAt(0, new Point2d(pos.X, pos.Y), 0, 0, 0);
            poly.AddVertexAt(1, new Point2d(pos.X - size, pos.Y + (size * 1.5)), 0, 0, 0);
            poly.AddVertexAt(2, new Point2d(pos.X + size, pos.Y + (size * 1.5)), 0, 0, 0);
            space.AppendEntity(poly);
            tr.AddNewlyCreatedDBObject(poly, true);
        }

        private static void DrawEarthGroundSymbol(BlockTableRecord space, Transaction tr, Point3d pos, double size, string layer)
        {
            // Símbolo de puesta a tierra: 3 líneas horizontales decrecientes
            DrawLine(space, tr, pos, new Point3d(pos.X, pos.Y - (size * 0.8), 0), layer, 3);
            double y = pos.Y - (size * 0.8);
            DrawLine(space, tr, new Point3d(pos.X - size, y, 0), new Point3d(pos.X + size, y, 0), layer, 3);
            DrawLine(space, tr, new Point3d(pos.X - (size * 0.6), y - (size * 0.3), 0), new Point3d(pos.X + (size * 0.6), y - (size * 0.3), 0), layer, 3);
            DrawLine(space, tr, new Point3d(pos.X - (size * 0.25), y - (size * 0.6), 0), new Point3d(pos.X + (size * 0.25), y - (size * 0.6), 0), layer, 3);
        }

        private static void DrawDashedBox(BlockTableRecord space, Transaction tr, double minX, double minY, double maxX, double maxY, string layer)
        {
            var poly = new Polyline(4) { Layer = layer, Closed = true };
            poly.AddVertexAt(0, new Point2d(minX, minY), 0, 0, 0);
            poly.AddVertexAt(1, new Point2d(maxX, minY), 0, 0, 0);
            poly.AddVertexAt(2, new Point2d(maxX, maxY), 0, 0, 0);
            poly.AddVertexAt(3, new Point2d(minX, maxY), 0, 0, 0);
            space.AppendEntity(poly);
            tr.AddNewlyCreatedDBObject(poly, true);
        }

        private static void DrawMText(BlockTableRecord space, Transaction tr, string text, Point3d pos, double height, string layer, short colorIndex = 256)
        {
            var mtext = new MText
            {
                Contents = text,
                Location = pos,
                TextHeight = height,
                Layer = layer
            };
            if (colorIndex != 256) mtext.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            space.AppendEntity(mtext);
            tr.AddNewlyCreatedDBObject(mtext, true);
        }

        private static void EnsureLayers(Database db, Transaction tr)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);

            CreateLayerIfNotExists(lt, tr, LAYER_UNIFILAR, Color.FromColorIndex(ColorMethod.ByAci, 7), "Continuous");
            CreateLayerIfNotExists(lt, tr, LAYER_PE, Color.FromColorIndex(ColorMethod.ByAci, 3), "Continuous");
            CreateLayerIfNotExists(lt, tr, LAYER_BORDE, Color.FromColorIndex(ColorMethod.ByAci, 8), "DASHED");
        }

        private static void CreateLayerIfNotExists(LayerTable lt, Transaction tr, string name, Color color, string lineTypeName)
        {
            if (!lt.Has(name))
            {
                var ltr = new LayerTableRecord
                {
                    Name = name,
                    Color = color
                };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }
    }
}
