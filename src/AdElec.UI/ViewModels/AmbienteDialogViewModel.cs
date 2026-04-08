using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AdElec.Core.Models;

namespace AdElec.UI.ViewModels;

public sealed class AmbienteDialogViewModel : INotifyPropertyChanged
{
    // ── Datos del recinto ────────────────────────────────────────────────────

    public double AreaM2 { get; }
    public string AreaDisplay => $"{AreaM2:F2} m²";

    public IReadOnlyList<string> UfOptions { get; }
    public IReadOnlyList<string> PlantaOptions { get; } =
        ["PB", "Primer Piso", "Segundo Piso", "Tercer Piso", "Cuarto Piso", "Subsuelo"];

    public IReadOnlyList<TipoAmbienteInfo> TiposLocal { get; } = TipoAmbienteInfo.Todos;

    private string _selectedUF;
    private string _planta = "PB";
    private TipoAmbienteInfo _selectedTipo;
    private double _espesorMuro = 0.15;

    public string SelectedUF
    {
        get => _selectedUF;
        set { _selectedUF = value; OnPropertyChanged(); }
    }

    public string Planta
    {
        get => _planta;
        set { _planta = value; OnPropertyChanged(); }
    }

    public TipoAmbienteInfo SelectedTipo
    {
        get => _selectedTipo;
        set { _selectedTipo = value; OnPropertyChanged(); }
    }

    public double EspesorMuro
    {
        get => _espesorMuro;
        set { _espesorMuro = value; OnPropertyChanged(); }
    }

    // ── Resultado ────────────────────────────────────────────────────────────

    public bool Confirmed { get; private set; }

    /// <summary>Ambiente construido al confirmar. Null si se canceló.</summary>
    public Ambiente? ResultadoAmbiente { get; private set; }

    // ── Comandos ────────────────────────────────────────────────────────────

    public ICommand ConfirmarCommand { get; }
    public ICommand CancelarCommand { get; }

    private Action? _cerrar;

    // ── Constructor ─────────────────────────────────────────────────────────

    public AmbienteDialogViewModel(double areaM2, IEnumerable<string> ufOptions)
    {
        AreaM2 = areaM2;
        UfOptions = ufOptions.ToList();
        _selectedUF = UfOptions.FirstOrDefault() ?? "";
        _selectedTipo = TipoAmbienteInfo.Todos[0]; // sala_estar

        ConfirmarCommand = new RelayCommand(Confirmar,
            () => !string.IsNullOrWhiteSpace(SelectedUF));
        CancelarCommand = new RelayCommand(Cancelar);
    }

    public void SetCerrar(Action cerrar) => _cerrar = cerrar;

    // ── Lógica ──────────────────────────────────────────────────────────────

    private void Confirmar()
    {
        ResultadoAmbiente = new Ambiente
        {
            UF = SelectedUF.Trim(),
            Planta = Planta.Trim(),
            TipoDisplay = SelectedTipo.Nombre,
            TipoApi = SelectedTipo.ApiValue,
            AreaM2 = AreaM2,
            EspesorMuro = EspesorMuro,
        };
        Confirmed = true;
        _cerrar?.Invoke();
    }

    private void Cancelar()
    {
        Confirmed = false;
        _cerrar?.Invoke();
    }

    // ── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
