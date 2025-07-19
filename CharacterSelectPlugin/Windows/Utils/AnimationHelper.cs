using System;
using System.Collections.Generic;
using ImGuiNET;

namespace CharacterSelectPlugin.Windows.Utils
{
    public static class AnimationHelper
    {
        private static Dictionary<string, float> AnimationStates = new();
        private static Dictionary<string, float> AnimationTargets = new();

        // Smooth interpolation between current and target values
        public static float SmoothStep(string key, float target, float speed = 8f)
        {
            if (!AnimationStates.ContainsKey(key))
                AnimationStates[key] = 0f;

            if (!AnimationTargets.ContainsKey(key))
                AnimationTargets[key] = target;

            // Only update if target changed
            if (Math.Abs(AnimationTargets[key] - target) > 0.001f)
                AnimationTargets[key] = target;

            float current = AnimationStates[key];
            float deltaTime = ImGui.GetIO().DeltaTime;

            // Smooth interpolation
            current = current + (AnimationTargets[key] - current) * deltaTime * speed;

            // Clamp to avoid overshooting
            current = Math.Clamp(current, 0f, 1f);

            AnimationStates[key] = current;
            return current;
        }

        // Ease-in-out animation curve
        public static float EaseInOut(float t)
        {
            return t * t * (3f - 2f * t);
        }

        // Ease-out animation curve (starts fast, ends slow)
        public static float EaseOut(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }

        // Ease-in animation curve (starts slow, ends fast)
        public static float EaseIn(float t)
        {
            return t * t;
        }

        // Bounce animation effect
        public static float Bounce(float t)
        {
            if (t < 0.5f)
                return 2f * t * t;
            else
                return 1f - 2f * (1f - t) * (1f - t);
        }

        // Pulse animation (0 to 1 and back)
        public static float Pulse(float frequency = 2f)
        {
            return (float)(Math.Sin(ImGui.GetTime() * frequency) + 1f) / 2f;
        }

        // Clear animation state for a specific key
        public static void ClearAnimation(string key)
        {
            AnimationStates.Remove(key);
            AnimationTargets.Remove(key);
        }

        // Clear all animation states
        public static void ClearAllAnimations()
        {
            AnimationStates.Clear();
            AnimationTargets.Clear();
        }

        // Get current animation value without updating
        public static float GetAnimationValue(string key)
        {
            return AnimationStates.TryGetValue(key, out float value) ? value : 0f;
        }

        // Set animation value directly
        public static void SetAnimationValue(string key, float value)
        {
            AnimationStates[key] = Math.Clamp(value, 0f, 1f);
        }

        // Animate a float value with custom easing
        public static float AnimateFloat(string key, float target, float speed = 8f, Func<float, float>? easing = null)
        {
            float t = SmoothStep(key, target, speed);
            return easing != null ? easing(t) : t;
        }

        // Create a hover animation for UI elements
        public static float HoverAnimation(string key, bool isHovered, float speed = 10f)
        {
            return SmoothStep(key, isHovered ? 1f : 0f, speed);
        }

        // Create a loading spinner animation
        public static float SpinnerAnimation(float speed = 4f)
        {
            return (float)(ImGui.GetTime() * speed) % (2f * (float)Math.PI);
        }
    }
}
