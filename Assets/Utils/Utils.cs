using UnityEngine;
using System.Collections;
using DateTime = System.DateTime;
using Math = System.Math;
using System;
using System.Text; // include at the top
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

public class Utils
{
    public static void PrintList2D(List<List<object>> list)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < list.Count(); i++)
        {
            for (int j = 0; j < list[0].Count(); j++)
            {
                sb.Append(list[i][j]);
                sb.Append(' ');
            }
            sb.AppendLine();
        }
        Debug.Log(sb.ToString());
    }

    public static void Print2DArray<T>(T[,] matrix)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < matrix.GetLength(0); i++)
        {
            for (int j = 0; j < matrix.GetLength(1); j++)
            {
                sb.Append(matrix[i, j]);
                sb.Append(' ');
            }
            sb.AppendLine();
        }
        Debug.Log(sb.ToString());
    }

    // Méthode générique pour chercher dans les parents
    public static T FindScriptInParents<T>(Transform current) where T : Component
    {
        while (current != null)
        {
            T component = current.GetComponent<T>();
            if (component != null)
                return component;

            current = current.parent;
        }

        return null; // rien trouvé
    }
    // Surcharge avec GameObject
    public static T FindScriptInParents<T>(GameObject obj) where T : Component
    {
        return FindScriptInParents<T>(obj.transform);
    }

    // Méthode générique pour chercher récursivement dans les enfants
    public static T FindScriptInChildren<T>(Transform current) where T : Component
    {
        // Regarde sur l'objet courant
        T component = current.GetComponent<T>();
        if (component != null)
            return component;

        // Sinon, cherche récursivement dans les enfants
        foreach (Transform child in current)
        {
            T result = FindScriptInChildren<T>(child);
            if (result != null)
                return result;
        }

        return null; // rien trouvé
    }

    public static T FindScriptInChildren<T>(GameObject obj) where T : Component
    {
        return FindScriptInChildren<T>(obj.transform);
    }

    // Convertit un chemin absolu vers un chemin relatif à partir de basePath
    public static string GetRelativePath(string fullPath, string basePath)
    {
        if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            basePath += Path.DirectorySeparatorChar;

        Uri baseUri = new Uri(basePath);
        Uri fullUri = new Uri(fullPath);
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
    }
}