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
    /// Usa TransientManager (gráficos temporales) + bucle de Keywords.
    /// 
    /// Controles:
    ///   W / S  → Aumentar / Disminuir filas
    ///   A / D  → Aumentar / Disminuir columnas
    ///   Enter o "confirmaR" → Aceptar la distribución
    ///   Escape → Cancelar
    /// </summary>
    public class LuminariasInteractive
    {
        private readonly Point3d _minPt;
        private readonly double _width;
        private readonly double _length;
        private int _columns;
        private int _rows;

        // Lista de entidades temporales (fantasmas) dibujadas
        private readonly List<DBObject> _transients = new List<DBObject>();

        public int FinalColumns => _columns;
        public int FinalRows => _rows;
        public bool Confirmed { get; private set; }

        public LuminariasInteractive(Point3d minPt, double width, double length, int initialCols, int initialRows)
        {
            _minPt = minPt;
            _width = width;
            _length = length;
            _columns = Math.Max(1, initialCols);
            _rows = Math.Max(1, initialRows);
            Confirmed = false;
        }

        /// <summary>
        /// Ejecuta el bucle interactivo. Dibuja fantasmas y espera input del teclado.
        /// </summary>
        public void Run()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            try
            {
                // Dibujar la previsualización inicial
                UpdateGhostGraphics();

                while (true)
                {
                    // Mostrar prompt con opciones de teclado
                    PromptKeywordOptions pko = new PromptKeywordOptions(
                        $"\nLuminarias: {_columns}x{_rows} = {_columns * _rows} | " +
                        $"[W +fila / S -fila / A +col / D -col / confirmaR]: ");

                    pko.Keywords.Add("W");
                    pko.Keywords.Add("S");
                    pko.Keywords.Add("A");
                    pko.Keywords.Add("D");
                    pko.Keywords.Add("confirmaR");

                    // Enter sin escribir nada = confirmar
                    pko.AllowNone = true;
                    pko.AllowArbitraryInput = false;

                    PromptResult pr = ed.GetKeywords(pko);

                    // Enter vacío → Confirmar
                    if (pr.Status == PromptStatus.None)
                    {
                        Confirmed = true;
                        break;
                    }

                    // Escape → Cancelar
                    if (pr.Status == PromptStatus.Cancel)
                    {
                        Confirmed = false;
                        break;
                    }

                    if (pr.Status == PromptStatus.OK)
                    {
                        string kw = pr.StringResult.ToUpper();
                        switch (kw)
                        {
                            case "W":
                                _rows = Math.Min(_rows + 1, 30);
                                break;
                            case "S":
                                _rows = Math.Max(_rows - 1, 1);
                                break;
                            case "A":
                                _columns = Math.Min(_columns + 1, 30);
                                break;
                            case "D":
                                _columns = Math.Max(_columns - 1, 1);
                                break;
                            case "CONFIRMAR":
                                Confirmed = true;
                                break;
                        }

                        if (Confirmed) break;

                        // Redibujar los fantasmas con la nueva cantidad
                        UpdateGhostGraphics();
                    }
                }
            }
            finally
            {
                // Siempre limpiar los gráficos temporales al salir
                ClearGhostGraphics();
            }
        }

        /// <summary>
        /// Recalcula y redibuja los círculos fantasma usando TransientManager.
        /// </summary>
        private void UpdateGhostGraphics()
        {
            // Limpiar los fantasmas anteriores
            ClearGhostGraphics();

            // Calcular las nuevas posiciones de grilla
            var gridPoints = GridCalculator.CalculateUniformGrid(_width, _length, _columns, _rows);

            foreach (var gPt in gridPoints)
            {
                Point3d pt = new Point3d(_minPt.X + gPt.X, _minPt.Y + gPt.Y, 0);

                // Crear un círculo fantasma (radio 0.15m, amarillo)
                Circle ghost = new Circle(pt, Vector3d.ZAxis, 0.15);
                ghost.ColorIndex = 2; // Amarillo

                // Crear las líneas de la cruz central (+)
                double cs = 0.08;
                Line crossH = new Line(
                    new Point3d(pt.X - cs, pt.Y, 0),
                    new Point3d(pt.X + cs, pt.Y, 0));
                crossH.ColorIndex = 2;

                Line crossV = new Line(
                    new Point3d(pt.X, pt.Y - cs, 0),
                    new Point3d(pt.X, pt.Y + cs, 0));
                crossV.ColorIndex = 2;

                // Agregar al TransientManager
                TransientManager.CurrentTransientManager.AddTransient(
                    ghost, TransientDrawingMode.DirectShortTerm,
                    128, new IntegerCollection());

                TransientManager.CurrentTransientManager.AddTransient(
                    crossH, TransientDrawingMode.DirectShortTerm,
                    128, new IntegerCollection());

                TransientManager.CurrentTransientManager.AddTransient(
                    crossV, TransientDrawingMode.DirectShortTerm,
                    128, new IntegerCollection());

                _transients.Add(ghost);
                _transients.Add(crossH);
                _transients.Add(crossV);
            }

            // Forzar un refresco de la vista
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.Editor.UpdateScreen();
        }

        /// <summary>
        /// Elimina todos los gráficos temporales de la pantalla.
        /// </summary>
        private void ClearGhostGraphics()
        {
            foreach (var obj in _transients)
            {
                try
                {
                    TransientManager.CurrentTransientManager.EraseTransient(
                        obj, new IntegerCollection());
                    obj.Dispose();
                }
                catch { /* Ignorar si ya fue eliminado */ }
            }
            _transients.Clear();
        }
    }
}
