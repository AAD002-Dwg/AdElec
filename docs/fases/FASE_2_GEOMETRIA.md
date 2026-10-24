# Fase 2: Geometría y Arquitectura Planar (AD-CAD)

La Fase 2 transformó la capacidad del plugin para que AutoCAD actúe como el "mapeador de arquitectura" para el motor web, permitiendo reconstruir plantas 3D a partir de polilíneas.

## Implementación Técnica

### 1. Extracción de Geometría (Polígonos e Interior)
Se modificó `DwgAmbienteRepository.cs` para recolectar los vértices reales de las polilíneas de contorno.
- **Vínculo XData**: El bloque `ID_LOCALES` guarda el `Handle` de la polilínea, permitiendo actualizaciones automáticas si la forma del local cambia en CAD.
- **Atributos Paramétricos**: Se añadió el atributo `ESP` (Espesor de muro) para informar al motor web sobre el volumen arquitectónico.

### 2. Conversor de Grafo Planar (`GeometryConverter.cs`)
Este es el motor central de esta fase. Convierte polígonos independientes en un **Grafo Planar** unificado:
- **Deduplicación de Nodos**: Une vértices que están a menos de 0.01m de distancia.
- **Topología de Muros (Aristas)**: Identifica muros compartidos entre habitaciones, evitando que el motor web dibuje muros dobles donde solo hay uno.
- **Centroides**: Calcula el centro geométrico de cada local para posicionar etiquetas en la Web.

### 3. Integración con el Módulo AD-CAD
El payload de sincronización ahora incluye la sección `ad_cad.plantas`, la cual alimenta directamente al editor de arquitectura web, permitiendo visualización y edición inmediata de muros.

## Estado de la Fase: ✅ COMPLETADA

---

## Logros
- [x] Generación de grafos unificados (Nodos/Aristas).
- [x] Soporte para espesores de muros paramétricos.
- [x] Visualización en tiempo real de la arquitectura de CAD en la Web.
