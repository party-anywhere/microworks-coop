using System;
using HarmonyLib;
using PartyAnywhere;
using Steamworks.Data;
using UnityEngine;

namespace MicroWorksCoop.Net
{
    // Com o Party Pass (é ele que libera sala privada) e gente no lobby do Party Anywhere, "Criar partida" já abre
    // como sala privada e sem VAC: só o sofá joga. Sem o Party Pass, o jogo só cria sala pública (qualquer um pela
    // internet pode entrar junto) e a tela fica como o jogo deixa: o mod não contorna o recurso pago.
    [HarmonyPatch(typeof(CreateServerMenuManager), "OnEnableMenu")]
    static class HostMenuDefaults
    {
        static void Postfix(CreateServerMenuManager __instance)
        {
            if (Plugin.Hub == null || Plugin.Hub.ActiveSeatCount == 0 || !Dlcs.PartyPass.OwnedByUser) return;
            __instance.isPrivateSwitch.isOn = true;
            __instance.VACSwitch.isOn = false;
        }
    }

    /// <summary>
    /// Para testar sem passar pelos menus: "-pa-dev-host" cria a partida assim que o menu
    /// principal carrega, pelo mesmo caminho do botão "Hospedar agora" (privada com o Party Pass, pública sem).
    /// </summary>
    class DevHost : MonoBehaviour
    {
        bool done;

        void Awake()
        {
            GlobalEvent<MainMenuFullyLoadedEvent>.AddListener(_ => Invoke(nameof(Host), 2f));
        }

        async void Host()
        {
            if (done) return;
            done = true;
            // o mesmo que o "Hospedar agora" faz depois de ler a tela, com os valores padrão do jogo
            GameLobbyData data = GameLobbyData.CreateDefault();
            data.name = "Party Anywhere (teste)";
            if (Dlcs.PartyPass.OwnedByUser)
            {
                data.maxPlayers = Client.MaxSeats;
                data.isPublic = false;
                data.VACProtected = false;
            }
            CVars.SetValue("dbg_iLogLevel", 3, persist: false); // o jogo passa a logar as respostas da API dele
            Plugin.Log.LogInfo("-pa-dev-host: criando a partida");
            try
            {
                Lobby lobby = await PersistentLobbyManager.CreateLobbyAsync(data);
                MicrogameCoordinator.singleton.HostGame(data.gamemode, lobby.MaxMembers, lobby.MemberCount);
            }
            catch (Exception e) { Plugin.Log.LogError($"-pa-dev-host: {e}"); }
        }
    }

    // para desenvolver: "-pa-skip-tutorial" (ou PularTutorial no .cfg) pula o tutorial sem gravar nada
    [HarmonyPatch(typeof(LaunchManager), "BeginLoading")]
    static class DevSkipTutorial
    {
        const int TutorialDone = 8;

        static void Prefix()
        {
            if (Plugin.DevSkipTutorial) CVars.SetValue("gam_iTutorialProgress", TutorialDone, persist: false);
        }
    }
}
