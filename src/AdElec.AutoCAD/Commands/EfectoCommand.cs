using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Commands
{
    /// <summary>
    /// Asistente interactivo de efectos e interruptores para AutoCAD.
    /// Gestiona atributos LLAVE-N°0/1/2, visibilidad de llave 1/2/3 efecto y PX en luminarias.
    /// </summary>
    public class EfectoCommand
    {
        [CommandMethod("ADE_EFECTO")]
        [CommandMethod("ADE_LLAVE")]
        public void GestionarEfectos()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n══ Asistente de Interruptores y Efectos (ADE_EFECTO) ══");

            var peo = new PromptEntityOptions("\nSeleccione interruptor (I.E-AD-01) o luminaria (I.E-AD-07): ");
            peo.SetRejectMessage("\nDebe seleccionar un bloque.");
            peo.AddAllowedClass(typeof(BlockReference), true);

            var res = ed.GetEntity(peo);
            if (res.Status != PromptStatus.OK) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(res.ObjectId, OpenMode.ForRead) as BlockReference;
                if (br == null) return;

                string bName = br.IsDynamicBlock
                    ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                    : ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;

                if (bName.IndexOf("I.E-AD-01", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    bName.IndexOf("INTERRUPTOR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    bName.IndexOf("LLAVE", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Es un interruptor
                    ConfigurarInterruptor(doc, tr, br);
                }
                else if (bName.IndexOf("I.E-AD-07", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         bName.IndexOf("LUM", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Es una luminaria
                    ConfigurarLuminariasDirecto(doc, tr, res.ObjectId);
                }
                else
                {
                    ed.WriteMessage($"\n[!] El bloque '{bName}' no corresponde a interruptor ni luminaria.");
                }

                tr.Commit();
            }
        }

        private static void ConfigurarInterruptor(Document doc, Transaction tr, BlockReference switchBr)
        {
            var ed = doc.Editor;

            // Leer atributos actuales
            var atts = new Dictionary<string, AttributeReference>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId attId in switchBr.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                atts[att.Tag] = att;
            }

            string actual0 = atts.TryGetValue("LLAVE-N°0", out var a0) ? a0.TextString?.Trim() ?? "" : "";
            string actual1 = atts.TryGetValue("LLAVE-N°1", out var a1) ? a1.TextString?.Trim() ?? "" : "";
            string actual2 = atts.TryGetValue("LLAVE-N°2", out var a2) ? a2.TextString?.Trim() ?? "" : "";

            var actuales = new List<string>();
            if (!string.IsNullOrEmpty(actual0)) actuales.Add(actual0);
            if (!string.IsNullOrEmpty(actual1)) actuales.Add(actual1);
            if (!string.IsNullOrEmpty(actual2)) actuales.Add(actual2);

            string defaultPrompt = actuales.Count > 0 ? string.Join(",", actuales) : "A";

            var pso = new PromptStringOptions($"\nLetras de efecto para esta llave [ej: A o A,B o A,B,C] <{defaultPrompt}>: ");
            pso.AllowSpaces = false;
            var res = ed.GetString(pso);
            if (res.Status != PromptStatus.OK) return;

            string input = res.StringResult?.Trim() ?? "";
            if (string.IsNullOrEmpty(input)) input = defaultPrompt;

            var efectos = input.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(s => s.Trim().ToUpperInvariant())
                               .ToList();

            if (efectos.Count == 0) return;

            // Actualizar atributos de la llave
            switchBr.UpgradeOpen();

            SetAttributeValue(tr, switchBr, "LLAVE-N°0", efectos.Count > 0 ? efectos[0] : "");
            SetAttributeValue(tr, switchBr, "LLAVE-N°1", efectos.Count > 1 ? efectos[1] : "");
            SetAttributeValue(tr, switchBr, "LLAVE-N°2", efectos.Count > 2 ? efectos[2] : "");

            // Configurar estado de visibilidad dinámica de la llave
            string targetVis = efectos.Count switch
            {
                1 => "llave 1 efecto",
                2 => "llave 2 efecto",
                _ => "llave 3 efecto"
            };

            SetDynamicProperty(switchBr, "Visibilidad1", targetVis);

            var userPrefs = AdElec.Core.Models.UserPreferences.Cargar();
            string nomSugerida = userPrefs.ObtenerNomenclaturaParaEfectos(efectos.Count);
            ed.WriteMessage($"\n✓ Interruptor configurado: {efectos.Count} efecto(s) [{string.Join(", ", efectos)}] con visibilidad '{targetVis}'.");
            ed.WriteMessage($"\n💡 Nomenclatura física de cable sugerida: {nomSugerida} (aprendido según sus preferencias).");

            // Opción: Vincular luminarias inmediatamente
            var pko = new PromptKeywordOptions("\n¿Desea seleccionar las luminarias para vincularlas? [Si/No] <S>: ");
            pko.Keywords.Add("Si");
            pko.Keywords.Add("No");
            pko.Keywords.Default = "Si";

            var kRes = ed.GetKeywords(pko);
            if (kRes.Status != PromptStatus.OK || kRes.StringResult == "No") return;

            if (efectos.Count == 1)
            {
                // Asignar directo a todas las seleccionadas
                ed.WriteMessage($"\nSeleccione luminarias a controlar con efecto '{efectos[0]}':");
                var psoLums = new PromptSelectionOptions { MessageForAdding = $"\nSeleccione luminarias (I.E-AD-07) para efecto '{efectos[0]}': " };
                var sel = ed.GetSelection(psoLums);
                if (sel.Status == PromptStatus.OK)
                {
                    int count = 0;
                    foreach (SelectedObject so in sel.Value)
                    {
                        var lumBr = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as BlockReference;
                        if (lumBr != null)
                        {
                            SetAttributeValue(tr, lumBr, "PX", efectos[0]);
                            count++;
                        }
                    }
                    ed.WriteMessage($"\n✓ Asignado efecto '{efectos[0]}' a {count} luminaria(s).");
                }
            }
            else
            {
                // Múltiples efectos: asignar por cada efecto
                foreach (string ef in efectos)
                {
                    ed.WriteMessage($"\nSeleccione luminarias para el efecto '{ef}' (o Enter para omitir):");
                    var sel = ed.GetSelection();
                    if (sel.Status == PromptStatus.OK)
                    {
                        int count = 0;
                        foreach (SelectedObject so in sel.Value)
                        {
                            var lumBr = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as BlockReference;
                            if (lumBr != null)
                            {
                                SetAttributeValue(tr, lumBr, "PX", ef);
                                count++;
                            }
                        }
                        ed.WriteMessage($"\n✓ Asignado efecto '{ef}' a {count} luminaria(s).");
                    }
                }
            }
        }

        private static void ConfigurarLuminariasDirecto(Document doc, Transaction tr, ObjectId firstLumId)
        {
            var ed = doc.Editor;

            var firstBr = tr.GetObject(firstLumId, OpenMode.ForRead) as BlockReference;
            string actualPx = GetAttributeValue(tr, firstBr!, "PX");

            var pso = new PromptStringOptions($"\nLetra de efecto para luminaria(s) [A, B, C...] <{(!string.IsNullOrEmpty(actualPx) ? actualPx : "A")}>: ");
            var res = ed.GetString(pso);
            if (res.Status != PromptStatus.OK) return;

            string ef = res.StringResult?.Trim().ToUpperInvariant() ?? "";
            if (string.IsNullOrEmpty(ef)) ef = !string.IsNullOrEmpty(actualPx) ? actualPx : "A";

            // Asignar al primero
            firstBr!.UpgradeOpen();
            SetAttributeValue(tr, firstBr, "PX", ef);

            // Permitir seleccionar más luminarias
            ed.WriteMessage($"\nSeleccione otras luminarias adicionales para efecto '{ef}' (o Enter si es solo esta):");
            var sel = ed.GetSelection();
            int count = 1;

            if (sel.Status == PromptStatus.OK)
            {
                foreach (SelectedObject so in sel.Value)
                {
                    if (so.ObjectId == firstLumId) continue;
                    var lumBr = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as BlockReference;
                    if (lumBr != null)
                    {
                        SetAttributeValue(tr, lumBr, "PX", ef);
                        count++;
                    }
                }
            }

            ed.WriteMessage($"\n✓ Asignado efecto '{ef}' a {count} luminaria(s).");
        }

        public static void SetAttributeValue(Transaction tr, BlockReference br, string tag, string value)
        {
            foreach (ObjectId attId in br.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForWrite);
                if (string.Equals(att.Tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    att.TextString = value;
                    return;
                }
            }
        }

        public static string GetAttributeValue(Transaction tr, BlockReference br, string tag)
        {
            foreach (ObjectId attId in br.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                if (string.Equals(att.Tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    return att.TextString?.Trim() ?? "";
                }
            }
            return "";
        }

        public static bool SetDynamicProperty(BlockReference br, string propName, object value)
        {
            if (!br.IsDynamicBlock) return false;

            foreach (DynamicBlockReferenceProperty prop in br.DynamicBlockReferencePropertyCollection)
            {
                if (string.Equals(prop.PropertyName, propName, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        prop.Value = value;
                        return true;
                    }
                    catch { return false; }
                }
            }
            return false;
        }
    }
}
