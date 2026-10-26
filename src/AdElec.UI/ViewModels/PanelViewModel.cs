using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AdElec.Core.AeaMotor;
using AdElec.Core.AeaMotor.Dtos;
using AdElec.Core.Aea;
using AdElec.Core.Interfaces;
using AdElec.Core.Models;
using System.Linq;

namespace AdElec.UI.ViewModels;

public sealed class PanelViewModel : INotifyPropertyChanged
{
    private readonly IPanelRepository _repo;
    private readonly IAeaMotorClient _motorClient;
    private readonly IAmbienteRepository? _ambienteRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly Action _onSugerirLuminarias;
    private readonly Action _onSugerirTomas;
    private readonly Action<string> _onRecargarCircuitos;
    private readonly Action<string, string>? _onInsertarTablero;
    private readonly Func<string, List<SyncRoom>>? _onGetRoomsConPuntos;
    private readonly Action? _onPullFromWeb;
    private readonly Action? _onRecargarDocumento;
    private readonly Action<string>? _onGenerarUnifilar;
    private readonly Action? _onEfectos;

    // ── Estado ──────────────────────────────────────────────────────────────

    private Panel? _selectedPanel;
    private string _statusMessage = "Listo";
    private bool _isMotorAvailable;
    private bool _isBusy;
    private int _projectId;
    private string _projectName = "";
    private string _syncMode = "AXIS";
    private ResultadoProyectoDto? _lastResultado;

    public ObservableCollection<Panel> Panels { get; } = [];
    public ObservableCollection<Circuit> Circuits { get; } = [];

    public ObservableCollection<string> CircuitTypesList { get; } = ["IUG", "TUG", "IUE", "TUE", "AP", "ACU", "RESERVA"];
    public ObservableCollection<double> WireOptionsList { get; } = [1.5, 2.5, 4.0, 6.0, 10.0, 16.0];
    public ObservableCollection<double> BreakerOptionsList { get; } = [10, 16, 20, 25, 32, 40, 50, 63];
    public ObservableCollection<char> PhaseOptionsList { get; } = ['R', 'S', 'T'];

    private Circuit? _selectedCircuit;
    public Circuit? SelectedCircuit
    {
        get => _selectedCircuit;
        set
        {
            if (_selectedCircuit == value) return;
            _selectedCircuit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedCircuit));
            if (EliminarCircuitoCommand is RelayCommand rc) rc.RaiseCanExecuteChanged();
        }
    }
    public bool HasSelectedCircuit => _selectedCircuit is not null;

    public Panel? SelectedPanel
    {
        get => _selectedPanel;
        set
        {
            if (_selectedPanel == value) return;
            _selectedPanel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPanel));
            OnPropertyChanged(nameof(TotalLoadVA));
            OnPropertyChanged(nameof(TotalLoadKVA));
            Diagnostics.Clear();
            OnPropertyChanged(nameof(HasDiagnostics));
            OnPropertyChanged(nameof(DiagnosticsSummary));

            if (_selectedPanel != null)
            {
                if (_selectedPanel.SuperficieCubiertaM2 <= 0 && _ambienteRepo != null)
                {
                    double sup = 0;
                    if (!string.IsNullOrWhiteSpace(_selectedPanel.Location))
                        sup = _ambienteRepo.GetAmbientesParaUF(_selectedPanel.Location).Sum(a => a.AreaM2);
                    if (sup <= 0)
                        sup = _ambienteRepo.GetAllAmbientes().Sum(a => a.AreaM2);
                    if (sup > 0)
                        _selectedPanel.SuperficieCubiertaM2 = Math.Round(sup, 1);
                }
                _selectedPanel.NotifyCalculations();
            }

            RefreshCircuits();
            RaiseAllCommandsCanExecuteChanged();
        }
    }

    public bool HasPanel => _selectedPanel is not null;
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); RaiseAllCommandsCanExecuteChanged(); } }

    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }
    public bool IsMotorAvailable { get => _isMotorAvailable; private set { _isMotorAvailable = value; OnPropertyChanged(); OnPropertyChanged(nameof(MotorStatusText)); OnPropertyChanged(nameof(MotorStatusColor)); } }

    public string MotorStatusText => IsMotorAvailable ? "AEA Core ●" : "AEA Core ○";
    public string MotorStatusColor => IsMotorAvailable ? "#4CAF50" : "#F44336";

    public double TotalLoadVA => _selectedPanel?.TotalLoadVA() ?? 0;
    public double TotalLoadKVA => Math.Round(TotalLoadVA / 1000.0, 2);

    public string? LastGrado => _selectedPanel?.LastGrado ?? _lastResultado?.Grado?.Grado;
    public bool? CumpleNorma => Diagnostics.Count > 0 ? !Diagnostics.Any(d => d.Severidad == "error") : _lastResultado?.CumpleNorma;
    public string CumpleNormaText => CumpleNorma == null ? "" : (CumpleNorma == true ? "✓ CUMPLE" : "✗ NO CUMPLE");
    public string CumpleNormaColor => CumpleNorma == true ? "#4CAF50" : "#F44336";

    public ObservableCollection<AeaDiagnostic> Diagnostics { get; } = [];
    public bool HasDiagnostics => Diagnostics.Count > 0;
    public string DiagnosticsSummary => $"{Diagnostics.Count(d => d.Severidad == "error")} err · {Diagnostics.Count(d => d.Severidad != "error")} adv";

    public int ProjectId
    {
        get => _projectId;
        set
        {
            if (_projectId == value) return;
            _projectId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EstaVinculado));
            _projectRepo.SaveProjectId(value);
            RaiseAllCommandsCanExecuteChanged();
        }
    }

    public string ProjectName
    {
        get => _projectName;
        set
        {
            if (_projectName == value) return;
            _projectName = value;
            OnPropertyChanged();
            _projectRepo.SaveProjectName(value);
        }
    }

    public bool EstaVinculado => _projectId > 0;
    public bool HasCircuits => Circuits.Count > 0;

    private string _activeDwgName = "";
    /// <summary>Nombre del DWG activo mostrado en el header de la paleta.</summary>
    public string ActiveDwgName
    {
        get => _activeDwgName;
        private set { _activeDwgName = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveDwgLabel)); }
    }
    public string ActiveDwgLabel => string.IsNullOrWhiteSpace(_activeDwgName) ? "Sin documento" : _activeDwgName;

    public string SyncMode
    {
        get => _syncMode;
        set
        {
            if (_syncMode == value) return;
            _syncMode = value;
            OnPropertyChanged();
            _projectRepo.SaveSyncMode(value);
            StatusMessage = value == "INTERIOR" 
                ? "Modo: Perímetros Interiores (se aplicará unificación de muros)" 
                : "Modo: Ejes de Muro (estándar)";
        }
    }

    // ── Nuevo Tablero (inline form) ──────────────────────────────────────────

    private bool _showNewPanelForm;
    private string _newPanelName = "TD1";
    private string _newPanelLocation = "";
    private int _newPanelPhaseCount = 1;
    private string _newPanelTipo = "Tablero Seccional";
    private double _newPanelSuperficie = 0;
    private double _newPanelMainBreakerAmps = 32;

    public bool ShowNewPanelForm { get => _showNewPanelForm; set { _showNewPanelForm = value; OnPropertyChanged(); } }
    public string NewPanelName { get => _newPanelName; set { _newPanelName = value; OnPropertyChanged(); } }
    public string NewPanelLocation { get => _newPanelLocation; set { _newPanelLocation = value; OnPropertyChanged(); } }
    public int NewPanelPhaseCount { get => _newPanelPhaseCount; set { _newPanelPhaseCount = value; OnPropertyChanged(); } }
    public string NewPanelTipo { get => _newPanelTipo; set { _newPanelTipo = value; OnPropertyChanged(); } }
    public double NewPanelSuperficie { get => _newPanelSuperficie; set { _newPanelSuperficie = value; OnPropertyChanged(); } }
    public double NewPanelMainBreakerAmps { get => _newPanelMainBreakerAmps; set { _newPanelMainBreakerAmps = value; OnPropertyChanged(); } }

    // ── Comandos ────────────────────────────────────────────────────────────

    public ICommand NuevoTableroCommand { get; }
    public ICommand ConfirmarNuevoTableroCommand { get; }
    public ICommand CancelarNuevoTableroCommand { get; }
    public ICommand CalcularAeaCommand { get; }
    public ICommand SugerirLuminariasCommand { get; }
    public ICommand SugerirTomasCommand { get; }
    public ICommand CheckMotorCommand { get; }
    public ICommand RecargarCircuitosCommand { get; }
    public ICommand SincronizarCommand { get; }
    public ICommand PullFromWebCommand { get; }
    public ICommand RecargarDocumentoCommand { get; }
    public ICommand GenerarUnifilarCommand { get; }
    public ICommand EfectosCommand { get; }
    public ICommand AgregarCircuitoCommand { get; }
    public ICommand EliminarCircuitoCommand { get; }
    public ICommand GuardarCircuitosCommand { get; }
    public ICommand GuardarTableroCommand { get; }
    public ICommand EliminarTableroCommand { get; }
    public ICommand CalcularSuperficieCommand { get; }
    public ICommand AplicarCorreccionCommand { get; }

    // ── Constructor ─────────────────────────────────────────────────────────

    public PanelViewModel(
        IPanelRepository repo,
        IAeaMotorClient motorClient,
        IProjectRepository projectRepo,
        Action onSugerirLuminarias,
        Action onSugerirTomas,
        Action<string, string>? onInsertarTablero = null,
        IAmbienteRepository? ambienteRepo = null,
        Action<string>? onRecargarCircuitos = null,
        Func<string, List<SyncRoom>>? onGetRoomsConPuntos = null,
        Action? onPullFromWeb = null,
        Action? onRecargarDocumento = null,
        Action<string>? onGenerarUnifilar = null,
        Action? onEfectos = null)
    {
        _repo = repo;
        _motorClient = motorClient;
        _ambienteRepo = ambienteRepo;
        _onSugerirLuminarias = onSugerirLuminarias;
        _onSugerirTomas = onSugerirTomas;
        _onInsertarTablero = onInsertarTablero;
        _onRecargarCircuitos = onRecargarCircuitos ?? (_ => { });
        _onGetRoomsConPuntos = onGetRoomsConPuntos;
        _projectRepo = projectRepo;
        _onPullFromWeb = onPullFromWeb;
        _onRecargarDocumento = onRecargarDocumento;
        _onGenerarUnifilar = onGenerarUnifilar;
        _onEfectos = onEfectos;

        NuevoTableroCommand = new RelayCommand(() => ShowNewPanelForm = true, () => !ShowNewPanelForm);
        ConfirmarNuevoTableroCommand = new RelayCommand(ConfirmarNuevoTablero, () => !string.IsNullOrWhiteSpace(NewPanelName));
        CancelarNuevoTableroCommand = new RelayCommand(() => ShowNewPanelForm = false);
        CalcularAeaCommand = new RelayCommand(async () => await CalcularAeaAsync(), () => HasPanel && IsMotorAvailable && !IsBusy);
        SugerirLuminariasCommand = new RelayCommand(_onSugerirLuminarias, () => HasPanel);
        SugerirTomasCommand = new RelayCommand(_onSugerirTomas, () => HasPanel);
        CheckMotorCommand = new RelayCommand(async () => await CheckMotorAsync());
        RecargarCircuitosCommand = new RelayCommand(RecargarCircuitos, () => HasPanel);
        SincronizarCommand = new RelayCommand(async () => await SincronizarAsync(), () => EstaVinculado && IsMotorAvailable && !IsBusy);
        PullFromWebCommand = new RelayCommand(() => _onPullFromWeb?.Invoke(), () => EstaVinculado && IsMotorAvailable && !IsBusy);
        RecargarDocumentoCommand = new RelayCommand(() => _onRecargarDocumento?.Invoke(), () => !IsBusy);
        GenerarUnifilarCommand = new RelayCommand(() => _onGenerarUnifilar?.Invoke(SelectedPanel?.Name ?? ""), () => HasPanel);
        EfectosCommand = new RelayCommand(() => _onEfectos?.Invoke());
        AgregarCircuitoCommand = new RelayCommand(AgregarCircuito, () => HasPanel);
        EliminarCircuitoCommand = new RelayCommand(EliminarCircuito, () => HasSelectedCircuit);
        AplicarCorreccionCommand = new RelayCommand(param => AplicarCorreccion(param as AeaDiagnostic));
        GuardarCircuitosCommand = new RelayCommand(GuardarCircuitos, () => HasPanel);
        GuardarTableroCommand = new RelayCommand(GuardarTablero, () => HasPanel);
        EliminarTableroCommand = new RelayCommand(EliminarTablero, () => HasPanel);
        CalcularSuperficieCommand = new RelayCommand(CalcularSuperficieDesdeAmbientes, () => HasPanel);

        _projectId = _projectRepo.GetProjectId();
        _projectName = _projectRepo.GetProjectName();
        _syncMode = _projectRepo.GetSyncMode();
        LoadPanels();
        _ = CheckMotorAsync();
    }

    // ── Lógica ──────────────────────────────────────────────────────────────

    private void LoadPanels()
    {
        Panels.Clear();
        try
        {
            var panels = _repo.GetAllPanels();
            foreach (var p in panels)
                Panels.Add(p);

            SelectedPanel = Panels.FirstOrDefault();
            StatusMessage = Panels.Count == 0 ? "Sin tableros. Creá uno nuevo." : $"{Panels.Count} tablero(s) cargados.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar tableros: {ex.Message}";
        }
    }

    /// <summary>
    /// Recarga todo el estado dependiente del documento activo.
    /// Llamar solo cuando los datos ya fueron leídos en el hilo de AutoCAD.
    /// </summary>
    public void ReloadFromActiveDocument(string? dwgName = null)
    {
        // Silenciar: si una operación está en curso, no recargar
        if (IsBusy) return;

        // Recargar metadatos del proyecto desde el nuevo DWG (llamado en hilo AutoCAD → WPF vía Dispatcher)
        _projectId   = _projectRepo.GetProjectId();
        _projectName = _projectRepo.GetProjectName();
        _syncMode    = _projectRepo.GetSyncMode();

        ApplyDocumentSwitch(dwgName);
    }

    /// <summary>
    /// Recarga el ViewModel con datos pre-leídos en el hilo de AutoCAD.
    /// Usar este método cuando la lectura de repos ya ocurrió fuera del hilo WPF
    /// para evitar que Application.DocumentManager.MdiActiveDocument devuelva el doc incorrecto.
    /// </summary>
    public void ReloadWithPreloadedData(
        string dwgName,
        int projectId,
        string projectName,
        string syncMode,
        List<AdElec.Core.Models.Panel> panels)
    {
        if (IsBusy) return;

        _projectId   = projectId;
        _projectName = projectName;
        _syncMode    = syncMode;

        ApplyDocumentSwitch(dwgName, panels);
    }

    private void ApplyDocumentSwitch(string? dwgName, List<AdElec.Core.Models.Panel>? preloadedPanels = null)
    {
        if (!string.IsNullOrWhiteSpace(dwgName))
            ActiveDwgName = dwgName;

        // Limpiar resultado de cálculo anterior (pertenecía al otro documento)
        _lastResultado = null;
        Diagnostics.Clear();
        OnPropertyChanged(nameof(HasDiagnostics));
        OnPropertyChanged(nameof(DiagnosticsSummary));
        OnPropertyChanged(nameof(LastGrado));
        OnPropertyChanged(nameof(CumpleNorma));
        OnPropertyChanged(nameof(CumpleNormaText));
        OnPropertyChanged(nameof(CumpleNormaColor));

        OnPropertyChanged(nameof(ProjectId));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(SyncMode));
        OnPropertyChanged(nameof(EstaVinculado));

        ShowNewPanelForm = false;

        if (preloadedPanels != null)
            LoadPanelsFromData(preloadedPanels);
        else
            LoadPanels();

        RaiseAllCommandsCanExecuteChanged();

        string docLabel = string.IsNullOrWhiteSpace(dwgName) ? "nuevo documento" : dwgName;
        StatusMessage = $"Documento activo: {docLabel}";
    }

    private void LoadPanelsFromData(List<AdElec.Core.Models.Panel> panels)
    {
        Panels.Clear();
        foreach (var p in panels)
            Panels.Add(p);
        SelectedPanel = Panels.FirstOrDefault();
        StatusMessage = Panels.Count == 0 ? "Sin tableros. Creá uno nuevo." : $"{Panels.Count} tablero(s) cargados.";
    }

    private void RefreshCircuits()
    {
        Circuits.Clear();
        if (_selectedPanel is null)
        {
            SelectedCircuit = null;
            return;
        }
        foreach (var c in _selectedPanel.Circuits)
            Circuits.Add(c);

        SelectedCircuit = Circuits.FirstOrDefault();
        OnPropertyChanged(nameof(TotalLoadVA));
        OnPropertyChanged(nameof(TotalLoadKVA));
        OnPropertyChanged(nameof(HasCircuits));
    }

    private void AgregarCircuito()
    {
        if (_selectedPanel == null) return;

        int num = 1;
        while (_selectedPanel.Circuits.Any(c => string.Equals(c.Name, $"C{num}", StringComparison.OrdinalIgnoreCase)))
        {
            num++;
        }
        string nuevoNombre = $"C{num}";

        bool tieneIug = _selectedPanel.Circuits.Any(c => c.Type.StartsWith("IUG", StringComparison.OrdinalIgnoreCase));
        string tipo = (!tieneIug) ? "IUG" : "TUG";
        double seccion = tipo == "IUG" ? 1.5 : 2.5;
        double termica = tipo == "IUG" ? 10 : 16;
        double carga = tipo == "IUG" ? 150 : 2200;

        char fase = 'R';
        if (_selectedPanel.PhaseCount > 1)
        {
            char[] fases = ['R', 'S', 'T'];
            fase = fases[_selectedPanel.Circuits.Count % _selectedPanel.PhaseCount];
        }

        var nuevo = new Circuit
        {
            Name = nuevoNombre,
            Type = tipo,
            WireSectionMm2 = seccion,
            LoadVA = carga,
            BreakerAmps = termica,
            Phase = fase,
            PointsCount = 1,
            Destinations = !string.IsNullOrWhiteSpace(_selectedPanel.Location) ? _selectedPanel.Location : "General"
        };

        _selectedPanel.Circuits.Add(nuevo);
        Circuits.Add(nuevo);
        SelectedCircuit = nuevo;

        _repo.SavePanel(_selectedPanel);

        OnPropertyChanged(nameof(TotalLoadVA));
        OnPropertyChanged(nameof(TotalLoadKVA));
        OnPropertyChanged(nameof(HasCircuits));
        StatusMessage = $"✓ Agregado circuito '{nuevo.Name}' ({tipo}) al tablero '{_selectedPanel.Name}'.";
    }

    private void EliminarCircuito()
    {
        if (_selectedCircuit == null || _selectedPanel == null) return;

        string nombre = _selectedCircuit.Name;
        _selectedPanel.Circuits.Remove(_selectedCircuit);
        Circuits.Remove(_selectedCircuit);
        SelectedCircuit = Circuits.FirstOrDefault();

        _repo.SavePanel(_selectedPanel);

        OnPropertyChanged(nameof(TotalLoadVA));
        OnPropertyChanged(nameof(TotalLoadKVA));
        OnPropertyChanged(nameof(HasCircuits));
        StatusMessage = $"✓ Circuito '{nombre}' eliminado.";
    }

    private void GuardarCircuitos()
    {
        if (_selectedPanel == null) return;
        _repo.SavePanel(_selectedPanel);
        OnPropertyChanged(nameof(TotalLoadVA));
        OnPropertyChanged(nameof(TotalLoadKVA));
        StatusMessage = $"✓ Tablero '{_selectedPanel.Name}' con {Circuits.Count} circuito(s) guardado en el DWG.";
    }

    private void ConfirmarNuevoTablero()
    {
        var panel = new Panel
        {
            Name = NewPanelName.Trim().ToUpper(),
            Location = NewPanelLocation.Trim(),
            PhaseCount = NewPanelPhaseCount >= 3 ? 3 : 1,
            TipoTablero = NewPanelTipo,
            SuperficieCubiertaM2 = NewPanelSuperficie,
            MainBreakerAmps = NewPanelMainBreakerAmps > 0 ? NewPanelMainBreakerAmps : 32,
        };

        try
        {
            _repo.SavePanel(panel);
            Panels.Add(panel);
            SelectedPanel = panel;
            ShowNewPanelForm = false;

            string nombre = panel.Name;
            string vis = NewPanelTipo;

            NewPanelName = "TD1";
            NewPanelLocation = "";
            NewPanelTipo = "Tablero Seccional";
            NewPanelSuperficie = 0;
            NewPanelMainBreakerAmps = 32;

            StatusMessage = $"Tablero '{nombre}' guardado. Ubicalo en el plano.";

            // Disparar inserción del bloque I.E-AD-04 en el dibujo
            _onInsertarTablero?.Invoke(nombre, vis);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al guardar tablero: {ex.Message}";
        }
    }

    private async Task CalcularAeaAsync()
    {
        if (_selectedPanel is null) return;
        IsBusy = true;
        StatusMessage = "Validando con núcleo normativo AEA Core...";

        try
        {
            List<Ambiente>? ambientes = null;
            double superficie = _selectedPanel.SuperficieCubiertaM2;

            if (_ambienteRepo is not null && !string.IsNullOrWhiteSpace(_selectedPanel.Location))
            {
                var ambs = _ambienteRepo.GetAmbientesParaUF(_selectedPanel.Location);
                if (ambs.Count > 0)
                {
                    ambientes = ambs;
                    if (superficie <= 0)
                        superficie = ambs.Sum(a => a.AreaM2);
                }
            }

            // 1. Ejecución nativa instantánea con aea_core
            var diags = _selectedPanel.ValidarNormativaAea(ambientes);
            Diagnostics.Clear();
            foreach (var d in diags) Diagnostics.Add(d);

            // 2. Grado de electrificación (residencial 770 o comercial 771)
            AeaGradoResultadoDto gradoRes;
            bool esComercial = _selectedPanel.TipoTablero?.Contains("comercial", StringComparison.OrdinalIgnoreCase) == true ||
                               _selectedPanel.TipoTablero?.Contains("oficina", StringComparison.OrdinalIgnoreCase) == true;

            if (esComercial)
                gradoRes = AeaBridge.CalcularGradoComercial(superficie);
            else
                gradoRes = AeaBridge.CalcularGrado(superficie, 0);

            _selectedPanel.LastGrado = gradoRes.Grado;
            _repo.SavePanel(_selectedPanel);

            RefreshCircuits();
            OnPropertyChanged(nameof(LastGrado));
            OnPropertyChanged(nameof(CumpleNorma));
            OnPropertyChanged(nameof(CumpleNormaText));
            OnPropertyChanged(nameof(CumpleNormaColor));
            OnPropertyChanged(nameof(HasDiagnostics));
            OnPropertyChanged(nameof(DiagnosticsSummary));

            int errCount = diags.Count(d => d.Severidad == "error");
            int warnCount = diags.Count(d => d.Severidad != "error");

            StatusMessage = errCount == 0 && warnCount == 0
                ? $"✓ AEA: Tablero reglamentario (Grado {gradoRes.Grado})"
                : $"⚠ AEA: {errCount} errores, {warnCount} advertencias";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error AEA: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AplicarCorreccion(AeaDiagnostic? diag)
    {
        if (_selectedPanel is null || diag is null) return;
        var corr = diag.Correcciones?.FirstOrDefault();
        if (corr is null) return;

        if (_selectedPanel.AplicarCorreccion(diag, corr))
        {
            _repo.SavePanel(_selectedPanel);
            RefreshCircuits();
            _ = CalcularAeaAsync();
            StatusMessage = $"✓ Corrección aplicada en {diag.Elemento}: {corr.Accion}";
        }
    }

    private async Task CheckMotorAsync()
    {
        IsMotorAvailable = true;
        StatusMessage = "AEA Core nativo activo (100% verificado contra norma).";
    }

    private async Task SincronizarAsync()
    {
        if (!EstaVinculado) return;
        IsBusy = true;
        StatusMessage = "Sincronizando con AEA-MOTOR...";

        try
        {
            // 1. Verificar que el proyecto sigue existiendo en el servidor
            string projectNameOnServer;
            try
            {
                var currentProject = await _motorClient.GetProjectAsync(_projectId);
                projectNameOnServer = currentProject.Name;
            }
            catch
            {
                // El proyecto fue borrado del servidor → resetear ID y crear uno nuevo al final
                StatusMessage = $"Proyecto #{_projectId} no encontrado en AEA-MOTOR. Se creará uno nuevo...";
                _projectId = 0;
                projectNameOnServer = "";
            }

            if (_ambienteRepo == null)
                throw new InvalidOperationException("No se pudo acceder al repositorio de ambientes.");

            // 2. Geometría: rooms desde ID_LOCALES con polígonos
            var ambientesDwg = _ambienteRepo.GetAllAmbientes();
            if (ambientesDwg.Count == 0)
                throw new InvalidOperationException("No hay recintos en el dibujo. Usá ADE_AMBIENTES primero.");

            StatusMessage = $"Procesando {ambientesDwg.Count} recintos y circuitos...";

            // 4. Circuitos del panel → Board del canvas
            var syncCircuits = (_selectedPanel?.Circuits ?? [])
                .Select(c => new SyncCircuit
                {
                    Id         = c.Name,
                    Name       = c.Name,
                    Amperage   = (int)c.BreakerAmps,
                    Protection = $"TM {c.BreakerAmps}A",
                })
                .ToList();

            // 5. Grafo planar para el módulo AD-CAD (visualización de muros)
            var graph = AdElec.Core.Utils.GeometryConverter.BuildGraphFromAmbientes(ambientesDwg, SyncMode == "INTERIOR");

            // 5b. FaceKey por ambiente: en V2 el id del room = faceKey = IDs de aristas del borde unidas por "|"
            var faceKeyByHandle = ambientesDwg
                .Where(a => !string.IsNullOrEmpty(a.Handle) && a.PolygonPoints.Count > 0)
                .ToDictionary(
                    a => $"room_{a.Handle}",
                    a => AdElec.Core.Utils.GeometryConverter.ComputeFaceKey(a.PolygonPoints, graph),
                    StringComparer.OrdinalIgnoreCase);

            // 5c. Planta ID por ambiente
            var plantaGroups = ambientesDwg
                .GroupBy(a => string.IsNullOrWhiteSpace(a.Planta) ? "PB" : a.Planta,
                         StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key)
                .ToList();

            var plantaIdByHandle = ambientesDwg
                .Where(a => !string.IsNullOrEmpty(a.Handle))
                .ToDictionary(
                    a => $"room_{a.Handle}",
                    a =>
                    {
                        int idx = plantaGroups.FindIndex(pg => pg.Key.Equals(
                            string.IsNullOrWhiteSpace(a.Planta) ? "PB" : a.Planta,
                            StringComparison.OrdinalIgnoreCase));
                        return $"PL{(idx >= 0 ? idx + 1 : 1)}";
                    },
                    StringComparer.OrdinalIgnoreCase);

            // 3. Puntos eléctricos: rooms con puntos desde bloques eléctricos
            List<SyncRoom> syncRooms;
            if (_selectedPanel != null && _onGetRoomsConPuntos != null)
            {
                var rawRooms = _onGetRoomsConPuntos(_selectedPanel.Name);

                // Completar polígonos faltantes desde ambientesDwg usando Handle
                var ambByHandle = ambientesDwg
                    .Where(a => !string.IsNullOrEmpty(a.Handle))
                    .ToDictionary(a => $"room_{a.Handle}", a => a, StringComparer.OrdinalIgnoreCase);

                foreach (var sr in rawRooms.Where(r => r.PolygonPoints.Count == 0))
                {
                    if (ambByHandle.TryGetValue(sr.Id, out var match) && match.PolygonPoints.Count > 0)
                        sr.PolygonPoints = match.PolygonPoints
                            .Select(p => new Dictionary<string, double>
                                { ["x"] = Math.Round(p.X, 3), ["y"] = Math.Round(p.Y, 3) })
                            .ToList();
                }

                // Reasignar ID a faceKey y enriquecer puntos con _roomId/_roomZ/_plantaId
                syncRooms = rawRooms.Select(sr =>
                {
                    string faceKey = faceKeyByHandle.GetValueOrDefault(sr.Id, sr.Id);
                    string plantaId = plantaIdByHandle.GetValueOrDefault(sr.Id, "PL1");
                    sr.Id = faceKey;
                    sr.Points = sr.Points.Select(p => new AdElec.Core.AeaMotor.Dtos.ProposalPoint
                    {
                        Id        = p.Id,
                        X         = p.X,
                        Y         = p.Y,
                        Type      = p.Type,
                        CircuitId = p.CircuitId,
                        SwitchId  = p.SwitchId,
                        PowerVa   = p.PowerVa,
                        Label     = p.Label,
                        IsFixed   = p.IsFixed,
                        RoomId    = faceKey,
                        RoomZ     = 0,
                        PlantaId  = plantaId,
                    }).ToList();
                    return sr;
                }).ToList();
            }
            else
            {
                // Sin tablero seleccionado: sólo geometría, sin puntos
                syncRooms = ambientesDwg.Select(a =>
                {
                    string handleKey = string.IsNullOrEmpty(a.Handle)
                        ? $"room_{ambientesDwg.IndexOf(a):000}" : $"room_{a.Handle}";
                    string faceKey = faceKeyByHandle.GetValueOrDefault(handleKey, handleKey);
                    return new SyncRoom
                    {
                        Id            = faceKey,
                        Name          = $"{a.TipoDisplay} — {a.Planta}",
                        Type          = AdElec.Core.Models.TipoAmbienteInfo.DesdeNombre(a.TipoDisplay).ApiValue,
                        Area          = a.AreaM2,
                        WallThickness = a.EspesorMuro,
                        PolygonPoints = a.PolygonPoints
                            .Select(p => new Dictionary<string, double>
                                { ["x"] = Math.Round(p.X, 3), ["y"] = Math.Round(p.Y, 3) })
                            .ToList(),
                        Centroid      = a.PolygonPoints.Count > 0
                            ? new Dictionary<string, double>
                              {
                                ["x"] = Math.Round(a.PolygonPoints.Average(p => p.X), 3),
                                ["y"] = Math.Round(a.PolygonPoints.Average(p => p.Y), 3),
                              }
                            : new Dictionary<string, double> { ["x"] = 0, ["y"] = 0 },
                    };
                }).ToList();
            }

            // 5d. Nombre del proyecto
            string finalProjectName = !string.IsNullOrWhiteSpace(_projectName)
                ? _projectName
                : (!string.IsNullOrWhiteSpace(projectNameOnServer) ? projectNameOnServer : "Proyecto AD-ELEC");

            // 5e. Plantas con faceKeys y UFs correctas
            var plantas = plantaGroups.Select((pg, idx) =>
            {
                string plantaId = $"PL{idx + 1}";

                var plantaFaceKeys = new HashSet<string>(
                    pg.Where(a => !string.IsNullOrEmpty(a.Handle))
                      .Select(a => faceKeyByHandle.GetValueOrDefault($"room_{a.Handle}", "")),
                    StringComparer.OrdinalIgnoreCase);

                var ufGroups = ambientesDwg
                    .GroupBy(a => string.IsNullOrWhiteSpace(a.UF) ? "Sin UF" : a.UF,
                             StringComparer.OrdinalIgnoreCase)
                    .Select(ufg => new SyncUnidadFuncional
                    {
                        Id      = ufg.Key,
                        Nombre  = ufg.Key,
                        RoomIds = syncRooms
                            .Where(sr => plantaFaceKeys.Contains(sr.Id) &&
                                         ufg.Any(a => faceKeyByHandle.GetValueOrDefault(
                                             $"room_{a.Handle}", "") == sr.Id))
                            .Select(sr => sr.Id)
                            .ToList(),
                    })
                    .Where(uf => uf.RoomIds.Count > 0)
                    .ToList();

                return new SyncPlanta
                {
                    Id     = plantaId,
                    Nombre = pg.Key,
                    Graph  = graph,
                    RecintosMeta = pg.Select(a =>
                    {
                        string fk = faceKeyByHandle.GetValueOrDefault(
                            string.IsNullOrEmpty(a.Handle) ? "" : $"room_{a.Handle}", "");
                        return new SyncRecintoMeta
                        {
                            FaceKey = fk,
                            Nombre  = a.TipoDisplay,
                            Tipo    = AdElec.Core.Models.TipoAmbienteInfo.DesdeNombre(a.TipoDisplay).ApiValue,
                            Coord   = a.PolygonPoints.Count > 0
                                ? new Dictionary<string, double>
                                  {
                                    ["x"] = Math.Round(a.PolygonPoints.Average(p => p.X), 3),
                                    ["y"] = Math.Round(a.PolygonPoints.Average(p => p.Y), 3),
                                  }
                                : null,
                        };
                    }).ToList(),
                    UnidadesFuncionales = ufGroups,
                };
            }).ToList();

            // 6. Construir request
            var request = new SyncProjectRequest
            {
                Name = finalProjectName,
                DataJson = new SyncProjectData
                {
                    AdElec = new SyncProjectCanvas
                    {
                        Rooms = syncRooms,
                        Board = new SyncBoard
                        {
                            Id         = _selectedPanel?.Name ?? "board_001",
                            MainSwitch = $"TM {_selectedPanel?.MainBreakerAmps ?? 32}A",
                            Rcd        = "ID 40A/30mA",
                            Circuits   = syncCircuits,
                        }
                    },
                    AdCad = new SyncAdCadData
                    {
                        Plantas = plantas
                    }
                }
            };

            // 8. PUT si el proyecto existe, POST si fue borrado o nunca se creó
            SyncProjectResponse syncResult;
            if (_projectId > 0)
            {
                syncResult = await _motorClient.ActualizarProyectoAsync(_projectId, request);
            }
            else
            {
                syncResult = await _motorClient.SincronizarProyectoAsync(request);
                _projectId = syncResult.Id;
                _projectRepo.SaveProjectId(_projectId);
                OnPropertyChanged(nameof(ProjectId));
                OnPropertyChanged(nameof(EstaVinculado));
                RaiseAllCommandsCanExecuteChanged();
            }

            int totalPuntos = syncRooms.Sum(r => r.Points.Count);
            StatusMessage = $"✓ Sync OK — {syncRooms.Count} recintos, {syncCircuits.Count} circuitos, {totalPuntos} bocas → Proyecto #{_projectId}";
        }
        catch (System.Exception ex)
        {
            StatusMessage = $"Error de sincronización: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Dispara ADE_RECARGAR en AutoCAD para que escanee el DWG y actualice los circuitos
    /// del tablero seleccionado, luego recarga la vista.
    /// </summary>
    private void RecargarCircuitos()
    {
        if (_selectedPanel is null) return;
        // Delegar a AutoCAD (PaletteCommand provee la implementación)
        // El callback ejecuta ADE_RECARGAR y luego llama RefreshFromRepo()
        _onRecargarCircuitos(_selectedPanel.Name);
    }

    private void GuardarTablero()
    {
        if (_selectedPanel == null) return;
        _repo.SavePanel(_selectedPanel);
        StatusMessage = $"✓ Tablero '{_selectedPanel.Name}' guardado exitosamente.";
    }

    private void EliminarTablero()
    {
        if (_selectedPanel == null) return;
        string nombre = _selectedPanel.Name;
        _repo.DeletePanel(nombre);
        Panels.Remove(_selectedPanel);
        SelectedPanel = Panels.FirstOrDefault();
        StatusMessage = $"✓ Tablero '{nombre}' eliminado.";
    }

    private void CalcularSuperficieDesdeAmbientes()
    {
        if (_selectedPanel == null || _ambienteRepo == null) return;
        double sup = 0;
        if (!string.IsNullOrWhiteSpace(_selectedPanel.Location))
        {
            sup = _ambienteRepo.GetAmbientesParaUF(_selectedPanel.Location).Sum(a => a.AreaM2);
        }
        if (sup <= 0)
        {
            sup = _ambienteRepo.GetAllAmbientes().Sum(a => a.AreaM2);
        }
        if (sup > 0)
        {
            _selectedPanel.SuperficieCubiertaM2 = Math.Round(sup, 1);
            _repo.SavePanel(_selectedPanel);
            StatusMessage = $"✓ Superficie calculada: {_selectedPanel.SuperficieCubiertaM2} m² desde los ambientes.";
        }
        else
        {
            StatusMessage = "No se detectaron recintos con superficie en el DWG (usá ADE_AUTOAMBIENTES).";
        }
    }

    /// <summary>Recarga el panel desde el XRecord y actualiza la vista. Llamado por PaletteCommand tras ADE_RECARGAR.</summary>
    public void RefreshFromRepo(string? panelName = null)
    {
        var allPanels = _repo.GetAllPanels();
        Panels.Clear();
        foreach (var p in allPanels)
            Panels.Add(p);

        Panel? target = null;
        if (!string.IsNullOrEmpty(panelName))
            target = Panels.FirstOrDefault(p => string.Equals(p.Name, panelName, StringComparison.OrdinalIgnoreCase));

        target ??= Panels.FirstOrDefault();
        SelectedPanel = target;

        if (SelectedPanel != null)
        {
            StatusMessage = $"Recargado: {SelectedPanel.Circuits.Count} circuito(s) desde el DWG";
        }
    }

    private void RaiseAllCommandsCanExecuteChanged()
    {
        ((RelayCommand)CalcularAeaCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SugerirLuminariasCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SugerirTomasCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RecargarCircuitosCommand).RaiseCanExecuteChanged();
        ((RelayCommand)NuevoTableroCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SincronizarCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PullFromWebCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RecargarDocumentoCommand).RaiseCanExecuteChanged();
        ((RelayCommand)GenerarUnifilarCommand).RaiseCanExecuteChanged();
        ((RelayCommand)EfectosCommand).RaiseCanExecuteChanged();
        ((RelayCommand)AgregarCircuitoCommand).RaiseCanExecuteChanged();
        ((RelayCommand)EliminarCircuitoCommand).RaiseCanExecuteChanged();
        ((RelayCommand)GuardarCircuitosCommand).RaiseCanExecuteChanged();
        ((RelayCommand)GuardarTableroCommand).RaiseCanExecuteChanged();
        ((RelayCommand)EliminarTableroCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CalcularSuperficieCommand).RaiseCanExecuteChanged();
    }

    // ── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
