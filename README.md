# AD-ELEC (AutoCAD Electrical Plugin)

Este repositorio contiene el código fuente de AD-ELEC, un complemento (plugin) nativo para AutoCAD 2025 que automatiza tareas de diseño eléctrico como la ubicación de luminarias, enrutamiento automático de cañerías, gestión de cargas de tableros y generación de diagramas unifilares.

## Estructura del Proyecto

El proyecto se divide en tres módulos principales para garantizar una arquitectura limpia (Clean Architecture) y facilitar el mantenimiento continuo:

### 1. `AdElec.Core` (Lógica de Negocio y Datos)
Este módulo se encarga puramente de la lógica y *no tiene dependencias* a las librerías de interfaz de usuario de AutoCAD. Es testeable por separado.
- **Modelos**: Tableros (`Panel`), Circuitos (`Circuit`), Cargas (`Load`), Cables.
- **Algoritmos**: Algoritmo de distribución de puntos (luminarias), pathfinding básico (enrutamiento de caños).

### 2. `AdElec.UI` (Interfaz Gráfica - WPF)
Contiene todo el entorno visual.
- **Dockable Palette**: El panel lateral ("PaletteSet") que siempre permanece visible en AutoCAD.
- **Views & ViewModels (MVVM)**: Diálogos para configurar la asignación de circuitos, selección de secciones de cables, etc.
- *Nota*: Referencia y usa los modelos de `AdElec.Core`.

### 3. `AdElec.AutoCAD` (Interacción con el motor de dibujo DWG)
La "capa externa" que conecta el programa con AutoCAD.
- **Comandos (`CommandMethods`)**: Los comandos que escribes en la barra de AutoCAD (ej. `ADE_AUTORUTA`).
- **Base de Datos DWG (`TransactionManager`)**: Métodos para leer atributos de bloques, dibujar polilíneas, MLeaders, etc.
- **Persistencia (`XRecords / Extension Dictionaries`)**: Guarda de forma "oculta" la información de los tableros y circuitos directamente dentro del `.dwg`.

## Flujo de Desarrollo (Git Commit)
Para evitar pérdidas de datos, realizaremos Commits de Git con mensajes descriptivos. Se recomienda hacer PUSH a Github (o tu plataforma de preferencia) periódicamente usando:
```bash
git add .
git commit -m "Descripción de lo avanzado"
git push origin master
```

## Prerrequisitos de Desarrollo
Debido a que este plugin es para AutoCAD 2025+, requiere del **.NET 8 SDK** o **Visual Studio 2022**. Si la terminal arroja que no tienes los SDK de .NET instalados, deberás instalar el workload "Desarrollo de escritorio de .NET".
