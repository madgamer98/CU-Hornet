using System.Reflection;
using HarmonyLib;
using HornetPassthrough;
using InControl;

namespace HornetExporter
{
    /// <summary>
    /// S0 input round-trip: drive Hornet's real InControl actions from the host's raw buttons.
    ///
    /// Why these patches:
    /// - <c>InputManager.UpdateInternal</c> is skipped while the game is unfocused when
    ///   <c>SuspendInBackground</c> is set (InControl's default). A prefix clears the flag so
    ///   input keeps ticking while the host game holds focus.
    /// - <c>PlayerActionSet.Update</c> is where the device's bindings are resolved into the
    ///   <see cref="HeroActions"/> set each tick. A postfix runs right after and asserts the host's
    ///   pressed buttons, so <c>HeroController</c> sees them. The four direction actions are also
    ///   fed into <c>MoveVector</c>, which the set had already derived from the (idle) device this
    ///   tick — without recomputing it, HeroController's horizontal movement would stay zero.
    /// </summary>
    internal static class InputInjector
    {
        private static readonly MethodInfo MoveVectorUpdate =
            AccessTools.Method(typeof(PlayerTwoAxisAction), "Update");

        [HarmonyPatch(typeof(InputManager), "UpdateInternal")]
        internal static class KeepInputAliveWhileUnfocused
        {
            private static void Prefix()
            {
                InputManager.SuspendInBackground = false;
            }
        }

        [HarmonyPatch(typeof(PlayerActionSet), "Update")]
        internal static class InjectHostButtons
        {
            private static void Postfix(PlayerActionSet __instance, ulong updateTick, float deltaTime)
            {
                if (!(__instance is HeroActions ha))
                {
                    return;
                }

                int b = LiveLink.InjectedButtons;
                Commit(ha.Left, b, Proto.BtnLeft, updateTick, deltaTime);
                Commit(ha.Right, b, Proto.BtnRight, updateTick, deltaTime);
                Commit(ha.Up, b, Proto.BtnUp, updateTick, deltaTime);
                Commit(ha.Down, b, Proto.BtnDown, updateTick, deltaTime);

                // Recompute the derived vector from the direction actions just asserted.
                if (MoveVectorUpdate != null && ha.MoveVector != null)
                {
                    try
                    {
                        MoveVectorUpdate.Invoke(ha.MoveVector, new object[] { updateTick, deltaTime });
                    }
                    catch (TargetInvocationException)
                    {
                        // Ignore; movement simply trails a tick in that case.
                    }
                }

                Commit(ha.Jump, b, Proto.BtnJump, updateTick, deltaTime);
                Commit(ha.Attack, b, Proto.BtnAttack, updateTick, deltaTime);
                Commit(ha.Dash, b, Proto.BtnDash, updateTick, deltaTime);
                // Hornet's needle/harpoon throw is the SuperDash action in Silksong (QuickCast is the
                // tool/spell button and produced an "AirSphere Attack" instead).
                Commit(ha.SuperDash, b, Proto.BtnNeedle, updateTick, deltaTime);
            }

            private static void Commit(PlayerAction action, int buttons, int bit, ulong updateTick, float deltaTime)
            {
                if (action == null)
                {
                    return;
                }
                // Additive: only assert the host's press. Never commit a release, otherwise a host
                // holding nothing would cancel the real keyboard (breaking Silksong's own menus).
                if ((buttons & bit) == 0)
                {
                    return;
                }
                action.CommitWithValue(1f, updateTick, deltaTime);
            }
        }
    }
}
