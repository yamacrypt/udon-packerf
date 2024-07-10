using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UdonPacker;

namespace UdonPacker.Example
{
    public class PackedTest : MonoBehaviour
    {
        int b = -1;
        int baseTarget_baseV = 1;
        public virtual void baseTarget_Clear()
        {
            Debug.Log("Base Clear");
        }

        int target_extendV = 1;
        public void target_Clear()
        {
            Debug.Log("Extend Clear");
        }

        void Start()
        {
            Mathf.Abs(b);
            baseTarget_Clear();
            target_Clear();
        }

        // Update is called once per frame
        void Clear()
        {
        }
    }
}