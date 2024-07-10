using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace UdonPacker.Example{
public class Base : MonoBehaviour
{
  int baseV=1;
  public  virtual void Clear(){
    Debug.Log("Base Clear");
  }
}
}
