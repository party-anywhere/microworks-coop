using System;
using HarmonyLib;

namespace MicroWorksCoop.Net
{
    /// <summary>
    /// Para desenvolver: em vez do sorteio, os desafios (e o chefe) escolhidos no config do mod, em ordem e de novo, para
    /// testar um desafio com vários jogadores sem esperar ele cair. Vazio = o sorteio do jogo.
    /// </summary>
    static class DevForce
    {
        public static string[] Microgames = new string[0];
        public static string Boss = "";
        public static int RoundsUntilBoss;
        static int next = -1;
        static bool listed;

        // uma rodada nova (o servidor escolhe o próximo desafio): o próximo da lista; e o chefe mais cedo
        [HarmonyPatch(typeof(MicrogameCoordinator), "PreloadNextMicrogameForClients")]
        static class NextRound
        {
            static void Prefix(MicrogameCoordinator __instance)
            {
                next++;
                if (RoundsUntilBoss <= 0) return;
                uint skipTo = (uint)Math.Max(0, __instance.maxRounds.Value - RoundsUntilBoss);
                if (__instance.currentRound < skipTo) __instance.currentRound = skipTo;
            }
        }

        [HarmonyPatch(typeof(MicrogameSchemes), nameof(MicrogameSchemes.GetRandomMicrogame))]
        static class PickMicrogame
        {
            static void Postfix(MicrogameSchemes __instance, ref MicrogameBase __result)
            {
                if (Microgames.Length == 0) return;
                if (!listed)
                {
                    listed = true;
                    Plugin.Log.LogInfo("Desafios do jogo: " + string.Join(", ", Traverse.Create(__instance).Field("microgameItems").GetValue<System.Collections.IEnumerable>().Cast()));
                }
                string name = Microgames[Math.Max(0, next) % Microgames.Length];
                MicrogameBase forced = __instance.GetMicrogameByName(name);
                if (forced != null) __result = forced;
                else Plugin.Log.LogWarning($"Desafio forçado '{name}' não existe");
                Plugin.Log.LogInfo($"Desafio da rodada: {__result.Name}");
            }
        }

        [HarmonyPatch(typeof(VariedModifierContainer<BossStageSettings>), nameof(VariedModifierContainer<BossStageSettings>.Get))]
        static class PickBoss
        {
            // o Mono divide o código entre as versões do recipiente (chefes, regras da rodada): só a dos chefes
            static void Postfix(object __instance, ref object __result)
            {
                if (string.IsNullOrEmpty(Boss) || !(__instance is VariedModifierContainer<BossStageSettings>)) return;
                BossStageSettings[] all = UnityEngine.Resources.FindObjectsOfTypeAll<BossStageSettings>();
                BossStageSettings forced = Array.Find(all, b => b.BossStageName == Boss);
                if (forced != null) __result = forced;
                else Plugin.Log.LogWarning($"Chefe forçado '{Boss}' não existe; os chefes: " + string.Join(", ", Array.ConvertAll(all, b => b.BossStageName)));
                Plugin.Log.LogInfo($"Chefe: {(__result as BossStageSettings)?.BossStageName}");
            }
        }

        static System.Collections.Generic.IEnumerable<string> Cast(this System.Collections.IEnumerable items)
        {
            foreach (object item in items)
            {
                var value = Traverse.Create(item).Field("value").GetValue<MicrogameBase>();
                if (value != null) yield return value.Name.ToString();
            }
        }
    }
}
