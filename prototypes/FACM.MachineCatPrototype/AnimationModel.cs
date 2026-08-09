using System.Numerics;

namespace FACM.MachineCatPrototype;

internal enum PetState
{
    Idle,
    Walk,
    Run,
    Turn,
    Observe,
    Raised,
    Recover,
    Sleep
}

internal readonly record struct RigPose(
    string PrimaryAsset,
    bool PrimaryMirror,
    string? SecondaryAsset,
    bool SecondaryMirror,
    double SecondaryOpacity,
    bool UseLayeredRig,
    double RootX,
    double RootY,
    double RootRotation,
    double RootScaleX,
    double RootScaleY,
    double HeadY,
    double HeadRotation,
    double LeftArmRotation,
    double RightArmRotation,
    double LeftFootRotation,
    double RightFootRotation,
    double LeftFootY,
    double RightFootY,
    double ShadowScaleX,
    double ShadowOpacity)
{
    public static RigPose Single(
        string asset,
        double x = 0d,
        double y = 0d,
        double rotation = 0d,
        double scaleX = 1d,
        double scaleY = 1d,
        double shadowScaleX = 1d,
        double shadowOpacity = 0.18d,
        bool mirror = false,
        bool layered = false)
        => new(
            asset,
            mirror,
            null,
            false,
            0d,
            layered,
            x,
            y,
            rotation,
            scaleX,
            scaleY,
            0d,
            0d,
            0d,
            0d,
            0d,
            0d,
            0d,
            0d,
            shadowScaleX,
            shadowOpacity);
}

internal sealed class MachineCatAnimator
{
    // The second real-Windows recording showed that whole-character alpha blending
    // creates an obvious double image. State/gait hand-offs therefore switch a
    // single opaque approved asset at the narrowest point of a short squash.
    private const double TransitionSeconds = 0.14d;
    internal const double WalkFrequency = 1.82d;
    internal const double RunFrequency = 3.62d;

    private PetState _state = PetState.Idle;
    private double _timeInState;
    private double _transitionElapsed = TransitionSeconds;
    private RigPose _lastPose = SampleState(PetState.Idle, 0d, Vector2.Zero);
    private RigPose _transitionFrom = SampleState(PetState.Idle, 0d, Vector2.Zero);

    public PetState State => _state;
    public RigPose CurrentPose => _lastPose;

    public void SetState(PetState state)
    {
        if (state == _state) return;
        _transitionFrom = _lastPose;
        _state = state;
        _timeInState = 0d;
        _transitionElapsed = 0d;
    }

    public RigPose Update(double deltaTime, Vector2 mouseDirection)
    {
        deltaTime = ClampDelta(deltaTime);
        _timeInState += deltaTime;
        _transitionElapsed += deltaTime;

        var target = SampleState(_state, _timeInState, mouseDirection);
        if (_transitionElapsed >= TransitionSeconds)
            return _lastPose = target;

        var raw = Math.Clamp(_transitionElapsed / TransitionSeconds, 0d, 1d);
        var t = SmoothStep(raw);
        var visual = raw < 0.5d ? _transitionFrom : target;
        var squeeze = Math.Sin(raw * Math.PI);

        return _lastPose = visual with
        {
            SecondaryAsset = null,
            SecondaryOpacity = 0d,
            UseLayeredRig = false,
            RootX = Mix(_transitionFrom.RootX, target.RootX, t),
            RootY = Mix(_transitionFrom.RootY, target.RootY, t),
            RootRotation = Mix(_transitionFrom.RootRotation, target.RootRotation, t),
            RootScaleX = Mix(_transitionFrom.RootScaleX, target.RootScaleX, t) * (1d - (squeeze * 0.045d)),
            RootScaleY = Mix(_transitionFrom.RootScaleY, target.RootScaleY, t),
            ShadowScaleX = Mix(_transitionFrom.ShadowScaleX, target.ShadowScaleX, t),
            ShadowOpacity = Mix(_transitionFrom.ShadowOpacity, target.ShadowOpacity, t)
        };
    }

    internal static double ClampDelta(double deltaTime)
    {
        if (!double.IsFinite(deltaTime) || deltaTime <= 0d) return 1d / 120d;
        return Math.Min(deltaTime, 0.05d);
    }

    internal static RigPose SampleState(PetState state, double time, Vector2 mouseDirection)
    {
        mouseDirection = new Vector2(
            Math.Clamp(mouseDirection.X, -1f, 1f),
            Math.Clamp(mouseDirection.Y, -1f, 1f));

        return state switch
        {
            PetState.Walk => SampleGait("Walk", time, WalkFrequency, run: false),
            PetState.Run => SampleGait("Run", time, RunFrequency, run: true),
            PetState.Turn => SampleTurn(time),
            PetState.Observe => SampleObserve(time, mouseDirection),
            PetState.Raised => SampleRaised(time),
            PetState.Recover => SampleRecover(time),
            PetState.Sleep => SampleSleep(time),
            _ => SampleIdle(time)
        };
    }

    private static RigPose SampleIdle(double time)
    {
        var breath = Math.Sin(time * Math.PI * 2d * 0.34d);
        var sway = Math.Sin(time * 0.70d);
        return RigPose.Single(
            "Idle",
            x: sway * 0.22d,
            y: breath * -0.48d,
            rotation: sway * 0.25d,
            scaleX: 1d - (breath * 0.002d),
            scaleY: 1d + (breath * 0.005d),
            shadowScaleX: 1d - (breath * 0.008d));
    }

    private static RigPose SampleGait(string asset, double time, double frequency, bool run)
    {
        var phase = PositiveModulo(time * frequency, 1d);
        var cycle = phase * Math.PI * 2d;
        var sway = Math.Sin(cycle);
        var lift = Math.Abs(sway);
        var mirror = phase >= 0.5d;

        // Switch the mirrored stride only when horizontal/rotation offsets are zero.
        // Around phase 0 / 0.5 the sprite narrows a few percent, hiding the hand-off
        // without ever drawing two cats at the same time.
        var switchDistance = Math.Min(
            Math.Abs(phase - 0.5d),
            Math.Min(phase, 1d - phase));
        var switchEnvelope = 1d - Math.Clamp(switchDistance / 0.09d, 0d, 1d);
        switchEnvelope *= switchEnvelope;
        var handoffScale = 1d - (switchEnvelope * (run ? 0.060d : 0.045d));

        if (!run)
        {
            return RigPose.Single(
                asset,
                mirror: mirror,
                x: sway * 0.55d,
                y: -0.55d - (lift * 1.25d),
                rotation: sway * 0.70d,
                scaleX: handoffScale * (1d + (lift * 0.002d)),
                scaleY: 1d - (lift * 0.004d),
                shadowScaleX: 0.99d + (lift * 0.018d),
                shadowOpacity: 0.18d);
        }

        var flight = Math.Max(0d, sway);
        var compression = Math.Abs(Math.Cos(cycle));
        return RigPose.Single(
            asset,
            mirror: mirror,
            x: sway * 0.75d,
            y: -2.0d - (flight * 2.75d),
            rotation: 1.15d + (sway * 0.85d),
            scaleX: handoffScale * (1d + (compression * 0.006d)),
            scaleY: 1d - (compression * 0.010d),
            shadowScaleX: 0.94d + (compression * 0.050d),
            shadowOpacity: 0.17d - (flight * 0.028d));
    }

    private static RigPose SampleTurn(double time)
    {
        var sequence = new (string Asset, bool Mirror)[]
        {
            ("TurnFront", false),
            ("TurnThreeQuarter", false),
            ("TurnSide", false),
            ("TurnBack", false),
            ("TurnSide", true),
            ("TurnThreeQuarter", true),
            ("TurnFront", false)
        };

        const double cycleSeconds = 4.45d;
        var progress = PositiveModulo(time, cycleSeconds) / cycleSeconds;
        var segmentPosition = progress * (sequence.Length - 1);
        var index = Math.Min(sequence.Length - 2, (int)Math.Floor(segmentPosition));
        var local = segmentPosition - index;
        var visual = local < 0.5d ? sequence[index] : sequence[index + 1];
        var handoffDistance = Math.Abs(local - 0.5d) / 0.5d;
        var squeeze = 1d - (Math.Pow(1d - Math.Clamp(handoffDistance, 0d, 1d), 2d) * 0.105d);
        var wave = Math.Sin(progress * Math.PI * 2d);

        return RigPose.Single(
            visual.Asset,
            mirror: visual.Mirror,
            x: wave * 0.38d,
            y: -Math.Abs(wave) * 0.35d,
            rotation: wave * 0.22d,
            scaleX: squeeze,
            shadowScaleX: 1d - (Math.Abs(wave) * 0.018d));
    }

    private static RigPose SampleObserve(double time, Vector2 mouseDirection)
    {
        var settle = 1d - Math.Exp(-Math.Max(0d, time) * 4.2d);
        var micro = Math.Sin(time * 1.35d);
        return RigPose.Single(
            "Observe",
            x: mouseDirection.X * 1.45d * settle,
            y: mouseDirection.Y * 0.42d * settle,
            rotation: (mouseDirection.X * 1.45d * settle) + (micro * 0.28d),
            scaleY: 1d + (Math.Sin(time * 2.2d) * 0.002d));
    }

    private static RigPose SampleRaised(double time)
    {
        var pendulum = Math.Sin(time * 2.35d);
        var bob = Math.Sin(time * 3.35d + 0.6d);
        return RigPose.Single(
            "Raised",
            x: pendulum * 1.55d,
            y: -14d + (bob * 0.95d),
            rotation: pendulum * 2.6d,
            scaleY: 1.006d,
            shadowScaleX: 0.72d,
            shadowOpacity: 0.11d);
    }

    private static RigPose SampleRecover(double time)
    {
        var envelope = Math.Exp(-Math.Max(0d, time) * 3.8d);
        var bounce = Math.Cos(time * 10.8d) * envelope;
        var squash = Math.Max(0d, bounce);
        return RigPose.Single(
            "Recover",
            x: Math.Sin(time * 7d) * envelope * 0.45d,
            y: squash * 4.8d,
            rotation: Math.Sin(time * 8.5d) * envelope * 1.25d,
            scaleX: 1d + (squash * 0.020d),
            scaleY: 1d - (squash * 0.030d),
            shadowScaleX: 1.05d - (Math.Abs(bounce) * 0.038d),
            shadowOpacity: 0.18d);
    }

    private static RigPose SampleSleep(double time)
    {
        var breath = Math.Sin(time * Math.PI * 2d * 0.22d);
        return RigPose.Single(
            "Sleep",
            x: -1d,
            y: 15d,
            rotation: Math.Sin(time * 0.45d) * 0.14d,
            scaleX: 1d + (breath * 0.002d),
            scaleY: 1d + (breath * 0.007d),
            shadowScaleX: 1.10d + (breath * 0.006d),
            shadowOpacity: 0.14d);
    }

    private static double Mix(double from, double to, double amount)
        => from + ((to - from) * amount);

    private static double PositiveModulo(double value, double period)
    {
        var result = value % period;
        return result < 0d ? result + period : result;
    }

    private static double SmoothStep(double value)
    {
        value = Math.Clamp(value, 0d, 1d);
        return value * value * (3d - (2d * value));
    }
}

internal static class MachineCatSelfTest
{
    private static readonly string[] RequiredAssets =
    {
        "Idle", "Walk", "Run", "Observe", "Raised", "Recover", "Sleep",
        "TurnFront", "TurnThreeQuarter", "TurnSide", "TurnBack"
    };

    public static int Run()
    {
        try
        {
            foreach (var asset in RequiredAssets)
            {
                if (!MachineCatAssetCatalog.Contains(asset))
                    throw new InvalidOperationException($"Missing embedded asset: {asset}");
                _ = MachineCatAssetCatalog.Get(asset);
            }

            foreach (var state in Enum.GetValues<PetState>())
            {
                for (var frame = 0; frame < 720; frame++)
                    ValidatePose(state, MachineCatAnimator.SampleState(state, frame / 120d, new Vector2(0.7f, -0.4f)));
            }

            if (CountMirrorTransitions(PetState.Run, 2d) <= CountMirrorTransitions(PetState.Walk, 2d))
                throw new InvalidOperationException("Run gait cadence must exceed Walk cadence.");

            foreach (var state in new[] { PetState.Walk, PetState.Run, PetState.Turn })
            {
                for (var frame = 0; frame < 720; frame++)
                {
                    var pose = MachineCatAnimator.SampleState(state, frame / 120d, Vector2.Zero);
                    if (pose.SecondaryOpacity != 0d || pose.SecondaryAsset is not null)
                        throw new InvalidOperationException($"{state} must never alpha-crossfade whole characters.");
                }
            }

            var turnAssets = Enumerable.Range(0, 540)
                .Select(frame => MachineCatAnimator.SampleState(PetState.Turn, frame / 120d, Vector2.Zero).PrimaryAsset)
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (turnAssets < 4)
                throw new InvalidOperationException("Turn must traverse the approved multi-angle views.");

            var walkHandoff = MachineCatAnimator.SampleState(PetState.Walk, 0.5d / MachineCatAnimator.WalkFrequency, Vector2.Zero);
            if (walkHandoff.RootScaleX >= 0.99d)
                throw new InvalidOperationException("Walk mirror hand-off must be visually masked by a short squash.");

            var animator = new MachineCatAnimator();
            animator.SetState(PetState.Walk);
            for (var i = 0; i < 30; i++)
            {
                var pose = animator.Update(1d / 120d, Vector2.Zero);
                if (pose.SecondaryOpacity != 0d || pose.SecondaryAsset is not null)
                    throw new InvalidOperationException("State hand-off must not reintroduce whole-character ghosting.");
            }

            if (MachineCatAnimator.ClampDelta(1d) > 0.050001d)
                throw new InvalidOperationException("deltaTime frame-gap clamp failed.");

            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "machine-cat-self-test-error.txt"), exception.ToString());
            }
            catch
            {
            }
            return 51;
        }
    }

    private static int CountMirrorTransitions(PetState state, double seconds)
    {
        var changes = 0;
        bool? last = null;
        for (var frame = 0; frame <= seconds * 120d; frame++)
        {
            var pose = MachineCatAnimator.SampleState(state, frame / 120d, Vector2.Zero);
            if (last.HasValue && last.Value != pose.PrimaryMirror) changes++;
            last = pose.PrimaryMirror;
        }
        return changes;
    }

    private static void ValidatePose(PetState state, RigPose pose)
    {
        if (string.IsNullOrWhiteSpace(pose.PrimaryAsset))
            throw new InvalidOperationException($"{state} has no primary asset.");
        if (pose.SecondaryAsset is null && pose.SecondaryOpacity != 0d)
            throw new InvalidOperationException($"{state} has opacity without secondary asset.");

        var values = new[]
        {
            pose.SecondaryOpacity, pose.RootX, pose.RootY, pose.RootRotation,
            pose.RootScaleX, pose.RootScaleY, pose.ShadowScaleX, pose.ShadowOpacity
        };
        if (values.Any(value => !double.IsFinite(value)))
            throw new InvalidOperationException($"{state} generated a non-finite value.");
        if (pose.SecondaryOpacity is < 0d or > 1d)
            throw new InvalidOperationException($"{state} secondary opacity is invalid.");
        if (pose.RootScaleX is < 0.78d or > 1.22d || pose.RootScaleY is < 0.78d or > 1.22d)
            throw new InvalidOperationException($"{state} root scale is invalid.");
        if (pose.ShadowOpacity is < 0d or > 1d)
            throw new InvalidOperationException($"{state} shadow opacity is invalid.");
    }
}
