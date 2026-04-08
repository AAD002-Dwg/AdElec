;; ============================================================
;;  ade_canerias.lsp  —  ADE_CANERIAS
;;  Trazado de canalizaciones eléctricas.
;;
;;  Modos:
;;    L = Luminarias → arcos de 45° entre bloques I.E-AD-07
;;    T = Tomas      → polilíneas ortogonales entre I.E-AD-09
;;
;;  Cada modo tiene sub-modo Manual / Auto (por circuito CX).
;; ============================================================

(defun c:ADE_CANERIAS ( / modo)
  (vl-load-com)

  (initget "Luminarias Tomas")
  (setq modo (getkword "\nModo [Luminarias/Tomas] <Luminarias>: "))
  (if (null modo) (setq modo "Luminarias"))

  (cond
    ((= modo "Luminarias") (ade:canerias-luminarias))
    ((= modo "Tomas")      (ade:canerias-tomas))
  )
  (princ)
)

;; ============================================================
;;  CAÑERÍAS — LUMINARIAS (arcos de 45°)
;; ============================================================

(defun ade:canerias-luminarias ( / sub-modo capa cx pts)

  (initget "Manual Auto")
  (setq sub-modo (getkword "\nModo arcos [Manual/Auto] <Manual>: "))
  (if (null sub-modo) (setq sub-modo "Manual"))

  (setq capa (getstring "\nCapa para arcos [Canerias_Luz]: "))
  (if (= capa "") (setq capa "Canerias_Luz"))
  (ade:ensure-layer capa 4)

  (if (= sub-modo "Auto")
    (progn
      (setq cx (getstring "\nCircuito a cablear [C1]: "))
      (if (= cx "") (setq cx "C1"))
      (setq cx  (strcase cx)
            pts (ade:get-blocks-by-cx "I.E-AD-07" cx))

      (if (< (length pts) 2)
        (progn
          (princ (strcat "\nMenos de 2 luminarias con CX=" cx " en el dibujo."))
          (exit)
        )
      )
      ;; Ordenar por Y desc, luego X asc (fila a fila de arriba a abajo)
      (setq pts (vl-sort pts
                  '(lambda (a b)
                     (if (> (abs (- (cadr a) (cadr b))) 0.01)
                       (> (cadr a) (cadr b))
                       (< (car  a) (car  b))
                     )
                  )))
      (princ (strcat "\n✓ " (itoa (length pts))
                     " luminarias encontradas con CX=" cx "."))
    )
    (progn
      ;; Manual: selección uno a uno
      (setq pts '())
      (princ "\nSeleccioná las luminarias en orden. ENTER para terminar.")
      (while
        (progn
          (setq ent (car (entsel
                    (strcat "\nLuminaria #"
                            (itoa (1+ (length pts))) ": "))))
          ent)
        (setq pts (append pts
                   (list (cdr (assoc 10 (entget ent))))))
      )
      (if (< (length pts) 2)
        (progn (princ "\nSe necesitan al menos 2 bloques.") (exit))
      )
    )
  )

  ;; Dibujar arcos encadenados
  (setq i 0)
  (repeat (1- (length pts))
    (ade:draw-arc-45
      (nth i       pts)
      (nth (1+ i)  pts)
      capa)
    (setq i (1+ i))
  )
  (princ (strcat "\n✓ " (itoa (1- (length pts)))
                 " arcos trazados en capa '" capa "'."))
)

;; ── Dibujar arco de 45° entre dos puntos ────────────────────
;; Usa el comando nativo ARC de AutoCAD:
;;   ARC pt1 Segunda_dir pt2 con ángulo de arco ~45°
;; Equivalente al LISP original: (command "_.ARC" pt1 "F" pt2 "U" "45")
(defun ade:draw-arc-45 (p1 p2 layer / p1-3d p2-3d old-layer)
  (setq old-layer (getvar "CLAYER"))
  (setvar "CLAYER" layer)
  ;; El comando ARC con opción "U" (ángulo incluido) dibuja el arco
  ;; pasando por p1, con dirección tangente, hasta p2 con 45° de abertura
  (command "_.ARC"
           (list (car p1) (cadr p1) 0.0)
           "_F"
           (list (car p2) (cadr p2) 0.0)
           "_U"
           "45")
  (setvar "CLAYER" old-layer)
)

;; ============================================================
;;  CAÑERÍAS — TOMAS (polilíneas ortogonales)
;; ============================================================

(defun ade:canerias-tomas ( / sub-modo cx capa pts)

  (initget "Manual Auto")
  (setq sub-modo (getkword "\nModo trazado [Manual/Auto] <Manual>: "))
  (if (null sub-modo) (setq sub-modo "Manual"))

  (if (= sub-modo "Auto")
    (progn
      (setq cx (getstring "\nCircuito a cablear [C1]: "))
      (if (= cx "") (setq cx "C1"))
      (setq cx (strcase cx))
    )
    (setq cx "C1")
  )

  (setq capa (getstring
               (strcat "\nCapa para canalización [Canalizacion_" cx "]: ")))
  (if (= capa "") (setq capa (strcat "Canalizacion_" cx)))
  (ade:ensure-layer capa 1)   ; rojo

  (if (= sub-modo "Auto")
    (progn
      (setq pts (ade:get-blocks-by-cx "I.E-AD-09" cx))
      (if (< (length pts) 2)
        (progn
          (princ (strcat "\nMenos de 2 tomas con CX=" cx " en el dibujo."))
          (exit)
        )
      )
      ;; Ordenar por X, luego Y
      (setq pts (vl-sort pts
                  '(lambda (a b)
                     (if (< (abs (- (car a) (car b))) 0.01)
                       (< (cadr a) (cadr b))
                       (< (car  a) (car  b))
                     )
                  )))
      (princ (strcat "\n✓ " (itoa (length pts))
                     " tomas encontradas con CX=" cx "."))
    )
    (progn
      ;; Manual: selección con puntos intermedios opcionales
      (setq pts '())
      (princ "\nSeleccioná los bloques de toma en orden.")
      (princ "\nEntre bloques podés agregar puntos intermedios (ENTER para saltar).")

      (while
        (progn
          (setq ent (car (entsel
                    (strcat "\nToma #" (itoa (1+ (length pts))) " [ENTER=terminar]: "))))
          ent)
        (setq pts (append pts
                   (list (cdr (assoc 10 (entget ent))))))

        ;; Puntos intermedios
        (while
          (setq pt-mid (getpoint "  Punto intermedio [ENTER=saltar]: "))
          (setq pts (append pts (list pt-mid)))
        )
      )

      (if (< (length pts) 2)
        (progn (princ "\nSe necesitan al menos 2 puntos.") (exit))
      )
    )
  )

  ;; Dibujar tramos ortogonales encadenados
  (setq i 0)
  (repeat (1- (length pts))
    (ade:draw-ortho-route
      (nth i       pts)
      (nth (1+ i)  pts)
      capa)
    (setq i (1+ i))
  )
  (princ (strcat "\n✓ Canalización trazada en capa '" capa
                 "' (" (itoa (1- (length pts))) " tramos)."))
)

;; ── Polilínea ortogonal H→V con chamfer de 45° ──────────────
;; p1, p2: listas '(x y) o '(x y z)
(defun ade:draw-ortho-route (p1 p2 layer /
                              x1 y1 x2 y2 dx dy
                              kx ky chamfer sx sy
                              bx by ax ay
                              old-layer)
  (setq x1 (car  p1) y1 (cadr p1)
        x2 (car  p2) y2 (cadr p2)
        dx (- x2 x1)  dy (- y2 y1)
        old-layer (getvar "CLAYER"))

  (setvar "CLAYER" layer)

  (cond
    ;; Segmento recto (H o V)
    ((or (< (abs dx) 1e-4) (< (abs dy) 1e-4))
     (command "_.PLINE"
              (list x1 y1 0.0)
              (list x2 y2 0.0)
              "")
    )
    ;; Codo con chamfer: H hasta la esquina, 45° diagonal, V hasta p2
    (t
     ;; Tamaño del chamfer: 15cm fijo, pero no más que la menor dimensión / 3
     (setq chamfer (min 0.15 (/ (min (abs dx) (abs dy)) 3.0))
           sx (if (> dx 0) 1.0 -1.0)
           sy (if (> dy 0) 1.0 -1.0)
           ;; Esquina del codo (x2, y1)
           kx x2  ky y1
           ;; Punto antes del chamfer (sobre la horizontal)
           bx (- kx (* sx chamfer)) by ky
           ;; Punto después del chamfer (sobre la vertical)
           ax kx ay (+ ky (* sy chamfer)))
     (command "_.PLINE"
              (list x1 y1 0.0)
              (list bx by 0.0)
              (list ax ay 0.0)
              (list x2 y2 0.0)
              "")
    )
  )
  (setvar "CLAYER" old-layer)
)

(princ "\n[AD-ELEC] ade_canerias.lsp cargado. Comando: ADE_CANERIAS")
(princ)
