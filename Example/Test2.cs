using System.Collections;
using System.Collections.Generic;
using UdonPacker;
using UnityEngine;

namespace UdonPacker.Example{
public class Test2 : MonoBehaviour
{
    [SerializeField,PackingAttribute]Test ts;
    [SerializeField,PackingAttribute]Test ts2;

    int _a=0;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
}