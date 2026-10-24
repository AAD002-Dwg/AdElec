;; ============================================================
;;  ade_nomenclatura.lsp  -  ADE_NOMENCLATURA  v2.0
;;  Etiquetado automatico de circuitos con nomenclatura de cables.
;;
;;  Bloque: I.E-AD-Nomenclatura_Cables
;;    Atributos:  CABLES  (ej. "2x1,5+T")
;;                CA ERIA (ej. "RS")
;;
;;  Modos:
;;    Auto Luminarias -> etiqueta todos los arcos en capa Canerias_Luz
;;                       que pertenecen al circuito CX
;;    Auto Tomas      -> etiqueta todos los segmentos de la polilinea
;;                       en capa Canalizacion_CX
;;    Manual          -> usuario selecciona arcos/polilineas,
;;                       el bloque se coloca en el punto medio de cada uno
;;
;;  Para cada entidad:
;;    - Calcula el punto medio geometrico
;;    - Alinea el bloque con la direccion del cable
;;    - Normaliza angulo para que el texto sea siempre legible
;;
;;  Shortcut:  WN -> ADE_NOMENCLATURA
;;
;;  Logica de geometria portada desde cable_labels.py (ELEC-AD)
;; ============================================================

(vl-load-com)

;; -- VARIABLES GLOBALES DE PERSISTENCIA -----------------------
(if (not *ADE:NOM:MODO*)     (setq *ADE:NOM:MODO*     "Luminarias"))
(if (not *ADE:NOM:CX*)       (setq *ADE:NOM:CX*       "C1"))
(if (not *ADE:NOM:CABLES-L*) (setq *ADE:NOM:CABLES-L* "2x1,5+T"))
(if (not *ADE:NOM:CABLES-T*) (setq *ADE:NOM:CABLES-T* "2x2,5+T"))
(if (not *ADE:NOM:CABLES-R*) (setq *ADE:NOM:CABLES-R* "2x1,5+T"))
(if (not *ADE:NOM:CANERIA*)  (setq *ADE:NOM:CANERIA*  "RS"))

(setq *ADE:NOM:BLK-NAME* "I.E-AD-Nomenclatura_Cables")

;; Busqueda dinamica del DWG del bloque
(defun ade:nom:get-block-dwg ( / candidates p)
  (setq candidates
    (list
      (strcat (getvar "DWGPREFIX") "blocks/" *ADE:NOM:BLK-NAME* ".dwg")
      (strcat (getvar "DWGPREFIX") *ADE:NOM:BLK-NAME* ".dwg")
      (strcat "g:/PROYECTOS AD/AD-ELEC/blocks/" *ADE:NOM:BLK-NAME* ".dwg")
      (strcat "g:/AD-ELEC/Bloques_CAD/" *ADE:NOM:BLK-NAME* ".dwg")
    )
  )
  (setq p (vl-some '(lambda (c) (if (findfile c) (findfile c))) candidates))
  (if p p (strcat "g:/PROYECTOS AD/AD-ELEC/blocks/" *ADE:NOM:BLK-NAME* ".dwg"))
)

(setq *ADE:NOM:BLK-DWG* (ade:nom:get-block-dwg))

;; -- Cache ActiveDocument -------------------------------------
(defun ade:nom:acdoc ()
  (eval (list 'defun 'ade:nom:acdoc 'nil
    (vla-get-activedocument (vlax-get-acad-object))))
  (ade:nom:acdoc)
)

;; -- EndUndo seguro -------------------------------------------
(defun ade:nom:endundo ()
  (while (= 8 (logand 8 (getvar "UNDOCTL")))
    (vla-endundomark (ade:nom:acdoc))
  )
)

;; =============================================================
;;  GEOMETRIA - portada desde cable_labels.py
;; =============================================================

;; -- Punto medio y angulo de un segmento recto ---------------
;; Entrada: p1, p2 como '(x y)
;; Salida:  (mid-pt ang-rad)   mid-pt = '(x y)
(defun ade:nom:geom-line (p1 p2 /  mx my ang)
  (setq mx  (/ (+ (car p1)  (car p2))  2.0)
        my  (/ (+ (cadr p1) (cadr p2)) 2.0)
        ang (atan (- (cadr p2) (cadr p1))
                  (- (car p2)  (car p1))))
  (list (list mx my) ang)
)

;; -- Punto medio y angulo de un arco por bulge (LWPolyline) --
;; Bulge = tan(angulo_incluido / 4)
;; Apice = punto medio de la cuerda desplazado por la sagitta
;; Angulo tangente en apice = angulo de la cuerda (paralelo)
(defun ade:nom:geom-bulge (p1 p2 bulge /
                            dx dy mx my ox oy ax ay ang)
  (setq dx (- (car p2)  (car p1))
        dy (- (cadr p2) (cadr p1))
        mx (/ (+ (car p1)  (car p2)) 2.0)
        my (/ (+ (cadr p1) (cadr p2)) 2.0)
        ;; Desplazamiento ortogonal a la cuerda (sagitta)
        ox (* (- dy) (/ bulge 2.0))
        oy (* dx     (/ bulge 2.0))
        ax (+ mx ox)
        ay (+ my oy)
        ang (atan dy dx))
  (list (list ax ay) ang)
)

;; -- Punto medio y angulo de una entidad ARC -----------------
;; Retorna (mid-pt ang-rad) donde ang-rad es la tangente en mid
(defun ade:nom:geom-arc (ent / ed cx cy r sa ea ma mx my tang)
  (setq ed (entget ent)
        cx (cadr  (assoc 10 ed))
        cy (caddr (assoc 10 ed))
        r  (cdr   (assoc 40 ed))
        sa (cdr   (assoc 50 ed))
        ea (cdr   (assoc 51 ed)))
  ;; Envolver angulo si end < start
  (if (< ea sa) (setq ea (+ ea (* 2.0 pi))))
  (setq ma   (/ (+ sa ea) 2.0)
        mx   (+ cx (* r (cos ma)))
        my   (+ cy (* r (sin ma)))
        ;; Tangente al arco en mid-angle = mid-angle + pi/2
        tang (+ ma (/ pi 2.0)))
  (list (list mx my) tang)
)

;; -- Normalizar angulo para texto legible (nunca invertido) ---
;; Si el angulo cae entre 90 y 270 grados, rotamos 180 grados.
;; Equivalente al ajuste de _insert_label_block en cable_labels.py
(defun ade:nom:normalize-angle (ang / deg)
  (setq deg (rem (* ang (/ 180.0 pi)) 360.0))
  (if (< deg 0.0) (setq deg (+ deg 360.0)))
  (if (and (> deg 90.0) (< deg 270.0))
    (+ ang pi)
    ang
  )
)

;; =============================================================
;;  INSERCION DEL BLOQUE
;; =============================================================

;; -- Cargar bloque desde DWG si no esta definido -------------
(defun ade:nom:ensure-block ( / old-att old-cmd)
  (if (not (tblsearch "BLOCK" *ADE:NOM:BLK-NAME*))
    (progn
      (setq old-att (getvar "ATTREQ")
            old-cmd (getvar "CMDECHO"))
      (setvar "ATTREQ"  0)
      (setvar "CMDECHO" 0)
      (command "_.INSERT" (strcat *ADE:NOM:BLK-DWG* "=")
               "0,0,0" "1" "1" "0")
      (command "_.ERASE" "L" "")
      (setvar "ATTREQ"  old-att)
      (setvar "CMDECHO" old-cmd)
      (if (not (tblsearch "BLOCK" *ADE:NOM:BLK-NAME*))
        (progn
          (princ (strcat "\n  ERROR: No se pudo cargar el bloque desde:\n  "
                         *ADE:NOM:BLK-DWG*))
          (exit)
        )
      )
    )
  )
)

;; -- Insertar bloque en pt con angulo ang --------------------
(defun ade:nom:place (pt ang cables caneria)
  (setq *ADE:NOM:BLK-DWG* (ade:nom:get-block-dwg))
  (ade:insert-block
    *ADE:NOM:BLK-NAME*
    (list (car pt) (cadr pt) 0.0)
    (ade:nom:normalize-angle ang)
    (list (cons "CABLES"   cables)
          (cons "CAÑERIA"  caneria)
          (cons "CA ERIA"  caneria)))
)

;; =============================================================
;;  PROCESADORES POR TIPO DE ENTIDAD
;; =============================================================

;; -- Etiquetar un ARC ----------------------------------------
(defun ade:nom:label-arc (ent cables caneria / geom)
  (setq geom (ade:nom:geom-arc ent))
  (ade:nom:place (car geom) (cadr geom) cables caneria)
  t
)

;; -- Etiquetar todos los segmentos de una LWPOLYLINE ---------
;; Cada segmento entre vertices i y i+1 recibe su propia etiqueta.
;; Soporta segmentos rectos (bulge=0) y curvos (bulge 0).
(defun ade:nom:label-poly (ent cables caneria /
                            ed pairs i v1 v2 bulge geom n-seg)
  (setq ed     (entget ent)
        ;; Extraer lista de grupos 10 (vertices) y 42 (bulge)
        ;; Construimos lista de (vx vy bulge) por vertice
        pairs  (ade:nom:poly-pairs ed)
        n-seg  (1- (length pairs))
        i      0)
  (repeat n-seg
    (setq v1    (nth i     pairs)
          v2    (nth (1+ i) pairs)
          bulge (caddr v1))   ;; bulge entre v1 y v2
    (if (> (abs bulge) 1e-6)
      (setq geom (ade:nom:geom-bulge
                   (list (car v1) (cadr v1))
                   (list (car v2) (cadr v2))
                   bulge))
      (setq geom (ade:nom:geom-line
                   (list (car v1) (cadr v1))
                   (list (car v2) (cadr v2))))
    )
    (ade:nom:place (car geom) (cadr geom) cables caneria)
    (setq i (1+ i))
  )
  t
)

;; -- Construir lista (x y bulge) por vertice de LWPOLYLINE ---
;; Grupos: 10=(x y [z]), 42=bulge (default 0 si ausente)
(defun ade:nom:poly-pairs (ed / result x y bulge-next pair-raw vt)
  ;; Estrategia: recorrer entget y recolectar 10 y 42 en orden
  (setq result '()
        bulge-next 0.0)
  (foreach pair ed
    (cond
      ((= (car pair) 10)
       ;; Guardar pendiente: (x y bulge-del-anterior-o-0)
       (setq result (append result
                      (list (list (cadr pair) (caddr pair) bulge-next)))
             bulge-next 0.0))
      ((= (car pair) 42)
       ;; Bulge del ultimo vertice agregado -> actualizar su bulge
       (if result
         (progn
           (setq vt (last result))
           (setq result (append (reverse (cdr (reverse result)))
                                (list (list (car vt) (cadr vt) (cdr pair)))))
         )
       ))
    )
  )
  result
)

;; =============================================================
;;  MODOS DE OPERACION
;; =============================================================

;; -- Modo Auto: etiqueta todas las entidades de una capa -----
;; Procesa ARCs y LWPOLYLINEs
(defun ade:nom:auto (capa cables caneria /
                      ss n i ent etype count)
  (princ (strcat "\n  Buscando entidades en capa '" capa "'..."))
  (setq ss (ssget "X" (list (cons 0 "ARC,LWPOLYLINE")
                             (cons 8 capa))))
  (if (null ss)
    (progn
      (princ (strcat "\n  Sin entidades en capa '" capa "'."))
      (exit)
    )
  )
  (setq n     (sslength ss)
        i     0
        count 0)
  (princ (strcat "\n  " (itoa n) " entidades encontradas."))
  (repeat n
    (setq ent   (ssname ss i)
          etype (cdr (assoc 0 (entget ent))))
    (cond
      ((= etype "ARC")
       (ade:nom:label-arc  ent cables caneria)
       (setq count (1+ count)))
      ((= etype "LWPOLYLINE")
       (ade:nom:label-poly ent cables caneria)
       (setq count (1+ count)))
    )
    (setq i (1+ i))
  )
  (princ (strcat "\n  " (itoa count) " entidades etiquetadas."))
)

;; -- Modo Manual: usuario selecciona entidades ---------------
(defun ade:nom:manual (cables caneria /
                        ss n i ent etype count)
  (princ "\n  Seleccione arcos y/o polilineas a etiquetar.")
  (setq ss (ssget '((0 . "ARC,LWPOLYLINE"))))
  (if (null ss)
    (progn (princ "\n  Cancelado.") (exit))
  )
  (setq n     (sslength ss)
        i     0
        count 0)
  (repeat n
    (setq ent   (ssname ss i)
          etype (cdr (assoc 0 (entget ent))))
    (cond
      ((= etype "ARC")
       (ade:nom:label-arc  ent cables caneria)
       (setq count (1+ count)))
      ((= etype "LWPOLYLINE")
       (ade:nom:label-poly ent cables caneria)
       (setq count (1+ count)))
    )
    (setq i (1+ i))
  )
  (princ (strcat "\n  " (itoa count) " entidades etiquetadas."))
)

;; =============================================================
;;  COMANDO PRINCIPAL
;; =============================================================
(defun c:ADE_NOMENCLATURA ( / modo cx cables caneria capa
                              sub old-err old-cmd)

  (setq old-err *error*
        old-cmd (getvar "CMDECHO"))

  (defun *error* (msg)
    (setvar "CMDECHO" old-cmd)
    (ade:nom:endundo)
    (if (and msg (not (wcmatch (strcase msg t) "*break,*cancel*,*exit*")))
      (princ (strcat "\nError: " msg))
    )
    (setq *error* old-err)
    (princ)
  )

  (setvar "CMDECHO" 0)
  (vla-startundomark (ade:nom:acdoc))

  (princ "\n-- ADE_NOMENCLATURA v2.0 --------------------------")

  ;; -- 1. Modo ------------------------------------------------
  (initget "Luminarias Tomas Retornos Manual")
  (setq modo (getkword
    (strcat "\nModo [Luminarias/Tomas/Retornos/Manual] <" *ADE:NOM:MODO* ">: ")))
  (if (null modo) (setq modo *ADE:NOM:MODO*) (setq *ADE:NOM:MODO* modo))

  ;; -- 2. Circuito CX (no se pide en Manual ni Retornos) ------
  (if (and (not (= modo "Manual")) (not (= modo "Retornos")))
    (progn
      (setq cx (getstring
        (strcat "\nCircuito [" *ADE:NOM:CX* "]: ")))
      (if (= cx "") (setq cx *ADE:NOM:CX*)
                    (setq *ADE:NOM:CX* (strcase cx)))
      (setq cx (strcase cx))
    )
  )

  ;; -- 3. Cables (segun modo y aprendizaje de habitos) -------
  (if (= modo "Retornos")
    (progn
      (initget "1 2 3 Combinacion Manual")
      (setq opt-ef (getkword "\nEfectos de llave [1 (2x1,5+T) / 2 (3x1,5+T) / 3 (4x1,5+T) / Combinacion / Manual] <1>: "))
      (cond
        ((or (null opt-ef) (= opt-ef "1")) (setq cables "2x1,5+T"))
        ((= opt-ef "2")                     (setq cables "3x1,5+T"))
        ((= opt-ef "3")                     (setq cables "4x1,5+T"))
        ((= opt-ef "Combinacion")           (setq cables "3x1,5+T"))
        ((= opt-ef "Manual")
         (setq cables (getstring (strcat "\nNomenclatura personalizada [" *ADE:NOM:CABLES-R* "]: ")))
         (if (= cables "") (setq cables *ADE:NOM:CABLES-R*)))
      )
      (setq *ADE:NOM:CABLES-R* cables)
    )
    (progn
      (setq cables (if (= modo "Tomas") *ADE:NOM:CABLES-T* *ADE:NOM:CABLES-L*))
      (setq cables-in (getstring (strcat "\nCables [" cables "]: ")))
      (if (not (= cables-in "")) (setq cables cables-in))
      (if (= modo "Tomas") (setq *ADE:NOM:CABLES-T* cables) (setq *ADE:NOM:CABLES-L* cables))
    )
  )

  ;; -- 4. Caneria ---------------------------------------------
  (setq caneria (getstring
    (strcat "\nCaneria [" *ADE:NOM:CANERIA* "]: ")))
  (if (= caneria "") (setq caneria *ADE:NOM:CANERIA*)
                     (setq *ADE:NOM:CANERIA* caneria))

  ;; -- 5. Cargar bloque ---------------------------------------
  (ade:nom:ensure-block)

  ;; -- 6. Despachar por modo ----------------------------------
  (cond
    ((= modo "Luminarias")
     ;; Capa de arcos de luminarias (misma que usa ADE_CANERIAS)
     (setq capa (if *ADE:CAN:CAPA-LUZ* *ADE:CAN:CAPA-LUZ* "Canerias_Luz"))
     (princ (strcat "\n  Capa: '" capa "' | CX: " cx
                    " | Cables: " cables " | Caneria: " caneria))
     ;; Filtrar por CX no es directo (los arcos no tienen atrib),
     ;; asi que buscamos en la capa + el usuario eligio el CX para
     ;; nombrar la sesion. Si cada CX tiene su propia capa, usar:
     ;;   (setq capa (strcat "Canerias_Luz_" cx))
     ;; Por ahora etiqueta toda la capa (patron actual de ADE_CANERIAS).
     (ade:nom:auto capa cables caneria))

    ((= modo "Tomas")
     ;; Capa de canalizacion de tomas: Canalizacion_CX
     (setq capa (strcat "Canalizacion_" cx))
     (princ (strcat "\n  Capa: '" capa
                    "' | Cables: " cables " | Caneria: " caneria))
     (ade:nom:auto capa cables caneria))

    ((= modo "Retornos")
     (princ (strcat "\n  Seleccione cañeria hacia llave/interruptor (Cables: " cables " | Cañeria: " caneria ")"))
     (ade:nom:manual cables caneria))

    ((= modo "Manual")
     (princ (strcat "\n  Cables: " cables " | Caneria: " caneria))
     (ade:nom:manual cables caneria))
  )

  (ade:nom:endundo)
  (setvar "CMDECHO" old-cmd)
  (setq *error* old-err)
  (princ)
)

;; -- Shortcut -------------------------------------------------
(defun c:WN () (c:ADE_NOMENCLATURA))

(princ "\n[AD-ELEC] ade_nomenclatura.lsp v2.0 cargado.")
(princ "\n  Comandos: ADE_NOMENCLATURA | WN")
(princ)
