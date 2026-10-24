using System;
using System.Collections.Generic;
using AdElec.Core.Models;

namespace AdElec.AutoCAD.Geometry
{
    /// <summary>
    /// Utilidades de geometría 2D puras — sin dependencia de la API de AutoCAD.
    /// Diseñadas para que V2 (soporte de bulge/arcos) sea un reemplazo limpio
    /// del método de extracción de vértices, sin alterar estas funciones.
    /// </summary>
    public static class PolygonMath
    {
        /// <summary>
        /// Calcula el área de un polígono simple usando la fórmula del Shoelace.
        /// Devuelve el valor absoluto (siempre positivo).
        /// </summary>
        public static double CalculateArea(IReadOnlyList<Point2D> vertices)
        {
            if (vertices == null || vertices.Count < 3) return 0;

            double signedArea = 0;
            int n = vertices.Count;
            for (int i = 0; i < n; i++)
            {
                var p0 = vertices[i];
                var p1 = vertices[(i + 1) % n];
                signedArea += p0.X * p1.Y - p1.X * p0.Y;
            }
            return Math.Abs(signedArea) / 2.0;
        }

        /// <summary>
        /// Calcula el centroide de un polígono simple (fórmula del Shoelace).
        /// Fallback al promedio de vértices si el área es degenerada.
        /// </summary>
        public static Point2D CalculateCentroid(IReadOnlyList<Point2D> vertices)
        {
            if (vertices == null || vertices.Count == 0)
                return new Point2D(0, 0);

            if (vertices.Count < 3)
                return AveragePoint(vertices);

            int n = vertices.Count;
            double cx = 0, cy = 0, signedArea = 0;
            for (int i = 0; i < n; i++)
            {
                var p0 = vertices[i];
                var p1 = vertices[(i + 1) % n];
                double cross = p0.X * p1.Y - p1.X * p0.Y;
                signedArea += cross;
                cx += (p0.X + p1.X) * cross;
                cy += (p0.Y + p1.Y) * cross;
            }
            signedArea /= 2.0;

            if (Math.Abs(signedArea) < 1e-10)
                return AveragePoint(vertices);

            double factor = 1.0 / (6.0 * signedArea);
            return new Point2D(cx * factor, cy * factor);
        }

        /// <summary>
        /// Calcula el perímetro de un polígono (suma de distancias entre vértices consecutivos).
        /// </summary>
        public static double CalculatePerimeter(IReadOnlyList<Point2D> vertices)
        {
            if (vertices == null || vertices.Count < 2) return 0;

            double perimeter = 0;
            int n = vertices.Count;
            for (int i = 0; i < n; i++)
            {
                perimeter += Distance(vertices[i], vertices[(i + 1) % n]);
            }
            return perimeter;
        }

        /// <summary>
        /// Determina si un punto está dentro de un polígono (ray casting algorithm).
        /// </summary>
        public static bool PointInPolygon(Point2D point, IReadOnlyList<Point2D> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;

            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];

                if ((pi.Y > point.Y) != (pj.Y > point.Y) &&
                    point.X < (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y) + pi.X)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// Determina si el polígono 'inner' está completamente contenido dentro de 'outer'.
        /// Se verifica que todos los vértices de inner estén dentro de outer.
        /// </summary>
        public static bool PolygonContainsPolygon(IReadOnlyList<Point2D> outer, IReadOnlyList<Point2D> inner)
        {
            if (outer == null || inner == null || inner.Count == 0) return false;

            foreach (var p in inner)
            {
                if (!PointInPolygon(p, outer)) return false;
            }
            return true;
        }

        /// <summary>
        /// Calcula el aspect ratio (ancho/alto) del bounding box del polígono.
        /// Valores cercanos a 1 indican forma compacta; valores altos indican forma alargada (pasillo).
        /// Siempre devuelve >= 1.0 (el mayor dividido por el menor).
        /// </summary>
        public static double AspectRatio(IReadOnlyList<Point2D> vertices)
        {
            if (vertices == null || vertices.Count < 2) return 1.0;

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            foreach (var p in vertices)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            double width = maxX - minX;
            double height = maxY - minY;

            if (width < 1e-10 || height < 1e-10) return 1.0;

            double ratio = width / height;
            return ratio >= 1.0 ? ratio : 1.0 / ratio;
        }

        /// <summary>Distancia euclidiana entre dos puntos.</summary>
        public static double Distance(Point2D a, Point2D b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Distancia al cuadrado entre dos puntos (más rápido para comparaciones).</summary>
        public static double DistanceSq(Point2D a, Point2D b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>Promedio aritmético de puntos (fallback para centroide degenerado).</summary>
        private static Point2D AveragePoint(IReadOnlyList<Point2D> points)
        {
            double cx = 0, cy = 0;
            foreach (var p in points) { cx += p.X; cy += p.Y; }
            return new Point2D(cx / points.Count, cy / points.Count);
        }
    }
}
