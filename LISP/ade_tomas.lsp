;; ============================================================
;;  ade_tomas.lsp  —  ADE_TOMAS
;;  Inserción de tomacorrientes (I.E-AD-09) en 3 modos.
;;
;;  Modos:
;;    P = Perímetro  → distribución a lo largo de una polilínea
;;    M = Manual     → clicks consecutivos con orientación libre
;;    D = Desde      → posición exacta desde punto de referencia
;; ============================================================

(defun c:ADE_TOMAS ( / cx tablero modo)

  (vl-load-com)

  ;; ── Inputs comunes ────────────────────────────────────────
  (initget "Perimetro Manual Desde")
  (setq modo (getkword "\nModo [Perimetro/Manual/Desde] <Perimetro>: "))
  (if (null modo) (setq modo "Perimetro"))

  (setq cx (getstring "\nCircuito [C1]: "))
  (if (= cx "") (setq cx "C1"))
  (setq cx (strcase cx))

  (setq tablero (getstring "\nTablero (D) [TD1]: "))
  (if (= tablero "") (setq tablero "TD1"))
  (setq tablero (strcase tablero))

  (if (not (ade:block-exists "I.E-AD-09"))
    (progn
      (princ "\n[AD-ELEC] Bloque I.E-AD-09 no encontrado en el DWG.")
      (princ "\n          Insertá el bloque manualmente al menos una vez primero.")
      (exit)
    )
  )

  (cond
    ((= modo "Perimetro") (ade:tomas-perimetro cx tablero))
    ((= modo "Manual")    (ade:tomas-manual    cx tablero))
    ((= modo "Desde")     (ade:tomas-desde     cx tablero))
  )
  (princ)
)

;; ── MODO PERÍMETRO ────────────────────────────────────────────
(defun ade:tomas-perimetro (cx tablero / ent dist-str dist
                             total-len n-tomas i dist-actual
                             pt tang ang count)
  (setq ent (car (entsel "\nSeleccioná la polilínea de pared: ")))
  (if (null ent) (exit))

  (setq dist-str (getstring "\nDistancia entre tomas [3.0]: "))
  (if (or (= dist-str "") (null dist-str))
    (setq dist 3.0)
    (setq dist (atof dist-str))
  )
  (if (<= dist 0) (setq dist 3.0))

  (setq total-len (vla-get-Length (vlax-ename->vla-object ent))
        n-tomas   (fix (/ total-len dist))
        i         0
        count     0)

  (repeat (1+ n-tomas)
    (setq dist-actual (* i dist))
    (if (> dist-actual total-len)
      (setq dist-actual total-len)
    )
    ;; Obtener punto en la curva
    (setq pt   (vlax-invoke (vlax-ename->vla-object ent)
                             'GetPointAtDist dist-actual)
          tang (vlax-invoke (vlax-ename->vla-object ent)
                             'GetFirstDerivative
                             (vlax-invoke (vlax-ename->vla-object ent)
                                          'GetParameterAtDistance dist-actual))
          ang  (+ (angle '(0 0 0)
                         (list (vlax-safearray-get-element tang 0)
                               (vlax-safearray-get-element tang 1)
                               0.0))
                  (/ pi 2.0))
    )
    (ade:insert-block "I.E-AD-09"
      (list (vlax-safearray-get-element pt 0)
            (vlax-safearray-get-element pt 1)
            0.0)
      ang
      (list (cons "CX" cx) (cons "D" tablero)))
    (setq count (1+ count)
          i     (1+ i))
  )
  (princ (strcat "\n✓ " (itoa count)
                 " tomas en perímetro — CX=" cx " D=" tablero))
)

;; ── MODO MANUAL ───────────────────────────────────────────────
;; Click → insertar toma. Esc para terminar.
(defun ade:tomas-manual (cx tablero / pt ang ang-str count)
  (setq count 0)
  (princ "\nClick para colocar tomas. ENTER en un punto vacío para terminar.")

  (while (setq pt (getpoint (strcat "\nToma #" (itoa (1+ count)) ": ")))
    ;; Pedir ángulo (Enter = 0)
    (setq ang-str (getstring
                    (strcat "\n  Ángulo °, Enter=0 (eje X): ")))
    (if (or (null ang-str) (= ang-str ""))
      (setq ang 0.0)
      (setq ang (* (atof ang-str) (/ pi 180.0)))
    )
    (ade:insert-block "I.E-AD-09"
      (list (car pt) (cadr pt) 0.0)
      ang
      (list (cons "CX" cx) (cons "D" tablero)))
    (setq count (1+ count))
  )
  (princ (strcat "\n✓ " (itoa count)
                 " tomas colocadas — CX=" cx " D=" tablero))
)

;; ── MODO DESDE ────────────────────────────────────────────────
;; Seleccionar pared → punto de referencia → distancia → insertar.
;; Repite hasta Esc.
(defun ade:tomas-desde (cx tablero / ent vla-ent total-len
                         pt-ref pt-ref-dist dist-str dist
                         dist-final pt tang ang count seguir)
  (setq ent (car (entsel "\nSeleccioná la pared (línea o polilínea): ")))
  (if (null ent) (exit))

  (setq vla-ent  (vlax-ename->vla-object ent)
        total-len (vla-get-Length vla-ent)
        count 0
        seguir t)

  (princ "\nEsc o Enter sin punto para terminar.")

  (while seguir
    (setq pt-ref (getpoint "\nPunto de referencia en la pared: "))
    (if (null pt-ref)
      (setq seguir nil)
      (progn
        ;; Proyectar punto de referencia sobre la curva
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

        ;; Punto e inclinación en la curva
        (setq pt   (vlax-invoke vla-ent 'GetPointAtDist dist-final)
              tang (vlax-invoke vla-ent 'GetFirstDerivative
                     (vlax-invoke vla-ent 'GetParameterAtDistance dist-final))
              ang  (+ (angle '(0 0 0)
                             (list (vlax-safearray-get-element tang 0)
                                   (vlax-safearray-get-element tang 1)
                                   0.0))
                      (/ pi 2.0))
        )

        (ade:insert-block "I.E-AD-09"
          (list (vlax-safearray-get-element pt 0)
                (vlax-safearray-get-element pt 1)
                0.0)
          ang
          (list (cons "CX" cx) (cons "D" tablero)))
        (setq count (1+ count))
        (princ (strcat "\n✓ Toma #" (itoa count) " insertada."))
      )
    )
  )
  (princ (strcat "\n✓ " (itoa count)
                 " tomas desde referencia — CX=" cx " D=" tablero))
)

(princ "\n[AD-ELEC] ade_tomas.lsp cargado. Comando: ADE_TOMAS")
(princ)
