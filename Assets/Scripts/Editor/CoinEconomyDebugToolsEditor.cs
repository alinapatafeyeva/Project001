using Project001.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Project001.EditorTools
{
    /// <summary>
    /// Makes CoinEconomyDebugTools easy for a non-programmer to use: shows
    /// "Current Coin Balance" as a plain, non-editable label (not a normal-
    /// looking editable int field, so nobody mistakes it for something they
    /// can type into) plus one big button per debug action, in addition to
    /// the same actions already available via right-click > (their
    /// [ContextMenu] name).
    ///
    /// Editor-only by construction (this whole class derives from
    /// UnityEditor.Editor, which only ever exists in the Editor), matching
    /// CoinEconomyDebugTools's own #if UNITY_EDITOR guard — neither can end
    /// up in a Player build.
    /// </summary>
    [CustomEditor(typeof(CoinEconomyDebugTools))]
    public class CoinEconomyDebugToolsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var debugTools = (CoinEconomyDebugTools)target;

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("coinWalletService"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("levelRewardController"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();

            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Current Coin Balance", debugTools.CurrentCoinBalance.ToString(), EditorStyles.boldLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see the current coin balance here.", MessageType.Info);
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Log Current Coin Balance"))
                    debugTools.LogCurrentCoinBalance();

                if (GUILayout.Button("Reset Coin Balance To Zero"))
                    debugTools.ResetCoinBalanceToZero();

                if (GUILayout.Button("Simulate Rewarded Ad SUCCESS (current level)"))
                    debugTools.SimulateRewardedAdSuccess();
            }
        }
    }
}
