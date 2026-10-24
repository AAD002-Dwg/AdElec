using System;

namespace AdElec.AutoCAD.Geometry
{
    /// <summary>
    /// Utilidades para convertir geometrías y rotaciones entre diferentes sistemas de coordenadas.
    /// </summary>
    public static class RotationHelper
    {
        /// <summary>
        /// Convierte una rotación del editor Web de AEA-MOTOR (donde 0 es Arriba y es Horaria)
        /// al sistema estándar de AutoCAD (donde 0 es Derecha y es Anti-horaria).
        /// </summary>
        /// <param name="webRotationRad">Rotación en radianes (0=Up, CW+)</param>
        /// <returns>Rotación en radianes para AutoCAD (0=Right, CCW+)</returns>
        public static double WebToAutoCAD(double webRotationRad)
        {
            // Fórmula final verificada: Angulo_CAD = -Angulo_Web
            // Web 0 (Up)      -> 0 (0°)        -> Apunta ARRIBA
            // Web 180 (Down)  -> -180 (180°)   -> Apunta ABAJO
            // Web 90 (Right)  -> -PI/2 (270°)  -> Apunta DERECHA
            // Web -90 (Left)  -> PI/2 (90°)    -> Apunta IZQUIERDA
            
            return -webRotationRad;
        }

        /// <summary>
        /// Formatea un ángulo en radianes a una cadena de grados legible para atributos de AutoCAD.
        /// Ejemplo: 1.5708 -> "90.00°"
        /// </summary>
        public static string ToDegreesString(double radians)
        {
            double degrees = radians * 180.0 / Math.PI;
            // Normalizar a [0, 360) para visualización
            while (degrees < 0) degrees += 360.0;
            while (degrees >= 360.0) degrees -= 360.0;
            
            return degrees.ToString("F2") + "°";
        }
    }
}
