namespace AdElec.Core.Models;

/// <summary>
/// Representación simple de un punto en el plano 2D para transferencia de geometría.
/// </summary>
public struct Point2D
{
    public double X { get; set; }
    public double Y { get; set; }

    public Point2D(double x, double y)
    {
        X = x;
        Y = y;
    }
}
