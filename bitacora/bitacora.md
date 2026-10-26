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

### [2026-10-25] — Auditoría Exhaustiva de Calidad, Endurecimiento de Rust Core, Portabilidad de DLL y Quick-Fixes de Tablero
**Tipo:** `Fix`
**Autor / Asistente:** Antigravity / Usuario

#### 🎯 Objetivo
- Auditar y verificar exhaustivamente el motor normativo AEA frente a casos de borde, cadenas con tildes/mayúsculas, superficies nulas o negativas, punteros no administrados y portabilidad del plugin en entornos AutoCAD reales.
- Asegurar 100% de coherencia entre los catálogos JSON, los cálculos analíticos en Rust y las correcciones automáticas en la UI de AutoCAD.

#### 🛠️ Cambios realizados
- `aea/core/src/aea770.rs`:
  - Eliminados los `.expect(...)` en `grado_electrificacion` y selección de variantes mínimas, sustituyéndolos por `.unwrap_or(...)` seguros con clamp de superficie a `0.0` para prevenir panics ante `NaN` o valores atípicos.
  - Normalización Unicode con reemplazo de diacríticos (`á`, `é`, `í`, `ó`, `ú`, `ñ` a ASCII base) en `calcular_puntos_minimos_ambiente` para soportar nombres de ambientes en mayúsculas y con acentos provenientes de capas o bloques CAD (`"BAÑO"`, `"GALERÍA"`, `"BALCÓN"`).
  - Verificación formal de la tabla 770.7.III: `balcon_galeria` exige 0 TUG y 1 IUG c/5m.
- `src/AdElec.Core/Aea/AeaBridge.cs`:
  - Resuelto fallo potencial de carga de `aea_core.dll` en clientes AutoCAD: `AppDomain.CurrentDomain.BaseDirectory` apunta a `C:\Program Files\Autodesk\AutoCAD 2024\`, por lo que se incorporó la resolución mediante `Path.GetDirectoryName(assembly.Location)`.
  - Reemplazo del loop manual de punteros no administrados por `Marshal.PtrToStringUTF8(ptr)` nativo y seguro.
- `src/AdElec.Core/Aea/PanelAeaExtensions.cs`:
  - Protección de conversión ante valores nulos, divisiones por cero en factor de potencia y redondeo de enteros positivos (`u32`) requeridos por Rust.
  - Soporte de Quick-Fixes a nivel de tablero: `cambiar_norma` y `agregar_circuitos` automáticos directamente en `panel.Circuits`.
- `src/AdElec.UI/ViewModels/PanelViewModel.cs`:
  - Limpieza de diagnósticos residuales (`Diagnostics.Clear()`) al cambiar de tablero o conmutar de plano DWG para evitar diagnósticos desfasados.
- Tests y Verificaciones:
  - 13/13 tablas de catálogo verificadas contra los PDFs oficiales de la AEA (`python aea/herramientas/verificar_catalogo.py`).
  - 10/10 tests unitarios en Rust aprobados (`cargo test`).
  - 8/8 tests xUnit en .NET aprobados en 65 ms (`dotnet test`).
  - Compilación de `AdElec.sln` con 0 errores y binarios sincronizados en `deploy/`.

#### 💡 Decisiones técnicas
- En entornos host de AutoCAD (`acad.exe`), los plugins no residen en el directorio de la aplicación anfitriona sino en carpetas de extensiones/plugins de usuario o red. Resolver las DLLs nativas FFI a través de la ubicación del ensamblado invocador garantiza portabilidad universal sin requerir variables de entorno de sistema.

#### 📌 Próximos pasos pendientes
- Integración en producción durante la próxima sesión de modelado en AutoCAD y pruebas en DWGs reales complejos.

---

### [2026-10-25] — Integración Visual de Diagnósticos y Quick-Fixes en AutoCAD (ADE_PANEL), Motor Multi-Norma en Rust y Exposición MCP
**Tipo:** `Feature`
**Autor / Asistente:** Antigravity / Usuario

#### 🎯 Objetivo
- Incorporar visualización interactiva y reactiva de diagnósticos normativos en la paleta de gestión de tableros de AutoCAD (`ADE_PANEL`).
- Implementar la ejecución de Quick-Fixes directos sobre los circuitos en el plano DWG con un solo clic.
- Completar la lógica de evaluación especializada para AEA 771 (locales y oficinas comerciales) y AEA 772 (partes comunes de edificios) dentro del núcleo Rust y exponerla en .NET y MCP.

#### 🛠️ Cambios realizados
- `aea/core/` (Rust):
  - `src/catalogo.rs`: Añadidos `Catalogo771` y `Catalogo772` embebidos en el binario sin dependencias de disco.
  - `src/aea771.rs`: Implementado cálculo de grado comercial (Tabla 771.8.IV) y validación de circuitos comerciales (Tabla 771.7.I: IUE, TUE, APM, etc., con límites de bocas e intensidades).
  - `src/aea772.rs`: Implementadas dotaciones mínimas de circuitos para partes comunes según superficie (Tabla 772.8.I) y requerimiento de separación en circuitos de iluminación fija y automática.
  - `src/ffi.rs`: Agregado despacho dinámico multi-norma en `aea_validar_unidad` y exportación de `aea_grado_electrificacion_comercial`.
  - Recompilados binarios de producción: `aea_core.dll` (nativo C-ABI) y `aea_core.wasm` (WebAssembly para OpenCADStudio).
  - Tests unitarios en Rust ampliados a 9/9 superados.
- `src/AdElec.Core/Aea/`:
  - `AeaBridge.cs`: Agregado soporte de campo `Norma` en `AeaUnidadDto` y método `CalcularGradoComercial`.
  - `PanelAeaExtensions.cs`: Detección contextual de norma según `TipoTablero` ("AEA-771" para comerciales, "AEA-772" para partes comunes, "AEA-770" residencial) y método de extensión `panel.AplicarCorreccion(diagnostico, correccion)` para aplicar el Quick-Fix modificando en vivo las propiedades del circuito.
- `src/AdElec.UI/`:
  - `PanelViewModel.cs`: Colección observable `Diagnostics`, comandos `CalcularAeaCommand` y `AplicarCorreccionCommand`, validación in-process de alta velocidad en lugar de depender de servidores HTTP externos.
  - `MainPaletteView.xaml`: Integrado panel retráctil de diagnósticos normativos en tiempo real en la pestaña de Circuitos con badges de severidad (🔴 Error, 🟡 Alerta), citas a la norma/tabla/página de la AEA y botón interactivo `⚡ Solucionar` (Quick-Fix) que corrige calibres o secciones de cable al instante.
  - `Converters`: Registrado `IntToVisibilityConverter` en los recursos del control para control de visibilidad condicional.
- `tests/AdElec.Core.Tests/`:
  - Ampliadas pruebas de integración xUnit a 6/6 superadas (probando cálculo de grado comercial, validación multi-norma con circuitos IUE y aplicación efectiva de Quick-Fix en memoria).
- `aea/mcp/server.py`:
  - Agregada la herramienta `aea_grado_comercial` y soporte multi-norma en `aea_validar_tablero`.
- `deploy/`:
  - Copiada la biblioteca de enlace dinámico `aea_core.dll` optimizada junto a los ensamblados de despliegue.

#### 💡 Decisiones técnicas
- La aplicación de Quick-Fixes opera directamente sobre el modelo de datos de `Circuit` en el `Panel`, permitiendo que el proyectista resuelva no conformidades (como sobrecarga de térmica respecto a la sección admisible del cable) con un solo clic, recalculando y guardando los cambios en los atributos del dibujo sin fricción.
- Se mantiene 100% de aislamiento entre el motor normativo y las bibliotecas propietarias de CAD: la UI interactúa a través de abstracciones limpias en `AdElec.Core.Aea`, lo que asegura portabilidad idéntica a Revit y OpenCADStudio.

#### 📌 Próximos pasos pendientes
- Integrar la barra de herramientas del editor CAD para activar la visualización coloreada de circuitos en el plano DWG según su estado de cumplimiento.

### [2026-10-25] — Integración Completa del Motor Normativo AEA: FFI C#, WebAssembly, Servidor MCP y Soporte Multi-Norma (770 / 771 / 772)
**Tipo:** `Feature`
**Autor / Asistente:** Antigravity / Usuario

#### 🎯 Objetivo
- Culminar la implementación y despliegue del motor normativo universal de AEA 90364.
- Conectar el núcleo portable de Rust con los tres canales operativos: AutoCAD/Revit (.NET P/Invoke), OpenCADStudio web (WebAssembly) y Agentes de IA (servidor MCP).
- Incorporar la verificación y soporte de tablas para AEA 771 (locales y oficinas) y AEA 772 (servicios comunes de edificios).

#### 🛠️ Cambios realizados
- `aea/catalogo/`:
  - `aea-770-2017/puntos_minimos.json`: Extracción y verificación de la Tabla 770.7.III (puntos mínimos por ambiente para estar, dormitorios, cocina, baño, pasillos, vestíbulo, lavadero, balcones).
  - `aea-771-2006/`: Creadas y verificadas `tipos_circuito.json` (Tabla 771.7.I con IUE, TUE, APM, ATE, ITE) y `grados_oficinas_locales.json` (Tabla 771.8.IV con potencias máximas por kVA).
  - `aea-772-2019/`: Creadas y verificadas `tipos_circuito.json` (Tabla 772.7.I) y `circuitos_minimos_espacios_comunes.json` (Tabla 772.8.I para ascensores, bombas de incendio y circuitos de iluminación fija/automática).
  - Verificador automático (`verificar_catalogo.py`): 13/13 tablas verificadas OK contra los PDFs originales.
- `aea/core/` (Rust):
  - `catalogo.rs`, `modelo.rs`, `aea770.rs`: Integrado cálculo y validación de puntos mínimos de utilización por ambiente según grado y dimensiones.
  - `ffi.rs`: Exportadas funciones nativas C-ABI (`aea_validar_unidad`, `aea_puntos_minimos_ambiente`, `aea_grado_electrificacion`, `aea_free_string`).
  - Compilación nativa en Release: `target/release/aea_core.dll` generada.
  - Compilación WebAssembly: `target/wasm32-unknown-unknown/release/aea_core.wasm` (320 KB) generada sin dependencias.
  - Suite de tests en Rust: 7/7 pruebas unitarias pasando en 0.09s.
- `src/AdElec.Core/Aea/`:
  - `AeaBridge.cs`: Puente P/Invoke con resolución automática de la biblioteca nativa, gestión segura de memoria y serialización UTF-8.
  - `PanelAeaExtensions.cs`: Métodos de extensión para validar instancias de `Panel` y `Ambiente` de AutoCAD en vivo en < 1 ms.
- `tests/AdElec.Core.Tests/`:
  - Proyecto de pruebas xUnit (`AdElec.Core.Tests.csproj` y `AeaBridgeTests.cs`).
  - 4/4 pruebas de integración pasando en 28 ms (cálculo de grado, puntos mínimos, sobrecargas en circuitos y validación directa sobre objetos `Panel`).
- `aea/mcp/server.py`:
  - Servidor MCP compatible con JSON-RPC 2.0 stdio exponiendo las herramientas `aea_grado_electrificacion`, `aea_puntos_minimos_ambiente` y `aea_validar_tablero`.
  - Verificado con tests automatizados sobre el protocolo de herramientas.
- `docs/PLAN_MOTOR_NORMATIVO_AEA.md`:
  - Actualizado el documento maestro con la arquitectura tripartita implementada, especificación de normas y resultados de auditoría.

#### 💡 Decisiones técnicas
- El puente FFI utiliza `c_void_p` en los punteros intermedios para garantizar que la memoria asignada por Rust sea deasignada explícitamente por `aea_free_string` en el heap de Rust, eliminando fugas de memoria entre el runtime de C#/.NET o Python y Rust.
- La compilación dual (C-ABI `cdylib` + WebAssembly `wasm32`) desacopla totalmente a AD-ELEC de AutoCAD: el mismo núcleo puede calcular planos en Revit mediante C#, en OpenCADStudio en el navegador mediante WASM, y responder consultas en tiempo real a agentes de IA mediante MCP.

#### 📌 Próximos pasos pendientes
- Integrar la visualización de los diagnósticos y botones de Quick-Fix en la interfaz XAML de `ADE_PANEL` para AutoCAD.
- Configurar el archivo de manifiesto MCP en la configuración de Antigravity / OpenCADStudio para uso permanente.

### [2026-10-25] — Implementación de Núcleo Portable AEA en Rust (`aea-core`) y Verificador Automático de Catálogo contra PDFs Originales
**Tipo:** `Feature`
**Autor / Asistente:** Antigravity / Usuario

#### 🎯 Objetivo
- Eliminar la incertidumbre de si las tablas normativas fueron bien extraídas o aplicadas.
- Construir un verificador determinístico que contraste cualquier tabla JSON contra el PDF oficial de la norma (secuencias numéricas y citas textuales).
- Implementar el núcleo de cálculo y validación en Rust (`aea-core`), independiente de cualquier software CAD, compilable a librería nativa (C-ABI / P-Invoke para C# en AutoCAD/Revit), WebAssembly (para OpenCADStudio en navegador) y servidor MCP.

#### 🛠️ Cambios realizados
- `aea/catalogo/fuentes.json`: Registro de documentos normativos con SHA-256 para fijar la versión exacta de las normas AEA 770-2017, AEA 771-2006 y AEA 772-2019.
- `aea/catalogo/aea-770-2017/`: Extracción de tablas normativas con anclas de texto, cláusula, página y citas de verificación:
  - `alcance.json` (Cláusula 770.1: $\le 63\text{ A}$, $I_{cc} \le 10\text{ kA}$).
  - `tipos_circuito.json` (Tabla 770.6.I: IUG, TUG, TUE con bocas y calibres máximos).
  - `grados_electrificacion.json` (Tabla 770.7.I: límite con factor 50% semicubierta).
  - `circuitos_minimos.json` (Tabla 770.7.II: variantes a y b por grado).
  - `secciones_minimas.json` (Tabla 770.11.I: secciones por uso).
  - `iz_cables_canalizacion.json` (Tabla 770.12.I: corrientes admisibles IRAM-NM 247-3 / 62267 a 40 °C).
  - `factor_agrupamiento.json` (Tabla 770.12.II: factores de agrupamiento monofásicos y trifásicos).
  - `coordinacion_protecciones.json` (770.15.3: coordinación $I_b \le I_n \le I_z$ y relación IEC 60898-1).
- `aea/herramientas/verificar_catalogo.py`:
  - Script que contrasta secuencias de números y citas literales contra el PDF usando PyMuPDF.
  - Verificación exitosa de 8/8 tablas contra `_AEA 90364-7-770-2017.pdf`.
  - Superó test de mutación (detecta alteración de números, omisión de filas, columnas cruzadas o citas cambiadas).
- `aea/core/` (Crate Rust `aea-core`):
  - `catalogo.rs`: Embebe los JSON validados en el binario en tiempo de compilación (`include_str!`).
  - `modelo.rs`: Modelos neutros de entrada (`Circuito`, `Unidad`).
  - `diagnostico.rs`: Diagnósticos estructurados (`Error`, `Advertencia`, `Info`) con referencia a artículo/página y `Correccion` (Quick-Fix).
  - `aea770.rs`: Reglas determinísticas de alcance, grado, circuitos mínimos, coordinación $I_b \le I_n \le I_z$ con agrupamiento automático y generación de sugerencias.
  - `lib.rs`: Exposición pública y suite de tests unitarios (4/4 tests pasando en 0.01s).
- `.gitignore`: Agregada exclusión de PDFs normativos (`AEA_NORMATIVA/`, `*.pdf`) y `target/` de Rust.

#### 💡 Decisiones técnicas
- Emplear Rust para el núcleo permite portabilidad total sin depender del entorno de AutoCAD ni de un servidor web Python local: compila a DLL nativa para AutoCAD/Revit (.NET P/Invoke), WebAssembly para OpenCADStudio web, y binario CLI/MCP.
- El catálogo embebido viaja dentro del binario garantizando velocidad instantánea (0 ms) y cero problemas de rutas relativas o archivos faltantes en tiempo de ejecución.
- Cada diagnóstico transporta la referencia normativa exacta (documento, tabla, cláusula y página del PDF) para dar respaldo legal y técnico inmediato al proyectista.

#### 📌 Próximos pasos pendientes
- Incorporar la Tabla 770.7.III (puntos mínimos de utilización por ambiente).
- Exponer la interfaz FFI C-ABI / JSON para consumo directo desde `AdElec.Core` (C# / AutoCAD).
- Crear el servidor MCP (`aea-mcp`) para permitir la interacción de agentes de IA con el motor.
- Incorporar las tablas de AEA 771 (locales y oficinas) y AEA 772 (servicios comunes).

### [2026-10-25] — Propuesta de Arquitectura y Plan Integral para Motor Normativo Multi-Norma (AEA 770 / 771 / 772)
**Tipo:** `Docs` / `Refactor`  
**Autor / Asistente:** Antigravity / Usuario

#### 🎯 Objetivo
- Analizar y superar el modelo de codificación normativa puramente basado en `.json` monolítico de `AEA-MOTOR-RESTORED`.
- Diseñar una arquitectura técnica robusta, modular y de alto rendimiento que integre simultáneamente las 3 reglamentaciones AEA utilizadas: **AEA 90364-7-770 (2017)** (viviendas unifamiliares $\le 63\text{ A}$), **AEA 90364-7-771 (2006)** (viviendas, locales y oficinas) y **AEA 90364-7-772 (2019)** (servicios generales de edificios).

#### 🛠️ Cambios realizados
- `docs/PLAN_MOTOR_NORMATIVO_AEA.md`:
  - Creado documento maestro de arquitectura y roadmap.
  - Definida la arquitectura híbrida en 2 niveles: Catálogos JSON declarativos para tablas de valores + Motor de Especificaciones tipado en C# (in-process, 0 ms) + Backend FastAPI para memorias de cálculo y auditorías profundas.
  - Detallado el alcance y tratamiento diferenciado de las normas 770, 771 y 772 (viviendas unifamiliares vs colectivas vs servicios comunes como bombas, ascensores y cajas de incendio).
  - Planteado el esquema de carpetas para catálogos normativos en `src/AdElec.Core/Normativa/`.
  - Diseñado el patrón de reglas (`Specification Pattern`) con reporte de severidades y botones de corrección automática (`Quick-Fix`).

#### 💡 Decisiones técnicas
- Mantener los datos numéricos tabulares en archivos `.json` separados y versionados para que sean legibles y editables sin recompilar.
- No delegar la validación interactiva de AutoCAD a un servidor HTTP externo para evitar bloqueos o dependencias de ejecución de procesos Python durante el dibujo continuo.
- Diseñar un despachador polimórfico (`INormaStrategy`) que permita seleccionar qué norma rige cada tablero o unidad funcional (ej. departamentos con AEA 770/771 y el tablero de servicios generales con AEA 772).

#### 📌 Próximos pasos pendientes
- Iniciar la Fase 1: Creación de catálogos JSON normalizados en `src/AdElec.Core/Normativa/`.
- Implementar las clases de estrategia y validadores para 770, 771 y 772.
- Incorporar el selector de norma y estado de validación en tiempo real en la paleta `ADE_PANEL`.

### [2026-10-24] — Rediseño del Gestor de Tableros (Edición, Persistencia, Cálculo Ib/In, Superficie de Ambientes y Corrección de ComboBoxes)
**Tipo:** `Feature` / `Fix`  
**Autor / Asistente:** Antigravity / Usuario

#### 🎯 Objetivo
- Solucionar el problema visual donde los ComboBox (`DarkCombo`) colapsaban en una línea fina y no mostraban el texto seleccionado al cerrarse (falta de `MinHeight` y enlace `SelectionBoxItem`).
- Convertir la pestaña Tablero en un gestor completo, persistente y editable (crear, editar en vivo, guardar y eliminar tableros del DWG).
- Corregir la definición y rotulado de fases: Monofásico (2P / 1F+N) y Trifásico (4P / 3F+N), eliminando textos confusos como "1 Fases".
- Incorporar el cálculo automático de superficie sumando las áreas de los recintos (`IAmbienteRepository`) correspondientes al tablero.
- Implementar la estimación de corriente de empleo simultánea $I_b$ y el calibre comercial sugerido del interruptor principal $I_n$ según AEA 90364-771.
- Resolver la sincronización de circuitos al seleccionar un tablero: asegurar que `SelectedPanel` sea una instancia viva de la colección observable para que la tabla de circuitos se actualice de inmediato.

#### 🛠️ Cambios realizados
- `src/AdElec.Core/Models/Panel.cs`:
  - Implementado `INotifyPropertyChanged` con `SetField` reactivo.
  - Añadidas propiedades calculadas: `PhaseDescription`, `CalculatedIbAmps` ($I_b$ en 220V o 380V con factor de simultaneidad) y `SuggestedBreakerAmps` ($I_n$ normalizado).
- `src/AdElec.UI/Views/MainPaletteView.xaml`:
  - Corregido el estilo `DarkCombo` con `MinHeight="30"`, `VerticalContentAlignment="Center"` y `Content="{TemplateBinding SelectionBoxItem}"`.
  - Rediseñada la tarjeta de tablero activo para permitir edición directa de `Nombre`, `Tipo`, `Fases`, `Ubicación`, `Superficie` e `In`.
  - Agregada barra de cálculo de demanda reglamentaria y botones `+ Nuevo`, `🗑 Borrar`, `💾 Guardar Tablero` y `↺ Ambientes`.
- `src/AdElec.UI/ViewModels/PanelViewModel.cs`:
  - Agregados comandos `GuardarTableroCommand`, `EliminarTableroCommand` y `CalcularSuperficieCommand`.
  - Método `CalcularSuperficieDesdeAmbientes()` que totaliza m² desde la UF o todos los recintos del plano.
  - Corregido `RefreshFromRepo` para vincular directamente la instancia seleccionada con la colección `Panels` y disparar `RefreshCircuits()`.
- `deploy/`:
  - Binarios actualizados y copiados.

#### 💡 Decisiones técnicas
- En WPF, un `ComboBox` personalizado sin `SelectionBoxItem` en su `ContentPresenter` no renderiza el elemento seleccionado en la caja cerrada. Añadirlo junto a `MinHeight` garantiza dimensiones estables y legibilidad idéntica a los campos de texto (`DarkInput`).
- El cálculo preliminar de $I_b$ y calibre de protección general le otorga al usuario una referencia técnica inmediata sin necesidad de esperar la validación en la nube.

### [2026-10-24] — Corrección de Recurso XAML en Paleta (AdeExpander)
**Tipo:** `Fix`  
**Autor / Asistente:** Antigravity / Usuario

#### 🎯 Objetivo
- Resolver la excepción `System.Windows.StaticResourceExtension` en línea 601 de [MainPaletteView.xaml](file:///g:/PROYECTOS%20AD/AD-ELEC/src/AdElec.UI/Views/MainPaletteView.xaml) al ejecutar el comando `ADE_PANEL`.

#### 🛠️ Cambios realizados
- `src/AdElec.UI/Views/MainPaletteView.xaml`:
  - Corregido el estilo del Expander de acciones de tablero: reemplazado `DarkExpander` por la clave declarada existente `AdeExpander`.
  - Verificada la consistencia de todos los `StaticResource` del archivo con 0 referencias faltantes.

#### 💡 Decisiones técnicas
- La excepción en runtime de WPF se producía al no encontrar la clave de recurso estático durante la instanciación de la vista. Se validó la totalidad de recursos mediante script automatizado.

#### 📌 Próximos pasos pendientes
- Reiniciar sesión de AutoCAD para liberar las DLLs en memoria y cargar la versión compilada sin bloqueos.

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
