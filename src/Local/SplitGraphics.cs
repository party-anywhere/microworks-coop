using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace MicroWorksCoop.Local
{
    /// <summary>
    /// Com a tela dividida o jogo desenha a cena uma vez por jogador: no vídeo integrado, no preset da pessoa, quatro
    /// jogadores já passam a ~20 quadros por segundo. Enquanto houver mais de um jogador na tela vale um preset do
    /// próprio jogo (QualidadeComVariosJogadores), só na memória; com um jogador só, voltam as configurações dela.
    /// </summary>
    static class SplitGraphics
    {
        static readonly List<(string name, object value)> personal = new List<(string, object)>();
        static bool active;

        /// <summary>Preset do jogo com a tela dividida ("" = as configurações da pessoa).</summary>
        public static string Preset = "";

        public static void Update(int players)
        {
            bool want = players > 1 && !string.IsNullOrEmpty(Preset);
            if (want == active) return;
            if (want) Apply();
            else Restore();
        }

        static void Apply()
        {
            var presets = AccessTools.Property(typeof(QualitySettingsHelper), "PresetsJSONObject").GetValue(null) as JObject;
            if (!(presets?[Preset] is JObject values))
            {
                Plugin.Log.LogWarning($"Preset de vídeo \"{Preset}\" não existe; a tela dividida fica com as suas configurações");
                Preset = "";
                return;
            }
            personal.Clear();
            // o mesmo que o QualitySettingsHelper.LoadPreset faz, sem gravar no fim, guardando o valor da pessoa
            foreach (JProperty p in values.Properties())
            {
                if (p.Value.Type == JTokenType.Boolean) Set(p.Name, p.Value.ToObject<bool>());
                else if (p.Value.Type == JTokenType.Integer) Set(p.Name, p.Value.ToObject<int>());
            }
            active = true;
            Plugin.Log.LogInfo($"Tela dividida: vídeo no preset {Preset} (só nesta sessão)");
        }

        static void Set<T>(string name, T value)
        {
            if (!CVars.Exists(name)) return;
            personal.Add((name, CVars.GetValue<T>(name)));
            CVars.SetValue(name, value, persist: false);
        }

        static void Restore()
        {
            foreach ((string name, object value) in personal) CVars.SetValue(name, value, persist: false);
            active = false;
            Plugin.Log.LogInfo("Um jogador na tela: vídeo de volta às suas configurações");
        }

        // o jogo grava todas as configurações de uma vez: grava as da pessoa, não as do preset da tela dividida
        [HarmonyPatch(typeof(CVars), nameof(CVars.SerializePersistentCVars))]
        static class SavePersonalSettings
        {
            static void Prefix(out bool __state)
            {
                __state = active;
                if (active) foreach ((string name, object value) in personal) CVars.SetValue(name, value, persist: false);
            }

            static void Postfix(bool __state)
            {
                if (!__state) return;
                active = false;
                Apply();
            }
        }
    }
}
