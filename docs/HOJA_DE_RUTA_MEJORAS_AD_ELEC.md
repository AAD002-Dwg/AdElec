# 🗺️ Hoja de Ruta Maestra y Plan de Mejoras — AD-ELEC

**Versión:** 2.0  
**Fecha:** 2026-10-25  
**Estado:** En ejecución  

---

## 🎯 Visión General

El objetivo de **AD-ELEC** es constituirse en la herramienta integral definitiva de diseño, dimensionamiento, auditoría y documentación de instalaciones eléctricas residenciales, comerciales e industriales en entornos CAD (AutoCAD, y a futuro Open CAD Studio / Revit), con estricta conformidad a la reglamentación argentina **AEA 90364** (Partes 770, 771 y 772).

---

## 🏗️ Dimensiones de Mejora y Frentes de Trabajo

### 1. ⚡ Física y Cálculo Eléctrico Riguroso
- [x] **Cálculo de Corriente Admisible ($I_z$):** Tablas oficiales de cables en cañería embutida / a la vista con corrección por agrupamiento y temperatura.
- [x] **Coordinación de Protecciones ($I_B \le I_n \le I_z$):** Verificación de sobrecarga para interruptores termomagnéticos (PIA).
- [ ] **Caída de Tensión ($\Delta V\%$):**
  - Cálculo analítico reglamentario monofásico ($2 \cdot L \cdot I \cdot \cos\varphi / (\gamma \cdot S)$) y trifásico ($\sqrt{3} \cdot L \cdot I \cdot \cos\varphi / (\gamma \cdot S)$) considerando conductividad del cobre a $70^\circ\text{C}$ ($\gamma \approx 47.17\,\text{m}/(\Omega\cdot\text{mm}^2)$).
  - Verificación contra límites normativos:
    - $\le 3.0\%$ en circuitos de iluminación (IUG/IUE).
    - $\le 5.0\%$ en tomacorrientes y usos específicos (TUG/TUE/APM).
    - $\le 1.0\%$ en líneas principales y seccionales.
  - Quick-Fix automático: cálculo de la sección mínima necesaria para cumplir con $\Delta V\%$.
- [ ] **Verificación Térmica ante Cortocircuito ($I_{cc}$):**
  - Comprobación de energía específica pasante: $I^2 \cdot t \le k^2 \cdot S^2$ ($k = 115$ para PVC / Cu).
  - Capacidad de ruptura mínima de interruptores automáticos según ubicación (mínimo $3\,\text{kA}$ o $4.5\,\text{kA}$ según AEA 770).

---

### 2. 📐 Automatización CAD y Visión Espacial en DWG
- [x] **Detección Geométrica de Recintos (`RoomDetector`):** Reconocimiento de polígonos de ambientes y cálculo de superficie neta.
- [x] **Inspector de Tableros (`ADE_PANEL`):** Paleta interactiva WPF con árbol de tableros, circuitos y diagnósticos normativos en tiempo real.
- [ ] **Auditoría Espacial de Bocas (`ADE_AUDITORIA_BOCAS`):**
  - Contabilización automática de bloques de tomas e iluminación dentro del contorno poligonal de cada ambiente.
  - Comparación contra la Tabla 770.7.III de puntos mínimos de utilización.
  - Marcado visual interactivo en el DWG con círculos de advertencia o textos de apoyo en capas dedicadas (`ELEC-REVISIONES`).
- [ ] **Trazado Automático y Enrutamiento de Cañerías:**
  - Generación de polilíneas de interconexión inteligente entre bocas de techo y llaves de pared respetando tabiques y puertas.
  - Estimación automática de longitud real de cable ($L = L_{\text{plano}} + 2 \times \text{altura subida/bajada}$).

---

### 3. 📄 Generación de Documentación y Planillas Reglamentarias
- [ ] **Inserción de Tabla de Cargas en DWG (`ADE_TABLA_CARGAS`):**
  - Creación de una entidad nativa `Autodesk.AutoCAD.DatabaseServices.Table` con formato profesional.
  - Columnas completas: N° Circuito, Designación, Tensión, Cantidad de Bocas, $P$ (W), $S$ (VA), $I_B$ (A), Calibre PIA $I_n$ (A), Sección Fase/PE ($mm^2$), Longitud (m), $\Delta V\%$, Estado AEA.
  - Resumen de tablero: Grado de Electrificación, Potencia Máxima Simultánea, Factor de Simultaneidad ($F_s$), Intensidad de Acometida, y Sección del Alimentador.
- [ ] **Exportación a Formatos Externos:**
  - Exportación directa a Excel (`.xlsx`) y CSV compatible con memorias técnicas de Edenor, Edesur y colegios de ingenieros.

---

### 4. 🌐 Compatibilidad Multiplataforma y Open CAD Studio (OCS)
- [x] **Núcleo Aislado en Rust (`aea-core`):** Sin dependencias de Windows ni de AutoCAD en el motor normativo.
- [x] **Compilación WebAssembly (`target/wasm32-unknown-unknown/release/aea_core.wasm`):** Listo para correr en navegadores.
- [ ] **Binding Rust Nativo para OCS (`aea-ocs`):**
  - Exposición de tipos directos en Rust para que Open CAD Studio pueda ejecutar la validación eléctrica como un plugin nativo de OCS sin sobrecarga FFI ni serialización JSON repetitiva.
- [ ] **Servidor MCP de Asistencia Inteligente (`aea/mcp/`):**
  - Exposición de herramientas FastMCP para que agentes de IA asistan al proyectista en el cálculo de cargas y optimización de tableros.

---

## ⏱️ Cronograma de Ejecución Inmediata

| Hito | Alcance Técnico | Prioridad |
|---|---|---|
| **Hito 1** | Implementación de Caída de Tensión ($\Delta V\%$) en Rust Core, FFI y Quick-Fix | Alta (Inmediata) |
| **Hito 2** | Comando AutoCAD `ADE_TABLA_CARGAS` para inserción de tabla en plano | Alta (Inmediata) |
| **Hito 3** | Comando AutoCAD `ADE_AUDITORIA_BOCAS` cruzando bloques vs. mínimos AEA | Media-Alta |
| **Hito 4** | Suite de Pruebas Unitarias de Caída de Tensión y Regeneración de Binarios | Alta |
