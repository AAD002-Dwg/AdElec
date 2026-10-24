namespace AdElec.Core.Models;

public class Circuit
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "TUG"; // TUG, TUE, IUG, IUE, AP, ACU, RESERVA
    public double BreakerAmps { get; set; } = 16;
    public double WireSectionMm2 { get; set; } = 2.5;
    public double LoadVA { get; set; } = 0;
    
    // R, S, T phase
    public char Phase { get; set; } = 'R';
    public string PhaseString { get; set; } = "SN"; // SN, R, S, T, RSTN

    // Unifilar and AEA metadata
    public int ArtefactCount { get; set; } = 0;
    public string Destinations { get; set; } = string.Empty; // e.g. "MONOAMBIENTE\nBAÑO"
    public string Curve { get; set; } = "C";
    public int Poles { get; set; } = 2; // 1, 2, 3, 4
    public string Norma { get; set; } = "IRAM 62267";
    public string Description { get; set; } = string.Empty; // "ILUMINACION", "TOMAS", "RESERVA", etc.

    public Circuit() { }
}
