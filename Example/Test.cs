using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UdonPacker;

namespace UdonPacker.Example{
public class Test : MonoBehaviour
{
    int b=-1;
    [SerializeField,PackingAttribute]Base baseTarget;    // Base
    //InterfaceToClassAttributeは再帰的に適用されます
    [InterfaceToClassAttribute("Base","Extend")][SerializeField,PackingAttribute]Base target;   
    void Start()
    {
        Mathf.Abs(b);
        baseTarget.Clear();
        target.Clear();
        
    }

    // Update is called once per frame


    void Clear(){
        
    }
}
}