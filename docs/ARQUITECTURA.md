# Arquitectura del Sistema AD-ELEC

> Documentación técnica del plugin AutoCAD para diseño eléctrico multi-norma según reglamentaciones **AEA 90364-7-770 (Edición 2017)**, **AEA 90364-7-771 (Edición 2006)** y **AEA 90364-7-772 (Edición 2019)**.

---

## 1. Visión General

AD-ELEC es una **aplicación nativa de alto rendimiento** que integra la interfaz CAD/WPF en .NET 8 con un núcleo normativo compilado en **Rust** (`aea_core.dll`) ejecutado en memoria (*in-process*) mediante `P/Invoke` (`AeaBridge`):

| Capa | Tecnología | Ubicación |
|------|-----------|-----------|
| Plugin AutoCAD & UI | C# / .NET 8 / WPF + AutoLISP | `G:\PROYECTOS AD\AD-ELEC\src` y `lisp/` |
| Núcleo Normativo Nativo | Rust (`aea_core.dll` / WASM) | `G:\PROYECTOS AD\AD-ELEC\aea\core` |
| Catálogo Verificado | JSON embebido + Verificador PyMuPDF | `G:\PROYECTOS AD\AD-ELEC\aea\catalogo` (23/23 tablas contrastadas contra PDFs oficiales) |

La comunicación entre C# y el motor Rust ocurre directamente en memoria a través de C-ABI (`AeaBridge.cs`) con latencia de microsegundos y sin depender de procesos ni servidores externos.

```
┌──────────────────────────────────────────────────────────┐
│                      AutoCAD 2025                        │
│                                                          │
│  ┌────────────────┐        ┌───────────────────────────┐ │
│  │ Rutinas LISP   │        │   Sidebar WPF             │ │
│  │ (WE, WN, etc.) │        │   (ADE_PANEL / Tableros)  │ │
│  └───────┬────────┘        └─────────────┬─────────────┘ │
│          │                               │               │
│          └───────────────┬───────────────┘               │
│                          ▼                               │
│          ┌───────────────────────────────┐               │
│          │   AdElec.Core (AeaBridge.cs)  │               │
│          └───────────────┬───────────────┘               │
│                          │ P/Invoke C-ABI (In-Process)   │
│                          ▼                               │
│          ┌───────────────────────────────┐               │
│          │     aea_core.dll (Rust)       │               │
│          │  ┌─────────┬─────────┬──────┐ │               │
│          │  │ AEA 770 │ AEA 771 │ 772  │ │               │
│          │  └─────────┴─────────┴──────┘ │               │
│          │  Catálogo Embebido (23 JSON)  │               │
│          └───────────────────────────────┘               │
└──────────────────────────────────────────────────────────┘
```

---

## 2. Estructura de Directorios

### 2.1 Repositorio Principal — `G:\PROYECTOS AD\AD-ELEC`

```
G:\PROYECTOS AD\AD-ELEC\
├── AdElec.sln                    # Solución Visual Studio
├── aea/                          # Subsistema Normativo AEA
│   ├── catalogo/                 # 23 tablas JSON verificadas contra PDFs oficiales
│   │   ├── aea-770-2017/         # Viviendas unifamiliares hasta 63 A (15 tablas)
│   │   ├── aea-771-2006/         # Locales comerciales, oficinas y terciario (5 tablas)
│   │   └── aea-772-2019/         # Servicios generales y partes comunes (3 tablas)
│   ├── core/                     # Motor en Rust (compila a aea_core.dll y WASM)
│   │   └── src/                  # aea770.rs, aea771.rs, aea772.rs, catalogo.rs, ffi.rs
│   └── herramientas/
│       └── verificar_catalogo.py # Verificador criptográfico/textual contra AEA_NORMATIVA/*.pdf
│
├── src/
│   ├── AdElec.Core/              # Lógica de negocio (sin dependencias CAD)
│   │   ├── Models/               # Panel, Circuit, Ambiente, Point2D
│   │   ├── Interfaces/           # IPanelRepository, IAmbienteRepository
│   │   ├── Aea/                  # AeaBridge.cs (P/Invoke Rust) + PanelAeaExtensions.cs
│   │   ├── AeaMotor/             # Cliente HTTP legado + DTOs
│   │   ├── Algorithms/           # GridCalculator, AeaRules
│   │   └── Utils/                # GeometryConverter
│   │
│   ├── AdElec.AutoCAD/           # Capa de integración con AutoCAD
│   │   ├── PluginEntry.cs        # Punto de entrada del plugin
│   │   ├── Commands/             # Comandos AutoCAD (ADE_PANEL, ADE_CALCULAR, etc.)
│   │   ├── Repositories/         # Persistencia en DWG
│   │   ├── Managers/             # BlockManager
│   │   ├── Geometry/             # PolylineExtensions, RoomDetector
│   │   └── Helpers/              # XDataHelper
│   │
│   └── AdElec.UI/                # Interfaz WPF
│       ├── Views/                # MainPaletteView, AmbienteDialog
│       └── ViewModels/           # PanelViewModel, AmbienteDialogViewModel
│
├── lisp/                         # Rutinas AutoLISP propias del plugin
│   ├── ade_utils.lsp             # Funciones utilitarias compartidas
│   ├── ade_luminarias.lsp        # Comando ADE_LUMINARIAS
│   ├── ade_tomas.lsp             # Comando ADE_TOMAS
│   ├── ade_canerias.lsp          # Comando ADE_CANERIAS
│   └── vendor/                   # Utilidades y librerías LISP de terceros
│
├── blocks/                       # Bloques dinámicos DWG del sistema
│   └── inspect/                  # Utilidades de inspección de bloques
│
├── bitacora/                     # Registro de avances y decisiones técnicas
│   └── bitacora.md
│
├── docs/                         # Documentación técnica centralizada
│   ├── fases/                    # Especificaciones por fase
│   └── specs/                    # Especificaciones de algoritmos
│
└── deploy/
    ├── build.bat                 # Script de compilación automática
    ├── acad.lsp                  # Script de inicio / autocarga en AutoCAD
    ├── AdElec.AutoCAD.dll
    ├── AdElec.Core.dll
    └── AdElec.UI.dll
```

### 2.2 Motor de Cálculo — `G:\PROYECTOS AD\AEA 90364-B\AEA-MOTOR-RESTORED`

```
AEA-MOTOR-RESTORED\
├── backend/
│   ├── api/
│   │   ├── app.py                # Setup FastAPI, CORS, routers
│   │   ├── routes.py             # /calcular (otros: /grado, /validar — NO usados por AD-ELEC)
│   │   ├── adelec_routes.py      # /adelec/generate_proposal ← usado por AD-ELEC
│   │   │                         # /adelec/analyze, /adelec/export ← NO usados por AD-ELEC
│   │   ├── db_routes.py          # CRUD proyectos ← usado por AD-ELEC
│   │   ├── auth_routes.py        # JWT login/register ← NO usado por AD-ELEC (web UI)
│   │   ├── database.py           # SQLAlchemy engine + session
│   │   ├── db_models.py          # ORM: User, Project, Building, BuildingUnit
│   │   ├── schemas.py            # Pydantic I/O schemas
│   │   └── crud.py               # Operaciones de base de datos
│   │
│   └── adelec/                   # Usado por /generate_proposal ← sí lo usa AD-ELEC
│       ├── models/               # architecture.py, electrical.py
│       ├── core/
│       │   ├── parser/           # dxf_parser.py — DXF → Project (solo para /analyze)
│       │   ├── rules/            # Reglas mínimas por ambiente
│       │   ├── placer/           # Colocación de luminarias, tomas, llaves ← usado
│       │   ├── circuits/         # Agrupación en circuitos ← usado
│       │   ├── router/           # Trazado de cañerías ← usado
│       │   └── caratula/         # Generación de carátula (independiente)
│       └── web_models.py         # Pydantic schemas para /adelec
│
└── aea_motor/                    # Paquete principal de cálculo ← usado por AD-ELEC
    ├── models/                   # proyecto.py, resultados.py, factibilidad.py
    ├── engine/                   # Módulos de cálculo puro
    │   ├── grado.py              # Grado de electrificación (4 grados)
    │   ├── puntos_minimos.py     # Bocas mínimas por tipo de ambiente
    │   ├── demanda.py            # Demanda por circuito
    │   ├── conductores.py        # Sección de conductores (tabla 771.11)
    │   ├── caida_tension.py      # Verificación caída de tensión
    │   ├── montante.py           # Dimensionamiento línea principal
    │   ├── cortocircuito.py      # Verificación cortocircuito
    │   └── selectividad.py       # Coordinación de protecciones
    ├── norma/                    # NormStrategy, NormRegistry
    ├── rules/                    # aea_2006_rules.py
    └── calcular.py               # Orquestador principal
```

---

## 3. Módulos del Plugin AutoCAD (C#)

### 3.1 AdElec.Core — Lógica de Negocio

#### Modelos de Dominio

**[Panel.cs](src/AdElec.Core/Models/Panel.cs)** — Tablero eléctrico principal
```csharp
string Name             // Identificador (ej: "TD1")
string Location         // Unidad Funcional (UF)
double MainBreakerAmps  // Corriente nominal del interruptor principal
int PhaseCount          // 1 (monofásico) o 3 (trifásico)
double SuperficieCubiertaM2   // Superficie para cálculo de grado AEA
string? LastGrado        // Último grado calculado
ObservableCollection<Circuit> Circuits  // Circuitos terminales
```

**[Circuit.cs](src/AdElec.Core/Models/Circuit.cs)** — Circuito terminal
```csharp
string Name             // Identificador (ej: "C1")
string Type             // TUG, TUE, IUG, IUE, APM, etc.
double BreakerAmps      // Corriente del dispositivo de protección
double WireSectionMm2   // Sección del conductor
double LoadVA           // Demanda calculada
char Phase              // R, S o T (en trifásico)
```

**[Ambiente.cs](src/AdElec.Core/Models/Ambiente.cs)** — Ambiente/local del plano
```csharp
string UF               // Unidad Funcional
string Planta           // Piso (ej: "PB")
string TipoDisplay      // Nombre legible (ej: "Dormitorio")
string TipoApi          // Enum para la API (ej: "dormitorio")
double AreaM2           // Superficie calculada
double EspesorMuro      // Espesor de muro (defecto 0.15m)
List<Point2D> PolygonPoints  // Vértices del contorno
string Handle           // Handle del bloque en DWG (ID estable)
```

#### Cliente HTTP

**[AeaMotorClient.cs](src/AdElec.Core/AeaMotor/AeaMotorClient.cs)** — Comunicación con backend

| Método | Endpoint | Propósito |
|--------|----------|-----------|
| `CalcularAsync` | POST `/api/v1/calcular` | Cálculo completo de proyecto |
| `GetGradoAsync` | POST `/api/v1/grado` | Solo grado de electrificación |
| `ValidarAsync` | POST `/api/v1/validar` | Solo verificación de norma |
| `FactibilidadAsync` | POST `/api/v1/factibilidad` | Demanda edificio multi-unidad |
| `GenerarPropuestaAsync` | POST `/api/v1/adelec/generate_proposal` | Propuesta de instalación |
| `SincronizarProyectoAsync` | POST/PUT `/api/v1/proyectos` | Guardar proyecto en DB |
| `GetProjectAsync` | GET `/api/v1/proyectos/{id}` | Recuperar proyecto de DB |

---

### 3.2 AdElec.AutoCAD — Integración CAD

#### Punto de Entrada

**[PluginEntry.cs](src/AdElec.AutoCAD/PluginEntry.cs)** — `IExtensionApplication`
- Se ejecuta cuando AutoCAD carga el DLL via `NETLOAD`
- Registra todos los comandos `[CommandMethod]`
- Carga automáticamente los archivos LISP desde la carpeta `LISP/`

#### Comandos AutoCAD

| Comando | Archivo | Descripción |
|---------|---------|-------------|
| `ADE_PANEL` | [PaletteCommand.cs](src/AdElec.AutoCAD/Commands/PaletteCommand.cs) | Abre el sidebar WPF de gestión de tableros/circuitos |
| `ADE_AMBIENTES` | [AmbientesCommand.cs](src/AdElec.AutoCAD/Commands/AmbientesCommand.cs) | Crear/editar ambientes y calcular áreas |
| `ADE_INSPECT` | [BlockInspectorCommand.cs](src/AdElec.AutoCAD/Commands/BlockInspectorCommand.cs) | Inspeccionar atributos de bloques, exportar JSON |
| `ADE_INSERTAR_TABLERO` | [InsertarTableroCommand.cs](src/AdElec.AutoCAD/Commands/InsertarTableroCommand.cs) | Insertar bloque de tablero con atributos |
| `ADE_PROPUESTA` | [PropuestaCommand.cs](src/AdElec.AutoCAD/Commands/PropuestaCommand.cs) | Generar propuesta eléctrica completa |
| `ADE_RECARGAR` | [RecargarCommand.cs](src/AdElec.AutoCAD/Commands/RecargarCommand.cs) | Recargar circuitos desde datos del tablero |
| `ADE_PULL` | [SyncReceiverCommand.cs](src/AdElec.AutoCAD/Commands/SyncReceiverCommand.cs) | Sincronizar bloques desde la base de datos web |

#### Repositorios (Persistencia en DWG)

| Clase | Archivo | Datos Guardados |
|-------|---------|----------------|
| `DwgPanelRepository` | [Repositories/](src/AdElec.AutoCAD/Repositories/) | Tablero/circuitos en diccionario DWG (Xrecord) |
| `DwgAmbienteRepository` | — | Ambientes desde bloques del plano |
| `DwgProjectRepository` | — | ID/nombre del proyecto en DWG |
| `DwgElectricoRepository` | — | Puntos eléctricos y circuitos |

---

### 3.3 AdElec.UI — Interfaz WPF

**[MainPaletteView.xaml](src/AdElec.UI/Views/MainPaletteView.xaml)** — Sidebar principal
- Lista de tableros del proyecto
- Lista de circuitos por tablero
- Indicadores de grado y cumplimiento de norma
- Botones: Calcular, Sincronizar, Nueva Propuesta

**[PanelViewModel.cs](src/AdElec.UI/ViewModels/PanelViewModel.cs)** — Lógica MVVM
- Colecciones observables de tableros y circuitos
- Comandos para botones de la UI
- Llamadas HTTP a AEA-MOTOR
- Estado de la UI (ocupado, motor disponible, etc.)

---

## 4. Módulos LISP

### [ade_utils.lsp](LISP/ade_utils.lsp) — Utilidades Compartidas

Funciones disponibles para todos los módulos LISP:

| Función | Descripción |
|---------|-------------|
| `ade:ensure-layer` | Crear capa si no existe (con color) |
| `ade:get-att` | Leer atributo de un bloque por tag |
| `ade:insert-block` | Insertar bloque con lista de atributos |
| `ade:point-in-polygon` | Algoritmo ray-casting |
| `ade:poly-vertices` | Extraer vértices de LWPOLYLINE |
| `ade:poly-bbox` | Obtener bounding box de una polilínea |

---

### [ade_luminarias.lsp](LISP/ade_luminarias.lsp) — Comando `ADE_LUMINARIAS`

Colocación interactiva de luminarias en grilla:

**Estado global:**
- `*ADE:LUM:CX*` — Código de circuito
- `*ADE:LUM:TABLERO*` — Tablero asignado
- `*ADE:LUM:SP-INICIAL*` — Espaciado inicial
- `*ADE:LUM:FIXED-MODE*` — Modo fijo/automático

**Modos de operación:**
- **Auto:** espaciado automático según área del ambiente
- **Fijo:** espaciado manual definido por el usuario

**Características:**
- Visualización en tiempo real de la grilla (grdraw)
- Extracción de geometría del bloque (explode → LINE/CIRCLE/ARC)
- Input tipo joystick con modificadores de teclado (`+`/`-` para espaciado)
- Persistencia de últimos parámetros usados

---

### [ade_tomas.lsp](LISP/ade_tomas.lsp) — Comando `ADE_TOMAS`

Colocación interactiva de tomacorrientes con snap:

**Estado global:**
- `*ADE:TOMAS:CX*` — Código de circuito
- `*ADE:TOMAS:TABLERO*` — Tablero asignado
- `*ADE:TOMAS:OFFSET*` — Distancia al muro
- `*ADE:TOMAS:ROT*` — Rotación actual

**Modos de snap (GrSnapV1-0.lsp — Lee Mac):**
- Endpoint (1) + Midpoint (2) + Center (4) + Intersection (32) + Perpendicular (128) + Nearest (512)

**Controles de teclado:**
| Tecla | Acción |
|-------|--------|
| `A` / `D` | Rotar ±90° |
| `+` / `-` | Ajustar offset |
| `<` / `>` | Rotar ±45° |
| `O` / `R` | Ingresar valores exactos |

**Características:**
- Rotación perpendicular al muro (alejada del centroide del ambiente)
- Fix de rotación de atributos (siempre 0° para legibilidad)

---

### [ade_canerias.lsp](LISP/ade_canerias.lsp) — Comando `ADE_CANERIAS`

Trazado de cañerías/conductores:

**Modos:**
- **Luminarias:** arcos a 45° en esquinas
- **Tomas:** polilíneas a 45° en esquinas

**Submodos:**
- **Manual:** interactivo punto a punto
- **Auto:** trazado automático por código de circuito

**Características:**
- Handler de errores con rollback (undo mark)
- Patrón `ade:endundo` seguro

---

## 5. Motor de Cálculo AEA-MOTOR (FastAPI)

### 5.1 API Endpoints

#### Cálculo (`/api/v1/`)

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/calcular` | Cálculo completo de proyecto eléctrico |
| POST | `/grado` | Solo grado de electrificación |
| POST | `/validar` | Solo verificación de cumplimiento de norma |
| POST | `/factibilidad` | Demanda global de edificio multi-unidad |
| GET | `/norma/tablas` | Tablas AEA (conductores, demandas, etc.) |

#### Diseño Eléctrico (`/api/v1/adelec/`)

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/analyze` | Subir DXF → parsear → analizar → proponer |
| POST | `/export` | Generar DXF con propuesta |
| POST | `/generate_proposal` | Propuesta desde input del plugin CAD |

#### Gestión de Proyectos (`/api/v1/`)

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/proyectos` | Crear/sincronizar proyecto |
| GET | `/proyectos/{id}` | Obtener proyecto |
| PUT | `/proyectos/{id}` | Actualizar proyecto |
| DELETE | `/proyectos/{id}` | Eliminar proyecto |

#### Autenticación (`/api/v1/`)

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/register` | Crear usuario |
| POST | `/login` | Obtener token JWT |
| GET | `/me` | Información del usuario actual |

---

### 5.2 Motor de Cálculo AEA 90364 (`aea_motor/`)

**Orquestador principal: [calcular.py](../AEA-MOTOR-RESTORED/aea_motor/calcular.py)**

```
calcular_proyecto(proyecto: Proyecto) → ResultadoProyecto
  1. Cargar estrategia de norma (AEA 2006)
  2. Calcular grado de electrificación
  3. Calcular bocas mínimas por ambiente
  4. Por cada circuito:
     - Calcular demanda (VA)
     - Determinar sección del conductor
     - Verificar capacidad de corriente
     - Verificar caída de tensión (≤3% terminal, ≤5% total)
  5. Calcular montante (línea principal)
  6. Verificar protección contra cortocircuito
  7. Verificar selectividad (coordinación de protecciones)
  8. Evaluar reglas de cumplimiento
  9. Retornar ResultadoProyecto
```

**Módulos del motor:**

| Archivo | Descripción |
|---------|-------------|
| [grado.py](../AEA-MOTOR-RESTORED/aea_motor/engine/grado.py) | Grado de electrificación (4 grados por superficie) |
| [puntos_minimos.py](../AEA-MOTOR-RESTORED/aea_motor/engine/puntos_minimos.py) | Bocas mínimas por tipo de ambiente (tabla 771.8.II) |
| [demanda.py](../AEA-MOTOR-RESTORED/aea_motor/engine/demanda.py) | Demanda por circuito según tipo de ambiente |
| [conductores.py](../AEA-MOTOR-RESTORED/aea_motor/engine/conductores.py) | Sección de conductores (tabla 771.11) |
| [caida_tension.py](../AEA-MOTOR-RESTORED/aea_motor/engine/caida_tension.py) | Verificación de caída de tensión |
| [montante.py](../AEA-MOTOR-RESTORED/aea_motor/engine/montante.py) | Dimensionamiento de la línea principal |
| [cortocircuito.py](../AEA-MOTOR-RESTORED/aea_motor/engine/cortocircuito.py) | Verificación de corriente de cortocircuito |
| [selectividad.py](../AEA-MOTOR-RESTORED/aea_motor/engine/selectividad.py) | Coordinación de protecciones |
| [nomenclatura.py](../AEA-MOTOR-RESTORED/aea_motor/engine/nomenclatura.py) | Nomenclatura de conductores (ej: "3×2.5+1.5T PVC") |

---

### 5.3 Modelos de Base de Datos (SQLAlchemy)

**Archivo: [db_models.py](../AEA-MOTOR-RESTORED/backend/api/db_models.py)**

```
User
├── id, username, email, hashed_password, created_at
└── projects → [Project]

Project
├── id, user_id (FK), name
├── data_json    → Proyecto serializado (input de cálculo)
├── result_json  → ResultadoProyecto serializado (output)
└── created_at, updated_at

Building (edificio multi-unidad)
├── id, user_id (FK), name, total_floors, floor_height_m
├── result_json  → Resultados globales del edificio
└── units → [BuildingUnit]

BuildingUnit
├── id, building_id (FK), project_id (FK)
├── quantity      → Cantidad de unidades de este tipo
├── floor_range   → "PB", "1-4", etc.
└── name_override → Nombre opcional
```

**Base de datos:** SQLite (`aea_motor.db`), configurable via `DATABASE_URL`

---

## 6. Flujos de Datos

### 6.1 Cálculo de Proyecto Eléctrico

```
Usuario hace clic en "Calcular" en ADE_PANEL
  ↓
PanelViewModel.CalcularCommand
  ↓
Construir ProyectoDto (tablero + ambientes del DWG)
  ↓
AeaMotorClient.CalcularAsync(proyectoDto)
  ↓
HTTP POST http://localhost:8000/api/v1/calcular
  ↓
Backend: calcular_proyecto(proyecto)
  (grado → bocas → circuitos → conductores → montante → validaciones)
  ↓
ResultadoProyectoDto (grado, circuitos, demanda, montante, reglas)
  ↓
Actualizar UI (color de cumplimiento, lista de circuitos, grado)
  ↓
(Opcional) Guardar en DB via SincronizarProyectoAsync
```

### 6.2 Generación de Propuesta Eléctrica

```
Usuario ejecuta ADE_PROPUESTA en AutoCAD
  ↓
PropuestaCommand.GenerarPropuesta()
  ↓
Leer ambientes de DwgAmbienteRepository
  ↓
Si ProjectId > 0: obtener datos de /api/v1/proyectos/{id}
  ↓
motorClient.GenerarPropuestaAsync(ProposalProjectInput)
  ↓
HTTP POST /api/v1/adelec/generate_proposal
  ↓
Backend: PlacementEngine + RoutingEngine
  (colocación de luminarias, tomas, llaves + trazado de cañerías)
  ↓
ProposalResponse (ambientes con puntos eléctricos, circuitos, rutas)
  ↓
SyncReceiverCommand inserta bloques en AutoCAD
  (cada punto eléctrico → un bloque con atributos CX, POT, LLAVE-N°0)
  ↓
Bloques visibles en el plano con sus atributos
```

### 6.3 Sincronización Web → CAD (ADE_PULL)

```
Usuario ejecuta ADE_PULL en AutoCAD
  ↓
SyncReceiverCommand.PullFromWeb()
  ↓
Obtener ProjectId de DwgProjectRepository
  ↓
motorClient.GetProjectAsync(projectId)
  ↓
HTTP GET /api/v1/proyectos/{id}
  ↓
Backend retorna SyncProjectResponse con rooms[].points[]
  ↓
AutoCAD:
  - Escanear bloques existentes por WebID (xdata)
  - Por cada punto web:
    → Actualizar bloque existente OR crear nuevo bloque
  - Eliminar bloques huérfanos (en CAD pero no en web)
```

---

## 7. Despliegue

### 7.1 Plugin AutoCAD

**Script de inicio: [deploy/acad.lsp](deploy/acad.lsp)**
1. AutoCAD arranca
2. `acad.lsp` ejecuta `NETLOAD` → carga `AdElec.AutoCAD.dll`
3. `PluginEntry.Initialize()` → carga archivos LISP
4. Usuario ejecuta `ADE_PANEL` → abre el sidebar

**Secuencia de carga del DLL:**
```
acad.lsp
  └─ NETLOAD "deploy\AdElec.AutoCAD.dll"
       └─ PluginEntry.Initialize()
            ├─ Registrar comandos C# (via [CommandMethod])
            └─ Cargar LISP: ade_utils, ade_luminarias, ade_tomas, ade_canerias
```

**Para compilar:**
```bash
dotnet build AdElec.sln
# DLLs → deploy/
```

### 7.2 Backend FastAPI

**Para iniciar:**
```bash
cd "G:\PROYECTOS AD\AEA 90364-B\AEA-MOTOR-RESTORED"
uvicorn backend.api.app:app --reload --host 0.0.0.0 --port 8000
```

**Variables de entorno (`.env`):**
```env
DATABASE_URL=sqlite:///./aea_motor.db
SECRET_KEY=tu-clave-secreta
ALLOWED_ORIGINS=http://localhost:5173
```

**Verificación:** `http://localhost:8000/docs` → Swagger UI

---

## 8. Patrones Arquitectónicos

### Plugin C#
| Patrón | Donde se aplica |
|--------|----------------|
| **Clean Architecture** | Core → AutoCAD → UI (sin dependencias inversas) |
| **Repository Pattern** | `DwgPanelRepository`, `DwgAmbienteRepository` |
| **MVVM** | `PanelViewModel` con `ObservableCollection` |
| **DTO Pattern** | `ProyectoDto`, `AmbienteDto` para serialización JSON |
| **Dependency Injection** | DI manual en `PaletteCommand` |

### Backend Python
| Patrón | Donde se aplica |
|--------|----------------|
| **Strategy Pattern** | `NormRegistry`/`NormStrategy` para distintas versiones de norma |
| **Engine/Core Separation** | Cálculos puros en `engine/`, ORM en `api/` |
| **Pydantic Validation** | Modelos tipados con serialización automática |
| **Repository Pattern** | `crud.py` abstrae operaciones de DB |

---

## 9. Tecnologías y Dependencias

### Plugin AutoCAD
- **.NET 8** — Framework de aplicación
- **AutoCAD 2025 SDK** — `Autodesk.AutoCAD.Interop.*`
- **WPF** — Interfaz de usuario embebida en AutoCAD
- **System.Net.Http** — Cliente HTTP para comunicación con backend
- **AutoLISP** — Macros de CAD para comandos interactivos

### Backend Python
- **FastAPI** — Framework web REST
- **SQLAlchemy 2.0** — ORM para base de datos
- **Pydantic v2** — Validación y serialización de datos
- **Uvicorn** — Servidor ASGI
- **ezdxf** — Lectura/escritura de archivos DXF
- **SQLite** — Base de datos (por defecto)
- **python-jose** — JWT para autenticación

---

*Documento generado automáticamente. Última actualización: 2026-04-09*
