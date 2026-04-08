using System.Collections.ObjectModel;
using System.Linq;

namespace AdElec.Core.Models;

public class Panel
{
    public string Name { get; set; } = "TD1";
    public string Location { get; set; } = string.Empty;
    public double MainBreakerAmps { get; set; } = 32;
    public int PhaseCount { get; set; } = 1; // 1 (Monofasico) o 3 (Trifasico)
    /// <summary>Superficie cubierta en m² — requerida por AEA 90364 para el grado de electrificación.</summary>
    public double SuperficieCubiertaM2 { get; set; } = 0;
    /// <summary>Último grado AEA calculado por AEA-MOTOR ("1", "2", "3", "4"). Null si aún no se calculó.</summary>
    public string? LastGrado { get; set; }
    public ObservableCollection<Circuit> Circuits { get; set; } = new ObservableCollection<Circuit>();

    public double TotalLoadVA()
    {
        return Circuits.Sum(c => c.LoadVA);
    }

    public Panel() { }
}
