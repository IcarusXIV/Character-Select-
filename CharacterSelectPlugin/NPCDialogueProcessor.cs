using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Dalamud.Hooking;
using Dalamud.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.STD;
using Dalamud.Plugin.Services;
using System.Runtime.InteropServices;
using System.Linq;
using System.IO;

namespace CharacterSelectPlugin
{
    [StructLayout(LayoutKind.Explicit, Size = 0x20)]
    public struct TextDecoderParam
    {
        [FieldOffset(0x00)] public ulong Value;
        [FieldOffset(0x08)] public nuint Self;
        [FieldOffset(0x16)] public sbyte Status;
    }

    public unsafe class NPCDialogueProcessor : IDisposable
    {
        private readonly Plugin plugin;
        private readonly ISigScanner sigScanner;
        private readonly IGameInteropProvider gameInteropProvider;
        private readonly IChatGui chatGui;
        private readonly IClientState clientState;
        private readonly IPluginLog log;
        private readonly ICondition condition;
        private static int debugLogCount = 0;
        private const bool ENABLE_FLAG_DISCOVERY_LOGGING = false;

        // Lua hooks
        private delegate int GetStringPrototype(RaptureTextModule* textModule, byte* text, void* decoder, Utf8String* stringStruct);
        private Hook<GetStringPrototype>? getStringHook;

        private delegate byte GetLuaVarPrototype(nint poolBase, nint a2, nint a3);
        private Hook<GetLuaVarPrototype>? getLuaVarHook;

        // Audio/voice gender hook
        private delegate int GetCutVoGenderPrototype(nint a1, nint a2);
        private Hook<GetCutVoGenderPrototype>? getCutVoGenderHook;

        // Text processing hook
        private delegate nint ProcessTextPrototype(nint textPtr, nint length);
        private Hook<ProcessTextPrototype>? processTextHook;

        // Name replacement byte patterns
        private static readonly byte[] FullNameBytes = { 0x02, 0x29, 0x03, 0xEB, 0x02, 0x03 };
        private static readonly byte[] FirstNameBytes = { 0x02, 0x2C, 0x0D, 0xFF, 0x07, 0x02, 0x29, 0x03, 0xEB, 0x02, 0x03, 0xFF, 0x02, 0x20, 0x02, 0x03 };
        private static readonly byte[] LastNameBytes = { 0x02, 0x2C, 0x0D, 0xFF, 0x07, 0x02, 0x29, 0x03, 0xEB, 0x02, 0x03, 0xFF, 0x02, 0x20, 0x03, 0x03 };

        private byte[] ProcessGenderFlags(byte[] data, Character character)
        {
            var pronounSet = PronounParser.Parse(character.RPProfile?.Pronouns ?? "");
            bool isTheyThem = pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase);
            
            // log.Info($"[ProcessGenderFlags] Called for pronouns: {character.RPProfile?.Pronouns} | ReplacePronounsInDialogue: {plugin.Configuration.ReplacePronounsInDialogue}");
            
            if (!plugin.Configuration.ReplacePronounsInDialogue)
                return data;
            
            try
            {
                var result = new List<byte>();
                int pos = 0;
                
                while (pos < data.Length)
                {
                    // Flag pattern: 02 08 [ID] E9 05 FF
                    if (pos + 10 < data.Length &&
                        data[pos] == 0x02 &&
                        data[pos + 1] == 0x08 &&
                        data[pos + 3] == 0xE9 &&
                        data[pos + 4] == 0x05 &&
                        data[pos + 5] == 0xFF)
                    {
                        var flagInfo = ExtractFlagWords(data, pos);
                        if (flagInfo != null)
                        {
                            var flagId = data[pos + 2];
                            log.Info($"[FLAG] Processing flag 0x{flagId:X2} with '{flagInfo.FemaleWord}' / '{flagInfo.MaleWord}'");

                            if (ENABLE_FLAG_DISCOVERY_LOGGING)
                            {
                                LogFlagDiscovery(flagId, flagInfo, data, pos);
                            }

                            var neutralTitle = plugin.Configuration.GetGenderNeutralTitle();
                            string replacement = GetCorrectPronounForFlag(flagInfo, pronounSet, neutralTitle);

                            result.AddRange(System.Text.Encoding.UTF8.GetBytes(replacement));
                            pos = SkipPastFlagPattern(data, pos);
                            continue;
                        }
                    }

                    result.Add(data[pos]);
                    pos++;
                }
                
                return result.ToArray();
            }
            catch (Exception ex)
            {
                log.Error($"[FLAG] Error in ProcessGenderFlags: {ex.Message}");
                return data;
            }
        }

        private FlagInfo? ExtractFlagWords(byte[] data, int startIndex)
        {
            try
            {
                int pos = startIndex + 6;
                if (pos >= data.Length) return null;
                int femaleLen = data[pos++];
                if (pos + femaleLen + 3 >= data.Length) return null;

                var femaleBytes = new byte[femaleLen];
                Array.Copy(data, pos, femaleBytes, 0, femaleLen);
                var femaleWord = System.Text.Encoding.UTF8.GetString(femaleBytes);
                pos += femaleLen;

                if (pos >= data.Length || data[pos] != 0xFF)
                {
                    log.Info($"[FLAG EXTRACT DEBUG] Alternative parsing attempt for flag at {startIndex}");
                    var altResult = TryAlternativeFlagExtraction(data, startIndex);
                    if (altResult != null)
                    {
                        log.Info($"[FLAG EXTRACT DEBUG] Alternative extraction returned: '{altResult.FemaleWord}' / '{altResult.MaleWord}'");
                    }
                    return altResult;
                }
                pos++;

                if (pos >= data.Length) return null;
                int maleLen = data[pos++];
                if (pos + maleLen + 1 >= data.Length) return null;

                var maleBytes = new byte[maleLen];
                Array.Copy(data, pos, maleBytes, 0, maleLen);
                var maleWord = System.Text.Encoding.UTF8.GetString(maleBytes);
                
                return new FlagInfo { FemaleWord = femaleWord, MaleWord = maleWord };
            }
            catch (Exception ex)
            {
                log.Error($"[FLAG EXTRACT DEBUG] Exception: {ex.Message}");
                return null;
            }
        }

        private FlagInfo? TryAlternativeFlagExtraction(byte[] data, int startIndex)
        {
            try
            {
                // Hex format: 02 08 0D E9 05 FF 04 68 65 72 FF 04 68 69 73 03
                int pos = startIndex + 6;
                if (pos >= data.Length) return null;

                int femaleLen = data[pos++];
                if (pos + femaleLen >= data.Length) return null;

                var femaleBytes = new byte[femaleLen - 1];
                Array.Copy(data, pos, femaleBytes, 0, femaleLen - 1);
                var femaleWord = System.Text.Encoding.UTF8.GetString(femaleBytes);
                pos += femaleLen;

                if (pos >= data.Length) return null;
                int maleLen = data[pos++];
                if (pos + maleLen >= data.Length) return null;

                var maleBytes = new byte[maleLen - 1];
                Array.Copy(data, pos, maleBytes, 0, maleLen - 1);
                var maleWord = System.Text.Encoding.UTF8.GetString(maleBytes);
                
                log.Info($"[FLAG EXTRACT DEBUG] Alternative extraction success: '{femaleWord}' / '{maleWord}'");
                return new FlagInfo { FemaleWord = femaleWord, MaleWord = maleWord };
            }
            catch (Exception ex)
            {
                log.Error($"[FLAG EXTRACT DEBUG] Alternative extraction failed: {ex.Message}");
                return null;
            }
        }

        private void LogFlagDiscovery(byte flagId, FlagInfo flagInfo, string context)
        {
            var flagKey = $"{flagId:X2}:{flagInfo.FemaleWord}:{flagInfo.MaleWord}";
            
            if (!discoveredFlags.Contains(flagKey))
            {
                discoveredFlags.Add(flagKey);
                
                var discoveryEntry = new
                {
                    Timestamp = DateTime.Now,
                    FlagId = $"0x{flagId:X2}",
                    FemaleWord = flagInfo.FemaleWord,
                    MaleWord = flagInfo.MaleWord,
                    Category = ClassifyWordPair(flagInfo.FemaleWord, flagInfo.MaleWord),
                    SuggestedNeutral = SuggestNeutralWord(flagInfo.FemaleWord, flagInfo.MaleWord),
                    Context = ExtractCleanContext(context),
                    Notes = GenerateAnalysisNotes(flagInfo.FemaleWord, flagInfo.MaleWord),
                    IsKnown = GetReplacementForFlag(flagId, new PronounSet { Subject = "they", Object = "them", Possessive = "their", Reflexive = "themselves", PossessivePronoun = "theirs" }, plugin.Configuration) != null
                };

                log.Info($"[FLAG DISCOVERY] ID: {discoveryEntry.FlagId} | {discoveryEntry.FemaleWord} / {discoveryEntry.MaleWord} | Category: {discoveryEntry.Category} | Suggested: {discoveryEntry.SuggestedNeutral} | Status: {(discoveryEntry.IsKnown ? "KNOWN" : "NEW")}");
                SaveFlagDiscoveryEntry(discoveryEntry);
            }
        }

        private string ClassifyWordPair(string femaleWord, string maleWord)
        {
            var female = femaleWord.ToLower();
            var male = maleWord.ToLower();

            if ((female == "she" && male == "he") ||
                (female == "her" && male == "his") ||
                (female == "her" && male == "him") ||
                (female == "hers" && male == "his") ||
                (female == "herself" && male == "himself"))
                return "Pronoun";

            if ((female == "mistress" && male == "master") ||
                (female == "madam" && male == "sir") ||
                (female == "lady" && male == "lord") ||
                (female == "dame" && male == "sir"))
                return "Title/Honorific";

            if ((female == "sister" && male == "brother") ||
                (female == "daughter" && male == "son") ||
                (female == "mother" && male == "father") ||
                (female == "aunt" && male == "uncle"))
                return "Family_Relation";

            if ((female == "woman" && male == "man") ||
                (female == "women" && male == "men") ||
                (female == "girl" && male == "boy") ||
                (female == "lass" && male == "lad") ||
                (female == "maiden" && male == "youth"))
                return "Person/Role";

            if (female.Length > 3 && male.Length > 3)
            {
                if (female.EndsWith("ess") && !male.EndsWith("ess"))
                    return "Title/Role_Suffix";
                if (female.Contains("woman") || male.Contains("man"))
                    return "Person/Role";
            }

            return "Unknown";
        }

        private string SuggestNeutralWord(string femaleWord, string maleWord)
        {
            var female = femaleWord.ToLower();
            var male = maleWord.ToLower();

            var neutralMap = new Dictionary<(string, string), string>
            {
                {("she", "he"), "they"},
                {("her", "his"), "their"},
                {("her", "him"), "them"},
                {("hers", "his"), "theirs"},
                {("herself", "himself"), "themselves"},
                {("mistress", "master"), "Mx."},
                {("madam", "sir"), "friend"},
                {("lady", "lord"), "noble"},
                {("dame", "sir"), "friend"},
                {("woman", "man"), "person"},
                {("women", "men"), "people"},
                {("girl", "boy"), "youth"},
                {("lass", "lad"), "friend"},
                {("maiden", "youth"), "young one"},
                {("sister", "brother"), "sibling"},
                {("daughter", "son"), "child"},
                {("mother", "father"), "parent"},
                {("aunt", "uncle"), "relative"}
            };

            if (neutralMap.TryGetValue((female, male), out string? neutral))
                return neutral;

            if (neutralMap.TryGetValue((male, female), out neutral))
                return neutral;

            if (female.EndsWith("ess") && male.Length > 0)
                return male.Replace("er", "one").Replace("or", "one");

            return "[needs manual review]";
        }

        private string ExtractCleanContext(string rawContext)
        {
            var clean = System.Text.RegularExpressions.Regex.Replace(rawContext, @"[^\w\s\.\,\!\?\:\;\-]", " ");
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ");
            return clean.Trim();
        }

        private string GenerateAnalysisNotes(string femaleWord, string maleWord)
        {
            var notes = new List<string>();
            
            var female = femaleWord.ToLower();
            var male = maleWord.ToLower();

            if (female == "her" && male == "his")
                notes.Add("Possessive pronoun - very common");
            else if (female == "she" && male == "he")
                notes.Add("Subject pronoun - fundamental");
            else if (female.Contains("woman") || male.Contains("man"))
                notes.Add("Person identifier - affects plurality");
            else if (female == "mistress" && male == "master")
                notes.Add("Formal title - context sensitive");

            if (female.Length != male.Length)
                notes.Add($"Length mismatch: {female.Length} vs {male.Length}");

            return string.Join("; ", notes);
        }

        private void SaveFlagDiscoveryEntry(object discoveryEntry)
        {
            try
            {
                var discoveryFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FFXIV_Flag_Discovery.json");
                
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(discoveryEntry, Newtonsoft.Json.Formatting.Indented);
                
                File.AppendAllText(discoveryFile, json + ",\n");

                var summaryFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FFXIV_Flag_Discovery_Summary.txt");
                var summaryLine = $"{discoveryEntry.GetType().GetProperty("Timestamp")?.GetValue(discoveryEntry)} | " +
                                 $"Flag {discoveryEntry.GetType().GetProperty("FlagId")?.GetValue(discoveryEntry)} | " +
                                 $"{discoveryEntry.GetType().GetProperty("FemaleWord")?.GetValue(discoveryEntry)} / {discoveryEntry.GetType().GetProperty("MaleWord")?.GetValue(discoveryEntry)} | " +
                                 $"Category: {discoveryEntry.GetType().GetProperty("Category")?.GetValue(discoveryEntry)} | " +
                                 $"Suggested: {discoveryEntry.GetType().GetProperty("SuggestedNeutral")?.GetValue(discoveryEntry)}\n";
                
                File.AppendAllText(summaryFile, summaryLine);
            }
            catch (Exception ex)
            {
                log.Error($"[FLAG DISCOVERY] Failed to save entry: {ex.Message}");
            }
        }

        private string GetContextAroundFlag(byte[] data, int flagIndex)
        {
            try
            {
                int contextStart = Math.Max(0, flagIndex - 100);
                int contextEnd = Math.Min(data.Length, flagIndex + 200);
                int contextLen = contextEnd - contextStart;
                
                var contextBytes = new byte[contextLen];
                Array.Copy(data, contextStart, contextBytes, 0, contextLen);

                var contextText = System.Text.Encoding.UTF8.GetString(contextBytes);
                return System.Text.RegularExpressions.Regex.Replace(contextText, @"[^\x20-\x7E]", " ");
            }
            catch
            {
                return "[context extraction failed]";
            }
        }


        private readonly HashSet<string> discoveredFlags = new HashSet<string>();
        private bool testFileCreated = false;
        private readonly Dictionary<byte, DateTime> lastFlagProcessTime = new Dictionary<byte, DateTime>();
        private readonly TimeSpan flagProcessCooldown = TimeSpan.FromMilliseconds(100);

        private class FlagInfo
        {
            public string FemaleWord { get; set; } = "";
            public string MaleWord { get; set; } = "";
        }
        
        private int SkipPastFlagPattern(byte[] data, int startIndex)
        {
            try
            {
                int pos = startIndex + 6;
                if (pos >= data.Length) return startIndex + 1;

                int femaleLen = data[pos++];
                pos += femaleLen;

                if (pos >= data.Length) return pos;
                int maleLen = data[pos++];
                pos += maleLen;
                
                log.Info($"[FLAG SKIP] Skipped from {startIndex} to {pos}");
                return pos;
            }
            catch (Exception ex)
            {
                log.Error($"[FLAG SKIP] Error: {ex.Message}");
                return startIndex + 1;
            }
        }
        
        private string GetNeutralReplacementForFlag(string femaleWord, string maleWord)
        {
            var female = femaleWord.ToLower();
            var male = maleWord.ToLower();

            if ((female == "she" && male == "he") || (female == "he" && male == "she"))
                return "they";
            if ((female == "her" && male == "his") || (female == "his" && male == "her"))
                return "their";
            if ((female == "her" && male == "him") || (female == "him" && male == "her"))
                return "them";
            if ((female == "hers" && male == "his") || (female == "his" && male == "hers"))
                return "theirs";
            if ((female == "herself" && male == "himself") || (female == "himself" && male == "herself"))
                return "themselves";

            var neutralTitle = plugin.Configuration.GetGenderNeutralTitle().ToLower();
            if ((female == "woman" && male == "man") || (female == "man" && male == "woman"))
                return neutralTitle;
            if ((female == "lady" && male == "lord") || (female == "lord" && male == "lady"))
                return neutralTitle;
            if ((female == "mistress" && male == "master") || (female == "master" && male == "mistress"))
                return neutralTitle;
            if ((female == "girl" && male == "boy") || (female == "boy" && male == "girl"))
                return neutralTitle;
            if ((female == "madam" && male == "sir") || (female == "sir" && male == "madam"))
                return neutralTitle;
            if ((female == "miss" && male == "sir") || (female == "sir" && male == "miss"))
                return neutralTitle;
            if ((female == "sister" && male == "brother") || (female == "brother" && male == "sister"))
                return "sibling";

            return femaleWord;
        }

        private bool IsDialogueText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 10)
                return false;

            // Skip common UI patterns
            if (text.StartsWith("Level ") || 
                text.StartsWith("Gil: ") ||
                text.StartsWith("HP: ") ||
                text.StartsWith("MP: ") ||
                text.Contains("+++") ||
                text.Contains("===") ||
                text.All(c => char.IsDigit(c) || c == ',' || c == '.' || c == ' ') ||
                text.Contains("％") || // Percentage signs
                text.Length < 5)
                return false;

            // Look for dialogue patterns
            return text.Contains(" ") && // Must have spaces (sentences)
                   (text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') || text.Length > 20) &&
                   !text.Contains('\t') && // No tabs (UI elements)
                   text.Count(c => char.IsLetter(c)) > text.Length / 3; // At least 1/3 letters
        }

        private string? GetReplacementForFlag(byte flagId, PronounSet pronounSet, Configuration config, byte[] data = null, int flagIndex = -1, FlagInfo flagInfo = null)
        {
            // Only process known PLAYER-specific flags to avoid changing NPC references
            return flagId switch
            {
                // Core pronouns
                0x0C => pronounSet.Subject, // she/he → they
                0x0D => GetCorrectPronoun(flagInfo, pronounSet), // her/his/him → their/them/theirs (based on actual flag content)
                
                // Person identifiers
                0x0F => pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase) 
                        ? "person" // or could use config.GetGenderNeutralTitle()
                        : pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase) ? "woman" : "man",
                
                // Formal titles
                0x15 => pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase) 
                        ? config.GetGenderNeutralTitle() ?? "Friend"
                        : pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase) ? "Mistress" : "Master",
                
                // Family relations
                0x14 => pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase) 
                        ? "sibling"
                        : pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase) ? "sister" : "brother",
                
                // Address/titles (various contexts)
                0x0E => GetNeutralTitle(config), // miss/sir, lady/sir, girl/boy, lady/man, lass/lad
                0x11 => GetNeutralTitle(config), // Miss/Mister, lady/friend
                
                // Complex phrases - use neutral alternatives
                0x29 => GetNeutralCompliment(), // "quite taken with/somewhat in awe of" → "impressed by"
                0x2B => "person of distinction", // "princess among women/prince among men"
                0x31 => "look quite distinguished", // "look rather fine/cut quite a dashing figure"
                
                // Additional gender-specific words
                0x10 => pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase) 
                        ? "They're"
                        : pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase) ? "She's" : "He's",
                
                0x12 => pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase) 
                        ? "child"
                        : pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase) ? "daughter" : "son",
                
                0x13 => pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase) 
                        ? GetNeutralTitle(config) // Use configured title like "friend" 
                        : pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase) ? "missus" : "mister",
                
                0x1B => pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase) 
                        ? "crafter"
                        : pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase) ? "craftswoman" : "craftsman",
                
                0x21 => pronounSet.Object, // her/him → them (appears to be object pronoun)
                
                0x2D => pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase) 
                        ? "My distinguished guests" // More neutral formal address
                        : pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase) ? "My lords and ladies" : "My lords...and lady",
                
                // Don't process unknown flags - they might refer to NPCs
                _ => null
            };
        }

        private string GetCorrectPronounForFlag(FlagInfo flagInfo, PronounSet pronounSet, string neutralTitle)
        {
            if (pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase))
            {
                return flagInfo.FemaleWord;
            }
            else if (pronounSet.Subject.Equals("he", StringComparison.OrdinalIgnoreCase))
            {
                return flagInfo.MaleWord;
            }
            else
            {
                var result = (flagInfo.FemaleWord.ToLower(), flagInfo.MaleWord.ToLower()) switch
                {
                    ("she", "he") => "they",
                    ("her", "his") => "their", 
                    ("her", "him") => "them",
                    ("hers", "his") => "theirs",
                    ("herself", "himself") => "themselves",
                    ("sister", "brother") => neutralTitle,
                    _ => neutralTitle
                };

                if (char.IsUpper(flagInfo.FemaleWord[0]))
                {
                    return char.ToUpper(result[0]) + result.Substring(1);
                }
                
                return result;
            }
        }

        private string GetCorrectPronoun(FlagInfo flagInfo, PronounSet pronounSet)
        {
            var femaleWord = flagInfo.FemaleWord.ToLower();
            var maleWord = flagInfo.MaleWord.ToLower();

            // Object: her/him → them
            if ((femaleWord == "her" && maleWord == "him") ||
                (femaleWord == "him" && maleWord == "her"))
            {
                return pronounSet.Object;
            }

            // Possessive pronoun: his/hers → theirs
            if ((femaleWord == "hers" && maleWord == "his") ||
                (femaleWord == "his" && maleWord == "hers"))
            {
                return pronounSet.PossessivePronoun;
            }

            // Possessive adjective: her/his → their
            if ((femaleWord == "her" && maleWord == "his") ||
                (femaleWord == "his" && maleWord == "her"))
            {
                return pronounSet.Possessive;
            }

            return pronounSet.Possessive;
        }

        private string GetNeutralTitle(Configuration config)
        {
            var title = config.GetGenderNeutralTitle();
            return title switch
            {
                "friend" => "friend",
                "Mx." => "Mx.",
                "traveler" => "traveler",
                "adventurer" => "adventurer",
                _ => title ?? "friend"
            };
        }

        private string GetNeutralCompliment()
        {
            return "impressed by";
        }

        private string FixGrammarIssues(string text, PronounSet pronounSet)
        {
            if (!pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase))
                return text;

            var result = text;
            var originalText = text;

            result = result.Replace("took their prisoner", "took them prisoner");
            result = result.Replace("served their to", "served them to");
            result = result.Replace("failed their utterly", "failed them utterly");

            result = Regex.Replace(result, @"\bthey's\b", "they're", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bThey's\b", "They're");

            result = Regex.Replace(result, @"\bthey is\b", "they are", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bThey is\b", "They are");
            result = Regex.Replace(result, @"\bthey was\b", "they were", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bThey was\b", "They were");
            result = Regex.Replace(result, @"\bthey has\b", "they have", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bThey has\b", "They have");
            result = Regex.Replace(result, @"\bthey does\b", "they do", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bThey does\b", "They do");
            result = Regex.Replace(result, @"\bthey goes\b", "they go", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bThey goes\b", "They go");
            result = Regex.Replace(result, @"\bthey comes\b", "they come", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bThey comes\b", "They come");

            if (result != originalText)
            {
                log.Info($"[GRAMMAR FIX] '{originalText}' -> '{result}'");
            }

            return result;
        }


        private byte[] ReplaceGenderFlag(byte[] data, int startIndex, string replacement)
        {
            int endIndex = startIndex;
            while (endIndex < data.Length && data[endIndex] != 0x03)
                endIndex++;

            if (endIndex >= data.Length) return data;
            endIndex++;

            var replacementBytes = System.Text.Encoding.UTF8.GetBytes(replacement);
            var result = new byte[data.Length - (endIndex - startIndex) + replacementBytes.Length];

            Array.Copy(data, 0, result, 0, startIndex);
            Array.Copy(replacementBytes, 0, result, startIndex, replacementBytes.Length);
            Array.Copy(data, endIndex, result, startIndex + replacementBytes.Length, data.Length - endIndex);

            return result;
        }

        private string ProcessPlayerParameterPatterns(string text, Configuration config, PronounSet pronounSet)
        {
            if (!text.Contains("<If(PlayerParameter(4))>"))
                return text;

            var neutralTitle = config.GetGenderNeutralTitle();
            var isTheyThem = pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase);
            var isFemale = pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase);
            var pattern = @"<If\(PlayerParameter\(4\)\)>([^<]+)<Else/>([^<]+)</If>";

            return Regex.Replace(text, pattern, (Match match) =>
            {
                var femaleWord = match.Groups[1].Value;
                var maleWord = match.Groups[2].Value;
                var femaleWordLower = femaleWord.ToLower();
                var maleWordLower = maleWord.ToLower();

                if (isTheyThem)
                {
                    return (femaleWordLower, maleWordLower) switch
                    {
                        ("she", "he") => "they",
                        ("her", "his") => "their",
                        ("her", "him") => "them",
                        ("hers", "his") => "theirs",
                        ("herself", "himself") => "themselves",
                        ("She", "He") => "They",
                        ("Her", "His") => "Their",
                        ("Her", "Him") => "Them",
                        ("Hers", "His") => "Theirs",
                        ("Herself", "Himself") => "Themselves",
                        _ => neutralTitle
                    };
                }

                return isFemale ? femaleWord : maleWord;
            }, RegexOptions.IgnoreCase);
        }

        private static bool DetectAndReplaceGenderFlags(Span<byte> textSpan, string pronounSubject, IPluginLog logger, Configuration config)
        {
            if (!pronounSubject.Equals("they", StringComparison.OrdinalIgnoreCase))
                return false;

            bool modified = false;

            // Flag pattern: 02 08 [id] E9 05 FF [len] [female] FF [len] [male] 03
            for (int i = 0; i <= textSpan.Length - 10; i++)
            {
                if (textSpan[i] == 0x02 &&
                    textSpan[i + 1] == 0x08 &&
                    i + 2 < textSpan.Length &&
                    i + 3 < textSpan.Length && textSpan[i + 3] == 0xE9 &&
                    i + 4 < textSpan.Length && textSpan[i + 4] == 0x05 &&
                    i + 5 < textSpan.Length && textSpan[i + 5] == 0xFF)
                {
                    int patternStart = i;
                    int pos = i + 6;

                    if (pos >= textSpan.Length) continue;
                    int femaleLen = textSpan[pos++];
                    if (pos + femaleLen >= textSpan.Length) continue;

                    var femaleBytes = textSpan.Slice(pos, femaleLen);
                    pos += femaleLen;

                    if (pos >= textSpan.Length || textSpan[pos] != 0xFF) continue;
                    pos++;

                    if (pos >= textSpan.Length) continue;
                    int maleLen = textSpan[pos++];
                    if (pos + maleLen >= textSpan.Length) continue;

                    var maleBytes = textSpan.Slice(pos, maleLen);
                    pos += maleLen;

                    if (pos >= textSpan.Length || textSpan[pos] != 0x03) continue;
                    pos++;
                    
                    int patternEnd = pos;
                    int patternLength = patternEnd - patternStart;

                    var femaleWord = System.Text.Encoding.UTF8.GetString(femaleBytes).ToLower();
                    var maleWord = System.Text.Encoding.UTF8.GetString(maleBytes).ToLower();

                    logger.Info($"[Flag] Found flag: '{femaleWord}'/'{maleWord}' at position {patternStart}");

                    string placeholderCode = (femaleWord, maleWord) switch
                    {
                        ("she", "he") => "§THEY§",
                        ("her", "his") => "§THEIR§",
                        ("her", "him") => "§THEM§",
                        ("hers", "his") => "§THEIRS§",
                        ("herself", "himself") => "§THEMSELVES§",
                        _ => "§NEUTRAL§"
                    };

                    var placeholderBytes = System.Text.Encoding.UTF8.GetBytes(placeholderCode);

                    if (placeholderBytes.Length <= patternLength)
                    {
                        placeholderBytes.CopyTo(textSpan.Slice(patternStart));

                        var remaining = patternLength - placeholderBytes.Length;
                        if (remaining > 0)
                        {
                            var source = textSpan.Slice(patternEnd);
                            var dest = textSpan.Slice(patternStart + placeholderBytes.Length);
                            source.CopyTo(dest);

                            textSpan.Slice(textSpan.Length - remaining).Fill(0x00);
                        }

                        modified = true;
                    }
                }
            }
            
            return modified;
        }
        
        private static string GetNeutralReplacement(Span<byte> femaleBytes, Span<byte> maleBytes, Configuration config)
        {
            var neutralTitle = config.GetGenderNeutralTitle();
            var femaleWord = System.Text.Encoding.UTF8.GetString(femaleBytes).ToLower();
            var maleWord = System.Text.Encoding.UTF8.GetString(maleBytes).ToLower();

            if ((femaleWord.EndsWith("s") && femaleWord.Length > 3) ||
                (maleWord.EndsWith("s") && maleWord.Length > 3))
            {
                return neutralTitle + "s";
            }

            return neutralTitle;
        }

        // Verb conjugation patterns for they/them
        private static readonly Dictionary<Regex, string> ConjugationPatterns = new Dictionary<Regex, string>
        {
            { new Regex(@"\bthey\s+(finds)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they find" },
            { new Regex(@"\bthey\s+(goes)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they go" },
            { new Regex(@"\bthey\s+(does)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they do" },
            { new Regex(@"\bthey\s+(has)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they have" },
            { new Regex(@"\bthey\s+(is)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they are" },
            { new Regex(@"\bthey\s+(was)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they were" },
            { new Regex(@"\bthey\s+(says)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they say" },
            { new Regex(@"\bthey\s+(knows)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they know" },
            { new Regex(@"\bthey\s+(comes)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they come" },
            { new Regex(@"\bthey\s+(takes)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they take" },
            { new Regex(@"\bthey\s+(makes)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they make" },
            { new Regex(@"\bthey\s+(gets)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they get" },
            { new Regex(@"\bthey\s+(gives)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they give" },
            { new Regex(@"\bthey\s+(wants)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they want" },
            { new Regex(@"\bthey\s+(needs)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they need" },
            { new Regex(@"\bthey\s+(likes)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they like" },
            { new Regex(@"\bthey\s+(loves)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they love" },
            { new Regex(@"\bthey\s+(feels)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they feel" },
            { new Regex(@"\bthey\s+(thinks)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they think" },
            { new Regex(@"\bthey\s+(believes)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they believe" },
            { new Regex(@"\bthey\s+(understands)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they understand" },
            { new Regex(@"\bthey\s+(sees)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they see" },
            { new Regex(@"\bthey\s+(hears)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they hear" },
            { new Regex(@"\bthey\s+(speaks)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they speak" },
            { new Regex(@"\bthey\s+(tells)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they tell" },
            { new Regex(@"\bthey\s+(asks)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they ask" },
            { new Regex(@"\bthey\s+(works)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they work" },
            { new Regex(@"\bthey\s+(lives)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they live" },
            { new Regex(@"\bthey\s+(runs)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they run" },
            { new Regex(@"\bthey\s+(walks)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they walk" },
            { new Regex(@"\bthey\s+(stands)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they stand" },
            { new Regex(@"\bthey\s+(sits)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they sit" },
            { new Regex(@"\bthey\s+(travels)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they travel" },
            { new Regex(@"\bthey\s+(arrives)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they arrive" },
            { new Regex(@"\bthey\s+(leaves)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they leave" },
            { new Regex(@"\bthey\s+(returns)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they return" },
            { new Regex(@"\bthey\s+(helps)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they help" },
            { new Regex(@"\bthey\s+(saves)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they save" },
            { new Regex(@"\bthey\s+(protects)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they protect" },
            { new Regex(@"\bthey\s+(attacks)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they attack" },
            { new Regex(@"\bthey\s+(defends)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they defend" },
            { new Regex(@"\bthey\s+(uses)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they use" },
            { new Regex(@"\bthey\s+(carries)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they carry" },
            { new Regex(@"\bthey\s+(wears)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they wear" },
            { new Regex(@"\bthey\s+(holds)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they hold" },
            { new Regex(@"\bthey\s+(opens)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they open" },
            { new Regex(@"\bthey\s+(closes)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they close" },
            { new Regex(@"\bthey\s+(enters)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they enter" },
            { new Regex(@"\bthey\s+(follows)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they follow" },
            { new Regex(@"\bthey\s+(leads)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they lead" },
            { new Regex(@"\bthey\s+(learns)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they learn" },
            { new Regex(@"\bthey\s+(teaches)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they teach" },
            { new Regex(@"\bthey\s+(grows)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they grow" },
            { new Regex(@"\bthey\s+(changes)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they change" },
            { new Regex(@"\bthey\s+(becomes)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they become" },
            { new Regex(@"\bthey\s+(remains)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they remain" },
            { new Regex(@"\bthey\s+(waits)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they wait" },
            { new Regex(@"\bthey\s+(watches)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they watch" },
            { new Regex(@"\bthey\s+(looks)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they look" },
            { new Regex(@"\bthey\s+(appears)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they appear" },
            { new Regex(@"\bthey\s+(seems)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they seem" },
            { new Regex(@"\bthey\s+(creates)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they create" },
            { new Regex(@"\bthey\s+(builds)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they build" },
            { new Regex(@"\bthey\s+(fixes)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they fix" },
            { new Regex(@"\bthey\s+(breaks)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they break" },
            { new Regex(@"\bthey\s+(wins)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they win" },
            { new Regex(@"\bthey\s+(loses)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they lose" },
            { new Regex(@"\bthey\s+(fights)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they fight" },
            { new Regex(@"\bthey\s+(decides)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they decide" },
            { new Regex(@"\bthey\s+(chooses)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they choose" },
            { new Regex(@"\bthey\s+(tries)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they try" },
            { new Regex(@"\bthey\s+(succeeds)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they succeed" },
            { new Regex(@"\bthey\s+(fails)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they fail" },
            { new Regex(@"\bthey\s+(discovers)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they discover" },
            { new Regex(@"\bthey\s+(explores)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they explore" },
            { new Regex(@"\bthey\s+(adventures)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they adventure" },
            { new Regex(@"\bthey\s+(journeys)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they journey" },
            { new Regex(@"\bthey's\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they're" }
        };

        // Pre-compiled regex patterns
        private static readonly Regex HeRegex = new Regex(@"\bhe\b", RegexOptions.Compiled);
        private static readonly Regex HeCapitalRegex = new Regex(@"\bHe\b", RegexOptions.Compiled);
        private static readonly Regex SheRegex = new Regex(@"\bshe\b", RegexOptions.Compiled);
        private static readonly Regex SheCapitalRegex = new Regex(@"\bShe\b", RegexOptions.Compiled);
        private static readonly Regex HisRegex = new Regex(@"\bhis\b", RegexOptions.Compiled);
        private static readonly Regex HisCapitalRegex = new Regex(@"\bHis\b", RegexOptions.Compiled);
        private static readonly Regex LadRegex = new Regex(@"\blad\b", RegexOptions.Compiled);
        private static readonly Regex LadCapitalRegex = new Regex(@"\bLad\b", RegexOptions.Compiled);
        // Context-aware "her" patterns
        private static readonly Regex HerPossessiveRegex = new Regex(@"\bher(?=\s+(?!a\b|an\b|the\b)[A-Z][a-z]+)", RegexOptions.Compiled);
        private static readonly Regex HerPossessiveCapitalRegex = new Regex(@"\bHer(?=\s+(?!a\b|an\b|the\b)[A-Z][a-z]+)", RegexOptions.Compiled);
        private static readonly Regex HerPossessiveLowerRegex = new Regex(@"\bher(?=\s+(?!a\b|an\b|the\b|to\b|and\b|or\b|but\b)[a-z]+)", RegexOptions.Compiled);
        private static readonly Regex HerObjectRegex = new Regex(@"\bher\b", RegexOptions.Compiled);
        private static readonly Regex HerObjectCapitalRegex = new Regex(@"\bHer\b", RegexOptions.Compiled);

        private static readonly Regex HimRegex = new Regex(@"\bhim\b", RegexOptions.Compiled);
        private static readonly Regex HimCapitalRegex = new Regex(@"\bHim\b", RegexOptions.Compiled);
        private static readonly Regex HimselfRegex = new Regex(@"\bhimself\b", RegexOptions.Compiled);
        private static readonly Regex HimselfCapitalRegex = new Regex(@"\bHimself\b", RegexOptions.Compiled);
        private static readonly Regex HerselfRegex = new Regex(@"\bherself\b", RegexOptions.Compiled);
        private static readonly Regex HerselfCapitalRegex = new Regex(@"\bHerself\b", RegexOptions.Compiled);

        // Title regex patterns
        private static readonly Regex WomanRegex = new Regex(@"\bwoman\b", RegexOptions.Compiled);
        private static readonly Regex WomanCapitalRegex = new Regex(@"\bWoman\b", RegexOptions.Compiled);
        private static readonly Regex ManRegex = new Regex(@"\bman\b", RegexOptions.Compiled);
        private static readonly Regex ManCapitalRegex = new Regex(@"\bMan\b", RegexOptions.Compiled);
        private static readonly Regex LadyRegex = new Regex(@"\blady\b", RegexOptions.Compiled);
        private static readonly Regex LadyCapitalRegex = new Regex(@"\bLady\b", RegexOptions.Compiled);
        private static readonly Regex SirRegex = new Regex(@"\bsir\b", RegexOptions.Compiled);
        private static readonly Regex SirCapitalRegex = new Regex(@"\bSir\b", RegexOptions.Compiled);
        private static readonly Regex MistressRegex = new Regex(@"\bmistress\b", RegexOptions.Compiled);
        private static readonly Regex MistressCapitalRegex = new Regex(@"\bMistress\b", RegexOptions.Compiled);
        private static readonly Regex MasterRegex = new Regex(@"\bmaster\b", RegexOptions.Compiled);
        private static readonly Regex MasterCapitalRegex = new Regex(@"\bMaster\b", RegexOptions.Compiled);
        private static readonly Regex GirlRegex = new Regex(@"\bgirl\b", RegexOptions.Compiled);
        private static readonly Regex GirlCapitalRegex = new Regex(@"\bGirl\b", RegexOptions.Compiled);
        private static readonly Regex BoyRegex = new Regex(@"\bboy\b", RegexOptions.Compiled);
        private static readonly Regex BoyCapitalRegex = new Regex(@"\bBoy\b", RegexOptions.Compiled);
        private static readonly Regex MadamRegex = new Regex(@"\bmadam\b", RegexOptions.Compiled);
        private static readonly Regex MadamCapitalRegex = new Regex(@"\bMadam\b", RegexOptions.Compiled);
        private static readonly Regex DameRegex = new Regex(@"\bdame\b", RegexOptions.Compiled);
        private static readonly Regex DameCapitalRegex = new Regex(@"\bDame\b", RegexOptions.Compiled);
        private static readonly Regex LassRegex = new Regex(@"\blass\b", RegexOptions.Compiled);
        private static readonly Regex LassCapitalRegex = new Regex(@"\bLass\b", RegexOptions.Compiled);
        private static readonly Regex MaidenRegex = new Regex(@"\bmaiden\b", RegexOptions.Compiled);
        private static readonly Regex MaidenCapitalRegex = new Regex(@"\bMaiden\b", RegexOptions.Compiled);
        private static readonly Regex BrotherRegex = new Regex(@"\bbrother\b", RegexOptions.Compiled);
        private static readonly Regex BrotherCapitalRegex = new Regex(@"\bBrother\b", RegexOptions.Compiled);
        private static readonly Regex SisterRegex = new Regex(@"\bsister\b", RegexOptions.Compiled);
        private static readonly Regex SisterCapitalRegex = new Regex(@"\bSister\b", RegexOptions.Compiled);

        public NPCDialogueProcessor(Plugin plugin, ISigScanner sigScanner, IGameInteropProvider gameInteropProvider,
            IChatGui chatGui, IClientState clientState, IPluginLog log, ICondition condition)
        {
            this.plugin = plugin;
            this.sigScanner = sigScanner;
            this.gameInteropProvider = gameInteropProvider;
            this.chatGui = chatGui;
            this.clientState = clientState;
            this.log = log;
            this.condition = condition;

            try
            {
                InitializeHooks();
                log.Info("[Dialogue] Multi-pronoun dialogue processor initialized.");
            }
            catch (Exception ex)
            {
                log.Error($"[Dialogue] Failed to initialize processor: {ex.Message}");
            }
        }

        private void InitializeHooks()
        {
            // Main text processing hook (PrefPro my beloved)
            var getStringSignature = "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 83 B9 ?? ?? ?? ?? ?? 49 8B F9 49 8B F0 48 8B EA 48 8B D9 75 09 48 8B 01 FF 90";
            var getStringPtr = sigScanner.ScanText(getStringSignature);
            getStringHook = gameInteropProvider.HookFromAddress<GetStringPrototype>(getStringPtr, GetStringDetour);
            getStringHook.Enable();

            // Try to hook earlier in the text pipeline to catch unresolved flags
            try
            {
                // This signature might catch text before flag resolution
                var earlyTextSignature = "48 89 5C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 55 41 54 41 55 41 56 41 57 48 8B EC 48 83 EC 40";
                var earlyTextPtr = sigScanner.ScanText(earlyTextSignature);
                processTextHook = gameInteropProvider.HookFromAddress<ProcessTextPrototype>(earlyTextPtr, ProcessTextDetour);
                processTextHook.Enable();
                log.Info("[Dialogue] Early text processing hook enabled - will intercept flags before game processes them.");
            }
            catch (Exception ex)
            {
                log.Warning($"[Dialogue] Failed to initialize early text hook: {ex.Message}");
            }

            // Add Lua hook for they/them gender forcing
            try
            {
                // Try a more specific Lua hook signature
                var getLuaVar = "E8 ?? ?? ?? ?? 48 85 DB 74 1B 48 8D 8F";
                var getLuaVarPtr = IntPtr.Zero;
                try 
                {
                    getLuaVarPtr = sigScanner.ScanText(getLuaVar);
                    log.Info($"[Dialogue] Found Lua signature at: {getLuaVarPtr:X}");
                }
                catch (Exception ex)
                {
                    log.Error($"[Dialogue] Failed to find Lua signature: {ex.Message}");
                    
                    // Try alternative signature
                    try
                    {
                        getLuaVar = "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 8B F2 48 8B F9 8B DA";
                        getLuaVarPtr = sigScanner.ScanText(getLuaVar);
                        log.Info($"[Dialogue] Found alternative Lua signature at: {getLuaVarPtr:X}");
                    }
                    catch
                    {
                        log.Error("[Dialogue] Could not find any Lua signature");
                    }
                }
                
                if (getLuaVarPtr != IntPtr.Zero)
                {
                    getLuaVarHook = gameInteropProvider.HookFromAddress<GetLuaVarPrototype>(getLuaVarPtr, GetLuaVarDetour);
                    getLuaVarHook.Enable();
                    log.Info("[Dialogue] Lua gender override hook enabled.");
                }
                else
                {
                    log.Error("[Dialogue] Lua hook not installed - signature not found");
                }
            }
            catch (Exception ex)
            {
                log.Warning($"[Dialogue] Failed to initialize Lua hooks: {ex.Message}");
            }

            // Add audio voice gender hook for cutscenes
            try
            {
                var audioSignature = "E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? 8B D8 48 8B 89 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 63 4F 24";
                var audioPtr = sigScanner.ScanText(audioSignature);
                getCutVoGenderHook = gameInteropProvider.HookFromAddress<GetCutVoGenderPrototype>(audioPtr, GetCutVoGenderDetour);
                getCutVoGenderHook.Enable();
                log.Info("[Dialogue] Audio voice gender hook enabled.");
            }
            catch (Exception ex)
            {
                log.Warning($"[Dialogue] Failed to initialize audio hook: {ex.Message}");
            }
        }

        private bool IsInCutscene()
        {
            if (condition == null) return false;

            return condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInCutSceneEvent] ||
                   condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.WatchingCutscene] ||
                   condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.WatchingCutscene78] ||
                   condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInQuestEvent];
        }


        // Improved filtering - much more restrictive, only skip obvious non-dialogue
        private bool IsDefinitelyNotDialogue(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 5) return true;

            // Chat messages with player names - more comprehensive patterns
            var cleanText = Regex.Replace(text, @"[^\u0020-\u007E]", ""); // Remove non-ASCII
            if (Regex.IsMatch(cleanText, @"[A-Z][a-z]+\s+[A-Z][a-z]+\s*:")) return true;
            if (Regex.IsMatch(text, @"[A-Z][a-z]+\s+[A-Z][a-z]+\s*:")) return true;

            // Character names followed by colon (chat pattern)
            if (text.Contains(":") && text.Length < 100 && Regex.IsMatch(text, @"^\s*\w+.*:")) return true;
            
            // Specific pattern: "PlayerName: message" (like "Fawn Adeline: I lied :3")
            if (Regex.IsMatch(text, @"^[A-Za-z]+\s+[A-Za-z]+\s*:")) return true;

            // Chat message with === decorators (like "===Kid Icarus: He is there ==")
            if (text.Contains("===") && text.Contains(":")) return true;

            // Contains special chat characters
            if (text.Contains("����") || text.StartsWith("H") && text.EndsWith("H")) return true;

            // System messages and commands
            if (text.StartsWith("[") || text.StartsWith("/") || text.StartsWith("<")) return true;

            // Chat channels
            if (text.Contains(">>") || text.Contains("<<")) return true;

            // Mare Synchronos and other plugin messages
            if (text.Contains("[Mare") || text.Contains("Mare:")) return true;

            // Chat-specific patterns
            if (text.Contains(" says") || text.Contains(" tells") || text.Contains(" yells")) return true;

            // Numbers, timestamps, UI labels
            if (Regex.IsMatch(text, @"^\d+$") || Regex.IsMatch(text, @"^\d+/\d+$") || Regex.IsMatch(text, @"\d+:\d+")) return true;

            // Single words or very short phrases (likely UI elements)
            if (!text.Contains(" ") && text.Length < 10) return true;

            // Job names and world names
            var systemTerms = new[] { "Paladin", "Warrior", "Server", "World", "Level", "HP", "MP", "Experience" };
            if (systemTerms.Any(term => text.Contains(term))) return true;

            return false;
        }

        // Chat detection for post-processing
        private bool IsDefinitelyChat(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            // Unicode characters for better pattern matching
            var cleanText = Regex.Replace(text, @"[^\u0020-\u007E]", "");

            // Player Name: message
            if (Regex.IsMatch(cleanText, @"[A-Z][a-z]+\s+[A-Z][a-z]+\s*:"))
                return true;

            // Chat commands
            if (text.StartsWith("/say") || text.StartsWith("/tell") || text.StartsWith("/shout") ||
                text.StartsWith("/yell") || text.StartsWith("/party") || text.StartsWith("/fc") ||
                text.StartsWith("/ls") || text.StartsWith("/cwls"))
                return true;

            // Chat channel indicators
            if (text.Contains("[Say]") || text.Contains("[Yell]") || text.Contains("[Shout]") ||
                text.Contains("[Tell]") || text.Contains("[Party]") || text.Contains("[FC]") ||
                text.Contains("[LS") || text.Contains("[CWLS") || text.Contains("[Novice"))
                return true;

            return false;
        }

        private bool IsUIElement(string textString)
        {
            var text = textString.Trim();

            // Skip if too short
            if (text.Length < 10) return true;

            // Skip job/class names
            var jobNames = new[] { "Paladin", "Warrior", "Dark Knight", "Gunbreaker", "White Mage", "Scholar",
                                   "Astrologian", "Sage", "Monk", "Dragoon", "Ninja", "Samurai", "Reaper",
                                   "Black Mage", "Summoner", "Red Mage", "Blue Mage", "Bard", "Machinist",
                                   "Dancer", "Carpenter", "Blacksmith", "Armorer", "Goldsmith", "Leatherworker",
                                   "Weaver", "Alchemist", "Culinarian", "Miner", "Botanist", "Fisher",
                                   "Gladiator", "Marauder", "Conjurer", "Thaumaturge", "Pugilist", "Lancer",
                                   "Rogue", "Archer" };

            foreach (var job in jobNames)
            {
                if (text.Contains(job)) return true;
            }

            // Skip numbers/stats
            if (Regex.IsMatch(text, @"^\d+(\.\d+)?$")) return true;
            if (Regex.IsMatch(text, @"^\d+/\d+$")) return true;

            // Skip time stamps
            if (Regex.IsMatch(text, @"\d+:\d+")) return true;

            // Skip world names
            if (text.Contains("World") || text.Contains("Server")) return true;

            // Skip single words (likely UI labels)
            if (!text.Contains(" ") && text.Length < 15) return true;

            return false;
        }

        // Check if text contains emote patterns that shouldn't be touched
        private bool ContainsEmotePattern(string text)
        {
            // FFXIV emote pattern: H��I��emoteNameIH
            if (Regex.IsMatch(text, @"H[^\w]*I[^\w]*\w+I[^\w]*H"))
            {
                return true;
            }
            return false;
        }

        // Check if text has player-specific patterns
        private bool HasPlayerSpecificPattern(string text)
        {
            // Patterns that clearly refer to the player even without gender selection flags
            var playerPatterns = new[]
            {
                @"\b(men|women|man|woman|sir|madam|master|mistress|lady|lord)\s+(like\s+you|such\s+as\s+you)\b",
                @"\bgood\s+(man|woman|sir|madam|master|mistress|lady|lord)\b",
                @"\b(thank\s+you|well\s+done|excellent),?\s+(man|woman|sir|madam|master|mistress|lady|lord)\b",
                @"\byou\s+(are|were)\s+(a|an)?\s*(man|woman|sir|madam|master|mistress|lady|lord)\b",
                @"\b(listen|hear\s+me),?\s+(man|woman|sir|madam|master|mistress|lady|lord)\b"
            };

            foreach (var pattern in playerPatterns)
            {
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // Check if text refers to NPCs
        private bool IsNPCReference(string text)
        {
            // Patterns that refer to NPCs, not the player
            var npcPatterns = new[]
            {
                @"\b(a|an|the|this|that)\s+(suspicious|strange|mysterious|unknown|dead|evil|certain|particular)\s+(man|woman|person)\b"
            };

            foreach (var pattern in npcPatterns)
            {
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Text processing hook for unresolved flags.</summary>
        private nint ProcessTextDetour(nint textPtr, nint length)
        {
            try
            {
                if (!plugin.Configuration.EnableDialogueIntegration)
                    return processTextHook!.Original(textPtr, length);

                var activeCharacter = plugin.GetActiveCharacter();
                if (activeCharacter?.RPProfile?.Pronouns == null)
                    return processTextHook!.Original(textPtr, length);

                var pronounSet = PronounParser.Parse(activeCharacter.RPProfile.Pronouns);

                unsafe
                {
                    var span = new Span<byte>((void*)textPtr, (int)length);
                    var textString = System.Text.Encoding.UTF8.GetString(span);

                    bool hasGenderedContent = textString.Contains("woman", StringComparison.OrdinalIgnoreCase) ||
                                             textString.Contains("man", StringComparison.OrdinalIgnoreCase) ||
                                             textString.Contains("lady", StringComparison.OrdinalIgnoreCase) ||
                                             textString.Contains("lord", StringComparison.OrdinalIgnoreCase) ||
                                             textString.Contains(" she ", StringComparison.OrdinalIgnoreCase) ||
                                             textString.Contains(" he ", StringComparison.OrdinalIgnoreCase) ||
                                             textString.Contains(" her ", StringComparison.OrdinalIgnoreCase) ||
                                             textString.Contains(" his ", StringComparison.OrdinalIgnoreCase) ||
                                             textString.Contains(" him ", StringComparison.OrdinalIgnoreCase) ||
                                             textString.Contains("sister", StringComparison.OrdinalIgnoreCase) ||
                                             textString.Contains("brother", StringComparison.OrdinalIgnoreCase);

                    if (hasGenderedContent)
                    {
                        var hexString = string.Join(" ", span.ToArray().Select(b => b.ToString("X2")));
                        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                        log.Info($"[GENDER CONTENT] Text: '{textString}'");
                        log.Info($"[GENDER CONTENT] Hex: {hexString}");
                        log.Info($"[GENDER CONTENT] Has flags: {span.Contains((byte)0x02)}");

                        try
                        {
                            var investigationPath = @"F:\CS+\FFXIV_Dialogue_Investigation.txt";
                            var entry = $"[{timestamp}] ProcessTextDetour\n" +
                                       $"Text: {textString}\n" +
                                       $"Hex: {hexString}\n" +
                                       $"Has Control Codes (0x02): {span.Contains((byte)0x02)}\n" +
                                       $"Length: {span.Length}\n" +
                                       $"---\n";
                            File.AppendAllText(investigationPath, entry);
                        }
                        catch { }
                    }

                    if (span.Contains((byte)0x02) && !hasGenderedContent)
                    {
                        var hexString = string.Join(" ", span.ToArray().Select(b => b.ToString("X2")));
                        log.Info($"[CONTROL CODES] Text: '{textString}'");
                        log.Info($"[CONTROL CODES] Hex: {hexString}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Warning($"[Dialogue] Error in ProcessTextDetour: {ex.Message}");
            }

            return processTextHook!.Original(textPtr, length);
        }

        /// <summary>Main text processing detour.</summary>
        private int GetStringDetour(RaptureTextModule* textModule, byte* text, void* decoder, Utf8String* stringStruct)
        {
            try
            {
                if (!plugin.Configuration.EnableDialogueIntegration)
                    return getStringHook!.Original(textModule, text, decoder, stringStruct);

                var activeCharacter = plugin.GetActiveCharacter();
                if (activeCharacter?.RPProfile?.Pronouns == null)
                    return getStringHook!.Original(textModule, text, decoder, stringStruct);

                var pronounSet = PronounParser.Parse(activeCharacter.RPProfile.Pronouns);

                if (plugin.Configuration.ReplaceNameInDialogue && !string.IsNullOrEmpty(activeCharacter.Name))
                {
                    var textSpan = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text);
                    ProcessNameReplacementSeString(ref text, activeCharacter, textSpan.Length);
                }

                // Process gender flags
                {
                    var textSpan = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text);
                    var textBytes = textSpan.ToArray();
                    var processedBytes = ProcessGenderFlags(textBytes, activeCharacter);

                    if (!processedBytes.SequenceEqual(textBytes))
                    {
                        log.Info($"[GetString] Gender flags were processed for {pronounSet.Subject}/{pronounSet.Object} user");
                        var newText = Marshal.AllocHGlobal(processedBytes.Length + 1);
                        Marshal.Copy(processedBytes, 0, newText, processedBytes.Length);
                        Marshal.WriteByte(newText + processedBytes.Length, 0);
                        text = (byte*)newText;
                    }
                }

                var result = getStringHook!.Original(textModule, text, decoder, stringStruct);

                // Post-process pronouns and grammar
                if (stringStruct != null && stringStruct->BufUsed > 0)
                {
                    var gameGeneratedText = stringStruct->ToString();

                    if (!IsDialogueText(gameGeneratedText))
                        return result;

                    // Skip chat messages
                    if (IsDefinitelyChat(gameGeneratedText))
                        return result;

                    // Skip incomplete text
                    if (gameGeneratedText.EndsWith(" ") && !gameGeneratedText.EndsWith(". ") &&
                        !gameGeneratedText.EndsWith("! ") && !gameGeneratedText.EndsWith("? "))
                        return result;

                    bool hasPlaceholder = gameGeneratedText.Contains("Ξ") || gameGeneratedText.Contains("§");
                    bool looksLikeChat = gameGeneratedText.Contains(">>") || gameGeneratedText.Contains("<<");

                    if (looksLikeChat || gameGeneratedText.Length < 20)
                        return result;

                    // Only process colon-containing text if it has placeholders
                    if (gameGeneratedText.Contains(": ") && !hasPlaceholder)
                        return result;
                    
                    var processed = gameGeneratedText;

                    if (pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase))
                    {
                        bool hasPlaceholders = processed.Contains("Ξ") || processed.Contains("§") || processed.Contains("=");
                        bool hasProcessedFlags = processed.Contains("she  he") || processed.Contains("her  him") ||
                                                processed.Contains("She  He") || processed.Contains("her  his") ||
                                                processed.Contains("hers  his") || processed.Contains("herself  himself");
                        bool hasLuaForcedFemale = processed.Length > 20 && !processed.Contains(":") &&
                                                 (processed.Contains(" she ") || processed.Contains(" her ") ||
                                                  processed.Contains("She ") || processed.Contains("Her "));

                        if (!hasPlaceholders && !hasProcessedFlags && !hasLuaForcedFemale)
                            return result;

                        if (processed.Contains("="))
                            log.Info($"[PostProcess] Input contains '=' pattern: '{processed}'");

                        if (processed.Contains("§") || processed.Contains("Ξ"))
                            log.Info($"[PostProcess] Found placeholders in: '{processed}'");

                        if (processed.Contains("ΞNEUTRALΞ"))
                            log.Info($"[PostProcess] Found ΞNEUTRALΞ placeholder before replacement: '{processed}'");

                        // Replace placeholders with they/them
                        processed = processed.Replace("ΞTHEYΞ", "they");
                        processed = processed.Replace("ΞTHEIRΞ", "their");
                        processed = processed.Replace("ΞTHEMΞ", "them");
                        processed = processed.Replace("ΞTHEIRSΞ", "theirs");
                        processed = processed.Replace("ΞTHEMSELVESΞ", "themselves");

                        // Handle contractions
                        processed = Regex.Replace(processed, @"\bthey's\b", "they're", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\bThey's\b", "They're");

                        // Neutral title
                        var neutralTitle = plugin.Configuration.GetGenderNeutralTitle();
                        processed = processed.Replace("ΞNEUTRALΞ", neutralTitle);

                        // Bypassed flag patterns
                        processed = processed.Replace("she  he", "they");
                        processed = processed.Replace("She  He", "They");
                        processed = processed.Replace("her  him", "them");
                        processed = processed.Replace("her  his", "their");
                        processed = processed.Replace("hers  his", "theirs");
                        processed = processed.Replace("herself  himself", "themselves");

                        // Lua-forced female variants
                        if (hasLuaForcedFemale && !hasPlaceholders && !hasProcessedFlags)
                        {
                            log.Info($"[PostProcess] Processing Lua-forced female variant: '{processed}'");
                            processed = Regex.Replace(processed, @"\bshe finds\b", "they find", RegexOptions.IgnoreCase);
                            processed = Regex.Replace(processed, @"\bShe finds\b", "They find");
                            processed = Regex.Replace(processed, @"\bshe is\b", "they are", RegexOptions.IgnoreCase);
                            processed = Regex.Replace(processed, @"\bShe is\b", "They are");
                            processed = Regex.Replace(processed, @"\bshe was\b", "they were", RegexOptions.IgnoreCase);
                            processed = Regex.Replace(processed, @"\bShe was\b", "They were");
                            processed = Regex.Replace(processed, @"\bshe has\b", "they have", RegexOptions.IgnoreCase);
                            processed = Regex.Replace(processed, @"\bShe has\b", "They have");
                        }

                        if (processed.Contains("ΞNEUTRALΞ"))
                        {
                            log.Warning($"[PostProcess] ΞNEUTRALΞ placeholder still present after replacement: '{processed}'");
                            processed = processed.Replace("\u039ENEUTRAL\u039E", neutralTitle);
                            processed = processed.Replace("=NEUTRAL=", neutralTitle);
                        }

                        // Legacy § format
                        processed = processed.Replace("§THEY§", "they");
                        processed = processed.Replace("§THEIR§", "their");
                        processed = processed.Replace("§THEM§", "them");
                        processed = processed.Replace("§THEIRS§", "theirs");
                        processed = processed.Replace("§THEMSELVES§", "themselves");
                        processed = processed.Replace("§NEUTRAL§", neutralTitle);
                        processed = processed.Replace("§ NEUTRALS", neutralTitle);
                        processed = processed.Replace("§NEUTRALS§", neutralTitle);
                        processed = processed.Replace("§NEUTRALS", neutralTitle);

                        // Malformed placeholder cleanup
                        processed = processed.Replace("§ NEUTRALS", neutralTitle);
                        processed = processed.Replace("§NEUTRALS ", neutralTitle + " ");
                        processed = processed.Replace("§ NEUTRAL ", neutralTitle + " ");
                        processed = processed.Replace("§NEUTRAL ", neutralTitle + " ");

                        // Game flag output format: "word1=word2="
                        processed = Regex.Replace(processed, @"\b(she|he)=(she|he)=", "they", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\b(her|his)=(her|his)=", "their", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\b(her|him)=(her|him)=", "them", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\b(hers|his)=(hers|his)=", "theirs", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\b(herself|himself)=(herself|himself)=", "themselves", RegexOptions.IgnoreCase);

                        // Already-processed patterns
                        processed = Regex.Replace(processed, @"\btheir=their=", "their", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\bthem=them=", "them", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\bthey=they=", "they", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\btheirs=theirs=", "theirs", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\bthemselves=themselves=", "themselves", RegexOptions.IgnoreCase);

                        // Dawntrail content - plain gendered text without flags
                        if (processed.Contains("that woman there") || processed.Contains("that man there"))
                        {
                            processed = Regex.Replace(processed, @"\bthat woman there\b", $"that {neutralTitle} there", RegexOptions.IgnoreCase);
                            processed = Regex.Replace(processed, @"\bthat man there\b", $"that {neutralTitle} there", RegexOptions.IgnoreCase);
                        }
                        if (processed.Contains("remarkable young woman") || processed.Contains("remarkable young man"))
                        {
                            processed = Regex.Replace(processed, @"\bremarkable young woman\b", $"remarkable young {neutralTitle}", RegexOptions.IgnoreCase);
                            processed = Regex.Replace(processed, @"\bremarkable young man\b", $"remarkable young {neutralTitle}", RegexOptions.IgnoreCase);
                        }

                        // Plain pronouns (newer content without flags)
                        processed = Regex.Replace(processed, @"\bMore than a hero, (she|he)\b", "More than a hero, they", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\bwhere (she|he) finds\b", "where they find", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\b(She|He) finds\b", "They find");
                        processed = Regex.Replace(processed, @"\b(she|he) is\b", "they are", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\b(She|He) is\b", "They are");
                        processed = Regex.Replace(processed, @"\b(she|he) was\b", "they were", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\b(She|He) was\b", "They were");
                        processed = Regex.Replace(processed, @"\b(she|he) has\b", "they have", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\b(She|He) has\b", "They have");
                        processed = Regex.Replace(processed, @"\b(she|he)'s\b", "they're", RegexOptions.IgnoreCase);
                        processed = Regex.Replace(processed, @"\b(She|He)'s\b", "They're");

                        // Don't replace generic "man/woman" - often refers to NPCs

                        processed = FixGrammarIssues(processed, pronounSet);

                        if (processed != gameGeneratedText)
                            log.Info($"[PostProcess] They/them: '{gameGeneratedText}' -> '{processed}'");
                    }
                    // Non-they/them handled by Lua hook
                    
                    if (processed != gameGeneratedText)
                    {
                        SafeSetString(stringStruct, processed);
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                log.Error($"[Dialogue] Error in GetStringDetour: {ex.Message}");
                return getStringHook!.Original(textModule, text, decoder, stringStruct);
            }
        }

        /// <summary>Legacy fallback for pronoun/title processing.</summary>
        private string ProcessPronounsAndTitles(string text, PronounSet pronounSet, Character activeCharacter)
        {
            var processed = text;

            // Skip NPC contexts
            if (Regex.IsMatch(text, @"\b[A-Z][a-z]{4,}\s+and\s+(his|her|their)\b"))
                return processed;

            if (text.Contains("+0%") || text.Contains("0x") || text.Length < 10)
                return processed;

            if (ContainsEmotePattern(text))
                return processed;

            if (IsUIElement(text))
                return processed;

            if (IsNPCReference(text))
                return processed;

            var neutralTitle = "adventurer";
            if (plugin.Configuration.EnableAdvancedTitleReplacement)
                neutralTitle = plugin.Configuration.GetGenderNeutralTitle().ToLower();
            var capitalizedNeutralTitle = char.ToUpper(neutralTitle[0]) + neutralTitle.Substring(1);

            // They/them handled separately for flag patterns
            if (!pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase))
            {
                var subjectLower = pronounSet.Subject.ToLower();
                var subjectCapital = char.ToUpper(pronounSet.Subject[0]) + pronounSet.Subject.Substring(1).ToLower();
                var possessiveLower = pronounSet.Possessive.ToLower();
                var possessiveCapital = char.ToUpper(pronounSet.Possessive[0]) + pronounSet.Possessive.Substring(1).ToLower();
                var objectLower = pronounSet.Object.ToLower();
                var objectCapital = char.ToUpper(pronounSet.Object[0]) + pronounSet.Object.Substring(1).ToLower();
                var reflexiveLower = pronounSet.Reflexive.ToLower();
                var reflexiveCapital = char.ToUpper(pronounSet.Reflexive[0]) + pronounSet.Reflexive.Substring(1).ToLower();

                // Basic pronoun replacement
                if (plugin.Configuration.ReplacePronounsInDialogue)
                {
                    processed = HeRegex.Replace(processed, subjectLower);
                    processed = HeCapitalRegex.Replace(processed, subjectCapital);
                    processed = SheRegex.Replace(processed, subjectLower);
                    processed = SheCapitalRegex.Replace(processed, subjectCapital);

                    // Possessive
                    processed = HisRegex.Replace(processed, possessiveLower);
                    processed = HisCapitalRegex.Replace(processed, possessiveCapital);
                    processed = HerPossessiveRegex.Replace(processed, possessiveLower);
                    processed = HerPossessiveCapitalRegex.Replace(processed, possessiveCapital);
                    processed = HerPossessiveLowerRegex.Replace(processed, possessiveLower);

                    // Object
                    processed = HerObjectRegex.Replace(processed, objectLower);
                    processed = HerObjectCapitalRegex.Replace(processed, objectCapital);
                    processed = HimRegex.Replace(processed, objectLower);
                    processed = HimCapitalRegex.Replace(processed, objectCapital);

                    // Reflexive
                    processed = HimselfRegex.Replace(processed, reflexiveLower);
                    processed = HimselfCapitalRegex.Replace(processed, reflexiveCapital);
                    processed = HerselfRegex.Replace(processed, reflexiveLower);
                    processed = HerselfCapitalRegex.Replace(processed, reflexiveCapital);
                }
            }

            // Title replacement
            if (plugin.Configuration.ReplaceGenderedTerms)
            {
                // Gender selection patterns
                processed = Regex.Replace(processed, @"��woman�man", neutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"woman�man", neutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"��women�men", neutralTitle + "s", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"women�men", neutralTitle + "s", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"��madam�sir", neutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"madam�sir", neutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"��Mistress�Master", capitalizedNeutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"Mistress�Master", capitalizedNeutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"��Master�Mistress", capitalizedNeutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"Master�Mistress", capitalizedNeutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"��sir�madam", neutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"sir�madam", neutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"��man�woman", neutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"man�woman", neutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"��men�women", neutralTitle + "s", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"men�women", neutralTitle + "s", RegexOptions.IgnoreCase);

                // Player-specific patterns
                processed = Regex.Replace(processed, @"\b(men|women|man|woman)\s+(like\s+you)\b",
                    $"{neutralTitle}s like you", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bgood\s+(man|woman|sir|madam|master|mistress|lady|lord)\b",
                    $"good {neutralTitle}", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bGood\s+(man|woman|sir|madam|master|mistress|lady|lord)\b",
                    $"Good {neutralTitle}");

                // Individual word replacements
                processed = SirRegex.Replace(processed, neutralTitle);
                processed = SirCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                processed = MasterRegex.Replace(processed, neutralTitle);
                processed = MasterCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                processed = MistressRegex.Replace(processed, neutralTitle);
                processed = MistressCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                processed = MadamRegex.Replace(processed, neutralTitle);
                processed = MadamCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                processed = DameRegex.Replace(processed, neutralTitle);
                processed = DameCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                processed = LadyRegex.Replace(processed, neutralTitle);
                processed = LadyCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                processed = BrotherRegex.Replace(processed, neutralTitle);
                processed = BrotherCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                processed = SisterRegex.Replace(processed, neutralTitle);
                processed = SisterCapitalRegex.Replace(processed, capitalizedNeutralTitle);

                // Gendered nouns
                processed = WomanRegex.Replace(processed, neutralTitle);
                processed = WomanCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                processed = ManRegex.Replace(processed, neutralTitle);
                processed = ManCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                processed = Regex.Replace(processed, @"\bmen\b", neutralTitle + "s", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bMen\b", capitalizedNeutralTitle + "s");
                processed = Regex.Replace(processed, @"\bwomen\b", neutralTitle + "s", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bWomen\b", capitalizedNeutralTitle + "s");
                processed = GirlRegex.Replace(processed, neutralTitle);
                processed = GirlCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                processed = BoyRegex.Replace(processed, neutralTitle);
                processed = BoyCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                processed = LassRegex.Replace(processed, neutralTitle);
                processed = LassCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                processed = MaidenRegex.Replace(processed, neutralTitle);
                processed = MaidenCapitalRegex.Replace(processed, capitalizedNeutralTitle);
            }
            else
            {
                // Natural gendered terms - Lua hook with fallbacks
                if (pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase))
                {
                    // She/her gender selection
                    processed = Regex.Replace(processed, @"��man�woman", "woman", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"man�woman", "woman", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��woman�man", "woman", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"woman�man", "woman", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��men�women", "women", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"men�women", "women", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��women�men", "women", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"women�men", "women", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��sir�madam", "madam", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"sir�madam", "madam", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��madam�sir", "madam", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"madam�sir", "madam", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��Master�Mistress", "Mistress", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"Master�Mistress", "Mistress", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��Mistress�Master", "Mistress", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"Mistress�Master", "Mistress", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��lass�lad", "lass", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"lass�lad", "lass", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��lad�lass", "lass", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"lad�lass", "lass", RegexOptions.IgnoreCase);

                    processed = Regex.Replace(processed, @"\b(men|man)\s+(like\s+you)\b", "women like you", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bgood\s+(man|sir|master|lord)\b", "good woman", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bGood\s+(man|sir|master|lord)\b", "Good woman");

                    // Fallbacks
                    processed = Regex.Replace(processed, @"\bsir\b", "madam", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bSir\b", "Madam");
                    processed = Regex.Replace(processed, @"\bbrother\b", "sister", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bBrother\b", "Sister");
                    processed = Regex.Replace(processed, @"\bmaster\b", "mistress", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bMaster\b", "Mistress");
                    processed = Regex.Replace(processed, @"\bmen\b", "women", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bMen\b", "Women");
                    processed = Regex.Replace(processed, @"\bman\b", "woman", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bMan\b", "Woman");
                    processed = Regex.Replace(processed, @"\bhe\b", "she", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bHe\b", "She");
                    processed = Regex.Replace(processed, @"\bhim\b", "her", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bHim\b", "Her");
                    processed = Regex.Replace(processed, @"\bhis\b", "her", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bHis\b", "Her");
                }
                else if (pronounSet.Subject.Equals("he", StringComparison.OrdinalIgnoreCase))
                {
                    // He/him gender selection
                    processed = Regex.Replace(processed, @"��woman�man", "man", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"woman�man", "man", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��man�woman", "man", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"man�woman", "man", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��women�men", "men", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"women�men", "men", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��men�women", "men", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"men�women", "men", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��madam�sir", "sir", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"madam�sir", "sir", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��sir�madam", "sir", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"sir�madam", "sir", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��Mistress�Master", "Master", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"Mistress�Master", "Master", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��Master�Mistress", "Master", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"Master�Mistress", "Master", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��lass�lad", "lad", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"lass�lad", "lad", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��lad�lass", "lad", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"lad�lass", "lad", RegexOptions.IgnoreCase);

                    processed = Regex.Replace(processed, @"\b(women|woman)\s+(like\s+you)\b", "men like you", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bgood\s+(woman|madam|mistress|lady)\b", "good man", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bGood\s+(woman|madam|mistress|lady)\b", "Good man");

                    // Fallbacks
                    processed = Regex.Replace(processed, @"\bmadam\b", "sir", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bMadam\b", "Sir");
                    processed = Regex.Replace(processed, @"\bsister\b", "brother", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bSister\b", "Brother");
                    processed = Regex.Replace(processed, @"\bmistress\b", "master", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bMistress\b", "Master");
                    processed = Regex.Replace(processed, @"\bwomen\b", "men", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bWomen\b", "Men");
                    processed = Regex.Replace(processed, @"\bwoman\b", "man", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bWoman\b", "Man");
                    processed = Regex.Replace(processed, @"\bshe\b", "he", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bShe\b", "He");
                    processed = Regex.Replace(processed, @"\bher\b", "his", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bHer\b", "His");
                }
                else if (pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase))
                {
                    // They/them gender selection
                    processed = Regex.Replace(processed, @"��woman�man", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"woman�man", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��women�men", neutralTitle + "s", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"women�men", neutralTitle + "s", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��madam�sir", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"madam�sir", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��sir�madam", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"sir�madam", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��Mistress�Master", capitalizedNeutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"Mistress�Master", capitalizedNeutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��Master�Mistress", capitalizedNeutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"Master�Mistress", capitalizedNeutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��man�woman", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"man�woman", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��men�women", neutralTitle + "s", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"men�women", neutralTitle + "s", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��lass�lad", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"lass�lad", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��lad�lass", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"lad�lass", neutralTitle, RegexOptions.IgnoreCase);

                    processed = Regex.Replace(processed, @"\b(men|women|man|woman)\s+(like\s+you)\b",
                        $"{neutralTitle}s like you", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bgood\s+(man|woman|sir|madam|master|mistress|lady|lord)\b",
                        $"good {neutralTitle}", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bGood\s+(man|woman|sir|madam|master|mistress|lady|lord)\b",
                        $"Good {neutralTitle}");

                    // Neutral terms
                    processed = SirRegex.Replace(processed, neutralTitle);
                    processed = SirCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                    processed = MasterRegex.Replace(processed, neutralTitle);
                    processed = MasterCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                    processed = MistressRegex.Replace(processed, neutralTitle);
                    processed = MistressCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                    processed = MadamRegex.Replace(processed, neutralTitle);
                    processed = MadamCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                    processed = DameRegex.Replace(processed, neutralTitle);
                    processed = DameCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                    processed = LadyRegex.Replace(processed, neutralTitle);
                    processed = LadyCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                    processed = WomanRegex.Replace(processed, neutralTitle);
                    processed = WomanCapitalRegex.Replace(processed, capitalizedNeutralTitle);
                    processed = ManRegex.Replace(processed, neutralTitle);
                    processed = ManCapitalRegex.Replace(processed, capitalizedNeutralTitle);

                    // Plurals
                    processed = Regex.Replace(processed, @"\bmen\b", neutralTitle + "s", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bMen\b", capitalizedNeutralTitle + "s");
                    processed = Regex.Replace(processed, @"\bwomen\b", neutralTitle + "s", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bWomen\b", capitalizedNeutralTitle + "s");

                    // Fallback
                    processed = Regex.Replace(processed, @"\bvaliant men\b", $"valiant {neutralTitle}s", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bvaliant women\b", $"valiant {neutralTitle}s", RegexOptions.IgnoreCase);
                }
            }

            // Verb conjugation (they/them only)
            if (pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var kvp in ConjugationPatterns)
                {
                    processed = kvp.Key.Replace(processed, kvp.Value);
                }
            }

            return processed;
        }

        /// <summary>Lua variable detour for pronoun gender forcing.</summary>
        private byte GetLuaVarDetour(nint poolBase, IntPtr a2, IntPtr a3)
        {
            try
            {
                var activeCharacter = plugin.GetActiveCharacter();
                if (activeCharacter?.RPProfile?.Pronouns != null && plugin.Configuration.EnableDialogueIntegration)
                {
                    var pronounSet = PronounParser.Parse(activeCharacter.RPProfile.Pronouns);
                    var oldGender = GetLuaVarGender(poolBase);
                    int newGender = oldGender;

                    // Force gender variant based on pronouns
                    if (pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase))
                    {
                        newGender = 1;
                        log.Info($"[Lua] Forcing female variant for she/her pronouns");
                    }
                    else if (pronounSet.Subject.Equals("he", StringComparison.OrdinalIgnoreCase))
                    {
                        newGender = 0;
                    }
                    else if (pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase))
                    {
                        newGender = 1;
                    }

                    if (newGender != oldGender)
                    {
                        SetLuaVarGender(poolBase, newGender);
                        var returnValue = getLuaVarHook!.Original(poolBase, a2, a3);
                        SetLuaVarGender(poolBase, oldGender);
                        return returnValue;
                    }
                }

                return getLuaVarHook!.Original(poolBase, a2, a3);
            }
            catch (Exception ex)
            {
                log.Error($"[Dialogue] Error in GetLuaVarDetour: {ex.Message}");
                return getLuaVarHook!.Original(poolBase, a2, a3);
            }
        }

        /// <summary>Cutscene voice gender detour.</summary>
        private int GetCutVoGenderDetour(nint a1, nint a2)
        {
            try
            {
                var originalRet = getCutVoGenderHook!.Original(a1, a2);

                if (!plugin.Configuration.ReplacePronounsInDialogue)
                    return originalRet;

                var character = plugin.GetActiveCharacter();
                if (character?.RPProfile?.Pronouns == null)
                    return originalRet;

                var pronounSet = PronounParser.Parse(character.RPProfile.Pronouns);

                // Only modify audio for he/him and she/her
                if (pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        uint lang = 1;
                        try
                        {
                            var frameworkLang = Framework.Instance()->EnvironmentManager->CutsceneMovieVoice;
                            if (frameworkLang >= 0 && frameworkLang < 10)
                                lang = (uint)frameworkLang;
                        }
                        catch
                        {
                            lang = 1;
                        }

                        // Voice file existence check
                        unsafe
                        {
                            if (a2 != nint.Zero)
                            {
                                var offset = *(int*)(a2 + 0x1C);
                                var voiceCheck = *(int*)(a2 + offset + (12 * lang));
                                return voiceCheck != 1 ? 1 : 0;
                            }
                        }
                    }
                    catch
                    {
                        return originalRet;
                    }
                }
                else if (pronounSet.Subject.Equals("he", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        uint lang = 0;

                        unsafe
                        {
                            if (a2 != nint.Zero)
                            {
                                var offset = *(int*)(a2 + 0x1C);
                                var voiceCheck = *(int*)(a2 + offset + (12 * lang));
                                return voiceCheck != 1 ? 0 : originalRet;
                            }
                        }
                    }
                    catch
                    {
                        return originalRet;
                    }
                }

                // Preserve original for they/them and others
                return originalRet;
            }
            catch (Exception ex)
            {
                log.Error($"[Dialogue] Error in GetCutVoGenderDetour: {ex.Message}");
                return getCutVoGenderHook!.Original(a1, a2);
            }
        }

        // Lua gender helpers
        private int GetLuaVarGender(nint poolBase) => *(int*)(poolBase + 4 * 0x1B);
        private void SetLuaVarGender(nint poolBase, int gender) => *(int*)(poolBase + 4 * 0x1B) = gender;

        /// <summary>Name replacement with two-name handling.</summary>
        private void HandleNameReplacement(ref byte* text, Character character)
        {
            try
            {
                var playerName = clientState.LocalPlayer?.Name.TextValue;
                if (string.IsNullOrEmpty(playerName)) return;

                var textSpan = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text);
                if (!textSpan.Contains((byte)0x02)) return;

                var seString = Dalamud.Game.Text.SeStringHandling.SeString.Parse(text, textSpan.Length);
                var payloads = seString.Payloads;
                bool replaced = false;

                var csCharacterName = character.Name;
                var nameParts = csCharacterName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string csFirstName = "";
                string csLastName = "";

                if (nameParts.Length == 1)
                {
                    csFirstName = nameParts[0];
                    csLastName = "";
                }
                else if (nameParts.Length == 2)
                {
                    csFirstName = nameParts[0];
                    csLastName = nameParts[1];
                }
                else if (nameParts.Length > 2)
                {
                    csFirstName = nameParts[0];
                    csLastName = string.Join(" ", nameParts.Skip(1));
                }

                for (int i = 0; i < payloads.Count; i++)
                {
                    var payload = payloads[i];
                    if (payload.Type == Dalamud.Game.Text.SeStringHandling.PayloadType.Unknown)
                    {
                        var payloadBytes = payload.Encode();
                        var payloadHex = Convert.ToHexString(payloadBytes);

                        if (payloadHex.Contains(Convert.ToHexString(FullNameBytes)))
                        {
                            payloads[i] = new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(csCharacterName);
                            replaced = true;
                            log.Debug($"[Name] Replaced full name with: {csCharacterName}");
                        }
                        else if (payloadHex.Contains(Convert.ToHexString(FirstNameBytes)))
                        {
                            payloads[i] = new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(csFirstName);
                            replaced = true;
                            log.Debug($"[Name] Replaced first name with: {csFirstName}");
                        }
                        else if (payloadHex.Contains(Convert.ToHexString(LastNameBytes)))
                        {
                            if (!string.IsNullOrEmpty(csLastName))
                            {
                                payloads[i] = new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(csLastName);
                                replaced = true;
                                log.Debug($"[Name] Replaced last name with: {csLastName}");
                            }
                            else
                            {
                                // No last name - use first name
                                payloads[i] = new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(csFirstName);
                                replaced = true;
                                log.Debug($"[Name] No last name available, used first name: {csFirstName}");
                            }
                        }
                    }
                }

                if (!replaced) return;

                var newBytes = seString.EncodeWithNullTerminator();
                var originalLength = textSpan.Length + 1;

                if (newBytes.Length <= originalLength)
                    newBytes.CopyTo(new Span<byte>(text, originalLength));
                else
                {
                    var newText = (byte*)Marshal.AllocHGlobal(newBytes.Length);
                    newBytes.CopyTo(new Span<byte>(newText, newBytes.Length));
                    text = newText;
                }
            }
            catch (Exception ex)
            {
                log.Warning($"[Dialogue] Name replacement failed: {ex.Message}");
            }
        }

        private void SafeSetString(Utf8String* stringStruct, string newText)
        {
            try
            {
                if (stringStruct != null && !string.IsNullOrEmpty(newText))
                {
                    stringStruct->SetString(newText);
                }
            }
            catch (Exception ex)
            {
                log.Error($"[Dialogue] Failed to set string: {ex.Message}");
            }
        }

        private static bool ByteArrayEquals(ReadOnlySpan<byte> a1, ReadOnlySpan<byte> a2) => a1.SequenceEqual(a2);

        /// <summary>Direct gender pattern replacement.</summary>
        private string ProcessDirectGenderPatterns(string text, PronounSet pronounSet, Character activeCharacter)
        {
            var processed = text;
            var neutralTitle = "adventurer";
            if (plugin.Configuration.EnableAdvancedTitleReplacement)
                neutralTitle = plugin.Configuration.GetGenderNeutralTitle().ToLower();
            var capitalizedNeutralTitle = char.ToUpper(neutralTitle[0]) + neutralTitle.Substring(1);

            bool hasGenderSelectionPatterns = 
                text.Contains("��woman�man") || text.Contains("woman�man") ||
                text.Contains("��man�woman") || text.Contains("man�woman") ||
                text.Contains("��women�men") || text.Contains("women�men") ||
                text.Contains("��men�women") || text.Contains("men�women") ||
                text.Contains("��sir�madam") || text.Contains("sir�madam") ||
                text.Contains("��madam�sir") || text.Contains("madam�sir") ||
                text.Contains("��Master�Mistress") || text.Contains("Master�Mistress") ||
                text.Contains("��Mistress�Master") || text.Contains("Mistress�Master") ||
                text.Contains("��lad�lass") || text.Contains("lad�lass") ||
                text.Contains("��lass�lad") || text.Contains("lass�lad");

            if (hasGenderSelectionPatterns)
            {
                // Neutral terms for they/them
                if (plugin.Configuration.ReplaceGenderedTerms || pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase))
                {
                    processed = Regex.Replace(processed, @"��woman�man|woman�man", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��man�woman|man�woman", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��women�men|women�men", neutralTitle + "s", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��men�women|men�women", neutralTitle + "s", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��sir�madam|sir�madam", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��madam�sir|madam�sir", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��Master�Mistress|Master�Mistress", capitalizedNeutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��Mistress�Master|Mistress�Master", capitalizedNeutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��lad�lass|lad�lass", neutralTitle, RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��lass�lad|lass�lad", neutralTitle, RegexOptions.IgnoreCase);
                }
                else if (pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase))
                {
                    // Female variants
                    processed = Regex.Replace(processed, @"��man�woman|man�woman", "woman", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��woman�man|woman�man", "woman", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��men�women|men�women", "women", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��women�men|women�men", "women", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��sir�madam|sir�madam", "madam", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��madam�sir|madam�sir", "madam", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��Master�Mistress|Master�Mistress", "Mistress", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��Mistress�Master|Mistress�Master", "Mistress", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��lad�lass|lad�lass", "lass", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��lass�lad|lass�lad", "lass", RegexOptions.IgnoreCase);
                }
                else if (pronounSet.Subject.Equals("he", StringComparison.OrdinalIgnoreCase))
                {
                    // Male variants
                    processed = Regex.Replace(processed, @"��woman�man|woman�man", "man", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��man�woman|man�woman", "man", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��women�men|women�men", "men", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��men�women|men�women", "men", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��madam�sir|madam�sir", "sir", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��sir�madam|sir�madam", "sir", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��Mistress�Master|Mistress�Master", "Master", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��Master�Mistress|Master�Mistress", "Master", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��lass�lad|lass�lad", "lad", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"��lad�lass|lad�lass", "lad", RegexOptions.IgnoreCase);
                }

                return processed;
            }

            // No gender selection patterns - return original
            return text;
        }

        /// <summary>Grammar fixes for they/them.</summary>
        private string ApplyGrammarFixes(string text, PronounSet pronounSet)
        {
            if (!pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase))
                return text;

            var processed = text;

            foreach (var kvp in ConjugationPatterns)
                processed = kvp.Key.Replace(processed, kvp.Value);

            // Verb fixes
            processed = Regex.Replace(processed, @"\bthey\s+has\b", "they have", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"\bthey\s+is\b", "they are", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"\bthey\s+was\b", "they were", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"\bthey\s+does\b", "they do", RegexOptions.IgnoreCase);

            // Contractions
            processed = Regex.Replace(processed, @"\bthey's\s+been\b", "they've been", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"\bthey's\s+going\b", "they're going", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"\bthey's\s+a\b", "they're a", RegexOptions.IgnoreCase);

            return processed;
        }

        /// <summary>Fallback processing for cases without direct patterns.</summary>
        private int ProcessWithGenderParameters(RaptureTextModule* textModule, byte* text, void* decoder, Utf8String* stringStruct, PronounSet pronounSet)
        {
            var result = getStringHook!.Original(textModule, text, decoder, stringStruct);

            if (stringStruct != null && stringStruct->BufUsed > 0)
            {
                var gameGeneratedText = stringStruct->ToString();

                if (!IsDefinitelyNotDialogue(gameGeneratedText) && !string.IsNullOrEmpty(gameGeneratedText) &&
                    gameGeneratedText.Length > 15 && !gameGeneratedText.Contains("0x") && gameGeneratedText.Contains(" "))
                {
                    var processed = ProcessPronounsAndTitles(gameGeneratedText, pronounSet, plugin.GetActiveCharacter());

                    if (pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase))
                        processed = ApplyGrammarFixes(processed, pronounSet);

                    if (processed != gameGeneratedText)
                    {
                        SafeSetString(stringStruct, processed);
                        log.Info($"[Dialogue] Fallback Processing: '{gameGeneratedText}' -> '{processed}'");
                    }
                }
            }

            return result;
        }

        /// <summary>Determines if SeString should be processed.</summary>
        private bool ShouldProcessSeString(ReadOnlySpan<byte> textSpan, string textString)
        {
            if (IsDefinitelyNotDialogue(textString))
                return false;

            try
            {
                var seString = Dalamud.Game.Text.SeStringHandling.SeString.Parse(textSpan);

                // Check for player name or gender parameter payloads
                foreach (var payload in seString.Payloads)
                {
                    if (payload.Type == Dalamud.Game.Text.SeStringHandling.PayloadType.Unknown)
                    {
                        var payloadBytes = payload.Encode();
                        var payloadHex = Convert.ToHexString(payloadBytes);

                        // Player name patterns
                        if (payloadHex.Contains(Convert.ToHexString(FirstNameBytes)) ||
                            payloadHex.Contains(Convert.ToHexString(LastNameBytes)) ||
                            payloadHex.Contains(Convert.ToHexString(FullNameBytes)))
                            return true;

                        // Gender parameter patterns
                        if (payloadHex.Contains("022003") || payloadHex.Contains("022C0D") || payloadHex.Contains("022903"))
                            return true;
                    }
                }

                return textString.Contains("�") ||
                       HasPlayerSpecificPattern(textString) ||
                       (!IsNPCReference(textString) && ContainsGenderTerms(textString));
            }
            catch
            {
                return textString.Contains("�") && !IsNPCReference(textString);
            }
        }

        private bool ContainsGenderTerms(string text)
        {
            var genderTerms = new[] { "sir", "madam", "master", "mistress", "man", "woman", "his", "her", "he", "she" };
            return genderTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>SeString-based name replacement.</summary>
        private void ProcessNameReplacementSeString(ref byte* text, Character activeCharacter, int originalLength)
        {
            try
            {
                var textSpan = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text);
                var seString = Dalamud.Game.Text.SeStringHandling.SeString.Parse(text, textSpan.Length);
                var payloads = seString.Payloads;
                bool replaced = false;

                var csCharacterName = activeCharacter.Name;
                var nameParts = csCharacterName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string csFirstName = nameParts.Length > 0 ? nameParts[0] : "";
                string csLastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : "";

                for (int i = 0; i < payloads.Count; i++)
                {
                    if (payloads[i].Type == Dalamud.Game.Text.SeStringHandling.PayloadType.Unknown)
                    {
                        var payloadBytes = payloads[i].Encode();
                        var payloadHex = Convert.ToHexString(payloadBytes);

                        // Check FirstNameBytes and LastNameBytes before FullNameBytes (FirstNameBytes contains FullNameBytes)
                        if (payloadHex.Contains(Convert.ToHexString(FirstNameBytes)))
                        {
                            payloads[i] = new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(csFirstName);
                            replaced = true;
                        }
                        else if (payloadHex.Contains(Convert.ToHexString(LastNameBytes)))
                        {
                            var nameToUse = !string.IsNullOrEmpty(csLastName) ? csLastName : csFirstName;
                            payloads[i] = new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(nameToUse);
                            replaced = true;
                        }
                        else if (payloadHex.Contains(Convert.ToHexString(FullNameBytes)))
                        {
                            payloads[i] = new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(csCharacterName);
                            replaced = true;
                        }
                    }
                }

                if (!replaced) return;

                var newBytes = seString.EncodeWithNullTerminator();
                if (newBytes.Length <= originalLength + 1)
                {
                    newBytes.CopyTo(new Span<byte>(text, originalLength + 1));
                }
                else
                {
                    var newText = (byte*)Marshal.AllocHGlobal(newBytes.Length);
                    newBytes.CopyTo(new Span<byte>(newText, newBytes.Length));
                    text = newText;
                }
            }
            catch (Exception ex)
            {
                log.Warning($"[Dialogue] Name replacement failed: {ex.Message}");
            }
        }

        // TODO: Implement gender parameter manipulation when needed

        /// <summary>Simplified pronoun processing.</summary>
        private string ProcessPronounsSimplified(string text, PronounSet pronounSet)
        {
            var processed = text;

            if (pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase))
            {
                // She/her
                processed = Regex.Replace(processed, @"\bhe\b", "she", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bhis\b", "her", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bhim\b", "her", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bman\b", "woman", RegexOptions.IgnoreCase);
            }
            else if (pronounSet.Subject.Equals("he", StringComparison.OrdinalIgnoreCase))
            {
                // He/him
                processed = Regex.Replace(processed, @"\bshe\b", "he", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bher\b", "his", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bwoman\b", "man", RegexOptions.IgnoreCase);
            }
            else if (pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase))
            {
                // They/them
                var neutralTitle = "adventurer";
                processed = Regex.Replace(processed, @"\bhe\b", "they", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bshe\b", "they", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bhis\b", "their", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bher\b", "their", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bhim\b", "them", RegexOptions.IgnoreCase);

                // Neutral titles
                processed = Regex.Replace(processed, @"\bwoman\b", neutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bWoman\b", char.ToUpper(neutralTitle[0]) + neutralTitle.Substring(1));
                processed = Regex.Replace(processed, @"\bman\b", neutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bMan\b", char.ToUpper(neutralTitle[0]) + neutralTitle.Substring(1));
                processed = Regex.Replace(processed, @"\bsir\b", neutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bSir\b", char.ToUpper(neutralTitle[0]) + neutralTitle.Substring(1));
                processed = Regex.Replace(processed, @"\bmadam\b", neutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bMadam\b", char.ToUpper(neutralTitle[0]) + neutralTitle.Substring(1));
                processed = Regex.Replace(processed, @"\bmaster\b", neutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bMaster\b", char.ToUpper(neutralTitle[0]) + neutralTitle.Substring(1));
                processed = Regex.Replace(processed, @"\bmistress\b", neutralTitle, RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bMistress\b", char.ToUpper(neutralTitle[0]) + neutralTitle.Substring(1));

                // Plurals
                processed = Regex.Replace(processed, @"\bmen\b", neutralTitle + "s", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bMen\b", char.ToUpper(neutralTitle[0]) + neutralTitle.Substring(1) + "s");
                processed = Regex.Replace(processed, @"\bwomen\b", neutralTitle + "s", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bWomen\b", char.ToUpper(neutralTitle[0]) + neutralTitle.Substring(1) + "s");

                // Article fixes
                processed = Regex.Replace(processed, @"\bA adventurer\b", "An adventurer", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\ba adventurer\b", "an adventurer");

                foreach (var kvp in ConjugationPatterns)
                    processed = kvp.Key.Replace(processed, kvp.Value);

                // Grammar fixes
                processed = Regex.Replace(processed, @"\bthey\s+has\b", "they have", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bthey\s+is\b", "they are", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bthey\s+was\b", "they were", RegexOptions.IgnoreCase);
            }

            return processed;
        }


        /// <summary>Logs discovered flag patterns for analysis.</summary>
        private void LogFlagDiscovery(byte flagId, FlagInfo flagInfo, byte[] data, int flagPosition)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var context = GetContextAroundFlag(data, flagPosition);

                string category = flagId switch
                {
                    0x0C => "Pronoun_Subject", // she/he
                    0x0D => "Pronoun_Possessive", // her/his/him
                    0x0E => "Title_Address", // miss/sir
                    0x0F => "Person_Identifier", // woman/man
                    0x11 => "Title_Formal", // Miss/Mister
                    0x14 => "Family_Relation", // sister/brother
                    0x15 => "Title_Master", // Mistress/Master
                    0x29 => "Phrase_Complex", // taken with/in awe of
                    0x2B => "Phrase_Princess", // princess/prince
                    0x31 => "Phrase_Appearance", // look fine/dashing
                    _ => "Unknown"
                };

                bool isKnown = GetReplacementForFlag(flagId, PronounParser.Parse("they/them"), plugin.Configuration, data, flagPosition, flagInfo) != null;

                var logEntry = new
                {
                    Timestamp = timestamp,
                    FlagId = $"0x{flagId:X2}",
                    FemaleWord = flagInfo.FemaleWord,
                    MaleWord = flagInfo.MaleWord,
                    Category = category,
                    SuggestedNeutral = isKnown ? GetReplacementForFlag(flagId, PronounParser.Parse("they/them"), plugin.Configuration, data, flagPosition, flagInfo) : "[needs manual review]",
                    Context = context,
                    Notes = flagInfo.FemaleWord.Length != flagInfo.MaleWord.Length ? $"Length mismatch: {flagInfo.FemaleWord.Length} vs {flagInfo.MaleWord.Length}" : "",
                    IsKnown = isKnown
                };

                var jsonPath = @"F:\CS+\FFXIV_Flag_Discovery.json";
                var json = System.Text.Json.JsonSerializer.Serialize(logEntry, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.AppendAllText(jsonPath, json + ",\n");

                UpdateFlagDiscoverySummary(flagId, flagInfo, category, isKnown);
            }
            catch (Exception ex)
            {
                log.Warning($"[FLAG DISCOVERY] Failed to log flag: {ex.Message}");
            }
        }
        
        /// <summary>Appends new flag discoveries to summary file.</summary>
        private void UpdateFlagDiscoverySummary(byte flagId, FlagInfo flagInfo, string category, bool isKnown)
        {
            try
            {
                var summaryPath = @"F:\CS+\FFXIV_Flag_Discovery_Summary.txt";
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var status = isKnown ? "KNOWN" : "NEW!";

                var summaryLine = $"[{timestamp}] [{status}] Flag 0x{flagId:X2} ({category}): '{flagInfo.FemaleWord}' / '{flagInfo.MaleWord}'\n";

                if (!isKnown && !File.Exists(summaryPath) || !File.ReadAllText(summaryPath).Contains($"Flag 0x{flagId:X2}"))
                    File.AppendAllText(summaryPath, summaryLine);
            }
            catch { }
        }

        /// <summary>Disposes hooks.</summary>
        public void Dispose()
        {
            getStringHook?.Disable();
            getStringHook?.Dispose();
            getLuaVarHook?.Disable();
            getLuaVarHook?.Dispose();
            processTextHook?.Disable();
            processTextHook?.Dispose();
            getCutVoGenderHook?.Disable();
            getCutVoGenderHook?.Dispose();
        }
    }
}
