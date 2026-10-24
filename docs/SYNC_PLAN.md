# Plan Maestro de Sincronización: AEA-MOTOR ↔ AD-ELEC

Bienvenido a la documentación detallada del sistema de sincronización bidireccional. Este proyecto está dividido en 4 fases críticas para garantizar la integridad de los datos entre los planos de AutoCAD y el motor de cálculo.

---

## 📂 Desglose por Fases

### [Fase 1: Infraestructura y Vínculo](file:///g:/AD-ELEC/MD/FASE_1_INFRAESTRUCTURA.md)
Establecimiento del canal de comunicación, persistencia del ID de proyecto y vinculación inicial del dibujo.
> **Estado**: ✅ Completada

### [Fase 2: Geometría y Arquitectura Planar](file:///g:/AD-ELEC/MD/FASE_2_GEOMETRIA.md)
Extracción de polígonos, conversión a grafos y reconstrucción de la arquitectura en el módulo AD-CAD de la web.
> **Estado**: ✅ Completada

### [Fase 3: Lógica Eléctrica y Circuitos](file:///g:/AD-ELEC/MD/FASE_3_LOGICA_CIRCUITOS.md)
Transferencia de cargas, bocas, circuitos y validación reglamentaria según AEA 90364.
> **Estado**: 🏗️ En Ejecución

### [Fase 4: Retroalimentación Web-to-CAD](file:///g:/AD-ELEC/MD/FASE_4_RETROALIMENTACION.md)
Sincronización inversa de atributos y auditoría normativa en tiempo real dentro de AutoCAD.
> **Estado**: 🔮 Planificada

---

## 🛠️ Herramientas y Repositorios
- **Backend**: FastAPI (Python) que procesa el grafo y realiza cálculos eléctricos.
- **Frontend**: React (Zustand) con el módulo interactivo AD-CAD.
- **Plugin**: C# / .NET 8 con librerías de AutoCAD para manipulación de entidades.

---
*Este plan es un documento vivo y se actualiza a medida que avanzamos en la implementación.*
