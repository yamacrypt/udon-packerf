#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UdonPacker{
[CustomEditor(typeof(UdonPacker), true)]
 public class UdonPackerEditor : Editor
 {
   UdonPacker udonPacker;
    private void OnEnable()
     {    
        udonPacker = target as UdonPacker;
      }

      public override void OnInspectorGUI()
      {
         base.OnInspectorGUI();
              if (GUILayout.Button("展開"))
              {
                    udonPacker.SavePackedClassToFile();
              }
      }
 }
}
#endif