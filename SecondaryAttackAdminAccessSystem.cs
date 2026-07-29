namespace SecondaryAttacks;

internal static class SecondaryAttackAdminAccessSystem
{
    internal static bool ShouldBypassPresetCooldowns(Character? character)
    {
        return SecondaryAttacksPlugin.AdminNoPresetCooldowns.Value == SecondaryAttacksPlugin.Toggle.On &&
               character is Player player &&
               player == Player.m_localPlayer &&
               SecondaryAttacksPlugin.ConfigSync.IsAdmin;
    }
}
