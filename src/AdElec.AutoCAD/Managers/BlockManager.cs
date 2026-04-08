using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Managers
{
    public static class BlockManager
    {
        /// <summary>
        /// Importa un bloque desde un archivo externo .dwg si no existe en el dibujo actual.
        /// </summary>
        /// <param name="blockName">Nombre del bloque en el dibujo actual</param>
        /// <param name="sourceFilePath">Ruta absoluta al archivo .dwg que contiene el bloque (o es el bloque en sí)</param>
        /// <returns>True si se importó o ya existe, False si falló</returns>
        public static bool EnsureBlockExists(string blockName, string sourceFilePath)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (bt.Has(blockName))
                {
                    tr.Commit();
                    return true; // Ya existe
                }
                tr.Commit();
            }

            // Si no existe, lo importamos silenciosamente
            if (!File.Exists(sourceFilePath))
            {
                doc.Editor.WriteMessage($"\n[ERROR] No se encontró el archivo de bloque en: {sourceFilePath}\n");
                return false;
            }

            try
            {
                using (Database sourceDb = new Database(false, true))
                {
                    sourceDb.ReadDwgFile(sourceFilePath, FileOpenMode.OpenForReadAndAllShare, true, "");
                    
                    // Insertamos el modelo del archivo DWG externo como un bloque nuevo en nuestra base de datos actual
                    var id = db.Insert(blockName, sourceDb, false);
                    return !id.IsNull;
                }
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\n[ERROR] Falló la importación del bloque {blockName}: {ex.Message}\n");
                return false;
            }
        }
    }
}
