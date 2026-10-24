---
name: Integración AEA-MOTOR con AD-ELEC
description: AEA-MOTOR es un motor normativo Python/FastAPI con AEA 90364 completo, ubicado en G:\PROYECTOS AD\AEA 90364-B\AEA-MOTOR-RESTORED. La estrategia es integrar vía API HTTP, no fusionar.
type: project
---

AEA-MOTOR-RESTORED (`G:\PROYECTOS AD\AEA 90364-B\AEA-MOTOR-RESTORED`) es un proyecto separado que implementa el motor normativo AEA 90364-7-771 completo.

**Why:** AD-ELEC necesita cálculos eléctricos (Fase 5 del roadmap) pero reimplementarlos en C# sería duplicar trabajo. AEA-MOTOR ya los tiene completos en Python/FastAPI.

**How to apply:** La integración es vía HTTP. AD-ELEC llama a `POST localhost:8000/calcular` con el modelo `ProyectoDto` y recibe `ResultadoProyecto`. No fusionar los repos — mantenerlos separados con AD-ELEC como cliente CAD y AEA-MOTOR como backend de cálculo.

Stack AEA-MOTOR:
- Backend: Python 3.10 + FastAPI + SQLAlchemy + SQLite
- Frontend: React 19 + Vite + Zustand + TailwindCSS + Three.js + Pixi.js
- Arranque: `Iniciar_Proyecto.bat` (Windows) → backend :8000 + frontend :5173

Endpoints clave:
- `POST /calcular` — cálculo completo del proyecto
- `POST /grado` — solo grado de electrificación
- `POST /validar` — solo validaciones normativas
- `POST /exportar/JSON_AUTOCAD` — resultado para integración CAD
- `GET /norma/tablas?version=2006` — tablas normativas

Modelos a extender en AD-ELEC para compatibilidad:
- `Ambiente` (tipo, superficie_m2, longitud_m)
- `DatosMontante` (longitud_m, cos_phi, es_trifasico)
- `DatosRed` (impedancia_red_ohm, tension_v)
