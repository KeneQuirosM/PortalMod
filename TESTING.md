# TESTING.md — PortalMod

Checklist de pruebas manuales para validar PortalMod en una instalación real
de 7 Days to Die V3.0 ("Dead Hot Summer"). Ningún ítem de este checklist se
pudo ejecutar en el entorno donde se desarrolló el mod (sin acceso al juego
ni al DLL real de `Assembly-CSharp.dll`), así que **todo lo de aquí abajo
está pendiente de verificación real**.

## 1. Requisitos previos

- [ ] 7 Days to Die **V3.0** ("Dead Hot Summer") instalado.
- [ ] **EAC (Easy Anti-Cheat) desactivado**.
- [ ] DLL compilado (`PortalMod.dll`) y copiado a `PortalMod/Harmony/` (o a
      la raíz del mod, según config del `.csproj` — ver README.md).
- [ ] Carpeta `PortalMod/` completa copiada a `[7D2D]/Mods/PortalMod/`.

## 2. Pruebas de carga del mod

- [ ] El mod aparece en la lista de mods del juego.
- [ ] No hay errores `ERR` ni `EXC` relacionados con PortalMod en el log (F1).
- [ ] Los Harmony patches cargan sin excepciones (buscar
      `Harmony patches aplicados` en el log, ver `API.cs`).
- [ ] `PortalManager` y `PortalTeleport` se inicializan correctamente
      (buscar `PortalManager inicializado` / `PortalTeleport inicializado`).

## 3. Pruebas de crafteo

- [ ] `portalBlockItem` aparece en el workbench.
- [ ] La receta muestra los ingredientes correctos: 1500× resourceScrapIron,
      450× resourceForgedIron, 125× resourceElectricParts, 20×
      resourceDuctTape (ver `Config/recipes.xml`).
- [ ] El ícono `guppyFuturePortal6` se ve correctamente en el inventario.
- [ ] El crafteo completa en 60 segundos.
- [ ] `itemPortalKeyCard` aparece en el juego (creativo o loot).
      **Nota:** a la fecha de este checklist, el mod NO define ningún item
      con ese nombre — solo existe `portalBlockItem` (ver
      `Config/items.xml`). El ícono `gupPortKeyCard` está reservado pero sin
      usar. Si este ítem se agrega más adelante, actualizar este checklist;
      si no, este punto debe marcarse como "N/A" al probar.
- [ ] El ícono `gupPortKeyCard` se ve correctamente en inventario (aplica
      solo si el punto anterior deja de ser N/A).

## 4. Pruebas de colocación

- [ ] Al colocar el `portalBlock` aparece la ventana de nombrado
      (`windowPortalTag`, ver `UIFrames/XUi_InGame/windows.xml`).
- [ ] Se puede escribir un tag y confirmar.
- [ ] El modelo 3D `gupFuturePortal1` (inactivo) se ve en el mundo.
- [ ] El portal queda registrado como huérfano correctamente (un solo
      portal con ese tag).

## 5. Pruebas de vinculación

- [ ] Colocar dos portales con el mismo tag los vincula.
- [ ] El modelo cambia a `gupFuturePortal6` (activo) al vincularse.
- [ ] En el HUD aparece "Portal activo — conectado a '{tag}'".
- [ ] Colocar un tercer portal con un tag ya usado (2/2) muestra error en
      HUD ("Tag en uso — ya existen 2 portales con ese nombre") y no se
      registra.
- [ ] Un portal sin par muestra "Destino no encontrado — coloca otro
      portal con tag '{tag}'" al intentar entrar.

## 6. Pruebas de teletransporte

- [ ] Entrar al portal A teletransporta al portal B.
- [ ] Entrar al portal B teletransporta al portal A (bidireccional).
- [ ] El buff `buffPortalTravel` se aplica (congelación de movimiento, 2s).
- [ ] El sonido `gupKeyCardSound` suena al activarse.
- [ ] El efecto `gupFuturePortal4` se ve en el destino.
- [ ] El efecto `gupTeleportRide` se ve durante el viaje (loop de los 2s).
- [ ] El cooldown de 5s impide teletransportar inmediatamente después de
      un viaje (verificar que no ocurran loops).

## 7. Pruebas de sala de portales

- [ ] Múltiples pares con distintos tags conviven sin conflicto.
- [ ] "Westland" enlaza solo con "Westland", no con "Base".
- [ ] "Base" enlaza solo con "Base", no con "Westland".

## 8. Pruebas de renombrado

- [ ] Interactuar con **E** en un portal ya colocado abre la ventana de
      renombrado (no la de colocación inicial).
- [ ] Cambiar el tag desvincula el portal anterior (el antiguo par, si lo
      tenía, queda huérfano).
- [ ] El portal renombrado se vincula correctamente con su nuevo par (si
      ya existe otro portal con el nuevo tag).

## 9. Pruebas de destrucción

- [ ] Destruir un portal lo desregistra del sistema (`PortalManager`).
- [ ] El par restante queda como huérfano automáticamente.
- [ ] El HUD muestra el mensaje de portal huérfano al intentar usar el
      par restante.

## 10. Pruebas de TODOs pendientes

Estos puntos corresponden a los `// TODO: verificar en Assembly-CSharp
V3.0 ...` dejados en el código y los XML por no poder confirmarse sin el
juego real. Anotar el resultado real de cada uno al probar:

- [x] Nombre del prefab dentro de cada bundle — **confirmados** contra el
      XML original del mod de assets (SCore): `guppyFuturePortal1.prefab`
      (`gupFuturePortal1.unity3d`, `blocks.xml`), `guppyFuturePortal6.prefab`
      (`gupFuturePortal6.unity3d`, `blocks.xml`), `guppyFuturePortal4.prefab`
      (`gupFuturePortal4.unity3d`, `buffs.xml`), `guppyPortKeyCard.prefab`
      (`gupPortKeyCard.unity3d`, `items.xml` — `Meshfile`/`DropMeshfile`),
      `guppyTeleportRide` (`gupTeleportRide.unity3d`, `buffs.xml`),
      `guppyKeyCardSound` (`gupKeyCardSound.unity3d`, clip interno usado por
      `sounds.xml`). Pendiente de verificar en el juego real que estos
      nombres efectivamente cargan sin error (no se pudo probar en el
      entorno de desarrollo de este mod).
- [x] Confirmar si `buffs.xml` acepta rutas `#@modfolder:` directamente en
      los atributos `particle`/`sound` de `<triggered_effect>` — **confirmado
      que NO para sonido**: `action="PlaySound"` requiere un nombre de sonido
      registrado en `Config/sounds.xml` (`SoundDataNode`), no una ruta de
      AssetBundle directa; se agregó `sounds.xml` con el nodo `guppyKeyUsed`
      y `buffs.xml` ahora usa `sound="guppyKeyUsed"`. Para `particle`
      (`PlayParticleEffect`) y para prefabs adjuntos a entidad
      (`AttachPrefabToEntity`) sí se confirmó que aceptan rutas
      `#@modfolder:` directas.
- [x] Confirmar si `Meshfile` es la propiedad correcta para el mesh en
      mano/inventario de items que extienden `baseFullBlockPlaceHolder` en
      V3.0 — **confirmado**: `Meshfile` (mesh en mano) y `DropMeshfile`
      (mesh tirado en el suelo) son ambas correctas; `items.xml` ya usa las dos.
- [x] Confirmar si `sounds.xml` es necesario para registrar sonidos —
      **confirmado**: se creó `Config/sounds.xml` (nuevo archivo) con el
      `SoundDataNode` `guppyKeyUsed`.
- [ ] Confirmar firmas exactas de los métodos parcheados con Harmony en
      `Assembly-CSharp.dll` V3.0: `Block.OnBlockPlaceBefore`,
      `Block.OnBlockActivated`, `Block.OnBlockRemoved` (`PortalBlockPatch.cs`).
- [ ] Confirmar método correcto de teletransporte de `EntityPlayer` en
      servidor dedicado (`PortalTeleport.cs`, actualmente usa
      `player.SetPosition`).
- [ ] Confirmar API de persistencia oficial ligada al slot de guardado del
      mundo (`PortalManager.cs`, actualmente usa un archivo propio del mod).
- [ ] Confirmar acceso al `XUiManager` del jugador local y nombres de
      controles del sistema de binding V3.0 (`XUiPortalTag.cs`,
      `windows.xml`).

## 11. Pruebas de multijugador (si aplica)

- [ ] Cada jugador gestiona sus propios portales por steamId
      (`PortalIdentity.GetSteamId`).
- [ ] Los portales de un jugador no interfieren con los de otro, incluso
      usando el mismo tag.
- [ ] El teletransporte funciona en servidor dedicado (no solo en
      single-player/host).

## Cómo reportar un bug

Incluir en el reporte:

1. Paso exacto donde falló el checklist (número de sección + ítem).
2. Contenido del log (F1 → buscar `PortalMod` o `ERR`).
3. Versión del juego y sistema operativo.
