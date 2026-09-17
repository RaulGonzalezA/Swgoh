# Investment Optimizer: inventario real

El proveedor público actual de SWGOH no expone el inventario privado de materiales, gear ni mods desequipados. Por eso el optimizador mantiene un snapshot de inventario introducido o importado explícitamente por el usuario.

- El snapshot se persiste por ally code y conserva fecha y origen.
- Los requisitos de materiales de reliquia se acumulan desde la reliquia actual hasta la reliquia objetivo.
- El ranking muestra cantidades requeridas, disponibles y faltantes por recurso.
- Si todos los relic mats contabilizados están cubiertos, la recomendación recibe un bonus de disponibilidad.
- Esto no implica que estén cubiertos gear previo, fragmentos, energía u otros prerrequisitos no presentes en el snapshot.
- Sin snapshot, el Investment Optimizer conserva el ranking de coste relativo existente.

La API expone `GET` y `PUT` en `/api/v1/investments/players/{allyCode}/inventory` y la pantalla `/investments/{allyCode}` permite editar el snapshot y recalcular el ranking inmediatamente.
