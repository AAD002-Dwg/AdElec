using System.Collections.ObjectModel;
using System.Linq;

namespace AdElec.Core.Models;

public class Panel
{
    public string Name { get; set; } = "TD1";
    public string Location { get; set; } = string.Empty;
    public double MainBreakerAmps { get; set; } = 32;
    public int PhaseCount { get; set; } = 1; // 1 (Monofasico) o 3 (Trifasico)
    public double MainDiffAmps { get; set; } = 40;
    public double MainDiffSensitivityMa { get; set; } = 30; // 30mA
    public string FeederFrom { get; set; } = "DESDE\nT.P - SALA DE MEDIDORES";
    public double SimultaneityFactor { get; set; } = 0.70; // 70%
    public double PowerFactor { get; set; } = 0.85; // cos phi 0.85
    public int FunctionalUnitsCount { get; set; } = 1;

    /// <summary>Superficie cubierta en m² — requerida por AEA 90364 para el grado de electrificación.</summary>
    public double SuperficieCubiertaM2 { get; set; } = 0;
    /// <summary>Último grado AEA calculado por AEA-MOTOR ("1", "2", "3", "4"). Null si aún no se calculó.</summary>
    public string? LastGrado { get; set; }
    public ObservableCollection<Circuit> Circuits { get; set; } = new ObservableCollection<Circuit>();

    public double TotalLoadVA()
    {
        return Circuits.Sum(c => c.LoadVA);
    }

    public double TotalLoadKVA() => TotalLoadVA() / 1000.0;
    public double TotalLoadKW() => TotalLoadKVA() * PowerFactor;
    public double SimultaneousKW() => TotalLoadKW() * SimultaneityFactor;
    public double TotalProjectKW() => SimultaneousKW() * FunctionalUnitsCount;

    public Panel() { }
}
