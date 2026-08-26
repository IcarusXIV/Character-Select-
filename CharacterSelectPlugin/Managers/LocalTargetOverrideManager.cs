using System.Collections.Concurrent;
using System.Numerics;

namespace CharacterSelectPlugin.Managers
{
    public sealed class LocalNameOverride
    {
        public string DisplayName { get; init; } = string.Empty;
        public Vector3 NameplateColor { get; init; }
    }

    public sealed class LocalTargetOverrideManager
    {
        private readonly ConcurrentDictionary<ulong, LocalNameOverride> _overrides = new();

        public void Register(ulong gameObjectId, Character character)
        {
            if (gameObjectId == 0 || gameObjectId == 0xE0000000) return;
            var displayName = !string.IsNullOrWhiteSpace(character?.Alias)
                ? character.Alias!
                : character?.Name;
            if (string.IsNullOrWhiteSpace(displayName)) return;
            _overrides[gameObjectId] = new LocalNameOverride
            {
                DisplayName = displayName!,
                NameplateColor = character!.NameplateColor,
            };
        }

        public void Unregister(ulong gameObjectId)
        {
            _overrides.TryRemove(gameObjectId, out _);
        }

        public void Clear()
        {
            _overrides.Clear();
        }

        public bool TryGet(ulong gameObjectId, out LocalNameOverride overrideInfo)
        {
            return _overrides.TryGetValue(gameObjectId, out overrideInfo!);
        }

        public int Count => _overrides.Count;
    }
}
