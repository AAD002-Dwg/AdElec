;; ============================================================
;;  ade_tomas.lsp  —  ADE_TOMAS  v2.3
;;  Insercion interactiva de tomacorrientes (Joystick Mode).
;;
;;  v2.3:
;;    - Snap con GrSnapV1-0.lsp (Lee Mac) — endpoint, midpoint,
;;      nearest, intersection, perpendicular, center.
;;      Siempre incluye nearest(512) en el bitmask de snap.
;;    - Rotacion: perpendicular hacia afuera (centroide) +
;;      atributos (CX, D) fijados a 0° para que el texto
;;      siempre sea legible sin importar la orientacion del bloque.
;;    - Teclas A/D para rotar ±90°, +/- offset, </> rotar 45°,
;;      O/R valores exactos, S toggle snap.
;; ============================================================

(vl-load-com)

;; ── Cargar GrSnapV1-0.lsp si no esta cargado ────────────────
(if (null LM:grsnap:snapfunction)
  (progn
    (vl-catch-all-apply 'load
      (list
        (cond
          ((findfile "GrSnapV1-0.lsp"))
          ((findfile "LISP BASE/GrSnapV1-0.lsp"))
          ((findfile "../LISP BASE/GrSnapV1-0.lsp"))
          ((findfile "G:/AD-ELEC/LISP BASE/GrSnapV1-0.lsp"))
          (t "")
        )
      )
    )
  )
)

;; ── VARIABLES GLOBALES DE PERSISTENCIA ───────────────────────
(if (not *ADE:TOMAS:CX*)         (setq *ADE:TOMAS:CX* "C1"))
(if (not *ADE:TOMAS:TABLERO*)    (setq *ADE:TOMAS:TABLERO* "TD1"))
(if (not *ADE:TOMAS:MODO*)       (setq *ADE:TOMAS:MODO* "Manual"))
(if (not *ADE:TOMAS:OFFSET*)     (setq *ADE:TOMAS:OFFSET* 0.0))
(if (not *ADE:TOMAS:ROT*)        (setq *ADE:TOMAS:ROT* 0.0))

;; Bitmask de snap: endpoint(1) + midpoint(2) + center(4) +
;; intersection(32) + perpendicular(128) + nearest(512) = 679
(setq *ADE:TOMAS:SNAPMODES* 679)

;; ── Utilidades ───────────────────────────────────────────────
(defun ade:tomas:endundo (doc)
  (while (= 8 (logand 8 (getvar "UNDOCTL")))
    (vla-endundomark doc)
  )
)

;; Centroide de una polilínea
(defun ade:tomas:centroid (ent / verts n sx sy)
  (setq verts (ade:poly-vertices ent)
        n     (length verts)
        sx    0.0
        sy    0.0)
  (foreach v verts
    (setq sx (+ sx (car v))
          sy (+ sy (cadr v)))
  )
  (list (/ sx n) (/ sy n))
)

;; Fijar rotacion de atributos a 0° para que el texto sea
;; siempre legible sin importar el angulo del bloque.
(defun ade:tomas:fix-attr-rotation (br / atts)
  (setq atts (vlax-invoke br 'GetAttributes))
  (foreach att atts
    (vl-catch-all-apply 'vla-put-Rotation (list att 0.0))
  )
)

;; ═════════════════════════════════════════════════════════════
;;  COMANDO PRINCIPAL
;; ═════════════════════════════════════════════════════════════
(defun c:ADE_TOMAS ( / cx tablero modo blk-segs old-err old-dyn acad-doc)
  (vl-load-com)
  (setq acad-doc (vla-get-ActiveDocument (vlax-get-acad-object)))

  (defun *ade_tomas_error* (msg)
    (if old-dyn (setvar "DYNMODE" old-dyn))
    (ade:tomas:endundo acad-doc)
    (redraw)
    (if (not (member msg '("Function cancelled" "quit / exit abort")))
      (princ (strcat "\nError: " msg))
    )
    (setq *error* old-err) (princ)
  )

  (setq old-err *error*
        *error* *ade_tomas_error*
        old-dyn (getvar "DYNMODE"))

  (vla-startundomark acad-doc)

  (princ "\n── ADE_TOMAS v2.3 ──────────────────────────────────")

  ;; ── Inputs comunes ────────────────────────────────────────
  (initget "Perimetro Manual Desde")
  (setq modo (getkword (strcat "\nModo [Perimetro/Manual/Desde] <" *ADE:TOMAS:MODO* ">: ")))
  (if (null modo) (setq modo *ADE:TOMAS:MODO*) (setq *ADE:TOMAS:MODO* modo))

  (setq cx (getstring (strcat "\nCircuito [" *ADE:TOMAS:CX* "]: ")))
  (if (= cx "") (setq cx *ADE:TOMAS:CX*) (setq *ADE:TOMAS:CX* (strcase cx)))

  (setq tablero (getstring (strcat "\nTablero (D) [" *ADE:TOMAS:TABLERO* "]: ")))
  (if (= tablero "") (setq tablero *ADE:TOMAS:TABLERO*) (setq *ADE:TOMAS:TABLERO* (strcase tablero)))

  (if (not (ade:block-exists "I.E-AD-09"))
    (progn
      (princ "\n[AD-ELEC] Bloque I.E-AD-09 no encontrado en el DWG.")
      (*ade_tomas_error* "Bloque faltante")
    )
  )

  (setq blk-segs (ade:tomas:get-block-segs "I.E-AD-09"))

  (setvar "DYNMODE" 0)

  (cond
    ((= modo "Perimetro") (ade:tomas-perimetro cx tablero blk-segs))
    ((= modo "Manual")    (ade:tomas-manual    cx tablero blk-segs))
    ((= modo "Desde")     (ade:tomas-desde     cx tablero blk-segs))
  )

  (ade:tomas:endundo acad-doc)
  (setvar "DYNMODE" old-dyn)
  (setq *error* old-err)
  (princ)
)

;; ═════════════════════════════════════════════════════════════
;;  GEOMETRIA PARA EL JIG (preview con grdraw)
;; ═════════════════════════════════════════════════════════════

(defun ade:tomas:pt3 (p)
  (list (car p) (cadr p) (if (caddr p) (caddr p) 0.0))
)

(defun ade:tomas:get-block-segs (blkname / acad ms tmp-ref parts ent ed etype segs
                                  bx by r sa ea da n i a1 a2 p1 p2
                                  v10list verts closed)
  (setq segs '()
        acad (vlax-get-acad-object)
        ms   (vla-get-ModelSpace (vla-get-ActiveDocument acad)))
  (setq tmp-ref (vl-catch-all-apply 'vla-insertblock
                  (list ms (vlax-3d-point '(0 0 0)) blkname 1 1 1 0)))
  (if (vl-catch-all-error-p tmp-ref)
    nil
    (progn
      (setq parts (vlax-safearray->list (vlax-variant-value (vla-explode tmp-ref))))
      (vla-delete tmp-ref)
      (foreach part parts
        (setq ent   (vlax-vla-object->ename part)
              ed    (entget ent)
              etype (cdr (assoc 0 ed)))
        (cond
          ((= etype "LINE")
           (setq segs (cons (list (ade:tomas:pt3 (cdr (assoc 10 ed)))
                                  (ade:tomas:pt3 (cdr (assoc 11 ed)))) segs)))
          ((= etype "CIRCLE")
           (setq bx (car (cdr (assoc 10 ed)))
                 by (cadr (cdr (assoc 10 ed)))
                 r  (cdr (assoc 40 ed))
                 n 16  i 0)
           (while (< i n)
             (setq a1 (* (/ (* 2.0 pi) n) i)
                   a2 (* (/ (* 2.0 pi) n) (1+ i))
                   p1 (list (+ bx (* r (cos a1))) (+ by (* r (sin a1))) 0.0)
                   p2 (list (+ bx (* r (cos a2))) (+ by (* r (sin a2))) 0.0))
             (setq segs (cons (list p1 p2) segs)
                   i    (1+ i))))
          ((= etype "ARC")
           (setq bx (car (cdr (assoc 10 ed)))
                 by (cadr (cdr (assoc 10 ed)))
                 r  (cdr (assoc 40 ed))
                 sa (cdr (assoc 50 ed))
                 ea (cdr (assoc 51 ed))
                 n  16)
           (if (< ea sa) (setq ea (+ ea (* 2.0 pi))))
           (setq da (/ (- ea sa) n) i 0)
           (while (< i n)
             (setq a1 (+ sa (* i da))
                   a2 (+ sa (* (1+ i) da))
                   p1 (list (+ bx (* r (cos a1))) (+ by (* r (sin a1))) 0.0)
                   p2 (list (+ bx (* r (cos a2))) (+ by (* r (sin a2))) 0.0))
             (setq segs (cons (list p1 p2) segs)
                   i    (1+ i))))
          ((= etype "LWPOLYLINE")
           (setq v10list (vl-remove-if-not '(lambda (p) (= (car p) 10)) ed)
                 verts   (mapcar '(lambda (v) (list (cadr v) (caddr v) 0.0)) v10list)
                 closed  (= (logand (cdr (assoc 70 ed)) 1) 1)
                 i 0)
           (while (< i (1- (length verts)))
             (setq segs (cons (list (nth i verts) (nth (1+ i) verts)) segs)
                   i    (1+ i)))
           (if (and closed (> (length verts) 1))
             (setq segs (cons (list (last verts) (car verts)) segs))))
        )
        (vla-delete part)
      )
      segs
    )
  )
)

(defun ade:tomas:draw-preview (pt ang segs color / dx dy cosA sinA
                                p1w p2w p1u p2u x1 y1 x2 y2)
  (setq dx   (car pt)
        dy   (cadr pt)
        cosA (cos ang)
        sinA (sin ang))
  (foreach seg segs
    (setq x1  (caar seg)   y1  (cadar seg)
          x2  (caadr seg)  y2  (cadadr seg)
          p1w (list (+ dx (- (* x1 cosA) (* y1 sinA)))
                    (+ dy (+ (* x1 sinA) (* y1 cosA))) 0.0)
          p2w (list (+ dx (- (* x2 cosA) (* y2 sinA)))
                    (+ dy (+ (* x2 sinA) (* y2 cosA))) 0.0)
          p1u (trans p1w 0 1)
          p2u (trans p2w 0 1))
    (grdraw p1u p2u color)
  )
)

;; ═════════════════════════════════════════════════════════════
;;  CALCULO DE ANGULO PERPENDICULAR
;; ═════════════════════════════════════════════════════════════

;; Angulo perpendicular al segmento mas cercano, apuntando
;; SIEMPRE HACIA AFUERA usando el centroide como referencia.
(defun ade:ang-perpendicular-to-wall (ent pt /
                                       verts n i centro
                                       p1 p2 best-dist best-ang
                                       dx dy seg-len t-val
                                       cx cy dist-sq seg-ang
                                       perp1 perp2
                                       tp1 tp2 d1 d2)
  (setq verts  (ade:poly-vertices ent)
        n      (length verts)
        i      0
        best-dist 1e38
        best-ang  0.0
        centro (ade:tomas:centroid ent))

  (repeat n
    (setq p1 (nth i verts)
          p2 (nth (rem (1+ i) n) verts)
          dx (- (car p2) (car p1))
          dy (- (cadr p2) (cadr p1))
          seg-len (sqrt (+ (* dx dx) (* dy dy))))

    (if (> seg-len 1e-6)
      (progn
        (setq t-val (/ (+ (* (- (car pt) (car p1)) dx)
                          (* (- (cadr pt) (cadr p1)) dy))
                       (* seg-len seg-len)))
        (setq t-val (max 0.0 (min 1.0 t-val)))
        (setq cx (+ (car p1) (* t-val dx))
              cy (+ (cadr p1) (* t-val dy)))
        (setq dist-sq (+ (* (- (car pt) cx) (- (car pt) cx))
                         (* (- (cadr pt) cy) (- (cadr pt) cy))))

        (if (< dist-sq best-dist)
          (progn
            (setq best-dist dist-sq
                  seg-ang   (atan dy dx)
                  perp1     (+ seg-ang (/ pi 2.0))
                  perp2     (- seg-ang (/ pi 2.0)))
            ;; Evaluar cual apunta LEJOS del centro
            (setq tp1 (list (+ cx (cos perp1)) (+ cy (sin perp1)))
                  tp2 (list (+ cx (cos perp2)) (+ cy (sin perp2)))
                  d1  (distance tp1 centro)
                  d2  (distance tp2 centro))
            (if (> d1 d2)
              (setq best-ang perp1)
              (setq best-ang perp2)
            )
          )
        )
      )
    )
    (setq i (1+ i))
  )
  best-ang
)

;; ── Buscar polilínea mas cercana ─────────────────────────────
(defun ade:tomas:find-nearest-wall (pt / ss i ent-dist-list ent d p-near)
  (setq ss (ssget "_C"
             (list (- (car pt) 2.0) (- (cadr pt) 2.0))
             (list (+ (car pt) 2.0) (+ (cadr pt) 2.0))
             '((0 . "LWPOLYLINE"))))
  (if ss
    (progn
      (setq i 0 ent-dist-list '())
      (repeat (sslength ss)
        (setq ent    (ssname ss i)
              p-near (vlax-curve-getClosestPointTo ent pt)
              d      (distance pt p-near)
              ent-dist-list (cons (list d ent p-near) ent-dist-list)
              i      (1+ i))
      )
      (setq ent-dist-list (vl-sort ent-dist-list
              '(lambda (a b) (< (car a) (car b)))))
      (cadar ent-dist-list)
    )
    nil
  )
)

;; ── Insertar toma sobre pared ────────────────────────────────
;; Retorna (br . ang) para poder ajustar despues.
(defun ade:insert-toma-on-wall (ent pt cx tablero /
                                  ang rot1-deg br pt-off)
  (setq ang      (ade:ang-perpendicular-to-wall ent pt)
        rot1-deg (strcat (rtos (* (- ang) (/ 180.0 pi)) 2 2) "°"))
  (setq pt-off (polar pt ang *ADE:TOMAS:OFFSET*))
  (setq br (ade:insert-block "I.E-AD-09"
              (list (car pt-off) (cadr pt-off) 0.0)
              ang
              (list (cons "CX" cx)
                    (cons "D"  tablero)
                    (cons "ROT1" rot1-deg))))
  (ade:tomas:fix-attr-rotation br)
  (cons br ang)
)

;; ── Rotar bloque ya insertado sumando delta al angulo actual ─
(defun ade:tomas:rotate-block (br delta / cur-rot new-rot)
  (setq cur-rot (vla-get-Rotation br)
        new-rot (+ cur-rot delta))
  (vla-put-Rotation br new-rot)
  (ade:tomas:fix-attr-rotation br)
  new-rot
)

;; ── Sub-loop post-insercion: A/D para rotar ±90° ────────────
;; Retorna T si el usuario quiere seguir, NIL si quiere salir.
(defun ade:tomas:post-adjust (br / gr2 type2 val2 adjusting)
  (princ "\n    [A]:-90° [D]:+90° | Clic/Enter: confirmar")
  (setq adjusting t)
  (while adjusting
    (setq gr2   (grread t 15 0)
          type2 (car  gr2)
          val2  (cadr gr2))
    (cond
      ;; Tecla
      ((= type2 2)
       (cond
         ;; A / a  rotar -90°
         ((member val2 '(65 97))
          (ade:tomas:rotate-block br (- (/ pi 2.0)))
          (princ "\r    -90°  ")
         )
         ;; D / d  rotar +90°
         ((member val2 '(68 100))
          (ade:tomas:rotate-block br (/ pi 2.0))
          (princ "\r    +90°  ")
         )
         ;; Enter/Espacio: confirmar y seguir
         ((member val2 '(13 32))
          (setq adjusting nil)
         )
         ;; Esc: confirmar y terminar comando
         ((= val2 27)
          (setq adjusting nil)
         )
       )
      )
      ;; Clic: confirmar y seguir
      ((member type2 '(3 5))
       (if (= type2 3) (setq adjusting nil))
      )
    )
  )
)

;; ═════════════════════════════════════════════════════════════
;;  MODO PERIMETRO
;; ═════════════════════════════════════════════════════════════
(defun ade:tomas-perimetro (cx tablero blk-segs / ent vla-ent dist-str dist
                             total-len n-tomas i dist-actual
                             pt tang ang rot1-deg count br
                             centro pt-list tang-ang
                             perp-ang test-pt d-centro)
  (setq ent (car (entsel "\nSelecciona la polilínea de pared: ")))
  (if (null ent) (exit))

  (setq dist-str (getstring "\nDistancia entre tomas [3.0]: "))
  (if (or (= dist-str "") (null dist-str))
    (setq dist 3.0)
    (setq dist (atof dist-str))
  )
  (if (<= dist 0) (setq dist 3.0))

  (setq vla-ent  (vlax-ename->vla-object ent)
        total-len (vla-get-Length vla-ent)
        n-tomas   (fix (/ total-len dist))
        centro    (ade:tomas:centroid ent)
        i         0
        count     0)

  (repeat (1+ n-tomas)
    (setq dist-actual (* i dist))
    (if (> dist-actual total-len)
      (setq dist-actual total-len)
    )
    ;; Evitar vertices exactos (tangente ambigua)
    (if (and (> dist-actual 0.001) (< dist-actual (- total-len 0.001)))
      (setq dist-actual dist-actual)
    )
    ;; Punto sobre la curva
    (setq pt   (vlax-invoke vla-ent 'GetPointAtDist dist-actual)
          tang (vlax-invoke vla-ent 'GetFirstDerivative
                 (vlax-invoke vla-ent 'GetParameterAtDistance dist-actual)))

    (setq tang-ang (atan (vlax-safearray-get-element tang 1)
                         (vlax-safearray-get-element tang 0)))

    (setq pt-list (list (vlax-safearray-get-element pt 0)
                        (vlax-safearray-get-element pt 1)))

    ;; Perpendicular que apunte AFUERA del centro
    (setq perp-ang (+ tang-ang (/ pi 2.0))
          test-pt  (list (+ (car pt-list) (cos perp-ang))
                         (+ (cadr pt-list) (sin perp-ang)))
          d-centro (distance test-pt centro))

    (if (< d-centro (distance pt-list centro))
      (setq ang (- tang-ang (/ pi 2.0)))
      (setq ang perp-ang)
    )

    (setq rot1-deg (strcat (rtos (* (- ang) (/ 180.0 pi)) 2 2) "°"))

    (setq br (ade:insert-block "I.E-AD-09"
               (list (car pt-list) (cadr pt-list) 0.0)
               ang
               (list (cons "CX" cx) (cons "D" tablero) (cons "ROT1" rot1-deg))))
    ;; Fijar atributos legibles
    (ade:tomas:fix-attr-rotation br)

    (setq count (1+ count)
          i     (1+ i))
  )
  (princ (strcat "\n  " (itoa count)
                 " tomas en perimetro — CX=" cx " D=" tablero))
)

;; ═════════════════════════════════════════════════════════════
;;  MODO MANUAL (Joystick con GrSnap)
;; ═════════════════════════════════════════════════════════════
;; Controles:
;;   Clic izq     Insertar toma → luego A/D para ajustar
;;   +  / =       Offset +0.05
;;   -  / _       Offset -0.05
;;   O  / o       Offset exacto
;;   S  / s       Toggle snap on/off
;;   Esc/Enter    Terminar
;;
;; Post-insercion (despues de cada clic):
;;   A / a        Rotar bloque -90°
;;   D / d        Rotar bloque +90°
;;   Clic/Enter   Confirmar y seguir
(defun ade:tomas-manual (cx tablero blk-segs / count gr type val pt
                          ent-wall ang done msg-ctrl tmp br result
                          snap-active snap-fn ori-txt snap-mode)
  (setq count 0
        done  nil
        snap-active t)

  ;; Crear funcion de snap si GrSnapV1-0 esta cargado
  (setq snap-fn
    (if LM:grsnap:snapfunction
      (LM:grsnap:snapfunction)
      nil
    )
  )

  ;; Snap mode: OSMODE del usuario OR nuestros modos (siempre incluir nearest)
  (setq snap-mode (logior (getvar "OSMODE") *ADE:TOMAS:SNAPMODES*))

  (if snap-fn
    (princ "\n[GrSnap activo — end/mid/near/int/per/cen]")
    (princ "\n[GrSnap no disponible]")
  )

  (setq msg-ctrl
    "\n  Clic: insertar | [+/-]:Offset [O]:Ofs exacto [S]:Snap [Esc]:Fin"
  )
  (princ "\n[Joystick] Cursor cerca de pared = orientacion auto. Post-clic: A/D ajustan ±90°")
  (princ msg-ctrl)

  (while (not done)
    (setq gr   (grread t 15 0)
          type (car  gr)
          val  (cadr gr))

    (cond

      ;; ── Movimiento del cursor (Type 5) ────────────────────
      ((= type 5)
       (redraw)
       ;; Aplicar snap
       (setq pt (if (and snap-active snap-fn)
                  (snap-fn val snap-mode)
                  val))
       (if (not (and (listp pt) (numberp (car pt))))
         (setq pt val)
       )
       (setq ent-wall (ade:tomas:find-nearest-wall pt))
       (if ent-wall
         (setq ang (ade:ang-perpendicular-to-wall ent-wall pt))
         (setq ang *ADE:TOMAS:ROT*)
       )
       (ade:tomas:draw-preview (polar pt ang *ADE:TOMAS:OFFSET*) ang blk-segs 2)
       (setq ori-txt (if ent-wall "PARED" "LIBRE"))
       (princ (strcat "\r  #" (itoa (1+ count))
              " Ofs=" (rtos *ADE:TOMAS:OFFSET* 2 2)
              " " ori-txt
              (if snap-active " SNAP" "")
              "    "))
      )

      ;; ── Click izquierdo (Type 3) ──────────────────────────
      ((= type 3)
       (setq pt (if (and snap-active snap-fn)
                  (snap-fn val snap-mode)
                  val))
       (if (not (and (listp pt) (numberp (car pt))))
         (setq pt val)
       )
       (setq ent-wall (ade:tomas:find-nearest-wall pt))
       (if ent-wall
         (progn
           (setq result (ade:insert-toma-on-wall ent-wall pt cx tablero))
           (setq br (car result))
         )
         (progn
           (setq ang *ADE:TOMAS:ROT*)
           (setq br (ade:insert-block "I.E-AD-09"
                      (polar pt ang *ADE:TOMAS:OFFSET*)
                      ang
                      (list (cons "CX" cx) (cons "D" tablero)
                            (cons "ROT1" (strcat (rtos (* (- ang) (/ 180.0 pi)) 2 2) "°")))))
           (ade:tomas:fix-attr-rotation br)
         )
       )
       (setq count (1+ count))
       (princ (strcat "\n  Toma #" (itoa count) " insertada."))
       ;; ── Sub-loop: A/D para ajustar rotacion ──────────────
       (ade:tomas:post-adjust br)
       (princ msg-ctrl)
      )

      ;; ── Teclas (Type 2) ───────────────────────────────────
      ((= type 2)
       (cond
         ;; + / =  offset +0.05
         ((member val '(43 61))
          (setq *ADE:TOMAS:OFFSET* (+ *ADE:TOMAS:OFFSET* 0.05))
          (princ (strcat "\r  Offset = " (rtos *ADE:TOMAS:OFFSET* 2 2) "    "))
         )
         ;; - / _  offset -0.05
         ((member val '(45 95))
          (setq *ADE:TOMAS:OFFSET* (max 0.0 (- *ADE:TOMAS:OFFSET* 0.05)))
          (princ (strcat "\r  Offset = " (rtos *ADE:TOMAS:OFFSET* 2 2) "    "))
         )
         ;; O / o  offset exacto
         ((member val '(79 111))
          (if (setq tmp (getdist (strcat "\nOffset exacto <"
                          (rtos *ADE:TOMAS:OFFSET* 2 2) ">: ")))
            (setq *ADE:TOMAS:OFFSET* tmp)
          )
          (princ msg-ctrl)
         )
         ;; S / s  toggle snap
         ((member val '(83 115))
          (setq snap-active (not snap-active))
          (princ (strcat "\n  Snap: " (if snap-active "ON" "OFF")))
          (princ msg-ctrl)
         )
         ;; Esc / Enter / Espacio
         ((member val '(27 13 32)) (setq done t))
       )
      )
    )
  )
  (redraw)
  (princ (strcat "\n  " (itoa count) " tomas colocadas — CX=" cx " D=" tablero))
)

;; ═════════════════════════════════════════════════════════════
;;  MODO DESDE (referencia + distancia sobre curva)
;; ═════════════════════════════════════════════════════════════
(defun ade:tomas-desde (cx tablero blk-segs / ent vla-ent total-len
                         pt-ref pt-ref-dist dist-str dist
                         dist-final pt tang ang rot1-deg count seguir br
                         centro tang-ang perp-ang test-pt d-centro pt-list)
  (setq ent (car (entsel "\nSelecciona la pared (linea o polilinea): ")))
  (if (null ent) (exit))

  (setq vla-ent   (vlax-ename->vla-object ent)
        total-len (vla-get-Length vla-ent)
        centro    (ade:tomas:centroid ent)
        count     0
        seguir    t)

  (princ "\nEsc o Enter sin punto para terminar.")

  (while seguir
    (setq pt-ref (getpoint "\nPunto de referencia en la pared: "))
    (if (null pt-ref)
      (setq seguir nil)
      (progn
        (setq pt-ref-dist
          (vlax-invoke vla-ent 'GetDistanceAtParameter
            (vlax-invoke vla-ent 'GetParameterAtPoint
              (vlax-invoke vla-ent 'GetClosestPointTo
                (vlax-3d-point pt-ref) :vlax-false))))

        (setq dist-str (getstring "\nDistancia desde referencia [1.0]: "))
        (if (or (null dist-str) (= dist-str ""))
          (setq dist 1.0)
          (setq dist (atof dist-str))
        )

        (setq dist-final (max 0.0 (min total-len (+ pt-ref-dist dist))))

        (setq pt   (vlax-invoke vla-ent 'GetPointAtDist dist-final)
              tang (vlax-invoke vla-ent 'GetFirstDerivative
                     (vlax-invoke vla-ent 'GetParameterAtDistance dist-final)))

        (setq tang-ang (atan (vlax-safearray-get-element tang 1)
                             (vlax-safearray-get-element tang 0)))

        (setq pt-list (list (vlax-safearray-get-element pt 0)
                            (vlax-safearray-get-element pt 1)))

        ;; Perpendicular que apunte AFUERA del centro
        (setq perp-ang (+ tang-ang (/ pi 2.0))
              test-pt  (list (+ (car pt-list) (cos perp-ang))
                             (+ (cadr pt-list) (sin perp-ang)))
              d-centro (distance test-pt centro))

        (if (< d-centro (distance pt-list centro))
          (setq ang (- tang-ang (/ pi 2.0)))
          (setq ang perp-ang)
        )

        (setq rot1-deg (strcat (rtos (* (- ang) (/ 180.0 pi)) 2 2) "°"))

        (setq br (ade:insert-block "I.E-AD-09"
                   (list (car pt-list) (cadr pt-list) 0.0)
                   ang
                   (list (cons "CX" cx) (cons "D" tablero) (cons "ROT1" rot1-deg))))
        (ade:tomas:fix-attr-rotation br)

        (setq count (1+ count))
        (princ (strcat "\n  Toma #" (itoa count) " insertada."))
      )
    )
  )
  (princ (strcat "\n  " (itoa count)
                 " tomas desde referencia — CX=" cx " D=" tablero))
)

(princ "\n[AD-ELEC] ade_tomas.lsp v2.3 cargado. Comando: ADE_TOMAS")
(princ)
