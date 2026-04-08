# AD-ELEC — Auditoría Técnica y Roadmap
> Fecha: 2026-04-07 | Revisado por: Claude Sonnet 4.6

---

## 1. Estado General del Proyecto

| Dimensión | Estado | Nota |
|---|---|---|
| Arquitectura | ✅ Sólida | Clean Architecture correctamente separada |
| Compilación | ✅ OK | .NET 8, AutoCAD 2025 API |
| Comandos principales | ✅ Funcionales | ADE_LUMINARIAS, ADE_TOMAS, ADE_INSPECT |
| UI (Paleta WPF) | ⚠️ Esqueleto | Botones sin handlers, sin MVVM real |
| Persistencia | ✅ Funcional | XRecord en DWG, JSON chunks |
| Cálculos eléctricos | ❌ Ausente | Solo modelos, sin lógica de cálculo |
| Diagramas / Unifilar | ❌ Ausente | No implementado |
| Pruebas unitarias | ❌ Ausente | Sin proyecto de tests |
| Documentación técnica | ⚠️ Parcial | README básico, Bloques.md completo |

---

## 2. Inventario de Código — Lo que existe

### 2.1 AdElec.Core (Lógica de Negocio)

| Archivo | Clase / Interfaz | Estado | Observaciones |
|---|---|---|---|
| `Models/Panel.cs` | `Panel` | ✅ | Modelo completo. `TotalLoadVA()` implementado |
| `Models/Circuit.cs` | `Circuit` | ✅ | Modelo completo. Falta validación de fase |
| `Algorithms/GridCalculator.cs` | `GridCalculator` | ✅ | `CalculateUniformGrid()` funcional |
| `Interfaces/IPanelRepository.cs` | `IPanelRepository` | ✅ | Interfaz correctamente definida |

**Faltantes detectados en Core:**
- `Load.cs` — mencionado en README pero no existe
- `BlockDefinition.cs` — mencionado en README pero no existe
- Lógica de cálculo de caída de tensión
- Lógica de sección de cables (reglamento AEA 90364)
- Balanceo de fases (distribución R/S/T)
- Motor de cálculo de potencias por circuito

### 2.2 AdElec.AutoCAD (Motor de Dibujo)

| Archivo | Clase | Estado | Observaciones |
|---|---|---|---|
| `PluginEntry.cs` | `PluginEntry` | ✅ | Carga correctamente |
| `Commands/PaletteCommand.cs` | `PaletteCommand` | ✅ | Abre paleta sin errores |
| `Commands/BlockInspectorCommand.cs` | `BlockInspectorCommand` | ✅ | Exporta JSON a escritorio |
| `Commands/AutoPlacementCommands.cs` | `AutoPlacementCommands` | ✅ | ADE_LUMINARIAS y ADE_TOMAS funcionales |
| `Managers/BlockManager.cs` | `BlockManager` | ✅ | Importa bloques externos al DWG |
| `Geometry/LuminariasJig.cs` | `LuminariasInteractive` | ✅ | Preview transiente funcional |
| `Geometry/PolylineExtensions.cs` | extensiones | ✅ | Bounding box y perimetro implementados |
| `Repositories/DwgPanelRepository.cs` | `DwgPanelRepository` | ✅ | Persiste en XRecord con chunks de 2000 chars |

**Faltantes detectados en AutoCAD:**
- Comando `ADE_UNIFILAR` (diagrama unifilar) — no existe
- Comando `ADE_CABLEADO` (ruteo de cables) — no existe
- Comando `ADE_CIRCUITO` (asignación de circuitos a grupos de bocas) — no existe
- Comando `ADE_LABEL` (etiquetado automático de cañerías) — no existe
- Sin manejo de errores centralizado (cada comando con try/catch propio)

### 2.3 AdElec.UI (Interfaz WPF)

| Archivo | Estado | Observaciones |
|---|---|---|
| `Views/MainPaletteView.xaml` | ⚠️ Esqueleto | Layout OK, botones sin lógica |
| `Views/MainPaletteView.xaml.cs` | ❌ Vacío | Solo `InitializeComponent()` |
| ViewModels | ❌ No existe | Sin MVVM implementado |
| Servicios UI | ❌ No existen | Sin binding de datos a paleta |

---

## 3. Hallazgos y Problemas Detectados

### 3.1 Inconsistencias entre README y código real

| Ítem en README | Realidad | Impacto |
|---|---|---|
| `Load.cs` y `BlockDefinition.cs` en modelos | No existen | Documentación desactualizada |
| "MVVM: Separación estricta" | No hay ViewModels | README es aspiracional, no real |
| "Motor de cálculo, caída de tensión" | No implementado | Funcionalidad prometida ausente |
| "Enrutamiento automático" | No implementado | Funcionalidad prometida ausente |

### 3.2 Deuda técnica

| Problema | Severidad | Ubicación |
|---|---|---|
| Botones de paleta sin event handlers | Alta | `MainPaletteView.xaml.cs` |
| Ruta hardcodeada `G:\AD-ELEC` | Media | `AutoPlacementCommands.cs` |
| Sin proyecto de tests | Media | Toda la solución |
| `BlockManager` falla silenciosamente | Media | `BlockManager.cs` |
| No hay validación de inputs (circuito, spacing) | Baja | `AutoPlacementCommands.cs` |
| `CmbPanels` en UI no está conectado a `DwgPanelRepository` | Alta | `MainPaletteView` |

### 3.3 Ruta hardcodeada — riesgo concreto

En `AutoPlacementCommands.cs` la búsqueda de bloques usa:
```csharp
// Búsqueda relativa al DWG abierto y luego fallback a G:\AD-ELEC
```
Esto **romperá** en cualquier máquina que no sea la de desarrollo. Se debe resolver la ruta de forma dinámica (relativa al ensamblado o configurable).

---

## 4. Fortalezas del Proyecto

- **Arquitectura limpia real**: Core completamente desacoplado de AutoCAD. Permite testear lógica sin la API de Autodesk.
- **Sistema de bloques bien documentado**: `Bloques.md` + JSONs de inspección son una referencia sólida.
- **Persistencia robusta**: XRecord con chunks JSON es una solución correcta para el límite de AutoCAD.
- **Preview transiente**: `LuminariasInteractive` es una UX profesional, con `TransientManager` correctamente usado.
- **Fallback graceful**: Todos los comandos degradan a marcadores simples si los bloques no están disponibles.
- **Geometría correcta**: `GetPointsAlongPerimeter` con tangente para rotación de tomas es técnicamente correcto.

---

## 5. Roadmap Futuro

### Fase 4 — Completar la UI (Prioridad: ALTA)

**Objetivo**: Hacer que la paleta sea funcional y útil en obra.

- [ ] Implementar `PanelViewModel` con INotifyPropertyChanged
- [ ] Conectar `CmbPanels` a `DwgPanelRepository.GetAllPanels()`
- [ ] Implementar diálogo "Nuevo Tablero" (nombre, ubicación, tipo monofásico/trifásico)
- [ ] Implementar vista de circuitos dentro del tablero seleccionado (DataGrid)
- [ ] Conectar botón "Sugerir Luminarias" para llamar a `ADE_LUMINARIAS` desde UI
- [ ] Conectar botón "Sugerir Tomas" para llamar a `ADE_TOMAS` desde UI
- [ ] Indicador de carga total VA y corriente por fase en la paleta

### Fase 5 — Motor de Cálculo Eléctrico (Prioridad: ALTA)

**Objetivo**: Cumplir con el reglamento AEA 90364 (Argentina).

- [ ] Calcular corriente de diseño por circuito: `Id = P / (V × cosφ)`
- [ ] Calcular sección mínima de conductor por corriente admisible (tabla AEA)
- [ ] Calcular caída de tensión: `ΔV = (2 × L × Id × ρ) / S`
- [ ] Alertar cuando ΔV > 3% (iluminación) o 5% (fuerza)
- [ ] Balanceo de fases: algoritmo greedy de asignación R/S/T mínima diferencia
- [ ] Calcular corriente total de tablero y sugerir calibre de interruptor principal
- [ ] Calcular potencia de diseño con factor de simultaneidad por tipo de circuito

### Fase 6 — Diagrama Unifilar (Prioridad: ALTA)

**Objetivo**: Generar automáticamente el diagrama unifilar del tablero en el DWG.

- [ ] Diseñar bloques para representación de unifilar (interruptores, barras, cargas)
- [ ] Implementar `ADE_UNIFILAR` que genere el diagrama a partir del modelo `Panel`
- [ ] Incluir calibres de conductores, protecciones y denominación de circuitos
- [ ] Posicionar el unifilar en espacio de papel (Layout) o en coordenadas libres del modelo
- [ ] Actualizar el unifilar automáticamente al modificar circuitos

### Fase 7 — Etiquetado y Cañerías (Prioridad: MEDIA)

**Objetivo**: Completar el esquema de distribución eléctrica en planta.

- [ ] Implementar `ADE_LABEL`: asignar etiqueta de circuito (CX) a grupos de bocas
- [ ] Implementar `ADE_CAÑERIA`: trazar líneas de cañería entre bocas del mismo circuito
  - Algoritmo de árbol mínimo (MST) para recorrido óptimo
  - Uso del bloque `I.E-AD-Nomenclatura_Cables` para etiquetado automático
- [ ] Implementar `ADE_RECUENTO`: contar bocas por circuito y actualizar carga en `Panel`
- [ ] Detectar bocas sin circuito asignado y marcarlas visualmente

### Fase 8 — Calidad y Robustez (Prioridad: MEDIA)

**Objetivo**: Código production-ready.

- [ ] Crear proyecto `AdElec.Tests` con xUnit
  - Tests unitarios para `GridCalculator`
  - Tests unitarios para cálculos eléctricos (caída de tensión, sección de cables)
  - Tests de integración para `DwgPanelRepository` (requiere AutoCAD o mock)
- [ ] Centralizar manejo de errores con un `AdeLogger` o handler común
- [ ] Resolver ruta de bloques dinámicamente (relativa al ensamblado, no hardcodeada)
- [ ] Validar inputs de usuario en todos los comandos
- [ ] Agregar cancelación correcta (Escape) en todos los flujos de `ADE_TOMAS`

### Fase 9 — Features Avanzados (Prioridad: BAJA)

**Objetivo**: Diferenciación y productividad extendida.

- [ ] `ADE_IMPORT_CSV`: importar lista de circuitos desde planilla Excel/CSV
- [ ] `ADE_EXPORT_MEMORIA`: exportar memoria de cálculo a PDF/DOCX
- [ ] Soporte multi-tablero: vincular tableros secundarios a principal (jerarquía TG → TS)
- [ ] Reglas de diseño configurables (tensión nominal, factor de potencia, norma aplicable)
- [ ] Integración con planos de arquitectura: detectar locales automáticamente desde polilíneas de planta
- [ ] `ADE_MIGRATE`: migración de versiones de datos en XRecord (versionado de esquema)

---

## 6. Prioridades Recomendadas — Próximos Pasos Inmediatos

| # | Tarea | Esfuerzo | Impacto |
|---|---|---|---|
| 1 | Conectar paleta UI a repositorio (leer/crear tableros) | Medio | Alto |
| 2 | Implementar cálculo de corriente y sección de cable | Medio | Alto |
| 3 | Resolver ruta hardcodeada de bloques | Bajo | Alto |
| 4 | Crear proyecto de tests con casos básicos | Bajo | Medio |
| 5 | Implementar `ADE_UNIFILAR` básico (texto plano, sin bloques) | Alto | Alto |
| 6 | Balanceo de fases R/S/T en asignación de circuitos | Medio | Medio |

---

## 7. Integración con AEA-MOTOR (D:\AD-TONTO\AEA 90364-B\AEA-MOTOR-RESTORED)

### Hallazgo
El proyecto **AEA-MOTOR** es un motor normativo Python/FastAPI que implementa **completamente** la AEA 90364-7-771.
Contiene exactamente lo que AD-ELEC necesita en su Fase 5 (cálculos eléctricos) y que actualmente no existe en C#.

### Decisión de arquitectura: NO fusionar, SÍ integrar vía API HTTP

| Proyecto | Rol | Tecnología |
|---|---|---|
| AEA-MOTOR | Motor de cálculo normativo | Python 3.10 + FastAPI |
| AD-ELEC | Motor CAD + UI AutoCAD | C# .NET 8 + AutoCAD.NET |

### Lo que AEA-MOTOR aporta (ya implementado)

| Cálculo | Clausula AEA | Estado |
|---|---|---|
| Grado de electrificación | 771.8.I | ✅ |
| Puntos mínimos por ambiente | 771.8.III | ✅ |
| Demanda por tipo de circuito | 771.9.I | ✅ |
| Sección mínima de conductor | 771.13.I | ✅ |
| Corriente admisible Método B2 | 771.16.II | ✅ |
| Caída de tensión ≤3%/≤1% | 771.19.7 | ✅ |
| Conductor de protección PE | 771.18.III | ✅ |
| Simultaneidad edificios | 771.9.III | ✅ |
| Cortocircuito y selectividad | — | ✅ |
| Motor de reglas (10+ validaciones) | — | ✅ |

### Tareas de integración a agregar al Roadmap

- [ ] Crear `AdeApiClient.cs` en `AdElec.Core/Services/` con `HttpClient` hacia `localhost:8000`
- [ ] Extender modelos `Circuit` y `Panel` para incluir campos que espera AEA-MOTOR (`Ambiente`, `DatosMontante`, `DatosRed`)
- [ ] Conectar WPF palette para enviar proyecto a `/calcular` y mostrar resultados
- [ ] Validar y documentar el endpoint `/exportar/JSON_AUTOCAD` de AEA-MOTOR
- [ ] Inicializar AEA-MOTOR como servicio de fondo al cargar AD-ELEC (o documento de instrucciones de arranque manual)

---

## 8. Métricas del Proyecto (al 2026-04-07)

| Métrica | Valor |
|---|---|
| Proyectos en solución | 3 (Core, AutoCAD, UI) |
| Comandos AutoCAD implementados | 4 (ADE_PANEL, ADE_INSPECT, ADE_LUMINARIAS, ADE_TOMAS) |
| Comandos AutoCAD pendientes | ~5 (UNIFILAR, CAÑERIA, LABEL, CIRCUITO, RECUENTO) |
| Clases de dominio | 3 (Panel, Circuit, GridCalculator) |
| Clases de dominio faltantes | ~4 (Load, VoltageDropCalc, CableSection, PhaseBalancer) |
| Cobertura de tests | 0% |
| Bloques CAD documentados | 6 |
| Líneas de código (estimado) | ~1.200 |
