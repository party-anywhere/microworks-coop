using System.Collections;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace MicroWorksCoop.Net
{
    /// <summary>
    /// Nas cópias 2 a 10: assim que o menu principal carrega e a cópia 1 está hospedando, entra na partida dela.
    /// Caiu ou a partida acabou, o jogo volta ao menu e a cópia entra de novo sozinha.
    /// </summary>
    class CopyJoiner : MonoBehaviour
    {
        const float CheckSeconds = 1f;

        static bool menuLoaded;
        float nextCheck;

        void Awake()
        {
            GlobalEvent<MainMenuFullyLoadedEvent>.AddListener(_ => menuLoaded = true);
        }

        void Update()
        {
            if (!Plugin.Active || !menuLoaded || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + CheckSeconds;
            NetworkManager nm = NetworkManager.singleton;
            if (nm == null || NetworkClient.active || NetworkServer.active || !LocalNet.HostIsReady()) return;
            menuLoaded = false; // até o menu carregar de novo (depois de uma queda)
            LocalNet.JoinLocalHost(nm);
        }
    }

    // as cópias extras pulam o tutorial e o aviso de conduta (sem gravar): a conta é a da cópia 1, que já mostra os
    // dois; a cópia extra só precisa chegar ao menu para entrar na partida
    [HarmonyPatch(typeof(LaunchManager), "BeginLoading")]
    static class SkipFirstRunInCopies
    {
        const int TutorialDone = 8;

        static void Prefix()
        {
            if (!Plugin.IsLeader || Plugin.DevSkipTutorial) CVars.SetValue("gam_iTutorialProgress", TutorialDone, persist: false);
            if (!Plugin.IsLeader) CVars.SetValue("gam_bSeenCodeOfConduct", true, persist: false);
        }
    }
}
