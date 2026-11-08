/*
===============================================================================
Script:
CharacterControlRegistry

Purpose:
Tracks whether characters are controlled by the player or an autonomous agent.

Folder:
Assets/Scripts/Input/

Scene Hierarchy:
Used by player and autonomous control systems

Attach To:
None

Required Children:
None

Dependencies:
- Character
- CharacterControlMode

Automatically Resolved:
None

Inspector References:
None
===============================================================================
*/

using System.Collections.Generic;
using UnityEngine;

public static class CharacterControlRegistry
{
    private static readonly Dictionary<Character, CharacterControlMode> characterModes = new Dictionary<Character, CharacterControlMode>();
    private static readonly List<Character> staleCharacters = new List<Character>();
    private static Character playerControlledCharacter;
    private static int lastPrunedFrame = int.MinValue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnSubsystemRegistration()
    {
        Reset();
    }

    public static void Reset()
    {
        characterModes.Clear();
        staleCharacters.Clear();
        playerControlledCharacter = null;
        lastPrunedFrame = int.MinValue;
    }

    public static void Unregister(Character character)
    {
        if (character == null)
        {
            PruneDestroyedCharacters();
            return;
        }

        characterModes.Remove(character);

        if (character == playerControlledCharacter)
            playerControlledCharacter = null;
    }

    public static CharacterControlMode GetControlMode(Character character)
    {
        if (character == null)
            return CharacterControlMode.None;

        PruneDestroyedCharactersOncePerFrame();

        if (characterModes.TryGetValue(character, out CharacterControlMode controlMode))
            return controlMode;

        return CharacterControlMode.None;
    }

    public static Character GetPlayerControlledCharacter()
    {
        PruneDestroyedCharactersOncePerFrame();
        return playerControlledCharacter;
    }

    public static bool IsPlayerControlled(Character character)
    {
        return character != null && GetControlMode(character) == CharacterControlMode.Player;
    }

    public static void TakePlayerControl(Character character)
    {
        if (character == null)
            return;

        PruneDestroyedCharactersOncePerFrame();

        if (playerControlledCharacter != character)
            ReleasePlayerControl(playerControlledCharacter);

        playerControlledCharacter = character;
        characterModes[character] = CharacterControlMode.Player;
    }

    public static void ReleasePlayerControl(Character character)
    {
        if (character == null)
        {
            PruneDestroyedCharacters();
            return;
        }

        if (character != playerControlledCharacter)
            return;

        characterModes.Remove(character);
        playerControlledCharacter = null;
    }

    public static bool TryTakeAutonomousControl(Character character)
    {
        if (character == null)
            return false;

        CharacterControlMode controlMode = GetControlMode(character);
        if (controlMode == CharacterControlMode.Player)
            return false;

        if (controlMode == CharacterControlMode.Autonomous)
            return true;

        characterModes[character] = CharacterControlMode.Autonomous;
        return true;
    }

    public static void ReleaseAutonomousControl(Character character)
    {
        if (character == null || GetControlMode(character) != CharacterControlMode.Autonomous)
            return;

        characterModes.Remove(character);
    }

    private static void PruneDestroyedCharacters()
    {
        staleCharacters.Clear();

        foreach (Character character in characterModes.Keys)
        {
            if (character == null)
                staleCharacters.Add(character);
        }

        foreach (Character character in staleCharacters)
            characterModes.Remove(character);

        staleCharacters.Clear();

        if (playerControlledCharacter == null)
            playerControlledCharacter = null;

        lastPrunedFrame = Time.frameCount;
    }

    private static void PruneDestroyedCharactersOncePerFrame()
    {
        if (lastPrunedFrame == Time.frameCount)
            return;

        PruneDestroyedCharacters();
    }
}
