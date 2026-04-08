# AD-ELEC (AutoCAD Electrical Plugin)

Este repositorio contiene el código fuente de **AD-ELEC**, un complemento (plugin) nativo para AutoCAD 2025 que automatiza tareas de diseño eléctrico extremo a extremo: desde la ubicación inteligente de bocas hasta el cálculo de potencias y generación de documentación técnica.

## 🚀 Características Principales

- **Distribución Inteligente**: Algoritmos para ubicación de luminarias y tomas.
- **Enrutamiento Automático**: Generación de cañerías y cableado basada en lógica de circuitos.
- **Gestión de Cargas**: Cálculo automático de potencias y balanceo de fases en tableros.
- **Inspección Técnica**: Herramientas integradas para auditoría de bloques y atributos.

## 📂 Estructura del Proyecto

El proyecto sigue los principios de **Clean Architecture**:

### 1. `AdElec.Core` (Lógica de Negocio)
- **Modelos**: `Panel`, `Circuit`, `Load`, `BlockDefinition`.
- **Motor de Cálculo**: Sumatoria de cargas, secciones de cables y caída de tensión.

### 2. `AdElec.UI` (Interfaz WPF)
- **Paleta Lateral**: Interfaz nativa de AutoCAD para gestión de proyectos en tiempo real.
- **MVVM**: Separación estricta entre vista y lógica de interfaz.

### 3. `AdElec.AutoCAD` (Motor de Dibujo)
- **Comandos**: 
  - `ADE_PANEL`: Abre la interfaz principal.
  - `ADE_INSPECT`: Herramienta de diagnóstico que exporta atributos y estados de visibilidad a JSON.
- **Geometría**: Extensiones para manipulación de `BlockReference`, `Polyline` y Diccionarios de Extensión.

## 🏗️ Gestión de Bloques CAD

Los bloques son el núcleo del sistema. Se encuentran en [Bloques_CAD](file:///g:/AD-ELEC/Bloques_CAD).
La documentación técnica detallada de cada bloque, incluyendo sus atributos reales y modos de visibilidad, se encuentra en:
👉 **[Bloques_CAD/Bloques.md](file:///g:/AD-ELEC/Bloques_CAD/Bloques.md)**

## 🛠️ Desarrollo y Compilación

### Prerrequisitos
- **.NET 8 SDK**
- **AutoCAD 2025 SDK** (AutoCAD.NET / AutoCAD.NET.Model)
- **Visual Studio 2022**

### Compilación rápida
Para compilar el plugin desde la terminal:
```powershell
dotnet build AdElec.sln
```

### Carga en AutoCAD
1. Abrir AutoCAD 2025.
2. Ejecutar comando `NETLOAD`.
3. Seleccionar la DLL en: `src\AdElec.AutoCAD\bin\Debug\net8.0-windows\AdElec.AutoCAD.dll`.

## 🔄 Flujo de Git
Mantener el repositorio actualizado:
```bash
git add .
git commit -m "Descripción de avance"
git push origin master
```
