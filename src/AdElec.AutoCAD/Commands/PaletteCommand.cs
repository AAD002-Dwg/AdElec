using System;
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

        [CommandMethod("ADE_PANEL")]
        public void ShowPanel()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                if (_ps == null)
                {
                    // Dependencias
                    var repo = new DwgPanelRepository();
                    var ambienteRepo = new DwgAmbienteRepository();
                    var motorClient = new AeaMotorClient(); // localhost:8000

                    // Callbacks que ejecutan los comandos de AutoCAD desde la UI
                    Action onLuminarias = () =>
                        Application.DocumentManager.MdiActiveDocument
                            ?.SendStringToExecute("ADE_LUMINARIAS\n", false, false, true);

                    Action onTomas = () =>
                        Application.DocumentManager.MdiActiveDocument
                            ?.SendStringToExecute("ADE_TOMAS\n", false, false, true);

                    Action<string, string> onInsertarTablero = (panelName, visibilidad) =>
                    {
                        InsertarTableroCommand.PendingPanelName = panelName;
                        InsertarTableroCommand.PendingVisibilidad = visibilidad;
                        Application.DocumentManager.MdiActiveDocument
                            ?.SendStringToExecute("ADE_INSERTAR_TABLERO\n", false, false, true);
                    };

                    var viewModel = new PanelViewModel(
                        repo, motorClient,
                        onLuminarias, onTomas,
                        onInsertarTablero,
                        ambienteRepo);
                    _view = new MainPaletteView(viewModel);

                    _ps = new PaletteSet("AD-ELEC", new Guid("23CDA41A-6A3B-4D88-B3EE-9A4B8F67A811"));
                    _ps.Style = PaletteSetStyles.ShowPropertiesMenu |
                                PaletteSetStyles.ShowAutoHideButton |
                                PaletteSetStyles.ShowCloseButton;
                    _ps.MinimumSize = new System.Drawing.Size(260, 450);

                    var host = new System.Windows.Forms.Integration.ElementHost
                    {
                        AutoSize = true,
                        Dock = System.Windows.Forms.DockStyle.Fill,
                        Child = _view
                    };

                    _ps.Add("Panel Principal", host);
                }

                _ps.Visible = true;
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\nError al cargar la paleta: {ex.Message}\n");
            }
        }
    }
}
