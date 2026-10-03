using HarmonyLib;

namespace HornetExporter
{
    /// <summary>
    /// S4 jump fix. Injected input holds the Jump action across the ground jump, and HK's held-button
    /// double-jump path (`Jump.IsPressed` → `doubleJumpQueuing`) then fires the wing double jump
    /// immediately. Require the button to be released after a ground jump before a double jump is
    /// allowed. Wall jumps don't set the gate, so a double jump after a wall jump still works.
    /// </summary>
    internal static class JumpGating
    {
        /// <summary>True once Jump has been released since the last ground jump.</summary>
        public static bool ReleasedSinceGroundJump = true;
    }

    [HarmonyPatch(typeof(HeroController), "HeroJump", new System.Type[0])]
    internal static class HeroJumpPatch
    {
        private static void Postfix()
        {
            JumpGating.ReleasedSinceGroundJump = false;
        }
    }

    [HarmonyPatch(typeof(HeroController), "HeroJump", new System.Type[] { typeof(bool) })]
    internal static class HeroJumpSprintPatch
    {
        private static void Postfix()
        {
            JumpGating.ReleasedSinceGroundJump = false;
        }
    }

    [HarmonyPatch(typeof(HeroController), "HeroJumpNoEffect")]
    internal static class HeroJumpNoEffectPatch
    {
        private static void Postfix()
        {
            JumpGating.ReleasedSinceGroundJump = false;
        }
    }

    [HarmonyPatch(typeof(HeroController), "DoDoubleJump")]
    internal static class DoDoubleJumpPatch
    {
        private static bool Prefix()
        {
            return JumpGating.ReleasedSinceGroundJump;
        }
    }
}
