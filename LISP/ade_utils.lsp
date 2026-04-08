;; ============================================================
;;  ade_utils.lsp  —  Utilidades compartidas AD-ELEC
;;  Cargado automáticamente por el plugin C# al iniciar.
;; ============================================================

;; ── Asegurar que una capa existe ────────────────────────────
(defun ade:ensure-layer (nombre color / )
  (if (not (tblsearch "LAYER" nombre))
    (entmake (list
      (cons 0  "LAYER")
      (cons 100 "AcDbSymbolTableRecord")
      (cons 100 "AcDbLayerTableRecord")
      (cons 2  nombre)
      (cons 70 0)
      (cons 62 color)
    ))
  )
)

;; ── Leer atributo de un bloque por tag ──────────────────────
(defun ade:get-att (ent tag / atts)
  (setq atts (vlax-invoke
               (vlax-ename->vla-object ent) 'GetAttributes))
  (vl-some '(lambda (a)
              (if (= (strcase (vla-get-TagString a))
                     (strcase tag))
                (vla-get-TextString a)))
           atts)
)

;; ── Insertar bloque con atributos ───────────────────────────
;; atts-alist: lista de (tag . valor), ej: '(("CX" . "C1") ("D" . "TD1"))
(defun ade:insert-block (nombre pt rot atts-alist / br att-objs att tag)
  (setq br (vla-InsertBlock
             (vlax-get-property
               (vla-get-ActiveDocument (vlax-get-acad-object))
               'ModelSpace)
             (vlax-3d-point pt)
             nombre
             1.0 1.0 1.0
             rot))
  (setq att-objs (vlax-invoke br 'GetAttributes))
  (foreach att att-objs
    (setq tag (strcase (vla-get-TagString att)))
    (foreach pair atts-alist
      (if (= tag (strcase (car pair)))
        (vla-put-TextString att (cdr pair))
      )
    )
  )
  br
)

;; ── Punto en polígono (ray-casting 2D) ──────────────────────
;; pt: '(x y)   polygon: lista de '(x y)
(defun ade:point-in-polygon (pt polygon / n i j xi yi xj yj inside px py)
  (setq px (car pt) py (cadr pt)
        n  (length polygon)
        i  0
        j  (1- n)
        inside nil)
  (while (< i n)
    (setq xi (car  (nth i polygon))
          yi (cadr (nth i polygon))
          xj (car  (nth j polygon))
          yj (cadr (nth j polygon)))
    (if (and (/= (> yi py) (> yj py))
             (< px (+ (* (/ (- xj xi) (- yj yi)) (- py yi)) xi)))
      (setq inside (not inside))
    )
    (setq j i
          i (1+ i))
  )
  inside
)

;; ── Extraer vértices de una polilínea como lista '(x y) ─────
(defun ade:poly-vertices (ent / vla n i vpt result)
  (setq vla    (vlax-ename->vla-object ent)
        n      (vla-get-NumberOfVertices vla)
        i      0
        result '())
  (repeat n
    (setq vpt (vlax-invoke vla 'GetPoint i))
    (setq result (append result
                   (list (list (vlax-safearray-get-element vpt 0)
                               (vlax-safearray-get-element vpt 1)))))
    (setq i (1+ i))
  )
  result
)

;; ── Bounding box de una polilínea ───────────────────────────
;; Retorna (minX minY maxX maxY)
(defun ade:poly-bbox (ent / verts xs ys)
  (setq verts (ade:poly-vertices ent)
        xs    (mapcar 'car  verts)
        ys    (mapcar 'cadr verts))
  (list (apply 'min xs) (apply 'min ys)
        (apply 'max xs) (apply 'max ys))
)

;; ── Verificar que un bloque está definido en el DWG ─────────
(defun ade:block-exists (nombre)
  (tblsearch "BLOCK" nombre)
)

;; ── Recolectar inserciones de un bloque por atributo CX ─────
;; Retorna lista de puntos de inserción '(x y z)
(defun ade:get-blocks-by-cx (block-name cx / ss n i ent cx-val pts)
  (setq pts '())
  (if (setq ss (ssget "X" (list (cons 0 "INSERT")
                                (cons 2 block-name))))
    (progn
      (setq n (sslength ss) i 0)
      (while (< i n)
        (setq ent    (ssname ss i)
              cx-val (ade:get-att ent "CX"))
        (if (and cx-val (= (strcase cx-val) (strcase cx)))
          (setq pts (append pts
                     (list (cdr (assoc 10 (entget ent))))))
        )
        (setq i (1+ i))
      )
    )
  )
  pts
)

(princ "\n[AD-ELEC] ade_utils.lsp cargado.")
(princ)
