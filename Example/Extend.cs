using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace UdonPacker.Example{
public class Extend : Base
{
  int extendV=1;
   public override void Clear(){
    Debug.Log("Extend Clear");
  }
}
}