# Reglas y Guías de Desarrollo — AD-ELEC

## 📌 Protocolo de Bitácora y Registro de Avances (Obligatorio)

Al trabajar en este proyecto, cualquier asistente de IA debe seguir este protocolo:

1. **Ubicación de la bitácora:**
   - Archivo: `bitacora/bitacora.md`

2. **Cuándo actualizar:**
   - Al completar una tarea, sesión de trabajo, refactorización relevante, resolución de bug o cambio estructural en el repositorio.
   - Si una tarea compleja se divide en pasos, registrar el hito al concluir los cambios en el código.

3. **Formato obligatorio de la entrada:**
   Cada nueva entrada debe agregarse arriba de las anteriores bajo la sección `## 📜 Historial de Registros`:
   ```markdown
   ### [YYYY-MM-DD] — <Título descriptivo del avance o hito>
   **Tipo:** `Refactor` | `Feature` | `Fix` | `Limpieza` | `Docs`
   **Autor / Asistente:** Antigravity / Usuario

   #### 🎯 Objetivo
   Breve descripción de la necesidad o meta técnica.

   #### 🛠️ Cambios realizados
   - Detalle claro de modificaciones en archivos, métodos o carpetas.

   #### 💡 Decisiones técnicas
   Justificación de decisiones clave o consideraciones de arquitectura.

   #### 📌 Próximos pasos pendientes
   - Tareas pendientes que quedaron abiertas para continuar después.
   ```

4. **Reglas generales del proyecto:**
   - Mantener rutas y carpetas en minúsculas y sin espacios (`src/`, `lisp/`, `blocks/`, `docs/`, `bitacora/`, `deploy/`).
   - El código en C# debe mantener compatibilidad con las versiones de AutoCAD soportadas (AutoCAD 2024 / net48 o net8.0 según corresponda).
   - Las rutinas AutoLISP propias se ubican en `lisp/`, mientras que librerías externas o utilidades de terceros van en `lisp/vendor/`.
