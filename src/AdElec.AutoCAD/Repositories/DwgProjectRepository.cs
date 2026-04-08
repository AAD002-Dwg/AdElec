using System;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.DatabaseServices;
using AdElec.Core.Interfaces;

namespace AdElec.AutoCAD.Repositories
{
    public class DwgProjectRepository : IProjectRepository
    {
        private const string DICT_NAME = "ADE_CORE_DICT";
        private const string RECORD_NAME = "PROJECT_ID";
        private const string MODE_RECORD_NAME = "SYNC_MODE";

        public int GetProjectId()
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return 0;
            
            var db = document.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(DICT_NAME))
                    return 0;

                var dictId = nod.GetAt(DICT_NAME);
                var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead);

                if (!dict.Contains(RECORD_NAME))
                    return 0;

                var recordId = dict.GetAt(RECORD_NAME);
                var record = (Xrecord)tr.GetObject(recordId, OpenMode.ForRead);

                if (record.Data == null)
                    return 0;

                // El ID se guarda como un entero
                int projectId = 0;
                foreach (TypedValue tv in record.Data)
                {
                    if (tv.TypeCode == (short)DxfCode.Int32)
                    {
                        projectId = (int)tv.Value;
                        break;
                    }
                }

                tr.Commit();
                return projectId;
            }
        }

        public void SaveProjectId(int id)
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return;

            var db = document.Database;

            // LockDocument es vital para escrituras fuera de un comando nativo síncrono
            using (document.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                DBDictionary dict;

                if (!nod.Contains(DICT_NAME))
                {
                    dict = new DBDictionary();
                    nod.SetAt(DICT_NAME, dict);
                    tr.AddNewlyCreatedDBObject(dict, true);
                }
                else
                {
                    var dictId = nod.GetAt(DICT_NAME);
                    dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForWrite);
                }

                Xrecord record;
                if (dict.Contains(RECORD_NAME))
                {
                    var recId = dict.GetAt(RECORD_NAME);
                    record = (Xrecord)tr.GetObject(recId, OpenMode.ForWrite);
                }
                else
                {
                    record = new Xrecord();
                    dict.SetAt(RECORD_NAME, record);
                    tr.AddNewlyCreatedDBObject(record, true);
                }

                ResultBuffer rb = new ResultBuffer();
                rb.Add(new TypedValue((short)DxfCode.Int32, id));
                
                record.Data = rb;
                tr.Commit();
            }
        }
        public string GetSyncMode()
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return "AXIS";

            var db = document.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(DICT_NAME)) return "AXIS";

                var dictId = nod.GetAt(DICT_NAME);
                var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead);

                if (!dict.Contains(MODE_RECORD_NAME)) return "AXIS";

                var recordId = dict.GetAt(MODE_RECORD_NAME);
                var record = (Xrecord)tr.GetObject(recordId, OpenMode.ForRead);

                if (record.Data == null) return "AXIS";

                string syncMode = "AXIS";
                foreach (TypedValue tv in record.Data)
                {
                    if (tv.TypeCode == (short)DxfCode.Text)
                    {
                        syncMode = (string)tv.Value;
                        break;
                    }
                }
                tr.Commit();
                return syncMode;
            }
        }

        public void SaveSyncMode(string mode)
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return;

            var db = document.Database;

            using (document.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                DBDictionary dict;

                if (!nod.Contains(DICT_NAME))
                {
                    dict = new DBDictionary();
                    nod.SetAt(DICT_NAME, dict);
                    tr.AddNewlyCreatedDBObject(dict, true);
                }
                else
                {
                    var dictId = nod.GetAt(DICT_NAME);
                    dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForWrite);
                }

                Xrecord record;
                if (dict.Contains(MODE_RECORD_NAME))
                {
                    var recId = dict.GetAt(MODE_RECORD_NAME);
                    record = (Xrecord)tr.GetObject(recId, OpenMode.ForWrite);
                }
                else
                {
                    record = new Xrecord();
                    dict.SetAt(MODE_RECORD_NAME, record);
                    tr.AddNewlyCreatedDBObject(record, true);
                }

                ResultBuffer rb = new ResultBuffer();
                rb.Add(new TypedValue((short)DxfCode.Text, mode));

                record.Data = rb;
                tr.Commit();
            }
        }
    }
}
