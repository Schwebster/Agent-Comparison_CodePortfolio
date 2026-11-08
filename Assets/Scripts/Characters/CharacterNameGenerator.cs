/*
===============================================================================
Script:
CharacterNameGenerator

Purpose:
Provides simple random character names.

Folder:
Assets/Scripts/Characters/

Scene Hierarchy:
None

Attach To:
None

Required Children:
None

Dependencies:
None

Automatically Resolved:
None

Inspector References:
None
===============================================================================
*/
using System;

public static class CharacterNameGenerator
{
    private static readonly Random Random = new Random();

    internal static readonly string[] FirstNames =
    {
        "Alma",
        "Bruno",
        "Clara",
        "Dario",
        "Elsa",
        "Finn",
        "Greta",
        "Hugo",
        "Ida",
        "Jonas",
        "Lina",
        "Marta",
        "Niko",
        "Oskar",
        "Pia",
        "Ruben"
    };

    internal static readonly string[] LastNames =
    {
        "Acker",
        "Berg",
        "Feld",
        "Hain",
        "Kern",
        "Lenz",
        "Moos",
        "Reed",
        "Stein",
        "Tal",
        "Voss",
        "Wald"
    };

    public static string GetRandomFirstName()
    {
        return FirstNames[Random.Next(FirstNames.Length)];
    }

    public static string GetRandomLastName()
    {
        return LastNames[Random.Next(LastNames.Length)];
    }
}
