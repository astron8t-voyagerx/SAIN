using System;
using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using EFT;
using SAIN.Components;
using SAIN.Helpers;
using SAIN.Layers;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace SAIN.Components;

/// <summary>
/// 봇 디버그 오버레이 컴포넌트 - 봇 머리 위에 정보 표시
/// </summary>
public class BotDebugComponent : MonoBehaviour
{
    private static BotDebugComponent _instance;
    public static BotDebugComponent Instance => _instance;

    private GUIStyle _guiStyle;
    private readonly Dictionary<string, BotOverlayData> _botOverlays = new();

    private class BotOverlayData
    {
        public BotComponent Bot;
        public string Text;
        public float LastUpdateTime;
    }

    private void Awake()
    {
        _instance = this;
    }

    private void OnDestroy()
    {
        _instance = null;
    }

    public void RegisterBot(BotComponent bot)
    {
        if (bot != null && !_botOverlays.ContainsKey(bot.ProfileId))
        {
            _botOverlays[bot.ProfileId] = new BotOverlayData { Bot = bot };
        }
    }

    public void UnregisterBot(string profileId)
    {
        _botOverlays.Remove(profileId);
    }

    private void Update()
    {
        if (!SAINPlugin.DebugSettings.Overlay.Overlay_Info)
        {
            return;
        }

        float currentTime = Time.time;
        List<string> toRemove = new();

        foreach (var kvp in _botOverlays)
        {
            var data = kvp.Value;
            if (data.Bot == null || data.Bot.IsDead)
            {
                toRemove.Add(kvp.Key);
                continue;
            }

            // 100ms 마다 업데이트
            if (currentTime - data.LastUpdateTime > 0.1f)
            {
                data.LastUpdateTime = currentTime;
                data.Text = BuildOverlayText(data.Bot);
            }
        }

        foreach (var key in toRemove)
        {
            _botOverlays.Remove(key);
        }
    }

    private void OnGUI()
    {
        if (!SAINPlugin.DebugSettings.Overlay.Overlay_Info)
        {
            return;
        }

        if (!Singleton<GameWorld>.Instantiated || Camera.main == null)
        {
            return;
        }

        if (_guiStyle == null)
        {
            _guiStyle = CreateGuiStyle();
        }

        Player mainPlayer = Singleton<GameWorld>.Instance?.MainPlayer;
        if (mainPlayer == null)
        {
            return;
        }

        foreach (var kvp in _botOverlays)
        {
            var data = kvp.Value;
            if (data.Bot == null || string.IsNullOrEmpty(data.Text))
            {
                continue;
            }

            // 거리 계산 (거리 제한 없음)
            float distance = Vector3.Distance(mainPlayer.Position, data.Bot.Position);

            DrawBotOverlay(data.Bot, data.Text, distance);
        }
    }

    private void DrawBotOverlay(BotComponent bot, string text, float distance)
    {
        Vector3 headPosition = bot.Position + new Vector3(0, 1.8f, 0);
        Vector3 screenPos = Camera.main.WorldToScreenPoint(headPosition);

        // 카메라 뒤에 있으면 표시 안 함
        if (screenPos.z <= 0)
        {
            return;
        }

        // 거리 정보 추가
        string fullText = text + $"\nDistance: {distance:F1}m";

        GUIContent content = new GUIContent(fullText);
        Vector2 size = _guiStyle.CalcSize(content);

        // 스크린 좌표 변환 (Unity GUI는 Y축이 반대)
        float x = screenPos.x - (size.x / 2);
        float y = Screen.height - screenPos.y - size.y;

        Rect rect = new Rect(x, y, size.x, size.y);
        GUI.Box(rect, content, _guiStyle);
    }

    private string BuildOverlayText(BotComponent bot)
    {
        StringBuilder sb = new();

        try
        {
            // 기본 정보
            sb.AppendLine($"[{bot.Player.Profile.Nickname}] {bot.Info.Profile.WildSpawnType}");
            sb.AppendLine($"Personality: {ColorizePersonality(bot.Info.Personality)}");

            // 적 정보
            var enemy = bot.GoalEnemy;
            if (enemy != null)
            {
                sb.AppendLine($"Enemy: {enemy.EnemyPlayer?.Profile.Nickname ?? "Unknown"}");
                sb.AppendLine($"  ThreatLevel: {ColorizeThreatLevel(enemy.ThreatLevel)} | Dist: {enemy.RealDistance:F1}m");
                sb.AppendLine($"  Visible: {ColorizeBoolean(enemy.IsVisible)} | CanShoot: {ColorizeBoolean(enemy.CanShoot)}");
            }
            else
            {
                sb.AppendLine("Enemy: <color=#888888>None</color>");
            }

            // 결정 정보
            sb.AppendLine($"Combat: <color=#FFA500>{bot.Decision.CurrentCombatDecision}</color>");
            sb.AppendLine($"Squad: <color=#00BFFF>{bot.Decision.CurrentSquadDecision}</color>");
            sb.AppendLine($"Self: <color=#DA70D6>{bot.Decision.CurrentSelfDecision}</color>");

            // 무기 정보
            var weaponInfo = bot.Info.WeaponInfo;
            if (weaponInfo?.CurrentWeapon != null)
            {
                sb.AppendLine($"FireMode: <color=#FFFF00>{weaponInfo.SelectedFireMode}</color>");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"<color=#FF0000>Error: {ex.Message}</color>");
        }

        return sb.ToString();
    }

    private static string ColorizeThreatLevel(EEnemyThreatLevel threatLevel)
    {
        return threatLevel switch
        {
            EEnemyThreatLevel.Low => "<color=#00FF00>Low</color>",      // 초록색 - 낮은 위협
            EEnemyThreatLevel.High => "<color=#FF4444>High</color>",   // 빨간색 - 높은 위협
            _ => threatLevel.ToString()
        };
    }

    private static string ColorizePersonality(EPersonality personality)
    {
        return personality switch
        {
            EPersonality.GigaChad => "<color=#FF0000>GigaChad</color>",     // 빨간색 - 매우 공격적
            EPersonality.Chad => "<color=#FF6600>Chad</color>",             // 주황색 - 공격적
            EPersonality.Wreckless => "<color=#FF9900>Wreckless</color>",   // 주황-노랑 - 무모함
            EPersonality.Rat => "<color=#808080>Rat</color>",               // 회색 - 은밀
            EPersonality.Coward => "<color=#AAAAAA>Coward</color>",         // 연회색 - 겁쟁이
            EPersonality.Timmy => "<color=#87CEEB>Timmy</color>",           // 하늘색 - 초보
            EPersonality.Normal => "<color=#FFFFFF>Normal</color>",         // 흰색 - 보통
            EPersonality.SnappingTurtle => "<color=#228B22>SnappingTurtle</color>", // 숲녹색 - 방어적 공격
            _ => $"<color=#FFFFFF>{personality}</color>"
        };
    }

    private static string ColorizeBoolean(bool value)
    {
        return value
            ? "<color=#00FF00>True</color>"
            : "<color=#FF4444>False</color>";
    }

    private GUIStyle CreateGuiStyle()
    {
        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 12,
            richText = true
        };
        style.normal.textColor = Color.white;
        style.margin = new RectOffset(3, 3, 3, 3);
        style.padding = new RectOffset(5, 5, 5, 5);

        return style;
    }
}
