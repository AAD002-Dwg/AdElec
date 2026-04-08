using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace AdElec.AutoCAD.Helpers
{
    public static class XDataHelper
    {
        public const string APP_NAME = "AD_ELEC_SYNC";

        /// <summary>
        /// Asegura que la aplicación esté registrada en la tabla de RegApp.
        /// </summary>
        public static void EnsureRegApp(Database db, Transaction tr)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(APP_NAME))
            {
                rat.UpgradeOpen();
                var ratr = new RegAppTableRecord { Name = APP_NAME };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }
        }

        /// <summary>
        /// Asigna un ID de la web al XData de una entidad usando una transacción existente.
        /// </summary>
        public static void SetWebId(Entity ent, string webId, Transaction tr)
        {
            var db = ent.Database;
            EnsureRegApp(db, tr);
            
            var rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, APP_NAME),
                new TypedValue((int)DxfCode.ExtendedDataControlString, webId)
            );
            
            ent.XData = rb;
        }

        /// <summary>
        /// Asigna un ID de la web al XData de una entidad (Nueva transacción).
        /// </summary>
        public static void SetWebId(Entity ent, string webId)
        {
            var db = ent.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var transEnt = tr.GetObject(ent.ObjectId, OpenMode.ForWrite) as Entity;
                if (transEnt != null)
                {
                    SetWebId(transEnt, webId, tr);
                }
                tr.Commit();
            }
        }

        /// <summary>
        /// Recupera el ID de la web desde el XData de una entidad.
        /// </summary>
        public static string GetWebId(Entity ent)
        {
            var rb = ent.GetXDataForApplication(APP_NAME);
            if (rb == null) return string.Empty;

            foreach (var tv in rb)
            {
                if (tv.TypeCode == (int)DxfCode.ExtendedDataControlString)
                    return tv.Value?.ToString() ?? string.Empty;
            }

            return string.Empty;
        }
    }
}
