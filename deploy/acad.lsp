;;; acad.lsp — Carga automática de AD-ELEC al abrir AutoCAD
;;; Copiá este archivo a: C:\Users\<TU_USUARIO>\AppData\Roaming\Autodesk\AutoCAD 2025\R25.0\enu\Support\
;;; o agregá G:\PROYECTOS AD\AD-ELEC\deploy\ al Support File Search Path en Opciones → Archivos.

(defun s::startup ()
  (vl-load-com)
  (setq *adElecDll* 
    (cond 
      ((findfile "AdElec.AutoCAD.dll"))
      ((findfile "G:\\PROYECTOS AD\\AD-ELEC\\deploy\\AdElec.AutoCAD.dll"))
    )
  )
  (if (and *adElecDll* (findfile *adElecDll*))
    (progn
      (command "_.NETLOAD" *adElecDll*)
      (princ (strcat "\n[AD-ELEC] Plugin cargado correctamente desde " *adElecDll*))
    )
    (princ "\n[AD-ELEC] ADVERTENCIA: no se encontró AdElec.AutoCAD.dll en deploy.")
  )
  (princ)
)
