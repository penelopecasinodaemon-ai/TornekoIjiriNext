using System;
using System.Collections.Generic;

namespace TornekoIjiriPort;

/// <summary>
/// スライムの指輪による味方専用特技ID取得処理。
/// 敵用ディスパッチャーには接続しない。
/// </summary>
internal static class SlimeRingAllyActionOverride
{
    private static readonly object Sync = new();

    private static Dictionary<int, ushort> _cachedActions = new();
    private static DateTime _lastLoad = DateTime.MinValue;

    /// <summary>
    /// 陣営判定関数のゲーム内アドレス。
    /// 呼び出し規約が確定するまでは直接呼び出さない。
    /// </summary>
    internal const uint FactionCheckAddress = 0x00236F94u;

    /// <summary>
    /// 指輪が有効で、対象モンスターに特技変更が設定されていれば
    /// 上書き先のActionIdを返す。
    /// </summary>
    internal static bool TryGetActionId(
        int speciesId,
        out ushort actionId)
    {
        actionId = 0;

        // 指輪が有効なときだけ処理する。
        if (!SlimeRingRuntimeService.IsOverlayActiveForMonster(speciesId))
            return false;

        // 対象17種以外には適用しない。
        if (Array.IndexOf(
            SlimeRingEditorForm.TargetMonsterIds,
            speciesId) < 0)
            return false;

        RefreshSettingsIfNeeded();

        lock (Sync)
        {
            if (!_cachedActions.TryGetValue(speciesId, out actionId))
                return false;
        }

        return actionId != 0;
    }

    /// <summary>
    /// 設定ファイルを読み込み、ActionIdをキャッシュする。
    /// </summary>
    internal static void RefreshSettingsIfNeeded(bool force = false)
    {
        lock (Sync)
        {
            DateTime now = DateTime.UtcNow;

            if (!force &&
                (now - _lastLoad).TotalMilliseconds < 1000)
                return;

            var settings = SlimeRingSettings.Load(
                SlimeRingEditorForm.TargetMonsterIds);

            var next = new Dictionary<int, ushort>();

            foreach (int speciesId in
                SlimeRingEditorForm.TargetMonsterIds)
            {
                var monster = settings.GetMonster(speciesId);

                if (monster.ActionId <= 0 ||
                    monster.ActionId > byte.MaxValue)
                    continue;

                bool valid = false;

                foreach (var choice in ActionAutoRestoreService.ActionChoices)
                {
                    if (choice.Id == monster.ActionId)
                    {
                        valid = true;
                        break;
                    }
                }

                if (valid)
                    next[speciesId] = (ushort)monster.ActionId;
            }

            _cachedActions = next;
            _lastLoad = now;
        }
    }

    /// <summary>
    /// 現在の設定を強制的に再読み込みする。
    /// </summary>
    internal static void Reload()
    {
        RefreshSettingsIfNeeded(true);
    }
}
