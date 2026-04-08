using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using AdElec.Core.Algorithms;

namespace AdElec.AutoCAD.Geometry
{
    /// <summary>
    /// DrawJig interactivo para la distribución de luminarias.
    /// Combina dos modos de control:
    ///   - Mouse: mover el cursor controla dinámicamente la cantidad de filas/columnas.
    ///   - Teclado (Keywords W/A/S/D): ajuste fino manual de filas y columnas.
    /// Al hacer Click o presionar Enter se confirma la distribución.
    /// </summary>
    public class LuminariasJig : DrawJig
    {
        // --- Parámetros de la habitación ---
        private readonly Point3d _minPt;
        private readonly double _width;
        private readonly double _length;

        // --- Estado actual de la grilla ---
        private int _columns;
        private int _rows;

        // --- Posición actual del cursor (para el modo Mouse) ---
        private Point3d _currentCursorPos;

        // --- Resultado público para que el comando lea los valores finales ---
        public int FinalColumns => _columns;
        public int FinalRows => _rows;
        public bool Cancelled { get; private set; }

        // --- Constantes para el cálculo de la grilla basado en mouse ---
        // Distancia mínima y máxima de espaciado para mapear el cursor
        private const double MIN_SPACING = 0.5;  // Espaciado mínimo entre luminarias (metros)
        private const double MAX_SPACING = 5.0;  // Espaciado máximo

        public LuminariasJig(Point3d minPt, double width, double length, int initialCols, int initialRows)
        {
            _minPt = minPt;
            _width = width;
            _length = length;
            _columns = Math.Max(1, initialCols);
            _rows = Math.Max(1, initialRows);
            _currentCursorPos = new Point3d(minPt.X + width / 2, minPt.Y + length / 2, 0);
            Cancelled = false;
        }

        /// <summary>
        /// Sampler: Captura la posición del mouse y maneja los Keywords (WASD).
        /// AutoCAD llamará a este método continuamente mientras el Jig esté activo.
        /// </summary>
        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            // Configuramos el prompt con Keywords para WASD
            JigPromptPointOptions opts = new JigPromptPointOptions(
                $"\nLuminarias: {_columns}x{_rows} = {_columns * _rows} | [W +fila / S -fila / A +col / D -col] Click para confirmar: ");
            opts.UserInputControls =
                UserInputControls.Accept3dCoordinates |
                UserInputControls.NoZeroResponseAccepted |
                UserInputControls.NoNegativeResponseAccepted;

            // Registrar Keywords
            opts.Keywords.Add("W");
            opts.Keywords.Add("S");
            opts.Keywords.Add("A");
            opts.Keywords.Add("D");

            PromptPointResult res = prompts.AcquirePoint(opts);

            if (res.Status == PromptStatus.Keyword)
            {
                switch (res.StringResult.ToUpper())
                {
                    case "W": _rows = Math.Min(_rows + 1, 20); break;
                    case "S": _rows = Math.Max(_rows - 1, 1); break;
                    case "A": _columns = Math.Min(_columns + 1, 20); break;
                    case "D": _columns = Math.Max(_columns - 1, 1); break;
                }
                return SamplerStatus.OK;
            }

            if (res.Status == PromptStatus.Cancel)
            {
                Cancelled = true;
                return SamplerStatus.Cancel;
            }

            if (res.Status != PromptStatus.OK)
            {
                return SamplerStatus.NoChange;
            }

            // Modo Mouse: calcular filas/columnas basado en distancia del cursor al centro
            Point3d newPos = res.Value;
            if (newPos.DistanceTo(_currentCursorPos) < 0.01)
                return SamplerStatus.NoChange;

            _currentCursorPos = newPos;

            // Mapear la posición del cursor dentro de la caja envolvente al número de filas/columnas
            // Cuanto más cerca del centro -> más densidad (más filas/columnas)
            // Cuanto más lejos del centro -> menos densidad
            Point3d center = new Point3d(_minPt.X + _width / 2.0, _minPt.Y + _length / 2.0, 0);
            double distFromCenter = _currentCursorPos.DistanceTo(center);
            double maxDist = Math.Sqrt(_width * _width + _length * _length) / 2.0;

            // Normalizar la distancia a un rango [0, 1]
            double t = Math.Min(distFromCenter / maxDist, 1.0);

            // Interpolar: cerca del centro = muchos, lejos = pocos
            // Espaciado = MIN_SPACING + t * (MAX_SPACING - MIN_SPACING)
            double spacing = MIN_SPACING + t * (MAX_SPACING - MIN_SPACING);

            _columns = Math.Max(1, (int)Math.Round(_width / spacing));
            _rows = Math.Max(1, (int)Math.Round(_length / spacing));

            return SamplerStatus.OK;
        }

        /// <summary>
        /// WorldDraw: Dibuja la previsualización ("ghost") de las luminarias.
        /// Estos gráficos son efímeros y no se guardan en el dibujo.
        /// </summary>
        protected override bool WorldDraw(WorldDraw draw)
        {
            WorldGeometry geo = draw.Geometry;

            // Calcular los puntos de la grilla actual
            var gridPoints = GridCalculator.CalculateUniformGrid(_width, _length, _columns, _rows);

            // Dibujar cada luminaria como un círculo fantasma
            foreach (var gPt in gridPoints)
            {
                Point3d pt = new Point3d(_minPt.X + gPt.X, _minPt.Y + gPt.Y, 0);

                // Dibujar un círculo de previsualización (radio 0.15m)
                double radius = 0.15;
                int segments = 24;
                Point3d[] circlePoints = new Point3d[segments + 1];
                for (int i = 0; i <= segments; i++)
                {
                    double angle = 2.0 * Math.PI * i / segments;
                    circlePoints[i] = new Point3d(
                        pt.X + radius * Math.Cos(angle),
                        pt.Y + radius * Math.Sin(angle),
                        0);
                }
                geo.Polyline(new Point3dCollection(circlePoints), Vector3d.ZAxis, IntPtr.Zero);

                // Dibujar una cruz central (+)
                double crossSize = 0.08;
                geo.WorldLine(
                    new Point3d(pt.X - crossSize, pt.Y, 0),
                    new Point3d(pt.X + crossSize, pt.Y, 0));
                geo.WorldLine(
                    new Point3d(pt.X, pt.Y - crossSize, 0),
                    new Point3d(pt.X, pt.Y + crossSize, 0));
            }

            // Dibujar un texto informativo cerca del punto mínimo de la caja
            // No usamos geo.Text aquí para evitar dependencias adicionales.
            // El prompt de texto del Sampler ya informa las dimensiones.

            return true;
        }
    }
}
