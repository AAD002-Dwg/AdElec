(defun c:w (/ pt1 pt2 ss last-point)
  (defun getInsertionPoint (ent)
    (cdr (assoc 10 (entget ent)))
  )

  (defun drawArc45 (last-point)
    (if last-point
      (progn
        (prompt "\nSeleccione el bloque destino (INSERT) para continuar el arco: ")
        (setq ss (ssget '((0 . "INSERT"))))
        (if (and ss (= (sslength ss) 1))
          (progn
            (setq pt2 (getInsertionPoint (ssname ss 0)))
            (command "_.ARC" last-point "F" pt2 "U" "45")
            (drawArc45 pt2)
          )
          (progn
            (alert "Debe seleccionar exactamente un bloque de tipo INSERT.")
            (drawArc45 last-point)
          )
        )
      )
      (progn
        (prompt "\nSeleccione dos bloques (INSERT) para el primer arco: ")
        (setq ss (ssget '((0 . "INSERT"))))
        (if (and ss (= (sslength ss) 2))
          (progn
            (setq pt1 (getInsertionPoint (ssname ss 0)))
            (setq pt2 (getInsertionPoint (ssname ss 1)))
            (command "_.ARC" pt1 "F" pt2 "U" "45")
            (drawArc45 pt2)
          )
          (progn
            (alert "Debe seleccionar exactamente dos bloques.")
            (drawArc45 nil)
          )
        )
      )
    )
    (princ)
  )

  (drawArc45 nil)
)

(princ)
