using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// One Match ID-to-prefab mapping entry. A plain serializable class
    /// rather than a struct so Unity's Inspector list drawer can add and
    /// reorder entries normally.
    /// </summary>
    [Serializable]
    public sealed class CharacterEntry
    {
        [SerializeField]
        private int matchId;

        [SerializeField]
        private GameObject prefab;

        public int MatchId => matchId;

        public GameObject Prefab => prefab;
    }

    /// <summary>
    /// Maps a Match ID (1-20, the permanent id from Assets/Art/ColorPalette.md)
    /// to the Character_XX prefab that represents it, entirely through
    /// Inspector-configured entries - no Resources.Load, no Addressables, no
    /// species/color name baked into code. Each prefab already carries its
    /// own Character_XX.mat baked onto its body renderer (built by
    /// CharacterAssetBuilder), so resolving the prefab is the only lookup
    /// gameplay ever needs - there is no separate material resolution step.
    /// Mirrors the database this replaces (MonsterMaterialDatabase)
    /// structurally, for the same reason: this is the seam
    /// CollectorQueueBoard resolves a collector's visual through.
    /// </summary>
    public class CharacterDatabase : MonoBehaviour
    {
        [SerializeField, Tooltip("One entry per supported Match ID (1-20). Configured entirely through the Inspector.")]
        private List<CharacterEntry> characters = new List<CharacterEntry>();

        /// <summary>
        /// Resolves the prefab configured for the given Match ID. Never
        /// returns null: callers never need to null-check or null-propagate
        /// the result. If no entry matches (or its prefab reference is
        /// unassigned), logs an error and falls back to Match ID 1; if even
        /// that fallback is unconfigured, logs a second error and returns
        /// null (a collector shows no Visual rather than the caller
        /// crashing on a null CharacterDatabase result).
        /// </summary>
        public GameObject GetPrefab(int matchId)
        {
            GameObject prefab = FindConfiguredPrefab(matchId);
            if (prefab != null)
                return prefab;

            Debug.LogError($"CharacterDatabase: no prefab configured for Match ID '{matchId}'; falling back to Match ID 1.", this);

            prefab = FindConfiguredPrefab(1);
            if (prefab != null)
                return prefab;

            Debug.LogError("CharacterDatabase: fallback Match ID 1 has no prefab configured either; returning null.", this);
            return null;
        }

        private GameObject FindConfiguredPrefab(int matchId)
        {
            foreach (CharacterEntry entry in characters)
            {
                if (entry.MatchId == matchId && entry.Prefab != null)
                    return entry.Prefab;
            }

            return null;
        }
    }
}
