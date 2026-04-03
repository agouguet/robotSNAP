using UnityEngine;
using System.Collections;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using UnityEditor;

[AttributeUsage(AttributeTargets.Field)]
public class OptionsAttribute : PropertyAttribute
{
    public string OptionsName { get; set; }
 
    public OptionsAttribute(string OptionsName)
    {
        this.OptionsName = OptionsName;
    }
}