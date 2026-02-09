using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Textures.TextureWraps;
using Newtonsoft.Json;
using CharacterSelectPlugin.Windows;

namespace CharacterSelectPlugin.Windows.Components
{
    public static class ContentBoxEditor
    {
        private static Dictionary<string, object> editorState = new();

        // Available CS+ character names for the Connections dropdown
        public static List<string> AvailableCharacterNames { get; set; } = new();

        // Mapping from CS+ character name to in-game name (for server lookups)
        public static Dictionary<string, string> CharacterInGameNames { get; set; } = new();

        // Common relationship types
        private static readonly string[] RelationshipTypes = new[]
        {
            "Friend",
            "Family",
            "Sibling",
            "Parent",
            "Child",
            "Partner",
            "Spouse",
            "Rival",
            "Enemy",
            "Mentor",
            "Student",
            "Colleague",
            "Acquaintance",
            "Alt",
            "Past Self",
            "Other"
        };
        
        // Main editor method that dispatches to specific layout editors
        public static bool DrawContentBoxEditor(ContentBox box, float availableWidth, float scale = 1.0f)
        {
            bool modified = false;
            var boxId = box.GetHashCode().ToString();
            
            // Simple, consistent styling
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4 * scale, 4 * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4 * scale, 4 * scale));
            
            // Calculate label width to align fields
            var labelWidth = Math.Max(
                ImGui.CalcTextSize("Title:").X,
                ImGui.CalcTextSize("Description:").X
            ) + 40 * scale; // Add more padding to prevent overlap
            
            // Title field
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Title:");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("The heading for this content section");
                ImGui.EndTooltip();
            }
            ImGui.SameLine(labelWidth);
            ImGui.SetNextItemWidth(availableWidth - labelWidth);
            var title = box.Title ?? "";
            if (ImGui.InputText($"##title{boxId}", ref title, 100))
            {
                box.Title = title;
                modified = true;
            }
            
            // Description field on new line
            ImGui.Text("Description:");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("A subtitle or brief description shown under the title");
                ImGui.EndTooltip();
            }
            ImGui.SameLine(labelWidth);
            ImGui.SetNextItemWidth(availableWidth - labelWidth);
            var subtitle = box.Subtitle ?? "";
            if (ImGui.InputText($"##subtitle{boxId}", ref subtitle, 200))
            {
                box.Subtitle = subtitle;
                modified = true;
            }
            
            ImGui.Separator();
            ImGui.Spacing();

            // Check for special sidebar boxes that use RPProfile fields instead of ContentBox.Content
            // These are edited in the Basic Info section, not here
            if (IsSpecialSidebarBox(box.Title))
            {
                DrawSpecialSidebarBoxInfo(box.Title, availableWidth, scale);
            }
            else
            {
                // Content editor - no extra wrapping, just the content
                switch (box.LayoutType)
                {
                    case ContentBoxLayoutType.Timeline:
                        modified |= DrawTimelineEditor(box, availableWidth, scale);
                        break;

                    case ContentBoxLayoutType.Grid:
                        modified |= DrawGridEditor(box, availableWidth, scale);
                        break;

                    case ContentBoxLayoutType.List:
                        modified |= DrawListEditor(box, availableWidth, scale);
                        break;

                    case ContentBoxLayoutType.KeyValue:
                        modified |= DrawKeyValueEditor(box, availableWidth, scale);
                        break;

                    case ContentBoxLayoutType.Quote:
                        modified |= DrawQuoteEditor(box, availableWidth, scale);
                        break;

                    case ContentBoxLayoutType.ProsCons:
                        modified |= DrawProsConsEditor(box, availableWidth, scale);
                        break;

                    case ContentBoxLayoutType.Tagged:
                        modified |= DrawTaggedEditor(box, availableWidth, scale);
                        break;

                    case ContentBoxLayoutType.LikesDislikes:
                        modified |= DrawLikesDislikesEditor(box, availableWidth, scale);
                        break;

                    case ContentBoxLayoutType.Connections:
                        modified |= DrawConnectionsEditor(box, availableWidth, scale);
                        break;

                    default:
                        modified |= DrawStandardEditor(box, availableWidth, scale);
                        break;
                }
            }
            
            ImGui.PopStyleVar(2);
            
            return modified;
        }
        
        private static bool DrawTimelineEditor(ContentBox box, float width, float scale)
        {
            bool modified = false;
            var entries = ParseTimelineEntries(box.TimelineData);
            
            ImGui.TextDisabled("Add timeline events:");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Create a chronological history of important events");
                ImGui.EndTooltip();
            }
            
            // Timeline entries in a simple table
            List<TimelineEntry>? toRemove = null;
            
            if (entries.Count > 0 && ImGui.BeginTable("##timeline", 3, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("Date", ImGuiTableColumnFlags.WidthFixed, 80 * scale);
                ImGui.TableSetupColumn("Event", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 25 * scale);
                
                foreach (var entry in entries)
                {
                    ImGui.PushID(entry.Id);
                    ImGui.TableNextRow();
                    
                    // Date
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    var date = entry.Date ?? "";
                    if (ImGui.InputText("##date", ref date, 50))
                    {
                        entry.Date = date;
                        modified = true;
                    }
                    
                    // Event
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    var eventText = entry.Event ?? "";
                    if (ImGui.InputText("##event", ref eventText, 500))
                    {
                        entry.Event = eventText;
                        modified = true;
                    }
                    
                    // Remove
                    ImGui.TableNextColumn();
                    if (ImGui.Button("X"))
                    {
                        toRemove ??= new List<TimelineEntry>();
                        toRemove.Add(entry);
                        modified = true;
                    }
                    
                    ImGui.PopID();
                }
                
                ImGui.EndTable();
            }
            
            // Remove marked entries
            if (toRemove != null)
            {
                foreach (var entry in toRemove)
                    entries.Remove(entry);
            }
            
            // Add button
            if (ImGui.Button("+ Add Event"))
            {
                entries.Add(new TimelineEntry { Date = DateTime.Now.Year.ToString(), Event = "New event" });
                modified = true;
            }
            
            if (modified)
            {
                box.TimelineData = JsonConvert.SerializeObject(entries);
            }
            
            return modified;
        }
        
        private static bool DrawGridEditor(ContentBox box, float width, float scale)
        {
            bool modified = false;
            var items = ParseGridItems(box.Content);
            
            ImGui.TextDisabled("Icon/Inventory items:");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Create a visual grid of items, abilities, or possessions");
                ImGui.Text("Perfect for inventories, skill lists, or collections");
                ImGui.EndTooltip();
            }
            
            // Grid items editor
            List<GridItem>? toRemove = null;
            
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                ImGui.PushID($"grid{i}");
                
                if (ImGui.BeginTable($"##gridItem{i}", 3, ImGuiTableFlags.None))
                {
                    ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, 80 * scale);
                    ImGui.TableSetupColumn("Text", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 25 * scale);
                    
                    ImGui.TableNextRow();
                    
                    // Icon picker column
                    ImGui.TableNextColumn();
                    var itemIcon = item.Icon ?? "";
                    if (DrawIconPicker($"##icon{i}", ref itemIcon, 80 * scale))
                    {
                        item.Icon = itemIcon;
                        modified = true;
                    }
                    
                    // Text column
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    var itemText = item.Text ?? "";
                    if (ImGui.InputText($"##text{i}", ref itemText, 200))
                    {
                        item.Text = itemText;
                        modified = true;
                    }
                    
                    // Remove button
                    ImGui.TableNextColumn();
                    if (ImGui.Button("X"))
                    {
                        toRemove ??= new List<GridItem>();
                        toRemove.Add(item);
                        modified = true;
                    }
                    
                    ImGui.EndTable();
                }
                
                ImGui.PopID();
            }
            
            // Remove marked items
            if (toRemove != null)
            {
                foreach (var item in toRemove)
                    items.Remove(item);
            }
            
            // Add button
            if (ImGui.Button("+ Add Item"))
            {
                items.Add(new GridItem { Icon = "62001", Text = "New item" }); // Use Sword icon ID
                modified = true;
            }
            
            if (modified)
            {
                box.Content = JsonConvert.SerializeObject(items);
            }
            
            return modified;
        }
        
        private static bool DrawIconPicker(string id, ref string selectedIcon, float buttonSize)
        {
            bool modified = false;
            
            // Display current icon button - try to render as game icon first, fall back to text
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
            
            bool buttonClicked = false;
            if (int.TryParse(selectedIcon, out int iconId))
            {
                // Try to render FFXIV game icon
                var iconTexture = GetGameIcon(iconId);
                if (iconTexture != null)
                {
                    buttonClicked = ImGui.ImageButton(iconTexture.Handle, new Vector2(buttonSize, buttonSize));
                }
                else
                {
                    buttonClicked = ImGui.Button($"Icon {iconId}##{id}", new Vector2(buttonSize, buttonSize));
                }
            }
            else
            {
                // Render as text/unicode
                buttonClicked = ImGui.Button($"{selectedIcon}##{id}", new Vector2(buttonSize, buttonSize));
            }
            
            if (buttonClicked)
            {
                // Open the new icon picker window instead of popup
                var initialIcon = uint.TryParse(selectedIcon, out uint parsedId) ? parsedId : (uint?)null;
                var iconPicker = new IconPickerWindow(initialIcon);
                Plugin.Instance?.WindowSystem.AddWindow(iconPicker);
                iconPicker.IsOpen = true;
                
                // Store reference to update the icon when window closes
                var stateKey = $"iconpicker_{id}";
                if (!editorState.ContainsKey(stateKey))
                {
                    editorState[stateKey] = iconPicker;
                }
            }
            
            ImGui.PopStyleVar();
            
            // Check if we have a pending icon picker result
            var pickerStateKey = $"iconpicker_{id}";
            if (editorState.TryGetValue(pickerStateKey, out var pickerObj) && pickerObj is IconPickerWindow picker)
            {
                if (!picker.IsOpen && picker.Confirmed && picker.SelectedIconId.HasValue)
                {
                    selectedIcon = picker.SelectedIconId.Value.ToString();
                    modified = true;
                    editorState.Remove(pickerStateKey);
                    Plugin.Instance?.WindowSystem.RemoveWindow(picker);
                }
                else if (!picker.IsOpen && !picker.Confirmed)
                {
                    // User cancelled - just clean up
                    editorState.Remove(pickerStateKey);
                    Plugin.Instance?.WindowSystem.RemoveWindow(picker);
                }
            }
            
            return modified;
        }
        
        private static IDalamudTextureWrap? GetGameIcon(int iconId)
        {
            try
            {
                var texture = Plugin.TextureProvider.GetFromGameIcon((uint)iconId);
                return texture?.GetWrapOrEmpty();
            }
            catch
            {
                return null;
            }
        }
        
        private static (int IconId, string Tooltip)[] GetCommonGameIcons()
        {
            return new (int, string)[]
            {
                // Weapons & Equipment
                (62001, "Sword"), (62002, "Shield"), (62003, "Bow"), (62004, "Staff"),
                (62005, "Dagger"), (62006, "Axe"), (62007, "Spear"), (62008, "Gun"),
                (62009, "Catalyst"), (62010, "Grimoire"), (62011, "Fishing Rod"),
                
                // Items & Materials
                (65001, "Potion"), (65002, "Crystal"), (65003, "Shard"), (65004, "Cluster"),
                (65005, "Ore"), (65006, "Wood"), (65007, "Cloth"), (65008, "Leather"),
                (65009, "Food"), (65010, "Ingredient"),
                
                // Job Stones & Skills
                (62100, "Job Stone"), (62101, "Skill"), (62102, "Spell"), (62103, "Ability"),
                (62104, "Trait"), (62105, "Action"), (62106, "Macro"), (62107, "Command"),
                
                // Common Game Icons
                (66001, "Warning"), (66002, "Info"), (66003, "Success"), (66004, "Error"),
                (66005, "Question"), (66006, "Exclamation"), (66007, "Star"), (66008, "Crown"),
                (66009, "Gem"), (66010, "Key"), (66011, "Lock"), (66012, "Unlock"),
                
                // Status & Elements
                (61001, "HP"), (61002, "MP"), (61003, "TP"), (61004, "Fire"),
                (61005, "Ice"), (61006, "Wind"), (61007, "Earth"), (61008, "Lightning"),
                (61009, "Water"), (61010, "Light"), (61011, "Dark")
            };
        }
        
        private static bool DrawListEditor(ContentBox box, float width, float scale)
        {
            bool modified = false;
            var items = ParseListItems(box.Content);
            var stateKey = $"list_type_{box.GetHashCode()}";
            
            // Get or initialize list type
            if (!editorState.TryGetValue(stateKey, out var listTypeObj))
            {
                listTypeObj = box.Subtitle?.ToLower() switch
                {
                    "numbered" => 1,
                    "checkbox" => 2,
                    _ => 0 // bullet
                };
                editorState[stateKey] = listTypeObj;
            }
            var listType = (int)listTypeObj;
            
            // Compact list type selector
            string[] listTypes = { "Bullet", "Numbered", "Checkbox" };
            ImGui.Text("Style:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100 * scale);
            if (ImGui.Combo($"##listType", ref listType, listTypes, listTypes.Length))
            {
                editorState[stateKey] = listType;
                box.Subtitle = listTypes[listType].ToLower();
                modified = true;
            }
            
            // List items
            List<ListItem>? toRemove = null;
            
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                ImGui.PushID($"list{i}");
                
                // Checkbox for checkbox lists
                if (listType == 2) // checkbox
                {
                    var isChecked = item.IsChecked;
                    if (ImGui.Checkbox("##check", ref isChecked))
                    {
                        item.IsChecked = isChecked;
                        modified = true;
                    }
                    ImGui.SameLine();
                }
                
                // Indent indicator
                if (item.IndentLevel > 0)
                {
                    ImGui.Text(new string('>', item.IndentLevel));
                    ImGui.SameLine();
                }
                
                // Item text
                ImGui.SetNextItemWidth(width * 0.6f);
                var itemText = item.Text ?? "";
                if (ImGui.InputText("##text", ref itemText, 500))
                {
                    item.Text = itemText;
                    modified = true;
                }
                
                ImGui.SameLine();
                
                // Indent controls
                if (ImGui.Button("-") && item.IndentLevel > 0)
                {
                    item.IndentLevel--;
                    modified = true;
                }
                ImGui.SameLine();
                if (ImGui.Button("+") && item.IndentLevel < 3)
                {
                    item.IndentLevel++;
                    modified = true;
                }
                ImGui.SameLine();
                
                // Remove
                if (ImGui.Button("X"))
                {
                    toRemove ??= new List<ListItem>();
                    toRemove.Add(item);
                    modified = true;
                }
                
                ImGui.PopID();
            }
            
            // Remove marked items
            if (toRemove != null)
            {
                foreach (var item in toRemove)
                    items.Remove(item);
            }
            
            // Add button
            if (ImGui.Button("+ Add Item"))
            {
                items.Add(new ListItem { Text = "New item" });
                modified = true;
            }
            
            if (modified)
            {
                box.Content = JsonConvert.SerializeObject(items);
            }
            
            return modified;
        }
        
        private static bool DrawKeyValueEditor(ContentBox box, float width, float scale)
        {
            bool modified = false;
            var pairs = ParseKeyValuePairs(box.LeftColumn, box.RightColumn);
            
            ImGui.TextDisabled("Key-value pairs:");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Structured information in a clean two-column format");
                ImGui.Text("Examples: Height: 6'2\", Class: Black Mage, Hometown: Gridania");
                ImGui.EndTooltip();
            }
            
            // Key-value pairs
            int? indexToRemove = null;

            if (ImGui.BeginTable("##kvEditor", 3, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthFixed, 150 * scale);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 25 * scale);

                for (int i = 0; i < pairs.Count; i++)
                {
                    var pair = pairs[i];
                    ImGui.TableNextRow();
                    ImGui.PushID(i);  // Use stable index instead of random GUID
                    
                    // Key
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    var key = pair.Key ?? "";
                    if (ImGui.InputText("##key", ref key, 100))
                    {
                        pair.Key = key;
                        modified = true;
                    }
                    
                    // Value
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    var value = pair.Value ?? "";
                    if (ImGui.InputText("##value", ref value, 500))
                    {
                        pair.Value = value;
                        modified = true;
                    }
                    
                    // Remove
                    ImGui.TableNextColumn();
                    if (ImGui.Button("X"))
                    {
                        indexToRemove = i;
                        modified = true;
                    }

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }

            // Remove marked pair by index
            if (indexToRemove.HasValue)
            {
                pairs.RemoveAt(indexToRemove.Value);
            }
            
            // Add button
            if (ImGui.Button("+ Add Pair"))
            {
                pairs.Add(new KeyValuePairData { Key = "Label", Value = "Value" });
                modified = true;
            }
            
            if (modified)
            {
                box.LeftColumn = string.Join("\n", pairs.Select(p => p.Key));
                box.RightColumn = string.Join("\n", pairs.Select(p => p.Value));
            }
            
            return modified;
        }
        
        private static bool DrawQuoteEditor(ContentBox box, float width, float scale)
        {
            bool modified = false;
            
            // Quote text
            ImGui.Text("Quote:");
            var quoteText = box.QuoteText ?? "";
            if (ImGui.InputTextMultiline($"##quote", ref quoteText, 1000,
                new Vector2(width, 60 * scale)))
            {
                box.QuoteText = quoteText;
                modified = true;
            }
            
            // Attribution
            ImGui.Text("Attribution:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(width * 0.5f);
            var quoteAuthor = box.QuoteAuthor ?? "";
            if (ImGui.InputText($"##author", ref quoteAuthor, 200))
            {
                box.QuoteAuthor = quoteAuthor;
                modified = true;
            }
            
            return modified;
        }
        
        private static bool DrawProsConsEditor(ContentBox box, float width, float scale)
        {
            bool modified = false;
            
            if (ImGui.BeginTable("##proscons", 2, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("Strengths", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Weaknesses", ImGuiTableColumnFlags.WidthStretch);
                
                ImGui.TableHeadersRow();
                ImGui.TableNextRow();
                
                // Strengths
                ImGui.TableNextColumn();
                var leftColumn = box.LeftColumn ?? "";
                if (ImGui.InputTextMultiline($"##pros", ref leftColumn, 1000,
                    new Vector2(-1, 100 * scale)))
                {
                    box.LeftColumn = leftColumn;
                    modified = true;
                }
                
                // Weaknesses
                ImGui.TableNextColumn();
                var rightColumn = box.RightColumn ?? "";
                if (ImGui.InputTextMultiline($"##cons", ref rightColumn, 1000,
                    new Vector2(-1, 100 * scale)))
                {
                    box.RightColumn = rightColumn;
                    modified = true;
                }
                
                ImGui.EndTable();
            }
            
            ImGui.TextDisabled("Enter one per line");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("List your character's positive and negative traits");
                ImGui.Text("Great for showing character balance and depth");
                ImGui.EndTooltip();
            }
            
            return modified;
        }
        
        private static bool DrawTaggedEditor(ContentBox box, float width, float scale)
        {
            bool modified = false;
            var categories = ParseTagCategories(box.TaggedData);
            
            ImGui.TextDisabled("Organize tags by categories:");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Group related tags under category headers");
                ImGui.Text("Example: 'Combat Skills' → Swordsmanship, Fire Magic, Defensive Stance");
                ImGui.EndTooltip();
            }
            
            // Categories
            List<TagCategory>? toRemove = null;
            
            foreach (var category in categories)
            {
                ImGui.PushID(category.Id);
                ImGui.Separator();
                
                // Category name
                ImGui.Text("Category:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(150 * scale);
                var categoryName = category.Name ?? "";
                if (ImGui.InputText("##catname", ref categoryName, 100))
                {
                    category.Name = categoryName;
                    modified = true;
                }
                
                ImGui.SameLine();
                if (ImGui.Button("Remove"))
                {
                    toRemove ??= new List<TagCategory>();
                    toRemove.Add(category);
                    modified = true;
                }
                
                // Tags input
                ImGui.Text("Tags:");
                ImGui.SameLine();
                var tagsString = string.Join(", ", category.Tags);
                ImGui.SetNextItemWidth(width - 60 * scale);
                if (ImGui.InputText($"##tags", ref tagsString, 500))
                {
                    category.Tags = tagsString.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
                    modified = true;
                }
                
                ImGui.PopID();
            }
            
            // Remove marked categories
            if (toRemove != null)
            {
                foreach (var cat in toRemove)
                    categories.Remove(cat);
            }
            
            // Add category button
            ImGui.Spacing();
            if (ImGui.Button("+ Add Category"))
            {
                categories.Add(new TagCategory { Name = "New Category" });
                modified = true;
            }
            
            if (modified)
            {
                box.TaggedData = JsonConvert.SerializeObject(categories);
            }
            
            return modified;
        }
        
        private static bool DrawLikesDislikesEditor(ContentBox box, float width, float scale)
        {
            bool modified = false;
            
            // Likes editor
            ImGui.Text("Likes:");
            var likes = box.Likes ?? "";
            if (ImGui.InputTextMultiline($"##likes{box.GetHashCode()}", ref likes, 1000,
                new Vector2(width, 60 * scale)))
            {
                box.Likes = likes;
                modified = true;
            }
            
            ImGui.Spacing();
            
            // Dislikes editor
            ImGui.Text("Dislikes:");
            var dislikes = box.Dislikes ?? "";
            if (ImGui.InputTextMultiline($"##dislikes{box.GetHashCode()}", ref dislikes, 1000,
                new Vector2(width, 60 * scale)))
            {
                box.Dislikes = dislikes;
                modified = true;
            }
            
            ImGui.TextDisabled("Enter one per line or separate with commas");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("List things your character enjoys and dislikes");
                ImGui.Text("Helps others understand your character's preferences");
                ImGui.EndTooltip();
            }
            
            return modified;
        }
        
        private static bool DrawStandardEditor(ContentBox box, float width, float scale)
        {
            bool modified = false;
            
            ImGui.Text("Content:");
            var content = box.Content ?? "";
            if (ImGui.InputTextMultiline($"##content", ref content, 5000,
                new Vector2(width, 150 * scale)))
            {
                box.Content = content;
                modified = true;
            }
            
            return modified;
        }
        
        // Helper methods
        private static string GetDefaultSubtitleForLayout(ContentBoxLayoutType layout)
        {
            return layout switch
            {
                ContentBoxLayoutType.Timeline => "Character history and important events",
                ContentBoxLayoutType.Grid => "Inventory, skills, or abilities",
                ContentBoxLayoutType.List => "Traits, skills, or quick facts",
                ContentBoxLayoutType.KeyValue => "Detailed information",
                ContentBoxLayoutType.Quote => "A memorable saying",
                ContentBoxLayoutType.ProsCons => "Strengths and weaknesses",
                ContentBoxLayoutType.Tagged => "Categorized tags",
                ContentBoxLayoutType.LikesDislikes => "Personal preferences",
                ContentBoxLayoutType.Connections => "Character relationships and connections",
                _ => "Description"
            };
        }
        
        // Parsing methods (same as in ContentBoxRenderer)
        private static List<TimelineEntry> ParseTimelineEntries(string data)
        {
            if (string.IsNullOrEmpty(data)) return new List<TimelineEntry>();
            
            try
            {
                return JsonConvert.DeserializeObject<List<TimelineEntry>>(data) ?? new List<TimelineEntry>();
            }
            catch
            {
                var entries = new List<TimelineEntry>();
                foreach (var line in data.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|', 2);
                    if (parts.Length == 2)
                    {
                        entries.Add(new TimelineEntry { Date = parts[0].Trim(), Event = parts[1].Trim() });
                    }
                }
                return entries;
            }
        }
        
        private static List<ListItem> ParseListItems(string content)
        {
            if (string.IsNullOrEmpty(content)) return new List<ListItem>();
            
            try
            {
                return JsonConvert.DeserializeObject<List<ListItem>>(content) ?? new List<ListItem>();
            }
            catch
            {
                var items = new List<ListItem>();
                foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var text = line;
                    bool isChecked = false;
                    
                    if (line.StartsWith("[x]") || line.StartsWith("[X]"))
                    {
                        isChecked = true;
                        text = line.Substring(3).Trim();
                    }
                    else if (line.StartsWith("[ ]"))
                    {
                        text = line.Substring(3).Trim();
                    }
                    
                    items.Add(new ListItem { Text = text, IsChecked = isChecked });
                }
                return items;
            }
        }
        
        private static List<KeyValuePairData> ParseKeyValuePairs(string? keys, string? values)
        {
            var pairs = new List<KeyValuePairData>();

            if (string.IsNullOrEmpty(keys) || string.IsNullOrEmpty(values))
                return pairs;

            var keyArray = keys.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var valueArray = values.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < Math.Min(keyArray.Length, valueArray.Length); i++)
            {
                pairs.Add(new KeyValuePairData { Key = keyArray[i], Value = valueArray[i] });
            }

            return pairs;
        }
        
        private static List<TagCategory> ParseTagCategories(string data)
        {
            if (string.IsNullOrEmpty(data)) return new List<TagCategory>();
            
            try
            {
                return JsonConvert.DeserializeObject<List<TagCategory>>(data) ?? new List<TagCategory>();
            }
            catch
            {
                var categories = new List<TagCategory>();
                var lines = data.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                TagCategory? currentCategory = null;
                
                foreach (var line in lines)
                {
                    if (line.EndsWith(":"))
                    {
                        if (currentCategory != null) categories.Add(currentCategory);
                        currentCategory = new TagCategory { Name = line.TrimEnd(':') };
                    }
                    else if (currentCategory != null)
                    {
                        currentCategory.Tags.AddRange(line.Split(',').Select(t => t.Trim()));
                    }
                }
                
                if (currentCategory != null) categories.Add(currentCategory);
                return categories;
            }
        }
        
        private static List<GridItem> ParseGridItems(string content)
        {
            if (string.IsNullOrEmpty(content)) return new List<GridItem>();

            try
            {
                return JsonConvert.DeserializeObject<List<GridItem>>(content) ?? new List<GridItem>();
            }
            catch
            {
                var items = new List<GridItem>();
                foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(' ', 2);
                    if (parts.Length == 2)
                    {
                        items.Add(new GridItem { Icon = parts[0], Text = parts[1] });
                    }
                    else
                    {
                        items.Add(new GridItem { Icon = "", Text = line });
                    }
                }
                return items;
            }
        }

        private static bool DrawConnectionsEditor(ContentBox box, float width, float scale)
        {
            bool modified = false;
            var connections = ParseConnections(box.Content);

            ImGui.TextColored(new Vector4(0.7f, 0.75f, 0.8f, 1f), "Add connections to your characters or note relationships to others:");

            List<Connection>? toRemove = null;

            for (int connIdx = 0; connIdx < connections.Count; connIdx++)
            {
                var connection = connections[connIdx];
                ImGui.PushID(connection.Id);

                ImGui.BeginGroup();
                ImGui.Dummy(new Vector2(0, 4 * scale)); // Top padding

                // Header row: Connection # and Remove button
                ImGui.TextColored(new Vector4(0.5f, 0.7f, 0.9f, 1f), $"Connection {connIdx + 1}");
                ImGui.SameLine(width - 25 * scale);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.2f, 0.2f, 0.6f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.3f, 0.3f, 0.8f));
                if (ImGui.SmallButton("X"))
                {
                    toRemove ??= new List<Connection>();
                    toRemove.Add(connection);
                    modified = true;
                }
                ImGui.PopStyleColor(2);

                ImGui.Spacing();

                // Linkable toggle - first so user decides the type before entering name
                var isOwn = connection.IsOwnCharacter;
                if (ImGui.Checkbox("Link to my CS+ character", ref isOwn))
                {
                    connection.IsOwnCharacter = isOwn;
                    if (isOwn && AvailableCharacterNames.Count > 0)
                    {
                        var firstChar = AvailableCharacterNames[0];
                        connection.LinkedCharacterName = firstChar;
                        connection.Name = firstChar;
                        // Also store the in-game name for server lookups
                        if (CharacterInGameNames.TryGetValue(firstChar, out var inGameName))
                        {
                            connection.LinkedCharacterInGameName = inGameName;
                        }
                    }
                    else
                    {
                        connection.LinkedCharacterName = null;
                        connection.LinkedCharacterInGameName = null;
                    }
                    modified = true;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("When enabled, clicking this connection in the profile viewer");
                    ImGui.Text("will open that character's profile.");
                    ImGui.EndTooltip();
                }

                ImGui.Spacing();

                // Character name field
                ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.9f, 1f), "Character:");
                ImGui.SameLine(80 * scale);

                if (isOwn && AvailableCharacterNames.Count > 0)
                {
                    // Dropdown of own characters
                    var charIndex = AvailableCharacterNames.IndexOf(connection.LinkedCharacterName ?? "");
                    if (charIndex < 0) charIndex = 0;

                    ImGui.SetNextItemWidth(width - 85 * scale);
                    if (ImGui.Combo("##charSelect", ref charIndex, AvailableCharacterNames.ToArray(), AvailableCharacterNames.Count))
                    {
                        var selectedName = AvailableCharacterNames[charIndex];
                        connection.LinkedCharacterName = selectedName;
                        connection.Name = selectedName;
                        // Also store the in-game name for server lookups
                        if (CharacterInGameNames.TryGetValue(selectedName, out var inGameName))
                        {
                            connection.LinkedCharacterInGameName = inGameName;
                        }
                        modified = true;
                    }
                }
                else
                {
                    // Text field for external character
                    ImGui.SetNextItemWidth(width - 85 * scale);
                    var name = connection.Name ?? "";
                    if (ImGui.InputTextWithHint("##name", "Enter character name...", ref name, 100))
                    {
                        connection.Name = name;
                        modified = true;
                    }
                }

                ImGui.Spacing();

                // Relationship type
                ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.9f, 1f), "Relation:");
                ImGui.SameLine(80 * scale);
                ImGui.SetNextItemWidth(width - 85 * scale);

                var currentTypeIndex = Array.IndexOf(RelationshipTypes, connection.RelationshipType);
                if (currentTypeIndex < 0) currentTypeIndex = RelationshipTypes.Length - 1; // Default to "Other"

                if (ImGui.Combo("##relType", ref currentTypeIndex, RelationshipTypes, RelationshipTypes.Length))
                {
                    connection.RelationshipType = RelationshipTypes[currentTypeIndex];
                    modified = true;
                }

                // Custom label field when "Other" is selected
                if (connection.RelationshipType == "Other")
                {
                    ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.9f, 1f), "Custom:");
                    ImGui.SameLine(80 * scale);
                    ImGui.SetNextItemWidth(width - 85 * scale);
                    var customType = connection.CustomRelationshipType ?? "";
                    if (ImGui.InputTextWithHint("##customType", "Enter custom relationship...", ref customType, 50))
                    {
                        connection.CustomRelationshipType = customType;
                        modified = true;
                    }
                }

                ImGui.Dummy(new Vector2(0, 4 * scale)); // Bottom padding
                ImGui.EndGroup();

                // Simple separator line between connections
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.PopID();
            }

            // Remove marked connections
            if (toRemove != null)
            {
                foreach (var conn in toRemove)
                    connections.Remove(conn);
            }

            // Add button
            ImGui.Spacing();
            if (ImGui.Button("+ Add Connection", new Vector2(width, 0)))
            {
                connections.Add(new Connection
                {
                    Name = "",
                    RelationshipType = "Friend",
                    IsOwnCharacter = false
                });
                modified = true;
            }

            if (modified)
            {
                box.Content = JsonConvert.SerializeObject(connections);
            }

            return modified;
        }

        private static List<Connection> ParseConnections(string content)
        {
            if (string.IsNullOrEmpty(content)) return new List<Connection>();

            try
            {
                return JsonConvert.DeserializeObject<List<Connection>>(content) ?? new List<Connection>();
            }
            catch
            {
                return new List<Connection>();
            }
        }

        /// <summary>
        /// Checks if this is a special sidebar box that uses RPProfile fields instead of ContentBox.Content
        /// </summary>
        private static bool IsSpecialSidebarBox(string? title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            // Only Quick Info is truly read-only (displays Age, Race, Gender, Orientation from Basic Info)
            // Key Traits and Additional Details are now directly editable
            return title == "Quick Info";
        }

        /// <summary>
        /// Draws informational content for special sidebar boxes that are edited elsewhere
        /// </summary>
        private static void DrawSpecialSidebarBoxInfo(string? title, float width, float scale)
        {
            // Info box styling
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.18f, 0.25f, 0.9f));

            // Calculate appropriate height based on content
            float infoBoxHeight = title == "Key Traits" ? 110 * scale : 150 * scale;
            ImGui.BeginChild($"##infobox_{title}", new Vector2(width, infoBoxHeight), true, ImGuiWindowFlags.None);
            ImGui.Spacing();

            // Icon and header
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.7f, 1.0f, 1.0f));
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text("\uf05a"); // Info circle icon
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.Text("Linked to Basic Info Fields");
            ImGui.PopStyleColor();

            ImGui.Spacing();

            // Explanation based on which box this is
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
            switch (title)
            {
                case "Quick Info":
                    ImGui.TextWrapped("This section automatically displays the following fields from the Basic Info section above:");
                    ImGui.Spacing();
                    ImGui.BulletText("Age");
                    ImGui.BulletText("Race");
                    ImGui.BulletText("Gender");
                    ImGui.BulletText("Orientation");
                    break;

                case "Additional Details":
                    ImGui.TextWrapped("This section automatically displays the following fields from the Basic Info section above:");
                    ImGui.Spacing();
                    ImGui.BulletText("Relationship");
                    ImGui.BulletText("Occupation");
                    break;

                case "Key Traits":
                    ImGui.TextWrapped("This section displays tags from your RP Profile.");
                    ImGui.Spacing();
                    ImGui.TextDisabled("Edit your character's Tags in the RP Profile section to populate this box.");
                    break;
            }
            ImGui.PopStyleColor();

            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
            ImGui.TextWrapped("Scroll up to edit these fields in the Basic Info section, or remove this box if you don't want it displayed.");
            ImGui.PopStyleColor();

            ImGui.Spacing();
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }
    }
}