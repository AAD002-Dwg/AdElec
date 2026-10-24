using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AdElec.Core.Models;

public class Circuit : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _type = "TUG";
    private double _breakerAmps = 16;
    private double _wireSectionMm2 = 2.5;
    private double _loadVA = 0;
    private char _phase = 'R';
    private string _phaseString = "SN";
    private int _artefactCount = 0;
    private string _destinations = string.Empty;
    private string _curve = "C";
    private int _poles = 2;
    private string _norma = "IRAM 62267";
    private string _description = string.Empty;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Type
    {
        get => _type;
        set => SetField(ref _type, value);
    }

    public double BreakerAmps
    {
        get => _breakerAmps;
        set => SetField(ref _breakerAmps, value);
    }

    public double WireSectionMm2
    {
        get => _wireSectionMm2;
        set => SetField(ref _wireSectionMm2, value);
    }

    public double LoadVA
    {
        get => _loadVA;
        set => SetField(ref _loadVA, value);
    }

    public char Phase
    {
        get => _phase;
        set => SetField(ref _phase, value);
    }

    public string PhaseString
    {
        get => _phaseString;
        set => SetField(ref _phaseString, value);
    }

    public int ArtefactCount
    {
        get => _artefactCount;
        set => SetField(ref _artefactCount, value);
    }

    /// <summary>Alias for ArtefactCount representing the total bocas/points</summary>
    public int PointsCount
    {
        get => ArtefactCount;
        set => ArtefactCount = value;
    }

    public string Destinations
    {
        get => _destinations;
        set => SetField(ref _destinations, value);
    }

    public string Curve
    {
        get => _curve;
        set => SetField(ref _curve, value);
    }

    public int Poles
    {
        get => _poles;
        set => SetField(ref _poles, value);
    }

    public string Norma
    {
        get => _norma;
        set => SetField(ref _norma, value);
    }

    public string Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public Circuit() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
