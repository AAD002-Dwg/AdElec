# AD-ELEC — Plugin Eléctrico para AutoCAD (AEA 90364)

Plugin profesional para AutoCAD desarrollado en **C# (.NET 8)** y **AutoLISP** para el diseño, dimensionamiento y verificación normativa de instalaciones eléctricas residenciales y comerciales según la reglamentación argentina **AEA 90364-7-771**.

---

## 🏗️ Arquitectura y Repositorios

El ecosistema está compuesto por dos proyectos independientes que se comunican vía API HTTP REST:

| Proyecto | Ruta | Rol | Stack |
|---|---|---|---|
| **AD-ELEC** *(este repo)* | `G:\PROYECTOS AD\AD-ELEC` | Cliente CAD (UI, bloques, DWG, comandos) | .NET 8, WPF, AutoCAD .NET API, AutoLISP |
| **AEA-MOTOR** | `G:\PROYECTOS AD\AEA 90364-B\AEA-MOTOR-RESTORED` | Backend de cálculo normativo AEA | Python 3.10, FastAPI, SQLite |

> **Nota de Integración:** AD-ELEC consume el motor de cálculo enviando el modelo del proyecto a `http://localhost:8000/calcular`. No se fusionan los repositorios para mantener el plugin CAD desacoplado del motor normativo.

---

## 📁 Estructura del Repositorio

```
AD-ELEC/
├── AdElec.sln            # Solución .NET (3 proyectos)
├── src/
│   ├── AdElec.Core/      # Modelos de dominio y cliente HTTP (sin dependencias CAD)
│   ├── AdElec.UI/        # Paletas y ventanas WPF (ADE_PANEL)
│   └── AdElec.AutoCAD/   # Comandos AutoCAD, persistencia XData y geometría
├── lisp/                 # Rutinas AutoLISP propias (luminarias, tomas, cañerías)
│   └── vendor/           # Utilidades LISP de terceros
├── blocks/               # Bloques dinámicos DWG del sistema
├── deploy/               # DLLs compiladas, acad.lsp y script de compilación
├── bitacora/             # Historial cronológico de cambios y decisiones (bitacora.md)
└── docs/                 # Documentación técnica, arquitectura y especificaciones
```

---

## 🚀 Requisitos y Compilación

### Requisitos previos
- **.NET 8 SDK** (verificar con `dotnet --version`)
- **AutoCAD 2025** (o 2024 con .NET 8 / net48 según configuración)
- Opcional: **Visual Studio 2022** / **VS Code** con C# Dev Kit

### Compilar el proyecto
Desde la terminal en la raíz del proyecto:
```bash
dotnet build AdElec.sln --configuration Release
```
O simplemente ejecutando:
```cmd
deploy\build.bat
```
Las DLLs resultantes (`AdElec.AutoCAD.dll`, `AdElec.Core.dll`, `AdElec.UI.dll`) se copiarán automáticamente en la carpeta `deploy/`.

---

## 🔌 Carga en AutoCAD

### Opción 1: Carga manual
En la línea de comandos de AutoCAD:
```text
NETLOAD -> seleccionar G:\PROYECTOS AD\AD-ELEC\deploy\AdElec.AutoCAD.dll
```

### Opción 2: Carga automática al iniciar AutoCAD
Copiar [`deploy/acad.lsp`](file:///g:/PROYECTOS%20AD/AD-ELEC/deploy/acad.lsp) a la carpeta Support de AutoCAD:
`%APPDATA%\Autodesk\AutoCAD 2025\R25.0\enu\Support\`  
O agregar `G:\PROYECTOS AD\AD-ELEC\deploy\` a las rutas de búsqueda en *Opciones → Archivos → Support File Search Path*.

---

## ⌨️ Comandos Principales en AutoCAD

| Comando | Tipo | Descripción |
|---|---|---|
| `ADE_PANEL` | C# / WPF | Abre la paleta lateral de gestión de tableros, circuitos y ambientes |
| `ADE_CALCULAR` | C# | Envía el proyecto al backend AEA-MOTOR y actualiza los resultados |
| `ADE_AUTOAMBIENTES` | C# | Detección geométrica de ambientes cerrados a partir de polilíneas |
| `ADE_INSPECT` | C# | Inspector de bloques eléctricos y atributos en el DWG |
| `ADE_LUMINARIAS` | AutoLISP | Distribución y colocación de luminarias |
| `ADE_TOMAS` | AutoLISP | Distribución y conteo de bocas de tomacorrientes |
| `ADE_CANERIAS` | AutoLISP | Trazado de cañerías y vinculación de bocas |

---

## 📌 Bitácora y Reglas del Proyecto
Las actualizaciones y registros técnicos de cada sesión se documentan en [`bitacora/bitacora.md`](file:///g:/PROYECTOS%20AD/AD-ELEC/bitacora/bitacora.md) siguiendo las reglas de [`AGENTS.md`](file:///g:/PROYECTOS%20AD/AD-ELEC/AGENTS.md).
