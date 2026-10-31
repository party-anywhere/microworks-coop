using HarmonyLib;
using Luminosity.IO;
using MicroWorksCoop.Input;
using UnityEngine;

namespace MicroWorksCoop.Patches
{
    // Enquanto a vaga desta cópia tem alguém (PadInput.Routing), o InputManager responde só com o controle
    // dela: teclado, mouse e controles do PC não mexem em nenhuma cópia. Sem Hub ou com a vaga vazia, o jogo
    // lê o teclado e o mouse como sempre.

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetAxis))]
    static class GetAxisPatch
    {
        static bool Prefix(string name, ref float __result)
        {
            if (!PadInput.Routing) return true;
            PadInput.TryAxis(name, out __result);
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetAxisRaw))]
    static class GetAxisRawPatch
    {
        static bool Prefix(string name, ref float __result)
        {
            if (!PadInput.Routing) return true;
            PadInput.TryAxis(name, out __result);
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetButton))]
    static class GetButtonPatch
    {
        static bool Prefix(string name, ref bool __result)
        {
            if (!PadInput.Routing) return true;
            PadInput.TryButton(name, Edge.Held, out __result);
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.IsInputHeld))]
    static class IsInputHeldPatch
    {
        static bool Prefix(string name, ref bool __result)
        {
            if (!PadInput.Routing) return true;
            PadInput.TryButton(name, Edge.Held, out __result);
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetButtonDown))]
    static class GetButtonDownPatch
    {
        static bool Prefix(string name, ref bool __result)
        {
            if (!PadInput.Routing) return true;
            PadInput.TryButton(name, Edge.Down, out __result);
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetButtonUp))]
    static class GetButtonUpPatch
    {
        static bool Prefix(string name, ref bool __result)
        {
            if (!PadInput.Routing) return true;
            PadInput.TryButton(name, Edge.Up, out __result);
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetKey))]
    static class GetKeyPatch
    {
        static bool Prefix(KeyCode key, ref bool __result)
        {
            if (!PadInput.Routing) return true;
            __result = PadInput.Key(key, Edge.Held);
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetKeyDown))]
    static class GetKeyDownPatch
    {
        static bool Prefix(KeyCode key, ref bool __result)
        {
            if (!PadInput.Routing) return true;
            __result = PadInput.Key(key, Edge.Down);
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetKeyUp))]
    static class GetKeyUpPatch
    {
        static bool Prefix(KeyCode key, ref bool __result)
        {
            if (!PadInput.Routing) return true;
            __result = PadInput.Key(key, Edge.Up);
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetMouseButton))]
    static class GetMouseButtonPatch
    {
        static bool Prefix(int index, ref bool __result)
        {
            if (!PadInput.Routing) return true;
            __result = PadInput.Mouse(index, Edge.Held);
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetMouseButtonDown))]
    static class GetMouseButtonDownPatch
    {
        static bool Prefix(int index, ref bool __result)
        {
            if (!PadInput.Routing) return true;
            __result = PadInput.Mouse(index, Edge.Down);
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetMouseButtonUp))]
    static class GetMouseButtonUpPatch
    {
        static bool Prefix(int index, ref bool __result)
        {
            if (!PadInput.Routing) return true;
            __result = PadInput.Mouse(index, Edge.Up);
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.anyKey), MethodType.Getter)]
    static class AnyKeyPatch
    {
        static bool Prefix(ref bool __result)
        {
            if (!PadInput.Routing) return true;
            __result = PadInput.AnyKey(down: false);
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.anyKeyDown), MethodType.Getter)]
    static class AnyKeyDownPatch
    {
        static bool Prefix(ref bool __result)
        {
            if (!PadInput.Routing) return true;
            __result = PadInput.AnyKey(down: true);
            return false;
        }
    }

    // os menus do jogo seguem o mouse: nesta cópia, seguem o cursor virtual dela (o mouse do PC fica livre)
    [HarmonyPatch(typeof(InputManager), nameof(InputManager.mousePosition), MethodType.Getter)]
    static class MousePositionPatch
    {
        static bool Prefix(ref Vector2 __result)
        {
            if (!PadInput.Routing) return true;
            __result = PadInput.CursorPosition;
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.mouseScrollDelta), MethodType.Getter)]
    static class MouseScrollPatch
    {
        static bool Prefix(ref Vector2 __result)
        {
            if (!PadInput.Routing) return true;
            __result = PadInput.Scroll;
            return false;
        }
    }
}
