# AUTOambientes — Respuesta al plan de implementación

## Respuestas a Open Questions

**1. Nombre del comando**

Confirmar `ADE_AUTO_AMBIENTES`. Consistente con la convención existente.

**2. Tipo de geometría en los planos reales**

Los planos de trabajo NO tienen polilíneas cerradas que representen ambientes completos. Los ambientes están conformados por segmentos de polilínea (`LWPOLYLINE` no cerradas) que actúan como muros, equivalentes a `LINE` sueltos. La reconstrucción de perímetros desde segmentos es el caso predominante y debe implementarse en la Versión 1. No es opcional.

El flujo de detección de polilíneas cerradas puede mantenerse como caso rápido, pero no puede ser el único camino.

**3. Agrupamiento en Unidades Funcionales**

Las UFs se auto-numeran (`UF-01`, `UF-02`...). El tipo de UF se sugiere automáticamente según composición de ambientes, y el usuario puede modificarlo en el panel de verificación.

La taxonomía de UFs debe incluir una categoría explícita de **Servicios Generales (SG)** que agrupe áreas comunes del edificio. Esta categoría no es una unidad privativa sino un conjunto de espacios compartidos. Los tipos de ambiente que se asignan a SG son:

```
Palier
Hall
Salón de Usos Múltiples (SUM)
Cochera (área común, no unidad individual)
Sala de Máquinas
Paso / Pasillo (en áreas comunes)
Escalera (núcleo vertical)
Ascensor (núcleo vertical)
```

Regla de clasificación de UF:

```
Contiene cocina + baño                    → Departamento
Contiene baño sin cocina                  → Oficina
Contiene local sin baño ni cocina         → Local Comercial
Contiene Palier, Hall, SUM, Sala Máquinas → Servicios Generales (SG)
Contiene solo Cochera individual          → Cochera (UF propia)
No clasificable con confianza             → fallback (ver RoomGrouper)
```

**4. Envío a la aplicación web**

El panel de verificación NO incluye botón de envío directo a la web. El flujo continúa siendo: insertar bloques `ID_LOCALES` en el DWG y luego ejecutar `ADE_PANEL`. No agregar `HttpClient` al `AutoAmbientesViewModel`.

---

## Aclaraciones técnicas

### Segmentos con Bulge (arcos) — diferir a Versión 2

Diferir el soporte de `Bulge != 0` a la Versión 2 es eficiente. Los ambientes con segmentos curvos (balcones redondeados, escaleras, etc.) son un porcentaje menor del total y su tratamiento correcto agrega complejidad al `PolygonMath` sin beneficio proporcional en la primera iteración.

En Versión 1: si un segmento tiene `Bulge != 0`, ignorar el componente de arco y tratar el segmento como recto (unir el punto inicial con el punto final). Esto produce un área ligeramente incorrecta para esos ambientes, lo cual es aceptable dado que el panel de verificación permite corrección manual y el flag `RequiereVerificacion` puede activarse cuando se detecte presencia de bulge en algún segmento del polígono reconstruido.

```csharp
// V1: segmentos con bulge se tratan como rectos
// Activar RequiereVerificacion = true si el polígono contiene algún segmento con bulge
if (polyline.GetBulgeAt(i) != 0)
    room.RequiereVerificacion = true;
```

Versión 2 implementará la tesselación correcta del arco a partir del bulge.

### RoomGrouper — comportamiento de fallback obligatorio

El algoritmo de componentes conexos post-remoción de nodos de acceso tiene casos borde reales en plantas de edificios argentinos:

- Hall que conecta a otro hall antes de llegar a los departamentos.
- Cochera sin puerta detectable (acceso por portón, sin bloque representado).
- Acceso directo desde núcleo de escalera sin palier intermedio.
- Plantas con múltiples accesos a una misma UF.

Implementar el siguiente comportamiento de fallback obligatorio:

Si `RoomGrouper` no puede determinar las UFs con confianza (no encuentra nodos de acceso claros, el grafo es totalmente conexo sin separación natural, o un componente contiene más de N ambientes sin estructura reconocible), en lugar de fallar o producir agrupamientos incorrectos silenciosamente:

1. Asignar todos los ambientes no clasificados a una UF única denominada `SIN_CLASIFICAR`.
2. Marcar cada uno de esos ambientes con `RequiereVerificacion = true` y `Confianza = "baja"`.
3. En el panel de verificación, agrupar estos ambientes en una sección diferenciada visualmente, con un mensaje explícito: "No fue posible determinar la unidad funcional. Asignar manualmente."
4. El usuario puede reasignar cada ambiente a una UF existente o crear una nueva desde el panel.

El fallback no es un error — es un resultado válido que el sistema entrega al usuario para resolución manual. Nunca silenciar incertidumbre en el agrupamiento.

### Resaltado de polígono en AutoCAD desde el panel de verificación

Implementar interacción bidireccional entre el `DataGrid` y el dibujo:

**Al seleccionar una fila en el DataGrid:**

1. Usar `PolylineId` para recuperar la entidad en la base de datos del DWG.
2. Cambiar temporalmente el color de esa entidad a un color de resaltado configurable (sugerido: `Color.FromColorIndex(ColorMethod.ByAci, 1)` — rojo).
3. Ejecutar `ZoomToObjects` sobre el `ObjectId` para centrar la vista.
4. Al deseleccionar la fila (o seleccionar otra), restaurar el color original.

**Al deseleccionar o cerrar el diálogo:**

Restaurar el color original de todas las entidades modificadas. Usar un `Dictionary<ObjectId, Color>` para guardar los colores originales antes de modificarlos.

```csharp
// Estructura para restauración
private Dictionary<ObjectId, Color> _coloresOriginales = new();

private void ResaltarAmbiente(ObjectId polylineId)
{
    // Restaurar anteriores, guardar y aplicar nuevo color, zoom
}

private void RestaurarColores()
{
    // Restaurar todos los colores guardados en _coloresOriginales
}
```

Esta funcionalidad es parte de la Versión 1. Sin ella el panel de verificación no es usable en plantas con más de 10 ambientes.

---

## Resumen de decisiones

| Punto | Decisión |
|---|---|
| Reconstrucción desde segmentos sueltos | V1, obligatorio |
| Polilíneas cerradas como caso rápido | V1, como optimización |
| Soporte de Bulge/arcos | V2, diferido |
| Taxonomía SG / Servicios Generales | V1, incluir |
| UFs auto-numeradas con tipo sugerido | V1 |
| Fallback de RoomGrouper a SIN_CLASIFICAR | V1, obligatorio |
| Resaltado y zoom desde DataGrid | V1, obligatorio |
| Botón envío directo a web | No incluir |
| Nombre del comando | ADE_AUTO_AMBIENTES |
