using System.Collections.Generic;

namespace AdElec.Core.Algorithms
{
    public class GridPoint
    {
        public double X { get; set; }
        public double Y { get; set; }

        public GridPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    public static class GridCalculator
    {
        /// <summary>
        /// Calculates uniformly distributed points within a rectangular area.
        /// Useful for placing luminaires within a bounding box.
        /// </summary>
        /// <param name="width">Width of the bounding area</param>
        /// <param name="length">Length/Height of the bounding area</param>
        /// <param name="columns">Number of items along the width (X axis)</param>
        /// <param name="rows">Number of items along the length (Y axis)</param>
        /// <returns>Local coordinates from the bottom-left corner of the box</returns>
        public static List<GridPoint> CalculateUniformGrid(double width, double length, int columns, int rows)
        {
            var points = new List<GridPoint>();

            if (columns <= 0 || rows <= 0 || width <= 0 || length <= 0)
                return points;

            double spacingX = width / columns;
            double spacingY = length / rows;

            double startX = spacingX / 2.0;
            double startY = spacingY / 2.0;

            for (int i = 0; i < columns; i++)
            {
                for (int j = 0; j < rows; j++)
                {
                    double x = startX + (i * spacingX);
                    double y = startY + (j * spacingY);
                    points.Add(new GridPoint(x, y));
                }
            }

            return points;
        }
    }
}
