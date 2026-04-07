using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AdElec.AutoCAD.Geometry
{
    public static class PolylineExtensions
    {
        /// <summary>
        /// Calculates the 2D bounding box dimensions of a polyline.
        /// Returns (MinPoint, Width, Length)
        /// </summary>
        public static (Point3d MinPoint, double Width, double Length) GetBoundingBoxDimensions(this Polyline polyline)
        {
            if (polyline == null) throw new ArgumentNullException(nameof(polyline));

            Extents3d extents = polyline.GeometricExtents;
            Point3d min = extents.MinPoint;
            Point3d max = extents.MaxPoint;

            double width = max.X - min.X;
            double length = max.Y - min.Y;

            return (min, width, length);
        }

        /// <summary>
        /// Given a spacing distance, calculates points along the perimeter of the polyline.
        /// Useful for placing receptacles (Tomas) automatically.
        /// </summary>
        /// <param name="polyline">The room contour</param>
        /// <param name="spacing">Distance between elements</param>
        /// <returns>List of points and the tangent angle at that point</returns>
        public static List<(Point3d Point, double AngleRad)> GetPointsAlongPerimeter(this Polyline polyline, double spacing)
        {
            var points = new List<(Point3d, double)>();
            if (polyline == null || spacing <= 0) return points;

            double totalLength = polyline.Length;
            int count = (int)Math.Floor(totalLength / spacing);

            for (int i = 0; i <= count; i++)
            {
                double dist = i * spacing;
                // Avoid exceeding exact length due to precision
                if (dist > totalLength) dist = totalLength;

                Point3d pt = polyline.GetPointAtDist(dist);
                
                // Get the tangent (direction of the wall) to rotate the block appropriately
                Vector3d firstDeriv = polyline.GetFirstDerivative(polyline.GetParameterAtDistance(dist));
                double angleOfWallRad = firstDeriv.AngleOnPlane(new Plane());

                points.Add((pt, angleOfWallRad));
            }

            return points;
        }
    }
}
