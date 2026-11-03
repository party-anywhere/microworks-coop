using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// Os chefes e desafios reagem ao que acontece com cada jogador ("o congelamento de fulano mudou", "fulano chegou na
    /// fogueira", "fulano subiu na plataforma") e decidem pelo "fulano é o jogador local?": pela internet, cada jogo
    /// reage pelo seu jogador (trava o movimento de quem congelou, enche a barra dele, mostra o ícone de fogo dele).
    /// Aqui a reação rodava só como quem hospeda: um jogador do sofá congelado seguia andando e a barra dele não enchia.
    ///
    /// Quando a reação é sobre um jogador do sofá, ela roda no contexto dele; as peças do HUD da partida que o chefe
    /// mexe (a barra, o texto, o ícone) passam a ser as da cópia do HUD dele (HudCopies) enquanto isso. O que só o
    /// servidor faz continua igual (o servidor é o mesmo).
    /// </summary>
    static class SeatCallbacks
    {
        static readonly Dictionary<Type, FieldInfo[]> uiFields = new Dictionary<Type, FieldInfo[]>();

        public static void Install(Harmony harmony)
        {
            var prefix = new HarmonyMethod(AccessTools.Method(typeof(SeatCallbacks), nameof(Prefix)));
            var finalizer = new HarmonyMethod(AccessTools.Method(typeof(SeatCallbacks), nameof(Finalizer)));
            Type[] types;
            try { types = typeof(BossStageBase).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
            int done = 0;
            foreach (Type t in types)
            {
                if (t.IsAbstract || !typeof(BossStageBase).IsAssignableFrom(t) && !typeof(MicrogameBase).IsAssignableFrom(t)) continue;
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.IsAbstract || m.IsGenericMethodDefinition || m.GetMethodBody() == null || PlayerArg(m) < 0) continue;
                    try { harmony.Patch(m, prefix: prefix, finalizer: finalizer); done++; }
                    catch (Exception e) { Plugin.Log.LogWarning($"Reação {t.Name}.{m.Name}: {e.Message}"); }
                }
            }
            Plugin.Log.LogInfo($"Reações dos chefes e desafios no contexto do jogador: {done} métodos");
        }

        static int PlayerArg(MethodInfo m)
        {
            ParameterInfo[] ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++) if (ps[i].ParameterType == typeof(PlayerController)) return i;
            return -1;
        }

        struct State
        {
            public SeatContext.Token Token;
            public List<(FieldInfo field, Object value)> Swapped;
            public object Instance;
        }

        static void Prefix(object __instance, MethodBase __originalMethod, object[] __args, out State __state)
        {
            __state = default;
            if (SeatContext.IsExtra || __instance == null) return;
            int i = PlayerArg((MethodInfo)__originalMethod);
            if (i < 0 || !(__args[i] is PlayerController pc)) return;
            int seat = SeatContext.SeatOf(pc);
            if (seat < 0) return;
            __state.Token = SeatContext.Enter(seat);
            __state.Instance = __instance;
            __state.Swapped = SwapHud(__instance, seat);
        }

        static Exception Finalizer(Exception __exception, State __state)
        {
            if (__state.Swapped != null)
                foreach ((FieldInfo f, Object v) in __state.Swapped) f.SetValue(__state.Instance, v);
            SeatContext.Exit(__state.Token);
            return __exception;
        }

        /// <summary>As peças do HUD da partida guardadas no objeto passam a ser as da cópia do jogador. Devolve o que trocou.</summary>
        public static List<(FieldInfo, Object)> SwapHud(object instance, int seat)
        {
            List<(FieldInfo, Object)> swapped = null;
            foreach (FieldInfo f in UiFields(instance.GetType()))
            {
                var value = f.GetValue(instance) as Object;
                Object twin = HudCopies.TwinOf(value, seat);
                if (twin == null) continue;
                (swapped ?? (swapped = new List<(FieldInfo, Object)>())).Add((f, value));
                f.SetValue(instance, twin);
            }
            return swapped;
        }

        static FieldInfo[] UiFields(Type type)
        {
            if (uiFields.TryGetValue(type, out FieldInfo[] f)) return f;
            var list = new List<FieldInfo>();
            for (Type t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                foreach (FieldInfo fi in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!fi.IsInitOnly && (typeof(Component).IsAssignableFrom(fi.FieldType) || fi.FieldType == typeof(GameObject))) list.Add(fi);
            return uiFields[type] = list.ToArray();
        }
    }
}
