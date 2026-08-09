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
    double RootX,
    double RootY,
    double RootRotation,
    double RootScaleX,
    double RootScaleY,
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
        bool mirror = false)
        => new(asset, mirror, null, false, 0d, x, y, rotation, scaleX, scaleY, shadowScaleX, shadowOpacity);

    public static RigPose CrossFade(
        string primary,
        bool primaryMirror,
        string secondary,
        bool secondaryMirror,
        double blend,
        double x = 0d,
        double y = 0d,
        double rotation = 0d,
        double scaleX = 1d,
        double scaleY = 1d,
        double shadowScaleX = 1d,
        double shadowOpacity = 0.18d)
        => new(
            primary,
            primaryMirror,
            secondary,
            secondaryMirror,
            Math.Clamp(blend, 0d, 1d),
            x,
            y,
            rotation,
            scaleX,
            scaleY,
            shadowScaleX,
            shadowOpacity);
}

internal sealed class MachineCatAnimator
{
    private const double TransitionSeconds = 0.16d;
    internal const double WalkFrequency = 2.05d;
    internal const double RunFrequency = 4.40d;

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

        var t = SmoothStep(_transitionElapsed / TransitionSeconds);
        return _lastPose = RigPose.CrossFade(
            DominantAsset(_transitionFrom),
            DominantMirror(_transitionFrom),
            DominantAsset(target),
            DominantMirror(target),
            t,
            Mix(_transitionFrom.RootX, target.RootX, t),
            Mix(_transitionFrom.RootY, target.RootY, t),
            Mix(_transitionFrom.RootRotation, target.RootRotation, t),
            Mix(_transitionFrom.RootScaleX, target.RootScaleX, t),
            Mix(_transitionFrom.RootScaleY, target.RootScaleY, t),
            Mix(_transitionFrom.ShadowScaleX, target.ShadowScaleX, t),
            Mix(_transitionFrom.ShadowOpacity, target.ShadowOpacity, t));
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
        var breath = Math.Sin(time * Math.PI * 2d * 0.36d);
        var sway = Math.Sin(time * 0.72d);
        return RigPose.Single(
            "Idle",
            x: sway * 0.30d,
            y: breath * -0.65d,
            rotation: sway * 0.45d,
            scaleX: 1d - (breath * 0.003d),
            scaleY: 1d + (breath * 0.007d),
            shadowScaleX: 1d - (breath * 0.01d));
    }

    private static RigPose SampleGait(string asset, double time, double frequency, bool run)
    {
        var phase = PositiveModulo(time * frequency, 1d);
        var cycle = phase * Math.PI * 2d;
        var sway = Math.Sin(cycle);
        var lift = Math.Abs(Math.Sin(cycle));
        var (primaryMirror, secondaryMirror, blend) = GaitViews(phase);

        if (!run)
        {
            return RigPose.CrossFade(
                asset, primaryMirror, asset, secondaryMirror, blend,
                x: sway * 0.85d,
                y: -0.8d - (lift * 1.8d),
                rotation: sway * 1.25d,
                scaleX: 1d + (lift * 0.003d),
                scaleY: 1d - (lift * 0.006d),
                shadowScaleX: 0.99d + (lift * 0.025d),
                shadowOpacity: 0.19d);
        }

        var flight = Math.Max(0d, Math.Sin(cycle));
        var compression = Math.Abs(Math.Cos(cycle));
        return RigPose.CrossFade(
            asset, primaryMirror, asset, secondaryMirror, blend,
            x: sway * 1.25d,
            y: -2.5d - (flight * 3.1d),
            rotation: 1.8d + (sway * 1.35d),
            scaleX: 1d + (compression * 0.008d),
            scaleY: 1d - (compression * 0.014d),
            shadowScaleX: 0.94d + (compression * 0.055d),
            shadowOpacity: 0.18d - (flight * 0.035d));
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

        const double cycleSeconds = 4.20d;
        var progress = PositiveModulo(time, cycleSeconds) / cycleSeconds;
        var segmentPosition = progress * (sequence.Length - 1);
        var index = Math.Min(sequence.Length - 2, (int)Math.Floor(segmentPosition));
        var local = segmentPosition - index;
        var blend = SmoothStep(Math.Clamp((local - 0.64d) / 0.26d, 0d, 1d));
        var a = sequence[index];
        var b = sequence[index + 1];
        var wave = Math.Sin(progress * Math.PI * 2d);

        return RigPose.CrossFade(
            a.Asset, a.Mirror, b.Asset, b.Mirror, blend,
            x: wave * 0.55d,
            y: -Math.Abs(wave) * 0.55d,
            rotation: wave * 0.35d,
            shadowScaleX: 1d - (Math.Abs(wave) * 0.025d));
    }

    private static RigPose SampleObserve(double time, Vector2 mouseDirection)
    {
        var settle = 1d - Math.Exp(-Math.Max(0d, time) * 4.2d);
        var micro = Math.Sin(time * 1.35d);
        return RigPose.Single(
            "Observe",
            x: mouseDirection.X * 1.7d * settle,
            y: mouseDirection.Y * 0.55d * settle,
            rotation: (mouseDirection.X * 1.8d * settle) + (micro * 0.35d),
            scaleY: 1d + (Math.Sin(time * 2.2d) * 0.002d));
    }

    private static RigPose SampleRaised(double time)
    {
        var pendulum = Math.Sin(time * 2.5d);
        var bob = Math.Sin(time * 3.7d + 0.6d);
        return RigPose.Single(
            "Raised",
            x: pendulum * 1.8d,
            y: -14d + (bob * 1.1d),
            rotation: pendulum * 3d,
            scaleY: 1.008d,
            shadowScaleX: 0.72d,
            shadowOpacity: 0.11d);
    }

    private static RigPose SampleRecover(double time)
    {
        var envelope = Math.Exp(-Math.Max(0d, time) * 3.7d);
        var bounce = Math.Cos(time * 11d) * envelope;
        var squash = Math.Max(0d, bounce);
        return RigPose.Single(
            "Recover",
            x: Math.Sin(time * 7d) * envelope * 0.55d,
            y: squash * 5.5d,
            rotation: Math.Sin(time * 8.5d) * envelope * 1.5d,
            scaleX: 1d + (squash * 0.024d),
            scaleY: 1d - (squash * 0.035d),
            shadowScaleX: 1.06d - (Math.Abs(bounce) * 0.045d),
            shadowOpacity: 0.19d);
    }

    private static RigPose SampleSleep(double time)
    {
        var breath = Math.Sin(time * Math.PI * 2d * 0.22d);
        return RigPose.Single(
            "Sleep",
            x: -1d,
            y: 15d,
            rotation: Math.Sin(time * 0.45d) * 0.18d,
            scaleX: 1d + (breath * 0.003d),
            scaleY: 1d + (breath * 0.009d),
            shadowScaleX: 1.10d + (breath * 0.008d),
            shadowOpacity: 0.14d);
    }

    private static (bool PrimaryMirror, bool SecondaryMirror, double Blend) GaitViews(double phase)
    {
        if (phase < 0.36d) return (false, true, 0d);
        if (phase < 0.50d)
            return (false, true, SmoothStep((phase - 0.36d) / 0.14d));
        if (phase < 0.86d) return (true, false, 0d);
        return (true, false, SmoothStep((phase - 0.86d) / 0.14d));
    }

    private static string DominantAsset(in RigPose pose)
        => pose.SecondaryAsset is not null && pose.SecondaryOpacity >= 0.5d
            ? pose.SecondaryAsset
            : pose.PrimaryAsset;

    private static bool DominantMirror(in RigPose pose)
        => pose.SecondaryAsset is not null && pose.SecondaryOpacity >= 0.5d
            ? pose.SecondaryMirror
            : pose.PrimaryMirror;

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
                throw new InvalidOperationException("Run gait cadence must exceed Walk gait cadence.");

            var turnAssets = Enumerable.Range(0, 505)
                .Select(frame => MachineCatAnimator.SampleState(PetState.Turn, frame / 120d, Vector2.Zero).PrimaryAsset)
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (turnAssets < 4)
                throw new InvalidOperationException("Turn must traverse the approved multi-angle views.");

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
        var count = 0;
        bool? last = null;
        for (var frame = 0; frame <= seconds * 120d; frame++)
        {
            var pose = MachineCatAnimator.SampleState(state, frame / 120d, Vector2.Zero);
            var mirror = pose.SecondaryAsset is not null && pose.SecondaryOpacity >= 0.5d
                ? pose.SecondaryMirror
                : pose.PrimaryMirror;
            if (last.HasValue && last.Value != mirror) count++;
            last = mirror;
        }
        return count;
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
            throw new InvalidOperationException($"{state} crossfade opacity is invalid.");
        if (pose.RootScaleX is < 0.7d or > 1.3d || pose.RootScaleY is < 0.7d or > 1.3d)
            throw new InvalidOperationException($"{state} root scale is invalid.");
        if (pose.ShadowOpacity is < 0d or > 1d)
            throw new InvalidOperationException($"{state} shadow opacity is invalid.");
    }
}
