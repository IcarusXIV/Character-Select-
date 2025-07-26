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

        // Hook delegates - Lua hooks for they/them support
        private delegate int GetStringPrototype(RaptureTextModule* textModule, byte* text, void* decoder, Utf8String* stringStruct);
        private Hook<GetStringPrototype>? getStringHook;

        private delegate byte GetLuaVarPrototype(nint poolBase, nint a2, nint a3);
        private Hook<GetLuaVarPrototype>? getLuaVarHook;

        // Name replacement byte patterns (from PrefPro)
        private static readonly byte[] FullNameBytes = { 0x02, 0x29, 0x03, 0xEB, 0x02, 0x03 };
        private static readonly byte[] FirstNameBytes = { 0x02, 0x2C, 0x0D, 0xFF, 0x07, 0x02, 0x29, 0x03, 0xEB, 0x02, 0x03, 0xFF, 0x02, 0x20, 0x02, 0x03 };
        private static readonly byte[] LastNameBytes = { 0x02, 0x2C, 0x0D, 0xFF, 0x07, 0x02, 0x29, 0x03, 0xEB, 0x02, 0x03, 0xFF, 0x02, 0x20, 0x03, 0x03 };

        // Comprehensive verb conjugation patterns for they/them
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

        // Pre-compiled regex patterns for better performance
        private static readonly Regex HeRegex = new Regex(@"\bhe\b", RegexOptions.Compiled);
        private static readonly Regex HeCapitalRegex = new Regex(@"\bHe\b", RegexOptions.Compiled);
        private static readonly Regex SheRegex = new Regex(@"\bshe\b", RegexOptions.Compiled);
        private static readonly Regex SheCapitalRegex = new Regex(@"\bShe\b", RegexOptions.Compiled);
        private static readonly Regex HisRegex = new Regex(@"\bhis\b", RegexOptions.Compiled);
        private static readonly Regex HisCapitalRegex = new Regex(@"\bHis\b", RegexOptions.Compiled);
        private static readonly Regex LadRegex = new Regex(@"\blad\b", RegexOptions.Compiled);
        private static readonly Regex LadCapitalRegex = new Regex(@"\bLad\b", RegexOptions.Compiled);
        // Context-aware "her" patterns - distinguishes possessive vs object
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

            // Add Lua hook for they/them gender forcing
            try
            {
                var getLuaVar = "E8 ?? ?? ?? ?? 48 85 DB 74 1B 48 8D 8F";
                var getLuaVarPtr = sigScanner.ScanText(getLuaVar);
                getLuaVarHook = gameInteropProvider.HookFromAddress<GetLuaVarPrototype>(getLuaVarPtr, GetLuaVarDetour);
                getLuaVarHook.Enable();
                log.Info("[Dialogue] Lua gender override hook enabled.");
            }
            catch (Exception ex)
            {
                log.Warning($"[Dialogue] Failed to initialize Lua hooks: {ex.Message}");
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

        private bool IsChat(string textString)
        {
            if (string.IsNullOrEmpty(textString)) return false;

            // These are dialogue
            if (textString.Contains("woman�man") ||
                textString.Contains("women�men") ||
                textString.Contains("herself�himself"))
                return false;

            // Skip if it's clearly chat log content, not live dialogue
            // Long narrative text with ellipses and chat markers
            if (textString.Contains("����") &&
                textString.Contains("...") &&
                textString.Length > 150)
                return true;

            // Specific narrative patterns that indicate chat log content
            if (textString.Contains("...") && textString.Length > 100 && (
                textString.Contains("they carved") ||
                textString.Contains("did they put") ||
                textString.Contains("tempestuous winds") ||
                textString.Contains("dread wyrm") ||
                textString.Contains("hard-fought victory") ||
                textString.Contains("secrets laid bare")))
                return true;

            // Standard chat patterns
            if (textString.Contains("[Mare Synchronos]") ||
                textString.Contains("[Mare]") ||
                textString.Contains("Mare:") ||
                textString.Contains("is now online") ||
                textString.Contains("is now offline"))
                return true;

            if (textString.StartsWith("H") && textString.EndsWith("H") && textString.Length > 2)
                return true;

            var cleanText = Regex.Replace(textString, @"[^\u0020-\u007E]", "");
            if (Regex.IsMatch(cleanText, @"^[A-Z][a-z]+\s+[A-Z][a-z]+\s*:"))
                return true;
            if (Regex.IsMatch(textString, @"^[A-Z][a-z]+\s+[A-Z][a-z]+\s*:"))
                return true;

            if (textString.StartsWith("[") ||
                textString.StartsWith("<") ||
                textString.StartsWith("/") ||
                textString.Contains(">>") ||
                textString.Contains("<<") ||
                textString.Contains(" says") ||
                textString.Contains(" tells") ||
                Regex.IsMatch(textString, @"^\s*\[.*?\]"))
                return true;

            var text = textString.Trim();
            if (text.Length < 50)
            {
                if (text.StartsWith("...") || text.EndsWith("...")) return true;
                if (text.StartsWith("*") || text.EndsWith("*")) return true;
                if (text.StartsWith("(") && text.EndsWith(")")) return true;
                if (text.Contains("((") || text.Contains("))")) return true;
            }

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

        // Main text processing detour
        private int GetStringDetour(RaptureTextModule* textModule, byte* text, void* decoder, Utf8String* stringStruct)
        {

            try
            {
                var textSpan = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text);
                var textString = System.Text.Encoding.UTF8.GetString(textSpan);

                if (!plugin.Configuration.EnableDialogueIntegration)
                    return getStringHook!.Original(textModule, text, decoder, stringStruct);

                var activeCharacter = plugin.GetActiveCharacter();
                if (activeCharacter?.RPProfile?.Pronouns == null)
                    return getStringHook!.Original(textModule, text, decoder, stringStruct);

                // Only process text with gender/name flags (0x02)
                if (!textSpan.Contains((byte)0x02))
                {
                    // DEBUG: Log text without 0x02 flags that contains gendered terms
                    if (textString.Contains("sir") || textString.Contains("lady") || textString.Contains("master") || textString.Contains("mistress"))
                    {
                    }
                    return getStringHook!.Original(textModule, text, decoder, stringStruct);
                }

                // Skip if text contains emote patterns
                if (ContainsEmotePattern(textString))
                {
                    return getStringHook!.Original(textModule, text, decoder, stringStruct);
                }

                // Check for player tags in original text before processing
                var originalHex = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(textString));
                
                // Use hex flags
                bool hasPlayerNameFlags = originalHex.Contains("022C0D") || originalHex.Contains("022903");
                bool hasDirectPlayerFlags = originalHex.Contains("022003");
                bool mentionsPlayerName = textString.Contains(activeCharacter.Name) ||
                                         textString.Contains(activeCharacter.Name.Split(' ')[0]);

                // Detect visible gender selection patterns
                bool hasVisibleGenderSelection =
                    // Check for both words in same string
                    (textString.Contains("women") && textString.Contains("men")) ||
                    (textString.Contains("woman") && textString.Contains("man")) ||
                    (textString.Contains("Mistress") && textString.Contains("Master")) ||
                    (textString.Contains("herself") && textString.Contains("himself")) ||
                    (textString.Contains("her") && textString.Contains("his") && textString.Length < 200) || // Short text with both pronouns
                                                                                                             // Look for separator characters that indicate selections
                    textString.Contains("�") || // Any gender selection separator
                                                // Specific patterns seen in logs
                    textString.Contains("��madam�sir") ||
                    textString.Contains("madam�sir") ||
                    // Original patterns as backup
                    textString.Contains("��women�men") ||
                    textString.Contains("women�men") ||
                    textString.Contains("��woman�man") ||
                    textString.Contains("woman�man") ||
                    textString.Contains("��Mistress�Master") ||
                    textString.Contains("Mistress�Master");

                // Add player-specific pattern detection
                bool hasPlayerSpecificPattern = HasPlayerSpecificPattern(textString);

                // Check if this is an NPC reference that shouldn't be changed
                bool isNPCReference = IsNPCReference(textString);

                // Replace pronouns if player indicators detected & it's not an NPC reference
                bool shouldReplacePronouns = (hasDirectPlayerFlags || mentionsPlayerName ||
            hasVisibleGenderSelection || hasPlayerSpecificPattern) && !isNPCReference;


                var pronounSet = PronounParser.Parse(activeCharacter.RPProfile.Pronouns);

                
                // Skip chat messages
                if (IsChat(textString))
                    return getStringHook!.Original(textModule, text, decoder, stringStruct);

                // Skip UI elements
                if (IsUIElement(textString))
                    return getStringHook!.Original(textModule, text, decoder, stringStruct);

                // Only process if we're in a cutscene or it looks like proper dialogue
                if (!IsInCutscene() && textString.Length < 30)
                    return getStringHook!.Original(textModule, text, decoder, stringStruct);

                // Handle name replacement first (exactly like bestie, PrefPro)
                if (plugin.Configuration.ReplaceNameInDialogue && !string.IsNullOrEmpty(activeCharacter.Name))
                    HandleNameReplacement(ref text, activeCharacter);

                // Process the result
                var result = getStringHook!.Original(textModule, text, decoder, stringStruct);

                // Post process: Direct string replacement for pronouns
                if (stringStruct != null && stringStruct->BufUsed > 0 && shouldReplacePronouns)
                {
                    var gameGeneratedText = stringStruct->ToString();

                    // Skip if post-processed text contains emote patterns
                    if (ContainsEmotePattern(gameGeneratedText))
                    {
                        log.Info($"[EMOTE SKIP POST] Skipping emote in post-process: '{gameGeneratedText.Substring(0, Math.Min(50, gameGeneratedText.Length))}'");
                        return result;
                    }

                    // Safety net for post-processing
                    if (IsDefinitelyChat(gameGeneratedText))
                    {
                        return result;
                    }

                    // Only process readable text that looks like dialogue
                    if (!string.IsNullOrEmpty(gameGeneratedText) &&
                        gameGeneratedText.Length > 15 &&
                        !gameGeneratedText.Contains("0x") &&
                        !gameGeneratedText.Contains("+") &&
                        gameGeneratedText.Contains(" ") &&
                        !IsUIElement(gameGeneratedText) &&
                        !IsChat(gameGeneratedText))
                    {
                       

                        // Process pronouns
                        var processed = ProcessPronounsAndTitles(gameGeneratedText, pronounSet, activeCharacter);

                        if (processed != gameGeneratedText)
                        {
                            log.Info($"[Dialogue] CHANGED: '{gameGeneratedText}' -> '{processed}'");
                            SafeSetString(stringStruct, processed);
                        }
                        else if (gameGeneratedText.Contains("men") || gameGeneratedText.Contains("sir") ||
                                 gameGeneratedText.Contains("woman") || gameGeneratedText.Contains("master"))
                        {
                        }
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

        // Process all pronouns using PronounSet
        private string ProcessPronounsAndTitles(string text, PronounSet pronounSet, Character activeCharacter)
        {
            var processed = text;
            if (Regex.IsMatch(text, @"\b[A-Z][a-z]{4,}\s+and\s+(his|her|their)\b"))
            {
                return processed; // Don't replace pronouns in NPC contexts
            }
            // Skip if text contains flag codes
            if (text.Contains("+0%") || text.Contains("0x") || text.Length < 10)
                return processed;

            // Skip if text contains emote patterns
            if (ContainsEmotePattern(text))
                return processed;

            // Skip UI elements
            if (IsUIElement(text))
                return processed;

            // Skip if this is clearly an NPC reference
            if (IsNPCReference(text))
            {
                return processed;
            }

            // Get neutral title for replacements
            var neutralTitle = "adventurer";
            if (plugin.Configuration.EnableAdvancedTitleReplacement)
            {
                neutralTitle = plugin.Configuration.GetGenderNeutralTitle().ToLower();
            }
            var capitalizedNeutralTitle = char.ToUpper(neutralTitle[0]) + neutralTitle.Substring(1);


            // Replace pronouns using PronounSet
            var subjectLower = pronounSet.Subject.ToLower();
            var subjectCapital = char.ToUpper(pronounSet.Subject[0]) + pronounSet.Subject.Substring(1).ToLower();
            var possessiveLower = pronounSet.Possessive.ToLower();
            var possessiveCapital = char.ToUpper(pronounSet.Possessive[0]) + pronounSet.Possessive.Substring(1).ToLower();
            var objectLower = pronounSet.Object.ToLower();
            var objectCapital = char.ToUpper(pronounSet.Object[0]) + pronounSet.Object.Substring(1).ToLower();
            var reflexiveLower = pronounSet.Reflexive.ToLower();
            var reflexiveCapital = char.ToUpper(pronounSet.Reflexive[0]) + pronounSet.Reflexive.Substring(1).ToLower();

            // Replace basic pronouns - only if ReplacePronounsInDialogue is enabled
            if (plugin.Configuration.ReplacePronounsInDialogue)
            {
                processed = HeRegex.Replace(processed, subjectLower);
                processed = HeCapitalRegex.Replace(processed, subjectCapital);
                processed = SheRegex.Replace(processed, subjectLower);
                processed = SheCapitalRegex.Replace(processed, subjectCapital);

                // Replace possessive pronouns
                processed = HisRegex.Replace(processed, possessiveLower);
                processed = HisCapitalRegex.Replace(processed, possessiveCapital);

                // Context-aware "her" replacement
                processed = HerPossessiveRegex.Replace(processed, possessiveLower);
                processed = HerPossessiveCapitalRegex.Replace(processed, possessiveCapital);
                processed = HerPossessiveLowerRegex.Replace(processed, possessiveLower);

                // Object "her" -> object pronoun
                processed = HerObjectRegex.Replace(processed, objectLower);
                processed = HerObjectCapitalRegex.Replace(processed, objectCapital);

                // Object pronouns
                processed = HimRegex.Replace(processed, objectLower);
                processed = HimCapitalRegex.Replace(processed, objectCapital);

                // Reflexive pronouns
                processed = HimselfRegex.Replace(processed, reflexiveLower);
                processed = HimselfCapitalRegex.Replace(processed, reflexiveCapital);
                processed = HerselfRegex.Replace(processed, reflexiveLower);
                processed = HerselfCapitalRegex.Replace(processed, reflexiveCapital);
            }

            // Title replacement
            if (plugin.Configuration.ReplaceGenderedTerms)
            {
                // Handle gender selection patterns with separators
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

                // Handle player-specific patterns with context-aware replacement
                processed = Regex.Replace(processed, @"\b(men|women|man|woman)\s+(like\s+you)\b",
                    $"{neutralTitle}s like you", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bgood\s+(man|woman|sir|madam|master|mistress|lady|lord)\b",
                    $"good {neutralTitle}", RegexOptions.IgnoreCase);
                processed = Regex.Replace(processed, @"\bGood\s+(man|woman|sir|madam|master|mistress|lady|lord)\b",
                    $"Good {neutralTitle}");

                // Handle individual words as before (user wants neutral terms) - only if not NPC reference
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

                // Replace gendered titles/nouns (only when neutral terms enabled)
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
                // Natural gendered terms - let Lua hook do the work, with fallbacks
                if (pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase))
                {
                    // Handle gender selection patterns for she/her
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
                    // Handle player-specific patterns
                    processed = Regex.Replace(processed, @"\b(men|man)\s+(like\s+you)\b", "women like you", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bgood\s+(man|sir|master|lord)\b", "good woman", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bGood\s+(man|sir|master|lord)\b", "Good woman");

                    // Fallback individual word replacements if Lua hook failed
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
                    // Basic pronoun fixes
                    processed = Regex.Replace(processed, @"\bhe\b", "she", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bHe\b", "She");
                    processed = Regex.Replace(processed, @"\bhim\b", "her", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bHim\b", "Her");
                    processed = Regex.Replace(processed, @"\bhis\b", "her", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bHis\b", "Her");
                }
                else if (pronounSet.Subject.Equals("he", StringComparison.OrdinalIgnoreCase))
                {
                    // Handle gender selection patterns for he/him
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
                    // Handle player-specific patterns
                    processed = Regex.Replace(processed, @"\b(women|woman)\s+(like\s+you)\b", "men like you", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bgood\s+(woman|madam|mistress|lady)\b", "good man", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bGood\s+(woman|madam|mistress|lady)\b", "Good man");

                    // Fallback individual word replacements if Lua hook failed
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
                    // Basic pronoun fixes
                    processed = Regex.Replace(processed, @"\bshe\b", "he", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bShe\b", "He");
                    processed = Regex.Replace(processed, @"\bher\b", "his", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bHer\b", "His");
                }
                else if (pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase))
                {
                    // Handle gender selection patterns for they/them (same as neutral)
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
                    // Handle player-specific patterns for they/them
                    processed = Regex.Replace(processed, @"\b(men|women|man|woman)\s+(like\s+you)\b",
                        $"{neutralTitle}s like you", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bgood\s+(man|woman|sir|madam|master|mistress|lady|lord)\b",
                        $"good {neutralTitle}", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bGood\s+(man|woman|sir|madam|master|mistress|lady|lord)\b",
                        $"Good {neutralTitle}");

                    // Full neutral processing for they/them
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

                    // Handle plurals for they/them
                    processed = Regex.Replace(processed, @"\bmen\b", neutralTitle + "s", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bMen\b", capitalizedNeutralTitle + "s");
                    processed = Regex.Replace(processed, @"\bwomen\b", neutralTitle + "s", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bWomen\b", capitalizedNeutralTitle + "s");

                    // Fallback: Handle cases where Lua hook didn't work
                    processed = Regex.Replace(processed, @"\bvaliant men\b", $"valiant {neutralTitle}s", RegexOptions.IgnoreCase);
                    processed = Regex.Replace(processed, @"\bvaliant women\b", $"valiant {neutralTitle}s", RegexOptions.IgnoreCase);
                }
            }
            // Apply verb conjugation fixes (only for they/them)
            if (pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var kvp in ConjugationPatterns)
                {
                    processed = kvp.Key.Replace(processed, kvp.Value);
                }
            }

            return processed;
        }

        // Lua variable detour for pronoun gender forcing
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

                    // Force correct gender variant based on pronouns
                    if (pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase))
                    {
                        newGender = 1; // Force female variants ("women", "she")
                    }
                    else if (pronounSet.Subject.Equals("he", StringComparison.OrdinalIgnoreCase))
                    {
                        newGender = 0; // Force male variants ("men", "he")  
                    }
                    else if (pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase))
                    {
                        newGender = 1; // Force female for they/them (then post-process)
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

        // Helper methods for Lua gender manipulation
        private int GetLuaVarGender(nint poolBase)
        {
            var genderVarId = 0x1B;
            return *(int*)(poolBase + 4 * genderVarId);
        }

        private void SetLuaVarGender(nint poolBase, int gender)
        {
            var genderVarId = 0x1B;
            *(int*)(poolBase + 4 * genderVarId) = gender;
        }

        // Name replacement using byte patterns (exactly like PrefPro, the best there ever was)
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
                var nameParts = csCharacterName.Split(' ');
                var csFirstName = nameParts[0];
                var csLastName = nameParts.Length > 1 ? nameParts[1] : "";

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
                        }
                        else if (payloadHex.Contains(Convert.ToHexString(FirstNameBytes)))
                        {
                            payloads[i] = new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(csFirstName);
                            replaced = true;
                        }
                        else if (payloadHex.Contains(Convert.ToHexString(LastNameBytes)) && !string.IsNullOrEmpty(csLastName))
                        {
                            payloads[i] = new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(csLastName);
                            replaced = true;
                        }
                        else
                        {
                            
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

        private static bool ByteArrayEquals(ReadOnlySpan<byte> a1, ReadOnlySpan<byte> a2)
        {
            return a1.SequenceEqual(a2);
        }

        public void Dispose()
        {
            getStringHook?.Disable();
            getStringHook?.Dispose();
            getLuaVarHook?.Disable();
            getLuaVarHook?.Dispose();
        }
    }
}
