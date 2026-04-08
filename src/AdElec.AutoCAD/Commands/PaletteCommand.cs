using System;
using System.IO;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using AdElec.UI.Views;
using AdElec.UI.ViewModels;
using AdElec.AutoCAD.Repositories;
using AdElec.Core.Interfaces;
using AdElec.Core.AeaMotor;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Commands
{
    public class PaletteCommand
    {
        private static PaletteSet? _ps;
        private static MainPaletteView? _view;
        private static PanelViewModel? _vm;
        // Último documento notificado: evita disparar el reload si el evento
        // se repite para el mismo documento (AutoCAD lo dispara varias veces).
        private static string? _lastActiveDocPath;

        [CommandMethod("ADE_PANEL")]
        public void ShowPanel()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                if (_ps == null)
                {
                    // ── Dependencias ────────────────────────────────────────
                    var repo         = new DwgPanelRepository();
                    var ambienteRepo = new DwgAmbienteRepository();
                    var projectRepo  = new DwgProjectRepository();
                    var motorClient  = new AeaMotorClient();

                    // ── Callbacks hacia AutoCAD ──────────────────────────────
                    Action onLuminarias = () =>
                        Application.DocumentManager.MdiActiveDocument
                            ?.SendStringToExecute("ADE_LUMINARIAS\n", false, false, true);

                    Action onTomas = () =>
                        Application.DocumentManager.MdiActiveDocument
                            ?.SendStringToExecute("ADE_TOMAS\n", false, false, true);

                    Action<string, string> onInsertarTablero = (panelName, visibilidad) =>
                    {
                        InsertarTableroCommand.PendingPanelName    = panelName;
                        InsertarTableroCommand.PendingVisibilidad  = visibilidad;
                        Application.DocumentManager.MdiActiveDocument
                            ?.SendStringToExecute("ADE_INSERTAR_TABLERO\n", false, false, true);
                    };

                    Action<string> onRecargarCircuitos = (panelName) =>
                    {
                        Application.DocumentManager.MdiActiveDocument
                            ?.SendStringToExecute($"ADE_RECARGAR\n{panelName}\n", false, false, true);

                        System.Threading.Tasks.Task.Delay(900).ContinueWith(_ =>
                        {
                            System.Windows.Application.Current?.Dispatcher.Invoke(
                                () => _vm?.RefreshFromRepo(panelName));
                        });
                    };

                    Action onPullFromWeb = () =>
                        Application.DocumentManager.MdiActiveDocument
                            ?.SendStringToExecute("ADE_PULL\n", false, false, true);

                    // Recarga forzada desde el documento activo (callback del botón ↺)
                    Action onRecargarDocumento = () =>
                        Application.DocumentManager.MdiActiveDocument
                            ?.SendStringToExecute("ADE_PANEL\n", false, false, true);

                    var electricoRepo = new DwgElectricoRepository();
                    Func<string, List<AdElec.Core.AeaMotor.Dtos.SyncRoom>> onGetRoomsConPuntos =
                        (tableroName) => electricoRepo.GetRoomsConPuntos(tableroName);

                    // ── Crear ViewModel y Vista ──────────────────────────────
                    _vm = new PanelViewModel(
                        repo, motorClient, projectRepo,
                        onLuminarias, onTomas,
                        onInsertarTablero, ambienteRepo,
                        onRecargarCircuitos, onGetRoomsConPuntos,
                        onPullFromWeb, onRecargarDocumento);

                    _view = new MainPaletteView(_vm);

                    // ── Crear PaletteSet ─────────────────────────────────────
                    _ps = new PaletteSet("AD-ELEC", new Guid("23CDA41A-6A3B-4D88-B3EE-9A4B8F67A811"));
                    _ps.Style = PaletteSetStyles.ShowPropertiesMenu |
                                PaletteSetStyles.ShowAutoHideButton |
                                PaletteSetStyles.ShowCloseButton;
                    _ps.MinimumSize = new System.Drawing.Size(260, 450);

                    var host = new System.Windows.Forms.Integration.ElementHost
                    {
                        AutoSize = true,
                        Dock    = System.Windows.Forms.DockStyle.Fill,
                        Child   = _view,
                    };
                    _ps.Add("Panel Principal", host);

                    // ── Suscribir detección de cambio de documento (mejor esfuerzo) ─
                    _lastActiveDocPath = doc.Name;
                    Application.Idle += OnApplicationIdle;
                }
                else if (!string.Equals(doc.Name, _lastActiveDocPath, StringComparison.OrdinalIgnoreCase))
                {
                    // ADE_PANEL ejecutado en un documento diferente al que tiene la paleta:
                    // recargar datos desde el doc activo (contexto garantizadamente correcto aquí).
                    ReloadFromCurrentDoc(doc);
                }

                _ps.Visible = true;
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\nError al cargar la paleta: {ex.Message}\n");
            }
        }

        /// <summary>
        /// Recarga la paleta con los datos del documento pasado como argumento.
        /// Debe llamarse desde el hilo de AutoCAD para garantizar contexto correcto.
        /// </summary>
        private static void ReloadFromCurrentDoc(Document doc)
        {
            _lastActiveDocPath = doc.Name;
            string dwgName = Path.GetFileNameWithoutExtension(doc.Name);

            try
            {
                var projectRepo = new DwgProjectRepository();
                int    projectId   = projectRepo.GetProjectId();
                string projectName = projectRepo.GetProjectName();
                string syncMode    = projectRepo.GetSyncMode();

                var panelRepo = new DwgPanelRepository();
                var panels    = panelRepo.GetAllPanels();

                System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                    () => _vm?.ReloadWithPreloadedData(dwgName, projectId, projectName, syncMode, panels));
            }
            catch { /* ignorar si el doc no está listo */ }
        }

        /// <summary>
        /// Se dispara cuando AutoCAD está idle (en el command loop, hilo principal).
        /// Detecta cambios de documento activo y recarga la paleta con los datos correctos.
        /// La deduplicación por path garantiza que el trabajo real ocurre solo al cambiar de doc.
        /// </summary>
        private static void OnApplicationIdle(object sender, EventArgs e)
        {
            if (_vm == null) return;

            var currentDoc = Application.DocumentManager.MdiActiveDocument;
            if (currentDoc == null) return;

            // Mismo documento → nada que hacer
            if (string.Equals(currentDoc.Name, _lastActiveDocPath, StringComparison.OrdinalIgnoreCase))
                return;

            ReloadFromCurrentDoc(currentDoc);
        }
    }
}

