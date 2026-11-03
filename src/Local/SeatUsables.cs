using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PartyAnywhere;
using UnityEngine;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// Os objetos da fase que um jogador usa e que não são de rede (a neve do chão no Congela-circuito, os botões de
    /// apertar, os monitores de número, a lixeira, o pato) vivem só no jogo de cada pessoa: pela internet, cada PC tem a
    /// sua cópia, com o seu estado ("estou enrolando a bola", "já apertei este botão"). Aqui havia uma cópia só, a de
    /// quem hospeda: a neve guardava um jogador só e lia o controle de quem hospeda (só o P1 conseguia fazer a bola), e
    /// um botão apertado pelo P1 ficava apertado para todos.
    ///
    /// Cada objeto passa a ter um estado por jogador do sofá, guardado à parte (como os desafios em SeatClients):
    /// - usar (Use) mexe no estado de quem usou, no contexto dele;
    /// - o código de um jogador do sofá mexe no estado dele;
    /// - o resto (a fase reativando os botões, o Update de cada quadro) roda no estado de quem hospeda e de novo no de
    ///   cada jogador do sofá, no contexto dele (pela internet, cada jogo rodaria esse código).
    /// O estado de cada um começa como o objeto estava antes de qualquer coisa mexer nele.
    /// </summary>
    static class SeatUsables
    {
        // o que a Unity chama uma vez por objeto: roda só no estado de quem hospeda
        static readonly HashSet<string> once = new HashSet<string> { "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy", "OnValidate" };

        class State
        {
            public object[] Pristine;
            public readonly object[][] Seats = new object[Client.MaxSeats][];
        }

        static readonly Dictionary<Type, FieldInfo[]> fieldsOf = new Dictionary<Type, FieldInfo[]>();
        static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        static readonly HashSet<string> failed = new HashSet<string>();
        static readonly Dictionary<int, int> depth = new Dictionary<int, int>(); // objeto → chamadas abertas nele

        public static void Install(Harmony harmony)
        {
            var prefix = new HarmonyMethod(AccessTools.Method(typeof(SeatUsables), nameof(Prefix)));
            var postfix = new HarmonyMethod(AccessTools.Method(typeof(SeatUsables), nameof(Postfix)));
            var finalizer = new HarmonyMethod(AccessTools.Method(typeof(SeatUsables), nameof(Finalizer)));
            Type[] types;
            try { types = typeof(IUsable).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
            int done = 0;
            var seen = new HashSet<Type>();
            foreach (Type type in types)
            {
                if (!typeof(IUsable).IsAssignableFrom(type) || !typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsAbstract) continue;
                // os de rede já são um só para todos (o servidor decide); o repassador não guarda nada
                if (typeof(Mirror.NetworkBehaviour).IsAssignableFrom(type) || type == typeof(UsableForwarder)) continue;
                for (Type t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    if (!seen.Add(t)) continue;
                    foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (m.IsAbstract || m.IsGenericMethodDefinition || m.GetMethodBody() == null) continue;
                        try { harmony.Patch(m, prefix: prefix, postfix: postfix, finalizer: finalizer); done++; }
                        catch (Exception e) { Plugin.Log.LogWarning($"Objeto usável {t.Name}.{m.Name}: {e.Message}"); }
                    }
                }
            }
            Plugin.Log.LogInfo($"Objetos usáveis da fase com estado por jogador: {done} métodos");
        }

        /// <summary>Fase nova: os objetos são outros.</summary>
        public static void Clear()
        {
            states.Clear();
            depth.Clear();
            hostSaved.Clear();
        }

        struct Call
        {
            public int Id;
            public int Seat;            // -1: no estado de quem hospeda
            public SeatContext.Token Token;
            public bool Outer;          // a primeira chamada neste objeto (as de dentro seguem o estado já carregado)
            public bool Replay;         // depois, repetir no estado de cada jogador do sofá
        }

        static void Prefix(MonoBehaviour __instance, MethodBase __originalMethod, object[] __args, out Call __state)
        {
            __state = default;
            if (__instance == null) return;
            int id = __instance.GetInstanceID();
            depth.TryGetValue(id, out int d);
            depth[id] = d + 1;
            __state.Id = id;
            if (d > 0) return;
            __state.Outer = true;
            State st = StateOf(__instance, id);
            int seat;
            if (__originalMethod.Name == nameof(IUsable.Use) && __args.Length == 1 && __args[0] is PlayerController user)
            {
                seat = SeatContext.SeatOf(user); // quem usou, seja quem for o contexto
                if (SeatDiag.On) Plugin.Log.LogInfo($"[diag] {SeatDiag.Who(user)} usou {__instance.GetType().Name}");
            }
            else if (SeatContext.IsExtra)
                seat = SeatContext.Seat;
            else
            {
                seat = -1;
                __state.Replay = !once.Contains(__originalMethod.Name) && !((MethodInfo)__originalMethod).IsSpecialName && Any(st);
            }
            __state.Seat = seat;
            if (seat < 0) return;
            __state.Token = SeatContext.Enter(seat);
            Swap(__instance, st, seat);
        }

        static void Postfix(MonoBehaviour __instance, MethodBase __originalMethod, object[] __args, Call __state)
        {
            if (!__state.Outer || !__state.Replay || __instance == null) return;
            State st = StateOf(__instance, __state.Id);
            for (int s = 0; s < st.Seats.Length; s++)
            {
                if (SeatContext.PlayerOf(s) == null) continue;
                SeatContext.Token t = SeatContext.Enter(s);
                Swap(__instance, st, s);
                try { __originalMethod.Invoke(__instance, __args); }
                catch (Exception e)
                {
                    Exception inner = e.InnerException ?? e;
                    if (failed.Add(__originalMethod.DeclaringType.Name + __originalMethod.Name))
                        Plugin.Log.LogWarning($"P{s + 1} em {__originalMethod.DeclaringType.Name}.{__originalMethod.Name}: {inner.GetType().Name}: {inner.Message}");
                }
                finally
                {
                    Unswap(__instance, st, s);
                    SeatContext.Exit(t);
                }
            }
        }

        static Exception Finalizer(Exception __exception, MonoBehaviour __instance, Call __state)
        {
            if (__state.Id == 0) return __exception;
            if (depth.TryGetValue(__state.Id, out int d)) { if (d <= 1) depth.Remove(__state.Id); else depth[__state.Id] = d - 1; }
            if (__state.Outer && __state.Seat >= 0 && __instance != null)
            {
                Unswap(__instance, StateOf(__instance, __state.Id), __state.Seat);
                SeatContext.Exit(__state.Token);
            }
            return __exception;
        }

        // ---- o estado de cada um ----

        static State StateOf(MonoBehaviour mb, int id)
        {
            if (states.TryGetValue(id, out State st)) return st;
            // a primeira vez que algo mexe no objeto: como ele está agora é como cada jogador o encontra
            st = new State { Pristine = Save(mb, Fields(mb.GetType()), null) };
            states[id] = st;
            return st;
        }

        static bool Any(State st)
        {
            for (int s = 0; s < st.Seats.Length; s++) if (SeatContext.PlayerOf(s) != null) return true;
            return false;
        }

        // guarda o de quem hospeda em "scratch" e põe o do jogador; chamadas aninhadas em outro objeto usam outro State
        static readonly Dictionary<int, object[]> hostSaved = new Dictionary<int, object[]>();

        static void Swap(MonoBehaviour mb, State st, int seat)
        {
            FieldInfo[] fields = Fields(mb.GetType());
            hostSaved[mb.GetInstanceID()] = Save(mb, fields, null);
            object[] mine = st.Seats[seat] ?? (st.Seats[seat] = (object[])st.Pristine.Clone());
            Load(mb, fields, mine);
        }

        static void Unswap(MonoBehaviour mb, State st, int seat)
        {
            int id = mb.GetInstanceID();
            if (!hostSaved.TryGetValue(id, out object[] host)) return;
            hostSaved.Remove(id);
            FieldInfo[] fields = Fields(mb.GetType());
            st.Seats[seat] = Save(mb, fields, st.Seats[seat]);
            Load(mb, fields, host);
        }

        static FieldInfo[] Fields(Type type)
        {
            if (fieldsOf.TryGetValue(type, out FieldInfo[] f)) return f;
            var list = new List<FieldInfo>();
            for (Type t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                foreach (FieldInfo fi in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!fi.IsInitOnly && !fi.IsLiteral) list.Add(fi);
            return fieldsOf[type] = list.ToArray();
        }

        static object[] Save(object obj, FieldInfo[] fields, object[] into)
        {
            if (into == null || into.Length != fields.Length) into = new object[fields.Length];
            for (int i = 0; i < fields.Length; i++) into[i] = fields[i].GetValue(obj);
            return into;
        }

        static void Load(object obj, FieldInfo[] fields, object[] from)
        {
            if (from == null || from.Length != fields.Length) return;
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(obj, from[i]);
        }
    }
}
