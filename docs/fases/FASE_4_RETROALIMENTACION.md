# Fase 4: Retroalimentación y Sincronización Inversa

La Fase 4 representa el cierre del círculo de sincronización, permitiendo que las decisiones tomadas en el entorno Web se "escriban" de vuelta en el dibujo de AutoCAD.

## Estrategia de Implementación

### 1. Sincronización Inversa (Web → CAD)
Cuando un usuario modifica un circuito o una potencia en el simulador web (`AEA-MOTOR`), el plugin de AutoCAD deberá:
- Recibir el comando de actualización.
- Buscar todos los bloques que coincidan con los IDs modificados.
- Actualizar automáticamente los atributos (`CX`, `POT`, etc.) en el dibujo para mantener la paridad de datos.

### 2. Notificaciones de Cumplimiento en Tiempo Real
- **Semáforo de Normas**: Indicar visualmente en la paleta de AutoCAD si un local está "fuera de norma" basándose en el cálculo web.
- **Auditoría UI**: Resaltar en el dibujo los elementos que requieren atención (ej: tomas demasiado cerca de bachas o locales con potencia excesiva).

### 3. Generación de Reportes y Planillas
- Exportación de la Planilla de Carga y el Esquema Unifilar directamente desde AutoCAD, pero con los cálculos certificados por el motor central.

## Estado de la Fase: 🔮 FUTURO / PLANIFICADO

---

## Roadmap de Mejoras
- [ ] Detección automática de cruces de canalizaciones.
- [ ] Integración con la nube (Cloud Sync).
- [ ] Modo de colaboración multi-usuario sobre el mismo DWG.
