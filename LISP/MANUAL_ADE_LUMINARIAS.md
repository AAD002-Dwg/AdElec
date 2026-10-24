# Manual de Usuario: ADE_LUMINARIAS v4.2

## Descripción General
`ADE_LUMINARIAS` es una herramienta profesional para la distribución interactiva de luminarias en AutoCAD. Su principal característica es el sistema de control "Bi-Manual" que permite ajustar tanto la densidad de la grilla como su posición exacta mediante márgenes en tiempo real.

---

## 🛠 Comandos
*   `ADE_LUMINARIAS`: Inicia el comando principal.
*   `ADE_LUMINARIAS_DIAGKEYS`: Herramienta de diagnóstico para entrada de teclado.

---

## 🚀 Flujo de Trabajo

1.  **Configuración**: Introducí Circuito (CX) y Tablero (D).
2.  **Selección de Contorno**: Seleccioná una polilínea cerrada. El modo **AUTO** inicia por defecto.
3.  **Ajuste Bi-Manual**:
    - Usá la mano izquierda (**WASD**) para ajustar cuántas luces querés (o a qué distancia).
    - Usá la mano derecha (**Teclado Numérico 8-2-4-6**) para separar las luces de las paredes.
4.  **Confirmación**: `Enter` o `Espacio` para insertar y pasar al siguiente cuarto.

---

## ⌨️ Esquema de Controles

### Grilla (Mano Izquierda: W / S / A / D)
Controla la densidad de la distribución:
*   **Modo AUTO**: Ajusta la **cantidad** (Filas/Columnas).
*   **Modo FIJO**: Ajusta el **espaciado** en pasos de **0.1m**.

### Margen (Mano Derecha: 8 / 2 / 4 / 6)
Controla la distancia a las paredes en todas las direcciones:
*   Aumenta o disminuye el margen en pasos fijos de **0.1m** (10 cm).

### Funciones Especiales
*   **`M` (Modo)**: Cambia entre modo AUTO (cantidad) y FIJO (espaciado exacto).
*   **`G` (Grid)**: Muestra/Oculta la grilla verde de referencia.
*   **`U` (Undo)**: Deshace el último movimiento.
*   **`0-9`**: Ingreso directo de grilla (ej: "4,4").
*   **`Esc`**: Cancela el ajuste actual o sale del comando.

---

## 📊 Barra de Estado
Ejemplo: `M:AUTO | G:4x3 | E:2.5x2.1 | M:0.2x0.2 | V:12`

*   **M (Modo)**: AUTO o FIJO.
*   **G (Grilla)**: Columnas x Filas.
*   **E (Espaciado)**: Distancia real entre luminarias.
*   **M (Margen)**: Distancia desde el eje de las luces exteriores hasta la pared.
*   **V (Visibles)**: Total de luminarias dentro del área.

---
*Plugin desarrollado para AD-ELEC. v4.2 - 2026*
