using System;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.DatabaseServices;
using AdElec.Core.Interfaces;

namespace AdElec.AutoCAD.Repositories
{
    public class DwgProjectRepository : IProjectRepository
    {
        private const string DICT_NAME        = "ADE_CORE_DICT";
        private const string KEY_PROJECT_ID   = "PROJECT_ID";
        private const string KEY_PROJECT_NAME = "PROJECT_NAME";
        private const string KEY_SYNC_MODE    = "SYNC_MODE";

        // ── Lectura ──────────────────────────────────────────────────────────

        public int GetProjectId()
        {
            int value = 0;
            ReadXRecord(KEY_PROJECT_ID, tv =>
            {
                if (tv.TypeCode == (short)DxfCode.Int32)
                    value = (int)tv.Value;
            });
            return value;
        }

        public string GetProjectName()
        {
            string value = "";
            ReadXRecord(KEY_PROJECT_NAME, tv =>
            {
                if (tv.TypeCode == (short)DxfCode.Text)
                    value = (string)tv.Value;
            });
            return value;
        }

        public string GetSyncMode()
        {
            string value = "AXIS";
            ReadXRecord(KEY_SYNC_MODE, tv =>
            {
                if (tv.TypeCode == (short)DxfCode.Text)
                    value = (string)tv.Value;
            });
            return value;
        }

        // ── Escritura ────────────────────────────────────────────────────────

        public void SaveProjectId(int id) =>
            WriteXRecord(KEY_PROJECT_ID, rb => rb.Add(new TypedValue((short)DxfCode.Int32, id)));

        public void SaveProjectName(string name) =>
            WriteXRecord(KEY_PROJECT_NAME, rb => rb.Add(new TypedValue((short)DxfCode.Text, name ?? "")));

        public void SaveSyncMode(string mode) =>
            WriteXRecord(KEY_SYNC_MODE, rb => rb.Add(new TypedValue((short)DxfCode.Text, mode ?? "AXIS")));

        // ── Helpers privados ─────────────────────────────────────────────────

        /// <summary>
        /// Lee el primer TypedValue del XRecord indicado y lo pasa al callback.
        /// No hace nada si el diccionario o el registro no existen.
        /// </summary>
        private static void ReadXRecord(string key, Action<TypedValue> onValue)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using var tr = doc.Database.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(doc.Database.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(DICT_NAME)) { tr.Commit(); return; }

            var dict = (DBDictionary)tr.GetObject(nod.GetAt(DICT_NAME), OpenMode.ForRead);
            if (!dict.Contains(key)) { tr.Commit(); return; }

            var record = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForRead);
            if (record.Data == null) { tr.Commit(); return; }

            foreach (TypedValue tv in record.Data)
            {
                onValue(tv);
                break; // solo el primero
            }
            tr.Commit();
        }

        /// <summary>
        /// Crea o sobreescribe el XRecord indicado usando el ResultBuffer construido por el callback.
        /// Crea el diccionario ADE_CORE_DICT si no existe.
        /// </summary>
        private static void WriteXRecord(string key, Action<ResultBuffer> buildBuffer)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(doc.Database.NamedObjectsDictionaryId, OpenMode.ForWrite);

                DBDictionary dict;
                if (!nod.Contains(DICT_NAME))
                {
                    dict = new DBDictionary();
                    nod.SetAt(DICT_NAME, dict);
                    tr.AddNewlyCreatedDBObject(dict, true);
                }
                else
                {
                    dict = (DBDictionary)tr.GetObject(nod.GetAt(DICT_NAME), OpenMode.ForWrite);
                }

                Xrecord record;
                if (dict.Contains(key))
                {
                    record = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForWrite);
                }
                else
                {
                    record = new Xrecord();
                    dict.SetAt(key, record);
                    tr.AddNewlyCreatedDBObject(record, true);
                }

                var rb = new ResultBuffer();
                buildBuffer(rb);
                record.Data = rb;
                tr.Commit();
            }
        }
    }
}
