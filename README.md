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

## Cómo usar el mod (in-game)

1. Craftea uno de los **6 estilos de portal** en una **mesa de trabajo**
   (workbench), cada uno con su propio modelo 3D: `Platform Portal`
   (`portalBlock_platformItem`), `Grid Portal` (`portalBlock_gridItem`),
   `Claw Portal` (`portalBlock_clawsItem`), `Cylinder Portal`
   (`portalBlock_cylinderItem`), `Wings Portal` (`portalBlock_wingsItem`) o
   `Arch Portal` (`portalBlock_archItem`) — o el `Teleport Portal`
   (`portalBlockItem`) original. Costo de los 6 estilos nuevos: 15×
   Chatarra de Hierro, 5× Hierro Forjado, 3× Piezas Eléctricas, 60 segundos
   (el `Teleport Portal` original tiene un costo distinto, mayor). El
   estilo elegido queda fijo para ese portal — no se puede cambiar después
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
8. Tras cada viaje hay un **cooldown de 5 segundos** antes de poder volver a
   activar un portal, para evitar loops infinitos de teletransporte.
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

## Multijugador

Cada jugador gestiona su propio conjunto de portales de forma independiente
(indexados internamente por su identificador de plataforma, "steamId"). Los
portales de un jugador no interactúan con los de otro, incluso si usan el
mismo tag.

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
- **ModInfo.xml formato V2**: obligatorio desde V1.0; este mod ya lo usa.
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
│   └── XUi_InGame/
│       ├── windows.xml     Ventana popup "Nombrar portal" (windowPortalTag)
│       └── xui.xml         window_group que registra windowPortalTag
├── Harmony/
│   ├── PortalMod.csproj.template   Plantilla versionada (copiar, ver abajo)
│   ├── PortalMod.csproj            Copia local, en .gitignore, NO versionado
│   └── src/
│       ├── API.cs              Punto de entrada IModApi
│       ├── PortalManager.cs    Registro/vinculacion/cooldown/persistencia/estilo/bioma
│       ├── PortalTeleport.cs   Deteccion de colision y teletransporte
│       ├── PortalBlockPatch.cs Harmony patches (colocar/activar/destruir)
│       ├── PortalVisualFX.cs   Luz/particulas por estado + rafagas de teletransporte
│       ├── PortalHoverFX.cs    Tooltip + texto flotante al apuntar a un portal (mira)
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
