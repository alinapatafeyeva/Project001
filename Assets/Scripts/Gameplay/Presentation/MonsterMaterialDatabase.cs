using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// One MonsterColor-to-Material mapping entry. A plain serializable class
    /// rather than a struct so Unity's Inspector list drawer can add and
    /// reorder entries normally.
    /// </summary>
    [Serializable]
    public sealed class MonsterMaterialEntry
    {
        [SerializeField]
        private MonsterColor color;

        [SerializeField]
        private Material material;

        public MonsterColor Color => color;

        public Material Material => material;
    }

    /// <summary>
    /// Maps MonsterColor to the Material that represents it, entirely through
    /// Inspector-configured entries - no Resources.Load, no Addressables, no
    /// path convention baked into code. Adding a new color (once a new
    /// MonsterColor value exists) only requires adding an entry here and
    /// assigning its material; no gameplay code changes. Mirrors
    /// MonsterSkinDatabase's structure exactly, for the same reason: this is
    /// the seam CollectorQueueBoard resolves a collector's material through.
    /// </summary>
    public class MonsterMaterialDatabase : MonoBehaviour
    {
        [SerializeField, Tooltip("One entry per supported MonsterColor. Configured entirely through the Inspector.")]
        private List<MonsterMaterialEntry> materials = new List<MonsterMaterialEntry>();

        /// <summary>
        /// Resolves the Material configured for the given MonsterColor. Never
        /// returns null: callers never need to null-check or null-propagate
        /// the result. If no entry matches (or its material reference is
        /// unassigned), logs an error and falls back to MonsterColor.Purple;
        /// if even that fallback is unconfigured, logs a second error and
        /// returns null (a collector shows Unity's default/missing material
        /// rather than the caller crashing on a null MonsterMaterialDatabase
        /// result).
        /// </summary>
        public Material GetMaterial(MonsterColor monsterColor)
        {
            Material material = FindConfiguredMaterial(monsterColor);
            if (material != null)
                return material;

            Debug.LogError($"MonsterMaterialDatabase: no material configured for MonsterColor '{monsterColor}'; falling back to MonsterColor.Purple.", this);

            material = FindConfiguredMaterial(MonsterColor.Purple);
            if (material != null)
                return material;

            Debug.LogError("MonsterMaterialDatabase: fallback MonsterColor.Purple has no material configured either; returning null.", this);
            return null;
        }

        private Material FindConfiguredMaterial(MonsterColor monsterColor)
        {
            foreach (MonsterMaterialEntry entry in materials)
            {
                if (entry.Color == monsterColor && entry.Material != null)
                    return entry.Material;
            }

            return null;
        }
    }
}
