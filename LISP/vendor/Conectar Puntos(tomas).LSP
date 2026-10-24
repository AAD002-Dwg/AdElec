(defun c:WA (/ ent1 ent2 pt1 pt2 puntos seguir pt-extra cx1 cx2 layer-name)

  (vl-load-com)

  (defun leer-cx (blk)
    (vl-some '(lambda (att)
                (if (= (strcase (vla-get-tagstring att)) "CX")
                  (vla-get-textstring att)
                )
              )
             (vlax-invoke (vlax-ename->vla-object blk) 'getattributes)
    )
  )

  ;; Función para expandir puntos con segmentos rectos/diagonales
  (defun expandir-puntos (pts / i A B dx dy adx ady diagonal sign_dx sign_dy p_diag nueva-lista)
    (cond
      ((<= (length pts) 1) pts)
      (t
        (setq nueva-lista (list (car pts)))
        (setq i 0)
        (repeat (- (length pts) 1)
          (setq A (nth i pts))
          (setq B (nth (1+ i) pts))
          (setq dx (- (car B) (car A)))
          (setq dy (- (cadr B) (cadr A)))
          (setq adx (abs dx))
          (setq ady (abs dy))
          (setq diagonal (min adx ady))
          (setq sign_dx (if (>= dx 0) 1 -1))
          (setq sign_dy (if (>= dy 0) 1 -1))

          (if (> diagonal 0)
            (progn
              (setq p_diag (list (+ (car A) (* sign_dx diagonal)) (+ (cadr A) (* sign_dy diagonal))))
              (setq nueva-lista (append nueva-lista (list p_diag)))
            )
          )

          (if (not (equal diagonal (max adx ady) 1e-6))
            (setq nueva-lista (append nueva-lista (list B)))
          )
          (setq i (1+ i))
        )
        nueva-lista
      )
    )
  )

  ;; Selección inicial de bloque
  (princ "\nSeleccioná el primer bloque...")
  (setq ent1 (car (entsel "\nPrimer bloque: ")))

  (if ent1
    (progn
      (setq pt1 (cdr (assoc 10 (entget ent1))))
      (setq cx1 (leer-cx ent1))
      (if (null cx1)
        (progn (princ "\n❌ Ese bloque no tiene atributo CX.") (exit))
      )
      (setq seguir T
            puntos (list pt1))

      (while seguir
        (princ (strcat "\nSeleccioná el siguiente bloque del circuito " cx1 " o ENTER para terminar..."))
        (setq ent2 (car (entsel "\nSiguiente bloque: ")))

        (if ent2
          (progn
            (setq cx2 (leer-cx ent2))
            (if (not (= cx2 cx1))
              (progn (princ (strcat "\n❌ Ese bloque es de otro circuito (" cx2 "), se ignora.")) )
              (progn
                (setq pt2 (cdr (assoc 10 (entget ent2))))

                ;; Preguntar puntos intermedios
                (setq pt-extra nil)
                (while (setq pt-extra (getpoint "\nIndicar punto intermedio o ENTER para continuar: "))
                  (setq puntos (append puntos (list pt-extra)))
                )

                ;; Agregar punto final
                (setq puntos (append puntos (list pt2)))

                ;; Expandir puntos para segmentos rectos/diagonales
                (setq puntos (expandir-puntos puntos))

                ;; Dibujar polilínea
                (setq layer-name (strcat "Canalizacion_" cx1))
                (if (not (tblsearch "LAYER" layer-name))
                  (vla-add (vla-get-layers (vla-get-activedocument (vlax-get-acad-object))) layer-name)
                )

                (entmake (append
                  (list (cons 0 "LWPOLYLINE")
                        (cons 100 "AcDbEntity")
                        (cons 8 layer-name)
                        (cons 100 "AcDbPolyline")
                        (cons 90 (length puntos))
                        (cons 70 0)) ; Open polyline
                  (mapcar '(lambda (pt) (cons 10 pt)) puntos)
                ))

                ;; Actualizar último punto
                (setq pt1 pt2
                      puntos (list pt1))
              )
            )
          )
          (setq seguir nil) ;; si no selecciona nada, termina
        )
      )
      (princ "\n✅ Canalización terminada.")
    )
    (princ "\n⚠️ No seleccionaste ningún bloque.")
  )
  (princ)
)