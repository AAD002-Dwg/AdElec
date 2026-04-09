;; ============================================================
;;  ade_luminarias.lsp  —  ADE_LUMINARIAS  v4.2
;;  Distribución interactiva masiva de luminarias (Joystick Mode).
;; ============================================================

(vl-load-com)

;; ── VARIABLES GLOBALES DE PERSISTENCIA ───────────────────────
(if (not *ADE:LUM:CX*)         (setq *ADE:LUM:CX* "C1"))
(if (not *ADE:LUM:TABLERO*)    (setq *ADE:LUM:TABLERO* "TD1"))
(if (not *ADE:LUM:SP-INICIAL*) (setq *ADE:LUM:SP-INICIAL* 2.5))
(if (not *ADE:LUM:PASO-SP*)    (setq *ADE:LUM:PASO-SP* 0.1))
(if (not *ADE:LUM:FIXED-MODE*) (setq *ADE:LUM:FIXED-MODE* nil)) ;; nil=Auto, T=Fijo

;; ─────────────────────────────────────────────────────────────
;;  FUNCIONES AUXILIARES 
;; ─────────────────────────────────────────────────────────────

(defun ade:lum:pt3 (p) (list (car p) (cadr p) (if (caddr p) (caddr p) 0.0)))

(defun ade:lum:get-block-segs (blkname / acad ms tmp-ref parts ent ed etype segs bx by r sa ea da n i a1 a2 p1 p2 v10list verts closed)
  (setq segs '() acad (vlax-get-acad-object) ms (vla-get-ModelSpace (vla-get-ActiveDocument acad)))
  (setq tmp-ref (vl-catch-all-apply 'vla-insertblock (list ms (vlax-3d-point '(0 0 0)) blkname 1 1 1 0)))
  (if (vl-catch-all-error-p tmp-ref)
    (progn (princ (strcat "\n[ADE] Error al procesar bloque '" blkname "'.")) nil)
    (progn
      (setq parts (vlax-safearray->list (vlax-variant-value (vla-explode tmp-ref))))
      (vla-delete tmp-ref)
      (foreach part parts
        (setq ent (vlax-vla-object->ename part) ed (entget ent) etype (cdr (assoc 0 ed)))
        (cond
          ((= etype "LINE") (setq segs (cons (list (ade:lum:pt3 (cdr (assoc 10 ed))) (ade:lum:pt3 (cdr (assoc 11 ed)))) segs)))
          ((= etype "CIRCLE")
           (setq bx (car (cdr (assoc 10 ed))) by (cadr (cdr (assoc 10 ed))) r (cdr (assoc 40 ed)) n 16 i 0)
           (while (< i n) (setq a1 (* (/ (* 2.0 pi) n) i) a2 (* (/ (* 2.0 pi) n) (1+ i)) p1 (list (+ bx (* r (cos a1))) (+ by (* r (sin a1))) 0.0) p2 (list (+ bx (* r (cos a2))) (+ by (* r (sin a2))) 0.0)) (setq segs (cons (list p1 p2) segs) i (1+ i))))
          ((= etype "ARC")
           (setq bx (car (cdr (assoc 10 ed))) by (cadr (cdr (assoc 10 ed))) r (cdr (assoc 40 ed)) sa (cdr (assoc 50 ed)) ea (cdr (assoc 51 ed)) n 16)
           (if (< ea sa) (setq ea (+ ea (* 2.0 pi))))
           (setq da (/ (- ea sa) n) i 0)
           (while (< i n) (setq a1 (+ sa (* i da)) a2 (+ sa (* (1+ i) da)) p1 (list (+ bx (* r (cos a1))) (+ by (* r (sin a1))) 0.0) p2 (list (+ bx (* r (cos a2))) (+ by (* r (sin a2))) 0.0)) (setq segs (cons (list p1 p2) segs) i (1+ i))))
          ((= etype "LWPOLYLINE")
           (setq v10list (vl-remove-if-not '(lambda (p) (= (car p) 10)) ed) verts (mapcar '(lambda (v) (list (cadr v) (caddr v) 0.0)) v10list) closed (= (logand (cdr (assoc 70 ed)) 1) 1) i 0)
           (while (< i (1- (length verts))) (setq segs (cons (list (nth i verts) (nth (1+ i) verts)) segs) i (1+ i)))
           (if (and closed (> (length verts) 1)) (setq segs (cons (list (last verts) (car verts)) segs))))
        )
        (vla-delete part)
      )
      segs
    )
  )
)

(defun ade:lum:draw-one (pt segs color / dx dy p1w p2w p1u p2u)
  (setq dx (car pt) dy (cadr pt))
  (foreach seg segs 
    (setq p1w (list (+ dx (caar seg)) (+ dy (cadar seg)) 0.0)
          p2w (list (+ dx (caadr seg)) (+ dy (cadadr seg)) 0.0)
          p1u (trans p1w 0 1)
          p2u (trans p2w 0 1))
    (grdraw p1u p2u color))
)

(defun ade:lum:draw-all (pts segs show-grid cols rows spX spY minX minY marX marY W H mode-fixed / i x y x0 y0 x1 y1 p1 p2 x0_area y0_area x1_area y1_area stepX stepY)
  (foreach pt pts (ade:lum:draw-one pt segs 4))
  (if (and show-grid (> (length pts) 0))
    (progn 
      (setq x0_area (+ minX marX) y0_area (+ minY marY) x1_area (- (+ minX W) marX) y1_area (- (+ minY H) marY))
      (if mode-fixed
        (setq stepX spX stepY spY x0 (+ minX (/ (- W (* (1- cols) stepX)) 2.0)) y0 (+ minY (/ (- H (* (1- rows) stepY)) 2.0)))
        (setq stepX (/ (- W (* 2.0 marX)) (max 1 cols)) stepY (/ (- H (* 2.0 marY)) (max 1 rows)) x0 (+ minX marX) y0 (+ minY marY)))
      
      (setq x1 (+ x0 (* (max 0 (1- cols)) stepX))
            y1 (+ y0 (* (max 0 (1- rows)) stepY))
            i 0)
      
      ;; Dibujar recuadro de área de margen (siempre, para ver dónde termina el cuarto)
      (setq p1 (trans (list x0_area y0_area 0) 0 1) p2 (trans (list x1_area y0_area 0) 0 1)
            p3 (trans (list x1_area y1_area 0) 0 1) p4 (trans (list x0_area y1_area 0) 0 1))
      (grdraw p1 p2 3) (grdraw p2 p3 3) (grdraw p3 p4 3) (grdraw p4 p1 3)

      (repeat (1+ cols) 
        (setq x (+ x0 (* i stepX))) 
        (setq p1 (trans (list x y0 0.0) 0 1) p2 (trans (list x y1 0.0) 0 1))
        (grdraw p1 p2 3) (setq i (1+ i)))
      (setq i 0)
      (repeat (1+ rows) 
        (setq y (+ y0 (* i stepY))) 
        (setq p1 (trans (list x0 y 0.0) 0 1) p2 (trans (list x1 y 0.0) 0 1))
        (grdraw p1 p2 3) (setq i (1+ i)))))
)

(defun ade:lum:clear () (redraw))

(defun ade:lum:grread ( / gr typ val)
  (setq typ 0)
  (while (/= typ 2) (setq gr (grread t 12 0) typ (car gr)))
  (setq val (cadr gr))
  (if (or (= val 0) (= val 224))
    (progn (setq typ 0) (while (/= typ 2) (setq gr (grread t 12 0) typ (car gr))) (list 2 (- (cadr gr))))
    (list 2 val)
  )
)

(defun ade:lum:calc-pts (cols rows spX spY minX minY poly-verts marX marY fixedMode W H / pts startX startY c r px py stepX stepY totalW totalH)
  (if (or (null spX) (<= spX 0.001)) (setq spX 0.1))
  (if (or (null spY) (<= spY 0.001)) (setq spY 0.1))
  (if fixedMode 
    (progn
      (setq cols (max 1 (1+ (fix (/ (- W (* 2.0 marX) 0.001) spX))))
            rows (max 1 (1+ (fix (/ (- H (* 2.0 marY) 0.001) spY)))))
      (setq stepX spX stepY spY)
      (setq totalW (* (1- cols) stepX) totalH (* (1- rows) stepY)
            startX (+ minX (/ (- W totalW) 2.0)) startY (+ minY (/ (- H totalH) 2.0))))
    (progn
      (setq stepX (/ (- W (* 2.0 marX)) (max 1 cols))
            stepY (/ (- H (* 2.0 marY)) (max 1 rows))
            startX (+ minX marX (/ stepX 2.0)) startY (+ minY marY (/ stepY 2.0)))))
  (setq pts '() c 0)
  (repeat cols
    (setq r 0)
    (repeat rows
      (setq px (+ startX (* c stepX)) py (+ startY (* r stepY)))
      (if (ade:point-in-polygon (list px py) poly-verts) (setq pts (cons (list px py 0.0) pts)))
      (setq r (1+ r))
    )
    (setq c (1+ c))
  )
  pts
)

(defun ade:lum:status (cols rows spX spY n show-grid marX marY fixedMode)
  (princ (strcat "\r M:" (if fixedMode "FIJO" "AUTO") 
                 " | G:" (itoa cols) "x" (itoa rows)
                 " | E:" (rtos spX 2 1) "x" (rtos spY 2 1)
                 " | M:" (rtos marX 2 1) "x" (rtos marY 2 1)
                 " | V:" (itoa n) 
                 "  [ W S A D ]:Grilla  [ 8 2 4 6 ]:Margen  [ M ]:Modo  [ G ]:Grid  [ U ]:Undo  [ Esc ]:Salir  "))
)

(defun ade:lum:numeric-input (cur-cols cur-rows first-digit / buf done result val comma-pos str-f str-c nf nc gr)
  (setq buf (chr first-digit) done nil result nil)
  (princ (strcat "\r  Input [filas,cols] → " buf "  "))
  (while (not done)
    (setq gr (ade:lum:grread) val (cadr gr))
    (cond
      ((and (>= val 48) (<= val 57)) (setq buf (strcat buf (chr val))) (princ (strcat "\r  Input [filas,cols] → " buf "  ")))
      ((= val 44) (if (not (vl-string-search "," buf)) (progn (setq buf (strcat buf ",")) (princ (strcat "\r  Input [filas,cols] → " buf "  ")))))
      ((= val 8) (if (> (strlen buf) 0) (setq buf (substr buf 1 (1- (strlen buf))))) (princ (strcat "\r  Input [filas,cols] → " buf "  ")))
      ((or (= val 13) (= val 32))
       (setq done t comma-pos (vl-string-search "," buf))
       (cond
         ((and comma-pos (> comma-pos 0)) (setq str-f (substr buf 1 comma-pos) str-c (substr buf (+ comma-pos 2)) nf (atoi str-f) nc (atoi str-c)) (if (and (> nf 0) (> nc 0)) (setq result (list nf nc))))
         ((> (strlen buf) 0) (setq nf (atoi buf)) (if (> nf 0) (setq result (list nf cur-cols))))))
      ((= val 27) (setq done t result nil) (princ "\r  [Input cancelado]                              "))
    )
  )
  result
)

(defun c:ADE_LUMINARIAS ( / cx tablero sp-inicial paso-sp ent poly-verts bbox minX minY maxX maxY W H cols rows spX spY blk-segs show-grid undo-stack state done confirmed gr key pts n num-result ss old-dyn marX marY mode-fixed cmd-done)
  (vl-load-com)
  (defun *ade_lum_error* (msg)
    (if old-dyn (setvar "DYNMODE" old-dyn)) (ade:lum:clear)
    (if (not (member msg '("Function cancelled" "quit / exit abort"))) (princ (strcat "\nError: " msg)))
    (setq *error* old_err) (princ)
  )
  (setq old_err *error*  *error* *ade_lum_error* old-dyn (getvar "DYNMODE"))
  
  (princ "\n── ADE_LUMINARIAS v4.2 ─────────────────────────────")
  (princ "\n  LEYENDA DE VARIABLES:")
  (princ "\n    M:Modo (Fijo/Auto)  G:Grilla (Col x Fil)  E:Espac. (m)  M:Margen (m)")
  (princ "\n  CONTROLES:")
  (princ "\n    W S A D  →  Grilla (Cant. o Metros)")
  (princ "\n    8 2 4 6  →  Margen (0.1m step)")
  (princ "\n    M → Cambiar Modo  G → Ver Grilla  U → Undo")
  (princ "\n────────────────────────────────────────────────────")

  (setq cx (getstring (strcat "\nCircuito [" *ADE:LUM:CX* "]: ")))
  (if (= cx "") (setq cx *ADE:LUM:CX*) (setq *ADE:LUM:CX* (strcase cx)))
  (setq tablero (getstring (strcat "\nTablero (D) [" *ADE:LUM:TABLERO* "]: ")))
  (if (= tablero "") (setq tablero *ADE:LUM:TABLERO*) (setq *ADE:LUM:TABLERO* (strcase tablero)))
  
  (if (not (ade:block-exists "I.E-AD-07")) (progn (princ "\n[ADE] Bloque I.E-AD-07 no encontrado.") (*error* "Bloque faltante")))
  (setq blk-segs (ade:lum:get-block-segs "I.E-AD-07") sp-inicial 2.5 mode-fixed nil cmd-done nil)

  (while (not cmd-done)
    (princ "\nSeleccioná el contorno (polilínea cerrada) [Esc para finalizar]: ")
    (setq ss (ssget "_:S" '((0 . "LWPOLYLINE"))))
    (if (null ss) (setq cmd-done t)
      (progn
        (setq ent (ssname ss 0) poly-verts (ade:poly-vertices ent) bbox (ade:poly-bbox ent)
              minX (nth 0 bbox) minY (nth 1 bbox) maxX (nth 2 bbox) maxY (nth 3 bbox)
              W (- maxX minX) H (- maxY minY))
        (if (or (< W 0.1) (< H 0.1)) (princ "\nRecinto demasiado pequeño.")
          (progn
            (setq cols (max 1 (fix (+ 0.5 (/ W sp-inicial)))) rows (max 1 (fix (+ 0.5 (/ H sp-inicial))))
                  spX (/ W cols) spY (/ H rows) marX 0.0 marY 0.0 show-grid nil undo-stack '() done nil confirmed nil)
            (while (not done)
              (setq pts (ade:lum:calc-pts cols rows spX spY minX minY poly-verts marX marY mode-fixed W H) n (length pts))
              (ade:lum:clear) (ade:lum:draw-all pts blk-segs show-grid cols rows spX spY minX minY marX marY W H mode-fixed)
              (ade:lum:status cols rows spX spY n show-grid marX marY mode-fixed)
              (setq gr (ade:lum:grread) key (cadr gr) state (list cols rows spX spY marX marY mode-fixed))
              (cond
                ((or (= key 13) (= key 32)) (setq done t confirmed t))
                ((= key 27) (setq done t confirmed nil cmd-done t))
                ((or (= key 87) (= key 119)) (setq undo-stack (cons state undo-stack)) (if (not mode-fixed) (setq rows (min 50 (1+ rows))) (setq spY (min H (+ spY 0.1)))))
                ((or (= key 83) (= key 115)) (setq undo-stack (cons state undo-stack)) (if (not mode-fixed) (setq rows (max 1 (1- rows))) (setq spY (max 0.1 (- spY 0.1)))))
                ((or (= key 68) (= key 100)) (setq undo-stack (cons state undo-stack)) (if (not mode-fixed) (setq cols (min 50 (1+ cols))) (setq spX (min W (+ spX 0.1)))))
                ((or (= key 65) (= key 97))  (setq undo-stack (cons state undo-stack)) (if (not mode-fixed) (setq cols (max 1 (1- cols))) (setq spX (max 0.1 (- spX 0.1)))))
                ((or (= key 56) (= key -72)) (setq undo-stack (cons state undo-stack) marY (min (- (/ H 2) 0.05) (+ marY 0.1))))
                ((or (= key 50) (= key -80)) (setq undo-stack (cons state undo-stack) marY (max 0.0 (- marY 0.1))))
                ((or (= key 54) (= key -77)) (setq undo-stack (cons state undo-stack) marX (min (- (/ W 2) 0.05) (+ marX 0.1))))
                ((or (= key 52) (= key -75)) (setq undo-stack (cons state undo-stack) marX (max 0.0 (- marX 0.1))))
                ((or (= key 77) (= key 109)) (setq mode-fixed (not mode-fixed)) (if (not mode-fixed) (setq spX (/ (- W (* 2 marX)) cols) spY (/ (- H (* 2 marY)) rows))))
                ((or (= key 71) (= key 103)) (setq show-grid (not show-grid)))
                ((or (= key 85) (= key 117)) (if undo-stack (setq state (car undo-stack) undo-stack (cdr undo-stack) cols (nth 0 state) rows (nth 1 state) spX (nth 2 state) spY (nth 3 state) marX (nth 4 state) marY (nth 5 state) mode-fixed (nth 6 state))))
                ((and (>= key 48) (<= key 57)) (setq num-result (ade:lum:numeric-input cols rows key)) (if num-result (setq rows (car num-result) cols (cadr num-result))))
              )
              (if (not mode-fixed) (setq spX (/ (- W (* 2.0 marX)) (max 1 cols)) spY (/ (- H (* 2.0 marY)) (max 1 rows))))
            )
            (ade:lum:clear)
            (if confirmed (foreach pt (ade:lum:calc-pts cols rows spX spY minX minY poly-verts marX marY mode-fixed W H) (ade:insert-block "I.E-AD-07" pt 0.0 (list (cons "CX" cx) (cons "D" tablero)))))
          ))))
  )
  (setvar "DYNMODE" old-dyn) (setq *error* old_err) (princ)
)

(defun c:ADE_LUMINARIAS_DIAGKEYS ( / gr)
  (princ "\n── DIAGNÓSTICO RAW (Esc para salir) ──\n")
  (while (not (and (= (car (setq gr (grread t 12 0))) 2) (= (cadr gr) 27)))
    (if (not (= (car gr) 5)) (princ (strcat "\nRecibido: " (vl-prin1-to-string gr)))))
  (princ)
)

(princ "\n[AD-ELEC] ade_luminarias.lsp v4.2 cargado.\n")
(princ)
