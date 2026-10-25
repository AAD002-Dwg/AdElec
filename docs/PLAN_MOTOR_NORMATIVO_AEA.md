# Plan Integral y Arquitectura: Motor Normativo Portable AEA (770 / 771 / 772)

> **Fecha de Actualización:** 25 de Octubre de 2026  
> **Estado:** Implementado, Verificado y en Producción  
> **Objetivo:** Definir y consolidar la arquitectura técnica del motor normativo universal de **AEA 90364** (770-2017, 771-2006 y 772-2019), independiente de cualquier software CAD específico, capaz de ejecutarse en **AutoCAD / Revit** (.NET C-ABI), en **OpenCADStudio / Navegadores Web** (WebAssembly) y de ser operado por **Agentes de IA** (Model Context Protocol - MCP).

---

## 1. Arquitectura de 3 Niveles Desacoplados

Para evitar la duplicación de código, inconsistencias entre plataformas y dependencias de servidores externos pesados, el sistema se estructura en tres componentes concéntricos:

```
+---------------------------------------------------------------------------------+
|                       CATÁLOGOS NORMATIVOS VERIFICADOS (.JSON)                  |
|             Tablas con anclas, citas literales y hash SHA-256 de los PDFs       |
|                 (AEA 770-2017, AEA 771-2006, AEA 772-2019, IRAM 2183)           |
+---------------------------------------------------------------------------------+
                                         |
                                         v
+---------------------------------------------------------------------------------+
|                         NÚCLEO DETERMINÍSTICO (Rust: aea-core)                  |
|                   Catálogo embebido en tiempo de compilación (0 ms)             |
|                 Cálculo de grados, coordinación Ib <= In <= Iz, bocas mínimas   |
|               Diagnósticos tipo Roslyn con Severidad y propuestas de Quick-Fix  |
+---------------------------------------------------------------------------------+
         |                                |                               |
         v                                v                               v
+------------------+             +------------------+            +------------------+
|  C-ABI / DLL     |             |  WebAssembly     |            |  Servidor MCP    |
| (P/Invoke .NET)  |             |  (wasm32)        |            | (JSON-RPC stdio) |
|------------------|             |------------------|            |------------------|
| * AutoCAD        |             | * OpenCADStudio  |            | * Asistentes AI  |
| * Revit          |             |   Web            |            | * OpenCADStudio  |
| * AD-ELEC (C#)   |             | * Navegador puro |            |   Desktop / CLI  |
+------------------+             +------------------+            +------------------+
```

---

## 2. Blindaje y Verificación de Tablas contra los PDFs Originales

Para erradicar la incertidumbre sobre si una tabla fue bien transcripta o alterada:
1. **Registro SHA-256 (`aea/catalogo/fuentes.json`):**  
   Fija el hash criptográfico exacto de los PDFs oficiales utilizados (`_AEA 90364-7-770-2017.pdf`, `_AEA 90364-7-771-2006.pdf`, `_AEA-90364-7-772-2019.pdf`). Los PDFs están excluidos del versionado Git por derechos de autor.
2. **Herramienta de Verificación Automática (`aea/herramientas/verificar_catalogo.py`):**  
   Extrae el texto de las páginas correspondientes y realiza dos comprobaciones automáticas:
   - **Secuencia Numérica:** Comprueba que todos los números de la tabla existan en el PDF en el orden exacto.
   - **Citas Literales:** Valida que el texto normativo de condición aparezca textualmente.
   - **Test de Mutación:** Valida que ante cualquier alteración de valores, columnas cruzadas o filas borradas, el verificador falle inmediatamente.
   - **Estado Actual:** **13/13 tablas verificadas OK (100%)**.

---

## 3. Especificación Normativa Implementada

### A. AEA 90364-7-770 (Edición 2017) — Viviendas Unifamiliares $\le 63\text{ A}$
- **Ámbito:** Casas unifamiliares, PHs y unidades de vivienda aisladas ($I_{n} \le 63\text{ A}$, $I_{cc} \le 10\text{ kA}$). Si se supera este límite, el motor emite un diagnóstico bloqueante con Quick-Fix para conmutar a AEA 771.
- **Grados de Electrificación:** Mínimo ($\le 60\text{ m}^2$), Medio ($60$ a $130\text{ m}^2$), Elevado ($130$ a $200\text{ m}^2$) y Superior ($> 200\text{ m}^2$). Se computa la superficie cubierta más el 50% de la semicubierta.
- **Circuitos Mínimos:** Variantes a) y b) reglamentarias para IUG, TUG y libre elección.
- **Puntos Mínimos por Ambiente (Tabla 770.7.III):** Cálculo de bocas para Estar/Comedor (1 IUG cada 18 m², 1 TUG cada 6 m²), Dormitorios (<10 m², 10-36 m², >36 m²), Cocinas (según grado, con módulos para electrodomésticos fijos), Baños, Pasillos, Vestíbulos, Lavaderos y Balcones.
- **Coordinación de Protecciones (770.15.3):** Cumplimiento estricto de $I_b \le I_n \le I_z$, donde $I_z$ se corrige dinámicamente según el factor de agrupamiento de la canalización (Tabla 770.12.II).

### B. AEA 90364-7-771 (Edición 2006) — Viviendas Colectivas, Locales y Oficinas
- **Tipos de Circuitos:** Incorporación de IUE (12 bocas, 32 A), TUE (12 bocas, 32 A), APM (15 bocas, 25 A), ATE (15 bocas) e ITE (12 bocas por fase).
- **Oficinas y Locales:** Grados de electrificación por kVA calculados (Mínimo $\le 30\text{ m}^2$ y $4.5\text{ kVA}$, Medio $\le 75\text{ m}^2$ y $7.8\text{ kVA}$, Elevado $\le 150\text{ m}^2$ y $12.2\text{ kVA}$, Superior $> 150\text{ m}^2$).

### C. AEA 90364-7-772 (Edición 2019) — Servicios Generales y Partes Comunes
- **Espacios Comunes:** Obligatoriedad de bifurcar circuitos en "Iluminación Fija" e "Iluminación Automática" para medios de circulación.
- **Circuitos Mínimos:** Tabla 772.8.I según superficie común ($\le 30\text{ m}^2$: 3 circuitos; $30-75\text{ m}^2$: 4 circuitos; $> 75\text{ m}^2$: 6 circuitos).
- **Servicios Esenciales:** Circuitos independientes obligatorios para Ascensores (conforme AEA 791) y Bombas de Incendio / Elevadoras.

---

## 4. Canales de Integración

### 1. Integración en C# (.NET / AutoCAD / Revit)
- **Biblioteca Nativa:** `aea_core.dll` compilada en Release.
- **Puente P/Invoke (`AdElec.Core.Aea.AeaBridge`):** Carga dinámica de la biblioteca, manejo seguro de memoria y serialización UTF-8.
- **Extensiones de Modelo (`PanelAeaExtensions`):**  
  Permite validar directamente cualquier instancia de `Panel` con:
  ```csharp
  List<AeaDiagnostic> diags = panel.ValidarNormativaAea(ambientes);
  ```
  La ejecución tarda **< 1 ms**, permitiendo validación continua durante el dibujo en AutoCAD sin bloqueos.

### 2. Integración Web / OpenCADStudio (WebAssembly)
- **Binario WASM:** `aea_core.wasm` (320 KB), compilado para `wasm32-unknown-unknown`.
- Se ejecuta directamente en el runtime JavaScript/Rust de OpenCADStudio en el navegador sin enviar datos a la red.

### 3. Integración con Agentes de IA (MCP Server)
- **Servidor:** `aea/mcp/server.py` implementando JSON-RPC 2.0 sobre `stdio`.
- **Herramientas disponibles:**
  - `aea_grado_electrificacion`: Determina grado residencial y superficie computable (AEA 770).
  - `aea_grado_comercial`: Determina grado comercial y límite de kVA (AEA 771).
  - `aea_puntos_minimos_ambiente`: Calcula bocas IUG/TUG mínimas por habitación (Tabla 770.7.III).
  - `aea_validar_tablero`: Audita el tablero completo y devuelve diagnósticos con correcciones aplicables (multi-norma 770/771/772).

### 4. Interfaz de Usuario en AutoCAD (`ADE_PANEL`)
- **Panel Reactivo de Diagnósticos:** Integrado directamente en la pestaña de Circuitos de `MainPaletteView.xaml`. Muestra badges de severidad (🔴 Error, 🟡 Alerta), cita formal a la norma/tabla/página de la AEA y mensaje claro.
- **Botón Quick-Fix interactivo:** Un botón `⚡ Solucionar` permite adoptar la corrección sugerida (ej. corregir sección de cable a 2.5 mm² o ajustar calibre de protección a 20 A) con un solo clic, actualizando el tablero y recalculando automáticamente.

---

## 5. Pruebas y Garantía de Calidad

- **Tests de Catálogo:** Verificación automática de 13/13 tablas contra texto de PDF (100% aprobado).
- **Tests Unitarios en Rust:** 9 pruebas unitarias en `aea-core` (0.01 s).
- **Tests de Integración en C#:** 6 pruebas xUnit en `AdElec.Core.Tests` verificando P/Invoke, cálculo de grado residencial y comercial, puntos mínimos, sobrecargas, validación multi-norma con IUE y aplicación efectiva de Quick-Fix en memoria (38 ms).
- **Tests MCP:** Suite de pruebas JSON-RPC validando llamadas y respuestas de herramientas (100% aprobado).
