using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// O que a fase faz quando alguém encosta (a corda do "Pular a Corda" que mata, zonas, portas, botões de pisar)
    /// costuma perguntar "quem encostou é o jogador local?": pela internet, cada jogo cuida do seu jogador. Aqui só
    /// quem hospeda era local, e os jogadores do sofá passavam pela corda sem morrer. Quando um jogador do sofá
    /// encosta, o código da fase roda no contexto dele, como rodaria no jogo dele.
    ///
    /// Vale para os OnTriggerEnter/Stay/Exit de tudo o que não é de um personagem (o código dos personagens já roda no
    /// contexto do dono). Só os jogadores (camada 13) e os veículos deles (19, o ponto de controle do kart) contam: o
    /// resto passa direto, sem custo.
    /// </summary>
    static class SeatTriggers
    {
        const int LocalPlayerLayer = 13, PawnLayer = 19; // o jogador; o kart e o sapo
        static readonly string[] messages = { "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit" };

        /// <summary>Um método por vez: um que o Harmony não consiga mudar fica como o jogo fez, sem levar os outros.</summary>
        public static void Install(Harmony harmony)
        {
            var prefix = new HarmonyMethod(AccessTools.Method(typeof(SeatTriggers), nameof(Prefix)));
            var finalizer = new HarmonyMethod(AccessTools.Method(typeof(SeatTriggers), nameof(Finalizer)));
            int done = 0;
            foreach (MethodBase m in Targets())
            {
                try { harmony.Patch(m, prefix: prefix, finalizer: finalizer); done++; }
                catch (Exception e) { Plugin.Log.LogWarning($"Encostar em {m.DeclaringType.Name}: {e.Message}"); }
            }
            Plugin.Log.LogInfo($"Encostar no contexto de quem encostou: {done} métodos");
        }

        static IEnumerable<MethodBase> Targets()
        {
            Assembly game = typeof(PlayerController).Assembly;
            Type[] types;
            try { types = game.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
            foreach (Type t in types)
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(t) || t.IsAbstract || t.ContainsGenericParameters) continue;
                foreach (string name in messages)
                {
                    MethodInfo m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, new[] { typeof(Collider) }, null);
                    if (m != null && m.GetMethodBody() != null) yield return m;
                }
            }
        }

        static void Prefix(MonoBehaviour __instance, Collider __0, out SeatContext.Token __state)
        {
            __state = default;
            if (__0 == null || SeatContext.IsExtra || __instance == null) return;
            int layer = __0.gameObject.layer;
            if (layer != LocalPlayerLayer && layer != PawnLayer) return;
            PlayerController pc;
            if (layer == LocalPlayerLayer) pc = __0.GetComponent<PlayerController>();
            else { PlayerPawn pawn = __0.GetComponentInParent<PlayerPawn>(); pc = pawn != null ? pawn.Controller : null; }
            if (pc == null) return;
            int seat = SeatContext.SeatOf(pc);
            if (seat < 0) return;
            // o que é de um personagem ou do que ele controla já roda no contexto do dono
            if (__instance.GetComponentInParent<PlayerController>() != null || SeatContext.SeatOf(__instance) >= 0) return;
            __state = SeatContext.Enter(seat);
        }

        static Exception Finalizer(Exception __exception, SeatContext.Token __state)
        {
            SeatContext.Exit(__state);
            return __exception;
        }
    }
}
