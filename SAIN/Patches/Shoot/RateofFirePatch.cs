using System.Reflection;
using System.Text;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SAIN;
using SAIN.Components;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SPT.Reflection.Patching;
using UnityEngine;
using static SAIN.Helpers.Shoot;

namespace SAIN.Patches.Shoot.RateOfFire;

public class BotShootPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ShootData), nameof(ShootData.Shoot));
    }

    [PatchPrefix]
    public static bool PatchPrefix(ShootData __instance, ref bool __result)
    {
        BotOwner botOwner = __instance.Owner;
        if (!SAINEnableClass.GetSAIN(botOwner.ProfileId, out BotComponent bot))
        {
            return true;
        }
        __result = false;
        if (__instance.ShootController == null)
        {
            return false;
        }
        BotUnderbarrelLauncherController underbarrelLauncherController = botOwner.WeaponManager.UnderbarrelLauncherController;
        if (underbarrelLauncherController.IsActive)
        {
            if (underbarrelLauncherController.NeedToReload() && !underbarrelLauncherController.TryReload(null))
            {
                underbarrelLauncherController.TryDisable(null);
                return false;
            }
            if (!underbarrelLauncherController.CheckShootAttemptAndDisableIfNeeded())
            {
                return false;
            }
            __instance.NextFingerDownCan = Time.time - 0.1f;
        }
        if (!__instance.Shooting && __instance.NextFingerDownCan < Time.time)
        {
            // Low Threat (Scav) 상대 시: DogFight면 즉시 발사, 아니면 AimComplete 필요
            var enemy = bot.GoalEnemy;
            if (enemy != null && enemy.ThreatLevel == EEnemyThreatLevel.Low)
            {
                // DogFight 상태 확인 (근거리 격투 상황)
                bool isDogFight = bot.Decision.CurrentCombatDecision == ECombatDecision.DogFight;

                if (!isDogFight)
                {
                    // DogFight가 아니면 조준 완료(AimComplete) 필요
                    var aimingData = botOwner.AimingManager?.CurrentAiming;
                    if (aimingData is BotAimingClass botAiming)
                    {
                        if (botAiming.Status != AimStatus.AimComplete)
                        {
                            // 조준 미완료 - 발사하지 않음
                            return false;
                        }
                    }
                }
                // DogFight면 조준 미완료도 허용 (긴급 상황)
            }

            bool fullAuto = bot.Info.WeaponInfo.SelectedFireMode == Weapon.EFireMode.fullauto;
            if (fullAuto)
            {
                __instance.NextFingerUpTime = Time.time + FullAutoBurstLength(bot, bot.DistanceToAimTarget);
            }

            __instance.NextFingerDownCan = Time.time + bot.Info.WeaponInfo.Firerate.CalcFirerateInterval();
            __instance.Shooting = true;
            __instance.TimeFingerDown = Time.time;
            __instance.LastTriggerPressd = Time.time;
            __instance.ShootController.IsInLauncherMode();
            __instance.ShootController.SetTriggerPressed(true);
            botOwner.AimingManager.CurrentAiming.TriggerPressedDone();
            __result = true;

            // Low Threat (Scav) 상대 시 발사 로그 출력
            LogLowThreatShot(bot, botOwner);

            return false;
        }
        return false;
    }

    /// <summary>
    /// Low Threat (Scav) 또는 Player 상대 시 발사할 때마다 자세한 로그 출력
    /// </summary>
    private static void LogLowThreatShot(BotComponent bot, BotOwner botOwner)
    {
        try
        {
            var enemy = bot.GoalEnemy;
            if (enemy == null)
            {
                return;
            }

            // Low Threat (Scav) 또는 Player (사람) 상대 시에만 로그 출력
            bool isLowThreat = enemy.ThreatLevel == EEnemyThreatLevel.Low;
            bool isHumanPlayer = !enemy.IsAI;
            if (!isLowThreat && !isHumanPlayer)
            {
                return;
            }

            var aimingData = botOwner.AimingManager?.CurrentAiming;
            if (aimingData == null)
            {
                return;
            }

            StringBuilder log = new StringBuilder();
            string targetType = isHumanPlayer ? "PLAYER" : "LOW_THREAT";
            log.AppendLine($"========== [SAIN] SHOT LOG ({targetType}) ==========");

            // 기본 정보
            log.AppendLine($"[Shooter] {bot.Player.Profile.Nickname} ({bot.Info.Profile.WildSpawnType})");
            log.AppendLine($"[Target] {enemy.EnemyPlayer?.Profile.Nickname ?? "Unknown"} (ThreatLevel: {enemy.ThreatLevel}, IsAI: {enemy.IsAI})");
            log.AppendLine($"[Distance] {enemy.RealDistance:F2}m");

            // Visible 및 CanShoot 정보
            log.AppendLine($"[Visible] {enemy.IsVisible} | [CanShoot] {enemy.CanShoot}");

            // 전투 상태 정보
            bool isDogFight = bot.Decision.CurrentCombatDecision == ECombatDecision.DogFight;
            log.AppendLine($"[DogFight Mode] {isDogFight} (긴급 상황 - 조준 미완료도 발사 허용)");

            // 적 머리 위치
            var headPart = enemy.EnemyPlayer?.MainParts[BodyPartType.head];
            Vector3 headPosition = headPart?.Position ?? Vector3.zero;
            log.AppendLine($"[Enemy Head Position] {FormatVector3(headPosition)}");

            // 조준 부위 정보 (RealTargetPoint와 머리 위치 비교로 추정)
            float headYDiff = headPosition.y - aimingData.RealTargetPoint.y;
            string aimingAt = headYDiff < 0.1f && headYDiff > -0.3f ? "HEAD (추정)" : "BODY (추정)";
            log.AppendLine($"[Aiming At] {aimingAt} (Y diff: {headYDiff:F2}m)");

            // 조준 정보
            Vector3 realTargetPoint = aimingData.RealTargetPoint;
            Vector3 endTargetPoint = aimingData.EndTargetPoint;
            Vector3 aimOffset = endTargetPoint - realTargetPoint;
            float offsetMagnitude = aimOffset.magnitude;

            log.AppendLine($"[RealTargetPoint] {FormatVector3(realTargetPoint)} (원래 조준점)");
            log.AppendLine($"[EndTargetPoint] {FormatVector3(endTargetPoint)} (실제 발사 방향)");
            log.AppendLine($"[Aim Offset] {FormatVector3(aimOffset)} (Magnitude: {offsetMagnitude:F4}m)");

            // 머리와 실제 발사점 사이 거리
            float headToEndDistance = (headPosition - endTargetPoint).magnitude;
            log.AppendLine($"[Head to EndTarget Distance] {headToEndDistance:F4}m");

            // 각도 정보
            Vector3 weaponPos = botOwner.WeaponRoot?.position ?? bot.Position;
            Vector3 dirToHead = (headPosition - weaponPos).normalized;
            Vector3 dirToEnd = (endTargetPoint - weaponPos).normalized;
            float angleDiff = Vector3.Angle(dirToHead, dirToEnd);
            log.AppendLine($"[Angle Difference] {angleDiff:F2}° (머리 방향 vs 실제 발사 방향)");

            // Scatter 및 정확도 정보
            float aimAndScatterMulti = enemy.Aim?.AimAndScatterMultiplier ?? 1f;
            log.AppendLine($"[AimAndScatterMultiplier] {aimAndScatterMulti:F3}");

            // EFT 설정값
            float accuracySpeed = botOwner.Settings?.Current?.CurrentAccuratySpeed ?? 0f;
            log.AppendLine($"[AccuracySpeed] {accuracySpeed:F3}");

            // 조준 상태
            string aimStatus = "Unknown";
            if (aimingData is BotAimingClass botAiming)
            {
                aimStatus = botAiming.Status.ToString();
            }
            log.AppendLine($"[AimStatus] {aimStatus}");

            // 무기 정보
            var weaponInfo = bot.Info.WeaponInfo;
            log.AppendLine($"[FireMode] {weaponInfo?.SelectedFireMode}");
            log.AppendLine($"[FirerateInterval] {weaponInfo?.Firerate?.CalcFirerateInterval():F3}s");

            // 봇 이동 상태
            bool isMoving = botOwner.Mover?.IsMoving ?? false;
            log.AppendLine($"[Bot Moving] {isMoving}");

            // 현재 결정
            log.AppendLine($"[Combat Decision] {bot.Decision.CurrentCombatDecision}");

            log.AppendLine("==================================================");

            // BepInEx 로그로 출력
            Logger.LogInfo(log.ToString());
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[SAIN] LogLowThreatShot Error: {ex.Message}");
        }
    }

    private static string FormatVector3(Vector3 v)
    {
        return $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
    }
}
