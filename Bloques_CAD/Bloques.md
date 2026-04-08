# Definición de Bloques CAD - AD-ELEC

Este documento especifica la lista **real** de bloques de AutoCAD utilizados en el proyecto **AD-ELEC**, extrayendo la información base y los modos de visualización directamente desde la inspección de la base de datos (mediante `ADE_INSPECT`).

## 1. Interruptores (`I.E-AD-01.dwg`)
Representan los puntos de control o llaves de efecto.
- **Función:** Control de bocas de iluminación.
- **Atributos Dinámicos (Tags):**
  - `LLAVE-N°0`
  - `LLAVE-N°1`
  - `LLAVE-N°2`
- **Modos de Visualización (`Visibilidad1`):**
  - llave combinación
  - llave 1 efecto
  - llave 2 efecto
  - llave 3 efecto
  - Pulsador
  - Todo-edit-bq (Edición interna)

## 2. Tableros (`I.E-AD-04.dwg`)
Representan los centros de distribución eléctrica.
- **Función:** Gestión de circuitos y protecciones.
- **Atributos Dinámicos (Tags):**
  - `TX-X` (Acrónimo TS, TSS, TP, etc.)
- **Modos de Visualización (`Visibilidad1`):**
  - Tablero Seccional
  - Tablero Principal
  - Tablero Seccional Chico
  - Todo-edit-bq (Edición interna)

## 3. Bocas de Iluminación (`I.E-AD-07.dwg`)
Representan los puntos de luz en techo o pared.
- **Función:** Carga de iluminación.
- **Atributos Dinámicos (Tags):**
  - `CX` (Circuito)
  - `PX` (Llave)
  - `POT` (Potencia VA)
  - `D` (Designación)
- **Modos de Visualización (`Visibilidad1`):**
  - Boca de techo
  - Boca de techo FC
  - Boca de techo chica
  - Boca de techo colgante
  - Boca de techo plafon
  - Boca de techo Rec plafon
  - Boca de pared
  - Boca de pared FC
  - Reflector
  - Todo-edit-bq (Edición interna)

## 4. Bocas de Tomacorrientes Gral. (`I.E-AD-09.dwg`)
Representan los tomas de uso general (TUG).
- **Función:** Alimentación de cargas menores.
- **Atributos Dinámicos (Tags):**
  - `POT` (Potencia)
  - `CX` (Circuito)
  - `XT`
  - `D`
- **Modos de Visualización (`Visibilidad1`):**
  - Toma de Uso Gral.
  - Toma de Uso Gral. (capsulado)
  - Extractor
  - Todo-edit-bq (Edición interna)

## 5. Bocas de Tomacorrientes Especiales (`I.E-AD-09.02.dwg`)
Representan los tomas de uso especial (TUE).
- **Función:** Alimentación de cargas específicas de alta potencia.
- **Atributos Dinámicos (Tags):**
  - `POT` (Potencia)
  - `CX` (Circuito)
  - `XT`
  - `N°EQ`
- **Modos de Visualización (`Visibilidad1`):**
  - Toma de Uso Especial.
  - Toma de Uso Especial. (capsulado)
  - Todo-edit-bq (Edición interna)

## 6. Nomenclatura de Cañerías (`I.E-AD-Nomenclatura_Cables.dwg`)
Bloque auxiliar para el etiquetado de cables y cañerías.
- **Función:** Documentación técnica de rutas.
- **Atributos Dinámicos (Tags):**
  - `CABLES` (Ej. 2x1,5+T)
  - `CAÑERIA` (Ej. RS)
- **Modos de Visualización:**
  - *No expone estados de visibilidad estándar. Contiene campos de tabla y posicionadores de extensión libre.*

---

> [!CAUTION]
> Algunos bloques utilizan atributos distintos a los comúnmente inferidos. Por ejemplo, los Interruptores usan `LLAVE-N°1` en lugar de un tag `COMUN`. Es fundamental hacer coincidir la lógica de lectura C# (`BlockReference.AttributeCollection`) **exactamente** con las claves especificadas en este documento.
