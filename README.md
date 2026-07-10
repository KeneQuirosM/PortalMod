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
   `ModInfo.xml`, `Config/`, `PortalMod.dll` (generado al compilar) y
   `UIFrames/`.
4. Desactiva EAC y arranca el juego / servidor. Si el mod cargó
   correctamente, verás en la consola (F1 o log del servidor) líneas con el
   prefijo `[PortalMod]`.

## Compilación del DLL (Visual Studio 2022 / Rider)

El proyecto está en `Harmony/PortalMod.csproj`.

1. Localiza la carpeta `Managed` de tu instalación del juego:
   - Cliente Windows: `<instalación>\7DaysToDie_Data\Managed\`
   - Servidor dedicado: `<instalación>\7DaysToDieServer_Data\Managed\`
2. **Publiciza `Assembly-CSharp.dll`** (obligatorio: el proyecto necesita
   acceder a miembros `internal`/`private` del juego para los patches de
   Harmony). Herramientas recomendadas:
   - [AssemblyPublicizer](https://github.com/CabbageCrow/AssemblyPublicizer) (standalone)
   - El publicizer incluido en BepInEx / `Il2CppAssemblyUnhollower` no aplica
     aquí porque 7D2D V3.0 sigue usando Mono, no IL2CPP — usa la versión
     "Publicizer for Mono" del enlace anterior.
   - Copia el `Assembly-CSharp_publicized.dll` resultante a la carpeta
     `Managed` (o renómbralo a `Assembly-CSharp.dll` en una copia aparte que
     uses solo para compilar, **nunca reemplaces el original del juego**).
3. Define la variable de entorno `SEVENDAYS_INSTALL_DIR` apuntando a la raíz
   de tu instalación (recomendado, evita tocar el `.csproj`):
   - Windows (PowerShell): `setx SEVENDAYS_INSTALL_DIR "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"`
   - Linux: `export SEVENDAYS_INSTALL_DIR="$HOME/.steam/steam/steamapps/common/7 Days To Die"`
   - Alternativamente edita directamente `<Game7DaysToDiePath>` dentro de
     `Harmony/PortalMod.csproj`.
4. Abre `Harmony/PortalMod.csproj` en Visual Studio 2022 o Rider y compila en
   `Release`. El `.csproj` ya está configurado para que el DLL resultante
   (`PortalMod.dll`) se copie automáticamente a la raíz de `PortalMod/`
   (junto a `ModInfo.xml`), lista para distribuir/instalar.
5. Si tu IDE se queja de referencias no encontradas, confirma que
   `Assembly-CSharp.dll`, `Assembly-CSharp-firstpass.dll`,
   `UnityEngine.CoreModule.dll` y `0Harmony.dll` existen dentro de la carpeta
   `Managed` detectada.

## Cómo usar el mod (in-game)

1. Craftea `Teleport Portal` (`portalBlockItem`) en una **mesa de trabajo**
   (workbench). Costo: 15× Hierro, 5× Hierro Forjado, 3× Piezas Eléctricas.
   Tiempo de crafteo: 60 segundos.
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

## Multijugador

Cada jugador gestiona su propio conjunto de portales de forma independiente
(indexados internamente por su identificador de plataforma, "steamId"). Los
portales de un jugador no interactúan con los de otro, incluso si usan el
mismo tag.

## Notas técnicas — específicas de V3.0

- **XUi_InGame, no `XUi`**: la carpeta de interfaces de este mod vive en
  `UIFrames/XUi_InGame/`, siguiendo la convención de carpetas introducida
  antes de V1.0 (reemplaza a la antigua `XUi/`).
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

## Estructura del proyecto

```
PortalMod/
├── ModInfo.xml
├── README.md
├── Config/
│   ├── blocks.xml          Bloque unico "portalBlock"
│   ├── items.xml           Item crafteable "portalBlockItem"
│   ├── recipes.xml         Receta de workbench
│   ├── buffs.xml           buffPortalTravel
│   └── localization.txt    Strings en english / Spanish (es-ES)
├── Harmony/
│   ├── PortalMod.csproj
│   └── src/
│       ├── API.cs              Punto de entrada IModApi
│       ├── PortalManager.cs    Registro/vinculacion/cooldown/persistencia
│       ├── PortalTeleport.cs   Deteccion de colision y teletransporte
│       ├── PortalBlockPatch.cs Harmony patches (colocar/activar/destruir)
│       ├── XUiPortalTag.cs     Controller de la ventana de nombre de tag
│       └── PortalUtils.cs      Helpers compartidos (identidad de jugador, HUD)
└── UIFrames/
    └── XUi_InGame/
        └── windows.xml     Ventana popup "Nombrar portal"
```

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

Si compilas contra tu propio `Assembly-CSharp.dll` y alguna firma no
coincide, Harmony fallará de forma explícita en el log al hacer
`PatchAll()`, indicando exactamente qué método no pudo encontrar — ajusta la
firma en el archivo correspondiente y vuelve a compilar.
