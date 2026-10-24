# AUTOambientes — Especificación técnica

## Contexto del sistema existente

Existe una aplicación web de diseño de instalaciones eléctricas conforme a normativa argentina. La aplicación permite:

- Cargar archivos DXF y redibujar ambientes sobre ellos como referencia visual.
- Dibujar ambientes directamente sin referencia.
- Recibir ambientes desde AutoCAD mediante un complemento existente: el usuario selecciona manualmente polilíneas cerradas en AutoCAD, define el tipo de ambiente y las envía a la aplicación web.

El cuello de botella actual es que el usuario debe redibujar o seleccionar manualmente cada ambiente en AutoCAD antes de enviarlo. El objetivo es eliminar ese paso.

---

## Objetivo

Implementar un comando `AUTOambientes` en AutoCAD que, dado un DWG de planta de edificio:

1. Detecte automáticamente los perímetros de todos los ambientes como polilíneas cerradas.
2. Clasifique cada ambiente según su tipo.
3. Calcule el área de cada ambiente en metros cuadrados.
4. Agrupe los ambientes en unidades funcionales.
5. Presente los resultados al usuario para verificación y corrección manual.
6. Envíe los datos verificados a la aplicación web existente mediante la misma interfaz que usa el complemento actual.

---

## Taxonomía de tipos

### Unidades funcionales (nivel de agrupamiento)

```
Departamento
Oficina
Local comercial
Cochera (unidad)
Escalera (núcleo)
Ascensor (núcleo)
Área común
```

### Ambientes (nivel de espacio)

```
Baño
Toilette
Cocina
Estar
Comedor
Estar-Comedor
Monoambiente
Dormitorio
Balcón
Cochera (espacio individual)
Palier
Hall
Salón de Usos Múltiples (SUM)
Estudio
Paso / Pasillo
```

---

## Arquitectura del sistema

El sistema se divide en tres componentes desacoplados:

```
[AutoCAD] -- comando AUTOambientes
     |
     v
[Motor de detección] -- proceso Python externo o .NET embebido
     |
     v
[Panel de verificación] -- UI de revisión y corrección
     |
     v
[API de la aplicación web] -- mismo endpoint que usa el complemento actual
```

---

## Implementación detallada

### Componente 1: Interfaz con AutoCAD

Dos modalidades según entorno:

**Modalidad A — Plugin .NET (ObjectARX / AutoCAD .NET API)**

Apropiada para distribución como complemento nativo. El comando `AUTOambientes` se registra en AutoCAD mediante `[CommandMethod]`. El plugin extrae las entidades del modelspace, las serializa a JSON y lanza el motor Python como subproceso, o lo llama mediante un servidor local HTTP.

```csharp
// Registro del comando
[CommandMethod("AUTOambientes")]
public void EjecutarAutoAmbientes()
{
    Document doc = Application.DocumentManager.MdiActiveDocument;
    Database db = doc.Database;
    // Extraer entidades y delegar al motor de detección
}
```

Dependencias: AutoCAD .NET API (`acmgd.dll`, `acdbmgd.dll`), .NET Framework 4.8 o .NET 6+.

**Modalidad B — AutoLISP + servidor Python local**

Menor fricción de instalación. Un archivo `.lsp` define el comando y realiza llamadas HTTP a un servidor Python local (FastAPI/Flask) que ejecuta el motor de detección. Compatible con AutoCAD LT 2024+.

```lisp
(defun c:AUTOambientes ()
  ; Exportar DXF temporal y llamar al motor externo
)
```

Recomendación: implementar Modalidad B primero para validar el motor, luego envolver en Modalidad A para producción.

---

### Componente 2: Motor de detección (Python)

#### 2.1 Parseo del DXF

Librería principal: `ezdxf >= 1.1`.

El motor recibe la ruta al DXF (o el archivo exportado temporalmente desde el DWG). Extrae las entidades del modelspace filtrando por tipo y capa.

Entidades relevantes:
- `LWPOLYLINE` cerradas (`is_closed == True`): candidatos directos a perímetros de ambiente.
- `LINE`: segmentos sueltos que pueden conformar perímetros al unirse.
- `HATCH`: indica zona rellena, útil como señal de ambiente.
- `TEXT` / `MTEXT`: etiquetas que pueden indicar el tipo de ambiente.
- `INSERT`: bloques que representan sanitarios, cocinas, escaleras — útiles para inferencia de tipo.

```python
import ezdxf

doc = ezdxf.readfile("planta.dxf")
msp = doc.modelspace()

polilineas_cerradas = [
    e for e in msp.query("LWPOLYLINE")
    if e.dxf.flags & 1  # closed flag
]
```

#### 2.2 Reconstrucción de perímetros desde segmentos

Cuando los muros están dibujados como `LINE` individuales (caso frecuente en planos argentinos), es necesario reconstruir los polígonos cerrados.

Proceso:
1. Extraer todos los segmentos de capas de muro (identificadas por nombre: `MURO`, `PARED`, `WALL`, o configurables por el usuario).
2. Construir un grafo donde los nodos son endpoints y las aristas son segmentos.
3. Detectar ciclos en el grafo para encontrar perímetros cerrados.
4. Aplicar tolerancia de snap (configurable, default 5mm en escala del dibujo) para unir endpoints próximos.

Librerías: `networkx >= 3.0`, `shapely >= 2.0`.

```python
from shapely.ops import unary_union, polygonize
from shapely.geometry import LineString, MultiLineString

lineas = [LineString([e.dxf.start[:2], e.dxf.end[:2]]) for e in segmentos_muro]
multi = unary_union(lineas)
poligonos = list(polygonize(multi))
```

#### 2.3 Cálculo de escala y área

El DXF puede estar en distintas unidades (mm, cm, m). La escala se determina:
- Leyendo `$INSUNITS` del header del DXF.
- Como fallback: detectando un bloque de escala gráfica o solicitándola al usuario.

```python
INSUNITS_A_METROS = {
    1: 0.0254,   # inches
    2: 0.0254 * 12,  # feet
    4: 0.001,    # mm
    5: 0.01,     # cm
    6: 1.0,      # m
}

unidades = doc.header.get("$INSUNITS", 4)
factor = INSUNITS_A_METROS.get(unidades, 0.001)

area_m2 = poligono.area * (factor ** 2)
```

#### 2.4 Clasificación de ambientes

La clasificación opera en tres niveles de confianza, aplicados en orden:

**Nivel 1 — Texto explícito (alta confianza)**

Buscar entidades `TEXT`/`MTEXT` cuyo punto de inserción esté contenido dentro del polígono. Aplicar un diccionario de normalización sobre el texto encontrado:

```python
MAPA_TIPOS = {
    "baño": "Baño",
    "bano": "Baño",
    "toilette": "Toilette",
    "toilet": "Toilette",
    "cocina": "Cocina",
    "estar": "Estar",
    "comedor": "Comedor",
    "estar comedor": "Estar-Comedor",
    "living": "Estar",
    "dormitorio": "Dormitorio",
    "habitacion": "Dormitorio",
    "balcon": "Balcón",
    "terraza": "Balcón",
    "palier": "Palier",
    "hall": "Hall",
    "sum": "Salón de Usos Múltiples",
    "salon": "Salón de Usos Múltiples",
    "estudio": "Estudio",
    "pasillo": "Paso / Pasillo",
    "paso": "Paso / Pasillo",
    "cochera": "Cochera",
    "garage": "Cochera",
    "escalera": "Escalera",
    "ascensor": "Ascensor",
}
```

**Nivel 2 — Bloques contenidos (media confianza)**

Detectar entidades `INSERT` dentro del polígono y mapear el nombre del bloque a indicadores de tipo:

```python
BLOQUES_INDICADORES = {
    "inodoro": "Baño",
    "WC": "Baño",
    "bañera": "Baño",
    "ducha": "Baño",
    "pileta": "Cocina",
    "heladera": "Cocina",
    "escalera": "Escalera",
    "ascensor": "Ascensor",
}
```

**Nivel 3 — Geometría y contexto (baja confianza, requiere verificación)**

Heurísticas basadas en métricas del polígono:
- Área < 4 m² + relación de aspecto estrecha → Paso / Pasillo
- Área < 6 m² + forma compacta → Baño o Toilette
- Área > 100 m² + forma regular → SUM o área común
- Polígono sin conexión por puertas a otros ambientes → Cochera o Escalera

Para cada resultado de Nivel 3, el flag `requiere_verificacion = True` se activa.

#### 2.5 Detección de puertas y conectividad

Las puertas se detectan como:
- Bloques `INSERT` con nombres que contengan "puerta", "door", "abertura".
- Arcos (`ARC`) de radio típico de puerta (0.7m–1.2m).

Con las puertas localizadas se construye el grafo de conectividad entre ambientes: dos polígonos están conectados si comparten una puerta en su perímetro común (dentro de tolerancia).

```python
import networkx as nx

G = nx.Graph()
for amb in ambientes:
    G.add_node(amb.id, tipo=amb.tipo, area=amb.area_m2)

for puerta in puertas:
    amb_a, amb_b = encontrar_ambientes_adyacentes(puerta, ambientes)
    if amb_a and amb_b:
        G.add_edge(amb_a.id, amb_b.id)
```

#### 2.6 Agrupamiento en unidades funcionales

Una unidad funcional es un subgrafo conexo del grafo de ambientes que:
- Contiene exactamente un acceso desde el palier/hall (puerta al espacio común).
- Tiene al menos un baño o toilette (para ser clasificado como departamento u oficina).
- No contiene núcleos verticales (escaleras, ascensores) — estos son unidades propias.

Algoritmo:
1. Identificar nodos de acceso: ambientes del tipo Palier, Hall, o que tengan contacto con el perímetro del polígono envolvente del piso.
2. Remover esos nodos del grafo.
3. Los componentes conexos resultantes son candidatos a unidades funcionales.
4. Clasificar cada componente: contiene cocina + baño → Departamento; contiene baño sin cocina → Oficina; sin ninguno → Local.

---

### Componente 3: Panel de verificación

El resultado del motor se presenta al usuario antes del envío. La interfaz puede ser:

**Opción A — Panel dentro de AutoCAD**

Paleta de AutoCAD (`PaletteSet`) construida con WinForms o WPF embebido en el plugin .NET. Muestra la lista de ambientes detectados con su tipo y área. Permite editar el tipo mediante un dropdown con la taxonomía completa. Resalta en el dibujo el ambiente seleccionado en la lista.

**Opción B — Interfaz web local**

El motor Python sirve una página HTML en `localhost` con la lista de resultados. El usuario la revisa en el navegador, hace correcciones y confirma. Esta opción es más fácil de mantener y no requiere compilar código .NET.

Recomendación: Opción B para la primera versión, Opción A para integración profunda posterior.

---

### Componente 4: Envío a la aplicación web

El payload enviado replica exactamente la estructura que usa el complemento actual, extendida con los campos nuevos:

```json
{
  "unidades_funcionales": [
    {
      "id": "UF-01",
      "tipo": "Departamento",
      "piso": 1,
      "ambientes": [
        {
          "id": "AMB-001",
          "tipo": "Estar-Comedor",
          "area_m2": 28.4,
          "perimetro_m": 22.1,
          "polilinea": [[x1,y1],[x2,y2],...],
          "confianza": "alta",
          "requiere_verificacion": false
        },
        {
          "id": "AMB-002",
          "tipo": "Dormitorio",
          "area_m2": 12.8,
          "perimetro_m": 14.4,
          "polilinea": [[x1,y1],[x2,y2],...],
          "confianza": "media",
          "requiere_verificacion": true
        }
      ],
      "area_total_m2": 68.5
    }
  ],
  "areas_comunes": [
    {
      "id": "AC-01",
      "tipo": "Palier",
      "piso": 1,
      "area_m2": 8.2,
      "polilinea": [[x1,y1],[x2,y2],...]
    }
  ],
  "nucleos_verticales": [
    {
      "id": "NV-01",
      "tipo": "Escalera",
      "polilinea": [[x1,y1],[x2,y2],...]
    }
  ],
  "metadatos": {
    "archivo_origen": "edificio_calle_falsa_123.dwg",
    "escala_factor": 0.001,
    "insunits": 4,
    "total_ambientes": 24,
    "ambientes_requieren_verificacion": 3,
    "fecha_procesamiento": "2025-04-18T14:30:00Z"
  }
}
```

---

## Stack tecnológico

### Motor de detección

| Componente | Librería | Versión mínima |
|---|---|---|
| Parseo DXF | `ezdxf` | 1.1 |
| Geometría y área | `shapely` | 2.0 |
| Grafo de conectividad | `networkx` | 3.0 |
| Servidor local HTTP | `fastapi` + `uvicorn` | 0.100 |
| Serialización | `pydantic` | 2.0 |
| Cliente HTTP (envío) | `httpx` | 0.24 |
| Normalización de texto | `unidecode` | 1.3 |

### Plugin AutoCAD

| Componente | Tecnología |
|---|---|
| Comando AutoCAD | AutoCAD .NET API o AutoLISP |
| Comunicación con motor | HTTP local (localhost) |
| Panel de verificación | WinForms embebido o navegador local |

---

## Consideraciones críticas de implementación

### Convenciones de dibujo y robustez

Los planos DXF argentinos no tienen una convención única. El motor debe ser configurable y no asumir nombres de capas estándar. Proveer un archivo de configuración (`autoambientes_config.json`) donde el usuario mapea sus capas a los tipos del sistema:

```json
{
  "capas_muros": ["MUROS", "PARED", "A-WALL", "muro"],
  "capas_puertas": ["PUERTAS", "ABERTURAS", "A-DOOR"],
  "capas_texto_ambiente": ["TEXTO", "NOTAS", "A-TEXT"],
  "tolerancia_snap_mm": 5
}
```

### Polígonos con huecos

En planos de plantas con escaleras o ascensores, el polígono del piso puede tener huecos (holes en Shapely). Usar `Polygon.exterior` para el perímetro exterior y `Polygon.interiors` para los núcleos internos.

### Escala del dibujo

Es crítico determinar la escala correcta antes de calcular áreas. Un error en la escala produce resultados silenciosamente incorrectos. Implementar una validación heurística: si el área promedio de los polígonos detectados es menor a 1 m² o mayor a 10.000 m², advertir al usuario y solicitar confirmación de escala.

### Polilíneas con segmentos curvos (bulge)

`LWPOLYLINE` puede contener segmentos con `bulge != 0` (arcos). Shapely no maneja arcos directamente. Tesselar los segmentos curvos a segmentos rectos con una tolerancia configurable antes de construir el polígono de Shapely.

```python
def lwpolyline_a_shapely(entidad, tolerancia_arco=0.01):
    puntos = list(entidad.vertices())
    # tesselación de segmentos con bulge
    return Polygon(puntos_tesselados)
```

### Superposición de polígonos

Es posible que el algoritmo genere polígonos superpuestos (por ejemplo, el muro exterior detectado como ambiente). Filtrar polígonos que contengan completamente a otros polígonos del conjunto, conservando el más interior.

### Retroalimentación para mejora del modelo

Si el usuario realiza correcciones en el paso de verificación, registrar el par `{detección_original, corrección_usuario}`. Estos datos son el insumo para mejorar el clasificador con el tiempo. Almacenar en la aplicación web asociados al archivo fuente, sin requerir ninguna acción adicional del usuario.

---

## Estructura de archivos del proyecto

```
autoambientes/
├── motor/
│   ├── main.py               # entrypoint FastAPI
│   ├── parser_dxf.py         # extracción de entidades con ezdxf
│   ├── reconstruccion.py     # unión de segmentos y poligonización
│   ├── clasificador.py       # clasificación de ambientes
│   ├── agrupador.py          # detección de unidades funcionales
│   ├── escala.py             # detección y conversión de unidades
│   ├── modelos.py            # modelos Pydantic del payload
│   └── config.py             # carga de autoambientes_config.json
├── plugin_autocad/
│   ├── AUTOambientes.lsp     # comando AutoLISP (Modalidad B)
│   └── AUTOambientes.csproj  # plugin .NET (Modalidad A)
├── verificacion/
│   └── index.html            # panel de verificación web local
├── autoambientes_config.json # configuración de capas y tolerancias
└── requirements.txt
```

---

## Flujo de ejecución completo

```
Usuario ejecuta AUTOambientes en AutoCAD
  |
  +-- Plugin exporta DXF temporal del dibujo activo
  |
  +-- Plugin lanza POST /procesar al servidor motor local
        |
        +-- parser_dxf.py extrae entidades relevantes
        |
        +-- reconstruccion.py genera polígonos Shapely
        |
        +-- escala.py determina factor de conversión a m²
        |
        +-- clasificador.py asigna tipo a cada polígono
        |     (texto > bloques > heurísticas)
        |
        +-- agrupador.py construye grafo y detecta UF
        |
        +-- Motor devuelve JSON estructurado
              |
              +-- Plugin abre panel de verificación
                    |
                    +-- Usuario revisa, corrige si es necesario, confirma
                          |
                          +-- Plugin envía payload a API de aplicación web
                                |
                                +-- Aplicación web recibe y continúa el flujo
                                      de diseño de instalación eléctrica
```

---

## Dependencias externas a instalar

```
pip install ezdxf>=1.1 shapely>=2.0 networkx>=3.0 fastapi>=0.100 uvicorn>=0.23 pydantic>=2.0 httpx>=0.24 unidecode>=1.3
```

Para el plugin .NET (Modalidad A), referencia los ensamblados de AutoCAD desde la instalación local. No distribuir los DLLs de Autodesk con el plugin.

---

## Puntos de extensión futuros

- Incorporar Cascade Mask R-CNN para clasificación por imagen (ver paper LIFIA/UNLP ASAID 2024) como alternativa al clasificador por reglas cuando el DXF no tiene texto de etiqueta.
- Exportar resultado como `IfcSpace` para integración BIM (IfcOpenShell).
- Dataset de correcciones de usuario como insumo para fine-tuning del clasificador.
- Soporte para planos de múltiples plantas en un mismo DWG (detección por viewport o por capa de piso).
