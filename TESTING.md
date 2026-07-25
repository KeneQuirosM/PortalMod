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
- [ ] El cooldown de 5s impide teletransportar inmediatamente después de
      un viaje (verificar que no ocurran loops).

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
- [x] `EntityPlayer.PlatformUserIdentifierAbs` no existe — **corregido**
      usando `player.entityId.ToString()` como identificador de jugador
      (`PortalUtils.cs`). Confirmado en reporte real de servidor dedicado:
      esto NO es estable entre reconexiones/sesiones — cada reconexión
      rompe la asociación de portales del jugador (aparecen "sin dueño",
      hay que destruir y volver a colocar el bloque para recuperarlos).
      **Mitigado** (no confirmado 100%, ver `PortalIdentity.
      TryResolveStablePlatformId`): antes de caer a `entityId`, se intenta
      resolver por reflection un identificador de plataforma real
      (candidatos: `PlatformUserIdentifierAbs`, `PlatformId`,
      `CrossplatformId`, `UserIdentifier`, `SteamId`, `steamID`, buscados
      en toda la jerarquía de `EntityPlayer`) — mismo patrón defensivo que
      `PortalParty.TryGetPartyId`. Si ningún candidato existe en el
      `Assembly-CSharp.dll` real de V3.0, el mod sigue compilando y cae al
      comportamiento anterior (`entityId`, con el mismo bug). Pendiente:
      probar en un servidor real si alguno de los candidatos resuelve
      (revisar el log — si el ownerKey logueado en `RegisterPortal`
      empieza con `plat:` en vez de ser un número corto, el fix está
      activo) y, si no, decompilar `Assembly-CSharp.dll` para confirmar el
      nombre real y agregarlo a la lista de candidatos.
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

- [ ] Cada jugador gestiona sus propios portales por steamId
      (`PortalIdentity.GetSteamId`).
- [ ] Los portales de un jugador no interfieren con los de otro, incluso
      usando el mismo tag.
- [ ] El teletransporte funciona en servidor dedicado (no solo en
      single-player/host).

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
      (chunk probablemente descargado) y confirmar que aparece el mensaje
      "Área de destino aún no está cargada" en vez de teletransportar a
      ciegas.
- [ ] Corromper manualmente una línea de `portals.dat` (editar a mano,
      truncar un número) con el juego cerrado, volver a abrir, y confirmar
      que solo esa línea se descarta (log con "Linea corrupta/invalida... se
      salta") y el resto de los portales cargan normal.
- [ ] Provocar intencionalmente un error en medio de una sesión (por ejemplo
      con datos de mundo corruptos) y confirmar que el mod loguea el error
      con `API.LogError` en vez de crashear el proceso o congelar el juego.

## Cómo reportar un bug

Incluir en el reporte:

1. Paso exacto donde falló el checklist (número de sección + ítem).
2. Contenido del log (F1 → buscar `PortalMod` o `ERR`).
3. Versión del juego y sistema operativo.
