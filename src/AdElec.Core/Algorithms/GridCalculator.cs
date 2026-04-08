using System.Collections.Generic;

namespace AdElec.Core.Algorithms
{
    public class GridPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public GridPoint(double x, double y) { X = x; Y = y; }
    }

    public static class GridCalculator
    {
        /// <summary>
        /// Calculates uniformly distributed points within a rectangular bounding box.
        /// Returns local coordinates from the bottom-left corner.
        /// </summary>
        public static List<GridPoint> CalculateUniformGrid(double width, double length, int columns, int rows)
        {
            var points = new List<GridPoint>();
            if (columns <= 0 || rows <= 0 || width <= 0 || length <= 0) return points;

            double spacingX = width / columns;
            double spacingY = length / rows;
            double startX   = spacingX / 2.0;
            double startY   = spacingY / 2.0;

            for (int i = 0; i < columns; i++)
                for (int j = 0; j < rows; j++)
                    points.Add(new GridPoint(startX + i * spacingX, startY + j * spacingY));

            return points;
        }

        /// <summary>
        /// Ray-casting point-in-polygon test (2D).
        /// Returns true if (px, py) is strictly inside the polygon defined by vertices.
        /// Points on the boundary are considered inside.
        /// </summary>
        public static bool IsPointInPolygon(double px, double py, IList<GridPoint> polygon)
        {
            int n = polygon.Count;
            if (n < 3) return false;

            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = polygon[i].X, yi = polygon[i].Y;
                double xj = polygon[j].X, yj = polygon[j].Y;

                bool intersects = ((yi > py) != (yj > py)) &&
                                  (px < (xj - xi) * (py - yi) / (yj - yi) + xi);
                if (intersects) inside = !inside;
            }
            return inside;
        }
    }
}
