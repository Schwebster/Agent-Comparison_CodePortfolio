using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AgentComparison.Goap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// Runtime scripts live in Unity's predefined Assembly-CSharp. Reflection keeps the
// test assembly separate without moving the entire simulation to an asmdef.
public sealed class GoapExecutionTests
{
    private Component controller;
    private Component tasks;
    private readonly GoapAction foodAction = GoapSurvivalDomain.CreateActions().Single(a => a.Name == "GatherFood");
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return SceneManager.LoadSceneAsync("GoapAgent");
        yield return null;
        var controllerType = Type.GetType("GoapAgentController, Assembly-CSharp", true);
        var controllers = UnityEngine.Object.FindObjectsByType(controllerType, FindObjectsSortMode.None);
        Assert.That(controllers.Length, Is.GreaterThan(0), "The existing GOAP scene must spawn its configured NPC prefab.");
        foreach (Behaviour agent in controllers)
            agent.enabled = false;
        controller = (Component)controllers[0];
        tasks = controller.GetComponent(Type.GetType("NpcTaskController, Assembly-CSharp", true));
        Call(tasks, "CancelCurrentTask");
        Set("pendingAction", foodAction);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        yield return SceneManager.LoadSceneAsync("StartScene");
    }

    [Test]
    public void AsynchronousTaskFailureBlocksActionAndReleasesAssignment()
    {
        Call(tasks, "FailCurrentTask", "Injected execution failure for GOAP recovery test.");
        Call(controller, "ObserveActionOutcome");
        var blocked = (Dictionary<string, float>)Get("blockedUntil");
        Assert.That(blocked.ContainsKey(foodAction.Name), Is.True);
        Assert.That(Get("pendingAction"), Is.Null);
        Assert.That(tasks.GetType().GetProperty("HasAssignedWorkRole").GetValue(tasks), Is.False);
        float until = blocked[foodAction.Name];
        Call(controller, "ObserveActionOutcome");
        Assert.That(blocked[foodAction.Name], Is.EqualTo(until), "A terminal result must be consumed only once.");
    }

    [Test]
    public void CompletedTaskWithoutDeliveryDoesNotSatisfyFoodEffect()
    {
        Call(tasks, "CompleteCurrentTask");
        Call(controller, "ObserveActionOutcome");
        Assert.That(((Dictionary<string, float>)Get("blockedUntil")).ContainsKey(foodAction.Name), Is.True);
    }

    [Test]
    public void AcceptedTaskIsNotMistakenForCompletedExecution()
    {
        Type taskType = Type.GetType("AgentTaskType, Assembly-CSharp", true);
        Call(tasks, "TryStartTask", Enum.Parse(taskType, "GatherResource"));
        Call(controller, "ObserveActionOutcome");
        Assert.That(Get("pendingAction"), Is.SameAs(foodAction));
        Assert.That(((Dictionary<string, float>)Get("blockedUntil")).Count, Is.Zero);
    }

    [Test]
    public void ScheduleInterruptionDoesNotBlacklistValidWork()
    {
        Call(controller, "InterruptPlan", "Sleep window");
        Assert.That(Get("pendingAction"), Is.Null);
        Assert.That(((Dictionary<string, float>)Get("blockedUntil")).Count, Is.Zero);
    }

    private object Get(string field) => controller.GetType().GetField(field, PrivateInstance).GetValue(controller);
    private void Set(string field, object value) => controller.GetType().GetField(field, PrivateInstance).SetValue(controller, value);
    private static object Call(Component target, string method, params object[] args) => target.GetType()
        .GetMethod(method, PrivateInstance | BindingFlags.Public).Invoke(target, args);
}
