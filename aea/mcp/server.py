#!/usr/bin/env python3
"""
Servidor MCP (Model Context Protocol) para el Motor Normativo AEA 90364.
Permite a cualquier asistente de IA (Antigravity, Claude, OpenCADStudio, etc.)
ejecutar cálculos y validaciones eléctricas determinísticas sobre aea-core (Rust).

Protocolo: JSON-RPC 2.0 sobre stdin/stdout (MCP estándar).
"""
import ctypes
import json
import os
import sys
from pathlib import Path

# Cargar aea_core.dll nativo
DLL_PATHS = [
    Path(__file__).resolve().parents[1] / "core" / "target" / "release" / "aea_core.dll",
    Path(__file__).resolve().parents[1] / "core" / "target" / "debug" / "aea_core.dll",
]

dll_path = next((p for p in DLL_PATHS if p.exists()), None)
if not dll_path:
    sys.stderr.write(f"ERROR: No se encontró aea_core.dll en {DLL_PATHS}\n")
    sys.exit(1)

core = ctypes.CDLL(str(dll_path))

# Firmas C-ABI con c_void_p para preservar la dirección y liberar memoria limpiamente
core.aea_free_string.argtypes = [ctypes.c_void_p]
core.aea_free_string.restype = None

core.aea_grado_electrificacion.argtypes = [ctypes.c_double, ctypes.c_double]
core.aea_grado_electrificacion.restype = ctypes.c_void_p

core.aea_grado_electrificacion_comercial.argtypes = [ctypes.c_double]
core.aea_grado_electrificacion_comercial.restype = ctypes.c_void_p

core.aea_validar_unidad.argtypes = [ctypes.c_char_p]
core.aea_validar_unidad.restype = ctypes.c_void_p

core.aea_puntos_minimos_ambiente.argtypes = [ctypes.c_char_p]
core.aea_puntos_minimos_ambiente.restype = ctypes.c_void_p

core.aea_calcular_caida_tension.argtypes = [ctypes.c_double, ctypes.c_double, ctypes.c_double, ctypes.c_uint8, ctypes.c_double]
core.aea_calcular_caida_tension.restype = ctypes.c_double

core.aea_verificar_cortocircuito_conductor.argtypes = [ctypes.c_double, ctypes.c_double, ctypes.c_double, ctypes.c_bool]
core.aea_verificar_cortocircuito_conductor.restype = ctypes.c_void_p


def call_grado(cubierta: float, semicubierta: float = 0.0) -> dict:
    ptr = core.aea_grado_electrificacion(ctypes.c_double(cubierta), ctypes.c_double(semicubierta))
    try:
        raw = ctypes.string_at(ptr).decode("utf-8")
        return json.loads(raw)
    finally:
        core.aea_free_string(ptr)


def call_grado_comercial(superficie: float) -> dict:
    ptr = core.aea_grado_electrificacion_comercial(ctypes.c_double(superficie))
    try:
        raw = ctypes.string_at(ptr).decode("utf-8")
        return json.loads(raw)
    finally:
        core.aea_free_string(ptr)


def call_validar(unidad: dict) -> list | dict:
    payload = json.dumps(unidad).encode("utf-8")
    ptr = core.aea_validar_unidad(payload)
    try:
        raw = ctypes.string_at(ptr).decode("utf-8")
        return json.loads(raw)
    finally:
        core.aea_free_string(ptr)


def call_puntos_minimos(ambiente: dict, grado: str = "medio") -> dict:
    payload = json.dumps({"ambiente": ambiente, "grado": grado}).encode("utf-8")
    ptr = core.aea_puntos_minimos_ambiente(payload)
    try:
        raw = ctypes.string_at(ptr).decode("utf-8")
        return json.loads(raw)
    finally:
        core.aea_free_string(ptr)


def call_caida_tension(longitud_m: float, corriente_a: float, seccion_mm2: float, cables_cargados: int = 2, cos_phi: float = 0.9) -> dict:
    dv = float(core.aea_calcular_caida_tension(
        ctypes.c_double(longitud_m),
        ctypes.c_double(corriente_a),
        ctypes.c_double(seccion_mm2),
        ctypes.c_uint8(cables_cargados),
        ctypes.c_double(cos_phi)
    ))
    return {
        "longitud_m": longitud_m,
        "corriente_a": corriente_a,
        "seccion_mm2": seccion_mm2,
        "cables_cargados": cables_cargados,
        "cos_phi": cos_phi,
        "caida_tension_porcentaje": round(dv, 2),
        "cumple_iluminacion_3pct": dv <= 3.0,
        "cumple_otros_usos_5pct": dv <= 5.0
    }


def call_cortocircuito(icc_ka: float, tiempo_s: float, seccion_mm2: float, aislacion_pvc: bool = True) -> dict:
    ptr = core.aea_verificar_cortocircuito_conductor(
        ctypes.c_double(icc_ka),
        ctypes.c_double(tiempo_s),
        ctypes.c_double(seccion_mm2),
        ctypes.c_bool(aislacion_pvc)
    )
    try:
        raw = ctypes.string_at(ptr).decode("utf-8")
        return json.loads(raw)
    finally:
        core.aea_free_string(ptr)


TOOLS = [
    {
        "name": "aea_grado_electrificacion",
        "description": "Calcula el grado de electrificación reglamentario (Mínimo, Medio, Elevado, Superior) según AEA 90364-7-770 (Tabla 770.7.I), computando superficie cubierta + 50% de semicubierta.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "superficie_cubierta_m2": {
                    "type": "number",
                    "description": "Superficie cubierta del inmueble en m²"
                },
                "superficie_semicubierta_m2": {
                    "type": "number",
                    "description": "Superficie semicubierta en m² (se computa al 50%)",
                    "default": 0.0
                }
            },
            "required": ["superficie_cubierta_m2"]
        }
    },
    {
        "name": "aea_grado_comercial",
        "description": "Calcula el grado de electrificación y límite de demanda máxima de potencia para oficinas y locales comerciales según AEA 90364-7-771 (Tabla 771.8.IV).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "superficie_m2": {
                    "type": "number",
                    "description": "Superficie en m² de la oficina o local comercial"
                }
            },
            "required": ["superficie_m2"]
        }
    },
    {
        "name": "aea_puntos_minimos_ambiente",
        "description": "Calcula la cantidad mínima obligatoria de bocas de Iluminación (IUG) y Tomacorrientes (TUG) para un ambiente según AEA 90364-7-770 (Tabla 770.7.III).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "ambiente": {
                    "type": "object",
                    "properties": {
                        "id": { "type": "string" },
                        "nombre": { "type": "string" },
                        "tipo": {
                            "type": "string",
                            "description": "Tipo de local: sala_estar, dormitorio, cocina, baño, pasillo, lavadero, garaje, balcon, etc."
                        },
                        "superficie_m2": { "type": "number" },
                        "longitud_m": { "type": "number", "description": "Longitud en metros (requerida para pasillos y balcones)" }
                    },
                    "required": ["id", "tipo", "superficie_m2"]
                },
                "grado": {
                    "type": "string",
                    "enum": ["minimo", "medio", "elevado", "superior"],
                    "default": "medio"
                }
            },
            "required": ["ambiente"]
        }
    },
    {
        "name": "aea_validar_tablero",
        "description": "Audita integralmente un tablero y sus circuitos contra AEA 90364-7-770 (coordinación Ib <= In <= Iz, agrupamiento en cañería, calibres máximos, secciones mínimas y cantidad mínima de circuitos). Devuelve diagnósticos normativos con severidad y opciones de Quick-Fix.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "unidad": {
                    "type": "object",
                    "properties": {
                        "id": { "type": "string" },
                        "superficie_cubierta_m2": { "type": "number" },
                        "superficie_semicubierta_m2": { "type": "number", "default": 0.0 },
                        "in_cabecera_a": { "type": "number", "description": "Calibre del interruptor principal de cabecera" },
                        "icc_origen_ka": { "type": "number", "description": "Corriente de cortocircuito en el origen (máx 10 kA para 770)" },
                        "circuitos": {
                            "type": "array",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "id": { "type": "string" },
                                    "tipo": { "type": "string", "description": "IUG, TUG, TUE..." },
                                    "seccion_mm2": { "type": "number" },
                                    "in_a": { "type": "number", "description": "Calibre de térmica del circuito" },
                                    "ib_a": { "type": "number", "description": "Corriente de cálculo o carga simultánea (opcional)" },
                                    "bocas": { "type": "integer" },
                                    "cables_cargados": { "type": "integer", "enum": [2, 3], "default": 2 },
                                    "circuitos_en_canalizacion": { "type": "integer", "default": 1 }
                                },
                                "required": ["id", "tipo", "seccion_mm2", "in_a"]
                            }
                        },
                        "ambientes": {
                            "type": "array",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "id": { "type": "string" },
                                    "nombre": { "type": "string" },
                                    "tipo": { "type": "string" },
                                    "superficie_m2": { "type": "number" },
                                    "bocas_iug": { "type": "integer" },
                                    "bocas_tug": { "type": "integer" }
                                }
                            }
                        }
                    },
                    "required": ["id", "superficie_cubierta_m2", "circuitos"]
                }
            },
            "required": ["unidad"]
        }
    },
    {
        "name": "aea_calcular_caida_tension",
        "description": "Calcula analíticamente la caída de tensión porcentual (ΔV%) según física eléctrica estándar de cobre a 70°C (220V monofásico o 380V trifásico) y verifica cumplimiento AEA (3% iluminación, 5% tomas).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "longitud_m": { "type": "number", "description": "Longitud del circuito en metros" },
                "corriente_a": { "type": "number", "description": "Corriente de proyecto o calibre nominal en Amperes" },
                "seccion_mm2": { "type": "number", "description": "Sección del conductor en mm²" },
                "cables_cargados": { "type": "integer", "enum": [2, 3], "default": 2, "description": "2 para monofásico (220V), 3 para trifásico (380V)" },
                "cos_phi": { "type": "number", "default": 0.9, "description": "Factor de potencia (cos phi)" }
            },
            "required": ["longitud_m", "corriente_a", "seccion_mm2"]
        }
    },
    {
        "name": "aea_verificar_cortocircuito",
        "description": "Comprueba el estrés térmico del conductor ante cortocircuito (I²·t ≤ k²·S²) según norma IRAM NM 247-3 / AEA 770.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "icc_ka": { "type": "number", "description": "Corriente de cortocircuito presunta en kA" },
                "tiempo_despeje_s": { "type": "number", "default": 0.02, "description": "Tiempo de apertura del interruptor automático (ej: 0.02 s)" },
                "seccion_mm2": { "type": "number", "description": "Sección del conductor en mm²" },
                "aislacion_pvc": { "type": "boolean", "default": True, "description": "True para PVC (k=115), False para XLPE (k=143)" }
            },
            "required": ["icc_ka", "seccion_mm2"]
        }
    }
]


def send_response(req_id, result=None, error=None):
    resp = {"jsonrpc": "2.0", "id": req_id}
    if error is not None:
        resp["error"] = error
    else:
        resp["result"] = result
    sys.stdout.write(json.dumps(resp) + "\n")
    sys.stdout.flush()


def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
        except Exception as e:
            continue

        req_id = req.get("id")
        method = req.get("method")
        params = req.get("params", {})

        if method == "initialize":
            send_response(req_id, {
                "protocolVersion": "2024-11-05",
                "capabilities": { "tools": {} },
                "serverInfo": {
                    "name": "aea-normativa-mcp",
                    "version": "1.0.0"
                }
            })
        elif method == "notifications/initialized":
            pass
        elif method == "tools/list":
            send_response(req_id, { "tools": TOOLS })
        elif method == "tools/call":
            tool_name = params.get("name")
            args = params.get("arguments", {})
            try:
                if tool_name == "aea_grado_electrificacion":
                    cubierta = float(args.get("superficie_cubierta_m2", 0))
                    semicubierta = float(args.get("superficie_semicubierta_m2", 0))
                    res = call_grado(cubierta, semicubierta)
                elif tool_name == "aea_grado_comercial":
                    sup = float(args.get("superficie_m2", 0))
                    res = call_grado_comercial(sup)
                elif tool_name == "aea_puntos_minimos_ambiente":
                    amb = args.get("ambiente", {})
                    grado = args.get("grado", "medio")
                    res = call_puntos_minimos(amb, grado)
                elif tool_name == "aea_validar_tablero":
                    unidad = args.get("unidad", {})
                    res = call_validar(unidad)
                elif tool_name == "aea_calcular_caida_tension":
                    l = float(args.get("longitud_m", 0))
                    i = float(args.get("corriente_a", 0))
                    s = float(args.get("seccion_mm2", 0))
                    cargados = int(args.get("cables_cargados", 2))
                    cos_p = float(args.get("cos_phi", 0.9))
                    res = call_caida_tension(l, i, s, cargados, cos_p)
                elif tool_name == "aea_verificar_cortocircuito":
                    icc = float(args.get("icc_ka", 0))
                    t = float(args.get("tiempo_despeje_s", 0.02))
                    s = float(args.get("seccion_mm2", 0))
                    pvc = bool(args.get("aislacion_pvc", True))
                    res = call_cortocircuito(icc, t, s, pvc)
                else:
                    send_response(req_id, error={"code": -32601, "message": f"Tool '{tool_name}' no encontrada"})
                    continue

                send_response(req_id, {
                    "content": [{
                        "type": "text",
                        "text": json.dumps(res, ensure_ascii=False, indent=2)
                    }]
                })
            except Exception as ex:
                send_response(req_id, error={"code": -32000, "message": str(ex)})
        else:
            if req_id is not None:
                send_response(req_id, error={"code": -32601, "message": f"Método '{method}' no soportado"})


if __name__ == "__main__":
    main()
