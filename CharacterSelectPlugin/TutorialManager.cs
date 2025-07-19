using ImGuiNET;
using System.Numerics;
using Dalamud.Interface;
using System;
using System.Linq;

namespace CharacterSelectPlugin
{
    public enum TutorialStep
    {
        Welcome = 0,
        AddCharacter = 1,
        FillCharacterForm = 2,
        ExploreOtherFields = 3,
        SaveCharacter = 4,
        CharacterSavedDialog = 5,
        ClickDesignsButton = 6,
        AddNewDesign = 7,
        FillDesignForm = 8,
        ExploreDesignOptions = 9,
        SaveDesign = 10,
        DesignManagement = 11,
        ClickRPProfileButton = 12,
        ClickEditProfile = 13,
        StartWithPronouns = 14,
        AddBio = 15,
        ExploreImageOptions = 16,
        ExploreVisualOptions = 17,
        SetPrivacyAndSave = 18,
        ExploreMainFeatures = 19,
        SettingsOverview = 20,
        QuickSwitchOverview = 21,
        GalleryOverview = 22,
        Complete = 23
    }

    public class TutorialManager
    {
        private readonly Plugin plugin;
        private float lastFieldCheckTime = 0f;
        private const float FIELD_CHECK_DELAY = 1.0f; // Let users type! 

        public bool IsActive => plugin.Configuration.TutorialActive;
        public TutorialStep CurrentStep => (TutorialStep)plugin.Configuration.CurrentTutorialStep;

        public TutorialManager(Plugin plugin)
        {
            this.plugin = plugin;
        }

        public void StartTutorial()
        {
            Plugin.Log.Info("[Tutorial] Starting tutorial");
            plugin.Configuration.TutorialActive = true;
            plugin.Configuration.CurrentTutorialStep = 0;
            plugin.Configuration.HasSeenTutorial = false;
            plugin.Configuration.Save();
        }

        public void NextStep()
        {
            plugin.Configuration.CurrentTutorialStep++;
            plugin.Configuration.Save();
            Plugin.Log.Info($"[Tutorial] Advanced to step {plugin.Configuration.CurrentTutorialStep}");
        }

        public void EndTutorial()
        {
            plugin.Configuration.TutorialActive = false;
            plugin.Configuration.HasSeenTutorial = true;
            plugin.Configuration.Save();
            Plugin.Log.Info("[Tutorial] Tutorial ended");
        }

        public void DrawTutorialOverlay()
        {
            if (!IsActive) return;

            var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
            var uiScale = plugin.Configuration.UIScaleMultiplier;
            var totalScale = GetSafeScale(dpiScale * uiScale);

            CheckTutorialProgression();

            switch (CurrentStep)
            {
                case TutorialStep.Welcome:
                    DrawWelcomeStep(totalScale);
                    break;
                case TutorialStep.AddCharacter:
                    if (!plugin.IsAddCharacterWindowOpen)
                    {
                        DrawAddCharacterHighlight(totalScale);
                    }
                    break;
                case TutorialStep.FillCharacterForm:
                    DrawCharacterFormHelp(totalScale);
                    break;
                case TutorialStep.ExploreOtherFields:
                    DrawOtherFieldsHelp(totalScale);
                    break;
                case TutorialStep.SaveCharacter:
                    DrawSaveCharacterHelp(totalScale);
                    break;
                case TutorialStep.CharacterSavedDialog:
                    DrawCharacterSavedDialog(totalScale);
                    break;
                case TutorialStep.ClickDesignsButton:
                    DrawClickDesignsButtonHelp(totalScale);
                    break;
                case TutorialStep.AddNewDesign:
                    DrawAddNewDesignHelp(totalScale);
                    break;
                case TutorialStep.FillDesignForm:
                    DrawFillDesignFormHelp(totalScale);
                    break;
                case TutorialStep.ExploreDesignOptions:
                    DrawExploreDesignOptionsHelp(totalScale);
                    break;
                case TutorialStep.SaveDesign:
                    DrawSaveDesignHelp(totalScale);
                    break;
                case TutorialStep.DesignManagement:
                    DrawDesignManagementHelp(totalScale);
                    break;
                case TutorialStep.ClickRPProfileButton:
                    DrawClickRPProfileButtonHelp(totalScale);
                    break;
                case TutorialStep.ClickEditProfile:
                    DrawClickEditProfileHelp(totalScale);
                    break;
                case TutorialStep.StartWithPronouns:
                    DrawStartWithPronounsHelp(totalScale);
                    break;
                case TutorialStep.AddBio:
                    DrawAddBioHelp(totalScale);
                    break;
                case TutorialStep.ExploreImageOptions:
                    DrawExploreImageOptionsHelp(totalScale);
                    break;
                case TutorialStep.ExploreVisualOptions:
                    DrawExploreVisualOptionsHelp(totalScale);
                    break;
                case TutorialStep.SetPrivacyAndSave:
                    DrawSaveRPProfileHelp(totalScale);
                    break;
                case TutorialStep.ExploreMainFeatures:
                    DrawExploreMainFeaturesHelp(totalScale);
                    break;
                case TutorialStep.SettingsOverview:
                    DrawSettingsOverviewHelp(totalScale);
                    break;
                case TutorialStep.QuickSwitchOverview:
                    DrawQuickSwitchOverviewHelp(totalScale);
                    break;
                case TutorialStep.GalleryOverview:
                    DrawGalleryOverviewHelp(totalScale);
                    break;
                case TutorialStep.Complete:
                    DrawTutorialCompleteHelp(totalScale);
                    break;
            }
        }

        private void CheckTutorialProgression()
        {
            switch (CurrentStep)
            {
                case TutorialStep.AddCharacter:
                    if (plugin.IsAddCharacterWindowOpen)
                    {
                        Plugin.Log.Info("[Tutorial] Add Character window opened, advancing to form step");
                        NextStep();
                    }
                    break;

                case TutorialStep.FillCharacterForm:
                    if (!plugin.IsAddCharacterWindowOpen)
                    {
                        if (plugin.Characters.Count > 0)
                        {
                            plugin.Configuration.CurrentTutorialStep = (int)TutorialStep.CharacterSavedDialog;
                            plugin.Configuration.Save();
                        }
                        else
                        {
                            plugin.Configuration.CurrentTutorialStep = (int)TutorialStep.AddCharacter;
                            plugin.Configuration.Save();
                        }
                    }
                    else
                    {
                        float currentTime = (float)ImGui.GetTime();
                        if (currentTime - lastFieldCheckTime > FIELD_CHECK_DELAY)
                        {
                            if (AreRequiredFieldsFilled())
                            {
                                NextStep();
                            }
                        }

                        if (!AreRequiredFieldsFilled())
                        {
                            lastFieldCheckTime = currentTime;
                        }
                    }
                    break;

                case TutorialStep.ExploreOtherFields:
                    if (!plugin.IsAddCharacterWindowOpen)
                    {
                        if (plugin.Characters.Count > 0)
                        {
                            plugin.Configuration.CurrentTutorialStep = (int)TutorialStep.CharacterSavedDialog;
                            plugin.Configuration.Save();
                        }
                        else
                        {
                            plugin.Configuration.CurrentTutorialStep = (int)TutorialStep.AddCharacter;
                            plugin.Configuration.Save();
                        }
                    }
                    break;

                case TutorialStep.SaveCharacter:
                    if (!plugin.IsAddCharacterWindowOpen)
                    {
                        if (plugin.Characters.Count > 0)
                        {
                            NextStep();
                        }
                        else
                        {
                            plugin.Configuration.CurrentTutorialStep = (int)TutorialStep.AddCharacter;
                        }
                        plugin.Configuration.Save();
                    }
                    break;

                case TutorialStep.ClickDesignsButton:
                    if (plugin.IsDesignPanelOpen)
                    {
                        NextStep();
                    }
                    break;

                case TutorialStep.AddNewDesign:
                    if (plugin.IsEditDesignWindowOpen)
                    {
                        NextStep();
                    }
                    break;

                case TutorialStep.FillDesignForm:
                    if (!plugin.IsEditDesignWindowOpen)
                    {
                        // Check if a design was actually created
                        if (plugin.Characters.Count > 0 && plugin.Characters[0].Designs.Count > 0)
                        {
                            plugin.Configuration.CurrentTutorialStep = (int)TutorialStep.DesignManagement;
                            plugin.Configuration.Save();
                        }
                        else
                        {
                            plugin.Configuration.CurrentTutorialStep = (int)TutorialStep.AddNewDesign;
                            plugin.Configuration.Save();
                        }
                    }
                    else
                    {
                        float currentTime = (float)ImGui.GetTime();
                        if (currentTime - lastFieldCheckTime > FIELD_CHECK_DELAY)
                        {
                            if (AreRequiredDesignFieldsFilled())
                            {
                                NextStep();
                            }
                        }

                        if (!AreRequiredDesignFieldsFilled())
                        {
                            lastFieldCheckTime = currentTime;
                        }
                    }
                    break;

                case TutorialStep.ExploreDesignOptions:
                    if (!plugin.IsEditDesignWindowOpen)
                    {
                        if (plugin.Characters.Count > 0 && plugin.Characters[0].Designs.Count > 0)
                        {
                            // Don't skip to Complete
                            plugin.Configuration.CurrentTutorialStep = (int)TutorialStep.DesignManagement;
                            plugin.Configuration.Save();
                        }
                        else
                        {
                            plugin.Configuration.CurrentTutorialStep = (int)TutorialStep.AddNewDesign;
                            plugin.Configuration.Save();
                        }
                    }
                    break;

                case TutorialStep.SaveDesign:
                    if (!plugin.IsEditDesignWindowOpen)
                    {
                        if (plugin.Characters.Count > 0 && plugin.Characters[0].Designs.Count > 0)
                        {
                            NextStep();
                        }
                        else
                        {
                            plugin.Configuration.CurrentTutorialStep = (int)TutorialStep.AddNewDesign;
                        }
                        plugin.Configuration.Save();
                    }
                    break;
                case TutorialStep.DesignManagement:
                    break;
                case TutorialStep.ClickRPProfileButton:
                    if (plugin.IsRPProfileViewerOpen)
                    {
                        NextStep();
                    }
                    break;

                case TutorialStep.ClickEditProfile:
                    if (plugin.IsRPProfileEditorOpen)
                    {
                        NextStep();
                    }
                    break;

                case TutorialStep.StartWithPronouns:
                    if (!plugin.IsRPProfileEditorOpen)
                    {
                        plugin.Configuration.CurrentTutorialStep = (int)TutorialStep.ClickEditProfile;
                        plugin.Configuration.Save();
                    }
                    break;

                case TutorialStep.AddBio:
                    if (!plugin.IsRPProfileEditorOpen)
                    {
                        plugin.Configuration.CurrentTutorialStep = (int)TutorialStep.ClickEditProfile;
                        plugin.Configuration.Save();
                    }
                    else
                    {
                        // Check if bio has content
                        var activeCharacter = plugin.Characters.FirstOrDefault();
                        if (activeCharacter?.RPProfile != null && !string.IsNullOrWhiteSpace(activeCharacter.RPProfile.Bio))
                        {
                            NextStep();
                        }
                    }
                    break;

                case TutorialStep.ExploreImageOptions:
                    if (!plugin.IsRPProfileEditorOpen)
                    {
                        plugin.Configuration.CurrentTutorialStep = (int)TutorialStep.ClickEditProfile;
                        plugin.Configuration.Save();
                    }
                    break;

                case TutorialStep.ExploreVisualOptions:
                    if (!plugin.IsRPProfileEditorOpen)
                    {
                        plugin.Configuration.CurrentTutorialStep = (int)TutorialStep.ClickEditProfile;
                        plugin.Configuration.Save();
                    }
                    break;

                case TutorialStep.SetPrivacyAndSave:
                    if (!plugin.IsRPProfileEditorOpen)
                    {
                        NextStep();
                    }
                    break;

                case TutorialStep.Complete:
                    break;
                case TutorialStep.ExploreMainFeatures:
                    break;

                case TutorialStep.SettingsOverview:
                    break;

                case TutorialStep.QuickSwitchOverview:
                    break;

                case TutorialStep.GalleryOverview:
                    break;
            }
        }

        private bool AreRequiredFieldsFilled()
        {
            // Check if the three required fields have meaningful content
            bool hasName = !string.IsNullOrWhiteSpace(plugin.NewCharacterName) && plugin.NewCharacterName.Length >= 2;
            bool hasPenumbra = !string.IsNullOrWhiteSpace(plugin.NewPenumbraCollection) && plugin.NewPenumbraCollection.Length >= 2;
            bool hasGlamourer = !string.IsNullOrWhiteSpace(plugin.NewGlamourerDesign) && plugin.NewGlamourerDesign.Length >= 2;

            return hasName && hasPenumbra && hasGlamourer;
        }
        private bool AreRequiredDesignFieldsFilled()
        {
            bool hasName = !string.IsNullOrWhiteSpace(plugin.EditedDesignName) && plugin.EditedDesignName.Length >= 2;
            bool hasGlamourer = !string.IsNullOrWhiteSpace(plugin.EditedGlamourerDesign) && plugin.EditedGlamourerDesign.Length >= 2;
            return hasName && hasGlamourer;
        }

        private void DrawWelcomeStep(float scale)
        {
            var viewport = ImGui.GetMainViewport();
            var center = viewport.GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vector2(480 * scale, 360 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.3f, 0.4f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.08f, 0.08f, 0.12f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.12f, 0.12f, 0.18f, 1.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f * scale); // Scale border
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale); // Scale rounding
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            ImGui.SetNextWindowFocus();

            if (ImGui.Begin("Welcome to Character Select+ v1.2!", ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf005"); // Star icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.9f, 1.0f), "Welcome to Character Select+ v1.2!");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Ready to create your first Character? Don't worry, it's easier than explaining why you need 47 different glamour plates:");
                ImGui.PopTextWrapPos();

                ImGui.Spacing();
                ImGui.Indent(15 * scale);
                ImGui.BulletText("Creating your first Character profile");
                ImGui.BulletText("Adding Glamourer designs");
                ImGui.BulletText("Setting up your RP Profile with backgrounds & animations");
                ImGui.BulletText("Exploring the character gallery");
                ImGui.BulletText("(Close other windows during the tutorial for the best experience)");
                ImGui.Unindent(15 * scale);

                ImGui.Spacing();
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.7f, 0.8f, 1.0f), "Don't worry - you can end this tutorial at anytime and explore on your own!");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 120f * scale;
                float spacing = 20f * scale;
                float totalWidth = (buttonWidth * 2) + spacing;
                float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;

                ImGui.SetCursorPosX(startX);

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.4f, 0.15f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.5f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.35f, 0.1f, 1f));

                if (ImGui.Button("Start Tutorial", new Vector2(buttonWidth, 28 * scale))) 
                {
                    NextStep();
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();
                ImGui.SetCursorPosX(startX + buttonWidth + spacing);

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.25f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.3f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.15f, 0.15f, 0.2f, 1f));

                if (ImGui.Button("Skip Tutorial", new Vector2(buttonWidth, 28 * scale)))
                {
                    EndTutorial();
                }
                ImGui.PopStyleColor(3);

                ImGui.End();
            }

            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(4);
        }

        private void DrawAddCharacterHighlight(float scale)
        {
            var buttonInfo = GetAddCharacterButtonInfo();

            if (buttonInfo.HasValue)
            {
                var (buttonPos, buttonSize) = buttonInfo.Value;

                DrawInstructionPopup("Create Your First Character",
                    "Click the 'Add Character' button to create your first Character profile.",
                    buttonPos, buttonSize, scale);

                HighlightButton(buttonPos, buttonSize, scale);
            }
            else
            {
                Plugin.Log.Debug("[Tutorial] Button position not found, using fallback");
                DrawInstructionPopup("Create Your First Character",
                    "Look for the 'Add Character' button and click it to create your first Character profile.",
                    null, null, scale);
            }
        }

        private (Vector2 pos, Vector2 size)? GetAddCharacterButtonInfo()
        {
            if (plugin.AddCharacterButtonPos.HasValue && plugin.AddCharacterButtonSize.HasValue)
            {
                return (plugin.AddCharacterButtonPos.Value, plugin.AddCharacterButtonSize.Value);
            }
            return null;
        }

        private void DrawInstructionPopup(string title, string instruction, Vector2? buttonPos = null, Vector2? buttonSize = null, float scale = 1.0f)
        {
            Vector2 popupPos;

            if (buttonPos.HasValue && buttonSize.HasValue)
            {
                popupPos = new Vector2(
                    buttonPos.Value.X + buttonSize.Value.X + (30 * scale),
                    buttonPos.Value.Y + (20 * scale)
                );

                var viewport = ImGui.GetMainViewport();
                if (popupPos.Y < viewport.Pos.Y + (50 * scale))
                    popupPos.Y = viewport.Pos.Y + (50 * scale);
                if (popupPos.X + (300 * scale) > viewport.Pos.X + viewport.Size.X)
                    popupPos.X = buttonPos.Value.X - (330 * scale);
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + (200 * scale), viewport.Pos.Y + (80 * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(300 * scale, 140 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.3f, 0.4f, 0.8f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            // Window close detection
            bool windowOpen = true;
            if (ImGui.Begin($"Tutorial: {title}", ref windowOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf007");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.9f, 1.0f), title);

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), instruction);
                ImGui.PopTextWrapPos();

                ImGui.Spacing();

                if (ImGui.Button("End Tutorial", new Vector2(100 * scale, 25 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            if (!windowOpen)
            {
                EndTutorial();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }
        public void DrawRPProfileOverlays()
        {
            if (!IsActive) return;

            var dpiScale = ImGui.GetIO().DisplayFramebufferScale.X;
            var uiScale = plugin.Configuration.UIScaleMultiplier;
            var totalScale = dpiScale * uiScale;

            Plugin.Log.Info($"[Tutorial] RP Profile Overlay - Step: {CurrentStep}, Editor Open: {plugin.IsRPProfileEditorOpen}");

            switch (CurrentStep)
            {
                case TutorialStep.StartWithPronouns:
                    DrawStartWithPronounsHelp(totalScale); 
                    break;
                case TutorialStep.AddBio:
                    DrawAddBioHelp(totalScale); 
                    break;
                case TutorialStep.ExploreImageOptions:
                    DrawExploreImageOptionsHelp(totalScale); 
                    break;
                case TutorialStep.ExploreVisualOptions:
                    DrawExploreVisualOptionsHelp(totalScale); 
                    break;
                case TutorialStep.SetPrivacyAndSave:
                    DrawSaveRPProfileHelp(totalScale);
                    break;
            }
        }


        private void HighlightButton(Vector2 buttonPos, Vector2 buttonSize, float scale)
        {
            var dl = ImGui.GetForegroundDrawList();

            float time = (float)ImGui.GetTime();
            float pulse = 0.5f + 0.5f * (float)Math.Sin(time * 3.0f);
            var glowColor = new Vector4(0.3f, 0.6f, 1.0f, pulse * 0.6f);

            for (int i = 0; i < 3; i++)
            {
                float expansion = (i * 6f + pulse * 4f) * scale;
                dl.AddRect(
                    buttonPos - new Vector2(expansion, expansion),
                    buttonPos + buttonSize + new Vector2(expansion, expansion),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(glowColor.X, glowColor.Y, glowColor.Z, glowColor.W * (1f - i * 0.3f))),
                    4f * scale, ImDrawFlags.None, 2f * scale
                );
            }

            var arrowEnd = buttonPos + new Vector2(-10 * scale, buttonSize.Y / 2);
            var arrowStart = arrowEnd + new Vector2(-25 * scale, 0);
            dl.AddLine(arrowStart, arrowEnd, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 3f * scale);

            var arrowHead1 = arrowEnd + new Vector2(-8 * scale, -4 * scale);
            var arrowHead2 = arrowEnd + new Vector2(-8 * scale, 4 * scale);
            dl.AddLine(arrowEnd, arrowHead1, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 3f * scale);
            dl.AddLine(arrowEnd, arrowHead2, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 3f * scale);
        }


        private void DrawCharacterFormHelp(float scale)
        {
            if (!plugin.IsAddCharacterWindowOpen) return;

            
            Vector2 popupPos;
            if (plugin.CharacterNameFieldPos.HasValue && plugin.CharacterNameFieldSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.CharacterNameFieldPos.Value.X + plugin.CharacterNameFieldSize.Value.X + (50 * scale),
                    plugin.CharacterNameFieldPos.Value.Y - (50 * scale)
                );
            }
            else if (plugin.PenumbraFieldPos.HasValue && plugin.PenumbraFieldSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.PenumbraFieldPos.Value.X + plugin.PenumbraFieldSize.Value.X + (50 * scale),
                    plugin.PenumbraFieldPos.Value.Y - (50 * scale)
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + (500 * scale), viewport.Pos.Y + (200 * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(540 * scale, 280 * scale), ImGuiCond.Always); // Scale window

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.6f, 0.3f, 0.8f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Required Fields", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf040");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1.0f), "Fill Required Fields");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Fill in these required fields (* marked) to create your character:");
                ImGui.Spacing();

                // Required fields list
                ImGui.BulletText("Character Name* - Your OC's name");
                ImGui.BulletText("Penumbra Collection* - Must match exactly");
                ImGui.BulletText("Glamourer Design* - Must match exactly");

                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.8f, 0.3f, 1.0f));
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf071"); // Warning triangle
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.Text(" Names must match EXACTLY - we're more picky than a cat choosing where to nap!");
                ImGui.PopStyleColor();

                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.7f, 0.3f, 1.0f));
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf005"); // Star icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), " Required fields will glow to guide you!");
                ImGui.PopStyleColor();
                ImGui.Spacing();

                if (ImGui.Button("End Tutorial", new Vector2(100 * scale, 25 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
            HighlightRequiredFields(scale);
        }

        private void DrawOtherFieldsHelp(float scale)
        {
            if (!plugin.IsAddCharacterWindowOpen) return;

            Vector2 popupPos;
            if (plugin.CharacterNameFieldPos.HasValue && plugin.CharacterNameFieldSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.CharacterNameFieldPos.Value.X + plugin.CharacterNameFieldSize.Value.X + (50 * scale),
                    plugin.CharacterNameFieldPos.Value.Y + (100 * scale)
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + (500 * scale), viewport.Pos.Y + (300 * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(480 * scale, 400 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.4f, 0.6f, 0.8f, 0.8f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Optional Features", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf53f");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 0.8f, 1.0f, 1.0f), "Explore Optional Features");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Excellent! You've survived the mandatory paperwork. Now for the fun stuff:");
                ImGui.Spacing();

                // Optional features
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf53f");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Nameplate Colour");

                ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X - (20 * scale));
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Because your Character deserves a fabulous frame colour (I said colour, not color!)");
                ImGui.PopTextWrapPos();
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf03e");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Choose Image");

                ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X - (20 * scale));
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Upload a custom picture instead of the default placeholder");
                ImGui.PopTextWrapPos();
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf013");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Advanced Mode");

                ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X - (20 * scale));
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "For power users! Manually edit macros and add custom commands (i.e job change)");
                ImGui.PopTextWrapPos();
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb"); // Lightbulb icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), " You can always edit these later! For now, let's save your Character.");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb"); // Lightbulb icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), " You can also use '/select Character Name' to switch to your character!.");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                float buttonWidth = 120f * scale;
                float spacing = 15f * scale;
                float totalWidth = (buttonWidth * 2) + spacing;
                float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;

                ImGui.SetCursorPosX(startX);

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.4f, 0.15f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.5f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.35f, 0.1f, 1f));

                if (ImGui.Button("Continue", new Vector2(buttonWidth, 25 * scale)))
                {
                    NextStep();
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();
                ImGui.SetCursorPosX(startX + buttonWidth + spacing);

                if (ImGui.Button("End Tutorial", new Vector2(buttonWidth, 25 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }

        private void DrawSaveCharacterHelp(float scale)
        {
            if (!plugin.IsAddCharacterWindowOpen) return;

            Vector2 popupPos;
            if (plugin.SaveButtonPos.HasValue && plugin.SaveButtonSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.SaveButtonPos.Value.X + plugin.SaveButtonSize.Value.X + (30 * scale),
                    plugin.SaveButtonPos.Value.Y - (100 * scale)
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + (500 * scale), viewport.Pos.Y + (400 * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(500 * scale, 240 * scale), ImGuiCond.Always); 

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.8f, 0.3f, 0.8f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Save Character", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf00c"); // Check mark icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 1.0f, 0.7f, 1.0f), "Ready to Save!");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Perfect! Time to make it official - your Character's birth certificate awaits!");

                ImGui.Spacing();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb"); // Lightbulb icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), " After saving, you can:");
                ImGui.BulletText("Create Designs for different outfits");
                ImGui.BulletText("Set up RP Profiles with backgrounds & animations");
                ImGui.BulletText("Explore the character gallery");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 120f;
                float startX = (ImGui.GetContentRegionAvail().X - buttonWidth) * 0.5f;
                ImGui.SetCursorPosX(startX);

                if (ImGui.Button("End Tutorial", new Vector2(buttonWidth, 25 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            HighlightSaveButton(scale);
        }
        private void DrawCharacterSavedDialog(float scale)
        {
            var viewport = ImGui.GetMainViewport();
            var center = viewport.GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vector2(520 * scale, 240 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.8f, 0.3f, 0.8f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            ImGui.SetNextWindowFocus();

            if (ImGui.Begin("Character Created!", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf00c"); // Checkmark icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 1.0f, 0.7f, 1.0f), "Character Created Successfully!");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Congratulations! Your digital offspring has been successfully birthed into existence.");

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "What would you like to do next?");
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf553");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Continue: Learn to create Designs (outfits) for your Character");

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf00c");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Finish: End the tutorial and explore on your own");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 150f * scale;
                float spacing = 20f * scale;
                float totalWidth = (buttonWidth * 2) + spacing;
                float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;

                ImGui.SetCursorPosX(startX);

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.4f, 0.15f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.5f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.35f, 0.1f, 1f));

                if (ImGui.Button("Continue to Designs", new Vector2(buttonWidth, 28 * scale)))
                {
                    NextStep();
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();
                ImGui.SetCursorPosX(startX + buttonWidth + spacing);

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.25f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.3f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.15f, 0.15f, 0.2f, 1f));

                if (ImGui.Button("Finish Tutorial", new Vector2(buttonWidth, 28 * scale)))
                {
                    EndTutorial();
                }
                ImGui.PopStyleColor(3);

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }

        private void DrawClickDesignsButtonHelp(float scale)
        {
            // Position next to the first character's designs button
            Vector2 popupPos;
            if (plugin.FirstCharacterDesignsButtonPos.HasValue && plugin.FirstCharacterDesignsButtonSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.FirstCharacterDesignsButtonPos.Value.X + plugin.FirstCharacterDesignsButtonSize.Value.X + (30 * scale),
                    plugin.FirstCharacterDesignsButtonPos.Value.Y - (50 * scale)
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + (400 * scale), viewport.Pos.Y + (300 * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(460 * scale, 170 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.6f, 0.4f, 0.8f, 0.8f)); // Purple border
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Add Design", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf553");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.8f, 0.6f, 1.0f, 1.0f), "Create Your First Design");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X - 10);
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Brilliant! Your character exists, but they're probably feeling a bit naked. Let's fix that wardrobe situation.");
                ImGui.PopTextWrapPos();

                ImGui.Spacing();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf005"); // Star icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), " Click the 'Designs' button on your Character!");

                ImGui.Spacing();
                if (ImGui.Button("End Tutorial", new Vector2(100 * scale, 25 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            // Highlight the designs button
            if (plugin.FirstCharacterDesignsButtonPos.HasValue && plugin.FirstCharacterDesignsButtonSize.HasValue)
            {
                HighlightButton(plugin.FirstCharacterDesignsButtonPos.Value, plugin.FirstCharacterDesignsButtonSize.Value, scale);
            }
        }

        private void DrawAddNewDesignHelp(float scale)
        {
            Vector2 popupPos;
            if (plugin.DesignPanelAddButtonPos.HasValue && plugin.DesignPanelAddButtonSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.DesignPanelAddButtonPos.Value.X + (300 * scale),
                    plugin.DesignPanelAddButtonPos.Value.Y - (30 * scale)
                );

                var viewport = ImGui.GetMainViewport();
                if (popupPos.X + (440 * scale) > viewport.Pos.X + viewport.Size.X)
                {
                    popupPos.X = plugin.DesignPanelAddButtonPos.Value.X - (460 * scale);
                }
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + (700 * scale), viewport.Pos.Y + (200 * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(440 * scale, 180 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.4f, 0.8f, 0.4f, 0.8f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Add Design",
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoBringToFrontOnFocus))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf067");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 1.0f, 0.6f, 1.0f), "Add New Design");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Perfect! Now click the '+' button to create a new Design.");
                ImGui.Spacing();

                // Use proper bullet
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf005");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), " Designs let you have multiple outfits per Character!");

                ImGui.Spacing();
                if (ImGui.Button("End Tutorial", new Vector2(100 * scale, 25 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            if (plugin.DesignPanelAddButtonPos.HasValue && plugin.DesignPanelAddButtonSize.HasValue)
            {
                HighlightButtonWithLeftwardArrow(plugin.DesignPanelAddButtonPos.Value, plugin.DesignPanelAddButtonSize.Value, popupPos, scale);
            }
        }

        private void DrawFillDesignFormHelp(float scale)
        {
            if (!plugin.IsEditDesignWindowOpen) return;

            Vector2 popupPos;
            if (plugin.DesignNameFieldPos.HasValue && plugin.DesignNameFieldSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.DesignNameFieldPos.Value.X + plugin.DesignNameFieldSize.Value.X + (50 * scale),
                    plugin.DesignNameFieldPos.Value.Y - (50 * scale)
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + (500 * scale), viewport.Pos.Y + (300 * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(520 * scale, 220 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.6f, 0.3f, 0.8f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Design Fields", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf040");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1.0f), "Fill Design Fields");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Time for some fashion paperwork (yes, even virtual clothes need documentation):");
                ImGui.Spacing();

                ImGui.BulletText("Design Name* - e.g. 'Casual Outfit'");
                ImGui.BulletText("Glamourer Design* - Must match exactly");

                ImGui.Spacing();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf005"); // Star icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), " Required fields will glow!");

                ImGui.Spacing();
                if (ImGui.Button("End Tutorial", new Vector2(100 * scale, 25 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            HighlightRequiredDesignFields(scale);
        }

        private void DrawExploreDesignOptionsHelp(float scale)
        {
            if (!plugin.IsEditDesignWindowOpen) return;

            Vector2 popupPos;
            if (plugin.DesignNameFieldPos.HasValue && plugin.DesignNameFieldSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.DesignNameFieldPos.Value.X + plugin.DesignNameFieldSize.Value.X + (50 * scale),
                    plugin.DesignNameFieldPos.Value.Y + (100 * scale)
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + (500 * scale), viewport.Pos.Y + (350 * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(520 * scale, 360 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.4f, 0.6f, 0.8f, 0.8f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Design Options", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf53f");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 0.8f, 1.0f, 1.0f), "Design Options");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Great! Here are additional options for Designs:");
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf013");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Glamourer Automation");

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Apply Glamourer Automations when switching to this Design");
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf007"); // User icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Customize+ Profile");

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Apply a specific Customize+ Profile when switching to this Design");
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf302");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Preview Image");

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Shows when hovering over the Apply Design button");
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb"); // Lightbulb icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), " You can also use '/select Character Name Design Name'!");
                
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb"); // Lightbulb icon
                ImGui.PopFont();
                
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), " These are optional - let's save your Design!");
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 120f;
                float spacing = 15f;
                float totalWidth = (buttonWidth * 2) + spacing;
                float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;

                ImGui.SetCursorPosX(startX);

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.4f, 0.15f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.5f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.35f, 0.1f, 1f));

                if (ImGui.Button("Continue", new Vector2(buttonWidth, 25 * scale)))
                {
                    NextStep();
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();
                ImGui.SetCursorPosX(startX + buttonWidth + spacing);

                if (ImGui.Button("End Tutorial", new Vector2(buttonWidth, 25 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }

        private void DrawSaveDesignHelp(float scale)
        {
            if (!plugin.IsEditDesignWindowOpen) return;

            Vector2 popupPos;
            if (plugin.SaveDesignButtonPos.HasValue && plugin.SaveDesignButtonSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.SaveDesignButtonPos.Value.X + plugin.SaveDesignButtonSize.Value.X + (30 * scale),
                    plugin.SaveDesignButtonPos.Value.Y - (100 * scale)
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + (500 * scale), viewport.Pos.Y + (400 * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(520 * scale, 240 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.8f, 0.3f, 0.8f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Save Design", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0c7");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 1.0f, 0.7f, 1.0f), "Save Your Design!");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Perfect! Now click 'Save Design' to create your first Design!");

                ImGui.Spacing();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb"); // Lightbulb icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), " After saving, you can:");
                ImGui.BulletText("Create more Designs for different occasions");
                ImGui.BulletText("Apply Designs by clicking the Apply Design button (checkmark) in the list");
                ImGui.BulletText("Set up RP Profiles and explore the gallery");

                ImGui.Spacing();
                if (ImGui.Button("End Tutorial", new Vector2(100 * scale, 25 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            if (plugin.SaveDesignButtonPos.HasValue && plugin.SaveDesignButtonSize.HasValue)
            {
                HighlightButton(plugin.SaveDesignButtonPos.Value, plugin.SaveDesignButtonSize.Value, scale);
            }
        }

        private void DrawDesignManagementHelp(float scale)
        {
            Vector2 popupPos;
            if (plugin.IsDesignPanelOpen && plugin.DesignNameFieldPos.HasValue)
            {
                popupPos = new Vector2(
                    plugin.DesignNameFieldPos.Value.X + (300 * scale),
                    plugin.DesignNameFieldPos.Value.Y - (100 * scale)
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + (400 * scale), viewport.Pos.Y + (200 * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(560 * scale, 560 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.8f, 0.6f, 0.2f, 0.8f)); // Gold border
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Design Management", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf091");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.4f, 1.0f), "Congratulations!");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "You've created your first Character and Design! Here's what else you can do:");
                ImGui.Spacing();

                // Design Management Features
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf07b");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Add Folders");

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Click the folder icon to organize your Designs into categories");
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf302");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Preview Images");

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Hover over the ✓ (apply) button to see preview images you've added");
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf040");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Edit & Delete");

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Use the edit and delete buttons when hovering over Designs");
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf07d");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Drag & Drop Reordering");

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Switch to 'Manual' sorting, then drag those colourful handles like you're solving a puzzle");
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf005");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Favourites");

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Click the star icon to mark Designs as favourites");
                ImGui.Spacing();

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb"); // Lightbulb icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), " Next steps:");
                ImGui.BulletText("Set up RP Profiles with backgrounds & effects");
                ImGui.BulletText("Explore the Character Gallery");
                ImGui.BulletText("Create more Characters and Designs");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 150f;
                float spacing = 15f;
                float totalWidth = (buttonWidth * 2) + spacing;
                float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;

                ImGui.SetCursorPosX(startX);

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.4f, 0.15f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.5f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.35f, 0.1f, 1f));

                if (ImGui.Button("Continue to RP Profiles", new Vector2(buttonWidth, 30)))
                {
                    NextStep();
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();
                ImGui.SetCursorPosX(startX + buttonWidth + spacing);

                if (ImGui.Button("End Tutorial Here", new Vector2(buttonWidth, 30)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }

        private void DrawClickRPProfileButtonHelp(float scale)
        {
            var buttonInfo = GetRPProfileButtonInfo();

            if (buttonInfo.HasValue)
            {
                var (buttonPos, buttonSize) = buttonInfo.Value;
                DrawInstructionPopup("View RP Profile",
                    "Click the ID card icon next to your Character's name to view their RP Profile.",
                    buttonPos, buttonSize, scale); 

                HighlightButton(buttonPos, buttonSize, scale);
            }
            else
            {
                DrawInstructionPopup("View RP Profile",
                    "Look for the ID card icon next to your Character's name and click it to view their RP Profile.",
                    null, null, scale);
            }
        }

        private void DrawClickEditProfileHelp(float scale)
        {
            Vector2 popupPos;

            if (plugin.RPProfileViewWindowPos.HasValue && plugin.RPProfileViewWindowSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.RPProfileViewWindowPos.Value.X + plugin.RPProfileViewWindowSize.Value.X + (20f * scale),
                    plugin.RPProfileViewWindowPos.Value.Y + (100f * scale)
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + viewport.Size.X - 400f, viewport.Pos.Y + (150f * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(350 * scale, 160 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.9f, 0.7f, 0.2f, 0.9f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Edit RP Profile",
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoFocusOnAppearing))
            {
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf040");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 1.0f, 0.7f, 1.0f), "Edit RP Profile");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Click the 'Edit Profile' button to start customizing!");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 100f;
                float startX = (ImGui.GetContentRegionAvail().X - buttonWidth) * 0.5f;
                ImGui.SetCursorPosX(startX);

                if (ImGui.Button("End Tutorial", new Vector2(buttonWidth, 25 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            var buttonInfo = GetEditProfileButtonInfo();
            if (buttonInfo.HasValue)
            {
                var (buttonPos, buttonSize) = buttonInfo.Value;
                HighlightButton(buttonPos, buttonSize, scale);
            }
        }

        private void DrawStartWithPronounsHelp(float scale)
        {
            if (!plugin.IsRPProfileEditorOpen) return;

            Vector2 popupPos;

            if (plugin.RPProfileEditorWindowPos.HasValue && plugin.RPProfileEditorWindowSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.RPProfileEditorWindowPos.Value.X + plugin.RPProfileEditorWindowSize.Value.X,
                    plugin.RPProfileEditorWindowPos.Value.Y + 80f
                );
            }
            else
            {
                // Fallback positioning
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + viewport.Size.X - 320f, viewport.Pos.Y + 80f);
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(320 * scale, 160 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.9f, 0.7f, 0.2f, 0.9f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 8 * scale));

            if (ImGui.Begin("Tutorial: Start Here",
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoBringToFrontOnFocus))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf256");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1.0f), "Start with Pronouns");

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Great! Start by filling in the Pronouns field.");
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb"); // Lightbulb icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), " Freeform fields - enter anything you like");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 85f;
                float spacing = 10f;
                float totalWidth = (buttonWidth * 2) + spacing;
                float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;

                ImGui.SetCursorPosX(startX);

                if (ImGui.Button("Continue", new Vector2(buttonWidth, 20 * scale)))
                {
                    NextStep();
                }
                ImGui.SameLine();
                if (ImGui.Button("End Tutorial", new Vector2(buttonWidth, 20 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            if (plugin.RPPronounsFieldPos.HasValue && plugin.RPPronounsFieldSize.HasValue)
            {
                var dl = ImGui.GetForegroundDrawList();
                float time = (float)ImGui.GetTime();
                float pulse = 0.4f + 0.4f * (float)Math.Sin(time * 2.5f);
                var glowColor = new Vector4(0.9f, 0.7f, 0.2f, pulse * 0.8f);

                for (int i = 0; i < 3; i++)
                {
                    float expansion = i * 4f + 2f;
                    dl.AddRect(
                        plugin.RPPronounsFieldPos.Value - new Vector2(expansion, expansion),
                        plugin.RPPronounsFieldPos.Value + plugin.RPPronounsFieldSize.Value + new Vector2(expansion, expansion),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(glowColor.X, glowColor.Y, glowColor.Z, glowColor.W * (1f - i * 0.3f))),
                        3f, ImDrawFlags.None, 2f
                    );
                }

                var fieldCenter = plugin.RPPronounsFieldPos.Value + (plugin.RPPronounsFieldSize.Value * 0.5f);
                var tutorialLeft = new Vector2(popupPos.X, popupPos.Y + 80f);

                dl.AddLine(tutorialLeft, fieldCenter, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);

                var direction = Vector2.Normalize(fieldCenter - tutorialLeft);
                var arrowHead1 = fieldCenter - direction * 8f + new Vector2(-direction.Y, direction.X) * 4f;
                var arrowHead2 = fieldCenter - direction * 8f + new Vector2(direction.Y, -direction.X) * 4f;
                dl.AddLine(fieldCenter, arrowHead1, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);
                dl.AddLine(fieldCenter, arrowHead2, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);
            }
        }

        private void DrawAddBioHelp(float scale)
        {
            if (!plugin.IsRPProfileEditorOpen) return;

            Vector2 popupPos;

            if (plugin.RPProfileEditorWindowPos.HasValue && plugin.RPProfileEditorWindowSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.RPProfileEditorWindowPos.Value.X + plugin.RPProfileEditorWindowSize.Value.X + (50f * scale),
                    plugin.RPProfileEditorWindowPos.Value.Y + (80f * scale)
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + viewport.Size.X - 320f, viewport.Pos.Y + 120f);
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(300 * scale, 260 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.9f, 0.7f, 0.2f, 0.9f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Character Bio",
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoBringToFrontOnFocus))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf02d");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 0.8f, 1.0f, 1.0f), "Add Character Bio");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "The bio is where you describe your Character's personality, background, and story.");
                ImGui.PopTextWrapPos();

                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "💡 Tips for great bios:");
                ImGui.BulletText("Keep it concise but interesting");
                ImGui.BulletText("Include personality traits");
                ImGui.BulletText("Add hooks for RP interactions");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 120f * scale;
                float spacing = 15f;
                float totalWidth = (buttonWidth * 2) + spacing;
                float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;

                ImGui.SetCursorPosX(startX);

                if (ImGui.Button("Continue", new Vector2(buttonWidth, 25 * scale)))
                {
                    NextStep();
                }

                ImGui.SameLine();
                ImGui.SetCursorPosX(startX + buttonWidth + spacing);

                if (ImGui.Button("End Tutorial", new Vector2(buttonWidth, 25 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            if (plugin.RPBioFieldPos.HasValue && plugin.RPBioFieldSize.HasValue)
            {
                var dl = ImGui.GetForegroundDrawList();
                float time = (float)ImGui.GetTime();
                float pulse = 0.4f + 0.4f * (float)Math.Sin(time * 2.5f);
                var glowColor = new Vector4(0.9f, 0.7f, 0.2f, pulse * 0.8f);

                // Glow around the bio field
                for (int i = 0; i < 3; i++)
                {
                    float expansion = i * 4f + 2f;
                    dl.AddRect(
                        plugin.RPBioFieldPos.Value - new Vector2(expansion, expansion),
                        plugin.RPBioFieldPos.Value + plugin.RPBioFieldSize.Value + new Vector2(expansion, expansion),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(glowColor.X, glowColor.Y, glowColor.Z, glowColor.W * (1f - i * 0.3f))),
                        3f, ImDrawFlags.None, 2f
                    );
                }

                var arrowStart = plugin.RPBioFieldPos.Value + new Vector2(plugin.RPBioFieldSize.Value.X + 35, plugin.RPBioFieldSize.Value.Y / 2);
                var arrowEnd = arrowStart + new Vector2(20 * scale, 0 * scale);
                dl.AddLine(arrowStart, arrowEnd, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);

                var arrowHead1 = arrowStart + new Vector2(6, -4);
                var arrowHead2 = arrowStart + new Vector2(6 * scale, 4 * scale);
                dl.AddLine(arrowStart, arrowHead1, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);
                dl.AddLine(arrowStart, arrowHead2, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);
            }
        }

        private void DrawExploreImageOptionsHelp(float scale)
        {
            if (!plugin.IsRPProfileEditorOpen) return;

            Vector2 popupPos;

            if (plugin.RPProfileEditorWindowPos.HasValue && plugin.RPProfileEditorWindowSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.RPProfileEditorWindowPos.Value.X + (50f * scale),
                    plugin.RPProfileEditorWindowPos.Value.Y - (180f * scale)
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + viewport.Size.X - 450f, viewport.Pos.Y + 80f);
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(500 * scale, 180 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.9f, 0.7f, 0.2f, 0.9f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 8 * scale));

            if (ImGui.Begin("Tutorial: Image Options", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf03e");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.8f, 1.0f), "Upload a new image or stick with what you've got");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf030");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Upload your own image or keep the one you already chose.");

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf00e");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Position & Zoom - Adjust cropping and positioning");

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb"); // Lightbulb icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), " These settings are optional - you can adjust them later!");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 85f;
                float spacing = 10f;
                float totalWidth = (buttonWidth * 2) + spacing;
                float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;

                ImGui.SetCursorPosX(startX);

                if (ImGui.Button("Continue", new Vector2(buttonWidth, 20 * scale)))
                {
                    NextStep();
                }
                ImGui.SameLine();
                if (ImGui.Button("End Tutorial", new Vector2(buttonWidth, 20 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }

        private void DrawExploreVisualOptionsHelp(float scale)
        {
            if (!plugin.IsRPProfileEditorOpen) return;

            Vector2 popupPos;

            if (plugin.RPProfileEditorWindowPos.HasValue && plugin.RPProfileEditorWindowSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.RPProfileEditorWindowPos.Value.X + (100f * scale),
                    plugin.RPProfileEditorWindowPos.Value.Y - (200f * scale) 
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + viewport.Size.X - (480f * scale), viewport.Pos.Y + (120f * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(540 * scale, 240 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.9f, 0.7f, 0.2f, 0.9f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 8 * scale));

            if (ImGui.Begin("Tutorial: Backgrounds & Animations",
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoBringToFrontOnFocus))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf53f");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.8f, 1.0f), "Backgrounds & Animations");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf6fc");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Background Images - Choose from 50+ FFXIV locations");

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0d0");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Animated Effects - Make it rain butterflies, because why shouldn't your character live in a Disney movie?");
                ImGui.PopTextWrapPos();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf53f");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Animated Effects - Choose to match particle colours to your background");

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb"); // Lightbulb icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), " Mix and match effects to create your perfect aesthetic!");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 85f;
                float spacing = 10f;
                float totalWidth = (buttonWidth * 2) + spacing;
                float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;

                ImGui.SetCursorPosX(startX);

                if (ImGui.Button("Continue", new Vector2(buttonWidth, 20 * scale)))
                {
                    NextStep();
                }
                ImGui.SameLine();
                if (ImGui.Button("End Tutorial", new Vector2(100 * scale, 20 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            HighlightBackgroundAndEffectsWithConnectors(popupPos, scale);
        }

        private void DrawSaveRPProfileHelp(float scale)
        {
            if (!plugin.IsRPProfileEditorOpen) return;

            Vector2 popupPos;

            if (plugin.RPProfileEditorWindowPos.HasValue && plugin.RPProfileEditorWindowSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.RPProfileEditorWindowPos.Value.X + (plugin.RPProfileEditorWindowSize.Value.X * 0.5f) - 225f,
                    plugin.RPProfileEditorWindowPos.Value.Y + plugin.RPProfileEditorWindowSize.Value.Y + 10f
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + viewport.Size.X - 470f, viewport.Pos.Y + 400f);
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(580 * scale, 320 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.9f, 0.7f, 0.2f, 0.9f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 8 * scale));

            if (ImGui.Begin("Tutorial: Save RP Profile",
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoBringToFrontOnFocus))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0c7");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 1.0f, 0.7f, 1.0f), "Save Your RP Profile!");

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Perfect! Before saving, check your Profile Sharing setting:");
                ImGui.Spacing();

                // Sharing options explanation
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf070"); // Eye-slash icon for never share
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Private");
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Only you can see your Profile - keeps it completely hidden");
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf059"); // Question circle icon for share when requested
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Direct Sharing");
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Viewable via friend's list or chat commands - won't appear in Gallery");
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0ac"); // Globe icon for public
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Public");
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Goes public for all to admire - time to show off your creative genius!");
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf00c"); // Check mark icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), " You've learned: Characters, Designs, RP Profiles, and Backgrounds + Animations!");

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf005"); // Star icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), " Ready to explore the Gallery and Quick Switch features!");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float skipWidth = 100f;
                float startX = (ImGui.GetContentRegionAvail().X - skipWidth) * 0.5f;
                ImGui.SetCursorPosX(startX);

                if (ImGui.Button("End Tutorial", new Vector2(skipWidth, 25)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            HighlightPrivacyAndSaveWithConnectors(popupPos, scale);
        }

        private void DrawExploreMainFeaturesHelp(float scale)
        {
            var viewport = ImGui.GetMainViewport();
            var center = viewport.GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vector2(520 * scale, 340 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.8f, 0.6f, 0.2f, 0.8f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Explore Main Features", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf091");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.4f, 1.0f), "Great Job! You've learned the basics!");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Before we finish, let's explore some powerful features:");
                ImGui.Spacing();

                ImGui.Indent(15);
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf013");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Settings");

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Customize how Character Select+ works for you");
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0e7");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Quick Switch");

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "A compact window with dropdowns to instantly change Characters and Designs.");
                ImGui.Spacing();

                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf302");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1.0f), "Gallery");

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.75f, 1.0f), "Browse and discover amazing community Characters");
                ImGui.Unindent(15);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 120f;
                float spacing = 20f;
                float totalWidth = (buttonWidth * 2) + spacing;
                float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;

                ImGui.SetCursorPosX(startX);

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.4f, 0.15f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.5f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.35f, 0.1f, 1f));

                if (ImGui.Button("Show Me!", new Vector2(buttonWidth, 28)))
                {
                    NextStep();
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();
                ImGui.SetCursorPosX(startX + buttonWidth + spacing);

                if (ImGui.Button("Finish Tutorial", new Vector2(buttonWidth, 28)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }

        private void DrawSettingsOverviewHelp(float scale)
        {
            Vector2 popupPos;
            if (plugin.SettingsButtonPos.HasValue && plugin.SettingsButtonSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.SettingsButtonPos.Value.X + plugin.SettingsButtonSize.Value.X + (30f * scale),
                    plugin.SettingsButtonPos.Value.Y + (80f * scale)
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + (100f * scale), viewport.Pos.Y + (600f * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(560 * scale, 300 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.6f, 0.4f, 0.8f, 0.9f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Settings Overview", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf013");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.8f, 0.6f, 1.0f, 1.0f), "Settings Overview");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Settings: where perfectionists go to spend 3 hours tweaking things that were already fine:");
                ImGui.Spacing();

                ImGui.BulletText("Profile display - sizes, spacing, and grid layout options");
                ImGui.BulletText("Glamourer Automations - opt-in to enable automation features");
                ImGui.BulletText("Auto-apply behaviours - Character on login, Designs on job change");
                ImGui.BulletText("Quick Switch compactness and visual feedback effects");
                ImGui.BulletText("UI scaling and sorting preferences for the main window");
                ImGui.BulletText("Immersive Dialogue options to use your CS+ Character's name and pronouns");

                ImGui.Spacing();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), " Test out what settings work best for you!");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 120f;
                float spacing = 15f;
                float totalWidth = (buttonWidth * 2) + spacing;
                float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;

                ImGui.SetCursorPosX(startX);

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.4f, 0.15f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.5f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.35f, 0.1f, 1f));

                if (ImGui.Button("Continue", new Vector2(buttonWidth, 25 * scale)))
                {
                    NextStep();
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();
                ImGui.SetCursorPosX(startX + buttonWidth + spacing);

                if (ImGui.Button("End Tutorial", new Vector2(buttonWidth, 25 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            if (plugin.SettingsButtonPos.HasValue && plugin.SettingsButtonSize.HasValue)
            {
                HighlightButtonWithProperArrow(plugin.SettingsButtonPos.Value, plugin.SettingsButtonSize.Value, popupPos, scale);
            }
        }

        private void DrawQuickSwitchOverviewHelp(float scale)
        {
            Vector2 popupPos;
            if (plugin.QuickSwitchButtonPos.HasValue && plugin.QuickSwitchButtonSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.QuickSwitchButtonPos.Value.X - (100f * scale),
                    plugin.QuickSwitchButtonPos.Value.Y + plugin.QuickSwitchButtonSize.Value.Y + (60f * scale)
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + (200f * scale), viewport.Pos.Y + (200f * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(560 * scale, 300 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.2f, 0.8f, 0.9f, 0.9f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Quick Switch", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Fixed Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0e7");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 1.0f, 1.0f), "Quick Switch - Lightning Fast Changes");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "Quick Switch: for when you need to change characters faster than you change your mind:");
                ImGui.Spacing();

                ImGui.BulletText("A simple character dropdown to pick any Character");
                ImGui.BulletText("A design dropdown to choose Designs for that Character");
                ImGui.BulletText("An 'Apply' button to instantly switch - no main window needed!");
                ImGui.BulletText("Stays open while you play for rapid Character/Design changes");
                ImGui.BulletText("You can also use '/selectswitch' to open this window!");

                ImGui.Spacing();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), " Perfect for when the RP plot twist requires you to suddenly be someone else entirely!");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 120f;
                float spacing = 15f;
                float totalWidth = (buttonWidth * 2) + spacing;
                float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;

                ImGui.SetCursorPosX(startX);

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.4f, 0.15f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.5f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.35f, 0.1f, 1f));

                if (ImGui.Button("Continue", new Vector2(buttonWidth, 25 * scale)))
                {
                    NextStep();
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();
                ImGui.SetCursorPosX(startX + buttonWidth + spacing);

                if (ImGui.Button("End Tutorial", new Vector2(buttonWidth, 25 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            if (plugin.QuickSwitchButtonPos.HasValue && plugin.QuickSwitchButtonSize.HasValue)
            {
                HighlightButtonWithUpwardArrow(plugin.QuickSwitchButtonPos.Value, plugin.QuickSwitchButtonSize.Value, popupPos, scale);
            }
        }


        private void DrawGalleryOverviewHelp(float scale)
        {
            Vector2 popupPos;
            if (plugin.GalleryButtonPos.HasValue && plugin.GalleryButtonSize.HasValue)
            {
                popupPos = new Vector2(
                    plugin.GalleryButtonPos.Value.X - (100f * scale),
                    plugin.GalleryButtonPos.Value.Y + plugin.GalleryButtonSize.Value.Y + (60f * scale)
                );
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                popupPos = new Vector2(viewport.Pos.X + (300f * scale), viewport.Pos.Y + (200f * scale));
            }

            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(new Vector2(560 * scale, 360 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.9f, 0.5f, 0.7f, 0.9f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial: Gallery Discovery", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf302");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.8f, 1.0f), "Gallery - Discover & Share");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "The Gallery: basically Instagram for your FFXIV characters (but with better lighting):");
                ImGui.Spacing();

                ImGui.BulletText("Browse other people's creative genius and feel both inspired and intimidated");
                ImGui.BulletText("Set your Main Character in the settings tab to participate!");
                ImGui.BulletText("Click any Profile to view their full RP Profile with all details");
                ImGui.BulletText("Like profiles to show appreciation - likes are tracked publicly");
                ImGui.BulletText("Favourite profiles to save a snapshot (won't change if they edit)");
                ImGui.BulletText("Right-click profile images to see the full uncropped picture");
                ImGui.BulletText("Search and filter by character name, race, or custom tags");
                ImGui.BulletText("Add and Block users to curate your experience.");
                ImGui.BulletText("You can even use '/gallery' to open this window!");

                ImGui.Spacing();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), " Remember to set your RP Profile sharing to 'Public' to appear here!");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 120f;
                float spacing = 15f;
                float totalWidth = (buttonWidth * 2) + spacing;
                float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;

                ImGui.SetCursorPosX(startX);

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.4f, 0.15f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.5f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.35f, 0.1f, 1f));

                if (ImGui.Button("Continue", new Vector2(buttonWidth, 25 * scale)))
                {
                    NextStep();
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();
                ImGui.SetCursorPosX(startX + buttonWidth + spacing);

                if (ImGui.Button("End Tutorial", new Vector2(buttonWidth, 25 * scale)))
                {
                    EndTutorial();
                }

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            if (plugin.GalleryButtonPos.HasValue && plugin.GalleryButtonSize.HasValue)
            {
                HighlightButtonWithUpwardArrow(plugin.GalleryButtonPos.Value, plugin.GalleryButtonSize.Value, popupPos, scale);
            }
        }
        private void HighlightButtonWithLeftwardArrow(Vector2 buttonPos, Vector2 buttonSize, Vector2 tutorialPos, float scale)
        {
            var dl = ImGui.GetForegroundDrawList();
            float time = (float)ImGui.GetTime();
            float pulse = 0.5f + 0.5f * (float)Math.Sin(time * 3.0f);
            var glowColor = new Vector4(0.3f, 0.6f, 1.0f, pulse * 0.6f);

            // Glow around button
            for (int i = 0; i < 3; i++)
            {
                float expansion = i * 6f + pulse * 4f;
                dl.AddRect(
                    buttonPos - new Vector2(expansion, expansion),
                    buttonPos + buttonSize + new Vector2(expansion, expansion),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(glowColor.X, glowColor.Y, glowColor.Z, glowColor.W * (1f - i * 0.3f))),
                    4f, ImDrawFlags.None, 2f
                );
            }

            var buttonCenter = buttonPos + (buttonSize * 0.5f);
            var tutorialLeft = new Vector2(tutorialPos.X, tutorialPos.Y + 70f);

            dl.AddLine(tutorialLeft, buttonCenter, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);

            var direction = Vector2.Normalize(buttonCenter - tutorialLeft);
            var arrowHead1 = buttonCenter - direction * 8f + new Vector2(-direction.Y, direction.X) * 4f;
            var arrowHead2 = buttonCenter - direction * 8f + new Vector2(direction.Y, -direction.X) * 4f;
            dl.AddLine(buttonCenter, arrowHead1, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);
            dl.AddLine(buttonCenter, arrowHead2, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);
        }

        private void HighlightButtonWithProperArrow(Vector2 buttonPos, Vector2 buttonSize, Vector2 tutorialPos, float scale)
        {
            var dl = ImGui.GetForegroundDrawList();
            float time = (float)ImGui.GetTime();
            float pulse = 0.5f + 0.5f * (float)Math.Sin(time * 3.0f);
            var glowColor = new Vector4(0.3f, 0.6f, 1.0f, pulse * 0.6f);

            // Glow around button
            for (int i = 0; i < 3; i++)
            {
                float expansion = i * 6f + pulse * 4f;
                dl.AddRect(
                    buttonPos - new Vector2(expansion, expansion),
                    buttonPos + buttonSize + new Vector2(expansion, expansion),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(glowColor.X, glowColor.Y, glowColor.Z, glowColor.W * (1f - i * 0.3f))),
                    4f, ImDrawFlags.None, 2f
                );
            }

            var buttonCenter = buttonPos + (buttonSize * 0.5f);
            var tutorialSide = new Vector2(tutorialPos.X, tutorialPos.Y + 120f);

            dl.AddLine(tutorialSide, buttonCenter, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);

            var direction = Vector2.Normalize(buttonCenter - tutorialSide);
            var arrowHead1 = buttonCenter - direction * 8f + new Vector2(-direction.Y, direction.X) * 4f;
            var arrowHead2 = buttonCenter - direction * 8f + new Vector2(direction.Y, -direction.X) * 4f;
            dl.AddLine(buttonCenter, arrowHead1, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);
            dl.AddLine(buttonCenter, arrowHead2, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);
        }

        private void HighlightButtonWithUpwardArrow(Vector2 buttonPos, Vector2 buttonSize, Vector2 tutorialPos, float scale)
        {
            var dl = ImGui.GetForegroundDrawList();
            float time = (float)ImGui.GetTime();
            float pulse = 0.5f + 0.5f * (float)Math.Sin(time * 3.0f);
            var glowColor = new Vector4(0.3f, 0.6f, 1.0f, pulse * 0.6f);

            // Glow around button
            for (int i = 0; i < 3; i++)
            {
                float expansion = i * 6f + pulse * 4f;
                dl.AddRect(
                    buttonPos - new Vector2(expansion, expansion),
                    buttonPos + buttonSize + new Vector2(expansion, expansion),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(glowColor.X, glowColor.Y, glowColor.Z, glowColor.W * (1f - i * 0.3f))),
                    4f, ImDrawFlags.None, 2f
                );
            }

            var buttonCenter = buttonPos + (buttonSize * 0.5f);
            var tutorialTop = new Vector2(tutorialPos.X + (200f * scale), tutorialPos.Y);

            dl.AddLine(tutorialTop, buttonCenter, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);

            var direction = Vector2.Normalize(buttonCenter - tutorialTop);
            var arrowHead1 = buttonCenter - direction * 8f + new Vector2(-direction.Y, direction.X) * 4f;
            var arrowHead2 = buttonCenter - direction * 8f + new Vector2(direction.Y, -direction.X) * 4f;
            dl.AddLine(buttonCenter, arrowHead1, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);
            dl.AddLine(buttonCenter, arrowHead2, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f);
        }
        private void DrawTutorialCompleteHelp(float scale)
        {
            var viewport = ImGui.GetMainViewport();
            var center = viewport.GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vector2(600 * scale, 300 * scale), ImGuiCond.Always);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.8f, 0.3f, 0.8f)); // Green border
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15 * scale, 15 * scale));

            if (ImGui.Begin("Tutorial Complete!", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                // Fixed Icons
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf091");
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 1.0f, 0.7f, 1.0f), "Congratulations!");

                ImGui.Separator();
                ImGui.Spacing();

                ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.85f, 1.0f), "You've graduated from Character Select+ University! Your diploma includes mastery of:");
                ImGui.PopTextWrapPos();

                ImGui.Spacing();
                ImGui.BulletText("Creating Characters");
                ImGui.BulletText("Adding Designs for your Character");
                ImGui.BulletText("Setting up rich RP Profiles that are unique to you");
                ImGui.BulletText("Using Settings, Quick Switch, and the Gallery");

                ImGui.Spacing();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text("\uf0eb"); // Lightbulb icon
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), " Now go forth and create Characters so amazing they'll make other players green with envy!");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 120f;
                float startX = (ImGui.GetContentRegionAvail().X - buttonWidth) * 0.5f;
                ImGui.SetCursorPosX(startX);

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.4f, 0.15f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.5f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.35f, 0.1f, 1f));

                if (ImGui.Button("Finish", new Vector2(buttonWidth, 28)))
                {
                    EndTutorial();
                }
                ImGui.PopStyleColor(3);

                ImGui.End();
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }

        // Helper methods for button info
        private (Vector2 pos, Vector2 size)? GetRPProfileButtonInfo()
        {
            if (plugin.RPProfileButtonPos.HasValue && plugin.RPProfileButtonSize.HasValue)
            {
                return (plugin.RPProfileButtonPos.Value, plugin.RPProfileButtonSize.Value);
            }
            return null;
        }

        private (Vector2 pos, Vector2 size)? GetEditProfileButtonInfo()
        {
            if (plugin.EditProfileButtonPos.HasValue && plugin.EditProfileButtonSize.HasValue)
            {
                return (plugin.EditProfileButtonPos.Value, plugin.EditProfileButtonSize.Value);
            }
            return null;
        }

        private void HighlightRequiredFields(float scale)
        {
            var dl = ImGui.GetForegroundDrawList();
            float time = (float)ImGui.GetTime();
            float pulse = 0.4f + 0.4f * (float)Math.Sin(time * 2.5f);
            var glowColor = new Vector4(0.9f, 0.7f, 0.2f, pulse * 0.8f); // Golden glow

            if (string.IsNullOrWhiteSpace(plugin.NewCharacterName) &&
                plugin.CharacterNameFieldPos.HasValue && plugin.CharacterNameFieldSize.HasValue)
            {
                HighlightInputField(dl, plugin.CharacterNameFieldPos.Value, plugin.CharacterNameFieldSize.Value, glowColor, "Character Name", scale);
            }

            if (string.IsNullOrWhiteSpace(plugin.NewPenumbraCollection) &&
                plugin.PenumbraFieldPos.HasValue && plugin.PenumbraFieldSize.HasValue)
            {
                HighlightInputField(dl, plugin.PenumbraFieldPos.Value, plugin.PenumbraFieldSize.Value, glowColor, "Penumbra Collection", scale);
            }

            if (string.IsNullOrWhiteSpace(plugin.NewGlamourerDesign) &&
                plugin.GlamourerFieldPos.HasValue && plugin.GlamourerFieldSize.HasValue)
            {
                HighlightInputField(dl, plugin.GlamourerFieldPos.Value, plugin.GlamourerFieldSize.Value, glowColor, "Glamourer Design", scale);
            }
        }

        private void HighlightRequiredDesignFields(float scale)
        {
            var dl = ImGui.GetForegroundDrawList();
            float time = (float)ImGui.GetTime();
            float pulse = 0.4f + 0.4f * (float)Math.Sin(time * 2.5f);
            var glowColor = new Vector4(0.9f, 0.7f, 0.2f, pulse * 0.8f);

            if (string.IsNullOrWhiteSpace(plugin.EditedDesignName) &&
                plugin.DesignNameFieldPos.HasValue && plugin.DesignNameFieldSize.HasValue)
            {
                HighlightInputField(dl, plugin.DesignNameFieldPos.Value, plugin.DesignNameFieldSize.Value, glowColor, "Design Name", scale);
            }

            if (string.IsNullOrWhiteSpace(plugin.EditedGlamourerDesign) &&
                plugin.DesignGlamourerFieldPos.HasValue && plugin.DesignGlamourerFieldSize.HasValue)
            {
                HighlightInputField(dl, plugin.DesignGlamourerFieldPos.Value, plugin.DesignGlamourerFieldSize.Value, glowColor, "Glamourer Design", scale);
            }
        }

        private void HighlightInputField(ImDrawListPtr dl, Vector2 fieldPos, Vector2 fieldSize, Vector4 glowColor, string fieldName, float scale)
        {
            // Glow around the input field
            for (int i = 0; i < 3; i++)
            {
                float expansion = (i * 4f + 2f) * scale;
                dl.AddRect(
                    fieldPos - new Vector2(expansion, expansion),
                    fieldPos + fieldSize + new Vector2(expansion, expansion),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(glowColor.X, glowColor.Y, glowColor.Z, glowColor.W * (1f - i * 0.3f))),
                    3f * scale, ImDrawFlags.None, 2f * scale 
                );
            }

            var arrowStart = fieldPos + new Vector2(fieldSize.X + (35 * scale), fieldSize.Y / 2);
            var arrowEnd = arrowStart + new Vector2(20 * scale, 0); 
            dl.AddLine(arrowStart, arrowEnd, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f * scale);

            var arrowHead1 = arrowStart + new Vector2(6 * scale, -4 * scale);
            var arrowHead2 = arrowStart + new Vector2(6 * scale, 4 * scale);
            dl.AddLine(arrowStart, arrowHead1, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f * scale);
            dl.AddLine(arrowStart, arrowHead2, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.4f, 1f)), 2f * scale);
        }

        private void HighlightSaveButton(float scale)
        {
            if (plugin.SaveButtonPos.HasValue && plugin.SaveButtonSize.HasValue)
            {
                HighlightButton(plugin.SaveButtonPos.Value, plugin.SaveButtonSize.Value, scale);
            }
        }

        private void HighlightBackgroundAndEffectsWithConnectors(Vector2 tutorialPos, float scale)
        {
            var dl = ImGui.GetForegroundDrawList();
            float time = (float)ImGui.GetTime();
            float pulse = 0.4f + 0.4f * (float)Math.Sin(time * 2.5f);
            var glowColor = new Vector4(0.2f, 0.8f, 0.9f, pulse * 0.8f);

            // Highlight Background dropdown
            if (plugin.RPBackgroundDropdownPos.HasValue && plugin.RPBackgroundDropdownSize.HasValue)
            {
                // Glow around the dropdown
                for (int i = 0; i < 3; i++)
                {
                    float expansion = i * 4f + 2f;
                    dl.AddRect(
                        plugin.RPBackgroundDropdownPos.Value - new Vector2(expansion, expansion),
                        plugin.RPBackgroundDropdownPos.Value + plugin.RPBackgroundDropdownSize.Value + new Vector2(expansion, expansion),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(glowColor.X, glowColor.Y, glowColor.Z, glowColor.W * (1f - i * 0.3f))),
                        3f, ImDrawFlags.None, 2f
                    );
                }

                var dropdownCenter = plugin.RPBackgroundDropdownPos.Value + (plugin.RPBackgroundDropdownSize.Value * 0.5f);
                var tutorialBottom = new Vector2(tutorialPos.X + (200f * scale), tutorialPos.Y + (200f * scale));

                dl.AddLine(tutorialBottom, dropdownCenter, ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.9f, 1f, 1f)), 2f);

                var direction = Vector2.Normalize(dropdownCenter - tutorialBottom);
                var arrowHead1 = dropdownCenter - direction * 8f + new Vector2(-direction.Y, direction.X) * 4f;
                var arrowHead2 = dropdownCenter - direction * 8f + new Vector2(direction.Y, -direction.X) * 4f;
                dl.AddLine(dropdownCenter, arrowHead1, ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.9f, 1f, 1f)), 2f);
                dl.AddLine(dropdownCenter, arrowHead2, ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.9f, 1f, 1f)), 2f);
            }

            // Highlight Visual Effects section
            if (plugin.RPVisualEffectsPos.HasValue && plugin.RPVisualEffectsSize.HasValue)
            {
                // Glow around the entire visual effects section
                for (int i = 0; i < 3; i++)
                {
                    float expansion = i * 4f + 2f;
                    dl.AddRect(
                        plugin.RPVisualEffectsPos.Value - new Vector2(expansion, expansion),
                        plugin.RPVisualEffectsPos.Value + plugin.RPVisualEffectsSize.Value + new Vector2(expansion, expansion),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(glowColor.X, glowColor.Y, glowColor.Z, glowColor.W * (1f - i * 0.3f))),
                        3f, ImDrawFlags.None, 2f
                    );
                }

                var effectsCenter = plugin.RPVisualEffectsPos.Value + (plugin.RPVisualEffectsSize.Value * 0.5f);
                var tutorialBottom = new Vector2(tutorialPos.X + (350f * scale), tutorialPos.Y + (200f * scale) );

                dl.AddLine(tutorialBottom, effectsCenter, ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.9f, 1f, 1f)), 2f);

                var direction = Vector2.Normalize(effectsCenter - tutorialBottom);
                var arrowHead1 = effectsCenter - direction * 8f + new Vector2(-direction.Y, direction.X) * 4f;
                var arrowHead2 = effectsCenter - direction * 8f + new Vector2(direction.Y, -direction.X) * 4f;
                dl.AddLine(effectsCenter, arrowHead1, ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.9f, 1f, 1f)), 2f);
                dl.AddLine(effectsCenter, arrowHead2, ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.9f, 1f, 1f)), 2f);
            }
        }

        private void HighlightPrivacyAndSaveWithConnectors(Vector2 tutorialPos, float scale)
        {
            var dl = ImGui.GetForegroundDrawList();
            float time = (float)ImGui.GetTime();
            float pulse = 0.4f + 0.4f * (float)Math.Sin(time * 2.5f);

            // Highlight Privacy/Sharing dropdown with purple glow
            if (plugin.RPSharingDropdownPos.HasValue && plugin.RPSharingDropdownSize.HasValue)
            {
                var privacyGlowColor = new Vector4(0.8f, 0.4f, 0.9f, pulse * 0.8f);

                // Glow around the privacy dropdown
                for (int i = 0; i < 3; i++)
                {
                    float expansion = i * 4f + 2f;
                    dl.AddRect(
                        plugin.RPSharingDropdownPos.Value - new Vector2(expansion, expansion),
                        plugin.RPSharingDropdownPos.Value + plugin.RPSharingDropdownSize.Value + new Vector2(expansion, expansion),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(privacyGlowColor.X, privacyGlowColor.Y, privacyGlowColor.Z, privacyGlowColor.W * (1f - i * 0.3f))),
                        3f, ImDrawFlags.None, 2f
                    );
                }

                var dropdownCenter = plugin.RPSharingDropdownPos.Value + (plugin.RPSharingDropdownSize.Value * 0.5f);
                var tutorialTop = new Vector2(tutorialPos.X + (150f * scale), tutorialPos.Y);

                dl.AddLine(tutorialTop, dropdownCenter, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.6f, 1f, 1f)), 2f);

                var direction = Vector2.Normalize(dropdownCenter - tutorialTop);
                var arrowHead1 = dropdownCenter - direction * 8f + new Vector2(-direction.Y, direction.X) * 4f;
                var arrowHead2 = dropdownCenter - direction * 8f + new Vector2(direction.Y, -direction.X) * 4f;
                dl.AddLine(dropdownCenter, arrowHead1, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.6f, 1f, 1f)), 2f);
                dl.AddLine(dropdownCenter, arrowHead2, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.6f, 1f, 1f)), 2f);
            }

            // Highlight Save button with green glow
            if (plugin.SaveRPProfileButtonPos.HasValue && plugin.SaveRPProfileButtonSize.HasValue)
            {
                var saveGlowColor = new Vector4(0.3f, 0.8f, 0.3f, pulse * 0.6f);

                // Glow highlight around save button
                for (int i = 0; i < 3; i++)
                {
                    float expansion = i * 6f + pulse * 4f;
                    dl.AddRect(
                        plugin.SaveRPProfileButtonPos.Value - new Vector2(expansion, expansion),
                        plugin.SaveRPProfileButtonPos.Value + plugin.SaveRPProfileButtonSize.Value + new Vector2(expansion, expansion),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(saveGlowColor.X, saveGlowColor.Y, saveGlowColor.Z, saveGlowColor.W * (1f - i * 0.3f))),
                        4f, ImDrawFlags.None, 2f
                    );
                }

                var buttonCenter = plugin.SaveRPProfileButtonPos.Value + (plugin.SaveRPProfileButtonSize.Value * 0.5f);
                var tutorialTop = new Vector2(tutorialPos.X + (300f * scale), tutorialPos.Y);

                dl.AddLine(tutorialTop, buttonCenter, ImGui.ColorConvertFloat4ToU32(new Vector4(0.6f, 1f, 0.6f, 1f)), 3f);

                var direction = Vector2.Normalize(buttonCenter - tutorialTop);
                var arrowHead1 = buttonCenter - direction * 8f + new Vector2(-direction.Y, direction.X) * 4f;
                var arrowHead2 = buttonCenter - direction * 8f + new Vector2(direction.Y, -direction.X) * 4f;
                dl.AddLine(buttonCenter, arrowHead1, ImGui.ColorConvertFloat4ToU32(new Vector4(0.6f, 1f, 0.6f, 1f)), 3f);
                dl.AddLine(buttonCenter, arrowHead2, ImGui.ColorConvertFloat4ToU32(new Vector4(0.6f, 1f, 0.6f, 1f)), 3f);
            }
        }
        private float GetSafeScale(float baseScale)
        {
            return Math.Clamp(baseScale, 0.3f, 5.0f);
        }
    }
}
