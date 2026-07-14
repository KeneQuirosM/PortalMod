# AUDIT.md — Auditoría de estabilidad y seguridad en multijugador (PortalMod)

Auditoría de todo el código del mod (C# en `Harmony/src/` + XML en `Config/`)
buscando específicamente causas de **crashes** o **pérdida/corrupción de
datos** en multijugador. No se pudo ejecutar el mod en un juego real durante
esta auditoría — todos los hallazgos vienen de lectura estática del código
(el mismo que ya pasó por 60+ commits de pruebas reales en sesiones
anteriores) más razonamiento sobre las APIs ya confirmadas por decompilación
en ese código existente.

Convención de severidad: **Alta** = puede crashear el proceso o corromper/
perder datos de jugadores; **Media** = puede romper una funcionalidad
puntual o dejar estado inconsistente sin crash; **Baja** = mejora de
robustez/higiene sin impacto directo conocido.

Cada hallazgo indica si ya se **corrigió** en este mismo commit, o si quedó
solo **documentado** (por ejemplo porque es inherente a cómo funciona el
mod, no un bug puntual corregible en el código).

---

## 1 — Seguridad en multijugador

### 1.1 [Alta → CORREGIDO] `PortalManager` sin protección contra acceso concurrente

**Problema**: todas las colecciones internas (`_portals`, `_positionLookup`,
`_biomes`, `_styles`, `_cooldowns`) eran `Dictionary<,>` planos, sin ningún
mecanismo de sincronización. `Dictionary<,>` de .NET **no es thread-safe**:
lecturas/escrituras concurrentes desde hilos distintos pueden corromper su
estructura interna (en el peor caso, un `foreach` puede entrar en loop
infinito, o lanzar excepciones aleatorias tipo `NullReferenceException`
dentro del propio `Dictionary`).

No se pudo confirmar con certeza si 7 Days to Die V3.0 procesa RPCs de
colocación/destrucción de bloques exclusivamente en el hilo principal de
Unity, o si algún camino de red en el servidor dedicado puede invocar los
patches de Harmony (y por lo tanto estos métodos) desde otro hilo. Dado que
el costo de un `lock` sin contención es mínimo, se optó por protegerlo de
todas formas en vez de asumir que nunca hay concurrencia.

**Corrección**: se agregó `private readonly object _lock` en
`PortalManager.cs` y se envolvió el cuerpo completo de **todos** los métodos
públicos que leen o escriben esas colecciones (`RegisterPortal`,
`UnregisterPortal`, `RenamePortal`, `TryGetDestination`, `GetTagAt`,
`TryGetPortalRef`, `IsPortalOrphan`, `IsPositionActive`, `IsOnCooldown`,
`SetCooldown`, `GetRemainingCooldown`, `GetBiome`, `GetStyle`, `Save`,
`Load`, `GetAllPortalPositions`) en `lock (_lock) { ... }`. `lock` en C# es
reentrante por hilo, así que las llamadas internas entre estos métodos (ej.
`RenamePortal` → `UnregisterPortal` + `RegisterPortal`, o `RegisterPortal` →
`PortalVisualFX.RefreshBlockState` → `GetStyle`/`GetBiome`) no producen
deadlock.

### 1.2 [Alta → CORREGIDO] `GetAllPortalPositions()` devolvía una vista en vivo, no una copia

**Problema**: el método devolvía `_positionLookup.Keys` directamente — una
vista que sigue apuntando al diccionario real. `PortalVisualFX.AmbientTick()`
y `PortalTeleport` iteran esa colección con `foreach`; si el diccionario se
modifica mientras esa iteración externa está en curso (incluso protegida por
el mismo `lock` en cada método individual — el `lock` de `GetAllPortalPositions()`
ya terminó para cuando el llamador empieza a iterar), se lanza
`InvalidOperationException: Collection was modified`.

**Corrección**: ahora devuelve `new List<Vector3i>(_positionLookup.Keys)` —
una copia (snapshot) tomada dentro del `lock`, segura de enumerar sin
importar qué pase después con el diccionario real.

### 1.3 [Media → DOCUMENTADO, no corregible en el código] `blocks.xml`/`items.xml`/`recipes.xml`/`buffs.xml`/`sounds.xml` deben estar en servidor Y cliente

Estos archivos agregan bloques, items, un buff y un sonido **nuevos** — IDs
nuevos en las tablas compartidas del juego. Esto es inherente a cualquier
mod de este tipo, no un bug puntual: si el servidor no tiene el mod (o tiene
una versión distinta), lo más probable es que los clientes con el mod
instalado sean rechazados/desconectados por mismatch de mods, o en el peor
caso los bloques del portal se vean corruptos/distintos según el lado.
`Config/XUi_InGame/` es la única parte estrictamente client-side. Documentado
en `README.md` (`## Instalación`) con la recomendación explícita de nunca
separar archivos "cliente"/"servidor" a mano.

### 1.4 [Baja → DOCUMENTADO] Los patches de Harmony parchean clases base del juego, no solo `portalBlock`

`Block_OnBlockPlaceBefore_Patch`, `Block_OnBlockActivated_Patch` y
`Block_OnBlockDestroyedBy_Patch` interceptan métodos de la clase base
`Block` — se ejecutan para **todo bloque del juego**, filtrando por
`IsPortalBlock()` al inicio. `BlockPowered_HasBlockActivationCommands_Patch`
y `BlockPowered_OnBlockActivated_Patch` van un paso más allá: interceptan la
clase base `BlockPowered`, que heredan **todos los bloques eléctricos del
juego** (generadores, cercas eléctricas, torretas, luces, etc.), no solo
portalBlock. Esto no es un bug en sí — es el mecanismo correcto para no
tener que crear una subclase C# de bloque — pero significa que cualquier
excepción sin capturar en estos patches tiene un radio de impacto que va
mucho más allá de este mod (ver hallazgo 2.1, ya corregido con try/catch en
todos ellos).

---

## 2 — Manejo de errores

### 2.1 [Alta → CORREGIDO] Ningún patch de Harmony tenía try/catch

**Problema**: ninguno de los métodos `Prefix`/`Postfix` en
`PortalBlockPatch.cs` ni los de `API.cs` (`GameManager_Update_Patch`,
`GameManager_OnApplicationQuit_Patch`) tenía manejo de excepciones. En
Harmony, el código de un `Prefix`/`Postfix` se inyecta (vía IL) dentro del
método original que se está parcheando — una excepción sin capturar ahí se
propaga como si el propio método del juego la hubiera lanzado. El caso más
crítico: `GameManager_Update_Patch.Postfix()` corre una vez por frame dentro
de `GameManager.Update()`, el loop principal de Unity, tanto en cliente como
en servidor dedicado — una excepción sin capturar ahí podía interrumpir el
Update() del juego para ese frame.

**Corrección**: se agregó try/catch (registrando con `API.LogError`) en:

- `API.cs`: `GameManager_Update_Patch.Postfix`, `GameManager_OnApplicationQuit_Patch.Prefix`.
- `PortalBlockPatch.cs`: los 6 patches (`Block_OnBlockPlaceBefore_Patch`,
  `Block_OnBlockActivated_Patch`, `BlockPowered_HasBlockActivationCommands_Patch`,
  `BlockPowered_OnBlockActivated_Patch`, `Block_OnBlockDestroyedBy_Patch`,
  `Block_OnBlockEntityTransformAfterActivated_Patch`). En los `Prefix` que
  devuelven `bool` (controlan si el juego ejecuta su lógica original), el
  fallback en el `catch` es `return true` — dejar que el juego procese
  normalmente, el comportamiento menos sorpresivo ante un fallo interno del mod.
- `PortalTeleport.cs`: `Tick()` ahora aísla `PortalVisualFX.AmbientTick()`,
  `PortalHoverFX.Tick()` y el chequeo de colisión de **cada jugador
  individualmente** en su propio try/catch — antes, una falla revisando al
  jugador N abortaba el chequeo para todos los jugadores siguientes en ese
  mismo frame (se recuperaba solo, al frame siguiente, pero igual dejaba
  medio frame sin cobertura para el resto).
- `PortalVisualFX.cs`: `AmbientTick()` ahora aísla cada posición de portal en
  su propio try/catch — un portal con datos raros ya no le quita el ambient
  tick al resto.
- `XUiPortalTag.cs`: `Confirm()` — es un callback de click de UI, sin ningún
  try/catch "de arriba" que lo protegiera.

Con esto hay dos capas: manejo fino por sub-sistema/jugador/portal (para no
perder trabajo de más de lo necesario) + una red de seguridad final en
`GameManager_Update_Patch` que garantiza que, pase lo que pase, ninguna
excepción de este mod llegue a escapar hacia `GameManager.Update()`.

### 2.2 [Alta → CORREGIDO] `XUiPortalTag.Confirm()` podía lanzar `ArgumentNullException` sin capturar

**Problema**: `_pendingPlayer` es un campo **estático** que se llena al
abrir la ventana (`OpenForNewPortal`/`OpenForRename`) y se lee después,
potencialmente varios segundos más tarde, cuando el jugador confirma. Si el
jugador se desconecta con la ventana todavía abierta (o el objeto Unity
subyacente ya fue destruido — Unity trata los objetos destruidos como
`== null`), `_pendingPlayer` podía quedar `null`. `PortalIdentity.GetSteamId(null)`
ya devolvía `null` de forma segura, pero `PortalManager.RegisterPortal(null, ...)`
terminaba llamando `Dictionary.TryGetValue(null, ...)`, que lanza
`ArgumentNullException` (no capturada, dentro de un callback de click).

**Corrección**: se agregó un guard explícito `if (_pendingPlayer == null)`
al inicio de `Confirm()` que cierra la ventana sin registrar en vez de
seguir adelante. Además, como defensa en profundidad, `PortalManager.
RegisterPortal`/`RenamePortal` (los métodos públicos, ambos llamables desde
otro código) ahora también validan `steamId == null` explícitamente antes
de tocar cualquier diccionario.

### 2.3 [Alta → CORREGIDO] `PortalTeleport` no verificaba si el chunk destino estaba cargado

**Problema**: un portal registrado puede estar en un chunk que el servidor
ya descargó por distancia (streaming de chunks) — `PortalManager.Load()` ya
asume esto como posible (no descarta portales en chunks sin cargar, los
marca `Unknown` en vez de `Missing`). Sin embargo, `PortalTeleport.
ExecuteTeleport` movía al jugador con `player.SetPosition(...)` sin verificar
si el chunk de destino estaba realmente cargado, lo que podía dejar al
jugador cayendo en una zona sin terreno/colisiones generadas todavía.

**Corrección**: `TryTeleport` ahora llama `world.IsChunkAreaLoaded(x, y, z)`
sobre la posición de destino (la misma API ya confirmada y en uso real en
`PortalManager.CheckPortalBlockAt`) antes de ejecutar el viaje; si el chunk
no está cargado, se cancela el viaje y se muestra el mensaje HUD
"Área de destino aún no está cargada" (nueva clave `portalHudDestinationNotLoaded`
en `Config/Localization.csv`, inglés + español).

### 2.4 [Baja → DOCUMENTADO, no corregido] Logging de diagnóstico verboso en rutas calientes

`PortalPower.HasNearbyPower` y `PortalVisualFX.AmbientTick`/
`SpawnParticleServer` tienen `API.Log(...)` explícitamente marcados en el
código como "diagnóstico temporal, quitar una vez confirmado" — corren en
cada ambient tick (~cada 0.6s por portal) y en cada intento de
teletransporte. No es un riesgo de estabilidad, pero ensucia el log y tiene
un costo de I/O pequeño pero no nulo en partidas con muchos portales. No se
tocó en esta auditoría porque son diagnósticos activos dejados a propósito
por sesiones de debugging anteriores — bajarlos de nivel o quitarlos debería
decidirlo quien esté con el problema que los originó, no esta auditoría.

---

## 3 — Compatibilidad cliente/servidor

### 3.1 [Documentado] Qué pasa si el servidor no tiene el mod

| Archivo | Qué agrega | Riesgo si falta en el servidor |
|---|---|---|
| `Config/blocks.xml` | 55 definiciones de bloque (portal × 7 estilos × 6 variantes bioma + legacy) | IDs de bloque nuevos — mismatch de mods, probable desconexión del cliente |
| `Config/items.xml` | 7 items (`portalBlockItem` + 6 variantes de estilo) | IDs de item nuevos — mismatch de mods |
| `Config/recipes.xml` | 7 recetas | Depende de que los items existan (ver arriba); sin ellos, carga vacía/ignorada |
| `Config/buffs.xml` | `buffPortalTravel` | ID de buff nuevo — el cliente podría aplicar un buff que el servidor no reconoce |
| `Config/sounds.xml` | `SoundDataNode "guppyKeyUsed"` | Solo afecta reproducción de audio local; no debería romper sync, pero no se probó sin el mod en el servidor |
| `Config/XUi_InGame/` | Ventanas de UI (`windowPortalTag`) | Puramente client-side — el servidor nunca las renderiza, pero se recomienda igual mantenerlo en el paquete completo |
| `Harmony/PortalMod.dll` | Toda la lógica | Obligatorio en ambos lados — sin el DLL en el servidor, los bloques nuevos existen en la definición XML pero ningún patch de Harmony corre ahí |

**Conclusión**: no existe una forma segura de instalar este mod "solo en
cliente" o "solo en servidor" — debe ser idéntico en todos los lados. Esto
ya quedó documentado explícitamente en `README.md`.

### 3.2 [Documentado] `GameManager.IsDedicatedServer` ya se usa correctamente donde corresponde

`Block_OnBlockEntityTransformAfterActivated_Patch` (activación de partículas
del modelo 3D) y `PortalHoverFX.Tick` (tooltip + texto flotante al apuntar)
ya cortan temprano con `if (GameManager.IsDedicatedServer) return;` — ambas
son features puramente visuales/de cliente (partículas, cámara, texto
flotante) que no tienen sentido ni serían seguras de ejecutar en un servidor
sin cabeza gráfica. Este patrón ya estaba bien aplicado antes de esta
auditoría; se documenta acá como referencia de que es el patrón correcto a
seguir si se agregan más features visuales en el futuro.

---

## 4 — Persistencia

### 4.1 [Alta → CORREGIDO] Escritura de `portals.dat` no era atómica

**Problema**: `Save()` escribía directo sobre `portals.dat` con
`File.WriteAllText`. Si el proceso muere a mitad de esa escritura (crash del
servidor, `kill -9`, corte de energía), el archivo queda truncado/corrupto.

**Corrección**: ahora se escribe primero a `portals.dat.tmp` y recién al
final se reemplaza el archivo real (`File.Replace` si ya existía uno
previo, `File.Move` si es la primera vez) — un crash a mitad de camino deja
el `.tmp` a medio escribir, pero `portals.dat` (la última versión buena
conocida) queda intacto.

### 4.2 [Alta → CORREGIDO] Una sola línea corrupta descartaba los portales de TODOS los jugadores

**Problema**: `Load()` procesaba todas las líneas del archivo dentro de un
único try/catch exterior. Si UNA línea estaba corrupta (por ejemplo un
`int.Parse` fallando sobre un campo numérico truncado, escenario más
probable antes del fix 4.1), la excepción escapaba hasta ese catch exterior
y descartaba de un tirón los portales de **todos** los jugadores ya
procesados en ese mismo `Load()`, no solo la línea mala.

**Corrección**: cada línea ahora se procesa en su propio try/catch interno;
una línea corrupta se loguea (`API.LogWarning`, incluyendo el contenido
crudo de la línea) y se salta, pero el resto del archivo se sigue cargando
con normalidad. Se agregó también un contador `skippedLineCount` al log
final de `Load()`.

### 4.3 [Alta → CORREGIDO] Un tag con tab/newline embebido corrompía el archivo de guardado

**Problema**: el formato de `portals.dat` usa TAB como separador de campo y
NEWLINE como separador de línea. El campo de texto libre que el jugador
escribe (el tag del portal) no se saneaba — un tag con un tab o newline
embebido (posible vía pegar texto en el campo, no solo tipeándolo)
corrompería el archivo la próxima vez que se guardara, mezclando campos de
una línea con la siguiente al recargar.

**Corrección**: se agregó `PortalManager.SanitizeTag()` (reemplaza tab/CR/LF
por espacios y recorta espacios sobrantes), aplicado en el único punto de
entrada real — `RegisterPortal`/`RenamePortal` — así que cubre tanto la UI
como cualquier otro caller futuro.

### 4.4 [Alta → CORREGIDO] Sin autoguardado periódico — ventana de pérdida de datos ante crash duro

**Problema**: el único punto de guardado era `GameManager_OnApplicationQuit_Patch`,
que **nunca se ejecuta en un crash duro** del proceso (`kill -9`, OOM, corte
de energía, "Detener" abrupto de un panel de hosting). En ese escenario se
perdían todos los portales creados/modificados desde el último guardado
exitoso — potencialmente toda una sesión de juego.

**Corrección**: se agregó `PortalManager.MaybeAutoSave()`, llamado desde
`PortalTeleport.Tick()` en cada frame pero auto-throttleado a un intervalo
real de 5 minutos (`AutoSaveIntervalSeconds`); internamente sigue siendo un
no-op barato si no hay cambios pendientes (`_dirty`). Esto acota la ventana
de pérdida a, como máximo, ~5 minutos en vez de "toda la sesión".

### 4.5 [Ya resuelto en sesiones anteriores — verificado, sin cambios] Ruta de guardado atada al mundo/slot activo

`GetSaveFilePath()` ya usa `GameIO.GetSaveGameDir()` (API real confirmada
por decompilación), que resuelve la carpeta específica del mundo/slot de
guardado activo. Esto ya corrige el problema original de "cargar el Mundo B
después del Mundo A restaura los portales de A sobre las coordenadas de B".
Se verificó que sigue siendo así — no se encontró ninguna regresión.

### 4.6 [Media → CORREGIDO] `_cooldowns` no se limpiaba al cambiar de mundo

**Problema**: `PortalManager.Instance` es un singleton de **proceso**, no
de mundo — si un jugador (sin reiniciar el cliente) vuelve al menú principal
y carga otro mundo, `Load()` ya limpiaba `_portals`/`_positionLookup`/
`_biomes`/`_styles`, pero **no** `_cooldowns`. Un cooldown activo del Mundo A
podía seguir aplicando en el Mundo B para el mismo `steamId` (`entityId`).
No es un riesgo de crash/corrupción — a lo sumo el jugador espera unos
segundos de más antes de su primer viaje en el nuevo mundo — pero es estado
que no tenía motivo para sobrevivir al cambio de mundo.

**Corrección**: `Load()` ahora también llama `_cooldowns.Clear()`.

### 4.7 [Documentado, no corregible sin más contexto del usuario] Bloques huérfanos al desinstalar el mod

Si el mod se desinstala con portales todavía colocados en el mundo, esos
IDs de bloque quedan huérfanos en los datos de los chunks (comportamiento
exacto no garantizado, depende de la versión del juego). No es corregible
desde el código del mod una vez que ya no está instalado. Se agregó una
sección `## Antes de desinstalar el mod` en `README.md` recomendando
explícitamente destruir/minar todos los portales antes de quitar el mod.

### 4.8 [Verificado, sin hallazgos] Los datos del mod nunca tocan el save nativo del juego

`portals.dat` es un archivo propio del mod dentro de la carpeta de guardado
del mundo, en un formato de texto plano completamente separado de los
archivos `.ttw`/`.7rg`/región del juego. No hay ningún camino de código en
este mod que escriba sobre archivos del save nativo — un fallo en la
persistencia del mod (o borrar `portals.dat` a mano) no puede corromper el
save del mundo en sí, solo hace que el mod "olvide" dónde estaban los
portales (los bloques físicos en el mundo no se ven afectados).

---

## Resumen de archivos modificados en esta auditoría

- `Harmony/src/PortalManager.cs` — locking, snapshot seguro en
  `GetAllPortalPositions`, escritura atómica, tolerancia a líneas
  corruptas, saneo de tags, autoguardado periódico, limpieza de cooldowns
  al cargar mundo, guards de `steamId` null.
- `Harmony/src/API.cs` — try/catch en los dos patches de `GameManager`.
- `Harmony/src/PortalTeleport.cs` — try/catch por sub-sistema y por
  jugador, autoguardado, chequeo de chunk cargado antes de teletransportar.
- `Harmony/src/PortalVisualFX.cs` — try/catch por portal en `AmbientTick`.
- `Harmony/src/PortalBlockPatch.cs` — try/catch en los 6 patches de Harmony.
- `Harmony/src/XUiPortalTag.cs` — guard de `_pendingPlayer` null + try/catch
  en `Confirm()`.
- `Harmony/src/PortalUtils.cs` — nuevo mensaje HUD
  `ShowDestinationNotLoadedMessage`.
- `Config/Localization.csv` — nueva clave `portalHudDestinationNotLoaded`
  (inglés + español).
- `README.md` — secciones nuevas `## Persistencia` y
  `## Antes de desinstalar el mod`, advertencia explícita de instalación
  idéntica en cliente/servidor.

No se tocó la lógica de vinculación por tag, el sistema de bioma/estilo, el
requisito de electricidad, ni ninguna firma de API ya confirmada por
decompilación en sesiones anteriores — todos los cambios son aditivos
(try/catch, locks, saneo de datos) o correcciones acotadas a los puntos
descritos arriba.
