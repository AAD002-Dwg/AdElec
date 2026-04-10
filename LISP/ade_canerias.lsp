;; ============================================================
;;  ade_canerias.lsp  —  ADE_CANERIAS  v2.0
;;  Trazado de canalizaciones electricas.
;;
;;  Modos:
;;    Luminarias -> arcos de 45 grados entre bloques I.E-AD-07
;;    Tomas      -> polilineas 45 grados entre bloques I.E-AD-09
;;                  con puntos intermedios opcionales
;;
;;  Cada modo tiene sub-modo Manual / Auto (por circuito CX).
;;
;;  Shortcuts:
;;    W    -> ADE_CANERIAS (pregunta modo)
;;    WQ   -> directo a Luminarias
;;    WE   -> directo a Tomas
;;
;;  v2.0: refactor con buenas practicas Lee Mac:
;;   - Error handler local con restore de variables
;;   - Undo marks (StartUndo/EndUndo) para rollback con U
;;   - Save/restore CMDECHO y OSMODE
;;   - Modo Luminarias Manual: interactivo continuo (arco por arco)
;;   - Modo Tomas Manual: expandir-puntos para diagonal 45 + recto
;;   - LWPOLYLINE creada con entmake (mas limpio que command _.PLINE)
;; ============================================================

(vl-load-com)

;; ── VARIABLES GLOBALES DE PERSISTENCIA ───────────────────────
(if (not *ADE:CAN:MODO*)         (setq *ADE:CAN:MODO* "Luminarias"))
(if (not *ADE:CAN:SUBMODO*)      (setq *ADE:CAN:SUBMODO* "Manual"))
(if (not *ADE:CAN:CX*)           (setq *ADE:CAN:CX* "C1"))
(if (not *ADE:CAN:CAPA-LUZ*)     (setq *ADE:CAN:CAPA-LUZ* "Canerias_Luz"))

;; ── Cache del ActiveDocument (patron Lee Mac LM:acdoc) ──────
(defun ade:acdoc ()
  (eval (list 'defun 'ade:acdoc 'nil
    (vla-get-activedocument (vlax-get-acad-object))))
  (ade:acdoc)
)

;; ── EndUndoMark seguro (no falla si no hay marca abierta) ───
(defun ade:endundo ()
  (while (= 8 (logand 8 (getvar "UNDOCTL")))
    (vla-endundomark (ade:acdoc))
  )
)

;; ── Leer punto de insercion de un ename ─────────────────────
(defun ade:ins-point (ent)
  (cdr (assoc 10 (entget ent)))
)

;; ═════════════════════════════════════════════════════════════
;;  COMANDO PRINCIPAL
;; ═════════════════════════════════════════════════════════════
(defun c:ADE_CANERIAS ( / modo old-err old-cmd)
  (setq old-err *error*
        old-cmd (getvar "CMDECHO"))

  (defun *error* (msg)
    (setvar "CMDECHO" old-cmd)
    (ade:endundo)
    (if (and msg (not (wcmatch (strcase msg t) "*break,*cancel*,*exit*")))
      (princ (strcat "\nError: " msg))
    )
    (setq *error* old-err)
    (princ)
  )

  (setvar "CMDECHO" 0)
  (vla-startundomark (ade:acdoc))

  (princ "\n── ADE_CANERIAS v2.0 ──────────────────────────")

  (initget "Luminarias Tomas")
  (setq modo (getkword
    (strcat "\nModo [Luminarias/Tomas] <" *ADE:CAN:MODO* ">: ")))
  (if (null modo) (setq modo *ADE:CAN:MODO*) (setq *ADE:CAN:MODO* modo))

  (cond
    ((= modo "Luminarias") (ade:canerias-luminarias))
    ((= modo "Tomas")      (ade:canerias-tomas))
  )

  (ade:endundo)
  (setvar "CMDECHO" old-cmd)
  (setq *error* old-err)
  (princ)
)

;; ── Shortcuts ────────────────────────────────────────────────
(defun c:W  ()                       ; W -> pregunta modo
  (c:ADE_CANERIAS))

(defun c:WQ ( / old-err old-cmd)     ; WQ -> directo a Luminarias
  (setq old-err *error*
        old-cmd (getvar "CMDECHO"))
  (defun *error* (msg)
    (setvar "CMDECHO" old-cmd)
    (ade:endundo)
    (if (and msg (not (wcmatch (strcase msg t) "*break,*cancel*,*exit*")))
      (princ (strcat "\nError: " msg)))
    (setq *error* old-err) (princ))
  (setvar "CMDECHO" 0)
  (vla-startundomark (ade:acdoc))
  (setq *ADE:CAN:MODO* "Luminarias")
  (ade:canerias-luminarias)
  (ade:endundo)
  (setvar "CMDECHO" old-cmd)
  (setq *error* old-err)
  (princ)
)

(defun c:WE ( / old-err old-cmd)     ; WE -> directo a Tomas
  (setq old-err *error*
        old-cmd (getvar "CMDECHO"))
  (defun *error* (msg)
    (setvar "CMDECHO" old-cmd)
    (ade:endundo)
    (if (and msg (not (wcmatch (strcase msg t) "*break,*cancel*,*exit*")))
      (princ (strcat "\nError: " msg)))
    (setq *error* old-err) (princ))
  (setvar "CMDECHO" 0)
  (vla-startundomark (ade:acdoc))
  (setq *ADE:CAN:MODO* "Tomas")
  (ade:canerias-tomas)
  (ade:endundo)
  (setvar "CMDECHO" old-cmd)
  (setq *error* old-err)
  (princ)
)

;; ═════════════════════════════════════════════════════════════
;;  LUMINARIAS — arcos de 45 grados
;; ═════════════════════════════════════════════════════════════

(defun ade:canerias-luminarias ( / sub-modo capa cx pts i)

  (initget "Manual Auto")
  (setq sub-modo (getkword
    (strcat "\nModo arcos [Manual/Auto] <" *ADE:CAN:SUBMODO* ">: ")))
  (if (null sub-modo) (setq sub-modo *ADE:CAN:SUBMODO*)
                      (setq *ADE:CAN:SUBMODO* sub-modo))

  (setq capa (getstring
    (strcat "\nCapa para arcos [" *ADE:CAN:CAPA-LUZ* "]: ")))
  (if (= capa "") (setq capa *ADE:CAN:CAPA-LUZ*)
                  (setq *ADE:CAN:CAPA-LUZ* capa))
  (ade:ensure-layer capa 4)

  (cond
    ((= sub-modo "Auto")  (ade:canerias-luminarias-auto capa))
    ((= sub-modo "Manual") (ade:canerias-luminarias-manual capa))
  )
)

;; ── Modo Auto: por circuito CX, ordena y cablea ──────────────
(defun ade:canerias-luminarias-auto (capa / cx pts i)
  (setq cx (getstring
    (strcat "\nCircuito a cablear [" *ADE:CAN:CX* "]: ")))
  (if (= cx "") (setq cx *ADE:CAN:CX*)
                (setq *ADE:CAN:CX* (strcase cx)))
  (setq cx  (strcase cx)
        pts (ade:get-blocks-by-cx "I.E-AD-07" cx))

  (if (< (length pts) 2)
    (progn
      (princ (strcat "\n  Menos de 2 luminarias con CX=" cx "."))
      (exit)
    )
  )
  ;; Ordenar fila a fila (Y desc, X asc)
  (setq pts (vl-sort pts
              '(lambda (a b)
                 (if (> (abs (- (cadr a) (cadr b))) 0.01)
                   (> (cadr a) (cadr b))
                   (< (car a) (car b))))))

  (princ (strcat "\n  " (itoa (length pts))
                 " luminarias con CX=" cx "."))

  (setq i 0)
  (repeat (1- (length pts))
    (ade:draw-arc-45 (nth i pts) (nth (1+ i) pts) capa)
    (setq i (1+ i))
  )
  (princ (strcat "\n  " (itoa (1- (length pts)))
                 " arcos trazados en capa '" capa "'."))
)

;; ── Modo Manual: interactivo continuo (como c:w de Lee Mac) ─
(defun ade:canerias-luminarias-manual (capa / ent pt-prev pt-curr count)
  (princ "\nSelecciona las luminarias en orden. ENTER/Esc para terminar.")

  ;; Primer bloque
  (setq ent (car (entsel "\nPrimera luminaria: ")))
  (if (null ent)
    (progn (princ "\n  Cancelado.") (exit))
  )
  (setq pt-prev (ade:ins-point ent)
        count   0)

  ;; Cadena: cada nuevo bloque dibuja arco desde el anterior
  (while
    (progn
      (setq ent (car (entsel
        (strcat "\nLuminaria #" (itoa (+ 2 count)) " [ENTER=fin]: "))))
      ent
    )
    (setq pt-curr (ade:ins-point ent))
    (ade:draw-arc-45 pt-prev pt-curr capa)
    (setq pt-prev pt-curr
          count   (1+ count))
  )

  (if (= count 0)
    (princ "\n  Se necesitan al menos 2 bloques.")
    (princ (strcat "\n  " (itoa count)
                   " arcos trazados en capa '" capa "'."))
  )
)

;; ── Dibujar arco de 45 grados entre dos puntos ──────────────
;; Usa la forma Start - End - Included Angle del comando ARC
(defun ade:draw-arc-45 (p1 p2 layer / old-layer old-osm)
  (setq old-layer (getvar "CLAYER")
        old-osm   (getvar "OSMODE"))
  (setvar "CLAYER" layer)
  (setvar "OSMODE" 0)
  (command "_.ARC" (list (car p1) (cadr p1) 0.0)
                   "_E"
                   (list (car p2) (cadr p2) 0.0)
                   "_A" "45")
  (setvar "OSMODE" old-osm)
  (setvar "CLAYER" old-layer)
)

;; ═════════════════════════════════════════════════════════════
;;  TOMAS — polilineas con diagonal 45 grados + recto
;; ═════════════════════════════════════════════════════════════

(defun ade:canerias-tomas ( / sub-modo cx capa pts)

  (initget "Manual Auto")
  (setq sub-modo (getkword
    (strcat "\nModo trazado [Manual/Auto] <" *ADE:CAN:SUBMODO* ">: ")))
  (if (null sub-modo) (setq sub-modo *ADE:CAN:SUBMODO*)
                      (setq *ADE:CAN:SUBMODO* sub-modo))

  (cond
    ((= sub-modo "Auto")   (ade:canerias-tomas-auto))
    ((= sub-modo "Manual") (ade:canerias-tomas-manual))
  )
)

;; ── Modo Auto: cablea todos los bloques de un CX ─────────────
(defun ade:canerias-tomas-auto ( / cx capa pts i expanded)
  (setq cx (getstring
    (strcat "\nCircuito a cablear [" *ADE:CAN:CX* "]: ")))
  (if (= cx "") (setq cx *ADE:CAN:CX*)
                (setq *ADE:CAN:CX* (strcase cx)))
  (setq cx (strcase cx))

  (setq capa (strcat "Canalizacion_" cx))
  (ade:ensure-layer capa 1)

  (setq pts (ade:get-blocks-by-cx "I.E-AD-09" cx))
  (if (< (length pts) 2)
    (progn
      (princ (strcat "\n  Menos de 2 tomas con CX=" cx "."))
      (exit)
    )
  )

  ;; Ordenar por X, luego Y
  (setq pts (vl-sort pts
              '(lambda (a b)
                 (if (< (abs (- (car a) (car b))) 0.01)
                   (< (cadr a) (cadr b))
                   (< (car a) (car b))))))

  (princ (strcat "\n  " (itoa (length pts))
                 " tomas con CX=" cx "."))

  ;; Expandir con diagonal 45 grados y dibujar polilinea unica
  (setq expanded (ade:expandir-puntos pts))
  (ade:draw-lwpoly expanded capa)

  (princ (strcat "\n  Canalizacion trazada en capa '" capa "'."))
)

;; ── Modo Manual: interactivo con validacion de CX ────────────
;; Adapta el patron de c:WA (Conectar Puntos): selecciona primer
;; bloque, lee CX, continua con bloques del mismo CX, permite
;; puntos intermedios entre bloques.
(defun ade:canerias-tomas-manual ( / ent1 ent2 pt1 pt2 cx1 cx2
                                     puntos seguir pt-extra
                                     capa expanded count)
  (princ "\nSelecciona el primer bloque de toma...")
  (setq ent1 (car (entsel "\nPrimer bloque: ")))

  (if (null ent1)
    (progn (princ "\n  Cancelado.") (exit))
  )

  (setq pt1 (ade:ins-point ent1)
        cx1 (ade:get-att ent1 "CX"))

  (if (null cx1)
    (progn
      (princ "\n  Ese bloque no tiene atributo CX.")
      (exit)
    )
  )
  (setq cx1  (strcase cx1)
        capa (strcat "Canalizacion_" cx1))
  (ade:ensure-layer capa 1)

  (princ (strcat "\n  Circuito " cx1 " -> capa '" capa "'."))
  (princ "\nEntre bloques podes agregar puntos intermedios (ENTER para saltar).")

  (setq seguir t
        puntos (list pt1)
        count  0)

  (while seguir
    (setq ent2 (car (entsel
      (strcat "\nSiguiente bloque del circuito " cx1
              " [ENTER=fin]: "))))

    (if (null ent2)
      (setq seguir nil)
      (progn
        (setq cx2 (ade:get-att ent2 "CX"))
        (cond
          ((null cx2)
           (princ "\n  Bloque sin atributo CX, se ignora."))
          ((not (= (strcase cx2) cx1))
           (princ (strcat "\n  Otro circuito (" cx2 "), se ignora.")))
          (t
           (setq pt2 (ade:ins-point ent2))

           ;; Puntos intermedios opcionales
           (while (setq pt-extra
                    (getpoint (last puntos)
                      "\n  Punto intermedio [ENTER=continuar]: "))
             (setq puntos (append puntos (list pt-extra)))
           )

           ;; Agregar destino
           (setq puntos (append puntos (list pt2)))

           ;; Expandir y dibujar tramo
           (setq expanded (ade:expandir-puntos puntos))
           (ade:draw-lwpoly expanded capa)
           (setq count (1+ count))
           (princ (strcat "\n  Tramo #" (itoa count) " trazado."))

           ;; Nueva cadena empieza en el ultimo punto
           (setq puntos (list pt2))
          )
        )
      )
    )
  )

  (if (= count 0)
    (princ "\n  No se trazaron tramos.")
    (princ (strcat "\n  " (itoa count)
                   " tramos trazados en capa '" capa "'."))
  )
)

;; ── expandir-puntos: convierte quiebres en diagonal 45 + recto
;; Entrada: lista de puntos '(x y [z])
;; Salida: lista de puntos donde cada quiebre se divide en
;;   diagonal 45 (corto) + tramo recto (largo restante).
;; Adaptado de "Conectar Puntos(tomas).LSP" (c:WA).
(defun ade:expandir-puntos (pts /
                             i A B dx dy adx ady diag
                             sx sy p-diag result)
  (cond
    ((<= (length pts) 1) pts)
    (t
      (setq result (list (car pts))
            i      0)
      (repeat (1- (length pts))
        (setq A   (nth i pts)
              B   (nth (1+ i) pts)
              dx  (- (car B)  (car A))
              dy  (- (cadr B) (cadr A))
              adx (abs dx)
              ady (abs dy)
              diag (min adx ady)
              sx  (if (>= dx 0) 1.0 -1.0)
              sy  (if (>= dy 0) 1.0 -1.0))

        (if (> diag 1e-6)
          (progn
            ;; Punto a 45 grados desde A (recorriendo el lado corto)
            (setq p-diag (list (+ (car A)  (* sx diag))
                               (+ (cadr A) (* sy diag))))
            (setq result (append result (list p-diag)))
          )
        )

        ;; Agregar B solo si no coincide con p-diag (evitar duplicado)
        (if (not (equal diag (max adx ady) 1e-6))
          (setq result (append result (list B)))
        )
        (setq i (1+ i))
      )
      result
    )
  )
)

;; ── Dibujar LWPOLYLINE con entmake (Lee Mac style) ──────────
(defun ade:draw-lwpoly (pts layer)
  (if (>= (length pts) 2)
    (entmake
      (append
        (list '(0 . "LWPOLYLINE")
              '(100 . "AcDbEntity")
              (cons 8 layer)
              '(100 . "AcDbPolyline")
              (cons 90 (length pts))
              '(70 . 0))
        (mapcar '(lambda (p)
                   (cons 10 (list (car p) (cadr p))))
                pts)
      )
    )
  )
)

(princ "\n[AD-ELEC] ade_canerias.lsp v2.0 cargado.")
(princ "\n  Comandos: ADE_CANERIAS | W | WQ (luminarias) | WE (tomas)")
(princ)
