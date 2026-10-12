using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VSS;

[HarmonyPatch(typeof(Player), "TryPlacePiece")]
internal static class VssBuildingPatch
{
    private static readonly AccessTools.FieldRef<Player, GameObject> Ghost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
    private static readonly AccessTools.FieldRef<Player, float> LastTool = AccessTools.FieldRefAccess<Player, float>("m_lastToolUseTime");
    private static readonly AccessTools.FieldRef<Player, int> Debt = AccessTools.FieldRefAccess<Player, int>("m_buildRemoveDebt");
    private static readonly Func<Player, float> Stamina = AccessTools.MethodDelegate<Func<Player, float>>(AccessTools.Method(typeof(Player), "GetBuildStamina"));
    private static readonly Func<Player, ItemDrop.ItemData, float> Durability = AccessTools.MethodDelegate<Func<Player, ItemDrop.ItemData, float>>(AccessTools.Method(typeof(Player), "GetPlaceDurability"));
    private static bool Prefix(Player __instance, Piece piece, ref bool __result)
    {
        if (__instance != Player.m_localPlayer || VssResources.Current != null || __instance.NoCostCheat() || ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey())) return true;
        var stand = VssAccessStand.Find(__instance.transform.position, __instance.GetPlayerID());
        if (stand == null) return true;
        var context = new VssContext { Stand = stand, Player = __instance, Actor = __instance.GetPlayerID(), Mode = VssAccessMode.Resources };
        var chests = VssStorageScanner.Sources(context);
        var needs = VssMaterialPlan.Needs(__instance, piece.m_resources, 0, 1);
        var plan = VssMaterialPlan.Build(__instance.GetInventory(), chests.Select(c => c.GetInventory()), needs);
        if (plan == null) { __result = false; return false; }
        if (!plan.UsesStorage) return true;
        if (VssTransfer.Busy) { __result = false; return false; }
        var player = __instance; var tool = player.RightItem; var ghost = Ghost(player);
        if (tool == null || ghost == null) { __result = false; return false; }
        var position = ghost.transform.position; var rotation = ghost.transform.rotation;
        bool Guard() => player.GetSelectedPiece() == piece && player.RightItem == tool && Ghost(player) == ghost &&
            !InventoryGui.IsVisible() && ghost != null && (ghost.transform.position - position).sqrMagnitude < 0.0025f &&
            Quaternion.Angle(ghost.transform.rotation, rotation) < 1f && player.HaveStamina(Stamina(player));
        VssTransactions.Start(context, chests.Where(c => plan.ChestInventories.Contains(c.GetInventory())), Guard, locked =>
        {
            var payment = VssMaterialPlan.Build(player.GetInventory(), locked.Select(c => c.GetInventory()), needs);
            if (payment == null || !payment.Validate()) return false;
            payment.Player = player; payment.Requirements = piece.m_resources; VssResources.Current = payment;
            try
            {
                // Preserve native workbench, DLC and global-key checks after the ownership wait.
                if (!player.HaveRequirements(piece, Player.RequirementMode.CanBuild)) return false;
                if (!player.TryPlacePiece(piece)) return false;
                payment.Pay();
                Hud.instance?.m_buildUi.AddRecentPiece(piece);
                LastTool(player) = Time.time; player.UseStamina(Stamina(player));
                var table = tool.m_shared.m_buildPieces;
                if (table != null && table.m_skill != Skills.SkillType.None)
                {
                    if (table.m_skill == Skills.SkillType.Crafting) Game.instance.IncrementPlayerStat(PlayerStatType.BuiltPieces);
                    if (Debt(player) > 0) Debt(player)--;
                    else
                    {
                        player.RaiseSkill(table.m_skill);
                        if (table.m_skill == Skills.SkillType.Crafting) Game.instance.IncrementPlayerStat(PlayerStatType.BuiltPiecesNoDebt);
                    }
                }
                if (tool.m_shared.m_useDurability) tool.m_durability -= Durability(player, tool) * Game.m_durabilityRate;
                tool.m_shared.m_buildEffect.Create(player.transform.position, Quaternion.identity, null, 1f, -1, player.GetZDOID());
                return true;
            }
            finally { VssResources.Current = null; }
        }, error => { if (!string.IsNullOrEmpty(error) && player != null) player.Message(MessageHud.MessageType.TopLeft, error); });
        __result = false;
        return false;
    }
}
