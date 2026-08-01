using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Scripting;

namespace PortalMod
{
    /// <summary>
    /// Sincronizacion cliente-servidor del registro de portales — corrige la
    /// causa raiz 3 documentada en TESTING.md (seccion 12.3): antes de esto,
    /// "PortalManager.Instance" era un singleton POR PROCESO sin ningun
    /// mecanismo de red — un portal registrado por un cliente remoto (via
    /// XUiPortalTag.Confirm(), que solo puede correr en el proceso donde ese
    /// jugador es EntityPlayerLocal) nunca llegaba al PortalManager real del
    /// servidor, que es el que PortalTeleport.Tick() usa para decidir
    /// teletransportes. Ver TESTING.md 12.3 para la cadena de razonamiento
    /// completa (confirmada, no corregida en esa sesion) y la seccion 13
    /// para el detalle de ESTE fix.
    ///
    /// CONFIRMADO CONTRA Assembly-CSharp.dll REAL (decompilado con ilspycmd,
    /// no solo reflection — ver TESTING.md 13.1 para el detalle de cada
    /// clase inspeccionada):
    ///   - NetPackage es la clase base real y abstracta que usa el juego
    ///     para TODO su networking (confirmado: NetPackageSetBlock,
    ///     NetPackageConsoleCmdServer/Client, etc. heredan de ella).
    ///   - El registro de que clases de NetPackage existen es DINAMICO, no
    ///     un enum fijo: NetPackageManager (constructor estatico) llama
    ///     ReflectionHelpers.FindTypesImplementingBase(typeof(NetPackage),
    ///     ...), que escanea TODOS los ensamblados cargados via
    ///     ModManager.GetLoadedAssemblies() — un mod de Harmony como este
    ///     (PortalMod.dll) YA esta en esa lista, asi que cualquier subclase
    ///     de NetPackage definida aca se descubre sola, sin tocar ningun
    ///     array/enum del juego. NetPackageManager.StartServer() les asigna
    ///     IDs numericos en tiempo de ejecucion y se los manda a cada
    ///     cliente al conectarse (IdMappingsReceived) — si un cliente NO
    ///     tiene una clase que el servidor sí conoce (por ejemplo si le
    ///     falta este mod), el juego lo desconecta con un error CLARO
    ///     ("Unknown package type ..."), nunca corrompe la sesion de nadie
    ///     mas. Esto es lo que hace que agregar un NetPackage propio sea
    ///     seguro para un mod de terceros — no hay ningun array fijo que
    ///     pueda pisarse ni colisionar.
    ///   - API real de envio: ConnectionManager.Instance.SendToServer(pkg)
    ///     (cliente -&gt; servidor), ConnectionManager.Instance.SendPackage(pkg)
    ///     (servidor -&gt; todos los clientes con login ya terminado),
    ///     ClientInfo.SendPackage(pkg) (servidor -&gt; un cliente puntual).
    ///   - ConnectionManager.Instance.IsServer (protocolManager.IsServer) es
    ///     true en servidor dedicado, en el host de un listen server, Y en
    ///     singleplayer puro (confirmado: IsSinglePlayer == IsServer &amp;&amp;
    ///     ClientCount() == 0) — o sea, es EXACTAMENTE el chequeo "soy la
    ///     autoridad real" que hace falta, sin necesidad de distinguir esos
    ///     tres casos por separado en ningun lado de este mod.
    /// </summary>
    internal static class PortalNetSync
    {
        /// <summary>
        /// Manda el registro completo actual a UN cliente puntual — usado al
        /// conectarse (ver API.cs, OnPlayerSpawnedInWorld) para que su
        /// PortalManager local (que arranca vacio) refleje la realidad del
        /// servidor sin tener que esperar al primer cambio incremental.
        /// No-op si "client" es null (por ejemplo un jugador sin conexion de
        /// red real — singleplayer puro, ver PortalIdentity.cs sobre el
        /// mismo caso).
        /// </summary>
        internal static void SendFullSyncToClient(ClientInfo client)
        {
            if (client == null)
            {
                return;
            }

            var package = NetPackageManager.GetPackage<NetPackagePortalSync>().Setup(PortalManager.Instance.GetSyncSnapshot());
            client.SendPackage(package);
        }

        /// <summary>
        /// Manda el registro completo actual a TODOS los clientes conectados
        /// — llamado desde PortalManager tras cualquier mutacion real
        /// (RegisterPortal/RenamePortal/UnregisterPortal/MigratePortals).
        /// Se manda el snapshot COMPLETO (no un delta incremental) a
        /// proposito: la cantidad de portales de un servidor tipico es
        /// pequeña (decenas, no miles) y esto solo se dispara en eventos
        /// raros iniciados por un jugador (nombrar/renombrar/destruir un
        /// portal), nunca por tick — el costo extra de mandar todo de nuevo
        /// es insignificante comparado con el riesgo de un protocolo de
        /// deltas mal sincronizado (una entrada que nunca se borra, un
        /// "remove" que llega antes que su "add", etc.).
        /// No-op si esto no corre en el servidor (ver comentario de clase).
        /// </summary>
        internal static void BroadcastFullSyncIfServer()
        {
            if (ConnectionManager.Instance == null || !ConnectionManager.Instance.IsServer)
            {
                return;
            }

            var package = NetPackageManager.GetPackage<NetPackagePortalSync>().Setup(PortalManager.Instance.GetSyncSnapshot());
            ConnectionManager.Instance.SendPackage(package);
        }
    }

    /// <summary>
    /// Cliente -&gt; servidor: "quiero registrar/renombrar el portal en esta
    /// posicion con este tag". Nunca manda un ownerKey ni un estilo — el
    /// servidor los resuelve el mismo a partir de "Sender" (la identidad
    /// real de quien mando el paquete, ver NetPackage.Sender/ClientInfo) y
    /// del bloque real ya colocado en el mundo, exactamente igual que ya
    /// hacia PortalBlockPatch/PortalManager antes de este fix — nunca se
    /// confia en un dato de "quien soy"/"que estilo es" mandado por el
    /// cliente (superficie de trampa obvia si se confiara).
    /// </summary>
    [Preserve]
    public class NetPackagePortalRequest : NetPackage
    {
        private string _tag;
        private Vector3i _pos;

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;

        public NetPackagePortalRequest Setup(string tag, Vector3i pos)
        {
            _tag = tag;
            _pos = pos;
            return this;
        }

        // FIX real (CS0518 "System.ReadOnlySpan`1 no esta definido ni
        // importado" al compilar contra el Assembly-CSharp.dll real):
        // PooledBinaryReader/PooledBinaryWriter agregan sobrecargas propias
        // de Read/Write con ReadOnlySpan&lt;T&gt; (el juego corre sobre un
        // runtime moderno con soporte de Span) que el net48 clasico de
        // este proyecto no puede resolver — ni siquiera para las
        // sobrecargas simples (int/string) que SI se usan aca, porque el
        // compilador necesita poder resolver el conjunto COMPLETO de
        // sobrecargas de "Write"/"Read" en el tipo para hacer overload
        // resolution. Se evita el problema llamando a traves del tipo BASE
        // real "System.IO.BinaryWriter"/"BinaryReader" (el mismo net48 ya
        // trae esos completos, sin ninguna sobrecarga de Span) en vez del
        // tipo derivado — mismos bytes en el wire, ningun cambio de
        // comportamiento, solo evita necesitar resolver miembros que este
        // proyecto no usa.
        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader reader = _reader;
            _tag = reader.ReadString();
            _pos = new Vector3i(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter writer = _writer;
            writer.Write(_tag ?? string.Empty);
            writer.Write(_pos.x);
            writer.Write(_pos.y);
            writer.Write(_pos.z);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                // Doble chequeo de autoridad (PackageDirection=ToServer ya
                // implica que solo deberia llegar aca del lado servidor,
                // pero un mod que corre en el mismo proceso que el juego no
                // tiene forma de confiar ciegamente en eso — mismo espiritu
                // defensivo que el resto de este mod).
                if (_world == null || ConnectionManager.Instance == null || !ConnectionManager.Instance.IsServer)
                {
                    return;
                }

                var sender = Sender;
                var player = sender != null ? _world.GetEntity(sender.entityId) as EntityPlayer : null;
                if (player == null)
                {
                    API.LogWarning("[PortalMod] NetPackagePortalRequest: no se pudo resolver el EntityPlayer del remitente; se ignora.");
                    return;
                }

                // Mismo criterio que PortalBlockPatch.HandlePortalActivation
                // (decidir "nombrar" vs "renombrar" por si la posicion ya
                // esta registrada) — re-derivado aca en vez de confiar en un
                // flag mandado por el cliente, que podria estar
                // desactualizado si algo cambio el registro entre que se
                // abrio la ventana y se confirmo.
                var wasRename = PortalManager.Instance.TryGetPortalRef(_pos, out _);
                var result = wasRename
                    ? PortalManager.Instance.RenamePortal(player, _pos, _tag)
                    : PortalManager.Instance.RegisterPortal(player, _tag, _pos);

                if (sender != null)
                {
                    sender.SendPackage(NetPackageManager.GetPackage<NetPackagePortalRequestResult>().Setup(result, _tag, wasRename));
                }
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion en NetPackagePortalRequest.ProcessPackage: {e}");
            }
        }

        public override int GetLength()
        {
            return (_tag?.Length ?? 0) + 16;
        }
    }

    /// <summary>
    /// Servidor -&gt; el cliente que mando el NetPackagePortalRequest
    /// original: el resultado real (aceptado/huerfano/tag en uso/tag
    /// vacio) para que XUiPortalTag.HandleServerResult muestre el mismo
    /// mensaje de HUD y cierre la ventana igual que hacia el camino
    /// sincrono de antes (ver XUiPortalTag.cs).
    /// </summary>
    [Preserve]
    public class NetPackagePortalRequestResult : NetPackage
    {
        private byte _result;
        private string _tag;
        private bool _wasRename;

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

        public NetPackagePortalRequestResult Setup(PortalManager.RegisterResult result, string tag, bool wasRename)
        {
            _result = (byte)result;
            _tag = tag;
            _wasRename = wasRename;
            return this;
        }

        // Ver comentario extenso en NetPackagePortalRequest.read sobre por
        // que se llama a traves de BinaryReader/BinaryWriter en vez del
        // tipo Pooled derivado.
        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader reader = _reader;
            _result = reader.ReadByte();
            _tag = reader.ReadString();
            _wasRename = reader.ReadBoolean();
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter writer = _writer;
            writer.Write(_result);
            writer.Write(_tag ?? string.Empty);
            writer.Write(_wasRename);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                XUiPortalTag.HandleServerResult((PortalManager.RegisterResult)_result, _tag, _wasRename);
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion en NetPackagePortalRequestResult.ProcessPackage: {e}");
            }
        }

        public override int GetLength()
        {
            return (_tag?.Length ?? 0) + 4;
        }
    }

    /// <summary>
    /// Servidor -&gt; cliente(s): snapshot COMPLETO del registro de portales
    /// (ver PortalManager.GetSyncSnapshot/ApplyFullSync). Se usa tanto para
    /// el sync inicial de un jugador recien conectado como para el
    /// broadcast tras cualquier cambio real — siempre reemplaza el estado
    /// local del cliente por completo, nunca fusiona (ver ApplyFullSync).
    /// </summary>
    [Preserve]
    public class NetPackagePortalSync : NetPackage
    {
        private List<PortalManager.PortalSyncEntry> _entries;

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

        public NetPackagePortalSync Setup(List<PortalManager.PortalSyncEntry> entries)
        {
            _entries = entries;
            return this;
        }

        // Ver comentario extenso en NetPackagePortalRequest.read sobre por
        // que se llama a traves de BinaryReader/BinaryWriter en vez del
        // tipo Pooled derivado.
        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader reader = _reader;
            var count = reader.ReadUInt16();
            _entries = new List<PortalManager.PortalSyncEntry>(count);

            for (var i = 0; i < count; i++)
            {
                var ownerKey = reader.ReadString();
                var tag = reader.ReadString();
                var pos = new Vector3i(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                var biome = reader.ReadString();
                var style = reader.ReadString();
                _entries.Add(new PortalManager.PortalSyncEntry(ownerKey, tag, pos, biome, style));
            }
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);

            BinaryWriter writer = _writer;
            var entries = _entries ?? new List<PortalManager.PortalSyncEntry>();
            writer.Write((ushort)entries.Count);

            foreach (var entry in entries)
            {
                writer.Write(entry.OwnerKey ?? string.Empty);
                writer.Write(entry.Tag ?? string.Empty);
                writer.Write(entry.Pos.x);
                writer.Write(entry.Pos.y);
                writer.Write(entry.Pos.z);
                writer.Write(entry.Biome ?? string.Empty);
                writer.Write(entry.Style ?? string.Empty);
            }
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                PortalManager.Instance.ApplyFullSync(_entries ?? new List<PortalManager.PortalSyncEntry>());
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion en NetPackagePortalSync.ProcessPackage: {e}");
            }
        }

        public override int GetLength()
        {
            return (_entries?.Count ?? 0) * 48;
        }
    }
}
