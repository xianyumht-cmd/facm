using FACM.Core.Desktop;

internal static class FacmSurfaceStateMachineSmoke
{
    public static void Run()
    {
        var machine = new FacmSurfaceStateMachine();
        var transitions = new List<FacmSurfaceTransition>();
        machine.Transitioned += (_, transition) => transitions.Add(transition);

        Assert(machine.Mode == FacmSurfaceMode.Orb, "surface starts as Orb");
        Assert(machine.TransitionTo(FacmSurfaceMode.ControlMatrix, "orb-left-click", true) is not null,
            "orb expands to control matrix");
        Assert(machine.TransitionTo(FacmSurfaceMode.FeatureSurface, "diagnostics-selected", true) is not null,
            "tool selection enters feature surface");
        Assert(machine.ObserveGameflow("InGame", inGame: true, champSelect: false, lobbyRestored: false) is not null,
            "in-game hides surface");
        Assert(machine.Mode == FacmSurfaceMode.HiddenInGame, "surface is hidden in game");
        Assert(machine.ObserveGameflow("Lobby", inGame: false, champSelect: false, lobbyRestored: true) is not null,
            "lobby restores orb");
        Assert(machine.Mode == FacmSurfaceMode.Orb, "lobby restore returns to orb");
        using (machine.EnterModalScope())
            Assert(machine.IsModalScopeActive, "modal scope is observable");
        Assert(!machine.IsModalScopeActive, "modal scope releases");
        Assert(transitions.Count == 4, "transition telemetry count is deterministic");
        Assert(transitions.All(item => item.CorrelationId.Length == 32), "transitions have correlation ids");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FacmSurfaceStateMachineSmoke failed: " + message);
    }
}
