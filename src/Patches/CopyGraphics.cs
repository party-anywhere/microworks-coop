using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace MicroWorksCoop.Patches
{
    /// <summary>
    /// Com uma cópia por jogador, cada cópia desenha a cena inteira: no vídeo integrado, só uma cópia no "Alto"
    /// já não passa de poucos quadros por segundo. As cópias abertas pelo Hub usam um preset do próprio jogo
    /// (QualidadeNasCopias, ex.: "VeryLow") e o mesmo teto de quadros por segundo, só na memória: as configurações
    /// de vídeo da pessoa não mudam.
    /// </summary>
    // aplicado ao preparar o vídeo e de novo antes do menu: no primeiro uso de uma cópia, a detecção automática do
    // jogo aplica um preset depois do nosso
    [HarmonyPatch(typeof(QualitySettingsHelper), nameof(QualitySettingsHelper.Initialize))]
    static class CopyGraphicsOnInit
    {
        static void Postfix() => CopyGraphics.Apply();
    }

    [HarmonyPatch(typeof(LaunchManager), "BeginLoading")]
    static class CopyGraphicsBeforeMenu
    {
        static void Prefix() => CopyGraphics.Apply();
    }

    static class CopyGraphics
    {
        // todas as cópias estão em jogo, mas só uma janela tem o foco: sem isto, o jogo segura as outras no
        // limite de "janela em segundo plano" (gfx_iMaxFPSInactive) e elas caem para poucos quadros por segundo.
        // O mesmo teto para todas (uma cópia não toma a placa de vídeo das outras).
        const int CopyFps = 60;

        public static void Apply()
        {
            if (!Plugin.OpenedByHubAsCopy) return;
            CVars.SetValue("gfx_iMaxFPSActive", CopyFps, persist: false);
            CVars.SetValue("gfx_iMaxFPSInactive", CopyFps, persist: false);
            CVars.SetValue("gfx_bVSync", false, persist: false);
            string preset = Plugin.CopyQualityPreset;
            if (string.IsNullOrEmpty(preset)) return;
            var presets = AccessTools.Property(typeof(QualitySettingsHelper), "PresetsJSONObject").GetValue(null) as JObject;
            if (!(presets?[preset] is JObject values))
            {
                Plugin.Log.LogWarning($"Preset de vídeo \"{preset}\" não existe; as cópias ficam com as configurações de sempre");
                return;
            }
            // o mesmo que o QualitySettingsHelper.LoadPreset faz, sem gravar no fim
            foreach (JProperty p in values.Properties())
            {
                if (p.Value.Type == JTokenType.Boolean) CVars.SetValue(p.Name, p.Value.ToObject<bool>(), persist: false);
                else if (p.Value.Type == JTokenType.Integer) CVars.SetValue(p.Name, p.Value.ToObject<int>(), persist: false);
            }
            Plugin.Log.LogInfo($"Vídeo desta cópia: preset {preset} (só nesta sessão)");
        }
    }

    // O jogo grava todas as configurações de uma vez: na cópia que hospeda (a conta da pessoa), isso levaria o
    // preset leve para as configurações dela. Durante o co-op aberto pelo Hub, ela não grava; as outras cópias
    // têm arquivo próprio (SteamID de cópia).
    [HarmonyPatch(typeof(CVars), nameof(CVars.SerializePersistentCVars))]
    static class KeepPersonSettings
    {
        static bool Prefix() => !(Plugin.OpenedByHubAsCopy && Plugin.IsLeader);
    }
}
