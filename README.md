# Portal Mod (PortalMod)

Sistema de portales de teletransporte bidireccional por tag compartida para
**7 Days to Die V3.0 "Dead Hot Summer"**, con la misma mecánica conceptual que
los portales de *Valheim*: dos portales colocados con exactamente el mismo
nombre (tag) se enlazan automáticamente entre sí. No existe jerarquía
madre/hijo — cualquier portal es válido como origen o destino.

## Requisitos

- 7 Days to Die **V3.0** ("Dead Hot Summer") o superior.
- **EAC (Easy Anti-Cheat) desactivado** — obligatorio para cualquier mod que
  incluya una DLL de Harmony. Se desactiva desde el launcher del juego, en la
  pestaña de configuración, antes de iniciar partida.
- Para compilar el DLL: Visual Studio 2022 o JetBrains Rider, con soporte de
  **.NET Framework 4.8**.

## Dependencias

**Dependencia opcional: [0-SCore v3.0.x](https://www.nexusmods.com/7daystodie/mods/6176)**

Sin SCore el mod funciona pero sin partículas animadas en los portales. Con
SCore instalado las partículas se cargan automáticamente.

Esta dependencia se declara en `ModInfo.xml` (`<Dependencies>`) como
referencia informativa, pero **no es aplicada por el propio juego**: se
confirmó decompilando `Mod.parseModInfoV2` (el parser real del formato
`ModInfo.xml` que usa este mod) contra el `Assembly-CSharp.dll` de V3.0 que
7 Days to Die **no tiene ningún mecanismo de dependencias entre mods** — no
bloquea la carga, no reordena mods, no muestra advertencias por
dependencias faltantes. La detección real y funcional ocurre en tiempo de
ejecución: `API.InitMod` verifica `ModManager.ModLoaded("0-SCore")` (API
real confirmada por decompilación) y, si SCore está presente, desactiva
automáticamente el filtro de log de partículas (`LogFilterPatch.cs`) — ver
sección *Notas técnicas* más abajo.

## Instalación (usuario final)

1. Descarga o compila el mod (ver sección *Compilación* si necesitas generar
   `PortalMod.dll`).
2. Copia la carpeta completa `PortalMod/` dentro de la carpeta `Mods/` de tu
   instalación de 7 Days to Die:
   - Windows: `<ruta de instalación>\Mods\PortalMod\`
   - Linux (servidor dedicado): `<ruta de instalación>/Mods/PortalMod/`
3. Verifica que la carpeta `Mods/PortalMod/` contenga al menos:
   `ModInfo.xml`, `Config/` (incluye `Config/XUi_InGame/`) y `PortalMod.dll`
   (generado al compilar).
4. Desactiva EAC y arranca el juego / servidor. Si el mod cargó
   correctamente, verás en la consola (F1 o log del servidor) líneas con el
   prefijo `[PortalMod]`.

**IMPORTANTE — servidor dedicado**: `Config/blocks.xml`, `items.xml`,
`recipes.xml`, `buffs.xml` y `sounds.xml` agregan bloques/items/buffs/sonidos
NUEVOS al juego (nuevos IDs en las tablas compartidas del juego). Estos
archivos (y el DLL) **deben instalarse idénticos en el servidor Y en cada
cliente que se conecte** — no es opcional ni algo que se pueda tener solo en
un lado. Si el mod falta o difiere en el servidor, los clientes con el mod
instalado probablemente sean rechazados/desconectados al intentar conectarse
(mismatch de mods/checksums); si de alguna forma logran conectar igual, es
posible que los bloques del portal se corrompan o se vean como bloques
random del lado sin el mod. `Config/XUi_InGame/` es la única parte
puramente client-side (define ventanas de UI), pero de todas formas se
recomienda copiar la carpeta `PortalMod/` completa e idéntica a todos
lados — no separar archivos "cliente" de "servidor" a mano.

## Compilación del DLL (Visual Studio 2022 / Rider)

El proyecto está en `Harmony/PortalMod.csproj`. Ese archivo está en
`.gitignore` a propósito — cada persona lo personaliza con la ruta local de
su instalación del juego, y no queremos que `git pull` se la sobreescriba
con la de otra persona. **Copia `PortalMod.csproj.template` a
`PortalMod.csproj` y edita la ruta del juego antes de compilar**:

```
cp Harmony/PortalMod.csproj.template Harmony/PortalMod.csproj
```

1. Copia `PortalMod.csproj.template` a `PortalMod.csproj` (comando arriba).
2. Localiza la carpeta `Managed` de tu instalación del juego:
   - Cliente Windows: `<instalación>\7DaysToDie_Data\Managed\`
   - Servidor dedicado: `<instalación>\7DaysToDieServer_Data\Managed\`
3. **Publiciza `Assembly-CSharp.dll`** (obligatorio: el proyecto necesita
   acceder a miembros `internal`/`private` del juego para los patches de
   Harmony). Herramientas recomendadas:
   - [AssemblyPublicizer](https://github.com/CabbageCrow/AssemblyPublicizer) (standalone)
   - El publicizer incluido en BepInEx / `Il2CppAssemblyUnhollower` no aplica
     aquí porque 7D2D V3.0 sigue usando Mono, no IL2CPP — usa la versión
     "Publicizer for Mono" del enlace anterior.
   - Copia el `Assembly-CSharp_publicized.dll` resultante a la carpeta
     `Managed` (o renómbralo a `Assembly-CSharp.dll` en una copia aparte que
     uses solo para compilar, **nunca reemplaces el original del juego**).
4. Define la variable de entorno `SEVENDAYS_INSTALL_DIR` apuntando a la raíz
   de tu instalación (recomendado, evita tocar el `.csproj`):
   - Windows (PowerShell): `setx SEVENDAYS_INSTALL_DIR "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"`
   - Linux: `export SEVENDAYS_INSTALL_DIR="$HOME/.steam/steam/steamapps/common/7 Days To Die"`
   - Alternativamente edita directamente `<Game7DaysToDiePath>` dentro de
     tu copia local de `Harmony/PortalMod.csproj` (no el `.template`).
5. Abre `Harmony/PortalMod.csproj` en Visual Studio 2022 o Rider y compila en
   `Release`. El `.csproj` ya está configurado para que el DLL resultante
   (`PortalMod.dll`) se copie automáticamente a la raíz de `PortalMod/`
   (junto a `ModInfo.xml`), lista para distribuir/instalar.
6. Si tu IDE se queja de referencias no encontradas, confirma que
   `Assembly-CSharp.dll`, `Assembly-CSharp-firstpass.dll`,
   `UnityEngine.CoreModule.dll` y `0Harmony.dll` existen dentro de la carpeta
   `Managed` detectada.

## Publicación de releases (Nexus Mods)

`.github/workflows/nexus-release.yml` se dispara automáticamente al
**publicar un Release de GitHub** (no en drafts/pre-releases): descarga el
`.zip`/`.rar` adjunto al release, intenta subirlo a la página del mod en
Nexus Mods (`7daystodie`, mod
[11298](https://www.nexusmods.com/7daystodie/mods/11298)) usando la [Upload
API oficial de Nexus Mods](https://www.nexusmods.com/news/15454) (en open
beta), y manda un aviso a Discord con el link del release — sea que la
subida a Nexus haya funcionado, fallado, o todavía no esté configurada.

**Por qué hay un aviso de Discord además de la subida automática**: la
Upload API de Nexus Mods es de acceso restringido (beta orientada a
"Verified Mod Authors") y **no puede crear una página de mod ni un primer
archivo desde cero** — solo agrega una versión nueva a un archivo que ya
subiste una vez a mano desde la web. El detalle completo de esta
limitación, y de qué hacer si la subida automática todavía no está
disponible, está documentado en los comentarios de cabecera del propio
workflow.

**Configuración necesaria** (Settings → Secrets and variables → Actions):

| Tipo     | Nombre                | Notas                                                                                                    |
|----------|------------------------|-----------------------------------------------------------------------------------------------------------|
| Secret   | `NEXUS_API_KEY`        | API key personal de tu cuenta de Nexus Mods.                                                              |
| Variable | `NEXUS_FILE_ID`        | ID del "file group" ya creado a mano en la pestaña Files del mod (ver comentarios del workflow) — **sin esto, el workflow salta la subida a Nexus y solo avisa por Discord**. |
| Secret   | `DISCORD_WEBHOOK_URL`  | Opcional. Sin esto, el workflow no manda ningún aviso externo (solo queda el resumen en la pestaña Actions). |

## Cómo usar el mod (in-game)

1. Craftea uno de los **6 estilos de portal** en una **mesa de trabajo**
   (workbench), cada uno con su propio modelo 3D: `Platform Portal`
   (`portalBlock_platformItem`), `Grid Portal` (`portalBlock_gridItem`),
   `Claw Portal` (`portalBlock_clawsItem`), `Cylinder Portal`
   (`portalBlock_cylinderItem`), `Wings Portal` (`portalBlock_wingsItem`) o
   `Arch Portal` (`portalBlock_archItem`) — o el `Teleport Portal`
   (`portalBlockItem`) original. Los 7 (los 6 estilos + el original) usan
   la MISMA receta: 1500× Chatarra de Hierro, 450× Hierro Forjado, 125×
   Piezas Eléctricas, 20× Cinta Adhesiva, 60 segundos. El estilo elegido
   queda fijo para ese portal — no se puede cambiar después
   sin romperlo y volver a colocarlo.
2. Coloca el bloque donde quieras el primer portal. Al colocarlo se abrirá
   automáticamente una ventana pidiendo un **tag** (nombre), por ejemplo
   `"Westland"`. Escríbelo y pulsa **Confirmar**.
3. Ve a otra ubicación (tu base, otro punto del mapa) y coloca un segundo
   portal con **exactamente el mismo tag** (`"Westland"`).
4. En cuanto el segundo portal queda registrado, ambos portales quedan
   **vinculados bidireccionalmente**: entrar a cualquiera de los dos te
   teletransporta al otro.
5. Puedes tener tantos pares de portales como quieras, siempre que cada par
   use un tag distinto (`"Westland"`, `"Base"`, `"Mina-Norte"`, etc.). Un
   tercer portal con un tag ya usado por dos portales **no se puede
   registrar** — el HUD mostrará un mensaje de error.
6. Si solo existe un portal con un tag dado (el par todavía no fue
   colocado), entrar a él mostrará **"Destino no encontrado"** y no
   ocurrirá teletransporte.
7. Interactúa (tecla **E**) con un portal ya colocado para **renombrarlo**.
   Si le asignas un nuevo tag, el portal anterior (su antiguo par, si lo
   tenía) queda huérfano automáticamente.
8. Tras cada viaje hay un **cooldown** (5 segundos por defecto) antes de
   poder volver a activar un portal, para evitar loops infinitos de
   teletransporte. Configurable por servidor entre 0 y 30s — ver
   `Config/PortalModConfig.xml` (`TeleportCooldownSeconds`).
9. Al salir de un portal se aplica brevemente el buff `buffPortalTravel`
   (2 segundos), que congela el movimiento y dispara un efecto de
   partículas/sonido de llegada.
10. El **color** de un portal vinculado depende del **bioma** donde lo
    colocaste (nieve, yermo/wasteland, bosque quemado, bosque de pinos,
    desierto — cualquier otro bioma usa el color "default"). El modelo 3D
    es el del ESTILO que craftaste (punto 1); el bioma solo cambia el
    tinte de color sobre ese modelo. Se detecta y se aplica una sola vez al
    vincularse el par y no cambia después (ni siquiera si pierde la
    energía — ver punto 11).
11. Un portal vinculado **necesita estar cableado a una fuente de energía
    encendida** (generador, panel solar, banco de baterías) para
    teletransportar — usa la **herramienta de cableado** normal del juego,
    igual que con cualquier interruptor o trampa eléctrica. Sin conexión (o
    con la fuente apagada), entrar a él muestra **"Portal sin energía —
    conecta un generador"** y no ocurre el viaje. **Importante**: cablea el
    portal DESPUÉS de vincular el par (colocar ambos portales con el mismo
    tag) — cablear antes de vincular pierde el cable, porque vincular
    cambia el modelo del bloque al color del bioma, lo que reinicia su
    conexión eléctrica.
12. Mientras tengas cualquier item de portal en la mano, listo para colocar,
    verás un **ghost semitransparente** del marco (1x2x1) en la posición
    donde va a quedar, con la misma rotación que tendría el portal real si
    lo colocás en ese instante — ver *Feature: modo fantasma al colocar*
    más abajo.
13. Al colocar un portal, su rotación queda fija según hacia dónde estabas
    mirando en ese momento (no siempre la misma orientación), y el modelo
    muestra una pequeña flecha/chevron indicando por dónde vas a **salir**
    al usarlo — ver *Feature: rotación real + indicador de salida* más
    abajo.

## Feature: modo fantasma al colocar (ghost de colocación)

Mientras el jugador tiene equipado alguno de los 7 items que colocan un
`portalBlock` (el original o cualquiera de los 6 estilos), aparece un
**preview semitransparente** del marco del portal en la posición apuntada
por la mira, con la misma rotación (ver siguiente sección) que tendría el
portal real si se coloca en ese instante — igual idea que el preview nativo
del juego para el resto de los bloques, para poder ver de antemano cómo va a
quedar orientado antes de confirmar la colocación.

**Cómo está implementado** (`PortalPlacementGhost.cs`): a diferencia del
resto de los patches de este mod (que apuntan a métodos del motor ya
confirmados por un error real de carga de Harmony — ver `PortalBlockPatch.cs`),
**no se enganchó al sistema nativo de preview de colocación** del juego: no
se pudo confirmar contra el `Assembly-CSharp.dll` real qué método corre ese
preview cuadro a cuadro, y adivinar la firma de un Harmony patch nuevo
arriesga que **todo el mod** falle al cargar si no coincide exactamente (ver
el mismo riesgo, ya vivido varias veces en este mod, documentado en
`PortalBlockPatch.cs`). En su lugar, el ghost es un objeto propio, construido
enteramente con primitivas de Unity (`GameObject.CreatePrimitive`, sin
depender de ningún AssetBundle/prefab del mod) que sigue el mismo raycast de
mira ya confirmado y usado por `PortalHoverFX` (`EntityPlayerLocal.HitInfo`).

**Limitaciones conocidas de esta implementación** (pendientes de validar en
el juego real, ver `TESTING.md`):

- Es una **caja traslucida** del tamaño del marco (1 ancho x 2 alto x 1
  profundo), no un clon del modelo 3D real de cada estilo
  (platform/grid/claws/cylinder/wings/arch) — instanciar el prefab real de
  cada `.unity3d` como preview antes de la colocación requeriría confirmar
  una API de resolución de prefab que no se pudo verificar sin el DLL real.
- La celda exacta donde se muestra el ghost usa directamente
  `EntityPlayerLocal.HitInfo.hit.blockPos` (la celda que la mira está
  señalando) en vez de reproducir el desplazamiento exacto hacia la
  cara/normal de colocación que usa el sistema nativo — puede diferir en 1
  bloque de la posición real donde el juego terminaría colocando el portal.
- La detección de "qué item tiene equipado el jugador" es *best-effort* por
  reflection (mismo patrón defensivo que `PortalIdentity`/`PortalParty`: si
  ningún candidato de nombre resuelve contra el `Assembly-CSharp.dll` real
  de V3.0, el ghost simplemente no se muestra nunca — el mod sigue
  funcionando exactamente igual que antes de esta Feature).

## Feature: rotación real del portal + indicador de salida

Antes de este cambio, `portalBlock` se colocaba siempre con la rotación por
defecto del motor, sin importar hacia dónde miraba el jugador. Ahora:

- **Rotación real al colocar**: `Block_OnBlockPlaceBefore_Patch` (ver
  `PortalBlockPatch.cs`) calcula, a partir de hacia dónde miraba el jugador
  en el instante de la colocación (`EntityAlive.rotation.y`, redondeado al
  cardinal más cercano — ver `PortalOrientation.ComputeRotationFromPlayerFacing`),
  y reescribe el campo `rotation` del `BlockValue` ya colocado
  (`PortalOrientation.ApplyPlayerFacingRotation`), preservando el resto del
  `BlockValue` sin tocarlo. Como el "type" (ID de bloque) no cambia, esto NO
  dispara `Block.OnBlockRemoved` (el mismo gate ya documentado en
  `PortalVisualFX.cs` para el swap de bioma/estilo), así que no corta ningún
  cableado eléctrico.
- **Indicador de salida (flecha)**: al activarse visualmente el modelo del
  portal (mismo hook que ya usa el mod para reproducir las partículas
  embebidas — ver `Block_OnBlockEntityTransformAfterActivated_Patch`), se
  agrega un pequeño chevron (`>`), construido con primitivas de Unity, que
  apunta hacia el lado "frente" del portal — ver `PortalExitIndicator.cs`.
- **Salida al frente, no adentro del bloque**: `PortalTeleport.FindLandingBlockPos`
  ahora intenta aterrizar al jugador en la celda **al frente** del portal
  destino (lado opuesto al que se "entra", misma convención de rotación de
  arriba) en vez de siempre adentro del propio marco — validando primero que
  esa celda pase `World.CanPlayersSpawnAtPos` (nunca se asume "libre" a
  ciegas) y cayendo al comportamiento anterior (adentro del marco) si no.
  Esto es especialmente relevante para estilos no planos como
  `portalBlock_cylinder`, donde aparecer adentro del propio marco podía
  dejar al jugador chocando/incrustado contra el modelo al salir.

**ADVERTENCIA DE CONFIANZA** (ver comentario completo en `PortalOrientation.cs`):
no se pudo confirmar contra el `Assembly-CSharp.dll` real qué mapeo interno
usa el motor entre `BlockValue.rotation` (0-3) y la orientación visual real
que le aplica a un bloque `Shape="ModelEntity"` como `portalBlock`.
`PortalOrientation.cs` define su **propia** convención (rotación 0=Norte,
1=Este, 2=Sur, 3=Oeste, sentido horario), usada de forma **consistente** en
todo el mod (colocación, indicador, aterrizaje) — esto garantiza que el
portal siempre gira junto con el jugador que lo coloca, pase lo que pase.
Lo único que puede quedar desalineado si esta convención no coincide con la
real del motor es el sentido visual exacto (por ejemplo, que la flecha o el
punto de aterrizaje terminen apuntando 90/180 grados distinto de lo que el
modelo 3D muestra). Si al probar en el juego real esto no coincide, el
ajuste es **únicamente** en `PortalOrientation.cs` (`ForwardOffset`/
`ToQuaternion`) — ver `TESTING.md` para el procedimiento de calibración.

## Multijugador

### Portales por party/grupo

Los portales se comparten entre **todos los miembros de la misma party**. Un
jugador que no está en ninguna party sigue teniendo un conjunto de portales
puramente personal, exactamente igual que antes de esta funcionalidad.

- **Jugador solitario (sin party):** sus portales quedan indexados por su
  identificador de plataforma personal ("steamId") — solo él puede usarlos.
- **Miembro de una party:** sus portales (y los de cualquier otro miembro)
  quedan indexados por el ID de la party, no por steamId individual — **todos
  los miembros pueden usar los portales de todos los demás miembros**, sin
  importar quién los colocó.
- **Parties distintas (o un solitario vs. una party) nunca comparten
  portales entre sí**, ni por accidente ni por diseño: la clave interna
  ("ownerKey") de una party y la de un jugador solitario nunca coinciden.

**Migración automática al entrar/salir de una party:**

- Si colocaste portales estando solo y **luego te unís a una party**, esos
  portales se migran automáticamente al grupo — pasan a estar disponibles
  para todos los miembros.
- Si estás en una party y **te vas** (o te expulsan, o la party se
  disuelve), recuperás como personales **únicamente los portales que vos
  colocaste originalmente** — los portales que colocaron tus excompañeros de
  party se quedan con la party, no te siguen.
- Esta migración se detecta por sondeo periódico (no por un evento de
  "unirse/salir de party" — ver *Limitaciones conocidas* más abajo sobre por
  qué), así que puede haber un retraso de hasta unos segundos entre el
  cambio real de party y la migración de tus portales.

**El cooldown de 5 segundos post-teletransporte (regla anti-loop) sigue
siendo siempre individual**, incluso dentro de una party: cada jugador tiene
su propio cooldown independiente, viajar vos no bloquea a tus compañeros de
party.

## Persistencia

Los portales de cada jugador se guardan en un archivo de texto plano
(`portals.dat`) dentro de la carpeta de guardado del mundo activo
(`GameIO.GetSaveGameDir()`) — un archivo por mundo/slot de guardado, no
compartido entre mundos distintos. Se guarda automáticamente:

- Cada ~5 minutos si hubo cambios pendientes (autoguardado periódico).
- Al cerrar el juego/servidor normalmente (`OnApplicationQuit`).

La escritura es atómica (se escribe primero a un archivo temporal y recién
al final se reemplaza el archivo real), para que un crash a mitad de la
escritura no deje `portals.dat` corrupto. Aun así, ningún guardado cubre un
crash duro del proceso (`kill -9`, corte de energía) que ocurra **entre**
autoguardados — en ese caso se pierden los cambios de, como máximo, los
últimos ~5 minutos.

## Antes de desinstalar el mod

Como cualquier mod que agrega bloques nuevos, **destruye/mina todos los
portales que hayas colocado antes de quitar PortalMod** de tus mods. Si
desinstalas el mod con portales todavía en el mundo, esos IDs de bloque
quedan huérfanos en los datos de los chunks — el comportamiento exacto
depende de la versión del juego (normalmente se ven como aire/bloque
faltante, pero no está garantizado que sea así en todas las versiones), y no
es algo que este mod pueda arreglar después del hecho una vez que ya no está
instalado. Los datos propios del mod (`portals.dat`, ver sección
*Persistencia* más arriba) no tocan el save nativo del juego y se pueden
borrar sin riesgo — el problema real es únicamente los bloques ya colocados
en el mundo.

## Notas técnicas — específicas de V3.0

- **XUi_InGame, no `XUi`**: la carpeta de interfaces de este mod vive en
  `Config/XUi_InGame/` (reemplaza a la antigua `XUi/` de builds pre-V1.0).
  Los parches de mod para CUALQUIER archivo, incluidos los de XUi, siempre
  se buscan bajo `Config/` — no existe una carpeta `UIFrames/` en la
  búsqueda de patches del juego (confirmado decompilando
  `XmlPatcher.LoadAndPatchConfig`).
- **`templates.xml`, no `controls.xml`**: si necesitas definir un control XUi
  totalmente nuevo (no solo reutilizar los existentes), debe declararse en
  `templates.xml`, no en el `controls.xml` de builds pre-A20.
- **Atributo `visible`**: la visibilidad condicional de ventanas/controles se
  maneja con `visible="{binding}"`, ya no con `force_hide`.
- **Assembly-CSharp publicized**: ver sección de compilación arriba.
- **ModInfo.xml formato V2**: obligatorio desde V1.0; este mod ya lo usa
  (raíz `<xml>` plana, sin envoltorio `<ModInfo>` — ese envoltorio activa el
  parser V1 viejo en su lugar, confirmado en `Mod.LoadDefinitionFromFolder`).
  El parser real del formato V2 (`Mod.parseModInfoV2`, confirmado por
  decompilación) solo lee `Name`/`Version`/`DisplayName`/`Description`/
  `Author`/`Website`/`SkipWithAntiCheat` — cualquier otro elemento (como
  `GameVersion` o `Dependencies`, ambos presentes en este `ModInfo.xml`) se
  ignora en silencio, sin error y sin efecto funcional. No existe un
  mecanismo de dependencias entre mods en el loader de V3.0 — ver sección
  *Dependencias* más arriba.
- **Detección de mods opcionales**: `ModManager.ModLoaded(string _modName)`
  (estático, real, confirmado por decompilación) verifica si un mod está
  cargado por nombre — usado en `API.InitMod` para detectar `0-SCore` y
  desactivar el filtro de log de partículas cuando está presente (ver
  `LogFilterPatch.cs`).
- **Configs vía XPath**: todos los archivos en `Config/` usan `<configs>`
  como raíz con comandos `<append xpath="...">` para inyectarse sobre los
  XML base del juego sin reemplazarlos.
- **Bioma real**: `World.GetBiome(int x, int z)` (instancia, no estático)
  devuelve un `BiomeDefinition` cuyo campo público `m_sBiomeName` es el
  nombre real usado en `Data/Config/biomes.xml` ("snow", "wasteland",
  "burnt_forest", "pine_forest", "desert", "underwater"). No existe un
  bioma "ciudad" — las ciudades son POIs sobre un bioma normal.
- **Bloque eléctrico real (`Class="Powered"`)**: no existe una property XML
  tipo "TileEntityClass"/"PowerItemType" para hacer que un bloque acepte
  cableado (0 coincidencias en `blocks.xml` vanilla) — se activa siendo una
  instancia de la clase C# `BlockPowered` (o subclase), seleccionada vía
  `<property name="Class" value="Powered" />` (igual que
  `electricwirerelay` vanilla). Crea un `TileEntityPoweredBlock` real
  (`PowerItemType=Consumer` por defecto), conectable con la herramienta de
  cableado como cualquier bloque eléctrico del juego.
  `PortalPower.HasNearbyPower` lee `TileEntityPowered.IsPowered` directo de
  ese TileEntity.
- **Swap de bloque = cable cortado**: decompilando `Chunk.SetBlock` se
  confirmó que cualquier cambio de "type" (ID de bloque) en una posición
  dispara `Block.OnBlockRemoved`, y `BlockPowered.OnBlockRemoved` desconecta
  explícitamente el `PowerItem`/cable de esa posición. Por eso el color por
  bioma (que cambia el bloque) solo se aplica UNA VEZ al vincularse el par
  — nunca en un tick periódico ni según el estado de energía — y por eso
  cablear un portal ANTES de vincularlo pierde el cable en cuanto se
  vincula.

## Estructura del proyecto

```
PortalMod/
├── .gitignore
├── ModInfo.xml
├── README.md
├── Config/
│   ├── blocks.xml          portalBlock (legacy) + 6 estilos (portalBlock_platform/grid/claws/cylinder/wings/arch)
│   ├── items.xml           portalBlockItem (legacy) + 6 items de estilo (sufijo "Item")
│   ├── recipes.xml         Receta de workbench (7: la legacy + 1 por estilo)
│   ├── buffs.xml           buffPortalTravel
│   ├── sounds.xml          SoundDataNode "guppyKeyUsed" (sonido de activacion)
│   ├── Localization.csv    Strings en english / spanish (nombre real esperado por el juego)
│   ├── PortalModConfig.xml Config ajustable por el servidor (cooldown, espera de chunk) — solo lo lee el mod, no el juego
│   └── XUi_InGame/
│       ├── windows.xml     Ventana popup "Nombrar portal" (windowPortalTag)
│       └── xui.xml         window_group que registra windowPortalTag
├── Harmony/
│   ├── PortalMod.csproj.template   Plantilla versionada (copiar, ver abajo)
│   ├── PortalMod.csproj            Copia local, en .gitignore, NO versionado
│   └── src/
│       ├── API.cs              Punto de entrada IModApi
│       ├── PortalManager.cs    Registro/vinculacion/cooldown/persistencia/estilo/bioma
│       ├── PortalConfig.cs     Config ajustable por servidor (cooldown, espera de chunk) leida de Config/PortalModConfig.xml
│       ├── PortalTeleport.cs   Deteccion de colision y teletransporte
│       ├── PortalBlockPatch.cs Harmony patches (colocar/activar/destruir)
│       ├── PortalVisualFX.cs   Luz/particulas por estado + rafagas de teletransporte
│       ├── PortalHoverFX.cs    Tooltip + texto flotante al apuntar a un portal (mira)
│       ├── PortalOrientation.cs   Rotacion real: convencion compartida colocacion/salida/indicador
│       ├── PortalExitIndicator.cs Flecha/chevron que marca por donde sale el jugador
│       ├── PortalPlacementGhost.cs Ghost/preview semitransparente al tener un portal en mano
│       ├── PortalBiomes.cs     Mapeo estilo+bioma -> variante de bloque
│       ├── PortalPower.cs      Lee el TileEntity electrico real del portal (requiere cableado)
│       ├── LogFilterPatch.cs   Filtra spam inofensivo del log (particulas rotas del modelo 6)
│       ├── XUiPortalTag.cs     Controller de la ventana de nombre de tag
│       └── PortalUtils.cs      Helpers compartidos (identidad de jugador, HUD)
├── Resources/
│   ├── gupFuturePortal1.unity3d   Modelo 3D: estilo "platform" (y portalBlock legacy inactivo)
│   ├── gupFuturePortal2.unity3d   Modelo 3D: estilo "grid"
│   ├── gupFuturePortal3.unity3d   Modelo 3D: estilo "claws"
│   ├── gupFuturePortal4.unity3d   Modelo 3D: estilo "cylinder"; tambien efecto de particulas de teletransporte
│   ├── gupFuturePortal5.unity3d   Modelo 3D: estilo "wings" ("con alas"; tambien el estilo legacy vinculado)
│   ├── gupFuturePortal6.unity3d   Modelo 3D: estilo "arch"
│   ├── gupPortKeyCard.unity3d     Modelo 3D: mesh en mano de todos los items de portal
│   ├── gupKeyCardSound.unity3d    Sonido: activacion del portal
│   └── gupTeleportRide.unity3d    Efecto de particulas: viaje (loop del buff)
└── UIAtlases/
    └── ItemIconAtlas/
        ├── guppyFuturePortal1-6.png   Iconos de inventario: uno por estilo + legacy
        └── gupPortKeyCard.png         Icono reservado (sin item asociado aun)
```

**Nota sobre `Resources/`**: los bundles `.unity3d` deben subirse
manualmente al repositorio junto al resto del mod — no se generan ni se
validan desde este proyecto C#. Los nombres de prefab/clip DENTRO de cada
bundle (usados en los atributos `#@modfolder:...?NombrePrefab` de
`blocks.xml`/`items.xml`/`buffs.xml`) ya están confirmados contra el XML
original del mod de assets (SCore) — ver `TESTING.md` sección 10 para el
detalle de cada uno — pero siguen sin probarse contra el juego real.

**Nota sobre `UIAtlases/`**: esta es la ruta REAL donde el juego busca
iconos custom de mod — confirmado decompilando ModManager y
UIAtlasFromFolder.CreateUiAtlasFromFolder contra el Assembly-CSharp.dll
real: cada subcarpeta dentro de `<CarpetaDelMod>/UIAtlases/` se carga como
un atlas nuevo, tomando cada `.png`/`.jpg`/`.tga` de esa subcarpeta como un
sprite (nombre del sprite = nombre de archivo sin extension). `Resources/
ItemIcons/` (la ubicacion original de estos mismos archivos) NUNCA fue
escaneada por el juego para esto — los iconos nunca se veian en el
inventario.

**Nota sobre `PortalHoverFX.cs`**: identificacion visual del destino del
portal (tag) al apuntarle con la mira. El tooltip HUD reutiliza
`GameManager.ShowTooltip` (ya usado por `PortalHud`). El texto flotante
sobre el bloque reutiliza `DamageText.Create(string, Color, Vector3
worldPos, Vector3 velocity, float scale)` — confirmado decompilando
`EntityAlive.DamageEntity` contra el Assembly-CSharp.dll real: es el mismo
metodo que el juego usa para los numeros de daño flotantes, publico y
generico para cualquier string. Se prefirio sobre el sistema de
carteles/`TileEntitySign` (mucho mas pesado: textura horneada por bloque)
porque no existe una API de "texto flotante generico" mas simple expuesta
(se buscaron por reflection tipos `FloatingText`/`WorldText`/`NameTag`/
`Nameplate`: ninguno existe en V3.0). Limitacion conocida: `DamageText.Create`
instancia su GameObject localmente (`Resources.Load`+`Object.Instantiate`,
sin replicacion de red) y depende de `Camera.main`, por lo que
`PortalHoverFX` corre unicamente sobre el jugador local de cada cliente
(`World.GetPrimaryPlayer()`) y nunca en `GameManager.IsDedicatedServer`.

**Nota sobre los 6 estilos de portal ("Opcion A")**: cada estilo
(`platform`/`grid`/`claws`/`cylinder`/`wings`/`arch`) es una familia
INDEPENDIENTE de bloques en `blocks.xml` (1 inactivo + 6 activos —
default + 5 biomas), encadenados con `Extends` desde el bloque `portalBlock`
original (heredan `Class="Powered"`/`MaxDamage`/`Material`/etc. sin
redeclarar, solo cambian `Model`/`CustomIcon`). El bloque/item `portalBlock`/
`portalBlockItem` originales se conservan sin cambios como un septimo
estilo implicito ("legacy") — no se eliminaron, para no romper mundos ya
guardados con ese bloque colocado. `PortalManager.cs` detecta el estilo de
cada portal leyendo el bloque INACTIVO ya colocado en el momento de
vincularse (igual mecanismo que el bioma) y lo persiste junto a la
posicion; `PortalBiomes.cs` centraliza el mapeo estilo+bioma -> nombre de
bloque.

**FIX real (colision de nombre item/bloque)**: el pedido original nombraba
el item igual que el bloque (ej. `portalBlock_platform` para ambos). En
V3.0 items y bloques comparten un unico namespace plano de nombres — dos
entradas con el mismo `name` no pueden coexistir (mismo motivo por el que
el mod ya distinguia `portalBlockItem` de `portalBlock` desde el principio).
Se le agrego el sufijo `Item` a cada nombre de item nuevo
(`portalBlock_platformItem`, etc.), dejando los BLOQUES con el nombre
exacto pedido.

**FIX real (receta)**: el ingrediente pedido `resourceIron` no existe en
items.xml vanilla (0 coincidencias) — se uso `resourceScrapIron` (el mismo
recurso ya usado en la receta de `portalBlockItem`), confirmado real.

## Limitaciones conocidas / TODOs

Varios puntos de la API interna de 7 Days to Die V3.0 no pudieron
confirmarse sin acceso directo al `Assembly-CSharp.dll` publicized de esa
versión exacta. Cada uno está marcado en el código fuente con un comentario
`// TODO: verificar en Assembly-CSharp V3.0 ...` indicando qué buscar. Los
puntos más relevantes:

- Firmas exactas de `Block.OnBlockPlaceBefore`, `Block.OnBlockActivated` y
  `Block.OnBlockRemoved` (`PortalBlockPatch.cs`).
- Método correcto de teletransporte de un `EntityPlayer` en servidor
  dedicado (`PortalTeleport.cs`).
- API de persistencia oficial ligada al slot de guardado del mundo
  (`PortalManager.cs`); mientras tanto se usa un archivo de texto plano
  propio del mod.
- Acceso al `XUiManager` del jugador local y nombres de controles del
  sistema de binding V3.0 (`XUiPortalTag.cs`, `windows.xml`).
- **Sistema de party/grupo (`PortalParty.cs`)**: no se pudo confirmar contra
  el `Assembly-CSharp.dll` real si V3.0 tiene siquiera un sistema de
  party/grupo formal, ni bajo qué nombre (`PartyManager`, `EntityPlayer.Party`,
  etc.). Se resuelve por reflection probando varios nombres candidatos
  (ver comentario extenso en `PortalParty.cs`); si ninguno existe en el
  juego real, el mod simplemente sigue funcionando tratando a todos los
  jugadores como solitarios — el mismo comportamiento que había antes de
  esta funcionalidad, sin romper nada.
- **Evento de "unirse/salir de party"**: por el mismo motivo (no se pudo
  confirmar que exista, y una referencia directa a un evento inexistente
  sería un error de compilación, no solo de runtime), la migración de
  portales al cambiar de party **no** usa un evento — se detecta por
  sondeo periódico dentro del mismo tick que ya revisa colisiones
  jugador-portal (`PortalManager.CheckPartyMembershipChanged`, llamado
  desde `PortalTeleport.cs`). Esto puede introducir un pequeño retraso
  (hasta unos segundos) entre el cambio real de party y la migración.
- **Migración de portales no reaplica el límite de 2 por tag**: si dos
  miembros de una party (o un jugador que se une a una que ya tenía
  portales) tenían portales con el mismo tag por separado, tras fusionarse
  puede terminar habiendo 3 o más posiciones registradas para ese tag. El
  sistema sigue funcionando (se usa el primer destino válido que no sea el
  origen), pero deja de garantizar estrictamente el máximo de 2 — se
  prefirió esto a rechazar la migración, lo que dejaría un bloque físico ya
  colocado en el mundo sin ningún registro.
- **Identificador de jugador ("steamId") puede seguir sin ser estable entre
  sesiones — reportado en servidor dedicado real**: hasta la fecha,
  `PortalIdentity.GetSteamId` usaba `EntityPlayer.entityId.ToString()` como
  único identificador (`entityId` NO es estable entre reconexiones — el
  mismo jugador puede recibir uno distinto la próxima vez que se conecta).
  Un usuario reportó exactamente el síntoma esperado de esto: **pierde la
  propiedad de sus portales cada vez que se desconecta**, y tiene que
  destruirlos y volver a colocarlos para recuperarlos. Se agregó una
  resolución por reflection de un identificador de plataforma real
  (Steam64/EOS/etc., varios nombres candidatos — ver `PortalUtils.cs`) que
  se intenta ANTES de caer a `entityId`, con el mismo patrón defensivo que
  `PortalParty.cs`. **No se pudo confirmar contra el `Assembly-CSharp.dll`
  real si alguno de los candidatos existe** — si ninguno resuelve, el mod
  sigue funcionando exactamente como antes (mismo bug). Revisar el log del
  servidor: si el `ownerKey` logueado en `RegisterPortal` empieza con
  `plat:` el fix está activo; si sigue siendo un número corto, ningún
  candidato resolvió y hace falta decompilar el DLL real para encontrar el
  nombre correcto.
- **Cruce de identidad a mitad de sesión rompía el uso compartido de
  portales en party — reportado**: consecuencia directa del punto anterior.
  `PortalIdentity.GetSteamId` puede devolver el `entityId` (fallback)
  durante los primeros llamados de la sesión y "cruzar" a `plat:...` en
  cuanto el identificador de plataforma resuelve. Si un jugador colocaba un
  portal ANTES de ese cruce y se unía a una party DESPUÉS, la migración
  automática buscaba el portal bajo la key nueva (`plat:...`) pero seguía
  registrado bajo la vieja (`entityId`) — no encontraba nada que migrar, y
  el portal nunca pasaba a ser compartido: sus compañeros de party lo veían
  como el portal de un desconocido. **Corregido**: `PortalManager.
  ReassignSteamId`, invocado desde `PortalIdentity.GetSteamId` en el
  momento exacto del cruce, reescribe toda referencia al `entityId` viejo
  (portales registrados bajo esa key, atribución de dueño original,
  cooldown, cache de detección de cambios de party) por el identificador
  nuevo.
- **Renombrar un portal ya vinculado desconecta el cable de su PAREJA**: es
  consecuencia directa de la limitación de arriba ("swap de bloque = cable
  cortado") combinada con cómo funciona `RenamePortal` — internamente hace
  un desregistro + registro del portal que estás renombrando, lo que
  primero rompe el par (el portal que NO estás tocando queda huérfano y su
  bloque cambia a la variante inactiva, cortando su cable) y luego, si el
  nuevo tag encuentra pareja, vuelve a cambiar de bloque. Si el segundo
  portal del par pertenece a otro miembro de tu party, esto puede
  desconectar un cable que él tendió sin que vos lo notes ni él sepa por
  qué. Mitigación práctica mientras tanto: volver a cablear ambos portales
  después de renombrar uno que ya estaba vinculado.
- **Atribución de "dueño original" no se persiste a disco**: el índice de
  qué jugador colocó físicamente cada portal (usado para devolver solo tus
  propios portales al salir de una party) vive únicamente en memoria. Si el
  servidor se reinicia mientras un portal ya está registrado bajo una
  party, esa atribución se pierde para ese portal en particular — al salir
  de la party después de un reinicio, ese portal específico no se migrará
  de vuelta a nadie automáticamente (se queda con la party).
- **Ghost de colocación (Feature "modo fantasma")**: no es un hook sobre el
  sistema nativo de preview del juego (no confirmado contra el
  `Assembly-CSharp.dll` real, ver sección dedicada más arriba) — es una caja
  traslucida propia, no un clon del modelo 3D real de cada estilo, y la
  detección de "qué item tiene equipado el jugador" es *best-effort* por
  reflection (mismo patrón que `PortalIdentity`/`PortalParty`): si ningún
  candidato resuelve, el ghost simplemente no aparece nunca, sin romper nada
  más del mod.
- **Convención de rotación propia (Feature "rotación real")**: no se pudo
  confirmar el mapeo real del motor entre `BlockValue.rotation` y la
  orientación visual de un `Shape="ModelEntity"` — `PortalOrientation.cs`
  define su propia convención, consistente en todo el mod, pero el sentido
  visual exacto (si la flecha/el punto de aterrizaje coinciden con el frente
  real del modelo 3D) queda pendiente de calibrar contra el juego real — ver
  sección dedicada más arriba y `TESTING.md`.
- **Jugador atascado en una pared / devuelto al origen al llegar — reportado
  en servidor dedicado real**: `World.GetBlock()` en una celda de un chunk
  que todavía no terminó de cargar devuelve `BlockValue.Air`, indistinguible
  de "acá de verdad no hay nada". El cálculo del punto de aterrizaje
  (`PortalTeleport.FindLandingBlockPos`) confiaba en esa lectura sin
  verificar si el chunk estaba realmente cargado, así que en servidores con
  streaming de chunks lento podía "aterrizar" al jugador sobre terreno que
  en ese momento se leía como aire pero resultaba sólido apenas el chunk
  terminaba de cargar. **Corregido**: si el chunk destino no está cargado,
  ya no se escanea en absoluto (se usa la posición del portal sin
  modificar) — y `PortalTeleport.TryTeleport` ahora espera (con un límite
  configurable, ver `Config/PortalModConfig.xml` → `MaxChunkWaitSeconds`,
  default 2s, 0 desactiva la espera) a que el chunk destino esté cargado
  antes de mover al jugador, sin ningún mensaje de "espera" visible — en el
  caso normal (chunk ya cargado) el viaje sigue siendo instantáneo.

Ya resueltos (confirmados contra el XML original del mod de assets SCore,
pendientes solo de probarse en el juego real — ver `TESTING.md`):

- Nombres exactos de los prefabs/clips dentro de cada `.unity3d` en
  `Resources/`.
- `<triggered_effect action="PlaySound">` en `buffs.xml` **no** acepta una
  ruta de AssetBundle directa: requiere un nombre registrado en
  `Config/sounds.xml` (ver `SoundDataNode` `guppyKeyUsed`). `particle`
  (`PlayParticleEffect`) y `AttachPrefabToEntity` sí aceptan rutas
  `#@modfolder:...` directas.
- `Meshfile`/`DropMeshfile` son las propiedades correctas para el mesh en
  mano/inventario y en el suelo de un item.

Si compilas contra tu propio `Assembly-CSharp.dll` y alguna firma no
coincide, Harmony fallará de forma explícita en el log al hacer
`PatchAll()`, indicando exactamente qué método no pudo encontrar — ajusta la
firma en el archivo correspondiente y vuelve a compilar.
