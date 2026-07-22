using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// One MonsterColor-to-MonsterSkin mapping entry. A plain serializable
    /// class rather than a struct so Unity's Inspector list drawer can add
    /// and reorder entries normally.
    /// </summary>
    [Serializable]
    public sealed class MonsterSkinEntry
    {
        [SerializeField]
        private MonsterColor color;

        [SerializeField]
        private MonsterSkin skin;

        public MonsterColor Color => color;

        public MonsterSkin Skin => skin;
    }

    /// <summary>
    /// Maps MonsterColor to the MonsterSkin (sprite set) that represents it,
    /// entirely through Inspector-configured entries — no Resources.Load, no
    /// Addressables, no path convention baked into code. Adding a new skin
    /// (once a new MonsterColor value exists) only requires adding an entry
    /// here and assigning its sprites; no gameplay code changes.
    /// </summary>
    public class MonsterSkinDatabase : MonoBehaviour
    {
        [SerializeField, Tooltip("One entry per supported MonsterColor. Configured entirely through the Inspector.")]
        private List<MonsterSkinEntry> skins = new List<MonsterSkinEntry>();

        /// <summary>
        /// Resolves the MonsterSkin configured for the given MonsterColor.
        /// Never returns null: callers never need to null-check or
        /// null-propagate the result. If no entry matches (or its skin
        /// reference is unassigned), logs an error and falls back to
        /// MonsterColor.Purple; if even that fallback is unconfigured, logs a
        /// second error and returns an empty MonsterSkin (every sprite null,
        /// so a collector simply shows nothing rather than the caller
        /// crashing on a null MonsterSkin).
        /// </summary>
        public MonsterSkin GetSkin(MonsterColor monsterColor)
        {
            MonsterSkin skin = FindConfiguredSkin(monsterColor);
            if (skin != null)
                return skin;

            Debug.LogError($"MonsterSkinDatabase: no skin configured for MonsterColor '{monsterColor}'; falling back to MonsterColor.Purple.", this);

            skin = FindConfiguredSkin(MonsterColor.Purple);
            if (skin != null)
                return skin;

            Debug.LogError("MonsterSkinDatabase: fallback MonsterColor.Purple has no skin configured either; returning an empty MonsterSkin with no sprites assigned.", this);
            return new MonsterSkin();
        }

        private MonsterSkin FindConfiguredSkin(MonsterColor monsterColor)
        {
            foreach (MonsterSkinEntry entry in skins)
            {
                if (entry.Color == monsterColor && entry.Skin != null)
                    return entry.Skin;
            }

            return null;
        }
    }
}
