;;; acad.lsp — Carga automática de AD-ELEC al abrir AutoCAD
;;; Copiá este archivo a: C:\Users\<TU_USUARIO>\AppData\Roaming\Autodesk\AutoCAD 2025\R25.0\enu\Support\
;;; o agregá G:\AD-ELEC\deploy\ al Support File Search Path en Opciones → Archivos.

(defun s::startup ()
  (vl-load-com)
  (setq *adElecDll* "G:\\AD-ELEC\\deploy\\AdElec.AutoCAD.dll")
  (if (findfile *adElecDll*)
    (progn
      (command "_.NETLOAD" *adElecDll*)
      (princ "\n[AD-ELEC] Plugin cargado correctamente.")
    )
    (princ (strcat "\n[AD-ELEC] ADVERTENCIA: no se encontró el DLL en " *adElecDll*))
  )
  (princ)
)
