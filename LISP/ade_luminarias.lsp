;; ============================================================
;;  ade_luminarias.lsp  —  ADE_LUMINARIAS
;;  Distribución interactiva de luminarias en una grilla.
;;
;;  Flujo:
;;    1. Pedir CX (circuito) y D (tablero)
;;    2. Seleccionar polilínea cerrada del recinto
;;    3. Ajustar filas/columnas con W/S/A/D en tiempo real (grread)
;;    4. Enter = confirmar, Esc = cancelar
;;    5. Insertar bloques I.E-AD-07 solo dentro del polígono
;; ============================================================

(defun c:ADE_LUMINARIAS ( /
    cx tablero ent poly-verts bbox
    minX minY maxX maxY W H
    cols rows
    done confirmed key gr-val
    pts-grilla pt-mundo visible-count)

  (vl-load-com)

  ;; ── 1. Inputs iniciales ──────────────────────────────────
  (setq cx      (getstring "\nCircuito [C1]: "))
  (if (= cx "") (setq cx "C1"))
  (setq cx (strcase cx))

  (setq tablero (getstring "\nTablero (D) [TD1]: "))
  (if (= tablero "") (setq tablero "TD1"))
  (setq tablero (strcase tablero))

  ;; ── 2. Seleccionar polilínea ─────────────────────────────
  (setq ent (car (entsel "\nSeleccioná el contorno del recinto (polilínea cerrada): ")))
  (if (null ent) (exit))
  (if (not (= (cdr (assoc 0 (entget ent))) "LWPOLYLINE"))
    (progn (princ "\nDebe ser una polilínea cerrada.") (exit))
  )

  ;; ── 3. Extraer geometría del recinto ────────────────────
  (setq poly-verts (ade:poly-vertices ent)
        bbox       (ade:poly-bbox ent)
        minX  (nth 0 bbox)
        minY  (nth 1 bbox)
        maxX  (nth 2 bbox)
        maxY  (nth 3 bbox)
        W     (- maxX minX)
        H     (- maxY minY))

  (if (or (< W 0.1) (< H 0.1))
    (progn (princ "\nRecinto demasiado pequeño.") (exit))
  )

  ;; ── 4. Cantidad inicial (aprox 1 cada 2.5m) ─────────────
  (setq cols (max 1 (fix (+ 0.5 (/ W 2.5))))
        rows (max 1 (fix (+ 0.5 (/ H 2.5)))))

  ;; ── 5. Bucle interactivo con grread ─────────────────────
  (princ "\n── ADE_LUMINARIAS ─────────────────────────────────")
  (princ "\n  W +fila  S -fila  A +col  D -col")
  (princ "\n  Enter = confirmar   Esc = cancelar")
  (princ "\n────────────────────────────────────────────────────")

  ;; Función local para calcular puntos de la grilla filtrados
  (defun calc-pts-visibles (/ stepX stepY startX startY c r px py pts)
    (setq pts    '()
          stepX  (/ W cols)
          stepY  (/ H rows)
          startX (+ minX (/ stepX 2.0))
          startY (+ minY (/ stepY 2.0))
          c 0)
    (repeat cols
      (setq r 0)
      (repeat rows
        (setq px (+ startX (* c stepX))
              py (+ startY (* r stepY)))
        (if (ade:point-in-polygon (list px py) poly-verts)
          (setq pts (append pts (list (list px py 0.0))))
        )
        (setq r (1+ r))
      )
      (setq c (1+ c))
    )
    pts
  )

  ;; Función para mostrar estado en la línea de comando
  (defun mostrar-estado (n / )
    (princ (strcat "\rLuminarias: " (itoa cols) "x" (itoa rows)
                   " = " (itoa n) " visibles"
                   "  [W+fila S-fila A+col D-col Enter=OK Esc=cancel]  "))
  )

  (setq done nil confirmed nil)

  (while (not done)
    (setq pts-grilla     (calc-pts-visibles)
          visible-count  (length pts-grilla))
    (mostrar-estado visible-count)

    ;; grread: retorna '(tipo valor)
    ;; tipo 2 = teclado; valor = código ASCII
    (setq gr-val (grread t 4 0)
          key    (cadr gr-val))

    (cond
      ;; Enter (13) o espacio (32) → confirmar
      ((or (= key 13) (= key 32))
       (setq done t confirmed t))

      ;; Esc (27) → cancelar
      ((= key 27)
       (setq done t confirmed nil))

      ;; W (87) o w (119) → +fila
      ((or (= key 87) (= key 119))
       (setq rows (min 30 (1+ rows))))

      ;; S (83) o s (115) → -fila
      ((or (= key 83) (= key 115))
       (setq rows (max 1 (1- rows))))

      ;; A (65) o a (97) → +col
      ((or (= key 65) (= key 97))
       (setq cols (min 30 (1+ cols))))

      ;; D (68) o d (100) → -col
      ((or (= key 68) (= key 100))
       (setq cols (max 1 (1- cols))))
    )
  )

  (if (not confirmed)
    (progn (princ "\nCancelado.") (exit))
  )

  ;; ── 6. Recalcular puntos finales y verificar bloque ─────
  (setq pts-grilla (calc-pts-visibles))

  (if (not (ade:block-exists "I.E-AD-07"))
    (progn
      (princ "\n[AD-ELEC] Bloque I.E-AD-07 no encontrado en el DWG.")
      (princ "\n          Insertá el bloque manualmente al menos una vez primero.")
      (exit)
    )
  )

  ;; ── 7. Insertar bloques ──────────────────────────────────
  (setq visible-count 0)
  (foreach pt pts-grilla
    (ade:insert-block "I.E-AD-07" pt 0.0
      (list (cons "CX" cx)
            (cons "D"  tablero)))
    (setq visible-count (1+ visible-count))
  )

  (princ (strcat "\n✓ " (itoa visible-count)
                 " luminarias insertadas — CX=" cx " D=" tablero))
  (princ)
)

(princ "\n[AD-ELEC] ade_luminarias.lsp cargado. Comando: ADE_LUMINARIAS")
(princ)
