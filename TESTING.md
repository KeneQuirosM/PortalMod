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

**Nota**: esta sección cubre el item original `portalBlockItem` ("legacy").
Para los 6 items de estilo nuevos (`portalBlock_platformItem`, etc.) ver
sección 9.3.

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
      (`windowPortalTag`, ver `Config/XUi_InGame/windows.xml`).
- [ ] Se puede escribir un tag y confirmar.
- [ ] El modelo 3D `gupFuturePortal1` (inactivo) se ve en el mundo.
- [ ] El portal queda registrado como huérfano correctamente (un solo
      portal con ese tag).

## 4.1 Pruebas de modo fantasma al colocar (Feature 1)

Ver `PortalPlacementGhost.cs` y sección "Feature: modo fantasma al colocar"
en README.md. Ninguno de estos puntos se pudo probar en el entorno de
desarrollo del mod (sin acceso al juego) — es la Feature con más riesgo/
incertidumbre de todo este cambio, ver limitaciones documentadas en README.

- [ ] Al equipar cualquiera de los 7 items de portal (el original o
      cualquiera de los 6 estilos), aparece una caja semitransparente
      siguiendo la mira.
- [ ] La caja **rota** al girar el personaje (probar mirando hacia los 4
      puntos cardinales aproximados — Norte/Este/Sur/Oeste) — si no rota,
      revisar el log por `[PortalMod] Rotacion aplicada en ...` al colocar
      de verdad (confirma si `PortalOrientation.ComputeRotationFromPlayerFacing`
      corre) y si el problema es solo del ghost (Feature 1) o también de la
      colocación real (Feature 2, ver sección 4.2).
- [ ] Al soltar la mira o guardar el item, la caja desaparece.
- [ ] **Si la caja nunca aparece**: revisar el log por
      `PortalPlacementGhost: fallo resolviendo el item equipado por
      reflection` — significa que ningún candidato de nombre
      (`holdingItemItemValue`/`holdingItem`/etc., ver
      `PortalPlacementGhost.cs`) resolvió contra el `Assembly-CSharp.dll`
      real de V3.0. El mod sigue funcionando normalmente (el resto de las
      features no depende de esto) — anotar el error exacto del log para
      poder agregar el nombre real como candidato nuevo.
- [ ] La posición de la caja puede estar desplazada ~1 bloque respecto a
      donde el portal termina colocándose realmente (limitación conocida,
      ver README) — confirmar la magnitud/dirección real del desfasaje si
      lo hay, para poder ajustar el offset en `PortalPlacementGhost.Tick`.

## 4.2 Pruebas de rotación real + indicador de salida (Feature 2)

Ver `PortalOrientation.cs`, `PortalExitIndicator.cs` y
`PortalTeleport.FindLandingBlockPos`. **Advertencia de confianza**: la
convención de rotación usada acá es propia del mod (no confirmada contra el
mapeo real del motor, ver README) — estos pasos sirven también para
calibrarla si algo no coincide visualmente.

- [ ] Colocar un portal mirando hacia el Norte, y otro (en otra ubicación)
      mirando hacia el Este: los dos modelos deben verse rotados 90 grados
      entre sí (no la misma orientación fija de antes de este cambio).
      Revisar el log por `[PortalMod] Rotacion aplicada en ... rotation=...`.
- [ ] El modelo del portal muestra una pequeña flecha/chevron (`>`) violeta
      apuntando hacia un lado — ese lado debe coincidir con el lado "frente"
      calculado (mismo usado para el aterrizaje, ver siguiente punto). Si el
      indicador no aparece, revisar el log por
      `PortalExitIndicator: fallo agregando el indicador de salida`.
- [ ] Vincular un par y entrar por el portal A: al llegar a B, el jugador
      debe aparecer **al frente** de B (no incrustado adentro del propio
      marco) — comparar contra el lado señalado por la flecha del punto
      anterior.
- [ ] Repetir la prueba anterior específicamente con un par de estilo
      `portalBlock_cylinder` (`Blockname="portalBlock_cylinder"`, ver
      `Config/blocks.xml`) — este es el caso que más le costaba al
      comportamiento anterior (jugador chocando/incrustado contra el
      modelo del cilindro al salir); confirmar que ya no ocurre.
- [ ] Si la celda "al frente" del destino está bloqueada (pared, objeto),
      el aterrizaje debe caer de vuelta al comportamiento anterior (adentro
      del marco) en vez de fallar — revisar que el jugador nunca quede sin
      aterrizar.
- [ ] **Calibración** (solo si el indicador/aterrizaje NO coincide con el
      frente visual real del modelo 3D): editar únicamente
      `PortalOrientation.ForwardOffset`/`ToQuaternion` (reordenar u
      invertir el mapeo Norte/Este/Sur/Oeste) — no hace falta tocar
      `PortalTeleport.cs`/`PortalExitIndicator.cs`, ambos leen la
      convención desde ahí.

## 5. Pruebas de vinculación

- [ ] Colocar dos portales con el mismo tag los vincula.
- [ ] El modelo cambia a `gupFuturePortal5` (activo, con alas — mismo modelo
      para todos los biomas, ver sección 9.2) al vincularse.
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
- [ ] El cooldown (5s por defecto, configurable — ver `Config/
      PortalModConfig.xml`) impide teletransportar inmediatamente después de
      un viaje (verificar que no ocurran loops).
- [ ] Cambiar `TeleportCooldownSeconds` en `Config/PortalModConfig.xml` (por
      ejemplo a 15) y reiniciar el servidor: el cooldown real observado en
      juego debe coincidir con el nuevo valor. Un valor fuera de rango
      (negativo, o mayor a 30) debe caer al default (5s) — revisar el log
      por el warning correspondiente.
- [ ] Portal destino en un chunk lejano/recién generado (jugador viaja a un
      punto donde nunca estuvo antes en esta sesión, o el servidor recién
      arrancó): el jugador NO debe aparecer atascado dentro de terreno ni
      ser devuelto al origen. Revisar el log por
      `Teletransporte diferido...` (espera al chunk) o
      `FindLandingBlockPos: chunk destino ... todavia no cargado` (se agotó
      la espera configurada en `MaxChunkWaitSeconds`).
- [ ] Con `MaxChunkWaitSeconds` en 0 en la config: el comportamiento debe
      volver a ser el instantáneo original (sin espera), incluso hacia un
      chunk sin cargar.

## 6.1 Pruebas de requisito de energía (Feature "requiere electricidad")

**Historial**: la primera versión de esta Feature usaba un chequeo de
DISTANCIA propio (cualquier generador encendido a 10 bloques, sin cablear
nada). Se reemplazó por cableado REAL: `portalBlock` es ahora
`Class="Powered"` en `blocks.xml` (igual que `electricwirerelay` vanilla),
lo que le da un TileEntity eléctrico real, conectable con la **herramienta
de cableado** como cualquier generador/interruptor/trampa del juego.
`PortalPower.cs` ahora lee `TileEntityPowered.IsPowered` directo del
TileEntity, no busca nada por radio.

- [ ] Con la herramienta de cableado, se puede tirar un cable desde un
      generador/banco de baterías/panel solar hasta un `portalBlock`
      colocado (debe aceptar la conexión igual que un `electricwirerelay` o
      un foco — si el juego rechaza la conexión, revisar el TODO sobre
      `Class="Powered"`/`RequiredPower` en `blocks.xml`).
- [ ] Un portal vinculado, cableado a una fuente APAGADA o sin cablear
      (aunque haya un generador físicamente cerca sin cable), NO
      teletransporta al entrar — muestra `"Portal sin energía — conecta un
      generador"` (throttleado a 1 mensaje cada 3s).
- [ ] Encender la fuente conectada permite el teletransporte de inmediato en
      el siguiente intento.
- [ ] Apagar la fuente, o cortar el cable con la herramienta de cableado,
      corta el teletransporte de nuevo.
- [ ] El chequeo de energía es por el portal de ORIGEN (el que el jugador
      está pisando) — el portal de destino no necesita tener energía propia
      para poder LLEGAR a él.
- [ ] **Orden recomendado al construir un par**: primero colocar y VINCULAR
      ambos portales (mismo tag), y RECIÉN DESPUÉS cablear la energía. Si se
      cablea un portal ANTES de que su par quede vinculado, ese cable se
      pierde en el momento de vincularse — ver FIX real / nota de "orden de
      cableado" en `PortalVisualFX.cs` y `PortalManager.cs` sobre por qué
      (el swap de modelo al vincularse recrea el TileEntity eléctrico).
- [ ] El modelo/color del portal (bioma) ya NO cambia según el estado de
      energía (ver nota de "orden de cableado" arriba) — se ve igual
      vinculado-con-energía que vinculado-sin-energía; la única señal de
      "sin energía" es el mensaje HUD al intentar usarlo. Esto es un cambio
      respecto a la primera versión de esta Feature (que sí cambiaba el
      modelo), necesario para que el cableado real pueda mantenerse estable.

**Diagnóstico "el portal se ve apagado aunque tenga energía"**: se
investigó si `TileEntityPowered` expone algún evento/callback de cambio de
energía para refrescar el visual en vivo — **no existe ninguno**
(confirmado por reflection contra el Assembly-CSharp.dll real: el único
evento de `TileEntityPowered`/`TileEntity`/`PowerItem` es `Destroyed`, no
relacionado con energía; el grid de energía se recalcula por POLLING cada
0.16s en `PowerManager.Update()`, sin eventos). Aunque existiera, conectar
un swap de `BlockValue` a un cambio de energía reintroduciría el bug ya
descripto arriba (el propio swap desconecta el cable). Se agregaron logs
de diagnóstico para encontrar la causa REAL de "se ve apagado":

- [ ] `[PortalMod] Portal en pos X,Y,Z - IsPowered: true/false` (o
      `TileEntityPowered no encontrado`) en `PortalPower.HasNearbyPower` —
      confirma si el TileEntity eléctrico existe y su estado real.
- [ ] `[PortalMod] RefreshBlockState pos=... linked=True/False` en
      `PortalVisualFX` — confirma si el swap de modelo se INTENTÓ al
      vincular el par. Si este log nunca aparece al vincular, el bug real
      no es de energía sino de que `RegisterPortal` no llegó a completar el
      par (revisar el log `Portal registrado: ... (par actual: X/2)`).
- [ ] `[PortalMod] SetBlockState pos=... bloqueActual=... bloqueObjetivo=...`
      — si `bloqueObjetivo` ya coincide con `bloqueActual`, es esperado que
      no haya swap (ya estaba correcto). Si aparece un
      `[PortalMod] SetBlockState: no se encontro el bloque '...'`
      (warning), el bug real es que esa variante de blocks.xml no
      existe/no cargó (revisar el nombre exacto contra `blocks.xml`).

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
- [x] El `portalBlock` se puede romper con herramientas normales (pico,
      taladro) en un tiempo razonable — **FIX real**: `blocks.xml` tenía
      `<property name="Health" value="4000" />` y
      `<property name="HitMaskAll" value="0" />`, dos propiedades que NUNCA
      existieron en el esquema de bloques de V3.0 (confirmado: 0
      coincidencias de `Health`/`HitMaskAll` como propiedad de bloque en
      TODO el `blocks.xml` vanilla instalado, y ningún campo `Health`/
      `HitMaskAll` en la clase `Block` decompilada contra el
      Assembly-CSharp.dll real — mismo patrón que el fiasco anterior de
      `LightColor`/`LightIntensity`/`LightRadius`). Al ser propiedades
      fantasma, el bloque nunca tuvo un `MaxDamage` real configurado y
      quedaba con el valor por defecto del motor, sintiéndose indestructible
      en la práctica. La propiedad real (confirmada: campo `MaxDamage` tipo
      `int` en `Block`, 327 usos reales en el `blocks.xml` vanilla, rango
      típico 30 en madera hasta 7000 en bloques de acero) se agrega ahora
      como `<property name="MaxDamage" value="2000" />`, comparable al
      material de nivel scrap/metal (`corrugatedMetal*`) de la receta del
      portal.
- [x] Portales guardados en el archivo de persistencia sin bloque real en
      esa posición no se registran al cargar el mundo — ver FIX real de
      `PortalManager.Load()` en la sección 10.

## 9.1 Pruebas de persistencia entre mundos

- [ ] Los portales guardados en un mundo/slot de guardado NO aparecen en
      otro mundo distinto (antes del FIX de `GetSaveFilePath()` en la
      sección 10, el archivo `portals.dat` era único por INSTALACIÓN del
      mod, no por mundo — esto podía manifestarse como "portales apareciendo
      en posiciones random, incluso flotando en el aire" al cargar un mundo
      nuevo/distinto que reutilizaba coordenadas de un mundo anterior donde
      sí existía un portal real ahí).
- [ ] Revisar el log al cargar un mundo: debe aparecer una línea
      `[PortalMod] Validando portal cargado en pos X,Y,Z - bloque existe:
      true/false` por cada posición restaurada desde disco; las que dicen
      `false` se descartan automáticamente (y no deberían volver a
      aparecer en cargas futuras, una vez que el mod vuelva a guardar).

## 9.2 Pruebas de color/modelo por bioma (Feature "color y modelo por bioma")

El color (`TintColor`) sobre el modelo de CUALQUIER estilo (ver sección 9.3)
cambia según el bioma donde se vinculó el par:

- [ ] Colocar y vincular un par de portales en cada bioma listado muestra el
      color correspondiente sobre el modelo del estilo craftado, en cuanto
      el par queda **vinculado** (ya NO depende de tener energía — ver
      sección 6.1) — revisar el log en `RegisterPortal`: `[PortalMod]
      Bioma detectado para portal en X,Y,Z: <nombre>`, y en
      `PortalVisualFX`: `[PortalMod] RefreshBlockState pos=... linked=True
      style=...` seguido de `[PortalMod] SetBlockState pos=...
      bloqueActual=... bloqueObjetivo=...`.
      - nieve (`snow`): azul frío — `TintColor="3380FF"`.
      - yermo (`wasteland`): amarillo/dorado — `TintColor="FFCC1A"`.
      - bosque quemado (`burnt_forest`): naranja — `TintColor="FF6600"`.
      - bosque de pinos (`pine_forest`): verde — `TintColor="33FF33"`.
      - desierto (`desert`): naranja arena — `TintColor="FF991A"`.
      - default/sin mapeo (`underwater`, cualquier otro): morado —
        `TintColor="8033FF"`.
- [ ] El bioma detectado persiste correctamente al recargar el mundo (mismo
      color después de un reinicio, sin necesidad de re-vincular).

## 9.3 Pruebas de estilos de portal (Feature "Opcion A: 6 items separados")

- [ ] Los 6 items nuevos (`portalBlock_platformItem`, `portalBlock_gridItem`,
      `portalBlock_clawsItem`, `portalBlock_cylinderItem`,
      `portalBlock_wingsItem`, `portalBlock_archItem`) aparecen en el menu de
      crafteo de la mesa de trabajo, cada uno con su propio icono
      (`guppyFuturePortal1` a `6` respectivamente) y nombre localizado
      ("Platform Portal"/"Portal Plataforma", etc.).
- [ ] El item original `Teleport Portal` (`portalBlockItem`) SIGUE
      disponible sin cambios (estilo "legacy").
- [ ] Los 7 items (los 6 estilos + el original) muestran la MISMA receta:
      1500× resourceScrapIron, 450× resourceForgedIron, 125×
      resourceElectricParts, 20× resourceDuctTape, workbench, 60s (recetas
      unificadas — ver `Config/recipes.xml`).
- [ ] Colocar cada estilo muestra su modelo 3D correspondiente en estado
      inactivo — revisar el log en `RegisterPortal`: `[PortalMod] Estilo
      detectado para portal en X,Y,Z: <platform/grid/claws/cylinder/wings/
      arch>` (o `(desconocido, usa estilo default/legacy)` para el
      `portalBlockItem` original — esperado, no es un bug).
- [ ] Vincular un par usando el MISMO estilo en ambos portales funciona
      igual que antes (tag compartido, teletransporte bidireccional).
- [ ] Vincular un par usando estilos DISTINTOS en cada portal (ej. un
      `platform` y un `wings` con el mismo tag) tambien debe funcionar — el
      estilo es puramente visual, cada portal conserva el modelo de SU
      PROPIO estilo aunque esten vinculados entre si.
- [ ] Destruir un portal de estilo `grid` (por ejemplo) dropea un item
      `portalBlock_gridItem` (su propio estilo), no el `portalBlockItem`
      genérico — revisar el `<drop event="Destroy">` de cada bloque en
      `blocks.xml` si esto falla.
- [ ] El estilo persiste correctamente al recargar el mundo (mismo modelo
      después de un reinicio, sin necesidad de re-vincular).
- [ ] Cada estilo acepta cableado eléctrico igual que el original (hereda
      `Class="Powered"`/`RequiredPower` de `portalBlock` vía `Extends` — ver
      sección 6.1).
- [ ] **Pendiente de confirmar en el juego real**: los nombres de prefab
      dentro de `gupFuturePortal2/3/4/5.unity3d` (estilos grid/claws/
      cylinder/wings) — se asumió el mismo patrón que
      `gupFuturePortal1/6.unity3d` (`guppyFuturePortalN.prefab`), sin
      confirmar contra el XML original del mod de assets para los 4
      restantes. Si un modelo no carga, revisar el log de advertencia
      `SetBlockState: no se encontro el bloque` (bloque no encontrado en
      blocks.xml) vs. el log de carga de assets del juego (bloque
      encontrado pero el modelo 3D del prefab no carga — dos causas
      distintas, ver nota en sección 9.2 anterior sobre cómo diferenciarlas).

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
- [x] Confirmar firma de `Block.OnBlockPlaceBefore` — **confirmada** por el
      error real de carga de Harmony: `void OnBlockPlaceBefore(WorldBase,
      ref BlockPlacement.Result, EntityAlive, GameRandom)` (es `void`, no
      `bool`; el patch ya no declara `__result`). Pendiente: los campos
      reales de `BlockPlacement.Result` más allá de `blockPos` siguen sin
      confirmar, y sin `__result` ya no se puede distinguir si la colocación
      fue realmente aceptada — ver TODO en `Block_OnBlockPlaceBefore_Patch`.
- [x] Confirmar que `Block.OnBlockActivated` tiene múltiples sobrecargas en
      V3.0 — **confirmado** por `AmbiguousMatchException` al cargar el mod
      (ver 10.1 más abajo). El intento de especificar explícitamente
      `(WorldBase, Vector3i, BlockValue, EntityAlive)` **también falló**
      ("Undefined target method" — esos 4 tipos no coinciden con ninguna
      sobrecarga real). `Block_OnBlockActivated_Patch` quedó **comentado**
      (`/* */`) en `PortalBlockPatch.cs` para no seguir bloqueando la carga
      del mod. Se agregó `API.LogOnBlockActivatedOverloads()` (corre al
      inicio de `InitMod`, antes de `PatchAll`) que loguea con `Log.Out(...)`
      todas las sobrecargas reales de `Block.OnBlockActivated` encontradas
      por reflection — **pendiente: leer esas líneas del log del juego** para
      poder descomentar el patch con la firma correcta. Mientras tanto,
      renombrar un portal con **E** no funciona (regla 9); el resto del mod
      no depende de este patch y sigue funcionando.
- [ ] Confirmar firma exacta de `Block.OnBlockRemoved` (`PortalBlockPatch.cs`).
- [ ] Confirmar método correcto de teletransporte de `EntityPlayer` en
      servidor dedicado (`PortalTeleport.cs`, actualmente usa
      `player.SetPosition`).
- [x] Confirmar API de persistencia oficial ligada al slot de guardado del
      mundo (`PortalManager.cs`) — **confirmada**: `GameIO.GetSaveGameDir()`
      (estático, sin argumentos) devuelve la carpeta real del slot de
      guardado activo, resuelta internamente vía `GamePrefs` (`GameWorld`,
      `GameName`, `GameSaveStorageType`). `GetSaveFilePath()` ahora persiste
      ahí (`<SaveGameDir>/portals.dat`) en vez de en una ruta fija dentro de
      la carpeta del mod — ver FIX real de la sección 9 sobre por qué esto
      importa (portales de OTRO mundo apareciendo en el mundo actual).
- [ ] Confirmar acceso al `XUiManager` del jugador local y nombres de
      controles del sistema de binding V3.0 (`XUiPortalTag.cs`,
      `windows.xml`).
- [ ] **Interacción tecla E tras `Class="Powered"`** (`PortalBlockPatch.cs`,
      `BlockPowered_HasBlockActivationCommands_Patch`/
      `BlockPowered_OnBlockActivated_Patch`): no se pudo confirmar sin el
      juego real si `BlockPowered.HasBlockActivationCommands` (que devuelve
      `true` para cualquier bloque `Class="Powered"`, incluido portalBlock
      desde la Feature "requiere electricidad") efectivamente le impide al
      juego llegar a llamar al overload de 4 argumentos de
      `OnBlockActivated` que abre la ventana de nombrar/renombrar. Se
      agregaron dos patches de red de seguridad (suprimir el menú de
      comandos de `BlockPowered` para portalBlock, y redirigir cualquier
      activación con comando hacia la misma lógica de nombrar/renombrar) —
      **probar en el juego real** que presionar E sobre un portal (con o
      sin tag) sigue abriendo la ventana correcta y no un menú de comandos
      tipo "Take".
- [ ] Confirmar el mapeo real del motor entre `BlockValue.rotation` y la
      orientación visual de un bloque `Shape="ModelEntity"`
      (`PortalOrientation.cs`, Feature 2) — ver secciones "Feature: rotación
      real..." en README.md y 4.2 más arriba para el procedimiento de
      calibración si no coincide.
- [ ] Confirmar el nombre real del miembro de `Inventory` que expone el
      `ItemValue`/`ItemClass` actualmente equipado por el jugador local
      (`PortalPlacementGhost.cs`, Feature 1) — candidatos probados:
      `holdingItemItemValue`, `holdingItem` (ver lista completa en el
      archivo). Revisar el log por
      `PortalPlacementGhost: fallo resolviendo el item equipado por
      reflection` si ninguno resolvió.

### 10.1 Errores reales corregidos (compilación y carga del mod)

Estos errores salieron de compilar y cargar el DLL contra un
`Assembly-CSharp.dll` real de V3.0 (no de este entorno de desarrollo). Se
corrigieron para que compile/cargue, pero el comportamiento en juego de
cada uno sigue sin probarse:

- [x] `ModEvents.GameShutdown`/`ModEvents.GameUpdate` no aceptaban el
      handler sin parámetros — **corregido** reemplazándolos por Harmony
      patches sobre `GameManager.Update`/`GameManager.OnApplicationQuit`
      (`API.cs`). Pendiente: confirmar que `GameManager` realmente expone
      esos dos métodos con esos nombres exactos (si Harmony no los
      encuentra, falla al cargar el mod con un error claro en el log).
- [x] `EntityPlayer.PlatformUserIdentifierAbs` no existe — **CONFIRMADO Y
      CORREGIDO DE RAÍZ** (sesión de auditoría profunda de multiplayer, ver
      sección 12 para el detalle completo): el intento anterior (candidatos
      de nombre buscados por reflection DIRECTO en `EntityPlayer`) nunca
      podía funcionar — se inspeccionó el `Assembly-CSharp.dll` real
      instalado y NINGÚN miembro de identidad de plataforma existe en
      `EntityPlayer`/`EntityPlayerLocal`/`EntityAlive`/`Entity`. El dato
      real vive en `ClientInfo` (uno por conexión de red, vía
      `ConnectionManager.Instance.Clients.ForEntityId(entityId)`),
      accedido ahora de forma directa (ya no por reflection, ver
      `PortalIdentity.TryResolveStablePlatformId` en `PortalUtils.cs`):
      `ClientInfo.CrossplatformId ?? ClientInfo.PlatformId` →
      `.CombinedString`. Confirmado que compila contra el DLL real. Si el
      ownerKey logueado en `RegisterPortal` empieza con `plat:` en vez de
      ser un número corto (entityId), el fix está activo — probar en un
      servidor real que esto pase para jugadores remotos, y que sobreviva
      una reconexión (mismo `plat:...` antes y después).
- [x] `windowManager.Open(...)` con 4 argumentos no existe — **corregido**
      invocando `Open` por reflection, probando el primer método `Open`
      cuyo primer parámetro sea `string` (`XUiPortalTag.cs`). Pendiente:
      confirmar que la ventana de nombrar/renombrar portal realmente abre
      en pantalla.
- [x] `GetBindingValue` no es virtual — **corregido** cambiando `override`
      por `new` (`XUiPortalTag.cs`). Pendiente — **riesgo real**: con
      `new`, si el resolvedor de bindings de XUi invoca el método a través
      de una referencia tipada como `XUiController`, nuestra versión nunca
      se ejecuta y el título/placeholder de la ventana no se actualizará.
      Probar explícitamente que el texto de la ventana cambia entre "Nombrar
      Portal" y "Renombrar Portal" según corresponda.
- [x] `new BlockValue(int)` no compila, esperaba `uint` — **corregido**
      con cast explícito `(uint)targetBlock.blockID` (`PortalVisualFX.cs`).
- [x] `new ParticleEffect(string, Vector3)` no existe — **corregido**
      construyendo el `ParticleEffect` por reflection (constructor de 2
      argumentos si existe, si no constructor vacío + propiedades
      Name/Position) y llamando a `SpawnParticleEffectServer` también por
      reflection (`PortalVisualFX.cs`). Pendiente: confirmar que los
      efectos de partícula realmente aparecen en el mundo; si la
      reflection no encuentra un constructor/propiedades compatibles,
      solo se registra un warning en el log y no pasa nada visualmente
      (no debería romper el mod).
- [x] `Block.OnBlockPlaceBefore` es `void`, no `bool` — **corregido**,
      ver ítem dedicado en la lista principal de esta sección (10) arriba.
- [x] `AmbiguousMatchException` al cargar el mod: `Block.OnBlockActivated`
      tiene varias sobrecargas y el patch no especificaba cuál — **primer
      intento**: se agregaron los tipos explícitos `(WorldBase, Vector3i,
      BlockValue, EntityAlive)` al atributo `[HarmonyPatch(...)]`. Ese primer
      intento **también falló** — ver ítem siguiente.
- [x] `Undefined target method for patch method ... Prefix(WorldBase,
      Vector3i, BlockValue, EntityAlive, Boolean&)` — la sobrecarga de 4
      tipos del intento anterior **no existe** en el `Assembly-CSharp.dll`
      real. **Corregido** comentando por completo `Block_OnBlockActivated_Patch`
      con `/* */` (no se borró, para poder descomentarlo despues) y
      agregando `API.LogOnBlockActivatedOverloads()`, que corre al inicio de
      `InitMod` y loguea con `Log.Out("[PortalMod] OnBlockActivated
      overloads: ...")` cada sobrecarga real de `Block.OnBlockActivated`
      encontrada por reflection. **Pendiente: leer esas líneas en el log del
      juego** para conocer la firma real y poder reactivar el patch. Mientras
      tanto, renombrar un portal con la tecla E no hace nada (el resto del
      mod sigue funcionando).
- [x] `XML loader: Loading and parsing 'blocks.xml' failed` /
      `'buffs.xml' failed` — ambos archivos ya eran XML válido según un
      parser estricto (xmllint/libxml2), así que no había etiquetas sin
      cerrar ni caracteres sin escapar en el sentido estricto de XML.
      **Corregido de forma defensiva** (causa no confirmada con certeza):
      se agregó la declaración `<?xml version="1.0" encoding="UTF-8"?>`
      que faltaba en ambos archivos, y se reemplazaron los únicos caracteres
      no-ASCII presentes (guiones largos tipo em dash en los comentarios)
      por guiones ASCII normales, para eliminar cualquier ambigüedad de
      encoding que el loader del juego pueda manejar de forma más estricta
      que un parser XML genérico. **Pendiente: confirmar en el juego real**
      que esto era la causa; si el error persiste, revisar si el `?` dentro
      de los valores `#@modfolder:...?prefab` o el formato de
      `AttachPrefabToEntity`/`RemovePrefabFromEntity` en `buffs.xml` le cae
      mal al parser específico del juego.

- [x] `ModManager.ModLoaded("0-SCore")` nunca detectaba 0-SCore aunque
      estuviera instalado — **confirmado en el log real del juego** (no en
      este entorno de desarrollo): el mod carga con `Mod.Name` exacto
      `"0-SCore_sphereii"` (visible en el log como `"0-SCore_sphereii
      (3.0.16.732)"`), no `"0-SCore"`. `ModManager.ModLoaded` compara por
      igualdad exacta contra ese nombre. **Corregido** en `API.cs`: ahora
      usa `ModManager.GetLoadedMods().Any(m => m.Name.Contains("SCore"))`
      (búsqueda flexible por substring, tolera variaciones de nombre entre
      forks/versiones) en vez de un nombre exacto. Log agregado: `[PortalMod]
      SCore detectado: true/false`.

### 10.2 Comportamientos conocidos inofensivos (log)

- **"Unknown particle effect: 1566081755" (y variantes con otros IDs)** —
  proviene de partículas internas rotas del propio AssetBundle
  `gupFuturePortal6.unity3d` (no controladas por este mod ni por
  `buffs.xml`/`PortalVisualFX.cs`; ver FIX real en `blocks.xml` sobre por
  qué `portalBlockActive` dejó de usar ese modelo). No afecta el
  rendimiento (112 FPS estable en pruebas de campo). **Se filtra
  automáticamente del log** desde `LogFilterPatch.cs`, con un Harmony
  Prefix sobre `Log.Error(string)` que descarta cualquier línea que
  contenga "Unknown particle effect" ANTES de que se escriba a disco.
  Se intentó primero con `Application.logMessageReceived` (lo pedido
  originalmente) pero **no es posible por esa vía**: decompilando
  `Log.Error`/`Log.masterLogStandalone` (`LogLibrary.dll`) contra el DLL
  real se confirmó que en un build standalone (`Application.isEditor ==
  false`, el caso real de cualquier partida) ese método escribe la línea
  directo a disco y notifica sus propios listeners SIN pasar nunca por
  `UnityEngine.Debug`/`Application.logMessageReceived` — ese evento nativo
  de Unity simplemente nunca se dispara para este mensaje.

## 11. Pruebas de multijugador (si aplica)

**Ver sección 12 para el análisis de causa raíz completo (sesión de
auditoría dedicada) de los bugs reportados de multiplayer/servidores
dedicados — dos causas raíz confirmadas y corregidas, una tercera
identificada pero NO corregida (requiere testing en vivo, ver 12.3).**

- [ ] Cada jugador gestiona sus propios portales por steamId
      (`PortalIdentity.GetSteamId`) — revisar que el ownerKey logueado
      empiece con `plat:` para jugadores conectados por red (ver fix en
      sección 10.1 y detalle en 12.1).
- [ ] Los portales de un jugador no interfieren con los de otro, incluso
      usando el mismo tag.
- [ ] El teletransporte funciona en servidor dedicado (no solo en
      single-player/host) — **ver 12.3**: el registro de un portal
      colocado por un cliente remoto puede no llegar nunca al
      `PortalManager` autoritativo del servidor; confirmar específicamente
      si esto ocurre antes de asumir que el resto de la Feature funciona.
- [ ] **Portales compartidos en party**: dos jugadores en la misma party
      real del juego (creada/unida desde la UI de party nativa, NO el
      sistema de "grupo" de este mod). Con el fix de la sección 12.2
      (`PortalParty.TryGetPartyId` ahora usa `EntityPlayer.Party.PartyID`
      directo, confirmado contra el DLL real), el ownerKey de AMBOS
      jugadores debe resolver a `party:<PartyID>` — revisar el log
      `Portal registrado para key: party:X (party: True)` para cada uno.
      Antes de este fix, `TryGetPartyId` devolvía `false` SIEMPRE (ver
      12.2) — si esto se prueba con el DLL viejo, nunca va a funcionar.
- [ ] Un jugador coloca un portal INMEDIATAMENTE después de conectarse
      (para forzar el escenario de cruce de identidad — ver
      `PortalManager.ReassignSteamId`); el mismo jugador (no otro) debe
      seguir teniendo acceso a su propio portal sin interrupción. Revisar
      el log por `PortalIdentity: id de plataforma estable resuelto ...
      reasignando estado` y `ReassignSteamId: ... -> ... (cruce de
      identidad resuelto...)` si el fallback llegó a usarse antes de
      resolver `ClientInfo` (ver 12.1).

## 11.1 Pruebas de la auditoría de estabilidad (ver AUDIT.md)

Ver `AUDIT.md` para el detalle completo de cada hallazgo y su corrección.
Puntos concretos a probar en el juego real:

- [ ] Crashear el proceso a la fuerza (`kill -9` / Administrador de tareas)
      con portales sin guardar, esperar hasta 5 minutos, volver a matarlo, y
      confirmar que se perdió como máximo el último autoguardado (no toda la
      sesión).
- [ ] Pegar un tag con un salto de línea (copiar texto multilínea al
      portapapeles y pegarlo en el campo) y confirmar que no rompe
      `portals.dat` en el siguiente guardado.
- [ ] Intentar viajar a un portal en un chunk lejano recién cargado el mundo
      (chunk probablemente descargado): DESACTUALIZADO — el mod ya no
      muestra ningún mensaje de "área aún no cargada" (se eliminó
      deliberadamente para que el viaje se sienta instantáneo, ver commit
      "instant portal teleport, no chunk-load wait"). Ver en cambio la
      sección 6 ("Portal destino en un chunk lejano/recién generado") sobre
      el comportamiento actual: espera silenciosa acotada
      (`MaxChunkWaitSeconds`) en vez de bloquear con un mensaje.
- [ ] Corromper manualmente una línea de `portals.dat` (editar a mano,
      truncar un número) con el juego cerrado, volver a abrir, y confirmar
      que solo esa línea se descarta (log con "Linea corrupta/invalida... se
      salta") y el resto de los portales cargan normal.
- [ ] Provocar intencionalmente un error en medio de una sesión (por ejemplo
      con datos de mundo corruptos) y confirmar que el mod loguea el error
      con `API.LogError` en vez de crashear el proceso o congelar el juego.

## 12. Análisis de causa raíz — Multiplayer / servidores dedicados

Sesión de auditoría dedicada, pedida específicamente para los síntomas
reportados por usuarios:

1. El dueño del portal puede usarlo sin problema.
2. Los compañeros de party no pueden usarlo.
3. Si el compañero construye su propio portal, funciona algunas veces pero
   luego deja de funcionar completamente.
4. Ninguno puede usar los portales del otro.
5. En servidores dedicados los portales se pierden al reconectarse.

**Metodología**: a diferencia de sesiones anteriores (sin acceso al DLL
real), esta vez se inspeccionó `Assembly-CSharp.dll` directamente vía
`Reflection.ReflectionOnlyLoadFrom` (sin ejecutar el ensamblado) para
confirmar o refutar cada candidato de nombre usado por reflection en
`PortalParty.cs`/`PortalUtils.cs`. Se leyeron completos: `PortalManager.cs`,
`PortalParty.cs`, `PortalUtils.cs` (`PortalIdentity`), `PortalTeleport.cs`,
`PortalBlockPatch.cs`, `PortalOrientation.cs`, `PortalConfig.cs`,
`XUiPortalTag.cs`, `API.cs`.

Se encontraron **tres** causas raíz distintas. Las primeras dos están
**confirmadas contra el DLL real y corregidas**. La tercera está
**identificada con alta confianza pero NO corregida** — corregirla bien
requiere una capa de red nueva (NetPackage/RPC) que no se puede validar sin
un servidor real para iterar, y un intento a ciegas arriesga romper la
sincronización de red de TODO el mod si la implementación fuera incorrecta;
se documenta en detalle en 12.3 para que se corrija con testing en vivo.

### 12.1 Causa raíz 1 (CORREGIDA): identidad estable de jugador nunca resolvía

`PortalIdentity.TryResolveStablePlatformId` (antes de este fix) buscaba un
identificador de plataforma estable como propiedad/campo **directo** de
`EntityPlayer`, probando los candidatos `PlatformUserIdentifierAbs`,
`PlatformId`, `CrossplatformId`, `UserIdentifier`, `SteamId`, `steamID`.

**Confirmado por reflection contra el DLL real**: se enumeraron TODOS los
miembros de `EntityPlayer`, `EntityPlayerLocal`, `EntityAlive` y `Entity` —
**ninguno** de esos candidatos existe ahí, ni ningún otro miembro
relacionado con identidad de plataforma. La búsqueda apuntaba al objeto
equivocado desde el principio.

El dato real vive en un objeto completamente distinto, `ClientInfo` (uno
por conexión de red activa, mantenido por `ConnectionManager` — el mismo
objeto que el juego ya usa para autenticación/anti-cheat/networking):

```
ConnectionManager.Instance.Clients.ForEntityId(int entityId) -> ClientInfo
ClientInfo.CrossplatformId / ClientInfo.PlatformId -> PlatformUserIdentifierAbs
PlatformUserIdentifierAbs.CombinedString -> string real y estable
```

Todos esos miembros son **públicos** y viven en el namespace global (igual
que `World`/`EntityPlayer`/etc.), confirmado por reflection y por una
compilación real exitosa contra el DLL instalado.

**Impacto real**: como la búsqueda nunca miraba el lugar correcto,
`TryResolveStablePlatformId` devolvía `null` siempre, para TODO jugador —
`GetSteamId` caía SIEMPRE al fallback `entityId.ToString()`, inestable
entre reconexiones. Esto explica el síntoma 5 (portales perdidos al
reconectarse) de forma completa: no era un caso raro sin cubrir, el
mecanismo de id estable nunca funcionó desde que se agregó.

**Fix**: `PortalIdentity.TryResolveStablePlatformId` (`PortalUtils.cs`) ahora
llama directo a la cadena real de arriba (con `?.`/`??` para tolerar
conexiones sin cliente de red, ver 12.1.1), sin ninguna capa de reflection.
Se prefiere `CrossplatformId` (el id unificado del sistema de crossplay EOS
de V3.0) y se cae a `PlatformId` si el primero no resuelve. Se eliminaron
`StableIdPropertyCandidates`, `StableIdMemberCandidates`,
`_stableIdMemberCache`, `SafeFindStableIdMemberInHierarchy`,
`FindStableIdMemberInHierarchy`, `ExtractNestedId`, `IsSimpleIdType` (dead
code, ya no hace falta ninguna reflection). Se conserva el mecanismo de
cache por entityId y el de "cruce fallback→estable" (`ReassignSteamId`),
ahora con una razón real para dispararse.

#### 12.1.1 Límite conocido (no cubierto por este fix)

`ClientInfo` solo existe para conexiones de red reales (servidor dedicado,
o el propio host jugando con la red activa). En un mundo verdaderamente
offline/singleplayer (sin `ConnectionManager` con clientes), `ForEntityId`
puede devolver `null` para el jugador local — en ese caso se preserva el
fallback a `entityId.ToString()` (mismo comportamiento que antes de este
fix, pero ahora solo alcanzado en el caso realmente sin red en vez de en
todo multiplayer). **Probar**: confirmar que un singleplayer puro (sin
ningún tipo de hosting) sigue funcionando igual que antes.

### 12.2 Causa raíz 2 (CORREGIDA): detección de party nunca resolvía

`PortalParty.TryGetPartyId` (antes de este fix) probaba varios candidatos de
nombre por reflection, con un comentario propio `TODO CRÍTICO — SIN
CONFIRMAR CONTRA Assembly-CSharp.dll REAL`.

**Confirmado por reflection contra el DLL real**: el sistema de party SÍ
existe en V3.0.1 y es simple:

- `EntityPlayer.Party` — propiedad **pública**, tipo `Party`, declarada
  directo en `EntityPlayer` (no en una clase base), `null` si el jugador no
  está en ninguna party.
- `Party.PartyID` — campo **público**, `Int32`, el identificador real.

El intento anterior fallaba **siempre** (para cualquier jugador, en
cualquier party real — no un caso raro) por **dos** bugs distintos, ambos
confirmados:

1. **Sí** encontraba la propiedad `Party` (estaba en la lista de
   candidatos y existe de verdad), pero después buscaba el ID **dentro**
   de ese objeto con los candidatos `PartyId`/`Id`/`GroupId`/`TeamId`/
   `partyId`/`id` — el campo real es `PartyID` (mayúscula "ID", no "Id").
   `Type.GetField`/`GetProperty` son **case-sensitive** por defecto:
   `"PartyId"` nunca hace match contra `"PartyID"`, así que la extracción
   del ID fallaba siempre incluso cuando el objeto `Party` real ya se
   había encontrado.
2. El fallback por `PartyManager` (clase estática/singleton) sí encontraba
   la clase real `PartyManager` y su método real `GetParty(Int32)`, pero
   fallaba resolviendo la instancia singleton: buscaba una propiedad o
   campo estático llamado `Instance` — el real es la propiedad `Current`
   (el campo interno es `instance`, minúscula). Como `GetParty(Int32)` no
   es estático, sin poder resolver la instancia el candidato se descartaba
   en silencio.

**Impacto real**: `TryGetPartyId` devolvía `false` siempre — `GetPortalKey`
nunca devolvía `"party:X"` para nadie, todos los jugadores se trataban como
solitarios sin importar su party real dentro del juego. Esto explica los
síntomas 2 y 4 (compañeros de party no pueden usar portales del otro, en
ninguna dirección) de forma completa y determinística: nunca funcionó, para
nadie, no era una condición de carrera intermitente.

**Fix**: `PortalParty.TryGetPartyId` (`PortalParty.cs`, reescrito por
completo) ahora llama directo a `player.Party` / `party.PartyID.
ToString(CultureInfo.InvariantCulture)`, sin ninguna reflection. Se
eliminó toda la capa de candidatos/caches (`InstancePartyPropertyCandidates`,
`PartyIdMemberCandidates`, `StaticManagerTypeNameCandidates`,
`_instancePropertyCache`, `_staticManagerResolved`/`_staticManagerMethod`/
`_staticManagerTarget`, `TryGetPartyIdFromInstanceProperty`,
`TryGetPartyIdFromStaticManager`, `ResolveStaticManager`,
`FindPropertyInHierarchy`, `ExtractIdFromPartyObject`, `IsSimpleIdType`,
`GetSingletonInstance`, `SafeGetType`) — mucho más simple, correcto, y sin
el costo de reflection (incluyendo el escaneo caro de
`AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetTypes())` que
antes solo corría una vez pero nunca encontraba nada útil de todas formas).

**Nota de diseño sin confirmar (anotar en el juego real)**: no se pudo
confirmar sin el juego real si un jugador que NUNCA tocó el sistema de
party del juego (nunca creó/se unió a una) tiene `EntityPlayer.Party ==
null` (comportamiento esperado/asumido, estándar en juegos con sistema de
party opt-in) o si el motor le asigna automáticamente algún objeto `Party`
por defecto con un `PartyID` compartido/sentinel (ej. `0`) — este segundo
caso sería grave (todos los jugadores "solitarios" compartirían
accidentalmente sus portales entre sí). **Probar**: dos jugadores que
NUNCA se unieron a ninguna party no deben poder usar los portales del otro
(deben seguir tratándose como solitarios independientes).

### 12.3 Causa raíz 3 (IDENTIFICADA, NO CORREGIDA): el registro de un portal nunca se sincroniza al servidor autoritativo

Esta es, con alta probabilidad, la explicación real y completa del síntoma
3 ("el compañero construye su propio portal, funciona algunas veces pero
luego deja de funcionar") y de por qué los síntomas 2/4 **seguirían
ocurriendo incluso después del fix de la sección 12.2** en una topología de
**servidor dedicado real** (proceso separado de todos los clientes) — el
fix de 12.2 es necesario pero no alcanza a resolverlo solo.

**Cadena de razonamiento (confirmada por inspección de código + tipos
reales, no por testing en vivo — ver qué falta confirmar al final)**:

1. `PortalManager.Instance` es un singleton **por proceso**
   (`private static PortalManager _instance`). Un servidor dedicado y cada
   cliente conectado son procesos **distintos**, cada uno con su PROPIA
   instancia en memoria — no hay ningún mecanismo que las sincronice.
2. `XUiPortalTag.Confirm()` (que llama a
   `PortalManager.Instance.RegisterPortal`/`RenamePortal`) es un callback
   de click de una ventana XUi — **solo puede ejecutarse en un cliente**
   (donde se renderiza la UI), nunca en un servidor headless.
   `OpenWindow()` en el mismo archivo ya lo documenta explícitamente:
   solo abre si `player as EntityPlayerLocal` no es null, con un TODO
   propio: *"en un jugador remoto la apertura real debería dispararse via
   NetPackage en su propio cliente"*.
3. **Confirmado por tipos reales**: `EntityPlayerLocal` es la **única**
   subclase de `EntityPlayer` en todo el ensamblado. Un jugador remoto,
   visto desde el servidor (dedicado, o el propio host en un listen
   server), **nunca** es `EntityPlayerLocal` — solo lo es en el proceso
   del cliente que lo controla directamente. Esto significa que
   `Block_OnBlockPlaceBefore_Patch`/`Block_OnBlockActivated_Patch`
   (que llaman a `OpenWindow`) solo logran abrir la ventana de
   nombrado/renombrado cuando ese Postfix/Prefix corre en un proceso
   donde ese jugador específico ES el local — es decir, **en el propio
   cliente del jugador que coloca el bloque**, nunca en el servidor.
4. Confirmado en `API.cs` (comentario de `GameManager_Update_Patch`) que
   el motor de estos hooks de bloque corre tanto en cliente como en
   servidor (arquitectura estándar de predicción de cliente: el cliente
   procesa localmente la colocación para que se sienta instantánea,
   mientras el servidor procesa la misma colocación de forma autoritativa
   por separado — confirmado que existe ese roundtrip real via
   `NetPackageSetBlock`/`NetPackageSetBlockResponse`, ambos presentes en
   el DLL real).
5. Conclusión: `RegisterPortal`/`RenamePortal` **solo se ejecuta en el
   proceso del cliente que coloca/nombra el bloque**, mutando la
   `PortalManager.Instance` de ESE cliente únicamente. El servidor
   (dedicado, o el host en un listen server) nunca se entera — su propia
   `PortalManager.Instance`, la que realmente usa
   `PortalTeleport.Tick()` server-side (el único lugar donde
   `CheckPlayerPortalCollision` puede mover a un jugador de forma
   autoritativa) sigue sin ese portal.

**Por qué esto explica los 5 síntomas exactamente**:

- **Síntoma 1** (el dueño puede usarlo sin problema): quien coloca un
  portal siempre puede usarlo él mismo, sin importar host/dedicado/cliente
  remoto — su propio `PortalTeleport.Tick()` (que corre en TODOS los
  procesos, confirmado en `API.cs`) revisa su PROPIA `PortalManager.
  Instance`, que SÍ tiene registrado lo que él mismo colocó. Es
  autoconsistente localmente, no depende de ningún servidor.
- **Síntomas 2 y 4** (compañeros no pueden usar portales del otro): el
  registro de un portal nunca sale del proceso donde se colocó — ningún
  otro cliente (ni el servidor) tiene forma de enterarse, con o sin el fix
  de party de 12.2. El fix de 12.2 es **necesario** (sin él, ni siquiera
  el caso donde SÍ debería funcionar — mismo proceso — resolvía la key de
  party correctamente) pero **no alcanza** para portales registrados por
  OTRO proceso.
- **Síntoma 3** (el compañero funciona algunas veces, después no): el
  compañero (cliente remoto) SÍ logra nombrar/registrar sus DOS propios
  portales — todo ocurre en su propio proceso, autoconsistente. Su propio
  `Tick()` local encuentra el par y ejecuta `SetPosition` localmente —
  "funciona" (el jugador se ve moverse en su propia pantalla). Pero el
  servidor, que es la autoridad real de posición de ese jugador en un
  servidor dedicado, nunca aprobó ese movimiento (su propia
  `PortalManager.Instance` no tiene ningún registro) — la reconciliación
  de posición/anti-cheat del servidor eventualmente corrige/revierte al
  cliente, y el "teletransporte" deja de sentirse como que funciona.
- **Síntoma 5** (portales perdidos al reconectar en servidor dedicado):
  además de la causa raíz 1 (12.1, ya corregida), `Save()`/`Load()` (via
  `GameIO.GetSaveGameDir()`) persisten a la carpeta de guardado del
  **proceso que llama** — en un servidor dedicado remoto real, esa carpeta
  es distinta por completo entre el cliente y el servidor. Un portal que
  solo vivió en la `PortalManager.Instance` de un cliente nunca se guardó
  en el `portals.dat` del servidor para empezar.

**Qué NO se implementó en esta sesión, y por qué**: una corrección
completa requiere una capa de sincronización cliente→servidor real (un
`NetPackage` custom, o reutilizar el canal de comandos de consola
existente — `NetPackageConsoleCmdClient`/`NetPackageConsoleCmdServer`, que
sí están confirmados en el DLL) para que el cliente **pida** el registro
al servidor en vez de mutar su propia copia local, y que el servidor sea
la única fuente de verdad. No se encontró en `Assembly-CSharp.dll` una
clase base tipo `ConsoleCmdAbstract` para registrar comandos custom (puede
vivir en otro ensamblado no revisado en esta sesión), y registrar un
`NetPackage` custom nuevo en el `NetPackageManager` de 7 Days to Die
históricamente ha sido fecho a un mecanismo interno frágil para mods de
terceros — implementarlo a ciegas, sin poder probarlo contra un servidor
real, arriesga romper la sincronización de red de TODO el mod (radio de
impacto mucho mayor que el bug que se busca arreglar) si algo en el
registro/protocolo queda mal. Se prefirió documentar la causa raíz
completa y confirmada en vez de entregar una implementación de red sin
poder validarla.

**Siguiente paso recomendado** (para quien continúe este trabajo, con
acceso a un servidor real para iterar):

1. Definir un `NetPackage` custom (o investigar si existe una clase base
   de comandos de consola en otro ensamblado del juego) para
   `RegisterPortal`/`RenamePortal`/`UnregisterPortal`.
2. En el cliente, `XUiPortalTag.Confirm()` debe **enviar la solicitud al
   servidor** en vez de llamar a `PortalManager.Instance.RegisterPortal`
   directo — mostrar el HUD solo cuando llegue la respuesta del servidor
   (éxito/tag en uso/etc.), no de forma optimista.
3. En el servidor, procesar el paquete y mutar la ÚNICA
   `PortalManager.Instance` autoritativa (la del proceso servidor).
4. Considerar además restringir la ejecución real de
   `CheckPlayerPortalCollision`/`ExecuteTeleport` (en
   `PortalTeleport.Tick()`) a `ConnectionManager.Instance.IsServer == true`
   — evita que un cliente ejecute una copia local "fantasma" del
   teletransporte que compita con la del servidor. **No se aplicó este
   cambio en esta sesión de forma aislada**: sin el paso 1-3 primero,
   restringirlo a servidor haría que un portal registrado solo
   client-side (como hoy) deje de "funcionar algunas veces" para no
   funcionar NUNCA — un cambio de comportamiento peor sin la sync real que
   lo sostenga.

**Qué falta confirmar en el juego real** para validar esta cadena de
razonamiento (no se pudo ejecutar nada de esto sin el juego):

- [ ] Confirmar con logging temporal que `Block_OnBlockPlaceBefore_Patch`
      efectivamente corre en el cliente de un jugador remoto (no solo en
      el servidor) al colocar un portalBlock — si NO corre ahí, la cadena
      de razonamiento de arriba está incompleta y hay que revisar de
      nuevo desde el paso 3.
- [ ] Confirmar en un servidor dedicado REAL (proceso separado, no listen
      server) que un portal registrado por un cliente remoto nunca
      aparece en el log del servidor (`Portal registrado: ownerKey=...`
      debería aparecer SOLO en el log del cliente que lo coloca, nunca en
      la consola/log del servidor).
- [ ] Confirmar si el síntoma 3 coincide en el tiempo con algún log de
      corrección de posición / anti-cheat del lado servidor (buscar
      "requiresAntiCheat", rechazo de posición, o similar en el log del
      servidor en el momento exacto en que el compañero reporta que "deja
      de funcionar").

## 13. Sincronización cliente-servidor del registro de portales (causa raíz 3, CORREGIDA)

Implementación completa de lo identificado en la sección 12.3. A diferencia
de esa sesión (sin acceso al DLL, solo razonamiento por tipos), esta vez se
**decompiló** `Assembly-CSharp.dll` con `ilspycmd` (instalado localmente
como herramienta de `dotnet tool`) además de usar reflection — cada clase
mencionada abajo se leyó como código C# real generado por el decompilador,
no se adivinó ningún nombre/firma.

### 13.1 Qué se confirmó del sistema de red real (antes de escribir código)

- **`NetPackage`** (clase base real, `abstract`): `read(PooledBinaryReader)`
  y `ProcessPackage(World, GameManager)` son abstractos; `write
  (PooledBinaryWriter)` es virtual con una implementación base que escribe
  el `PackageId` (`ushort`) — **toda subclase debe llamar
  `base.write(_writer)` primero**, exactamente como hace el propio
  `NetPackageSetBlock` (ver más abajo). `PackageDirection` (virtual,
  default `Both`) controla si un paquete es válido `ToServer`/`ToClient`/
  ambos — confirmado en uso real: `NetPackageConsoleCmdServer` override a
  `ToServer`.
- **Registro de paquetes: DINÁMICO, no un array/enum fijo** — este era el
  riesgo real que se quería descartar antes de escribir nada (ver sección
  12.3, "por qué no se implementó a ciegas"). Decompilando
  `NetPackageManager`: su constructor estático llama
  `ReflectionHelpers.FindTypesImplementingBase(typeof(NetPackage), ...)`,
  que a su vez llama `ModManager.GetLoadedAssemblies()` — **la misma API
  que ya usa este mod en `API.cs`** para detectar SCore — e incluye
  cualquier ensamblado de mod cargado, este incluido. Cualquier subclase de
  `NetPackage` definida en `PortalMod.dll` se descubre sola por nombre de
  clase (`knownPackageTypes`, un diccionario `string -> Type`), sin tocar
  ningún array fijo del juego. `NetPackageManager.StartServer()` les asigna
  IDs numéricos en tiempo de ejecución (empezando en 1, después del ID 0
  reservado) y se los manda a cada cliente al conectarse
  (`IdMappingsReceived(string[] _mappings)`) — si un cliente no tiene una
  clase que el servidor sí conoce, el juego lo desconecta con un error
  claro (`"Unknown package type ..."` + `EKickReason.UnknownNetPackage}`),
  nunca corrompe la sesión de nadie más. **Esto confirma que agregar un
  `NetPackage` propio es seguro para un mod de terceros** — no hay ningún
  array fijo que pueda pisarse ni colisionar con otro mod o con una
  actualización del juego.
- **Envío real**: `ConnectionManager.Instance.SendToServer(NetPackage,
  bool _flush)` (cliente → servidor), `ConnectionManager.Instance.
  SendPackage(NetPackage, ...)` (servidor → todos los clientes con login
  terminado — `clientInfo.loginDone`, con filtros opcionales de rango/
  entidad que no se usan aquí), `ClientInfo.SendPackage(NetPackage)`
  (servidor → un cliente puntual).
- **`ConnectionManager.Instance.IsServer`** (`protocolManager.IsServer`) es
  el chequeo de autoridad real usado en TODO este fix. Confirmado
  decompilando `ConnectionManager.IsSinglePlayer`: `IsServer &&
  ClientCount() == 0` — es decir, **singleplayer puro YA es un caso de
  `IsServer == true`** (el juego corre un servidor interno incluso sin
  ningún cliente remoto conectado). `IsServer` es también true en un listen
  server (el propio host) y en un servidor dedicado real. Es `false`
  únicamente en un cliente remoto puro — exactamente la distinción que
  hacía falta, sin tener que manejar los tres casos "verdaderos" por
  separado en ningún lado de este mod.
- **Modelo real usado como referencia de implementación**:
  `NetPackageSetBlock` (coloca/destruye bloques — el mismo tipo de flujo
  cliente→servidor→broadcast que necesitaba este fix) y
  `NetPackageConsoleCmdServer`/`Client` (para el patrón `Setup()` +
  `PackageDirection` explícito). Ambos decompilados enteros antes de
  escribir una sola línea propia.
- **Evento de conexión real usado para el sync inicial**:
  `ModEvents.SPlayerSpawnedInWorldData` (ya enganchado en `API.cs` desde
  antes de esta sesión) trae `ClientInfo` **directo** como campo — no hace
  falta re-resolverlo vía `ConnectionManager.Instance.Clients.
  ForEntityId(...)` (aunque esa API también existe y es la que usa
  `PortalIdentity.cs`, ver sección 12.1).

### 13.2 Diseño de la solución

- **`PortalManager.PortalSyncEntry`** (struct pública anidada, nueva):
  aplana `ownerKey`/`tag`/posición/bioma/estilo — una fila autocontenida
  fácil de mandar por red.
- **`PortalManager.GetSyncSnapshot()`** (nuevo, servidor): construye la
  lista completa de `PortalSyncEntry` a partir de `_portals`/`_biomes`/
  `_styles`.
- **`PortalManager.ApplyFullSync(List<PortalSyncEntry>)`** (nuevo,
  cliente): **reemplaza por completo** `_portals`/`_positionLookup`/
  `_biomes`/`_styles` — nunca fusiona. Un snapshot completo siempre gana
  sobre lo que hubiera antes, así un cliente nunca arrastra una entrada que
  el servidor ya no tiene (por ejemplo un portal destruido mientras ese
  cliente estaba desconectado). Deliberadamente NO toca `_cooldowns`/
  `_originalOwnerSteamId`/`_lastKnownPortalKey`/`_pendingPartyKey` (estado
  de sesión que ya no se usa del lado cliente, ver más abajo) ni marca
  `_dirty` (esto no es un cambio que haya que persistir localmente).
- **Tres `NetPackage` nuevos** (`PortalNetSync.cs`):
  - `NetPackagePortalRequest` (`ToServer`): "quiero nombrar/renombrar el
    portal en esta posición con este tag". Solo manda `tag` + posición —
    **nunca un ownerKey ni un estilo mandado por el cliente**: el servidor
    resuelve el dueño real a partir de `Sender` (la identidad de conexión
    real, no falsificable por el cliente) y el estilo a partir del bloque
    real ya colocado en el mundo, mismo criterio que ya usaba
    `PortalBlockPatch.HandlePortalActivation` para decidir "nombrar" vs
    "renombrar" (re-derivado en el servidor, no confiado del cliente).
  - `NetPackagePortalRequestResult` (`ToClient`, al remitente original):
    el resultado real (`PortalManager.RegisterResult` + tag + si fue
    rename) para que el cliente muestre el mismo mensaje de HUD y
    cierre/mantenga abierta la ventana igual que el camino síncrono de
    antes.
  - `NetPackagePortalSync` (`ToClient`, a uno o a todos): snapshot completo
    (ver arriba). Se manda el registro **entero** en cada cambio (no un
    delta incremental) a propósito — la cantidad de portales de un
    servidor típico es pequeña (decenas, no miles) y esto solo se dispara
    en eventos raros iniciados por un jugador, nunca por tick; el costo
    extra es insignificante comparado con el riesgo de un protocolo de
    deltas mal sincronizado.
- **`PortalNetSync`** (clase estática, orquestación): `SendFullSyncToClient
  (ClientInfo)` (sync inicial) y `BroadcastFullSyncIfServer()` (tras
  cualquier mutación real) — ambos no-op si no corren en el servidor.

### 13.3 Cambios de autoridad (qué corre dónde ahora)

- **`XUiPortalTag.Confirm()`**: si `ConnectionManager.Instance.IsServer`
  (host/dedicado/singleplayer), aplica directo contra `PortalManager.
  Instance` — mismo comportamiento exacto que antes de este fix, sin red
  de por medio. Si NO es servidor (cliente remoto), manda
  `NetPackagePortalRequest` y **espera la respuesta real** antes de
  mostrar cualquier mensaje o cerrar la ventana (nunca optimista) — ver
  `XUiPortalTag.HandleServerResult`, invocado desde
  `NetPackagePortalRequestResult.ProcessPackage`.
- **`Block_OnBlockDestroyedBy_Patch`**: la baja real (`UnregisterPortal`)
  ahora solo ocurre si `ConnectionManager.Instance.IsServer` — igual que la
  colocación, la destrucción de bloque también corre por predicción del
  lado cliente (confirmado indirectamente: el mismo patrón
  `NetPackageSetBlock` maneja colocación Y destrucción, ver
  `BlockChangeInfo`), así que un cliente remoto que dispara este mismo
  Postfix por predicción ya no hace nada — se entera de la baja real por
  el broadcast del servidor.
- **`PortalManager.Load()`/`Save()`**: gateados a `ConnectionManager.
  Instance.IsServer`. Un cliente remoto puro ya no lee ni escribe su
  propio `portals.dat` local — ese archivo nunca fue el mismo que el del
  servidor real en un servidor dedicado remoto, y ahora el cliente recibe
  la verdad por red. Sigue funcionando exactamente igual que antes para
  singleplayer/host (que ya cumplían `IsServer == true`).
- **`PortalTeleport.Tick()`**: `ProcessPendingTeleports` y el loop de
  colisión jugador↔portal (`CheckPlayerPortalCollision`, que incluye el
  chequeo de cambio de party) ahora solo corren si `ConnectionManager.
  Instance.IsServer`. Antes de este fix, un cliente remoto ejecutaba esta
  MISMA lógica con su propia copia local (y desincronizada) de
  `PortalManager`, moviendo al jugador con `SetPosition` sin que el
  servidor real lo aprobara jamás — la causa más probable del síntoma
  "funciona un par de veces y después deja de funcionar" (el servidor
  termina revirtiendo/ignorando una posición que nunca aprobó). Ahora la
  única autoridad real para decidir "esto teletransporta" es siempre el
  servidor.

### 13.4 Problema de compilación encontrado y resuelto

Al compilar contra el `Assembly-CSharp.dll` real: `PooledBinaryWriter`/
`PooledBinaryReader` (las clases reales usadas por `NetPackage.read`/
`write`) agregan sus propias sobrecargas de `Write`/`Read` con
`ReadOnlySpan<T>` (el juego corre sobre un runtime moderno con soporte de
Span) — el `net48` clásico de este proyecto no tiene `System.
ReadOnlySpan<T>` en su `mscorlib` de referencia, y el compilador no podía
resolver el conjunto de sobrecargas de esos tipos en absoluto (error
`CS0518`), ni siquiera para las sobrecargas simples (`int`/`string`/`bool`)
que sí se usan. Se probó agregar el paquete NuGet oficial `System.Memory`
(el polyfill estándar de Microsoft para esto en net48) pero generó un
conflicto de versión real entre `System.Runtime.CompilerServices.Unsafe`
4.0.4.1 (traído por ese paquete) y 6.0.0.0 (el que espera `Assembly-
CSharp.dll`), y el error de compilación persistía. **Fix real, sin
dependencias nuevas**: en `PortalNetSync.cs`, todas las llamadas a
`_reader`/`_writer` pasan primero por una variable local tipada
explícitamente como `System.IO.BinaryReader`/`BinaryWriter` (la clase BASE
real, ya completa en el `mscorlib` de net48, sin ninguna sobrecarga de
Span) en vez de llamar directo sobre el tipo `Pooled...` derivado — mismos
bytes exactos en el wire (son los mismos métodos heredados), el compilador
simplemente ya no necesita resolver las sobrecargas adicionales que este
mod no usa. Confirmado: compila limpio (0 errores, 0 advertencias).

### 13.5 Qué falta confirmar en el juego real

No se pudo probar nada de esto en el entorno de desarrollo (sin el juego).
Verificar específicamente:

- [ ] Un cliente remoto (servidor dedicado real, proceso separado) coloca y
      nombra un portal: debe aparecer `NetPackagePortalRequest` procesado
      en el log del **servidor** (no en el del cliente), y el cliente debe
      recibir el mensaje de HUD correcto (activo/huérfano/tag en uso) sin
      demora perceptible.
- [ ] Un segundo cliente, ya conectado, ve el portal del primero
      aparecer/actualizarse sin necesidad de reconectarse (prueba del
      broadcast tras `RegisterPortal`).
- [ ] Un jugador que se conecta DESPUÉS de que ya existen portales los ve
      todos de entrada (revisar el log por `PortalManager: sincronizacion
      recibida del servidor aplicada (N posiciones)` apenas aparece en el
      mundo).
- [ ] Destruir un portal desde un cliente remoto lo desregistra realmente
      del lado servidor (y el resto de los clientes conectados dejan de
      verlo como registrado).
- [ ] El camino "soy servidor" (host de listen server, o singleplayer)
      sigue funcionando exactamente igual que antes de este fix — sin
      ningún paquete de red de por medio, mismos mensajes, mismo timing.
- [ ] **Caso especial a confirmar**: si el paquete no llega a tiempo o se
      pierde (timeout de red real, no simulable aquí), la ventana de tag
      queda abierta indefinidamente sin respuesta — no hay timeout/reintento
      implementado en `Confirm()`. Si esto resulta molesto en la práctica,
      agregar un timeout del lado cliente (por ejemplo, cerrar la ventana y
      avisar "no se pudo confirmar con el servidor" si no llega respuesta
      en unos segundos).
- [ ] Confirmar que el gateo de `Block_OnBlockDestroyedBy_Patch`/
      `Block_OnBlockPlaceBefore_Patch` a predicción del lado cliente es
      correcto — es decir, que el log de colocación/destrucción SÍ
      aparece también del lado cliente para un jugador remoto (confirmaría
      la cadena de razonamiento completa de la sección 12.3), y que
      gatearlo a `IsServer` en el punto de destrucción no rompe ningún otro
      efecto secundario esperado del lado cliente.

## Cómo reportar un bug

Incluir en el reporte:

1. Paso exacto donde falló el checklist (número de sección + ítem).
2. Contenido del log (F1 → buscar `PortalMod` o `ERR`).
3. Versión del juego y sistema operativo.
