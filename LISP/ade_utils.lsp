;; ============================================================
;;  ade_utils.lsp  -  Utilidades compartidas AD-ELEC
;;  Cargado automaticamente por el plugin C# al iniciar.
;; ============================================================

;; -- Asegurar que una capa existe ----------------------------
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

;; -- Leer atributo de un bloque por tag ----------------------
(defun ade:get-att (ent tag / atts)
  (setq atts (vlax-invoke
               (vlax-ename->vla-object ent) 'GetAttributes))
  (vl-some '(lambda (a)
              (if (= (strcase (vla-get-TagString a))
                     (strcase tag))
                (vla-get-TextString a)))
           atts)
)

;; -- Insertar bloque con atributos ---------------------------
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

;; -- Punto en poligono (ray-casting 2D) ----------------------
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

;; -- Extraer vertices de una polilinea como lista '(x y) -----
;; Usa entget con grupo 10 (valido para LWPOLYLINE sin VLA)
(defun ade:poly-vertices (ent / ed result pair)
  (setq ed     (entget ent)
        result '())
  (foreach pair ed
    (if (= (car pair) 10)
      (setq result (append result
                     (list (list (cadr pair) (caddr pair)))))
    )
  )
  result
)

;; -- Bounding box de una polilinea ---------------------------
;; Retorna (minX minY maxX maxY)
(defun ade:poly-bbox (ent / verts xs ys)
  (setq verts (ade:poly-vertices ent)
        xs    (mapcar 'car  verts)
        ys    (mapcar 'cadr verts))
  (list (apply 'min xs) (apply 'min ys)
        (apply 'max xs) (apply 'max ys))
)

;; -- Verificar que un bloque esta definido en el DWG ---------
(defun ade:block-exists (nombre)
  (tblsearch "BLOCK" nombre)
)

;; -- Recolectar inserciones de un bloque por atributo CX -----
;; Retorna lista de puntos de insercion '(x y z)
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

;; -- str2lst -------------------------------------------------
;; Convierte una cadena con separadores en lista de cadenas.
;; (str2lst "a b c" " ") -> ("a" "b" "c")
(defun ade:str2lst (str sep / pos)
  (if (setq pos (vl-string-search sep str))
    (cons (substr str 1 pos)
          (ade:str2lst (substr str (+ (strlen sep) pos 1)) sep))
    (list str)
  )
)

;; -- listbox --------------------------------------------------
;; Dialogo de lista de seleccion generado dinamicamente.
;; keylab: ((clave . etiqueta) ...)
;; flag: 0=popup  1=unica  2=multiple
;; Retorna: clave (flag 0/1) o lista de claves (flag 2)
(defun ade:listbox (title msg keylab flag / tmp file dcl_id choice)
  (setq tmp  (vl-filename-mktemp "tmp.dcl")
        file (open tmp "w"))
  (write-line (strcat "ListBox:dialog{label=\"" title "\";") file)
  (if (and msg (/= msg ""))
    (write-line (strcat ":text{label=\"" msg "\";}") file))
  (write-line
    (cond
      ((= 0 flag) "spacer;:popup_list{key=\"lst\";")
      ((= 1 flag) "spacer;:list_box{key=\"lst\";")
      (T          "spacer;:list_box{key=\"lst\";multiple_select=true;"))
    file)
  (write-line "}spacer;ok_cancel;}" file)
  (close file)
  (setq dcl_id (load_dialog tmp))
  (if (not (new_dialog "ListBox" dcl_id)) (exit))
  (start_list "lst")
  (mapcar 'add_list (mapcar 'cdr keylab))
  (end_list)
  (action_tile
    "accept"
    "(or (= (get_tile \"lst\") \"\")
    (if (= 2 flag) (progn
    (foreach n (ade:str2lst (get_tile \"lst\") \" \")
    (setq choice (cons (nth (atoi n) (mapcar 'car keylab)) choice)))
    (setq choice (reverse choice)))
    (setq choice (nth (atoi (get_tile \"lst\")) (mapcar 'car keylab)))))
    (done_dialog)"
  )
  (start_dialog)
  (unload_dialog dcl_id)
  (vl-file-delete tmp)
  choice
)

;; -- ADE_SCAN -------------------------------------------------
;; Escanea todos los bloques electricos del DWG y reporta
;; conteo y potencia por circuito (CX) y tablero (D).
;; Util para verificar antes de sincronizar con AEA-MOTOR.
(defun c:ADE_SCAN ( / blk-tipos ss n i ent bname atts cx d pot tipo
                     report-map key entry total-bloques sin-pot)
  (vl-load-com)
  (setq blk-tipos
    '(("I.E-AD-07" . "IUG") ("I.E-AD-08" . "IUE")
      ("I.E-AD-09" . "TUG") ("I.E-AD-09.02" . "TUE")
      ("I.E-AD-01" . "SWITCH"))
    report-map    '()
    total-bloques 0
    sin-pot       0)

  (princ "\n-- ADE_SCAN ------------------------------------------")
  (princ "\n  Escaneando bloques electricos del DWG...\n")

  (foreach pair blk-tipos
    (setq bname (car pair) tipo (cdr pair))
    (if (setq ss (ssget "X" (list (cons 0 "INSERT") (cons 2 bname))))
      (progn
        (setq n (sslength ss) i 0)
        (while (< i n)
          (setq ent  (ssname ss i)
                cx   (ade:get-att ent "CX")
                d    (ade:get-att ent "D")
                pot  (ade:get-att ent "POT")
                i    (1+ i))
          (if (not cx)  (setq cx  "?"))
          (if (not d)   (setq d   "?"))
          (if (or (not pot) (= pot "")) (progn (setq pot "0") (setq sin-pot (1+ sin-pot))))
          (setq key   (strcat d "|" cx "|" tipo)
                entry (assoc key report-map))
          (if entry
            (setq report-map
              (subst (list key (1+ (cadr entry)) (+ (caddr entry) (atof pot)))
                     entry report-map))
            (setq report-map
              (cons (list key 1 (atof pot)) report-map))
          )
          (setq total-bloques (1+ total-bloques))
        )
      )
    )
  )

  ;; -- Imprimir reporte agrupado ----------------------------
  (if (= total-bloques 0)
    (princ "  [Sin bloques electricos encontrados]\n")
    (progn
      (princ (strcat "  Total bloques : " (itoa total-bloques) "\n"))
      (if (> sin-pot 0)
        (princ (strcat "  *** " (itoa sin-pot) " bloque(s) sin atributo POT (potencia=0) ***\n")))
      (princ "\n")
      (princ (strcat "  " (ade:pad-right "Tablero" 8)
                          (ade:pad-right "Circ." 7)
                          (ade:pad-right "Tipo" 8)
                          (ade:pad-right "Cant." 6)
                          "Pot.Total(VA)\n"))
      (princ (strcat "  " (make-string 42 45) "\n"))  ;; 42 guiones
      (foreach entry (vl-sort report-map
                        '(lambda (a b) (< (car a) (car b))))
        (setq key   (car entry)
              parts (ade:str2lst key "|"))
        (princ (strcat "  " (ade:pad-right (nth 0 parts) 8)
                            (ade:pad-right (nth 1 parts) 7)
                            (ade:pad-right (nth 2 parts) 8)
                            (ade:pad-right (itoa (cadr entry)) 6)
                            (rtos (caddr entry) 2 0) " VA\n"))
      )
    )
  )
  (princ "------------------------------------------------------\n")
  (princ)
)

;; -- Pad right (rellena con espacios hasta n caracteres) -----
(defun ade:pad-right (str n / len)
  (setq len (strlen str))
  (if (>= len n)
    (substr str 1 n)
    (strcat str (make-string (- n len) 32))
  )
)

(princ "\n[AD-ELEC] ade_utils.lsp cargado.")
(princ)
