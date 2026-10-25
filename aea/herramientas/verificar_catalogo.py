#!/usr/bin/env python3
"""
Verificador de catálogo normativo AEA.

Contrasta cada tabla JSON de `aea/catalogo/` contra el PDF original de la norma.
No interpreta la norma: solo comprueba que lo transcripto coincide con lo impreso.

Dos comprobaciones independientes por tabla:

1. NÚMEROS — Todos los valores numéricos de `datos` (recorridos en el orden en que
   están escritos en el JSON) deben aparecer, en ese mismo orden y en forma
   contigua, en el texto del PDF entre las anclas `inicio` y `fin` de la página
   indicada. Detecta valores mal leídos, filas faltantes, filas de más y
   columnas cruzadas.

2. CITAS — Cada cita textual debe existir literalmente (normalizando espacios,
   guiones y comillas) en la página indicada. Detecta condiciones inventadas o
   mal parafraseadas.

Resultado por tabla: OK / FALLA / SIN_PDF. Si un ancla o cita no aparece en la
página declarada, se busca en todo el documento y se informa la página real.

Uso:
    python aea/herramientas/verificar_catalogo.py            # todo el catálogo
    python aea/herramientas/verificar_catalogo.py --json     # salida máquina (CI)

Los PDF no se versionan (copyright). Se buscan en AEA_NORMATIVA/ en la raíz del
repo o en la carpeta indicada por la variable de entorno AEA_PDF_DIR.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import unicodedata
from dataclasses import dataclass, field
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

try:
    import fitz  # PyMuPDF
except ImportError:  # pragma: no cover
    print("Falta PyMuPDF: pip install pymupdf", file=sys.stderr)
    sys.exit(2)

RAIZ_AEA = Path(__file__).resolve().parents[1]
RAIZ_REPO = RAIZ_AEA.parent
CATALOGO = RAIZ_AEA / "catalogo"
TOL = 1e-6


# --------------------------------------------------------------------------- #
# Normalización de texto
# --------------------------------------------------------------------------- #
_REEMPLAZOS = {
    "\u2013": "-", "\u2014": "-", "\u2212": "-",      # guiones
    "\u201c": '"', "\u201d": '"', "\u00ab": '"', "\u00bb": '"',
    "\u2018": "'", "\u2019": "'",
    "\u00ba": "°",                                     # º -> °
    "\u00a0": " ",
}


def normalizar(texto: str) -> str:
    texto = unicodedata.normalize("NFC", texto)
    for a, b in _REEMPLAZOS.items():
        texto = texto.replace(a, b)
    # Unir palabras cortadas por guion al final de línea: "acepta-\nbles" -> "aceptables"
    texto = re.sub(r"(\w)-\s*\n\s*(\w)", r"\1\2", texto)
    texto = re.sub(r"\s+", " ", texto)
    return texto.strip()


# Referencias que contienen dígitos pero no son datos de la tabla.
_RUIDO = [
    r"\b7[0-9]{2}(?:\.[0-9IVXA-Z]+)+",          # cláusulas/tablas: 770.8.3, 770.12.I
    r"AEA[\s-]*[0-9][0-9-]*",                   # AEA 90364-7-771
    r"IRAM(?:[\s-]*NM)?[\s-]*[0-9][0-9-]*",     # IRAM-NM 247-3, IRAM 62267
    r"IEC[\s-]*[0-9][0-9-]*",                   # IEC 60898-1
    r"\bB52-[0-9]+",                            # B52-2
    r"mm2|mm²|m2|m²",                           # unidades con superíndice
]


def numeros_en(texto: str) -> list[float]:
    for patron in _RUIDO:
        texto = re.sub(patron, " ", texto)
    return [float(n.replace(",", ".")) for n in re.findall(r"[0-9]+(?:,[0-9]+)?", texto)]


def numeros_de_datos(valor) -> list[float]:
    """Recorre `datos` en orden de escritura y devuelve los números (ignora texto y null)."""
    if isinstance(valor, bool) or valor is None or isinstance(valor, str):
        return []
    if isinstance(valor, (int, float)):
        return [float(valor)]
    if isinstance(valor, list):
        return [n for v in valor for n in numeros_de_datos(v)]
    if isinstance(valor, dict):
        return [n for v in valor.values() for n in numeros_de_datos(v)]
    return []


def es_subsecuencia_contigua(aguja: list[float], pajar: list[float]) -> int:
    """Índice donde `aguja` aparece contigua en `pajar`, o -1."""
    n = len(aguja)
    for i in range(len(pajar) - n + 1):
        if all(abs(pajar[i + k] - aguja[k]) < TOL for k in range(n)):
            return i
    return -1


# --------------------------------------------------------------------------- #
# Documentos
# --------------------------------------------------------------------------- #
class Documento:
    def __init__(self, ruta: Path):
        self.ruta = ruta
        pdf = fitz.open(ruta)
        self.paginas = [normalizar(p.get_text()) for p in pdf]

    def pagina(self, n: int) -> str:
        return self.paginas[n - 1] if 1 <= n <= len(self.paginas) else ""

    def buscar(self, texto: str) -> list[int]:
        t = normalizar(texto)
        return [i + 1 for i, p in enumerate(self.paginas) if t in p]


def sha256(ruta: Path) -> str:
    h = hashlib.sha256()
    with open(ruta, "rb") as f:
        for bloque in iter(lambda: f.read(1 << 20), b""):
            h.update(bloque)
    return h.hexdigest().upper()


def carpeta_pdfs() -> Path:
    return Path(os.environ.get("AEA_PDF_DIR", RAIZ_REPO / "AEA_NORMATIVA"))


# --------------------------------------------------------------------------- #
# Verificación
# --------------------------------------------------------------------------- #
@dataclass
class Resultado:
    archivo: str
    tabla: str
    estado: str = "OK"
    detalles: list[str] = field(default_factory=list)

    def falla(self, msg: str):
        self.estado = "FALLA"
        self.detalles.append(msg)


def ventana(texto: str, inicio: str | None, fin: str | None, res: Resultado) -> str | None:
    a = 0
    if inicio:
        i = texto.find(normalizar(inicio))
        if i < 0:
            res.falla(f"ancla de inicio no encontrada: «{inicio}»")
            return None
        a = i + len(normalizar(inicio))
    b = len(texto)
    if fin:
        j = texto.find(normalizar(fin), a)
        if j < 0:
            res.falla(f"ancla de fin no encontrada: «{fin}»")
            return None
        b = j
    return texto[a:b]


def verificar_tabla(ruta: Path, docs: dict[str, Documento | None], fuentes: dict) -> Resultado:
    tabla = json.loads(ruta.read_text(encoding="utf-8"))
    res = Resultado(archivo=str(ruta.relative_to(CATALOGO)), tabla=tabla.get("id", "?"))
    fuente = tabla["fuente"]
    doc = docs.get(fuente["doc"])
    if doc is None:
        res.estado = "SIN_PDF"
        res.detalles.append(f"no se encontró el PDF de {fuente['doc']}")
        return res

    pag = fuente["pagina_pdf"]
    texto = doc.pagina(pag)
    ver = tabla.get("verificacion", {})

    # 1) Citas textuales
    for item in ver.get("citas", []):
        # Una cita puede ser texto (misma página que la tabla) o {"texto", "pagina"}.
        cita, pag_cita = (item["texto"], item["pagina"]) if isinstance(item, dict) else (item, pag)
        if normalizar(cita) not in doc.pagina(pag_cita):
            otras = doc.buscar(cita)
            donde = f" (aparece en página {otras})" if otras else " (no aparece en el documento)"
            res.falla(f"cita no encontrada en pág. {pag_cita}{donde}: «{cita[:80]}…»")

    # 2) Secuencia numérica
    esperados = numeros_de_datos(tabla.get("datos"))
    if ver.get("inicio") is not None or ver.get("fin") is not None:
        v = ventana(texto, ver.get("inicio"), ver.get("fin"), res)
        if v is None and ver.get("inicio"):
            otras = doc.buscar(ver["inicio"])
            if otras:
                res.detalles.append(f"   → el ancla de inicio aparece en página(s) {otras}")
        if v is not None and esperados:
            en_pdf = numeros_en(v)
            if es_subsecuencia_contigua(esperados, en_pdf) < 0:
                res.falla("los números del JSON no coinciden con el PDF")
                res.detalles.append(f"   JSON: {fmt(esperados)}")
                res.detalles.append(f"   PDF : {fmt(en_pdf)}")
                primera = primera_diferencia(esperados, en_pdf)
                if primera:
                    res.detalles.append(f"   primera diferencia: {primera}")
    elif esperados and not ver.get("citas"):
        res.falla("tabla sin anclas ni citas: no es verificable")

    return res


def fmt(nums: list[float]) -> str:
    return " ".join(f"{n:g}" for n in nums)


def primera_diferencia(a: list[float], b: list[float]) -> str | None:
    for i, (x, y) in enumerate(zip(a, b)):
        if abs(x - y) > TOL:
            return f"posición {i}: JSON={x:g} PDF={y:g}"
    if len(a) != len(b):
        return f"longitud distinta: JSON={len(a)} PDF={len(b)}"
    return None


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--json", action="store_true", help="salida JSON")
    args = ap.parse_args()

    fuentes = json.loads((CATALOGO / "fuentes.json").read_text(encoding="utf-8"))["documentos"]
    docs: dict[str, Documento | None] = {}
    avisos: list[str] = []
    for clave, meta in fuentes.items():
        ruta = carpeta_pdfs() / meta["archivo"]
        if not ruta.exists():
            docs[clave] = None
            continue
        h = sha256(ruta)
        if h != meta["sha256"].upper():
            avisos.append(f"{clave}: el PDF no coincide con el SHA-256 registrado (¿otra edición?)")
        docs[clave] = Documento(ruta)

    tablas = sorted(p for p in CATALOGO.rglob("*.json") if p.name != "fuentes.json")
    resultados = [verificar_tabla(p, docs, fuentes) for p in tablas]

    if args.json:
        print(json.dumps({"avisos": avisos, "resultados": [r.__dict__ for r in resultados]},
                         ensure_ascii=False, indent=2))
    else:
        for a in avisos:
            print(f"⚠  {a}")
        iconos = {"OK": "✅", "FALLA": "❌", "SIN_PDF": "⚪"}
        for r in resultados:
            print(f"{iconos[r.estado]} {r.tabla:<10} {r.archivo}")
            for d in r.detalles:
                print(f"     {d}")
        ok = sum(r.estado == "OK" for r in resultados)
        print(f"\n{ok}/{len(resultados)} tablas verificadas contra el PDF.")

    return 0 if all(r.estado != "FALLA" for r in resultados) else 1


if __name__ == "__main__":
    sys.exit(main())
