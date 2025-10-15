using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;

namespace CharacterSelectPlugin.Effects
{
    public class FogSequenceEffect : IDisposable
    {
        private List<string> fogTexturePaths = new();
        private float animationTime = 0f;
        private const float AnimationSpeed = 0.03f; // Very slow continuous animation
        private bool isInitialized = false;
        private Vector2 effectArea = Vector2.Zero;
        private Plugin plugin;
        
        public FogSequenceEffect(Plugin plugin)
        {
            this.plugin = plugin;
            LoadFogTextures();
        }
        
        private void LoadFogTextures()
        {
            try
            {
                var pluginDir = Plugin.PluginInterface?.AssemblyLocation.DirectoryName;
                if (pluginDir == null) 
                {
                    Plugin.Log?.Warning("Plugin directory is null");
                    return;
                }
                
                var halloweenDir = Path.Combine(pluginDir, "Assets", "Halloween");
                Plugin.Log?.Info($"Looking for fog textures in: {halloweenDir}");
                
                if (!Directory.Exists(halloweenDir)) 
                {
                    Plugin.Log?.Warning($"Halloween directory does not exist: {halloweenDir}");
                    return;
                }
                
                // Load every 6th frame (60 frames total) for smoother animation
                for (int i = 1; i <= 360; i += 6)
                {
                    var fileName = $"fog_full_{i:D4}.png";
                    var filePath = Path.Combine(halloweenDir, fileName);
                    
                    if (File.Exists(filePath))
                    {
                        fogTexturePaths.Add(filePath);
                        Plugin.Log?.Debug($"Found fog texture: {fileName}");
                    }
                    else
                    {
                        Plugin.Log?.Warning($"Fog texture not found: {filePath}");
                    }
                }
                
                isInitialized = fogTexturePaths.Count > 0;
                Plugin.Log?.Info($"Found {fogTexturePaths.Count} fog textures out of 60 expected");
                
                if (fogTexturePaths.Count == 0)
                {
                    Plugin.Log?.Warning($"No fog textures found! Looking for files like: fog_full_0001.png in {halloweenDir}");
                    
                    // Check what files actually exist
                    if (Directory.Exists(halloweenDir))
                    {
                        var files = Directory.GetFiles(halloweenDir, "*.png");
                        Plugin.Log?.Info($"PNG files found in directory: {files.Length}");
                        foreach (var file in files.Take(5)) // Show first 5 files
                        {
                            Plugin.Log?.Info($"Found file: {Path.GetFileName(file)}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error($"Failed to load fog textures: {ex}");
            }
        }
        
        public void Update(float deltaTime)
        {
            if (!isInitialized || fogTexturePaths.Count == 0) return;
            
            animationTime += deltaTime;
        }
        
        public void SetEffectArea(Vector2 area)
        {
            effectArea = area;
        }
        
        public void Draw()
        {
            if (!isInitialized || fogTexturePaths.Count == 0 || effectArea.X <= 0 || effectArea.Y <= 0) 
            {
                return;
            }
            
            var drawList = ImGui.GetWindowDrawList();
            var windowPos = ImGui.GetWindowPos();
            
            // Calculate current position in animation cycle
            float cycleProgress = (animationTime * AnimationSpeed) % 1f;
            float frameFloat = cycleProgress * fogTexturePaths.Count;
            int currentFrameIndex = (int)frameFloat % fogTexturePaths.Count;
            int nextFrameIndex = (currentFrameIndex + 1) % fogTexturePaths.Count;
            float blendFactor = frameFloat - (int)frameFloat;
            
            // Get both textures
            var currentTexture = Plugin.TextureProvider.GetFromFile(fogTexturePaths[currentFrameIndex]).GetWrapOrDefault();
            var nextTexture = Plugin.TextureProvider.GetFromFile(fogTexturePaths[nextFrameIndex]).GetWrapOrDefault();
            
            // Always draw current frame with reducing opacity
            if (currentTexture != null)
            {
                float currentAlpha = 1.0f - blendFactor;
                var currentFogColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, currentAlpha));
                
                drawList.AddImage(
                    (ImTextureID)currentTexture.Handle,
                    windowPos,
                    windowPos + effectArea,
                    Vector2.Zero,
                    Vector2.One,
                    currentFogColor
                );
            }
            
            // Always draw next frame with increasing opacity
            if (nextTexture != null)
            {
                float nextAlpha = blendFactor;
                var nextFogColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, nextAlpha));
                
                drawList.AddImage(
                    (ImTextureID)nextTexture.Handle,
                    windowPos,
                    windowPos + effectArea,
                    Vector2.Zero,
                    Vector2.One,
                    nextFogColor
                );
            }
        }
        
        public void Reset()
        {
            animationTime = 0f;
        }
        
        public void Dispose()
        {
            // File paths don't need disposal, they're managed by TextureProvider
            fogTexturePaths.Clear();
        }
    }
}