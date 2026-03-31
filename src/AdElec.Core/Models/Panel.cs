using System.Collections.ObjectModel;
using System.Linq;

namespace AdElec.Core.Models;

public class Panel
{
    public string Name { get; set; } = "TD1";
    public string Location { get; set; } = string.Empty;
    public double MainBreakerAmps { get; set; } = 32;
    public int PhaseCount { get; set; } = 1; // 1 (Monofasico) o 3 (Trifasico)
    public ObservableCollection<Circuit> Circuits { get; set; } = new ObservableCollection<Circuit>();

    public double TotalLoadVA()
    {
        return Circuits.Sum(c => c.LoadVA);
    }

    public Panel() { }
}
