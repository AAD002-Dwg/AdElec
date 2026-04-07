using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using AdElec.UI.Views;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Commands
{
    public class PaletteCommand
    {
        // Static instance of the PaletteSet to prevent recreation every time the command is called
        static PaletteSet _ps = null;
        static MainPaletteView _myWpfControl = null;

        [CommandMethod("ADE_PANEL")]
        public void ShowPanel()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            
            try
            {
                if (_ps == null)
                {
                    // Basic UUID for the PaletteSet
                    _ps = new PaletteSet("AD-ELEC", new Guid("23CDA41A-6A3B-4D88-B3EE-9A4B8F67A811"));
                    _ps.Style = PaletteSetStyles.ShowPropertiesMenu | 
                                PaletteSetStyles.ShowAutoHideButton | 
                                PaletteSetStyles.ShowCloseButton;
                    
                    _ps.MinimumSize = new System.Drawing.Size(250, 400);

                    // Initialize the WPF UserControl from AdElec.UI
                    _myWpfControl = new MainPaletteView();

                    // Host the WPF control inside the PaletteSet using ElementHost
                    var host = new System.Windows.Forms.Integration.ElementHost();
                    host.AutoSize = true;
                    host.Dock = System.Windows.Forms.DockStyle.Fill;
                    host.Child = _myWpfControl;

                    _ps.Add("Panel Principal", host);
                }

                // Show the palette
                _ps.Visible = true;
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\nError al cargar la paleta: {ex.Message}\n");
            }
        }
    }
}
