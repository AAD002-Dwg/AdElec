using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AdElec.Core.AeaMotor;
using AdElec.Core.AeaMotor.Dtos;
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
            RefreshCircuits();
            RaiseAllCommandsCanExecuteChanged();
        }
    }

    public bool HasPanel => _selectedPanel is not null;
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); RaiseAllCommandsCanExecuteChanged(); } }

    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }
    public bool IsMotorAvailable { get => _isMotorAvailable; private set { _isMotorAvailable = value; OnPropertyChanged(); OnPropertyChanged(nameof(MotorStatusText)); OnPropertyChanged(nameof(MotorStatusColor)); } }

    public string MotorStatusText => IsMotorAvailable ? "AEA-MOTOR ●" : "AEA-MOTOR ○";
    public string MotorStatusColor => IsMotorAvailable ? "#4CAF50" : "#F44336";

    public double TotalLoadVA => _selectedPanel?.TotalLoadVA() ?? 0;
    public double TotalLoadKVA => Math.Round(TotalLoadVA / 1000.0, 2);

    public string? LastGrado => _lastResultado?.Grado?.Grado;
    public bool? CumpleNorma => _lastResultado?.CumpleNorma;
    public string CumpleNormaText => _lastResultado is null ? "" : (CumpleNorma == true ? "✓ CUMPLE" : "✗ NO CUMPLE");
    public string CumpleNormaColor => CumpleNorma == true ? "#4CAF50" : "#F44336";

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

    public bool ShowNewPanelForm { get => _showNewPanelForm; set { _showNewPanelForm = value; OnPropertyChanged(); } }
    public string NewPanelName { get => _newPanelName; set { _newPanelName = value; OnPropertyChanged(); } }
    public string NewPanelLocation { get => _newPanelLocation; set { _newPanelLocation = value; OnPropertyChanged(); } }
    public int NewPanelPhaseCount { get => _newPanelPhaseCount; set { _newPanelPhaseCount = value; OnPropertyChanged(); } }
    public string NewPanelTipo { get => _newPanelTipo; set { _newPanelTipo = value; OnPropertyChanged(); } }
    public double NewPanelSuperficie { get => _newPanelSuperficie; set { _newPanelSuperficie = value; OnPropertyChanged(); } }

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
        Action? onPullFromWeb = null)
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
        // Limpiar resultado de cálculo anterior (pertenecía al otro documento)
        _lastResultado = null;
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
        if (_selectedPanel is null) return;
        foreach (var c in _selectedPanel.Circuits)
            Circuits.Add(c);
        OnPropertyChanged(nameof(TotalLoadVA));
        OnPropertyChanged(nameof(TotalLoadKVA));
        OnPropertyChanged(nameof(HasCircuits));
    }

    private void ConfirmarNuevoTablero()
    {
        var panel = new Panel
        {
            Name = NewPanelName.Trim().ToUpper(),
            Location = NewPanelLocation.Trim(),
            PhaseCount = NewPanelPhaseCount,
            SuperficieCubiertaM2 = NewPanelSuperficie,
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
        StatusMessage = "Calculando con AEA-MOTOR...";

        try
        {
            // Leer ambientes del DWG para la UF del tablero seleccionado
            List<AmbienteDto>? ambientesDto = null;
            double superficie = _selectedPanel.SuperficieCubiertaM2;

            if (_ambienteRepo is not null && !string.IsNullOrWhiteSpace(_selectedPanel.Location))
            {
                var ambientes = _ambienteRepo.GetAmbientesParaUF(_selectedPanel.Location);
                if (ambientes.Count > 0)
                {
                    ambientesDto = ambientes.Select(a => a.ToAmbienteDto()).ToList();
                    // Superficie = suma de áreas de todos los recintos si no fue ingresada manualmente
                    if (superficie <= 0)
                        superficie = ambientes.Sum(a => a.AreaM2);
                    StatusMessage = $"Calculando — {ambientes.Count} recintos para '{_selectedPanel.Location}'...";
                }
            }

            var proyecto = _selectedPanel.ToProyectoDto(superficieCubiertaM2: superficie, ambientes: ambientesDto);
            _lastResultado = await _motorClient.CalcularAsync(proyecto);
            _selectedPanel.AplicarResultados(_lastResultado);

            // Persistir el grado en el XRecord para que ADE_PROPUESTA lo lea en futuras sesiones
            _selectedPanel.LastGrado = _lastResultado?.Grado?.Grado;
            _repo.SavePanel(_selectedPanel);

            RefreshCircuits();
            OnPropertyChanged(nameof(LastGrado));
            OnPropertyChanged(nameof(CumpleNorma));
            OnPropertyChanged(nameof(CumpleNormaText));
            OnPropertyChanged(nameof(CumpleNormaColor));

            string grado = _lastResultado?.Grado?.Grado ?? "—";
            int nCirc = _lastResultado?.Circuitos?.Count ?? 0;
            StatusMessage = $"Grado {grado} · {nCirc} circuitos · {CumpleNormaText}";
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex.GetType().Name == "HttpRequestException")
        {
            StatusMessage = "AEA-MOTOR no responde. ¿Está corriendo?";
            IsMotorAvailable = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckMotorAsync()
    {
        IsMotorAvailable = await _motorClient.EstaDisponibleAsync();
        StatusMessage = IsMotorAvailable
            ? "AEA-MOTOR conectado."
            : "AEA-MOTOR no disponible (iniciá Iniciar_Proyecto.bat).";
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

            // 3. Puntos eléctricos: rooms con puntos desde bloques eléctricos
            List<SyncRoom> syncRooms;
            if (_selectedPanel != null && _onGetRoomsConPuntos != null)
            {
                // Rooms con puntos eléctricos ya asignados (IDs estables por Handle)
                syncRooms = _onGetRoomsConPuntos(_selectedPanel.Name);

                // Completar polígonos faltantes desde ambientesDwg usando Handle (IDs coinciden)
                var ambByHandle = ambientesDwg
                    .Where(a => !string.IsNullOrEmpty(a.Handle))
                    .ToDictionary(a => $"room_{a.Handle}", a => a, StringComparer.OrdinalIgnoreCase);

                foreach (var sr in syncRooms.Where(r => r.PolygonPoints.Count == 0))
                {
                    if (ambByHandle.TryGetValue(sr.Id, out var match) && match.PolygonPoints.Count > 0)
                        sr.PolygonPoints = match.PolygonPoints
                            .Select(p => new Dictionary<string, double>
                                { ["x"] = Math.Round(p.X, 3), ["y"] = Math.Round(p.Y, 3) })
                            .ToList();
                }
            }
            else
            {
                // Sin tablero seleccionado: sólo geometría, sin puntos (IDs estables por Handle)
                syncRooms = ambientesDwg.Select(a => new SyncRoom
                {
                    Id            = string.IsNullOrEmpty(a.Handle) ? $"room_{ambientesDwg.IndexOf(a):000}" : $"room_{a.Handle}",
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
                }).ToList();
            }

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

            // 5b. Índice Handle→UF para grouping correcto (un Handle identifica un único recinto)
            var handleUfMap = ambientesDwg
                .Where(a => !string.IsNullOrEmpty(a.Handle))
                .ToDictionary(a => $"room_{a.Handle}", a => a.UF, StringComparer.OrdinalIgnoreCase);

            // 5c. Nombre del proyecto
            string finalProjectName = !string.IsNullOrWhiteSpace(_projectName)
                ? _projectName
                : (!string.IsNullOrWhiteSpace(projectNameOnServer) ? projectNameOnServer : "Proyecto AD-ELEC");

            // 5d. Plantas: agrupar ambientes por campo Planta → un SyncPlanta por planta
            var plantaGroups = ambientesDwg
                .GroupBy(a => string.IsNullOrWhiteSpace(a.Planta) ? "PB" : a.Planta,
                         StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key)
                .ToList();

            var plantas = plantaGroups.Select((pg, idx) =>
            {
                // IDs de rooms de esta planta
                var plantaRoomIds = new HashSet<string>(
                    pg.Where(a => !string.IsNullOrEmpty(a.Handle)).Select(a => $"room_{a.Handle}"),
                    StringComparer.OrdinalIgnoreCase);

                // UFs de esta planta (con los rooms que le corresponden)
                var ufGroups = ambientesDwg
                    .GroupBy(a => string.IsNullOrWhiteSpace(a.UF) ? "Sin UF" : a.UF,
                             StringComparer.OrdinalIgnoreCase)
                    .Select(ufg => new SyncUnidadFuncional
                    {
                        Id      = ufg.Key,
                        Nombre  = ufg.Key,
                        RoomIds = syncRooms
                            .Where(sr => plantaRoomIds.Contains(sr.Id) &&
                                         handleUfMap.TryGetValue(sr.Id, out var uf) &&
                                         string.Equals(uf, ufg.Key, StringComparison.OrdinalIgnoreCase))
                            .Select(sr => sr.Id)
                            .ToList(),
                    })
                    .Where(uf => uf.RoomIds.Count > 0)
                    .ToList();

                return new SyncPlanta
                {
                    Id     = $"PL{idx + 1}",
                    Nombre = pg.Key,
                    Graph  = graph,   // grafo compartido; AD-CAD filtra por planta con recintosMeta
                    RecintosMeta = pg.Select(a => new SyncRecintoMeta
                    {
                        FaceKey = "",
                        Nombre  = a.TipoDisplay,
                        Tipo    = AdElec.Core.Models.TipoAmbienteInfo.DesdeNombre(a.TipoDisplay).ApiValue,
                        Coord   = a.PolygonPoints.Count > 0
                            ? new System.Collections.Generic.Dictionary<string, double>
                              {
                                ["x"] = Math.Round(a.PolygonPoints.Average(p => p.X), 3),
                                ["y"] = Math.Round(a.PolygonPoints.Average(p => p.Y), 3),
                              }
                            : null,
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

    /// <summary>Recarga el panel desde el XRecord y actualiza la vista. Llamado por PaletteCommand tras ADE_RECARGAR.</summary>
    public void RefreshFromRepo(string panelName)
    {
        var updated = _repo.GetAllPanels().FirstOrDefault(p => p.Name == panelName);
        if (updated is null) return;
        _selectedPanel = updated;
        RefreshCircuits();
        StatusMessage = $"Recargado: {updated.Circuits.Count} circuito(s) desde el DWG";
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
    }

    // ── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
