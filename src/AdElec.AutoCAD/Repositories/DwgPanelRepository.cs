using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.DatabaseServices;
using AdElec.Core.Models;
using AdElec.Core.Interfaces;
using Panel = AdElec.Core.Models.Panel;

namespace AdElec.AutoCAD.Repositories
{
    public class DwgPanelRepository : IPanelRepository
    {
        private const string DICT_NAME = "ADE_PANELS_DICT";
        private const string RECORD_NAME = "ADE_PANELS_DATA";

        public void SavePanel(Panel panel)
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return;

            var db = document.Database;

            // Read all existing to update this one
            var panels = GetAllPanels();
            var existing = panels.FirstOrDefault(p => p.Name == panel.Name);
            if (existing != null)
            {
                panels.Remove(existing);
            }
            panels.Add(panel);

            string json = JsonSerializer.Serialize(panels);

            // LockDocument es obligatorio para escrituras fuera de un comando AutoCAD activo
            using (document.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                DBDictionary dict;

                // Ensure custom dictionary exists
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

                // Ensure custom XRecord exists
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

                // Store payload in 2000-character chunks
                ResultBuffer rb = new ResultBuffer();
                int chunkSize = 2000;
                for (int i = 0; i < json.Length; i += chunkSize)
                {
                    string chunk = json.Substring(i, Math.Min(chunkSize, json.Length - i));
                    rb.Add(new TypedValue((short)DxfCode.Text, chunk));
                }

                record.Data = rb;
                tr.Commit();
            }
        }

        public void DeletePanel(string panelName)
        {
            var panels = GetAllPanels();
            var existing = panels.FirstOrDefault(p => p.Name == panelName);
            if (existing != null)
            {
                panels.Remove(existing);
                // Call internal save for clean re-insertion
                if (panels.Count > 0)
                {
                    SavePanel(panels[0]); // Reuses SavePanel logic to rewrite all panels
                }
                else
                {
                    // Logic to empty dictionary if zero items remain could be placed here
                    SaveEmpty();
                }
            }
        }

        private void SaveEmpty()
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return;
            var db = document.Database;
            using (document.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                if (nod.Contains(DICT_NAME))
                {
                    var dictId = nod.GetAt(DICT_NAME);
                    var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForWrite);
                    if (dict.Contains(RECORD_NAME))
                    {
                        var recId = dict.GetAt(RECORD_NAME);
                        var record = (Xrecord)tr.GetObject(recId, OpenMode.ForWrite);
                        record.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, "[]"));
                    }
                }
                tr.Commit();
            }
        }

        public List<Panel> GetAllPanels()
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return new List<Panel>();
            
            var db = document.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(DICT_NAME))
                    return new List<Panel>();

                var dictId = nod.GetAt(DICT_NAME);
                var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead);

                if (!dict.Contains(RECORD_NAME))
                    return new List<Panel>();

                var recordId = dict.GetAt(RECORD_NAME);
                var record = (Xrecord)tr.GetObject(recordId, OpenMode.ForRead);

                if (record.Data == null)
                    return new List<Panel>();

                string json = "";
                foreach (TypedValue tv in record.Data)
                {
                    if (tv.TypeCode == (short)DxfCode.Text)
                    {
                        json += (string)tv.Value;
                    }
                }

                tr.Commit();

                if (string.IsNullOrEmpty(json)) return new List<Panel>();

                try
                {
                    return JsonSerializer.Deserialize<List<Panel>>(json) ?? new List<Panel>();
                }
                catch
                {
                    return new List<Panel>();
                }
            }
        }
    }
}
