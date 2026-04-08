using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AdElec.AutoCAD.Managers;
using AdElec.AutoCAD.Repositories;
using AdElec.AutoCAD.Helpers;
using AdElec.Core.AeaMotor;
using AdElec.Core.AeaMotor.Dtos;
using AdElec.Core.Models;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AdElec.AutoCAD.Commands
{
    public class PropuestaCommand
    {
        // Bloques a insertar según tipo de punto
        private const string BLOCK_IUG  = "I.E-AD-07";   // Bocas de iluminación
        private const string BLOCK_TUG  = "I.E-AD-09";   // Tomas generales
        private const string BLOCK_TUE  = "I.E-AD-09.02"; // Tomas especiales
        private const string BLOCK_SW   = "I.E-AD-01";   // Llaves / interruptores

        private const string FILE_IUG  = "Bloques_CAD\\I.E-AD-07.dwg";
        private const string FILE_TUG  = "Bloques_CAD\\I.E-AD-09.dwg";
        private const string FILE_TUE  = "Bloques_CAD\\I.E-AD-09.02.dwg";
        private const string FILE_SW   = "Bloques_CAD\\I.E-AD-01.dwg";

        [CommandMethod("ADE_PROPUESTA")]
        public void GenerarPropuesta()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            var panelRepo   = new DwgPanelRepository();
            var ambienteRepo = new DwgAmbienteRepository();
            var motorClient  = new AeaMotorClient();

            // ── 1. Verificar motor ────────────────────────────────────────────
            bool motorOk = Task.Run(() => motorClient.EstaDisponibleAsync()).GetAwaiter().GetResult();
            if (!motorOk)
            {
                ed.WriteMessage("\n[AD-ELEC] AEA-MOTOR no está corriendo. Iniciá Iniciar_Proyecto.bat primero.");
                return;
            }

            // ── 2. Seleccionar UF ─────────────────────────────────────────────
            var ufsDisponibles = ambienteRepo.GetUFsDisponibles();
            if (ufsDisponibles.Count == 0)
            {
                ed.WriteMessage("\nNo hay recintos cargados. Usá ADE_AMBIENTES primero.");
                return;
            }

            string uf;
            if (ufsDisponibles.Count == 1)
            {
                uf = ufsDisponibles[0];
                ed.WriteMessage($"\nUsando UF: {uf}");
            }
            else
            {
                var pso = new PromptStringOptions($"\nUnidades disponibles: {string.Join(", ", ufsDisponibles)}\nIngresá la UF a procesar: ");
                pso.AllowSpaces = true;
                var res = ed.GetString(pso);
                if (res.Status != PromptStatus.OK) return;
                uf = res.StringResult.Trim();
            }

            // ── 3. Obtener tablero asociado a esa UF ──────────────────────────
            var panel = panelRepo.GetAllPanels()
                .FirstOrDefault(p => string.Equals(p.Location, uf, StringComparison.OrdinalIgnoreCase));
            string tableroName = panel?.Name ?? uf;
            double slaArea = panel?.SuperficieCubiertaM2 ?? 0;

            // ── 4. Determinar Ambientes (Web vs DWG) ─────────────────────────
            var projectRepo  = new DwgProjectRepository();
            int    projectId    = projectRepo.GetProjectId();
            string projectName  = projectRepo.GetProjectName();
            string dwgFileName  = Path.GetFileNameWithoutExtension(db.Filename);

            // Nombre del proyecto: usuario → nombre del DWG → fallback genérico
            string resolvedProjectName = !string.IsNullOrWhiteSpace(projectName)
                ? projectName
                : (!string.IsNullOrWhiteSpace(dwgFileName) ? dwgFileName : "Proyecto AD-ELEC");

            List<ProposalRoomInput> rooms = new();

            if (projectId > 0)
            {
                try
                {
                    ed.WriteMessage($"\nBuscando ambientes en AEA-MOTOR (Proyecto #{projectId})...");
                    var webProj = Task.Run(() => motorClient.GetProjectAsync(projectId)).GetAwaiter().GetResult();
                    if (webProj?.DataJson?.AdElec?.Rooms?.Count > 0)
                    {
                        rooms = webProj.DataJson.AdElec.Rooms.Select(sr => new ProposalRoomInput
                        {
                            Id = sr.Id,
                            Name = sr.Name,
                            Type = sr.Type,
                            Area = sr.Area,
                            PolygonPoints = sr.PolygonPoints,
                            Centroid = sr.Centroid
                        }).ToList();
                        ed.WriteMessage($"\n✓ {rooms.Count} ambientes cargados desde AD-CAD (Web).");
                    }
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[Aviso] No se pudo leer de la web ({ex.Message}). Usando datos del dibujo...");
                }
            }

            if (rooms.Count == 0)
            {
                ed.WriteMessage($"\nLeyendo recintos de '{uf}' desde AutoCAD...");
                rooms = LeerRoomsConPoligono(db, uf);
            }

            if (rooms.Count == 0)
            {
                ed.WriteMessage("\nNo se encontraron recintos. Asegurate de que existan bloques ID_LOCALES en AutoCAD o ambientes en AD-CAD.");
                return;
            }

            if (slaArea <= 0)
                slaArea = rooms.Sum(r => r.Area);

            ed.WriteMessage($"\nProcesando propuesta para {rooms.Count} ambiente(s). SLA total: {slaArea:F1} m²");

            // ── 5. Llamar a generate_proposal ────────────────────────────────
            ed.WriteMessage("\nGenerando propuesta técnica...");

            // Nivel de electrificación: derivado del grado AEA del tablero si ya fue calculado
            string electrificationLevel = panel?.LastGrado switch
            {
                "1" => "Mínimo",
                "2" => "Medio",
                "3" => "Elevado",
                "4" => "Superior",
                _   => "Mínimo",
            };

            var input = new ProposalProjectInput
            {
                ProjectName          = $"{resolvedProjectName} [{dwgFileName}] — {tableroName}",
                ElectrificationLevel = electrificationLevel,
                SlaArea              = slaArea,
                Rooms                = rooms,
                Board                = new ProposalBoardInput
                {
                    Id         = tableroName,
                    MainSwitch = $"TM {panel?.MainBreakerAmps ?? 32}A",
                    Rcd        = "ID 40A/30mA",
                },
            };

            ProposalResponse proposal;
            try
            {
                proposal = Task.Run(() => motorClient.GenerarPropuestaAsync(input)).GetAwaiter().GetResult();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error] generate_proposal: {ex.Message}");
                return;
            }

            if (proposal.RoomsUpdate.Count == 0)
            {
                ed.WriteMessage("\nAEA-MOTOR no devolvió puntos. Verificá los recintos.");
                return;
            }

            ed.WriteMessage($"\n✓ Propuesta recibida. {proposal.Message}");

            // ── 6. Cargar bloques necesarios ─────────────────────────────────
            EnsureBlocks(db);

            // ── 6b. Limpiar bocas previas de este tablero ─────────────────────
            // Evita duplicados al re-ejecutar ADE_PROPUESTA sobre el mismo tablero.
            // Se eliminan los bloques eléctricos cuyo atributo D coincida con tableroName.
            EliminarBocasPrevias(db, tableroName, ed);

            // ── 7. Insertar bocas en el dibujo ───────────────────────────────
            int iugCount = 0, tugCount = 0, tueCount = 0, swCount = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt    = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // Construir un mapa room_id → ProposalRoomUpdate
                var roomMap = proposal.RoomsUpdate.ToDictionary(r => r.Id, r => r);
                // Construir mapa room_id → RoomInput (para obtener coordenadas de los points)
                var inputMap = input.Rooms.ToDictionary(r => r.Id, r => r);

                foreach (var roomUpdate in proposal.RoomsUpdate)
                {
                    foreach (var pt in roomUpdate.Points)
                    {
                        var pos = new Point3d(pt.X, pt.Y, 0);

                        switch (pt.Type)
                        {
                            case "IUG":
                            case "IUE":
                                if (bt.Has(BLOCK_IUG))
                                {
                                    InsertarBoca(tr, space, bt[BLOCK_IUG], pos, pt.Id, new Dictionary<string, string>
                                    {
                                        ["CX"]  = pt.CircuitId ?? "",
                                        ["PX"]  = pt.SwitchId ?? "",
                                        ["POT"] = pt.PowerVa > 0 ? pt.PowerVa.ToString("F0") : "0",
                                        ["D"]   = tableroName,   // ← Designación = tablero
                                    });
                                    iugCount++;
                                }
                                break;

                            case "TUG":
                                if (bt.Has(BLOCK_TUG))
                                {
                                    InsertarBoca(tr, space, bt[BLOCK_TUG], pos, pt.Id, new Dictionary<string, string>
                                    {
                                        ["CX"]  = pt.CircuitId ?? "",
                                        ["XT"]  = "",
                                        ["POT"] = pt.PowerVa > 0 ? pt.PowerVa.ToString("F0") : "0",
                                        ["D"]   = tableroName,
                                    });
                                    tugCount++;
                                }
                                break;

                            case "TUE":
                                if (bt.Has(BLOCK_TUE))
                                {
                                    InsertarBoca(tr, space, bt[BLOCK_TUE], pos, pt.Id, new Dictionary<string, string>
                                    {
                                        ["CX"]    = pt.CircuitId ?? "",
                                        ["XT"]    = "",
                                        ["POT"]   = pt.PowerVa > 0 ? pt.PowerVa.ToString("F0") : "0",
                                        ["D"]     = tableroName,
                                        ["N°EQ"]  = pt.Label ?? "",
                                    });
                                    tueCount++;
                                }
                                break;

                            case "SWITCH":
                                if (bt.Has(BLOCK_SW))
                                {
                                    InsertarBoca(tr, space, bt[BLOCK_SW], pos, pt.Id, new Dictionary<string, string>
                                    {
                                        ["LLAVE-N°0"] = pt.SwitchId ?? "",
                                        ["LLAVE-N°1"] = "",
                                        ["LLAVE-N°2"] = "",
                                    });
                                    swCount++;
                                }
                                break;
                        }
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\n✓ Insertado: {iugCount} IUG, {tugCount} TUG, {tueCount} TUE, {swCount} llaves — Tablero: {tableroName}");

            // ── 8. Actualizar circuitos del panel en el XRecord ──────────────
            ActualizarCircuitosPanel(panel, proposal, panelRepo, tableroName);

            // ── 9. Sincronizar con el editor web de AEA-MOTOR ────────────────
            SincronizarConAdelec(motorClient, projectRepo, input, proposal, tableroName, ed);

            ed.WriteMessage("\n  Atributo D = tablero. Usá ADE_PANEL → 'Calcular AEA 90364' para obtener secciones y validaciones.");
        }

        /// <summary>
        /// Extrae los circuitos únicos de la respuesta del motor y los guarda
        /// en el XRecord del panel para que "Calcular AEA 90364" los encuentre.
        /// </summary>
        private static void ActualizarCircuitosPanel(
            AdElec.Core.Models.Panel? panel,
            ProposalResponse proposal,
            DwgPanelRepository panelRepo,
            string tableroName)
        {
            if (panel is null) return;

            // Agrupar puntos por circuit_id
            var circuitMap = new Dictionary<string, List<ProposalPoint>>(StringComparer.OrdinalIgnoreCase);
            foreach (var room in proposal.RoomsUpdate)
                foreach (var pt in room.Points.Where(p => !string.IsNullOrEmpty(p.CircuitId)))
                {
                    if (!circuitMap.ContainsKey(pt.CircuitId!))
                        circuitMap[pt.CircuitId!] = new List<ProposalPoint>();
                    circuitMap[pt.CircuitId!].Add(pt);
                }

            var circuits = circuitMap
                .OrderBy(kvp => kvp.Key)
                .Select(kvp =>
                {
                    bool esLuz = kvp.Value.Any(p => p.Type == "IUG" || p.Type == "IUE");
                    string tipo = esLuz ? "IUG" : "TUG";
                    return new AdElec.Core.Models.Circuit
                    {
                        Name       = kvp.Key,
                        Type       = tipo,
                        BreakerAmps    = esLuz ? 10 : 16,
                        WireSectionMm2 = esLuz ? 1.5 : 2.5,
                    };
                })
                .ToList();

            panel.Circuits = new System.Collections.ObjectModel.ObservableCollection<AdElec.Core.Models.Circuit>(circuits);
            panelRepo.SavePanel(panel);
        }

        /// <summary>
        /// Crea (o actualiza) el proyecto en el editor web de AEA-MOTOR
        /// para poder visualizarlo en el canvas adelec del frontend.
        /// Fire-and-forget: si falla, solo se loggea sin abortar el comando.
        /// </summary>
        private static void SincronizarConAdelec(
            AeaMotorClient motorClient,
            DwgProjectRepository projectRepo,
            ProposalProjectInput input,
            ProposalResponse proposal,
            string tableroName,
            Editor ed)
        {
            try
            {
                // ── Rooms para el canvas adelec ──────────────────────────────
                var ptMap = proposal.RoomsUpdate.ToDictionary(r => r.Id, r => r.Points);

                var syncRooms = input.Rooms.Select(r =>
                {
                    ptMap.TryGetValue(r.Id, out var pts);
                    return new SyncRoom
                    {
                        Id            = r.Id,
                        Name          = r.Name,
                        Type          = r.Type,
                        Area          = r.Area,
                        PolygonPoints = r.PolygonPoints,
                        Centroid      = r.Centroid,
                        Points        = pts ?? [],
                    };
                }).ToList();

                // ── recintosMeta para AD-CAD (tipo por centroide) ────────────
                // AD-CAD matchea por coord (punto dentro del polígono) cuando no tiene faceKey
                var recintosMeta = input.Rooms.Select(r => new SyncRecintoMeta
                {
                    FaceKey = "",    // vacío → AD-CAD usa coord para matchear
                    Nombre  = r.Name,
                    Tipo    = r.Type, // "sala_estar", "dormitorio", etc.
                    Coord   = r.Centroid,
                }).ToList();

                // ── Circuitos ────────────────────────────────────────────────
                var circuitIds = proposal.RoomsUpdate
                    .SelectMany(r => r.Points)
                    .Where(p => !string.IsNullOrEmpty(p.CircuitId))
                    .Select(p => p.CircuitId!)
                    .Distinct().OrderBy(c => c);

                var syncCircuits = circuitIds.Select(cid =>
                {
                    bool esLuz = proposal.RoomsUpdate.SelectMany(r => r.Points)
                        .Any(p => p.CircuitId == cid && (p.Type == "IUG" || p.Type == "IUE"));
                    return new SyncCircuit
                    {
                        Id         = cid,
                        Name       = esLuz ? $"Iluminación {cid}" : $"Tomacorrientes {cid}",
                        Amperage   = esLuz ? 10 : 16,
                        Protection = esLuz ? "TM 10A" : "TM 16A",
                    };
                }).ToList();

                var syncData = new SyncProjectData
                {
                    AdElec = new SyncProjectCanvas
                    {
                        Rooms = syncRooms,
                        Board = new SyncBoard
                        {
                            Id         = tableroName,
                            MainSwitch = input.Board.MainSwitch,
                            Rcd        = input.Board.Rcd,
                            Circuits   = syncCircuits,
                        },
                    },
                    AdCad = new SyncAdCadData
                    {
                        Plantas =
                        [
                            new SyncPlanta
                            {
                                Id            = "PL1",
                                Nombre        = "Planta Baja",
                                RecintosMeta  = recintosMeta,
                            }
                        ]
                    },
                };

                var syncReq = new SyncProjectRequest
                {
                    Name     = input.ProjectName,
                    DataJson = syncData,
                };

                // ── POST si es nuevo, PUT si el DWG ya tiene un ID vinculado ──
                int existingId = projectRepo.GetProjectId();
                SyncProjectResponse syncResult;

                if (existingId > 0)
                {
                    syncResult = Task.Run(() => motorClient.ActualizarProyectoAsync(existingId, syncReq))
                                     .GetAwaiter().GetResult();
                    ed.WriteMessage($"\n  Proyecto #{existingId} actualizado en AEA-MOTOR.");
                }
                else
                {
                    syncResult = Task.Run(() => motorClient.SincronizarProyectoAsync(syncReq))
                                     .GetAwaiter().GetResult();
                    // Persistir el ID nuevo en el DWG para que futuras propuestas usen PUT
                    projectRepo.SaveProjectId(syncResult.Id);
                    ed.WriteMessage($"\n  Proyecto creado en AEA-MOTOR (ID #{syncResult.Id}) — vinculado a este DWG.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n  [Aviso] No se pudo sincronizar con el editor web: {ex.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Escanea el dibujo buscando todos los bloques ID_LOCALES de la UF indicada,
        /// y para cada uno encuentra la polilínea cerrada que lo contiene.
        /// Devuelve la lista de RoomInput lista para enviar a generate_proposal.
        /// </summary>
        private static List<ProposalRoomInput> LeerRoomsConPoligono(Database db, string uf)
        {
            var result = new List<ProposalRoomInput>();

            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

            // Recopilar todas las polilíneas cerradas del espacio
            var polylines = new List<Polyline>();
            foreach (ObjectId entId in space)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead);
                if (ent is Polyline poly && poly.Closed && poly.NumberOfVertices >= 3)
                    polylines.Add(poly);
            }

            // Iterar bloques ID_LOCALES
            foreach (ObjectId entId in space)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead);
                if (ent is not BlockReference br) continue;

                string blockName = br.IsDynamicBlock
                    ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                    : ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;

                if (!string.Equals(blockName, "ID_LOCALES", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Leer atributos
                var atts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    var a = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                    atts[a.Tag] = a.TextString?.Trim() ?? "";
                }

                if (!string.Equals(atts.GetValueOrDefault("01", ""), uf, StringComparison.OrdinalIgnoreCase))
                    continue;

                var insertPt = new Point2d(br.Position.X, br.Position.Y);

                // Buscar polilínea contenedora
                Polyline? container = polylines.FirstOrDefault(p => IsPointInPolyline(p, insertPt));
                if (container is null) continue;

                // Extraer vértices y calcular centroide
                var polyPts = new List<Dictionary<string, double>>();
                double cx = 0, cy = 0;
                int n = container.NumberOfVertices;
                for (int i = 0; i < n; i++)
                {
                    var v = container.GetPoint2dAt(i);
                    polyPts.Add(new Dictionary<string, double> { ["x"] = v.X, ["y"] = v.Y });
                    cx += v.X; cy += v.Y;
                }
                cx /= n; cy /= n;

                // Parsear área del atributo AREA
                double area = ParseArea(atts.GetValueOrDefault("AREA", ""));
                if (area <= 0) area = container.Area;

                string tipoDisplay = atts.GetValueOrDefault("LOCAL", "Otro");
                string roomTypeName = TipoAmbienteInfo.DesdeNombre(tipoDisplay).ApiValue;
                string planta = atts.GetValueOrDefault("55", "PB");

                // ID persistente basado en el handle del bloque ID_LOCALES.
                // Garantiza estabilidad entre llamadas sucesivas (a diferencia de un índice secuencial).
                string roomId = $"room_{br.Handle}";

                result.Add(new ProposalRoomInput
                {
                    Id    = roomId,
                    Name  = $"{tipoDisplay} — {planta}",
                    Type  = roomTypeName,
                    Area  = area,
                    PolygonPoints = polyPts,
                    Centroid = new Dictionary<string, double> { ["x"] = cx, ["y"] = cy },
                });
            }

            tr.Commit();
            return result;
        }

        /// <summary>
        /// Elimina del espacio actual todos los bloques eléctricos (IUG, TUG, TUE, SWITCH)
        /// cuyo atributo "D" coincida con el tablero indicado.
        /// También acepta bloques que tengan un WebId asignado (inserción previa de ADE_PROPUESTA).
        /// </summary>
        private static void EliminarBocasPrevias(Database db, string tableroName, Editor ed)
        {
            var electricoBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { BLOCK_IUG, BLOCK_TUG, BLOCK_TUE, BLOCK_SW };

            var toDelete = new List<ObjectId>();

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                foreach (ObjectId entId in space)
                {
                    var ent = tr.GetObject(entId, OpenMode.ForRead);
                    if (ent is not BlockReference br) continue;

                    string blockName = br.IsDynamicBlock
                        ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                        : ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;

                    if (!electricoBlocks.Contains(blockName)) continue;

                    // Verificar si el atributo D coincide con el tablero
                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                        if (string.Equals(att.Tag, "D", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(att.TextString?.Trim(), tableroName, StringComparison.OrdinalIgnoreCase))
                        {
                            toDelete.Add(entId);
                            break;
                        }
                    }
                }
                tr.Commit();
            }

            if (toDelete.Count == 0) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in toDelete)
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite);
                    ent.Erase();
                }
                tr.Commit();
            }

            ed.WriteMessage($"\n  Eliminadas {toDelete.Count} bocas previas del tablero '{tableroName}'.");
        }

        /// <summary>Carga los bloques necesarios en el DWG si no existen aún.</summary>
        private static void EnsureBlocks(Database db)
        {
            BlockManager.EnsureBlockExists(BLOCK_IUG, ResolveBlockPath(db, FILE_IUG));
            BlockManager.EnsureBlockExists(BLOCK_TUG, ResolveBlockPath(db, FILE_TUG));
            BlockManager.EnsureBlockExists(BLOCK_TUE, ResolveBlockPath(db, FILE_TUE));
            BlockManager.EnsureBlockExists(BLOCK_SW,  ResolveBlockPath(db, FILE_SW));
        }

        /// <summary>
        /// Inserta un BlockReference con sus atributos completados desde el diccionario tag→valor.
        /// </summary>
        private static void InsertarBoca(
            Transaction tr,
            BlockTableRecord space,
            ObjectId blockId,
            Point3d position,
            string webId,
            Dictionary<string, string> valores)
        {
            var br = new BlockReference(position, blockId);
            space.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);

            // Guardar ID persistente para sincronización bidireccional
            if (!string.IsNullOrEmpty(webId))
            {
                XDataHelper.SetWebId(br, webId, tr);
            }

            var blockDef = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
            foreach (ObjectId eid in blockDef)
            {
                var ent = tr.GetObject(eid, OpenMode.ForRead);
                if (ent is not AttributeDefinition attDef || attDef.Constant) continue;

                var attRef = new AttributeReference();
                attRef.SetAttributeFromBlock(attDef, br.BlockTransform);
                attRef.Position = attDef.Position.TransformBy(br.BlockTransform);
                attRef.TextString = valores.TryGetValue(attDef.Tag, out string? val) ? val : attDef.TextString;

                br.AttributeCollection.AppendAttribute(attRef);
                tr.AddNewlyCreatedDBObject(attRef, true);
            }
        }

        /// <summary>Ray casting para saber si un punto está dentro de una polilínea cerrada.</summary>
        private static bool IsPointInPolyline(Polyline poly, Point2d pt)
        {
            int n = poly.NumberOfVertices;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var pi = poly.GetPoint2dAt(i);
                var pj = poly.GetPoint2dAt(j);
                if ((pi.Y > pt.Y) != (pj.Y > pt.Y) &&
                    pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y) + pi.X)
                    inside = !inside;
            }
            return inside;
        }

        private static double ParseArea(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var clean = System.Text.RegularExpressions.Regex.Replace(text, @"[^\d.,]", "").Replace(',', '.');
            return double.TryParse(clean, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
        }

        private static string ResolveBlockPath(Database db, string rel)
        {
            string dwgDir = Path.GetDirectoryName(db.Filename) ?? "";
            if (!string.IsNullOrEmpty(dwgDir))
            {
                string c = Path.Combine(dwgDir, rel);
                if (File.Exists(c)) return c;
            }
            string asmDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            string dir = asmDir;
            for (int i = 0; i < 6; i++)
            {
                string c = Path.Combine(dir, rel);
                if (File.Exists(c)) return c;
                string? parent = Path.GetDirectoryName(dir);
                if (parent is null || parent == dir) break;
                dir = parent;
            }
            return Path.Combine(asmDir, rel);
        }
    }
}
