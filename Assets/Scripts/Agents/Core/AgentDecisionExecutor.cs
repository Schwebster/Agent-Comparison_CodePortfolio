/*
===============================================================================
Script:
AgentDecisionExecutor

Purpose:
Executes agent decisions through the shared character action interface.

Folder:
Assets/Scripts/Agents/Core/

Scene Hierarchy:
_World
- Characters
  - Character

Attach To:
Character

Required Children:
None

Dependencies:
- Character
- ICharacterActions
- CharacterActions
- AgentDecision

Automatically Resolved:
- Character on the same GameObject

Inspector References:
None
===============================================================================
*/

using UnityEngine;

public class AgentDecisionExecutor : MonoBehaviour
{
    private Character character;
    private ICharacterActions characterActions;

    private void Awake()
    {
        character = GetComponent<Character>();
        characterActions = new CharacterActions(character);

        Debug.Assert(
            character != null,
            $"Missing component: Character. Expected location: same GameObject as AgentDecisionExecutor. Script: {nameof(AgentDecisionExecutor)}. GameObject: {gameObject.name}."
        );
    }

    public void Execute(AgentDecision decision)
    {
        if (decision == null)
        {
            Debug.LogWarning("AgentDecisionExecutor received a null decision.");
            return;
        }

        if (characterActions == null)
        {
            Debug.LogWarning($"AgentDecisionExecutor cannot execute {decision.DecisionType}: character actions are missing. GameObject: {gameObject.name}.");
            return;
        }

        switch (decision.DecisionType)
        {
            case AgentDecisionType.None:
            case AgentDecisionType.Wait:
                return;

            case AgentDecisionType.MoveTo:
                characterActions.MoveTo(decision.TargetPosition);
                return;

            case AgentDecisionType.Harvest:
                ExecuteHarvest(decision);
                return;

            case AgentDecisionType.Deposit:
                ExecuteDeposit(decision);
                return;

            default:
                Debug.LogWarning($"AgentDecisionExecutor received unsupported decision type: {decision.DecisionType}. GameObject: {gameObject.name}.");
                return;
        }
    }

    private void ExecuteHarvest(AgentDecision decision)
    {
        if (decision.TargetResource == null)
        {
            Debug.LogWarning($"AgentDecisionExecutor cannot harvest: target ResourceNode is missing. GameObject: {gameObject.name}.");
            return;
        }

        characterActions.Harvest(decision.TargetResource);
    }

    private void ExecuteDeposit(AgentDecision decision)
    {
        if (decision.TargetStorage == null)
        {
            Debug.LogWarning($"AgentDecisionExecutor cannot deposit: target ResourceStorage is missing. GameObject: {gameObject.name}.");
            return;
        }

        characterActions.Deposit(decision.TargetStorage);
    }
}
