using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AdElec.Core.Models;

public class Panel : INotifyPropertyChanged
{
    private string _name = "TD1";
    private string _location = string.Empty;
    private double _mainBreakerAmps = 32;
    private int _phaseCount = 1; // 1 o 2 (Monofasico 2P) o 3 o 4 (Trifasico 4P)
    private string _tipoTablero = "Tablero Seccional";
    private double _mainDiffAmps = 40;
    private double _mainDiffSensitivityMa = 30; // 30mA
    private string _feederFrom = "DESDE\nT.P - SALA DE MEDIDORES";
    private double _simultaneityFactor = 0.70; // 70%
    private double _powerFactor = 0.85; // cos phi 0.85
    private int _functionalUnitsCount = 1;
    private double _superficieCubiertaM2 = 0;
    private string? _lastGrado;
    private ObservableCollection<Circuit> _circuits = new();

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Location
    {
        get => _location;
        set => SetField(ref _location, value);
    }

    public double MainBreakerAmps
    {
        get => _mainBreakerAmps;
        set => SetField(ref _mainBreakerAmps, value);
    }

    public int PhaseCount
    {
        get => _phaseCount;
        set
        {
            if (SetField(ref _phaseCount, value))
            {
                OnPropertyChanged(nameof(PhaseDescription));
                OnPropertyChanged(nameof(CalculatedIbAmps));
                OnPropertyChanged(nameof(SuggestedBreakerAmps));
            }
        }
    }

    public string TipoTablero
    {
        get => _tipoTablero;
        set => SetField(ref _tipoTablero, value);
    }

    public double MainDiffAmps
    {
        get => _mainDiffAmps;
        set => SetField(ref _mainDiffAmps, value);
    }

    public double MainDiffSensitivityMa
    {
        get => _mainDiffSensitivityMa;
        set => SetField(ref _mainDiffSensitivityMa, value);
    }

    public string FeederFrom
    {
        get => _feederFrom;
        set => SetField(ref _feederFrom, value);
    }

    public double SimultaneityFactor
    {
        get => _simultaneityFactor;
        set
        {
            if (SetField(ref _simultaneityFactor, value))
            {
                OnPropertyChanged(nameof(SimultaneousKW));
                OnPropertyChanged(nameof(TotalProjectKW));
                OnPropertyChanged(nameof(CalculatedIbAmps));
                OnPropertyChanged(nameof(SuggestedBreakerAmps));
            }
        }
    }

    public double PowerFactor
    {
        get => _powerFactor;
        set => SetField(ref _powerFactor, value);
    }

    public int FunctionalUnitsCount
    {
        get => _functionalUnitsCount;
        set => SetField(ref _functionalUnitsCount, value);
    }

    /// <summary>Superficie cubierta en m² — requerida por AEA 90364 para el grado de electrificación.</summary>
    public double SuperficieCubiertaM2
    {
        get => _superficieCubiertaM2;
        set => SetField(ref _superficieCubiertaM2, value);
    }

    /// <summary>Último grado AEA calculado por AEA-MOTOR ("1", "2", "3", "4"). Null si aún no se calculó.</summary>
    public string? LastGrado
    {
        get => _lastGrado;
        set => SetField(ref _lastGrado, value);
    }

    public ObservableCollection<Circuit> Circuits
    {
        get => _circuits;
        set
        {
            if (SetField(ref _circuits, value))
            {
                NotifyCalculations();
            }
        }
    }

    // ── Propiedades calculadas para UI y normativa ──────────────────────────

    public string PhaseDescription => (PhaseCount >= 3) ? "Trifásico (3F+N / 4P)" : "Monofásico (1F+N / 2P)";

    public double TotalLoadVA() => Circuits.Sum(c => c.LoadVA);
    public double TotalLoadKVA() => TotalLoadVA() / 1000.0;
    public double TotalLoadKW() => TotalLoadKVA() * PowerFactor;
    public double SimultaneousKW() => TotalLoadKW() * SimultaneityFactor;
    public double TotalProjectKW() => SimultaneousKW() * FunctionalUnitsCount;

    /// <summary>
    /// Corriente de empleo simultánea Ib (A) bajo AEA 90364-771
    /// Monofásico (220V): S_sim / 220
    /// Trifásico (380V): S_sim / (sqrt(3) * 380)
    /// </summary>
    public double CalculatedIbAmps
    {
        get
        {
            double simVa = TotalLoadVA() * SimultaneityFactor;
            if (simVa <= 0) return 0;
            if (PhaseCount >= 3)
            {
                return Math.Round(simVa / (Math.Sqrt(3) * 380.0), 1);
            }
            return Math.Round(simVa / 220.0, 1);
        }
    }

    /// <summary>
    /// Calibre normalizado comercial sugerido para el interruptor general (cabecera).
    /// Según AEA 770.15 y buenas prácticas de ingeniería:
    /// - La cabecera mínima no debe ser inferior a 25 A.
    /// - Se contempla un margen continuo sobre Ib (15%) para evitar disparos térmicos en carga plena simultánea.
    /// </summary>
    public double SuggestedBreakerAmps
    {
        get
        {
            double ib = CalculatedIbAmps;
            if (ib <= 0) return 32;

            // Mínimo 25A de cabecera según AEA 770.15 y margen continuo del 15% sobre Ib
            double target = Math.Max(25.0, ib * 1.15);
            double[] standardBreakers = [25, 32, 40, 50, 63, 80, 100, 125];
            foreach (var b in standardBreakers)
            {
                if (b >= target) return b;
            }
            return 125;
        }
    }

    public void NotifyCalculations()
    {
        OnPropertyChanged(nameof(TotalLoadVA));
        OnPropertyChanged(nameof(TotalLoadKVA));
        OnPropertyChanged(nameof(TotalLoadKW));
        OnPropertyChanged(nameof(SimultaneousKW));
        OnPropertyChanged(nameof(TotalProjectKW));
        OnPropertyChanged(nameof(CalculatedIbAmps));
        OnPropertyChanged(nameof(SuggestedBreakerAmps));
    }

    public Panel() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
