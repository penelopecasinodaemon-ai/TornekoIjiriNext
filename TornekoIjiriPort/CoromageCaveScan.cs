using System;
using System.Collections.Generic;
using System.Text;

namespace TornekoIjiriPort;

internal static class CoromageCaveScan
{
    private const uint StartAddress = 0x01C00000u;
    private const uint EndAddress = 0x01EFFFFFu;
    private const uint Step = 0x00010000u;
    private const uint RequiredSize = 0x600u;

    public static string Run()
    {
        uint status = ActionAutoRestoreService.GetSharedStatus();

        if (status != 1u)
        {
            throw new InvalidOperationException(
                "PCSX2を一時停止してから実行してください。");
        }

        List<uint> candidates = new List<uint>();
        List<string> occupied = new List<string>();

        int checkedRegions = 0;

        for (uint address = StartAddress; address <= EndAddress; address += Step)
        {
            checkedRegions++;

            bool free = true;
            uint firstNonZero = 0u;
            uint firstValue = 0u;

            for (uint offset = 0; offset < RequiredSize; offset += 4u)
            {
                uint value = ActionAutoRestoreService.ReadShared32(address + offset);

                if (value != 0u)
                {
                    free = false;
                    firstNonZero = address + offset;
                    firstValue = value;
                    break;
                }
            }

            if (free)
            {
                candidates.Add(address);
            }
            else
            {
                occupied.Add(
                    "0x" + address.ToString("X8") +
                    " 最初の非ゼロ位置=0x" +
                    firstNonZero.ToString("X8") +
                    " 値=0x" + firstValue.ToString("X8"));
            }
        }

        StringBuilder report = new StringBuilder();

        report.AppendLine("COROMAGE CAVE SCAN");
        report.AppendLine("READ ONLY");
        report.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine("PCSX2 status: PAUSED");
        report.AppendLine("Start: 0x" + StartAddress.ToString("X8"));
        report.AppendLine("End: 0x" + EndAddress.ToString("X8"));
        report.AppendLine("Required size: 0x" + RequiredSize.ToString("X"));
        report.AppendLine("Checked regions: " + checkedRegions);
        report.AppendLine("Free candidates: " + candidates.Count);
        report.AppendLine("");

        report.AppendLine("===== FREE CANDIDATES =====");

        if (candidates.Count == 0)
        {
            report.AppendLine("No candidate found.");
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            report.AppendLine(
                (i + 1) + ": 0x" + candidates[i].ToString("X8"));
        }

        report.AppendLine("");
        report.AppendLine("===== OCCUPIED REGIONS =====");

        foreach (string item in occupied)
        {
            report.AppendLine(item);
        }

        report.AppendLine("");
        report.AppendLine("Memory write operations: 0");

        return report.ToString();
    }
}