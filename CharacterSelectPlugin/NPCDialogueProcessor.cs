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

        // PrefPro Inspo
        private delegate int GetStringPrototype(RaptureTextModule* textModule, byte* text, void* decoder, Utf8String* stringStruct);
        private Hook<GetStringPrototype>? getStringHook;

        private delegate byte GetLuaVarPrototype(nint poolBase, nint a2, nint a3);
        private Hook<GetLuaVarPrototype>? getLuaVarHook;

        // Lua function hooks
        public delegate nuint LuaFunction(nuint a1);
        private Hook<LuaFunction>? getSexHook;
        private Hook<LuaFunction>? getRaceHook;
        private Hook<LuaFunction>? getTribeHook;

        // Pointers to Lua data
        private byte* luaSexPtr;
        private byte* luaRacePtr;
        private byte* luaTribePtr;

        // Name replacement byte patterns
        private static readonly byte[] FullNameBytes = { 0x02, 0x29, 0x03, 0xEB, 0x02, 0x03 };
        private static readonly byte[] FirstNameBytes = { 0x02, 0x2C, 0x0D, 0xFF, 0x07, 0x02, 0x29, 0x03, 0xEB, 0x02, 0x03, 0xFF, 0x02, 0x20, 0x02, 0x03 };
        private static readonly byte[] LastNameBytes = { 0x02, 0x2C, 0x0D, 0xFF, 0x07, 0x02, 0x29, 0x03, 0xEB, 0x02, 0x03, 0xFF, 0x02, 0x20, 0x03, 0x03 };

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
            { new Regex(@"\bthey\s+(answers)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they answer" },
            { new Regex(@"\bthey\s+(works)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they work" },
            { new Regex(@"\bthey\s+(lives)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they live" },
            { new Regex(@"\bthey\s+(runs)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they run" },
            { new Regex(@"\bthey\s+(walks)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they walk" },
            { new Regex(@"\bthey\s+(stands)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they stand" },
            { new Regex(@"\bthey\s+(sits)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they sit" },
            { new Regex(@"\bthey\s+(eats)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they eat" },
            { new Regex(@"\bthey\s+(drinks)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "they drink" },
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
        private static readonly Regex HeRegex = new Regex(@"\bhe\b", RegexOptions.Compiled);
        private static readonly Regex HeCapitalRegex = new Regex(@"\bHe\b", RegexOptions.Compiled);
        private static readonly Regex SheRegex = new Regex(@"\bshe\b", RegexOptions.Compiled);
        private static readonly Regex SheCapitalRegex = new Regex(@"\bShe\b", RegexOptions.Compiled);
        private static readonly Regex HisRegex = new Regex(@"\bhis\b", RegexOptions.Compiled);
        private static readonly Regex HisCapitalRegex = new Regex(@"\bHis\b", RegexOptions.Compiled);
        private static readonly Regex HerPossessiveRegex = new Regex(@"\bher(?=\s+(?!a\b|an\b|the\b)[A-Z][a-z]+)", RegexOptions.Compiled);
        private static readonly Regex HerPossessiveCapitalRegex = new Regex(@"\bHer(?=\s+(?!a\b|an\b|the\b)[A-Z][a-z]+)", RegexOptions.Compiled);
        private static readonly Regex HerPossessiveLowerRegex = new Regex(@"\bher(?=\s+(?!a\b|an\b|the\b|to\b|and\b|or\b|but\b)[a-z]+)", RegexOptions.Compiled);
        private static readonly Regex HerPossessiveLowerCapitalRegex = new Regex(@"\bHer(?=\s+(?!a\b|an\b|the\b|to\b|and\b|or\b|but\b)[a-z]+)", RegexOptions.Compiled);
        private static readonly Regex HerRegex = new Regex(@"\bher\b", RegexOptions.Compiled);
        private static readonly Regex HerCapitalRegex = new Regex(@"\bHer\b", RegexOptions.Compiled);
        private static readonly Regex HimRegex = new Regex(@"\bhim\b", RegexOptions.Compiled);
        private static readonly Regex HimCapitalRegex = new Regex(@"\bHim\b", RegexOptions.Compiled);
        private static readonly Regex HimselfRegex = new Regex(@"\bhimself\b", RegexOptions.Compiled);
        private static readonly Regex HimselfCapitalRegex = new Regex(@"\bHimself\b", RegexOptions.Compiled);
        private static readonly Regex HerselfRegex = new Regex(@"\bherself\b", RegexOptions.Compiled);
        private static readonly Regex HerselfCapitalRegex = new Regex(@"\bHerself\b", RegexOptions.Compiled);

        // Title regex patterns
        private static readonly Regex LadyRegex = new Regex(@"\blady\b", RegexOptions.Compiled);
        private static readonly Regex LadyCapitalRegex = new Regex(@"\bLady\b", RegexOptions.Compiled);
        private static readonly Regex MistressRegex = new Regex(@"\bmistress\b", RegexOptions.Compiled);
        private static readonly Regex MistressCapitalRegex = new Regex(@"\bMistress\b", RegexOptions.Compiled);
        private static readonly Regex DameRegex = new Regex(@"\bdame\b", RegexOptions.Compiled);
        private static readonly Regex DameCapitalRegex = new Regex(@"\bDame\b", RegexOptions.Compiled);
        private static readonly Regex MadamRegex = new Regex(@"\bmadam\b", RegexOptions.Compiled);
        private static readonly Regex MadamCapitalRegex = new Regex(@"\bMadam\b", RegexOptions.Compiled);
        private static readonly Regex WomanRegex = new Regex(@"\bwoman\b", RegexOptions.Compiled);
        private static readonly Regex WomanCapitalRegex = new Regex(@"\bWoman\b", RegexOptions.Compiled);
        private static readonly Regex GirlRegex = new Regex(@"\bgirl\b", RegexOptions.Compiled);
        private static readonly Regex GirlCapitalRegex = new Regex(@"\bGirl\b", RegexOptions.Compiled);
        private static readonly Regex LassRegex = new Regex(@"\blass\b", RegexOptions.Compiled);
        private static readonly Regex LassCapitalRegex = new Regex(@"\bLass\b", RegexOptions.Compiled);
        private static readonly Regex MaidenRegex = new Regex(@"\bmaiden\b", RegexOptions.Compiled);
        private static readonly Regex MaidenCapitalRegex = new Regex(@"\bMaiden\b", RegexOptions.Compiled);
        // Male title regex patterns
        private static readonly Regex SirRegex = new Regex(@"\bsir\b", RegexOptions.Compiled);
        private static readonly Regex SirCapitalRegex = new Regex(@"\bSir\b", RegexOptions.Compiled);
        private static readonly Regex LordRegex = new Regex(@"\blord\b", RegexOptions.Compiled);
        private static readonly Regex LordCapitalRegex = new Regex(@"\bLord\b", RegexOptions.Compiled);
        private static readonly Regex ManRegex = new Regex(@"\bman\b", RegexOptions.Compiled);
        private static readonly Regex ManCapitalRegex = new Regex(@"\bMan\b", RegexOptions.Compiled);


        public NPCDialogueProcessor(Plugin plugin, ISigScanner sigScanner, IGameInteropProvider gameInteropProvider,
            IChatGui chatGui, IClientState clientState, IPluginLog log)
        {
            this.plugin = plugin;
            this.sigScanner = sigScanner;
            this.gameInteropProvider = gameInteropProvider;
            this.chatGui = chatGui;
            this.clientState = clientState;
            this.log = log;

            try
            {
                InitializeHooks();
                log.Info("[Dialogue] Enhanced pronoun processor initialized with multiple hooks.");
            }
            catch (Exception ex)
            {
                log.Error($"[Dialogue] Failed to initialize processor: {ex.Message}");
            }
        }

        private void InitializeHooks()
        {
            // Text decoder hook
            var getStringSignature = "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 83 B9 ?? ?? ?? ?? ?? 49 8B F9 49 8B F0 48 8B EA 48 8B D9 75 09 48 8B 01 FF 90";
            var getStringPtr = sigScanner.ScanText(getStringSignature);
            getStringHook = gameInteropProvider.HookFromAddress<GetStringPrototype>(getStringPtr, GetStringDetour);
            getStringHook.Enable();

            // Lua variable hook
            var getLuaVar = "E8 ?? ?? ?? ?? 48 85 DB 74 1B 48 8D 8F";
            var getLuaVarPtr = sigScanner.ScanText(getLuaVar);
            getLuaVarHook = gameInteropProvider.HookFromAddress<GetLuaVarPrototype>(getLuaVarPtr, GetLuaVarDetour);
            getLuaVarHook.Enable();

            // Lua function hooks
            try
            {
                var sexFunctionAddress = GetLuaFunctionAddress("return Pc.GetSex");
                var raceFunctionAddress = GetLuaFunctionAddress("return Pc.GetRace");
                var tribeFunctionAddress = GetLuaFunctionAddress("return Pc.GetTribe");

                getSexHook = gameInteropProvider.HookFromAddress<LuaFunction>(sexFunctionAddress, SexFunctionDetour);
                getRaceHook = gameInteropProvider.HookFromAddress<LuaFunction>(raceFunctionAddress, RaceFunctionDetour);
                getTribeHook = gameInteropProvider.HookFromAddress<LuaFunction>(raceFunctionAddress, TribeFunctionDetour);

                // Get static addresses for Lua data
                luaSexPtr = (byte*)GetStaticAddressFromPtr(sexFunctionAddress + 0x32);
                luaRacePtr = (byte*)GetStaticAddressFromPtr(raceFunctionAddress + 0x32);
                luaTribePtr = (byte*)GetStaticAddressFromPtr(tribeFunctionAddress + 0x32);

                getSexHook.Enable();
                getRaceHook.Enable();
                getTribeHook.Enable();

                log.Debug($"[Dialogue] Lua hooks initialized - Sex: {sexFunctionAddress:X}, Race: {raceFunctionAddress:X}, Tribe: {tribeFunctionAddress:X}");
            }
            catch (Exception ex)
            {
                log.Warning($"[Dialogue] Failed to initialize Lua hooks: {ex.Message}");
            }
        }

        // Lua function address resolution
        private nint GetLuaFunctionAddress(string code)
        {
            var l = Framework.Instance()->LuaState.State;
            l->luaL_loadbuffer(code, code.Length, "test_chunk");
            if (l->lua_pcall(0, 1, 0) != 0)
                throw new Exception(l->lua_tostring(-1));
            var luaFunc = *(nint*)l->index2adr(-1);
            l->lua_pop(1);
            return *(nint*)(luaFunc + 0x20);
        }

        // Static address resolution
        private static unsafe IntPtr GetStaticAddressFromPtr(nint instructionAddress)
        {
            try
            {
                var instructionPtr = (byte*)instructionAddress;
                for (int i = 0; i < 64; i++)
                {
                    if (instructionPtr[i] == 0x48 && instructionPtr[i + 1] == 0x8B)
                    {
                        var displacement = *(int*)(instructionPtr + i + 3);
                        return (IntPtr)(instructionAddress + i + 7 + displacement);
                    }
                }
                throw new Exception("Could not find static address");
            }
            catch
            {
                throw new Exception("Failed to resolve static address");
            }
        }

        // Text decoder detour
        private int GetStringDetour(RaptureTextModule* textModule, byte* text, void* decoder, Utf8String* stringStruct)
        {
            try
            {
                if (!plugin.Configuration.EnableDialogueIntegration)
                    return getStringHook!.Original(textModule, text, decoder, stringStruct);

                var activeCharacter = plugin.GetActiveCharacter();
                if (activeCharacter?.RPProfile?.Pronouns == null)
                    return getStringHook!.Original(textModule, text, decoder, stringStruct);

                // Use the IsDefinitelyNPCDialogue method to filter
                if (!IsDefinitelyNPCDialogue(textModule, text, decoder))
                {
                    return getStringHook!.Original(textModule, text, decoder, stringStruct);
                }

                var pronounSet = PronounParser.Parse(activeCharacter.RPProfile.Pronouns);

                // Handle name replacement
                if (plugin.Configuration.ReplaceNameInDialogue && !string.IsNullOrEmpty(activeCharacter.Name))
                    HandleNameReplacement(ref text, activeCharacter);

                // Call original function (let game generate variants naturally)
                var result = getStringHook!.Original(textModule, text, decoder, stringStruct);

                // Post-process for they/them: Replace gendered titles and pronouns
                if (pronounSet.Subject.Equals("they", StringComparison.OrdinalIgnoreCase)
                    && stringStruct != null && stringStruct->BufUsed > 0)
                {
                    var gameGeneratedText = stringStruct->ToString();
                    var processed = ProcessTheyThemText(gameGeneratedText, pronounSet, activeCharacter);

                    if (processed != gameGeneratedText)
                    {
                        stringStruct->SetString(processed);
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

        private string ProcessTheyThemText(string text, PronounSet pronounSet, Character activeCharacter)
        {
            var processed = text;

            // Get the player's chosen neutral title
            var neutralTitle = "Adventurer"; // Default fallback
            var capitalizedTitle = "Adventurer"; // Default fallback

            if (plugin.Configuration.EnableAdvancedTitleReplacement)
            {
                neutralTitle = plugin.Configuration.GetGenderNeutralTitle();
                capitalizedTitle = char.ToUpper(neutralTitle[0]) + neutralTitle.Substring(1);
            }

            // Replace player-addressed titles and phrases
            processed = Regex.Replace(processed, @"\bSir\b", capitalizedTitle);
            processed = Regex.Replace(processed, @"\bsir\b", neutralTitle);
            processed = Regex.Replace(processed, @"\bLady\b", capitalizedTitle);
            processed = Regex.Replace(processed, @"\blady\b", neutralTitle);
            processed = Regex.Replace(processed, @"\bMaster\b", capitalizedTitle);
            processed = Regex.Replace(processed, @"\bmaster\b", neutralTitle);
            processed = Regex.Replace(processed, @"\bMistress\b", capitalizedTitle);
            processed = Regex.Replace(processed, @"\bmistress\b", neutralTitle);
            processed = Regex.Replace(processed, @"\bDame\b", capitalizedTitle);
            processed = Regex.Replace(processed, @"\bdame\b", neutralTitle);

            // Replace player-addressed phrases
            processed = Regex.Replace(processed, @"\bmy dear man\b", $"my dear {neutralTitle}", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"\bmy dear woman\b", $"my dear {neutralTitle}", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"\bmy good man\b", $"my good {neutralTitle}", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"\bmy good woman\b", $"my good {neutralTitle}", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"\byoung man\b", $"young {neutralTitle}", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"\byoung woman\b", $"young {neutralTitle}", RegexOptions.IgnoreCase);

            // Replace pronouns
            processed = Regex.Replace(processed, @"\bhe\b", pronounSet.Subject);
            processed = Regex.Replace(processed, @"\bHe\b", char.ToUpper(pronounSet.Subject[0]) + pronounSet.Subject.Substring(1));
            processed = Regex.Replace(processed, @"\bshe\b", pronounSet.Subject);
            processed = Regex.Replace(processed, @"\bShe\b", char.ToUpper(pronounSet.Subject[0]) + pronounSet.Subject.Substring(1));
            processed = Regex.Replace(processed, @"\bhis\b", pronounSet.Possessive);
            processed = Regex.Replace(processed, @"\bHis\b", char.ToUpper(pronounSet.Possessive[0]) + pronounSet.Possessive.Substring(1));
            processed = Regex.Replace(processed, @"\bher\b", pronounSet.Object);
            processed = Regex.Replace(processed, @"\bHer\b", char.ToUpper(pronounSet.Object[0]) + pronounSet.Object.Substring(1));
            processed = Regex.Replace(processed, @"\bhim\b", pronounSet.Object);
            processed = Regex.Replace(processed, @"\bHim\b", char.ToUpper(pronounSet.Object[0]) + pronounSet.Object.Substring(1));
            processed = Regex.Replace(processed, @"\bhimself\b", pronounSet.Reflexive);
            processed = Regex.Replace(processed, @"\bHimself\b", char.ToUpper(pronounSet.Reflexive[0]) + pronounSet.Reflexive.Substring(1));
            processed = Regex.Replace(processed, @"\bherself\b", pronounSet.Reflexive);
            processed = Regex.Replace(processed, @"\bHerself\b", char.ToUpper(pronounSet.Reflexive[0]) + pronounSet.Reflexive.Substring(1));

            // Fix verb conjugations
            processed = FixVerbConjugation(processed);

            return processed;
        }
        private bool IsDefinitelyNPCDialogue(RaptureTextModule* textModule, byte* text, void* decoder)
        {
            try
            {
                var textSpan = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text);
                if (textSpan.Length == 0) return false;

                var textString = System.Text.Encoding.UTF8.GetString(textSpan);

                // Any text with player name format (First Last: message)
                if (Regex.IsMatch(textString, @"[A-Z][a-z]+\s+[A-Z][a-z]+\s*:"))
                    return false;

                // Any text that starts with common chat prefixes
                if (Regex.IsMatch(textString, @"^\s*(\[LS\]|\[FC\]|\[CWLS\]|\[Novice\]|\[Say\]|\[Yell\]|\[Shout\]|\[Tell\])"))
                    return false;

                // Short messages with colons (likely chat)
                if (textString.Contains(":") && textString.Length < 100)
                    return false;

                // Messages that look like commands or system messages
                if (textString.StartsWith("/") || textString.Contains(">>") || textString.Contains("<<"))
                    return false;

                // Chat bubble exclusions
                if (textString.Length < 100 && !textSpan.Contains((byte)0x02))
                    return false;

                // Exclude messages that look like player chat (casual language patterns)
                if (Regex.IsMatch(textString, @"^(hey|hi|hello|lol|omg|wtf|brb|gg|ty|thx|thanks|np|nvm|ok|okay)\b", RegexOptions.IgnoreCase))
                    return false;

                // Exclude if it's clearly player-to-player conversation
                if (Regex.IsMatch(textString, @"\b(you too|same here|agreed|lmao|haha|nice|cool|awesome)\b", RegexOptions.IgnoreCase))
                    return false;

                // Exclude very short messages without formal language
                if (textString.Length < 50 && !Regex.IsMatch(textString, @"\b(greetings|adventurer|indeed|shall|thee|thou)\b", RegexOptions.IgnoreCase))
                    return false;

                // Original exclusions continue...
                bool hasNamePatterns = false;
                if (textSpan.Contains((byte)0x02) && textString.Length >= 15)
                {
                    if (!textString.Contains("[") && !textString.Contains("]") &&
                        !textString.Contains(": ") && !textString.Contains(" >> "))
                    {
                        hasNamePatterns = true;
                    }
                }

                if (textString.Length < 30)
                {
                    if (hasNamePatterns)
                    {
                        return true;
                    }
                    return false;
                }

                // Only process if it has formal dialogue patterns
                bool hasDialoguePatterns = Regex.IsMatch(textString,
                    @"\b(greetings|well\s+done|excellent|my\s+(dear|good)|you\s+have|I\s+shall|adventurer)\b",
                    RegexOptions.IgnoreCase);

                bool hasGenderedTerms = Regex.IsMatch(textString,
                    @"\b(sir|madam|lady|lord|master|mistress)\b",
                    RegexOptions.IgnoreCase);

                if (hasNamePatterns || hasDialoguePatterns || hasGenderedTerms)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // Lua variable detour (from PrefPro, I think that makes us besties now?)
        private byte GetLuaVarDetour(nint poolBase, IntPtr a2, IntPtr a3)
        {
            try
            {
                var activeCharacter = plugin.GetActiveCharacter();
                if (activeCharacter?.RPProfile?.Pronouns != null && plugin.Configuration.EnableDialogueIntegration)
                {
                    var pronounSet = PronounParser.Parse(activeCharacter.RPProfile.Pronouns);
                    var oldGender = GetLuaVarGender(poolBase);
                    var newGender = (int)GetTargetGender(pronounSet);

                    SetLuaVarGender(poolBase, newGender);
                    var returnValue = getLuaVarHook!.Original(poolBase, a2, a3);
                    SetLuaVarGender(poolBase, oldGender);

                    log.Debug($"[Dialogue] Lua var gender override: {oldGender} -> {newGender}");
                    return returnValue;
                }

                return getLuaVarHook!.Original(poolBase, a2, a3);
            }
            catch (Exception ex)
            {
                log.Error($"[Dialogue] Error in GetLuaVarDetour: {ex.Message}");
                return getLuaVarHook!.Original(poolBase, a2, a3);
            }
        }

        // Lua function detours (from PrefPro's LuaHandler, if not this is really awkward...)
        private nuint SexFunctionDetour(nuint a1)
        {
            try
            {
                var activeCharacter = plugin.GetActiveCharacter();
                if (activeCharacter?.RPProfile?.Pronouns != null && plugin.Configuration.EnableDialogueIntegration && luaSexPtr != null)
                {
                    var pronounSet = PronounParser.Parse(activeCharacter.RPProfile.Pronouns);
                    var oldSex = *luaSexPtr;
                    var newSex = (byte)GetTargetGender(pronounSet);

                    *luaSexPtr = newSex;
                    log.Debug($"[Dialogue] Lua sex function override: {oldSex} -> {newSex}");
                    var ret = getSexHook!.Original(a1);
                    *luaSexPtr = oldSex;

                    return ret;
                }

                return getSexHook!.Original(a1);
            }
            catch (Exception ex)
            {
                log.Error($"[Dialogue] Error in SexFunctionDetour: {ex.Message}");
                return getSexHook!.Original(a1);
            }
        }

        private nuint RaceFunctionDetour(nuint a1)
        {
            // Coming soon...
            return getRaceHook!.Original(a1);
        }
        private nuint TribeFunctionDetour(nuint a1)
        {
            // Coming soon...
            return getTribeHook!.Original(a1);
        }


        // Helper methods
        private ulong GetTargetGender(PronounSet pronounSet)
        {
            if (pronounSet.Subject.Equals("he", StringComparison.OrdinalIgnoreCase))
                return 0ul; // Male
            else if (pronounSet.Subject.Equals("she", StringComparison.OrdinalIgnoreCase))
                return 1ul; // Female
            else // they/them: use female variant
                return 1ul;
        }

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

        // Helper method to check if text contains gendered pronouns (indicating it had variations)
        private bool ContainsGenderedPronouns(string text)
        {
            return Regex.IsMatch(text, @"\b(he|she|his|her|him|himself|herself)\b", RegexOptions.IgnoreCase);
        }

        private string ProcessTheyThemPronouns(string text, PronounSet pronounSet, Character activeCharacter)
        {
            // Early exit for very short strings
            if (text.Length < 3) return text;

            var processed = text;

            // PRONOUNS - Use precompiled regex patterns
            processed = HeRegex.Replace(processed, pronounSet.Subject);
            processed = HeCapitalRegex.Replace(processed, char.ToUpper(pronounSet.Subject[0]) + pronounSet.Subject.Substring(1));
            processed = SheRegex.Replace(processed, pronounSet.Subject);
            processed = SheCapitalRegex.Replace(processed, char.ToUpper(pronounSet.Subject[0]) + pronounSet.Subject.Substring(1));

            processed = HisRegex.Replace(processed, pronounSet.Possessive);
            processed = HisCapitalRegex.Replace(processed, char.ToUpper(pronounSet.Possessive[0]) + pronounSet.Possessive.Substring(1));

            // Handle "her" - possessive vs object
            processed = HerPossessiveRegex.Replace(processed, pronounSet.Possessive);
            processed = HerPossessiveCapitalRegex.Replace(processed, char.ToUpper(pronounSet.Possessive[0]) + pronounSet.Possessive.Substring(1));
            processed = HerPossessiveLowerRegex.Replace(processed, pronounSet.Possessive);
            processed = HerPossessiveLowerCapitalRegex.Replace(processed, char.ToUpper(pronounSet.Possessive[0]) + pronounSet.Possessive.Substring(1));
            processed = HerRegex.Replace(processed, pronounSet.Object);
            processed = HerCapitalRegex.Replace(processed, char.ToUpper(pronounSet.Object[0]) + pronounSet.Object.Substring(1));

            processed = HimRegex.Replace(processed, pronounSet.Object);
            processed = HimCapitalRegex.Replace(processed, char.ToUpper(pronounSet.Object[0]) + pronounSet.Object.Substring(1));

            processed = HimselfRegex.Replace(processed, pronounSet.Reflexive);
            processed = HimselfCapitalRegex.Replace(processed, char.ToUpper(pronounSet.Reflexive[0]) + pronounSet.Reflexive.Substring(1));
            processed = HerselfRegex.Replace(processed, pronounSet.Reflexive);
            processed = HerselfCapitalRegex.Replace(processed, char.ToUpper(pronounSet.Reflexive[0]) + pronounSet.Reflexive.Substring(1));

            // TITLES - Use configurable replacements
            if (plugin.Configuration.EnableAdvancedTitleReplacement)
            {
                var neutralTitle = plugin.Configuration.GetGenderNeutralTitle();
                var capitalizedTitle = char.ToUpper(neutralTitle[0]) + neutralTitle.Substring(1);

                processed = LadyRegex.Replace(processed, neutralTitle);
                processed = LadyCapitalRegex.Replace(processed, capitalizedTitle);
                processed = MistressRegex.Replace(processed, neutralTitle);
                processed = MistressCapitalRegex.Replace(processed, capitalizedTitle);
                processed = DameRegex.Replace(processed, neutralTitle);
                processed = DameCapitalRegex.Replace(processed, capitalizedTitle);
                processed = MadamRegex.Replace(processed, neutralTitle);
                processed = MadamCapitalRegex.Replace(processed, capitalizedTitle);
                processed = WomanRegex.Replace(processed, neutralTitle);
                processed = WomanCapitalRegex.Replace(processed, capitalizedTitle);
                processed = GirlRegex.Replace(processed, neutralTitle);
                processed = GirlCapitalRegex.Replace(processed, capitalizedTitle);
                processed = LassRegex.Replace(processed, neutralTitle);
                processed = LassCapitalRegex.Replace(processed, capitalizedTitle);
                processed = MaidenRegex.Replace(processed, neutralTitle);
                processed = MaidenCapitalRegex.Replace(processed, capitalizedTitle);
                // Male titles
                processed = SirRegex.Replace(processed, neutralTitle);
                processed = SirCapitalRegex.Replace(processed, capitalizedTitle);
                processed = LordRegex.Replace(processed, neutralTitle);
                processed = LordCapitalRegex.Replace(processed, capitalizedTitle);
                processed = ManRegex.Replace(processed, neutralTitle);
                processed = ManCapitalRegex.Replace(processed, capitalizedTitle);
            }

            // Fix verb conjugations for they/them
            processed = FixVerbConjugation(processed);

            return processed;
        }


        private string FixVerbConjugation(string text)
        {
            foreach (var kvp in ConjugationPatterns)
            {
                text = kvp.Key.Replace(text, kvp.Value);
            }
            return text;
        }

        private void HandleNameReplacement(ref byte* text, Character character)
        {
            try
            {
                var playerName = clientState.LocalPlayer?.Name.TextValue;
                if (string.IsNullOrEmpty(playerName))
                    return;

                var textSpan = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text);

                if (!textSpan.Contains((byte)0x02))
                    return;

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

                        if (ByteArrayEquals(payloadBytes, FullNameBytes))
                        {
                            payloads[i] = new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(csCharacterName);
                            replaced = true;
                        }
                        else if (ByteArrayEquals(payloadBytes, FirstNameBytes))
                        {
                            payloads[i] = new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(csFirstName);
                            replaced = true;
                        }
                        else if (ByteArrayEquals(payloadBytes, LastNameBytes) && !string.IsNullOrEmpty(csLastName))
                        {
                            payloads[i] = new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(csLastName);
                            replaced = true;
                        }
                    }
                }

                if (!replaced)
                    return;

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
            getSexHook?.Disable();
            getSexHook?.Dispose();
            getRaceHook?.Disable();
            getRaceHook?.Dispose();
            getTribeHook?.Disable();
            getTribeHook?.Dispose();
        }
    }
}
