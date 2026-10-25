# Plan Integral: Motor Normativo AEA (770 / 771 / 772) para AD-ELEC

> **Fecha:** 25 de Octubre de 2026  
> **Estado:** Propuesta Técnica y Plan de Arquitectura  
> **Objetivo:** Definir la mejor estrategia técnica para codificar, estructurar y ejecutar las normas **AEA 90364-7-770 (2017)**, **AEA 90364-7-771 (2006)** y **AEA 90364-7-772 (2019)** de forma armónica entre **AD-ELEC (AutoCAD / C#)** y **AEA-MOTOR (Python / FastAPI)**.

---

## 1. Diagnóstico del Enfoque Original (.JSON Puro)

### Lo que funcionó bien
- **Formato universal y editable:** Separar los datos numéricos (tablas de corrientes $I_z$, tablas de potencias mínimas, grados de electrificación) del código fuente fue una decisión acertada.
- **Intercambiabilidad:** Permite que un backend web (FastAPI) y un cliente CAD (AutoCAD) hablen el mismo idioma sin acoplamiento binario.

### Los límites del "Solo JSON"
1. **La normativa no es solo una tabla; es un árbol de decisión:**
   - Hay dependencias condicionales que un JSON plano no puede resolver sin un motor de interpretación.
   - *Ejemplo:* Si una vivienda unifamiliar tiene $I_n \le 63\text{ A}$, aplica **AEA 770**; pero si supera 63 A o si el inmueble pasa a tener uso comercial, la norma 770 **deja de aplicar** y conmuta a **AEA 771**.
   - Intentar meter lógica condicional en JSON termina en la creación de un "DSL" (lenguaje propio de reglas en JSON) que es lento, propenso a errores y muy difícil de depurar.
2. **Dependencia operativa en el dibujo diario:**
   - Si la validación normativa depende 100% de llamar vía HTTP a `localhost:8000` (FastAPI), el usuario de AutoCAD se ve obligado a levantar servidores Python en segundo plano (`Iniciar_Proyecto.bat`). Si el servidor se cae, el CAD pierde la asistencia inteligente o se congela por timeouts de red.

---

## 2. ¿Existe una manera mejor? Sí: Arquitectura Híbrida de 2 Niveles

La mejor manera de resolver esto en la industria de software de ingeniería (como hacen Revit, Caneco o CYPE) es una **Arquitectura Híbrida Desacoplada**:

```
+--------------------------------------------------------------------------+
|                        CATÁLOGOS NORMATIVOS (.JSON)                      |
|           Tablas normalizadas de valores, coeficientes y límites         |
|             (770-2017, 771-2006, 772-2019, IRAM 2183/62267)              |
+--------------------------------------------------------------------------+
                                    |
          +-------------------------+-------------------------+
          |                                                   |
          v                                                   v
+-----------------------------------+   +----------------------------------+
|   NIVEL 1: MOTOR IN-PROCESS (C#)  |   |  NIVEL 2: MOTOR DE CÁLCULO (WEB) |
|      (Embebido en AD-ELEC)        |   |           (AEA-MOTOR)            |
|-----------------------------------|   |----------------------------------|
| * Ejecución instantánea (0 ms)    |   | * Memoria técnica completa       |
| * Validación en vivo en ADE_PANEL |   | * Cortocircuito (Icc) de red     |
| * Bocas mínimas en ambientes      |   | * Caída de tensión compleja      |
| * Coordinación Ib <= In <= Iz     |   | * Exportación formal PDF / Web   |
| * 100% Autónomo (sin Python)      |   | * Auditoría profunda del diseño  |
+-----------------------------------+   +----------------------------------+
```

---

## 3. Especificación de las 3 Normas en el Sistema

### A. AEA 90364-7-770 (Edición 2017) — Viviendas Unifamiliares $\le 63\text{ A}$
- **Ámbito:** Casas unifamiliares, PHs y unidades de vivienda aisladas con corriente de entrada máxima de $63\text{ A}$ y corrientes de cortocircuito presunto $I_{cc} \le 10\text{ kA}$.
- **Grados de Electrificación:**
  - *Mínimo:* hasta $60\text{ m}^2$ (Mínimo 2 circuitos: 1 IUG + 1 TUG).
  - *Medio:* $>60\text{ m}^2$ hasta $130\text{ m}^2$ (Mínimo 3 circuitos: 1 IUG + 1 TUG + 1 IUG/TUG/TUE).
  - *Elevado:* $>130\text{ m}^2$ hasta $200\text{ m}^2$ (Mínimo 5 circuitos).
  - *Superior:* $>200\text{ m}^2$ (Mínimo 6 circuitos).
- **Particularidad técnica:** Simplifica enormemente los requerimientos de la 771 para viviendas estándar, permitiendo esquemas más ágiles de tablero y selección de protecciones.

### B. AEA 90364-7-771 (Edición 2006) — Viviendas Colectivas, Locales y Oficinas
- **Ámbito:** Viviendas unifamiliares y multifamiliares (edificios de departamentos), locales comerciales y oficinas comerciales o administrativas.
- **Grados de Electrificación y Coeficientes:**
  - Para oficinas y locales comerciales, el grado no depende solo de la superficie, sino de la potencia instalada por metro cuadrado ($VA/m^2$).
  - Tablas completas de coeficientes de simultaneidad según tipo de local y número de circuitos.
  - Reglas estrictas de reserva en tableros seccionales (mínimo 20% de módulos libres).

### C. AEA 90364-7-772 (Edición 2019) — Servicios Generales y Partes Comunes
- **Ámbito:** Espacios y servicios comunes de edificios en propiedad horizontal o conjuntos inmobiliarios.
- **Clasificación de Servicios:**
  - **Fuerza Motriz Sanitaria:** Bombas de agua potable (elevación y presurizadoras), bombas pluviales y cloacales (achique).
  - **Fuerza Motriz de Transporte:** Ascensores, montacargas, rampas vehiculares.
  - **Seguridad Contra Incendio:** Bombas de incendio, presurización de cajas de escalera, extractores de humo (líneas con protección diferencial especial o supresión de disparo directo según directiva de seguridad).
  - **Iluminación de Espacios Comunes:** Palieres, accesos, cocheras, salas de máquinas, luces de emergencia (autónomas o centralizadas).
  - **Servicios Centrales:** Portones automáticos, CCTV, portería eléctrica / intercomunicadores.

---

## 4. Estructura de Archivos Propuesta

Organizamos la normativa en catálogos estructurados `.json` accesibles por ambos sistemas, ubicados en `src/AdElec.Core/Normativa/`:

```text
src/AdElec.Core/Normativa/
├── catalogs/
│   ├── cables_iram2183_62267.json     # Secciones, diámetros ext, Iz según método A1, B1, B2, C
│   ├── protecciones_modulares.json     # Calibres estándar ITM (6, 10, 16, 20, 25, 32, 40, 50, 63A) e ID (25, 40, 63A)
│   └── canerias_ocupacion.json         # Diámetro caño (RS, RL, semipesado) según cant. cables
├── aea_770_2017/
│   ├── grados_electrificacion.json
│   ├── circuitos_minimos.json
│   └── puntos_minimos_vivienda.json
├── aea_771_2006/
│   ├── grados_electrificacion.json
│   ├── circuitos_minimos.json
│   ├── puntos_minimos_vivienda.json
│   ├── puntos_minimos_locales.json
│   ├── puntos_minimos_oficinas.json
│   └── simultaneidad_tablas.json
└── aea_772_2019/
    ├── servicios_clasificacion.json    # FM_BOMBAS, FM_ASCENSORES, FM_INCENDIO, LUZ_COMUN, etc.
    └── reglas_tableros_comunes.json   # Requisitos para TSSG (Tablero Seccional Servicios Generales)
```

---

## 5. El Patrón de Software en C# (Specification Pattern + Quick-Fixes)

En lugar de validar mediante funciones gigantescas con decenas de `if-else`, se implementa el **Specification Pattern** con reglas atómicas. Cada regla reporta:
1. **Identificador del Artículo:** Ej. `AEA-770.8.2.1`.
2. **Severidad:**
   - 🔴 `BLOQUEANTE` (Error normativo grave: ej. conductor de 1.5 mm² con térmica de 25A).
   - 🟡 `ADVERTENCIA` (Omisión o desvío menor: ej. falta circuito de reserva en tablero).
   - 🔵 `INFORMATIVO` (Sugerencia técnica de optimización).
3. **Acción Correctiva Sugerida (Quick-Fix):**
   - El sistema no solo avisa el error, sino que ofrece el botón:  
     `[Ajustar ITM a 16A]` o `[Subir cable a 4.0 mm²]`.

### Ejemplo de Diseño de la Interfaz:
```csharp
public interface INormativeRule<TEntity>
{
    string RuleId { get; }            // "AEA770-8.2.a"
    string Norma { get; }             // "AEA 90364-7-770"
    string Descripcion { get; }
    RuleSeverity Severity { get; }

    RuleEvaluationResult Evaluate(TEntity entity, ValidationContext context);
}
```

---

## 6. Plan de Implementación Paso a Paso (Roadmap)

### Fase 1: Creación de Catálogos JSON Normalizados
- Consolidar las tablas de corrientes admisibles ($I_z$) y cañerías estándar en archivos JSON limpios.
- Extraer las matrices de ambientes y puntos mínimos para 770, 771 y 772.

### Fase 2: Motor Polimórfico en `AdElec.Core`
- Implementar la interfaz `INormaStrategy` y las tres clases especializadas:
  - `Aea770Strategy`
  - `Aea771Strategy`
  - `Aea772Strategy`
- Crear el despachador de normas automático según el tipo de tablero seleccionado en `ADE_PANEL` o la asignación del proyecto.

### Fase 3: Integración en la Interfaz de Usuario (`ADE_PANEL` y `ADE_AMBIENTES`)
- Agregar en la cabecera del tablero un ComboBox con la norma aplicable:
  - `Vivienda Unifamiliar (AEA 770 / 2017)`
  - `Vivienda / Oficinas / Locales (AEA 771 / 2006)`
  - `Servicios Generales Edificio (AEA 772 / 2019)`
- Mostrar badges de estado en tiempo real (Iconos verde, amarillo y rojo) para cada circuito.
- Incorporar el cálculo en tiempo real de corriente de cálculo ($I_b$), calibre de térmica ($I_n$) y capacidad del cable ($I_z$).

### Fase 4: Sincronización con AEA-MOTOR (Python / FastAPI)
- Mantener los endpoints de AEA-MOTOR compatibles con los mismos esquemas JSON.
- Incorporar botón en CAD: **"Generar Memoria de Cálculo AEA"** que envía el estado del DWG a AEA-MOTOR para emitir la memoria técnica y el informe de cálculo oficial.

---

## 7. Beneficios Directos de este Plan
1. **Velocidad inmediata:** El proyectista diseña en AutoCAD sin retrasos ni dependencias externas.
2. **Mantenibilidad absoluta:** Si mañana la AEA publica una nueva edición o tabla, solo se modifica o agrega el archivo JSON de la tabla correspondiente sin romper el código.
3. **Multi-norma real:** Permite que un mismo edificio tenga departamentos calculados con AEA 770/771, cocheras o locales con AEA 771, y la sala de bombas y servicios comunes con AEA 772.
