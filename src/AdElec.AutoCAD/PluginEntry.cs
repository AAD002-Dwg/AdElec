using System;
using System.IO;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(AdElec.AutoCAD.PluginEntry))]

namespace AdElec.AutoCAD
{
    public class PluginEntry : IExtensionApplication
    {
        public void Initialize()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            doc.Editor.WriteMessage("\n>>> Cargando AD-ELEC Plugin. Escribe ADE_PANEL para comenzar.\n");

            // Cargar archivos LISP desde la carpeta LISP/ junto al DLL
            CargarLispFiles();
        }

        public void Terminate() { }

        private static void CargarLispFiles()
        {
            // La carpeta lisp/ contiene las rutinas AutoLISP del plugin
            string dllDir  = Path.GetDirectoryName(
                typeof(PluginEntry).Assembly.Location) ?? "";

            // En desarrollo, subir hasta la raíz del repo para encontrar lisp/
            string lispDir = BuscarDirectorio(dllDir, "lisp");

            if (string.IsNullOrEmpty(lispDir))
            {
                Application.DocumentManager.MdiActiveDocument?
                    .Editor.WriteMessage(
                        "\n[AD-ELEC] Carpeta LISP/ no encontrada — " +
                        "ADE_LUMINARIAS, ADE_TOMAS y ADE_CANERIAS no disponibles.");
                return;
            }

            string[] archivos = {
                Path.Combine("vendor", "GrSnapV1-0.lsp"),
                "ade_utils.lsp",
                "ade_luminarias.lsp",
                "ade_tomas.lsp",
                "ade_canerias.lsp",
                "ade_nomenclatura.lsp"
            };

            foreach (var archivo in archivos)
            {
                string ruta = Path.Combine(lispDir, archivo);
                if (File.Exists(ruta))
                {
                    // Escapar backslashes para la cadena LISP
                    string rutaLisp = ruta.Replace("\\", "/");
                    Application.DocumentManager.MdiActiveDocument?
                        .SendStringToExecute(
                            $"(load \"{rutaLisp}\")\n", false, false, true);
                }
                else
                {
                    Application.DocumentManager.MdiActiveDocument?
                        .Editor.WriteMessage(
                            $"\n[AD-ELEC] LISP no encontrado: {ruta}");
                }
            }
        }

        /// <summary>
        /// Busca hacia arriba desde startDir hasta encontrar una carpeta con el nombre dado.
        /// </summary>
        private static string BuscarDirectorio(string startDir, string nombre)
        {
            string dir = startDir;
            for (int i = 0; i < 8; i++)
            {
                string candidate = Path.Combine(dir, nombre);
                if (Directory.Exists(candidate)) return candidate;
                string? parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            return "";
        }
    }
}
