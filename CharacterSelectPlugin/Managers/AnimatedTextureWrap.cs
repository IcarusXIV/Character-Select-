using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CharacterSelectPlugin.Managers
{
    // GIF / WebP wrap; frames advance only while hovered, un-hover resets to 0
    public sealed class AnimatedTextureWrap : IDalamudTextureWrap
    {
        private readonly Image<Rgba32> image;
        private readonly IDalamudTextureWrap empty;
        private readonly IDalamudTextureWrap?[] frames;
        private readonly int[] frameDelaysMs;
        private readonly int totalFrames;
        private readonly Stopwatch frameTimer = new();
        private readonly CancellationTokenSource cts = new();
        private int currentFrame;
        private bool disposed;

        // Renderer sets this each frame.  True = advance; false = pause + reset.
        public bool IsHovered { get; set; }

        // False until the first frame decodes
        public bool IsReady => frames.Length > 0 && frames[0] != null;

        public int Width { get; }
        public int Height { get; }

        public ImTextureID Handle
        {
            get
            {
                if (disposed) return empty.Handle;

                // Static image (single-frame GIF, non-animated WebP, etc.)
                if (totalFrames <= 1)
                    return frames[0]?.Handle ?? empty.Handle;

                if (IsHovered)
                {
                    if (!frameTimer.IsRunning) frameTimer.Restart();
                    if (frameTimer.ElapsedMilliseconds >= frameDelaysMs[currentFrame])
                    {
                        currentFrame = (currentFrame + 1) % totalFrames;
                        frameTimer.Restart();
                    }
                }
                else
                {
                    // Reset to frame 0 so the next hover replays from the start
                    currentFrame = 0;
                    frameTimer.Reset();
                }

                return frames[currentFrame]?.Handle
                    ?? frames[0]?.Handle
                    ?? empty.Handle;
            }
        }

        public AnimatedTextureWrap(ITextureProvider textureProvider, string filePath)
        {
            image = Image.Load<Rgba32>(filePath);
            Width = image.Width;
            Height = image.Height;
            totalFrames = Math.Max(1, image.Frames.Count);
            frames = new IDalamudTextureWrap?[totalFrames];
            frameDelaysMs = new int[totalFrames];
            empty = textureProvider.CreateEmpty(
                RawImageSpecification.Rgba32(Width, Height),
                false, false);

            ResolveFrameDelays(filePath);

            Task.Run(() => DecodeFramesAsync(textureProvider, cts.Token));
        }

        private void ResolveFrameDelays(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            for (int i = 0; i < totalFrames; i++)
            {
                int delayMs = 100;
                try
                {
                    if (ext == ".gif")
                    {
                        // GIF FrameDelay is in 1/100ths of a second
                        var meta = image.Frames[i].Metadata.GetGifMetadata();
                        delayMs = meta.FrameDelay * 10;
                    }
                    else if (ext == ".webp")
                    {
                        var meta = image.Frames[i].Metadata.GetWebpMetadata();
                        delayMs = (int)meta.FrameDelay;
                    }
                }
                catch { /* fall through to default */ }

                // Some GIFs ship FrameDelay = 0; browsers clamp to ~100ms.  Match that.
                frameDelaysMs[i] = delayMs <= 0 ? 100 : Math.Max(20, delayMs);
            }
        }

        private async Task DecodeFramesAsync(ITextureProvider textureProvider, CancellationToken token)
        {
            int stride = Width * 4;
            byte[] pixelBuffer = new byte[stride * Height];
            for (int i = 0; i < totalFrames; i++)
            {
                if (token.IsCancellationRequested) return;
                try
                {
                    image.Frames[i].CopyPixelDataTo(pixelBuffer);

                    if (token.IsCancellationRequested) return;

                    var wrap = textureProvider.CreateFromRaw(
                        RawImageSpecification.Rgba32(Width, Height),
                        pixelBuffer,
                        $"CSPlus_AnimFrame_{i}");
                    if (token.IsCancellationRequested)
                    {
                        wrap?.Dispose();
                        return;
                    }
                    frames[i] = wrap;
                    await Task.Yield();
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Plugin.Log.Warning($"[AnimatedTextureWrap] Frame {i} decode failed: {ex.Message}");
                }
            }

            int failed = 0;
            for (int i = 0; i < totalFrames; i++)
                if (frames[i] == null) failed++;
            if (failed > 0)
                Plugin.Log.Warning($"[AnimatedTextureWrap] {failed}/{totalFrames} frames failed to decode");
            else
                Plugin.Log.Information($"[AnimatedTextureWrap] Decoded {totalFrames} frames ({Width}x{Height})");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { cts.Cancel(); } catch { }
            try { image.Dispose(); } catch { }
            for (int i = 0; i < frames.Length; i++)
            {
                try { frames[i]?.Dispose(); } catch { }
                frames[i] = null;
            }
            try { empty.Dispose(); } catch { }
            try { cts.Dispose(); } catch { }
        }
    }
}
