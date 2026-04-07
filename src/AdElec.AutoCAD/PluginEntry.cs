using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using AdElec.AutoCAD;

[assembly: ExtensionApplication(typeof(PluginEntry))]

namespace AdElec.AutoCAD
{
    public class PluginEntry : IExtensionApplication
    {
        public void Initialize()
        {
            // Fires when the DLL is loaded via NETLOAD in AutoCAD
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.Editor.WriteMessage("\n>>> Cargando AD-ELEC Plugin. Escribe ADE_PANEL para comenzar.\n");
            }
        }

        public void Terminate()
        {
            // Fires when AutoCAD closes or unloads the plugin
        }
    }
}
