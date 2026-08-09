using System.Numerics;
using System.Windows.Media.Imaging;

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
    bool UseProceduralGait,
    double GaitPhase,
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
        bool mirror = false,
        bool proceduralGait = false,
        double gaitPhase = 0d)
        => new(
            asset,
            mirror,
            null,
            false,
            0d,
            proceduralGait,
            gaitPhase,
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
    // Real-Windows Gate 1 recordings established two hard rules:
    // 1) never alpha-blend two complete characters;
    // 2) never fake gait by flipping the complete character left/right.
    // Walk/Run now expose a continuous gait phase and the renderer deforms only
    // local hand/foot regions of the already-approved source PNG in memory.
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
            RootX = Mix(_transitionFrom.RootX, target.RootX, t),
            RootY = Mix(_transitionFrom.RootY, target.RootY, t),
            RootRotation = Mix(_transitionFrom.RootRotation, target.RootRotation, t),
            RootScaleX = Mix(_transitionFrom.RootScaleX, target.RootScaleX, t) * (1d - (squeeze * 0.035d)),
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
        var lift = Math.Abs(Math.Sin(cycle));

        if (!run)
        {
            return RigPose.Single(
                asset,
                proceduralGait: true,
                gaitPhase: phase,
                x: sway * 0.28d,
                y: -0.40d - (lift * 0.72d),
                rotation: sway * 0.36d,
                scaleX: 1d + (lift * 0.0015d),
                scaleY: 1d - (lift * 0.0025d),
                shadowScaleX: 0.995d + (lift * 0.012d),
                shadowOpacity: 0.18d);
        }

        var flight = Math.Max(0d, sway);
        var compression = Math.Abs(Math.Cos(cycle));
        return RigPose.Single(
            asset,
            proceduralGait: true,
            gaitPhase: phase,
            x: sway * 0.42d,
            y: -1.35d - (flight * 1.55d),
            rotation: 0.65d + (sway * 0.52d),
            scaleX: 1d + (compression * 0.0035d),
            scaleY: 1d - (compression * 0.006d),
            shadowScaleX: 0.955d + (compression * 0.035d),
            shadowOpacity: 0.17d - (flight * 0.020d));
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

            if (MachineCatAnimator.RunFrequency <= MachineCatAnimator.WalkFrequency)
                throw new InvalidOperationException("Run gait cadence must exceed Walk cadence.");

            foreach (var state in new[] { PetState.Walk, PetState.Run })
            {
                for (var frame = 0; frame < 720; frame++)
                {
                    var pose = MachineCatAnimator.SampleState(state, frame / 120d, Vector2.Zero);
                    if (!pose.UseProceduralGait)
                        throw new InvalidOperationException($"{state} must use the procedural local-deformation gait.");
                    if (pose.PrimaryMirror)
                        throw new InvalidOperationException($"{state} must not flip the whole character to fake alternating steps.");
                    if (pose.SecondaryOpacity != 0d || pose.SecondaryAsset is not null)
                        throw new InvalidOperationException($"{state} must never alpha-crossfade whole characters.");
                }
            }

            for (var frame = 0; frame < 720; frame++)
            {
                var pose = MachineCatAnimator.SampleState(PetState.Turn, frame / 120d, Vector2.Zero);
                if (pose.SecondaryOpacity != 0d || pose.SecondaryAsset is not null)
                    throw new InvalidOperationException("Turn must never alpha-crossfade whole characters.");
            }

            var walk0 = ProceduralGaitFrames.Get("Walk", 0d);
            var walkQuarter = ProceduralGaitFrames.Get("Walk", 0.25d);
            var run0 = ProceduralGaitFrames.Get("Run", 0d);
            var runQuarter = ProceduralGaitFrames.Get("Run", 0.25d);
            if (!FramesDiffer(walk0, walkQuarter))
                throw new InvalidOperationException("Walk local-deformation cache is static.");
            if (!FramesDiffer(run0, runQuarter))
                throw new InvalidOperationException("Run local-deformation cache is static.");

            var turnAssets = Enumerable.Range(0, 540)
                .Select(frame => MachineCatAnimator.SampleState(PetState.Turn, frame / 120d, Vector2.Zero).PrimaryAsset)
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (turnAssets < 4)
                throw new InvalidOperationException("Turn must traverse the approved multi-angle views.");

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

    private static bool FramesDiffer(BitmapSource first, BitmapSource second)
    {
        if (first.PixelWidth != second.PixelWidth || first.PixelHeight != second.PixelHeight)
            return true;

        var stride = first.PixelWidth * 4;
        var a = new byte[stride * first.PixelHeight];
        var b = new byte[stride * second.PixelHeight];
        first.CopyPixels(a, stride, 0);
        second.CopyPixels(b, stride, 0);

        long difference = 0;
        for (var i = 0; i < a.Length; i += 17)
        {
            difference += Math.Abs(a[i] - b[i]);
            if (difference > 256) return true;
        }
        return false;
    }

    private static void ValidatePose(PetState state, RigPose pose)
    {
        if (string.IsNullOrWhiteSpace(pose.PrimaryAsset))
            throw new InvalidOperationException($"{state} has no primary asset.");
        if (pose.SecondaryAsset is null && pose.SecondaryOpacity != 0d)
            throw new InvalidOperationException($"{state} has opacity without secondary asset.");

        var values = new[]
        {
            pose.SecondaryOpacity, pose.GaitPhase, pose.RootX, pose.RootY, pose.RootRotation,
            pose.RootScaleX, pose.RootScaleY, pose.ShadowScaleX, pose.ShadowOpacity
        };
        if (values.Any(value => !double.IsFinite(value)))
            throw new InvalidOperationException($"{state} generated a non-finite value.");
        if (pose.SecondaryOpacity is < 0d or > 1d)
            throw new InvalidOperationException($"{state} secondary opacity is invalid.");
        if (pose.UseProceduralGait && (pose.GaitPhase < 0d || pose.GaitPhase >= 1d))
            throw new InvalidOperationException($"{state} gait phase is outside [0,1).");
        if (pose.RootScaleX is < 0.78d or > 1.22d || pose.RootScaleY is < 0.78d or > 1.22d)
            throw new InvalidOperationException($"{state} root scale is invalid.");
        if (pose.ShadowOpacity is < 0d or > 1d)
            throw new InvalidOperationException($"{state} shadow opacity is invalid.");
    }
}
