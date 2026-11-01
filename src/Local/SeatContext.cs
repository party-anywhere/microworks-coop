using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirror;
using PartyAnywhere;
using UnityEngine;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// Num jogo só com vários jogadores, o código do MicroWorks pergunta "quem é o jogador local?" em todo canto
    /// (isLocalPlayer, GlobalData.localPlayer). Enquanto o código de um personagem extra roda, ele é o jogador
    /// local: assim a câmera, o HUD, o movimento e as ações dele usam o código do próprio jogo. Fora disso, o
    /// jogador local é o de quem hospeda, como sempre.
    /// </summary>
    static class SeatContext
    {
        static int current = -1; // a vaga em contexto; -1 = quem hospeda

        static readonly FieldInfo localPlayerField = AccessTools.Field(typeof(ClientScene), "_localPlayer");

        static readonly PlayerController[] players = new PlayerController[Client.MaxSeats];
        static readonly Dictionary<int, int> rootSeat = new Dictionary<int, int>();      // personagem → vaga
        static readonly Dictionary<int, int> componentSeat = new Dictionary<int, int>(); // componente → vaga (-1: nenhuma)

        /// <summary>O código que roda agora é de um jogador extra (não de quem hospeda).</summary>
        public static bool IsExtra => current >= 0;

        /// <summary>A vaga de quem está em contexto (a de quem hospeda, fora do código de um extra).</summary>
        public static int Seat => current >= 0 ? current : Plugin.Seat;

        public static PlayerController PlayerOf(int seat) => players[seat];

        /// <summary>Antes do personagem nascer: dali em diante o código dele roda como jogador local.</summary>
        public static void Register(int seat, PlayerController pc)
        {
            players[seat] = pc;
            rootSeat[pc.gameObject.GetInstanceID()] = seat;
            componentSeat.Clear(); // componentes vistos antes (ainda sem dono) são procurados de novo
            ContextPatcher.PatchTypesOf(pc.gameObject);
        }

        public static void Unregister(int seat)
        {
            PlayerController pc = players[seat];
            players[seat] = null;
            if (pc != null) rootSeat.Remove(pc.gameObject.GetInstanceID());
            componentSeat.Clear();
        }

        public struct Token
        {
            public bool Entered;
            public int PreviousSeat;
            public NetworkIdentity PreviousLocal;
            public PlayerController PreviousGlobal;
        }

        public static Token Enter(int seat)
        {
            // já é a vez dele: nada a fazer. Assim só a entrada de fora troca e volta, e a ordem em que o Harmony
            // fecha os patches de um mesmo método (a mesma da abertura, não a inversa) não deixa ninguém preso
            if (current == seat || players[seat] == null) return default; // sem personagem ainda: sem contexto
            var t = new Token { Entered = true, PreviousSeat = current, PreviousLocal = (NetworkIdentity)localPlayerField.GetValue(null), PreviousGlobal = GlobalData.localPlayer };
            current = seat;
            PlayerController pc = players[seat];
            localPlayerField.SetValue(null, pc != null ? pc.netIdentity : null);
            GlobalData.localPlayer = pc;
            return t;
        }

        public static void Exit(Token t)
        {
            if (!t.Entered) return;
            current = t.PreviousSeat;
            localPlayerField.SetValue(null, t.PreviousLocal);
            GlobalData.localPlayer = t.PreviousGlobal;
        }

        /// <summary>
        /// Um objeto passou a ser de um jogador extra (a arma na mão, o sapo, um projétil): o código dele também roda
        /// no contexto do extra, e os componentes já vistos são procurados de novo.
        /// </summary>
        public static void Owned(GameObject obj)
        {
            componentSeat.Clear();
            ContextPatcher.PatchTypesOf(obj);
        }

        /// <summary>Um objeto deixou de ser de um extra: os componentes vistos são procurados de novo.</summary>
        public static void Disowned() => componentSeat.Clear();

        /// <summary>A vaga do jogador extra dono deste componente, ou -1 (o de quem hospeda, ou de ninguém).</summary>
        public static int SeatOf(Component c)
        {
            if (c == null || rootSeat.Count == 0) return -1;
            int id = c.GetInstanceID();
            if (componentSeat.TryGetValue(id, out int seat)) return seat;
            PlayerController pc = c.GetComponentInParent<PlayerController>();
            seat = pc != null && rootSeat.TryGetValue(pc.gameObject.GetInstanceID(), out int s) ? s : -1;
            // fora do personagem: o que a rede diz que é dele (a arma antes de ir para a mão, o sapo, o kart, projéteis)
            if (seat < 0 && pc == null) seat = SeatAuthority.SeatOf(c.GetComponentInParent<NetworkIdentity>());
            // o que não é do personagem mas é dele (a cópia do HUD), mesmo desligado
            for (Transform t = c.transform; seat < 0 && pc == null && t != null; t = t.parent)
                if (t.TryGetComponent(out SeatOwner owner)) seat = owner.Seat;
            componentSeat[id] = seat;
            return seat;
        }
    }

    /// <summary>
    /// Põe a troca de contexto em volta do código dos componentes de um personagem: os métodos que a Unity chama
    /// (Start, Update...) e as corrotinas deles. Feito na primeira vez que um tipo aparece num personagem extra.
    /// </summary>
    static class ContextPatcher
    {
        static readonly string[] unityMessages = { "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable" };
        static readonly HashSet<Type> seen = new HashSet<Type>();
        static readonly Dictionary<Type, FieldInfo> iteratorOwner = new Dictionary<Type, FieldInfo>();
        static Harmony harmony;
        const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static void PatchTypesOf(GameObject root)
        {
            harmony = harmony ?? new Harmony(Plugin.Id + ".context");
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null) PatchType(mb.GetType());
        }

        static void PatchType(Type type)
        {
            for (Type t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(NetworkBehaviour); t = t.BaseType)
            {
                if (!seen.Add(t) || Skip(t)) continue;
                foreach (string name in unityMessages)
                {
                    MethodInfo m = t.GetMethod(name, Declared, null, Type.EmptyTypes, null);
                    if (m == null || m.IsAbstract) continue;
                    harmony.Patch(m, new HarmonyMethod(typeof(ContextPatcher), nameof(Prefix)), finalizer: new HarmonyMethod(typeof(ContextPatcher), nameof(Finalizer)));
                }
                // corrotinas: o código depois de cada "yield" também é do personagem
                foreach (Type nested in t.GetNestedTypes(BindingFlags.NonPublic))
                {
                    FieldInfo owner = nested.GetField("<>4__this", Declared);
                    MethodInfo moveNext = nested.GetMethod("MoveNext", Declared, null, Type.EmptyTypes, null);
                    if (owner == null || moveNext == null || !typeof(System.Collections.IEnumerator).IsAssignableFrom(nested)) continue;
                    iteratorOwner[nested] = owner;
                    harmony.Patch(moveNext, new HarmonyMethod(typeof(ContextPatcher), nameof(IteratorPrefix)), finalizer: new HarmonyMethod(typeof(ContextPatcher), nameof(Finalizer)));
                }
            }
        }

        // as peças da própria Unity e do Mirror não decidem nada pelo "jogador local"
        static bool Skip(Type t)
        {
            string ns = t.Namespace ?? "";
            return ns.StartsWith("UnityEngine") || ns.StartsWith("TMPro") || ns.StartsWith("Mirror") || ns.StartsWith("MicroWorksCoop");
        }

        static void Prefix(MonoBehaviour __instance, out SeatContext.Token __state)
        {
            int seat = SeatContext.SeatOf(__instance);
            __state = seat >= 0 ? SeatContext.Enter(seat) : default;
        }

        static void IteratorPrefix(object __instance, out SeatContext.Token __state)
        {
            __state = default;
            if (!iteratorOwner.TryGetValue(__instance.GetType(), out FieldInfo f)) return;
            int seat = SeatContext.SeatOf(f.GetValue(__instance) as Component);
            if (seat >= 0) __state = SeatContext.Enter(seat);
        }

        static Exception Finalizer(Exception __exception, SeatContext.Token __state)
        {
            SeatContext.Exit(__state);
            return __exception;
        }
    }
}
