using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace AdElec.AutoCAD.Commands
{
    public class AutoDatasetCommand
    {
        [CommandMethod("ADE_EXPORTAR_DATASET")]
        public void ExportDatasetCommand()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            // Require saved drawing
            string sourcePath = db.Filename;
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                ed.WriteMessage("\n[!] Error: El plano debe estar guardado en disco para exportarlo como Dataset.");
                return;
            }

            string dxfToProcess = sourcePath;
            bool isTemporary = false;

            ed.WriteMessage("\n══ ADE_EXPORTAR_DATASET: Generador de Datos Sintéticos YOLO ══");

            try
            {
                if (sourcePath.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                {
                    ed.WriteMessage("\n  [INFO] El archivo es DWG. Generando DXF temporal...");
                    dxfToProcess = Path.Combine(Path.GetTempPath(), "ADE_Temp_Dataset.dxf");
                    // Exportar a DXF (Version 2018 o superior para compatibilidad)
                    db.DxfOut(dxfToProcess, 16, DwgVersion.Current);
                    isTemporary = true;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ERROR] No se pudo generar el DXF temporal: {ex.Message}");
                return;
            }

            string scriptPath = FindDatasetExporterScript();
            if (scriptPath == null)
            {
                ed.WriteMessage("\n[!] Error: No se encontró el script 'DatasetExporter.py'.");
                return;
            }
            ed.WriteMessage($"\n  Script: {scriptPath}");

            // Ruta centralizada dentro del proyecto de IA
            string centralBaseDir = Path.GetDirectoryName(scriptPath) ?? "";
            string outDir = Path.Combine(centralBaseDir, "TRAINING_DATA");
            
            ed.WriteMessage($"\n  Destino Centralizado: {outDir}");
            ed.WriteMessage("\n  Iniciando motor de segmentación en segundo plano (Python/Matplotlib)...");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{scriptPath}\" --dxf \"{dxfToProcess}\" --outdir \"{outDir}\" --window 15 --overlap 0.3",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };
                
                proc.Start();
                
                // Leemos síncronamente los búferes para descartar fallos de eventos
                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();
                
                proc.WaitForExit(60000); 

                if (!string.IsNullOrEmpty(output))
                    ed.WriteMessage($"\n{output.Replace("\r\n", "\n")}");
                if (!string.IsNullOrEmpty(error))
                    ed.WriteMessage($"\n[IA-LOG] {error.Replace("\r\n", "\n")}");

                if (proc.ExitCode == 0)
                {
                    ed.WriteMessage($"\n\n¡Proceso de Slicing Completado Exitosamente!");
                    ed.WriteMessage($"\n  Carpeta generada: {outDir}");
                }
                else
                {
                    ed.WriteMessage($"\n\n[!] El exportador terminó con errores. Revisa los mensajes [IA-LOG] arriba.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ERROR] ADE_EXPORTAR_DATASET: {ex.Message}");
            }
            finally
            {
                if (isTemporary && File.Exists(dxfToProcess))
                {
                    try { File.Delete(dxfToProcess); } catch { }
                }
            }
        }

        private static string FindDatasetExporterScript()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                while (!string.IsNullOrEmpty(dir))
                {
                    string p = Path.Combine(dir, "src", "AdElec.AiSegmentation", "DatasetExporter.py");
                    if (File.Exists(p)) return p;
                    
                    p = Path.Combine(dir, "AdElec.AiSegmentation", "DatasetExporter.py");
                    if (File.Exists(p)) return p;
                    
                    var parent = Directory.GetParent(dir);
                    if (parent == null) break;
                    dir = parent.FullName;
                }
            }
            catch { }
            return null;
        }
    }
}
