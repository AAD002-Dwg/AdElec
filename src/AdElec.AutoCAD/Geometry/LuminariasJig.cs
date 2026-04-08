using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using AdElec.Core.Algorithms;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Geometry
{
    /// <summary>
    /// Motor de previsualización interactiva para distribución de luminarias.
    /// Usa TransientManager (gráficos temporales) + bucle con AllowArbitraryInput.
    ///
    /// Controles:
    ///   W → +fila   S → -fila
    ///   A → +col    D → -col
    ///   Enter        → confirmar
    ///   Escape       → cancelar
    /// </summary>
    public class LuminariasInteractive
    {
        private readonly Point3d           _minPt;
        private readonly double            _width;
        private readonly double            _length;
        private readonly IList<GridPoint>  _polygon;   // vértices del recinto para filtro
        private int _columns;
        private int _rows;

        private readonly List<DBObject> _transients = new();

        public int  FinalColumns => _columns;
        public int  FinalRows    => _rows;
        public bool Confirmed    { get; private set; }

        /// <param name="polygon">
        /// Vértices en coordenadas mundo del contorno del recinto.
        /// Si está vacío no se aplica filtro punto-en-polígono.
        /// </param>
        public LuminariasInteractive(
            Point3d minPt, double width, double length,
            int initialCols, int initialRows,
            IList<GridPoint>? polygon = null)
        {
            _minPt   = minPt;
            _width   = width;
            _length  = length;
            _columns = Math.Max(1, initialCols);
            _rows    = Math.Max(1, initialRows);
            _polygon = polygon ?? Array.Empty<GridPoint>();
        }

        /// <summary>Ejecuta el bucle interactivo.</summary>
        public void Run()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;

            try
            {
                UpdateGhostGraphics();

                while (true)
                {
                    int visible = CountVisible();
                    var pko = new PromptKeywordOptions(
                        $"\nLuminarias: {_columns}x{_rows} = {visible} | " +
                        "[W +fila][S -fila][A +col][D -col][confirmaR]: ");

                    // AllowArbitraryInput = true: la tecla se acepta sin necesitar Enter
                    pko.AllowArbitraryInput = true;
                    pko.AllowNone           = true;   // Enter vacío = confirmar
                    pko.Keywords.Add("W");
                    pko.Keywords.Add("S");
                    pko.Keywords.Add("A");
                    pko.Keywords.Add("D");
                    pko.Keywords.Add("confirmaR");

                    var pr = ed.GetKeywords(pko);

                    if (pr.Status == PromptStatus.None)          // Enter
                    { Confirmed = true; break; }

                    if (pr.Status == PromptStatus.Cancel)        // Esc
                    { Confirmed = false; break; }

                    if (pr.Status != PromptStatus.OK) continue;

                    switch (pr.StringResult.ToUpperInvariant())
                    {
                        case "W":        _rows    = Math.Min(_rows    + 1, 30); break;
                        case "S":        _rows    = Math.Max(_rows    - 1,  1); break;
                        case "A":        _columns = Math.Min(_columns + 1, 30); break;
                        case "D":        _columns = Math.Max(_columns - 1,  1); break;
                        case "CONFIRMAR": Confirmed = true; break;
                    }

                    if (Confirmed) break;
                    UpdateGhostGraphics();
                }
            }
            finally
            {
                ClearGhostGraphics();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Cuenta cuántos puntos de la grilla pasan el filtro polígono.</summary>
        private int CountVisible()
        {
            if (_polygon.Count < 3) return _columns * _rows;

            int count = 0;
            foreach (var gPt in GridCalculator.CalculateUniformGrid(_width, _length, _columns, _rows))
            {
                double wx = _minPt.X + gPt.X;
                double wy = _minPt.Y + gPt.Y;
                if (GridCalculator.IsPointInPolygon(wx, wy, _polygon)) count++;
            }
            return count;
        }

        private void UpdateGhostGraphics()
        {
            ClearGhostGraphics();

            var gridPoints = GridCalculator.CalculateUniformGrid(_width, _length, _columns, _rows);
            bool filterPoly = _polygon.Count >= 3;

            foreach (var gPt in gridPoints)
            {
                double wx = _minPt.X + gPt.X;
                double wy = _minPt.Y + gPt.Y;

                // Si hay polígono definido, omitir puntos fuera del recinto
                if (filterPoly && !GridCalculator.IsPointInPolygon(wx, wy, _polygon))
                    continue;

                AddGhostAt(new Point3d(wx, wy, 0), colorIndex: 2 /* amarillo */);
            }

            Application.DocumentManager.MdiActiveDocument?.Editor.UpdateScreen();
        }

        private void AddGhostAt(Point3d pt, short colorIndex)
        {
            const double R  = 0.15;
            const double cs = 0.08;

            var circle = new Circle(pt, Vector3d.ZAxis, R) { ColorIndex = colorIndex };
            var crossH = new Line(new Point3d(pt.X - cs, pt.Y, 0), new Point3d(pt.X + cs, pt.Y, 0)) { ColorIndex = colorIndex };
            var crossV = new Line(new Point3d(pt.X, pt.Y - cs, 0), new Point3d(pt.X, pt.Y + cs, 0)) { ColorIndex = colorIndex };

            var tm = TransientManager.CurrentTransientManager;
            var ic = new IntegerCollection();

            tm.AddTransient(circle, TransientDrawingMode.DirectShortTerm, 128, ic);
            tm.AddTransient(crossH, TransientDrawingMode.DirectShortTerm, 128, ic);
            tm.AddTransient(crossV, TransientDrawingMode.DirectShortTerm, 128, ic);

            _transients.Add(circle);
            _transients.Add(crossH);
            _transients.Add(crossV);
        }

        private void ClearGhostGraphics()
        {
            var tm = TransientManager.CurrentTransientManager;
            var ic = new IntegerCollection();
            foreach (var obj in _transients)
            {
                try { tm.EraseTransient(obj, ic); obj.Dispose(); } catch { }
            }
            _transients.Clear();
        }
    }
}
