# Fase 3: Lógica Eléctrica, Circuitos y Cargas

La Fase 3 se centra en la transferencia de la inteligencia eléctrica desde AutoCAD hacia el motor de cálculo de **AEA-MOTOR**.

## Objetivos de Implementación

### 1. Sincronización de Tableros y Protecciones
- **Vínculo de Tableros**: Mapear los bloques `TABLERO_GENERAL` con el objeto `Board` del backend.
- **Esquema Unifilar**: Enviar la configuración de protecciones (TM, ID) detectadas en los atributos del bloque de AutoCAD.

### 2. Mapeo de Circuitos y Potencias
- **Atributos de Boca**: Escanear todos los bloques de luminarias y tomas para extraer sus atributos `CX` (Circuito) y `POT` (Potencia).
- **Asociación de Cargas**: Agrupar los puntos eléctricos por su ID de circuito y sumarlos en la Web para validar la caída de tensión y el calibre de los conductores.
- **Validación Normativa**: Al enviar los datos, el motor web validará automáticamente si el tipo de circuito (IUG, TUG, etc.) y su potencia cumplen con la reglamentación AEA 90364-7-770.

### 3. Generación Automática de Propuestas
- **Regeneración en CAD**: Recibir sugerencias del motor web (ej: "Aumentar número de bocas en Living") y que el plugin de AutoCAD pueda asistir en la inserción de dichos elementos.

## Estado de la Fase: 🏗️ EN EJECUCIÓN / PRÓXIMAMENTE

---

## Tareas Pendientes
- [ ] Implementar lector de bocas y circuitos en `DwgPanelRepository`.
- [ ] Actualizar el payload de sincronización para enviar la lista detallada de `Points` y `Circuits`.
- [ ] Habilitar visualización de carga por circuito en la paleta de AutoCAD.
