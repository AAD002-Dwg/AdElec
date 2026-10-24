# Fase 1: Infraestructura y Vínculo de Proyecto

La Fase 1 estableció los cimientos para la comunicación bidireccional, garantizando que un dibujo de AutoCAD (`.dwg`) esté vinculado de forma única y persistente a un proyecto en la base de datos de **AEA-MOTOR**.

## Implementación Técnica

### 1. Persistencia mediante XRecord
En lugar de depender de nombres de archivos (que pueden cambiar), se utiliza un `XRecord` oculto en el diccionario de datos del dibujo de AutoCAD.
- **Ubicación**: `Named Objects Dictionary` -> `ADE_PROJECT_DATA`.
- **Dato**: `ProjectId` (int).
- **Repositorio**: `DwgProjectRepository.cs`.

### 2. Cliente de Comunicación (REST)
Se implementó `AeaMotorClient.cs` utilizando `HttpClient`.
- **Protocolo**: REST API (JSON).
- **Endpoint Base**: `http://localhost:8000/api/v1`.
- **Métodos**: `GetProject`, `CreateProject`, `SincronizarProyecto` (POST/PUT).

### 3. Interfaz de Usuario (Paleta WPF)
Se integró una paleta lateral (`MainPaletteView.xaml`) que permite al usuario:
- Ver el estado de la conexión con el motor.
- Vincular el dibujo actual a un proyecto existente mediante su ID.
- Visualizar datos generales del proyecto (Nombre, Dirección, etc.).

## Estado de la Fase: ✅ COMPLETADA

---

## Logros
- [x] Handshake exitoso entre AutoCAD y Python/FastAPI.
- [x] Persistencia de ID incluso tras cerrar y abrir AutoCAD.
- [x] Manejo de excepciones cuando el servidor no está en ejecución.
