namespace AdElec.Core.Models;

public class Circuit
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "TUG"; // TUG, TUE, IUG, IUE, AP, etc.
    public double BreakerAmps { get; set; } = 10;
    public double WireSectionMm2 { get; set; } = 1.5;
    public double LoadVA { get; set; } = 0;
    
    // R, S, T phase
    public char Phase { get; set; } = 'R';

    public Circuit() { }
}
