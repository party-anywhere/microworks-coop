using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace MicroWorksCoop.Patches
{
    /// <summary>
    /// O jogo trava um arquivo "lock" na pasta de dados para não abrir duas vezes. Cada cópia extra usa o
    /// próprio ("lock-p2", "lock-p3"...), e a cópia 1 continua com o original.
    /// </summary>
    [HarmonyPatch(typeof(LaunchManager), "Start")]
    static class SingleInstance
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            bool found = false;
            foreach (CodeInstruction ci in code)
            {
                if (ci.opcode == OpCodes.Ldstr && (string)ci.operand == "lock")
                {
                    found = true;
                    if (!Plugin.IsLeader) ci.operand = $"lock-p{Plugin.Seat + 1}";
                }
                yield return ci;
            }
            if (!found) Plugin.Log.LogWarning("Trava de cópia única não encontrada: o jogo mudou? Cópias extras podem não abrir");
        }
    }
}
