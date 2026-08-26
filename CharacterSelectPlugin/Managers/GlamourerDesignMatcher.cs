using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CharacterSelectPlugin.Managers
{
    // Which Glamourer design is the player wearing
    public static class GlamourerDesignMatcher
    {
        // No weapons, they swap with job
        private static readonly string[] MatchEquipSlots = { "Head", "Body", "Hands", "Legs", "Feet", "Ears", "Neck", "Wrists", "RFinger", "LFinger" };
        private static readonly (string Key, string ValueKey)[] MatchMetaToggles = { ("Hat", "Show"), ("VieraEars", "Show"), ("Visor", "IsToggled"), ("Weapon", "Show") };

        public static async Task<List<(string Name, Guid Id, float Score, int Fields)>> FindApplied()
        {
            var results = new List<(string Name, Guid Id, float Score, int Fields)>();

            var stateIpc = Plugin.PluginInterface.GetIpcSubscriber<int, uint, (int, JObject?)>("Glamourer.GetState");
            var (stateEc, state) = await Plugin.Framework.RunOnFrameworkThread(() => stateIpc.InvokeFunc(0, 0u));
            if (stateEc != 0 || state == null)
                return results;

            var listIpc = Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, string>>("Glamourer.GetDesignList.V2");
            var designs = await Task.Run(() => listIpc.InvokeFunc());
            if (designs == null || designs.Count == 0)
                return results;

            var designIpc = Plugin.PluginInterface.GetIpcSubscriber<Guid, JObject?>("Glamourer.GetDesignJObject");
            foreach (var kvp in designs)
            {
                try
                {
                    var design = await Task.Run(() => designIpc.InvokeFunc(kvp.Key));
                    if (design == null)
                        continue;

                    var (matched, total) = ScoreDesignAgainstState(design, state);
                    if (total == 0)
                        continue;

                    var name = design["Name"]?.Value<string>() ?? kvp.Value;
                    results.Add((name, kvp.Key, matched / (float)total, total));
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warning($"Failed to score design {kvp.Key}: {ex.Message}");
                }
            }

            return results
                .OrderByDescending(r => r.Score)
                .ThenByDescending(r => r.Fields)
                .ToList();
        }

        // Only fields the design applies count
        private static (int Matched, int Total) ScoreDesignAgainstState(JObject design, JObject state)
        {
            int matched = 0, total = 0;
            void Check(bool condition) { total++; if (condition) matched++; }

            var dEquip = design["Equipment"] as JObject;
            var sEquip = state["Equipment"] as JObject;
            if (dEquip != null && sEquip != null && sEquip["Array"] == null && dEquip["Array"] == null)
            {
                foreach (var slot in MatchEquipSlots)
                {
                    if (dEquip[slot] is not JObject dRow || sEquip[slot] is not JObject sRow)
                        continue;
                    if (dRow["Apply"]?.Value<bool>() == true)
                        Check(dRow["ItemId"]?.Value<ulong>() == sRow["ItemId"]?.Value<ulong>());
                    if (dRow["ApplyStain"]?.Value<bool>() == true)
                        Check((dRow["Stain"]?.Value<int>() ?? 0) == (sRow["Stain"]?.Value<int>() ?? 0)
                           && (dRow["Stain2"]?.Value<int>() ?? 0) == (sRow["Stain2"]?.Value<int>() ?? 0));
                    // Crests only apply on Head/Body/OffHand
                    if ((slot == "Head" || slot == "Body") && dRow["ApplyCrest"]?.Value<bool>() == true)
                        Check((dRow["Crest"]?.Value<bool>() ?? false) == (sRow["Crest"]?.Value<bool>() ?? false));
                }

                foreach (var (key, valueKey) in MatchMetaToggles)
                {
                    if (dEquip[key] is not JObject dMeta || sEquip[key] is not JObject sMeta)
                        continue;
                    if (dMeta["Apply"]?.Value<bool>() == true)
                        Check((dMeta[valueKey]?.Value<bool>() ?? false) == (sMeta[valueKey]?.Value<bool>() ?? false));
                }
            }

            if (design["Bonus"]?["Glasses"] is JObject dGlasses && state["Bonus"]?["Glasses"] is JObject sGlasses
                && dGlasses["Apply"]?.Value<bool>() == true)
                Check(dGlasses["BonusId"]?.Value<ulong>() == sGlasses["BonusId"]?.Value<ulong>());

            var dCust = design["Customize"] as JObject;
            var sCust = state["Customize"] as JObject;
            bool highlightsOff = (sCust?["Highlights"]?["Value"]?.Value<int>() ?? 0) == 0;
            if (dCust != null && sCust != null && sCust["Array"] == null && dCust["Array"] == null)
            {
                foreach (var prop in dCust.Properties())
                {
                    if (prop.Name == "ModelId" || prop.Value is not JObject dRow)
                        continue;
                    if (dRow["Apply"]?.Value<bool>() != true)
                        continue;
                    if (sCust[prop.Name] is not JObject sRow)
                        continue;
                    if (prop.Name == "Wetness")
                        Check((dRow["Value"]?.Value<bool>() ?? false) == (sRow["Value"]?.Value<bool>() ?? false));
                    else
                        Check((dRow["Value"]?.Value<int>() ?? 0) == (sRow["Value"]?.Value<int>() ?? 0));
                }
            }

            var dParams = design["Parameters"] as JObject;
            var sParams = state["Parameters"] as JObject;
            if (dParams != null && sParams != null)
            {
                foreach (var prop in dParams.Properties())
                {
                    if (prop.Value is not JObject dRow || dRow["Apply"]?.Value<bool>() != true)
                        continue;
                    // highlights off = HairDiffuse gets copied over this
                    if (prop.Name == "HairHighlight" && highlightsOff)
                        continue;
                    if (sParams[prop.Name] is not JObject sRow)
                        continue;
                    Check(FloatFieldsMatch(dRow, sRow));
                }
            }

            var dMats = design["Materials"] as JObject;
            var sMats = state["Materials"] as JObject;
            if (dMats != null)
            {
                foreach (var prop in dMats.Properties())
                {
                    if (prop.Value is not JObject dRow || dRow["Enabled"]?.Value<bool>() != true)
                        continue;
                    var sRow = sMats?[prop.Name] as JObject;
                    if (dRow["Revert"]?.Value<bool>() == true)
                        Check(sRow == null);
                    else
                        Check(sRow != null && FloatFieldsMatch(dRow, sRow));
                }
            }

            return (matched, total);
        }

        private static bool FloatFieldsMatch(JObject dRow, JObject sRow)
        {
            foreach (var prop in dRow.Properties())
            {
                if (prop.Name is "Apply" or "Enabled" or "Revert" or "Mode")
                    continue;
                if (prop.Value.Type != JTokenType.Float && prop.Value.Type != JTokenType.Integer)
                    continue;
                var sVal = sRow[prop.Name];
                if (sVal == null)
                    continue;
                if (Math.Abs(prop.Value.Value<float>() - sVal.Value<float>()) > 0.001f)
                    return false;
            }
            return true;
        }
    }
}
