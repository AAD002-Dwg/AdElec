# 📓 Bitácora de Desarrollo — AD-ELEC

Este documento registra de forma cronológica los avances, cambios arquitectónicos, correcciones de errores y decisiones técnicas relevantes tomadas en el proyecto **AD-ELEC**.

---

## 📋 Estructura de cada registro

Cada entrada debe seguir este formato estándar:

```markdown
### [YYYY-MM-DD] — Título descriptivo del avance o hito
**Tipo:** `Refactor` | `Feature` | `Fix` | `Limpieza` | `Docs`
**Autor / Asistente:** Antigravity / Usuario

#### 🎯 Objetivo
Breve descripción del problema a resolver o meta del cambio.

#### 🛠️ Cambios realizados
- Detalle de modificaciones
- Archivos creados / movidos / eliminados

#### 💡 Decisiones técnicas
Motivo detrás de las elecciones de diseño o solución implementada.

#### 📌 Próximos pasos pendientes
- Tareas subsiguientes derivadas de este avance
```

---

## 📜 Historial de Registros

### [2026-10-24] — Aprendizaje de Hábitos de Nomenclatura, Rediseño Profesional de Tableros y Circuitos (ADE_PANEL), y Sincronización DWG
**Tipo:** `Feature` / `Fix` / `Refactor`  
**Autor / Asistente:** Antigravity / Usuario

#### 🎯 Objetivo
- Diagnosticar y solucionar integralmente el funcionamiento de `ADE_PANEL`: carga de tableros desde bloques `I.E-AD-04` (atributo `TX-X`), corrección del problema de Dispatcher en AutoCAD (`System.Windows.Application.Current` es nulo en el entorno de AutoCAD), y activación del cuadro de circuitos con edición directa, agregado y eliminación.
- Incorporar un sistema inteligente de aprendizaje y adaptación a los hábitos del proyectista: permitir la nomenclatura física directa de conductores (ej: `2x1,5+T`, `3x1,5+T`, `4x1,5+T` para retornos y `2x2,5+T` para tomas) sin forzar códigos abstractos (`F+R+T`), persistiendo y aprendiendo de las decisiones del usuario tanto en C# como en AutoLISP.
- Alinear el sistema de cálculo y exportación con el motor normativo AEA-MOTOR (`G:\PROYECTOS AD\AEA 90364-B\AEA-MOTOR-RESTORED`).

#### 🛠️ Cambios realizados
- `src/AdElec.Core/Models/UserPreferences.cs`:
  - Creado modelo de preferencias de usuario y aprendizaje automático (`AprenderDecision`, `ObtenerNomenclaturaRetorno`).
  - Persistencia global en JSON (`%AppData%\AdElec\user_preferences.json`).
  - Soporte para estilos de rotulación: Sección Física (`2x1,5+T`, `3x1,5+T`, `4x1,5+T`) y Funcional.
- `src/AdElec.Core/Models/Circuit.cs`:
  - Implementado `INotifyPropertyChanged` con `SetField` reactivo para cambios en tiempo real en la UI.
  - Añadido alias `PointsCount` <-> `ArtefactCount` para vinculación con tablas y plantillas.
- `src/AdElec.AutoCAD/Commands/RecargarCommand.cs`:
  - Corregida y potenciada la detección de tableros desde bloques `I.E-AD-04` escaneando el atributo específico `TX-X`.
  - Si una boca no tiene asignado tablero o circuito (`D` vacío), se asigna de forma inteligente al tablero activo (`TD1`).
  - Mapeo espacial de ambientes a cada circuito para poblar automáticamente la columna de destinos.
  - Preservación de atributos editados previamente por el usuario al sincronizar desde el plano.
- `src/AdElec.AutoCAD/Commands/PaletteCommand.cs`:
  - Corregido el despachador de subprocesos: reemplazado `System.Windows.Application.Current?.Dispatcher` por `_view?.Dispatcher` asegurando la ejecución fluida en el hilo UI de AutoCAD.
  - Sincronización directa y segura de circuitos en el hilo de AutoCAD mediante `_view?.Dispatcher.Invoke(...)`.
- `src/AdElec.UI/ViewModels/PanelViewModel.cs`:
  - Incorporadas colecciones observables para listas desplegables normativas (`CircuitTypesList`, `WireOptionsList`, `BreakerOptionsList`, `PhaseOptionsList`).
  - Implementados comandos interactivos: `AgregarCircuitoCommand`, `EliminarCircuitoCommand` y `GuardarCircuitosCommand`.
  - Recálculo automático de potencia total en VA y kVA al modificar circuitos.
- `src/AdElec.UI/Views/MainPaletteView.xaml`:
  - Rediseñada la pestaña "Circuitos" con barra de herramientas (`+ Circuito`, `🗑 Eliminar`, `💾 Guardar`, `↺ DWG`, `⚡ AEA`) y `DataGrid` editable oscuro con celdas de alta legibilidad (`#141c2b`, texto blanco, borde azul celeste).
  - Limpieza de etiquetas redundantes y unificación visual con el tema oscuro del complemento.
- `lisp/ade_nomenclatura.lsp`:
  - Actualizado el flujo de rotulación para sugerir por defecto nomenclatura física: `2x1,5+T` para 1 efecto, `3x1,5+T` para 2 efectos, `4x1,5+T` para 3 efectos y `2x2,5+T` para tomas.
  - Opciones rápidas interactivas: `[1 (2x1,5+T) / 2 (3x1,5+T) / 3 (4x1,5+T) / Combinacion / Manual]`.
- `src/AdElec.AutoCAD/Commands/EfectoCommand.cs`:
  - Vinculación interactiva con el modelo de preferencias para sugerir la nomenclatura física aprendida al vincular llaves y efectos.

#### 💡 Decisiones técnicas
- En AutoCAD, las ventanas WPF no siempre disponen de `System.Windows.Application.Current` porque el ejecutable anfitrión es `acad.exe` y no una aplicación WPF nativa. Usar `_view?.Dispatcher` garantiza la ejecución en el hilo correcto evitando excepciones silenciosas o interfaces congeladas.
- En la práctica profesional argentina bajo AEA 90364-771, los planos eléctricos ejecutivos indican secciones físicas de conductores (`2x1,5+T`, `3x1,5+T`) en lugar de códigos abstractos (`F+R`), ya que son las que el instalador y el calculista necesitan para cómputo métrico y verificación de ocupación de cañerías.

#### 📌 Próximos pasos pendientes
- Continuar refinando el asistente de distribución de cañerías automáticas entre bocas agrupadas por circuito.
- Agregar exportación directa de cómputo métrico de cables y cañerías a Excel / CSV.

### [2026-10-24] — Generador Automático de Diagramas Unifilares (ADE_UNIFILAR), Asistente de Interruptores (ADE_EFECTO) y Validación Normativa AEA 90364-771
**Tipo:** `Feature` / `Refactor`  
**Autor / Asistente:** Antigravity / Usuario

#### 🎯 Objetivo
- Implementar la generación 100% automatizada de Diagramas Unifilares en AutoCAD (`ADE_UNIFILAR`) conforme a la reglamentación AEA 90364-771, ensamblando los bloques dinámicos oficiales (`UNIFV.3`, `I.E-U-2A`, `I.E-U-1A`, `INFO E.I-1.1`), barras colectoras, puesta a tierra reglamentaria y cálculo dinámico de potencias simultáneas.
- Implementar el Asistente de Interruptores y Efectos (`ADE_EFECTO`, `ADE_LLAVE`) para vincular y sincronizar de forma ágil las llaves (`I.E-AD-01`) con sus efectos (`LLAVE-N°0/1/2`), sus visibilidades dinámicas (`llave 1 efecto`, `llave 2 efecto`, `llave 3 efecto`) y los atributos `PX` de las luminarias (`I.E-AD-07`).
- Mejorar el etiquetado de cañerías y cables en `ade_nomenclatura.lsp`, resolviendo dinámicamente el bloque `I.E-AD-Nomenclatura_Cables` y agregando el modo `Retornos` (`F+R`, `F+2R`, `F+3R`, `F+R+T`).
- Integrar reglas normativas completas de circuitos AEA (`IUG`, `TUG`, `IUE`, `TUE`, `AP`, `ACU`, `RESERVA`), reserva obligatoria del 20% / 1 circuito, y botones de acceso directo en la paleta lateral `ADE_PANEL`.

#### 🛠️ Cambios realizados
- `src/AdElec.Core/Algorithms/AeaRules.cs`:
  - Creado módulo de validación y formateo normativo bajo AEA 90364-771: límites de bocas, sección mínima de conductores, capacidad admisible $I_z$, coordinación térmica $I_b \le I_n \le I_z$, potencias mínimas simultáneas y método `EnsureReserveCircuit`.
- `src/AdElec.Core/Models/Circuit.cs`:
  - Enriquecido con propiedades normativas: `ArtefactCount`, `Destinations`, `Curve`, `Poles`, `Norma`, `PhaseString` y `Description`.
- `src/AdElec.Core/Models/Panel.cs`:
  - Añadidas propiedades eléctricas y de unifilar: `MainDiffAmps`, `MainDiffSensitivityMa`, `FeederFrom`, `SimultaneityFactor`, `PowerFactor`, `FunctionalUnitsCount`, `TotalLoadKVA()`, `TotalLoadKW()`, `SimultaneousKW()` y `TotalProjectKW()`.
- `src/AdElec.AutoCAD/Services/UnifilarGeneratorService.cs`:
  - Motor de ensamble gráfico: inserción y configuración de columnas `UNIFV.3`, termomagnéticas `I.E-U-2A` (con visibilidad Bipolar/Tetrapolar), interruptor diferencial `I.E-U-1A`, cuadro resumen `INFO E.I-1.1` con estiramiento dinámico `Distancia1`, barra colectora (peine de distribución), bajadas con flechas, derivaciones PE (capa verde), sistema de puesta a tierra, acometida superior y caja perimetral discontinua con rótulo de tablero.
- `src/AdElec.AutoCAD/Commands/UnifilarCommand.cs`:
  - Comandos `ADE_UNIFILAR` y `ADE_UNIF`: selección de tablero (o sincronización automática si no hay cargados), ingreso de UF y coeficiente de simultaneidad, selección del punto de inserción e invocación del servicio unifilar.
- `src/AdElec.AutoCAD/Commands/EfectoCommand.cs`:
  - Comandos `ADE_EFECTO` y `ADE_LLAVE`: edición y vinculación interactiva de interruptores y luminarias, alternando automáticamente estados de visibilidad dinámica y rellenando `PX` en las bocas de techo/pared.
- `src/AdElec.AutoCAD/Commands/BlockInspectorCommand.cs`:
  - Agregado comando `ADE_INSPECT_FILE` para inspección directa de archivos DWG en disco sin abrirlos en el editor principal, y extracción de dimensiones espaciales (`Width`, `Height`, `Position`, `Extents`).
- `src/AdElec.UI/ViewModels/PanelViewModel.cs` y `src/AdElec.UI/Views/MainPaletteView.xaml`:
  - Incorporados botones de acceso rápido `⚡ Generar Unifilar (ADE_UNIFILAR)` y `🔀 Efectos e Interruptores (ADE_EFECTO)`.
- `src/AdElec.AutoCAD/Commands/PaletteCommand.cs`:
  - Conectados los nuevos comandos de unifilar y efectos a la paleta lateral.
- `lisp/ade_nomenclatura.lsp`:
  - Corregida la ruta del bloque `I.E-AD-Nomenclatura_Cables.dwg` a búsqueda dinámica local/relativa, compatibilidad con atributos `CAÑERIA` y `CA ERIA`, y añadido modo interactivo `Retornos`.

#### 💡 Decisiones técnicas
- **Autonomía total en AutoCAD:** Toda la lógica de cálculo y dibujo unifilar opera directamente sobre la base de datos de AutoCAD sin dependencias de red o de AEA-MOTOR, convirtiendo a AutoCAD en la fuente fidedigna de diseño eléctrico.
- **Parametrización dinámica nativa:** El diagrama unifilar aprovecha al 100% los bloques dinámicos y atributos preexistentes en `blocks/UNIFILARES/`, permitiendo que el usuario pueda seguir editando visualmente cualquier aspecto con los grips de AutoCAD tras la generación.
- **Reserva reglamentaria garantizada:** Si el plano carece de un circuito de reserva, el sistema lo incorpora automáticamente según el artículo 771.8 para cumplir la reglamentación.

#### 📌 Próximos pasos pendientes
- Posibilidad de exportar la planilla de cargas y el unifilar a formato PDF/Excel normalizado.
- Soporte para esquemas con múltiples interruptores diferenciales seccionales para tableros con más de 8 circuitos.

---

### [2026-10-24] — Rediseño Gestor de Ambientes (UI Dark), Mapeo de Artefactos Eléctricos y Autonomía de ADE_PANEL
**Tipo:** `Feature` / `Fix`  
**Autor / Asistente:** Antigravity / Usuario

#### 🎯 Objetivo
- Solucionar el problema de contraste en `AutoAmbientesDialog` donde los desplegables de ComboBox aparecían con fondo blanco y texto ilegible.
- Transformar `ADE_AUTOAMBIENTES` en un auténtico **Gestor de Ambientes**, permitiendo editar de forma ágil la Unidad Funcional (UF), el Piso/Planta y el Tipo de local.
- Mapear espacialmente las bocas de iluminación, tomacorrientes y circuitos dentro de los límites de cada ambiente mediante inclusión geométrica (Point-in-Polygon).
- Dotar a la paleta lateral `ADE_PANEL` de autonomía total: detectar y mostrar el documento DWG activo de inmediato, interpretar automáticamente los tableros y circuitos presentes en el plano y calcular sus potencias reales en VA/kVA sin requerir que AEA-MOTOR esté encendido.

#### 🛠️ Cambios realizados
- `src/AdElec.UI/Views/AutoAmbientesDialog.xaml`:
  - Se forzó el tema oscuro a través de `SystemBrushes` (`WindowBrushKey` = `#252525`, `WindowTextBrushKey` = `#FFFFFF`) y estilos específicos para `ComboBoxItem` y `ComboBox`, eliminando el fondo blanco en los desplegables.
  - Se añadieron columnas editables para **Piso / Planta** y **Unidad Funcional**.
  - Se incorporaron columnas de artefactos (**💡 Luces**, **🔌 Tomas**), **Circuitos** y **Estado** (Existente/Nuevo).
  - Se añadió un panel inferior de inspección que muestra el inventario detallado de cargas, potencia en VA y circuitos del recinto seleccionado, con botón de centrado en plano.
  - Se amplió el ancho a 1020px y se actualizó el título a "Gestor de Ambientes & Cargas".
- `src/AdElec.UI/ViewModels/AutoAmbientesViewModel.cs`:
  - Se hizo editable la propiedad `Planta` en `DetectedRoomRow`.
  - Se agregaron `PlantasOptions`, métricas globales de bocas y potencia, y soporte para notificación del ambiente seleccionado.
- `src/AdElec.Core/Models/DetectedRoom.cs`:
  - Se añadieron las propiedades `CantidadLuminarias`, `CantidadTomas`, `CantidadOtros`, `TotalBocas`, `PotenciaTotalVA`, `CircuitosDetalle` y `ArtefactosDetalle`.
- `src/AdElec.AutoCAD/Commands/AutoAmbientesCommand.cs`:
  - Se implementó `MapearArtefactosEnAmbientes`, que escanea bloques del dibujo (`I.E-AD-07`, `I.E-AD-08`, `I.E-AD-09`, `I.E-AD-09.02`, luminarias y tomas genéricos), verifica su pertenencia al polígono del recinto mediante `PolygonMath.PointInPolygon`, cuenta bocas, acumula potencias en VA y extrae los circuitos vinculados.
- `src/AdElec.AutoCAD/Commands/PaletteCommand.cs`:
  - Se corrigió la inicialización de `ActiveDwgName` llamando a `ReloadFromCurrentDoc(doc)` inmediatamente tras crear la paleta, resolviendo la etiqueta "Sin documento".
- `src/AdElec.AutoCAD/Commands/RecargarCommand.cs`:
  - Se implementó `SincronizarTablerosDesdeDwg`: descubre automáticamente tableros a partir del atributo `D` y de bloques `ID_TABLERO`, extrae sus circuitos `CX`, suma las potencias en VA de cada boca (leyendo `POT` o aplicando mínimos de AEA 90364) y calcula la carga total en VA y kVA.

#### 💡 Decisiones técnicas
- La paleta ahora calcula automáticamente la potencia simultánea de cada circuito y del tablero en el entorno local de AutoCAD, proporcionando valor inmediato al usuario antes o en ausencia del cálculo normativo con AEA-MOTOR.
- El mapeo punto-en-polígono permite relacionar cualquier bloque eléctrico (tomas, luces, interruptores) con su recinto físico sin necesidad de trazados manuales previos.

#### 📌 Próximos pasos pendientes
- Cerrar AutoCAD para liberar el DLL en `deploy\`, recompilar y probar la nueva experiencia visual y funcional.

---

### [2026-10-24] — Robustez e interoperabilidad total entre ADE_AMBIENTES y ADE_AUTOAMBIENTES
**Tipo:** `Feature` / `Fix`  
**Autor / Asistente:** Antigravity / Usuario

#### 🎯 Objetivo
- Lograr que los ambientes creados o etiquetados manualmente mediante `ADE_AMBIENTES` sean detectados, reconocidos y preservados por `ADE_AUTOAMBIENTES` (y su alias `ADE_AUTO_AMBIENTES`).
- Evitar duplicación de bloques `ID_LOCALES` y permitir actualizar o verificar recintos existentes en la interfaz gráfica.
- Brindar al usuario la opción interactiva de seleccionar polilíneas/entidades específicas en pantalla o realizar escaneo automático en todo el dibujo.
- Corregir el filtrado restrictivo de capas en `RoomDetector` y normalizar las rutas de bloques a `blocks/ID_LOCALES.dwg`.

#### 🛠️ Cambios realizados
- `src/AdElec.Core/Models/Ambiente.cs`:
  - Se agregó la propiedad `PolylineHandle` para almacenar el handle de la polilínea asociada a través del XData `ADE_SYNC_LINK`.
- `src/AdElec.Core/Models/DetectedRoom.cs`:
  - Se añadieron `ExistingBlockHandle` y la propiedad booleana calculada `IsExisting` para distinguir recintos preexistentes.
- `src/AdElec.Core/Models/RoomDetectionConfig.cs`:
  - Se agregó la lista de capas de recintos `CapasAmbientes` (`AMBIENTES`, `RECINTOS`, `LOCALES`, `AREAS`, `0`, `ARQUITECTURA`, etc.).
- `src/AdElec.AutoCAD/Repositories/DwgAmbienteRepository.cs`:
  - Se incorporó la lectura del XData `ADE_SYNC_LINK` para asociar `ambiente.PolylineHandle`.
- `src/AdElec.AutoCAD/Geometry/RoomDetector.cs`:
  - Se admitió el procesamiento de polilíneas cerradas pertenecientes a `CapasAmbientes` o con XData `ADE_SYNC_LINK`.
  - Se agregó el parámetro `selectedEntities` a `DetectRooms` para soportar selecciones parciales y se expuso `ExtractPolylineVertices` como método estático público.
- `src/AdElec.AutoCAD/Geometry/RoomClassifier.cs`:
  - Se implementó la coincidencia "Nivel 0", que busca bloques `ID_LOCALES` existentes dentro del polígono del recinto y hereda sus datos (Local, UF, Planta, Handle) con alta confianza.
- `src/AdElec.AutoCAD/Commands/AutoAmbientesCommand.cs`:
  - Se registró el doble comando `[CommandMethod("ADE_AUTOAMBIENTES")]` y `[CommandMethod("ADE_AUTO_AMBIENTES")]`.
  - Se corrigió la ruta del bloque a `blocks\\ID_LOCALES.dwg`.
  - Se agregó el prompt interactivo de modo: `[Automatico todo el dibujo/Seleccionar entidades]`.
  - Se sincronizaron los ambientes preexistentes mediante `DwgAmbienteRepository`, integrando en el panel tanto los detectados como los existentes que no coincidieran en la pasada.
  - Se diferenció la acción de confirmación: si el ambiente ya existía se actualizan sus atributos en el bloque `ID_LOCALES` existente; si es nuevo se inserta el bloque y se vincula con la polilínea vía `ADE_SYNC_LINK`.
- `src/AdElec.AutoCAD/Commands/AmbientesCommand.cs` y `AutoAmbientesCVCommand.cs`:
  - Se actualizó la constante de ruta de `ID_LOCALES.dwg` a `blocks\\ID_LOCALES.dwg`.

#### 💡 Decisiones técnicas
- El flujo manual (`ADE_AMBIENTES`) y el flujo automático (`ADE_AUTOAMBIENTES`) ahora comparten la misma fuente de verdad geométrica y semántica: el bloque `ID_LOCALES` y el XData `ADE_SYNC_LINK`. Ningún ambiente existente se pierde ni se duplica.
- Si el usuario elige `Seleccionar entidades`, se omiten las inferencias pesadas para una respuesta instantánea y enfocada en los recintos indicados.

#### 📌 Próximos pasos pendientes
- Cerrar AutoCAD para liberar el DLL en `deploy\`, recompilar y probar ambos comandos en el dibujo.

---

### [2026-10-24] — Corrección de OSNAP y fijación sobre polilínea en ADE_TOMAS
**Tipo:** `Fix`  
**Autor / Asistente:** Antigravity & Usuario

#### 🎯 Objetivo
- Solucionar la ausencia de glifos de OSNAP en `ADE_TOMAS` modo Manual tras la reorganización de la carpeta de utilidades a `lisp/vendor/`.
- Garantizar que al insertar tomacorrientes en modo Manual queden matemáticamente magnetizados y colocados sobre la polilínea de pared, tal como funciona en la opción "Desde".

#### 🛠️ Cambios realizados
- `lisp/ade_tomas.lsp`:
  - Se corrigió la ruta de carga de `GrSnapV1-0.lsp` para que resuelva hacia `vendor/GrSnapV1-0.lsp` mediante búsqueda dinámica relativa y absoluta.
  - En `ade:tomas-manual`, se incorporó la proyección exacta del punto `pt` sobre la curva de pared mediante `(vlax-curve-getClosestPointTo ent-wall pt)` tanto en el loop interactivo de previsualización (`Type 5`) como en el clic de inserción (`Type 3`).
  - En `ade:insert-toma-on-wall`, se aseguró la proyección de `pt` sobre la entidad seleccionada.
- `src/AdElec.AutoCAD/PluginEntry.cs`: Se agregó `vendor/GrSnapV1-0.lsp` al arreglo de rutinas cargadas automáticamente al iniciar el plugin.

#### 💡 Decisiones técnicas
- Igualar la lógica de fijación geométrica de `Manual` a la de `Desde`, de modo que el cursor se imante a la pared dentro del radio de captura y el bloque quede siempre apoyado en la polilínea.

#### 📌 Próximos pasos pendientes
- Recargar `ade_tomas.lsp` en AutoCAD y verificar la visualización de los snaps y la inserción sobre la polilínea.

---

### [2026-10-21] — Creación de Solución .sln, portabilidad de rutas y documentación raíz
**Tipo:** `Refactor` / `Feature` / `Docs`  
**Autor / Asistente:** Antigravity & Usuario

#### 🎯 Objetivo
- Dotar al proyecto de una solución .NET (`AdElec.sln`) para compilar todos los proyectos con un solo comando o IDE.
- Eliminar rutas hardcodeadas obsoletas (`D:\AD-TONTO\...` y `G:\AD-ELEC\...`) producto del traslado de repositorios al disco `G:\PROYECTOS AD\`.
- Documentar formalmente el ecosistema en `README.md` especificando la interacción entre `AD-ELEC` y `AEA-MOTOR-RESTORED`.

#### 🛠️ Cambios realizados
- **Solución .NET:** Creación de `AdElec.sln` vinculando `AdElec.Core.csproj`, `AdElec.UI.csproj` y `AdElec.AutoCAD.csproj`.
- **Ruta de Despliegue (.csproj):** En `AdElec.AutoCAD.csproj`, se reemplazó la ruta absoluta fija por la macro relativa `$(MSBuildThisFileDirectory)..\..\deploy\`.
- **Carga LISP / AutoCAD:** Actualización de `deploy/acad.lsp` para soportar búsqueda dinámica y la ruta oficial `G:\PROYECTOS AD\AD-ELEC\deploy\AdElec.AutoCAD.dll`.
- **Documentación:** Creación del `README.md` raíz y actualización completa de rutas y árbol de carpetas en `docs/ARQUITECTURA.md` y `docs/AEA_MOTOR.md`.

#### 💡 Decisiones técnicas
- Mantener `AD-ELEC` y `AEA-MOTOR` como repositorios separados en `G:\PROYECTOS AD\` con comunicación HTTP REST pura.
- La compilación `dotnet build AdElec.sln --configuration Release` quedó verificada con 0 errores.

#### 📌 Próximos pasos pendientes
- Implementar suite de tests en `tests/AdElec.Core.Tests` para validar reglas AEA 90364 offline.
- Conectar endpoints del cliente HTTP en `AdElec.Core/AeaMotor` con el backend en FastAPI cuando se levante el servicio.

---

### [2026-10-21] — Desacoplamiento de módulo IA y reorganización estructural del repositorio
**Tipo:** `Limpieza` / `Refactor` / `Fix`  
**Autor / Asistente:** Antigravity & Usuario

#### 🎯 Objetivo
1. Separar el pipeline experimental de IA (YOLOv8 y segmentación DXF) hacia su repositorio original (`AD_VIEJO ELEC`) para mantener AD-ELEC enfocado puramente en la lógica CAD (.NET + AutoLISP + AEA 90364).
2. Reorganizar la estructura de directorios del proyecto para eliminar nombres con espacios, estandarizar rutas y solucionar inconsistencias de carga en runtime.

#### 🛠️ Cambios realizados
- **Desacoplamiento a `AD_VIEJO ELEC`:**
  - Se movió `src/AdElec.AiSegmentation/` (scripts Python y pesos YOLO).
  - Se movió `src/Lisp/ADE_AI_Layers.lsp`.
  - Se movió `DATASET/` (~55 MB de DXFs y modelos de entrenamiento).
  - Se mantuvieron en C# los comandos `AutoAmbientesCommand`, `AutoAmbientesCVCommand` y `AutoDatasetCommand` con detección geométrica nativa segura (fallback ante ausencia de Python).
- **Estandarización de carpetas en `AD-ELEC`:**
  - `LISP-AD-ELEC/` ➔ `lisp/`
  - `LISP BASE/` ➔ `lisp/vendor/` (utilidades de terceros aisladas)
  - `Bloques_CAD/` ➔ `blocks/`
  - `MD/` y `memory/` ➔ consolidados en `docs/` (`docs/fases/`, `docs/specs/`)
- **Fix crítico de carga AutoLISP:**
  - `src/AdElec.AutoCAD/PluginEntry.cs`: Se corrigió la ruta de búsqueda. El código buscaba una carpeta `"LISP"`, por lo que nunca encontraba `"LISP-AD-ELEC"`. Ahora resuelve correctamente hacia `lisp/`.

#### 💡 Decisiones técnicas
- Mantener nombres en minúsculas y sin espacios (`lisp/`, `blocks/`, `docs/`) para evitar problemas con herramientas CLI, scripts batch y compilación cruzada.
- Aislar el código de terceros en `lisp/vendor/` para distinguir claramente el desarrollo propio de bibliotecas auxiliares.

#### 📌 Próximos pasos pendientes
- Validar compilación del plugin `.NET` y carga en AutoCAD.
- Continuar desarrollo del motor normativo AEA 90364 y comandos de cálculo de circuitos.

---

### [2026-10-21] — Inicialización de la bitácora del proyecto
**Tipo:** `Docs` / `Config`  
**Autor / Asistente:** Antigravity & Usuario

#### 🎯 Objetivo
Establecer un mecanismo formal y ordenado de seguimiento de tareas y registrar instrucciones persistentes para que los asistentes de IA documenten automáticamente los progresos.

#### 🛠️ Cambios realizados
- Creación del directorio [bitacora/](file:///g:/PROYECTOS%20AD/AD-ELEC/bitacora) y el archivo central [bitacora.md](file:///g:/PROYECTOS%20AD/AD-ELEC/bitacora/bitacora.md).
- Configuración de directivas para el asistente en `AGENTS.md` y `CLAUDE.md`.
