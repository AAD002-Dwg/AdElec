using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AdElec.Core.Models;

namespace AdElec.UI.ViewModels
{
    /// <summary>
    /// Fila editable del DataGrid de verificación.
    /// Wrappea un <see cref="DetectedRoom"/> con propiedades bindables y checkbox de selección.
    /// </summary>
    public sealed class DetectedRoomRow : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _tipo;
        private string _tipoApi;
        private string _unidadFuncional;
        private string _planta;

        public DetectedRoom Source { get; }

        public string Id => Source.Id;
        public double AreaM2 => Source.AreaM2;
        public double PerimetroM => Source.PerimetroM;
        public string Confianza => Source.Confianza;
        public bool RequiereVerificacion => Source.RequiereVerificacion;
        public bool ContieneBulge => Source.ContieneBulge;
        public string SourceHandle => Source.SourceHandle;
        public bool IsExisting => Source.IsExisting;
        public string EstadoTexto => Source.IsExisting ? "Existente" : "Nuevo";

        public int CantidadLuminarias => Source.CantidadLuminarias;
        public int CantidadTomas => Source.CantidadTomas;
        public int TotalBocas => Source.TotalBocas;
        public double PotenciaTotalVA => Source.PotenciaTotalVA;
        public string CircuitosDetalle => string.IsNullOrEmpty(Source.CircuitosDetalle) ? "—" : Source.CircuitosDetalle;
        public IReadOnlyList<string> ArtefactosDetalle => Source.ArtefactosDetalle;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string Tipo
        {
            get => _tipo;
            set
            {
                _tipo = value;
                Source.Tipo = value;
                Source.TipoApi = TipoAmbienteInfo.DesdeNombre(value).ApiValue;
                OnPropertyChanged();
            }
        }

        public string TipoApi
        {
            get => _tipoApi;
            set { _tipoApi = value; Source.TipoApi = value; OnPropertyChanged(); }
        }

        public string UnidadFuncional
        {
            get => _unidadFuncional;
            set { _unidadFuncional = value; Source.UnidadFuncional = value; OnPropertyChanged(); }
        }

        public string Planta
        {
            get => _planta;
            set { _planta = value; Source.Planta = value; OnPropertyChanged(); }
        }

        /// <summary>Texto del indicador de confianza para la UI.</summary>
        public string ConfianzaIcon => Confianza switch
        {
            "alta"  => "✓",
            "media" => "●",
            "baja"  => "⚠",
            _       => "?"
        };

        public DetectedRoomRow(DetectedRoom source)
        {
            Source = source;
            _tipo = source.Tipo;
            _tipoApi = source.TipoApi;
            _unidadFuncional = source.UnidadFuncional;
            _planta = string.IsNullOrEmpty(source.Planta) ? "PB" : source.Planta;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// ViewModel para el panel de verificación de ADE_AUTO_AMBIENTES.
    /// Maneja la lista editable de ambientes detectados, comandos de selección,
    /// y callbacks hacia AutoCAD para resaltado bidireccional y zoom.
    /// </summary>
    public sealed class AutoAmbientesViewModel : INotifyPropertyChanged
    {
        // ── Datos ────────────────────────────────────────────────────────────

        public ObservableCollection<DetectedRoomRow> Rooms { get; }

        /// <summary>Opciones de tipo de ambiente para el ComboBox de edición.</summary>
        public IReadOnlyList<TipoAmbienteInfo> TiposLocal { get; } = TipoAmbienteInfo.Todos;

        /// <summary>Lista de UFs disponibles (auto-generadas + SIN_CLASIFICAR).</summary>
        public ObservableCollection<string> UfOptions { get; }

        /// <summary>Lista de plantas/pisos disponibles sugeridos.</summary>
        public ObservableCollection<string> PlantasOptions { get; }

        // ── Resumen ──────────────────────────────────────────────────────────

        public int TotalDetectados => Rooms.Count;
        public int TotalSeleccionados => Rooms.Count(r => r.IsSelected);
        public int RequierenVerificacion => Rooms.Count(r => r.RequiereVerificacion);
        public int SinClasificar => Rooms.Count(r => r.UnidadFuncional == "SIN_CLASIFICAR");
        public double AreaTotal => Rooms.Where(r => r.IsSelected).Sum(r => r.AreaM2);
        public string AreaTotalDisplay => $"{AreaTotal:F1} m²";

        public int TotalBocasGlobal => Rooms.Where(r => r.IsSelected).Sum(r => r.TotalBocas);
        public int TotalLuminariasGlobal => Rooms.Where(r => r.IsSelected).Sum(r => r.CantidadLuminarias);
        public int TotalTomasGlobal => Rooms.Where(r => r.IsSelected).Sum(r => r.CantidadTomas);
        public double TotalCargaGlobalVA => Rooms.Where(r => r.IsSelected).Sum(r => r.PotenciaTotalVA);

        // ── Estado ───────────────────────────────────────────────────────────

        public bool Confirmed { get; private set; }

        private DetectedRoomRow? _selectedRoom;
        public DetectedRoomRow? SelectedRoom
        {
            get => _selectedRoom;
            set
            {
                if (_selectedRoom == value) return;
                _selectedRoom = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedRoom));
                OnSelectedRoomChanged();
            }
        }

        public bool HasSelectedRoom => _selectedRoom != null;

        // ── Comandos ─────────────────────────────────────────────────────────

        public ICommand InsertarCommand { get; }
        public ICommand CancelarCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand ZoomCommand { get; }

        // ── Callbacks hacia AutoCAD (inyectados por el Command) ──────────────

        /// <summary>
        /// Callback para resaltar un polígono en AutoCAD dado su Handle hex string.
        /// Parámetro 1: handle del ObjectId de la polilínea.
        /// Si null/"", restaura los colores originales.
        /// </summary>
        private readonly Action<string>? _onHighlight;

        /// <summary>Callback para zoom a un polígono dado su Handle hex string.</summary>
        private readonly Action<string>? _onZoom;

        /// <summary>Acción para cerrar la ventana.</summary>
        private Action? _cerrar;

        // ── Constructor ──────────────────────────────────────────────────────

        public AutoAmbientesViewModel(
            List<DetectedRoom> detectedRooms,
            Action<string>? onHighlight = null,
            Action<string>? onZoom = null)
        {
            _onHighlight = onHighlight;
            _onZoom = onZoom;

            Rooms = new ObservableCollection<DetectedRoomRow>(
                detectedRooms.Select(r => new DetectedRoomRow(r)));

            // Generar lista de UFs únicas
            var ufs = detectedRooms
                .Select(r => r.UnidadFuncional)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(u => u)
                .ToList();
            if (!ufs.Contains("UF-01") && !ufs.Contains("UF1")) ufs.Insert(0, "UF1");
            if (!ufs.Contains("SIN_CLASIFICAR")) ufs.Add("SIN_CLASIFICAR");
            UfOptions = new ObservableCollection<string>(ufs);

            // Generar lista de plantas únicas
            var plantas = detectedRooms
                .Select(r => r.Planta)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var defaultP in new[] { "PB", "1° Piso", "2° Piso", "3° Piso", "Subsuelo", "Terraza" })
            {
                if (!plantas.Contains(defaultP, StringComparer.OrdinalIgnoreCase))
                    plantas.Add(defaultP);
            }
            PlantasOptions = new ObservableCollection<string>(plantas);

            // Suscribir para actualizar resumen cuando cambia IsSelected
            foreach (var row in Rooms)
            {
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(DetectedRoomRow.IsSelected))
                        RefreshSummary();
                };
            }

            InsertarCommand = new RelayCommand(Insertar, () => Rooms.Any(r => r.IsSelected));
            CancelarCommand = new RelayCommand(Cancelar);
            SelectAllCommand = new RelayCommand(SelectAll);
            DeselectAllCommand = new RelayCommand(DeselectAll);
            ZoomCommand = new RelayCommand(ZoomToSelected, () => SelectedRoom != null);
        }

        public void SetCerrar(Action cerrar) => _cerrar = cerrar;

        // ── Lógica de comandos ───────────────────────────────────────────────

        private void Insertar()
        {
            Confirmed = true;
            // Restaurar colores antes de cerrar
            _onHighlight?.Invoke("");
            _cerrar?.Invoke();
        }

        private void Cancelar()
        {
            Confirmed = false;
            _onHighlight?.Invoke("");
            _cerrar?.Invoke();
        }

        private void SelectAll()
        {
            foreach (var r in Rooms) r.IsSelected = true;
            RefreshSummary();
        }

        private void DeselectAll()
        {
            foreach (var r in Rooms) r.IsSelected = false;
            RefreshSummary();
        }

        private void ZoomToSelected()
        {
            if (SelectedRoom == null) return;
            _onZoom?.Invoke(SelectedRoom.SourceHandle);
        }

        /// <summary>
        /// Llamado cuando cambia la fila seleccionada en el DataGrid.
        /// Dispara el resaltado bidireccional en AutoCAD.
        /// </summary>
        private void OnSelectedRoomChanged()
        {
            string handle = SelectedRoom?.SourceHandle ?? "";
            _onHighlight?.Invoke(handle);
        }

        private void RefreshSummary()
        {
            OnPropertyChanged(nameof(TotalSeleccionados));
            OnPropertyChanged(nameof(AreaTotal));
            OnPropertyChanged(nameof(AreaTotalDisplay));
            OnPropertyChanged(nameof(TotalBocasGlobal));
            OnPropertyChanged(nameof(TotalLuminariasGlobal));
            OnPropertyChanged(nameof(TotalTomasGlobal));
            OnPropertyChanged(nameof(TotalCargaGlobalVA));
        }

        /// <summary>
        /// Devuelve los rooms seleccionados con las ediciones del usuario aplicadas.
        /// </summary>
        public List<DetectedRoom> GetConfirmedRooms() =>
            Rooms.Where(r => r.IsSelected).Select(r => r.Source).ToList();

        // ── INotifyPropertyChanged ───────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
