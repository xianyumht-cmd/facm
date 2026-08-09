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
    double RootX,
    double RootY,
    double RootRotation,
    double RootScaleX,
    double RootScaleY,
    double HeadRotation,
    double HeadY,
    double LeftArmRotation,
    double RightArmRotation,
    double LeftLegRotation,
    double RightLegRotation,
    double LeftArmY,
    double RightArmY,
    double LeftLegY,
    double RightLegY,
    double EyeX,
    double EyeY,
    double EyeOpen,
    double MouthOpen,
    double BellRotation,
    double ShadowScaleX,
    double ShadowOpacity)
{
    public static RigPose Lerp(in RigPose from, in RigPose to, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return new RigPose(
            Mix(from.RootX, to.RootX, amount),
            Mix(from.RootY, to.RootY, amount),
            Mix(from.RootRotation, to.RootRotation, amount),
            Mix(from.RootScaleX, to.RootScaleX, amount),
            Mix(from.RootScaleY, to.RootScaleY, amount),
            Mix(from.HeadRotation, to.HeadRotation, amount),
            Mix(from.HeadY, to.HeadY, amount),
            Mix(from.LeftArmRotation, to.LeftArmRotation, amount),
            Mix(from.RightArmRotation, to.RightArmRotation, amount),
            Mix(from.LeftLegRotation, to.LeftLegRotation, amount),
            Mix(from.RightLegRotation, to.RightLegRotation, amount),
            Mix(from.LeftArmY, to.LeftArmY, amount),
            Mix(from.RightArmY, to.RightArmY, amount),
            Mix(from.LeftLegY, to.LeftLegY, amount),
            Mix(from.RightLegY, to.RightLegY, amount),
            Mix(from.EyeX, to.EyeX, amount),
            Mix(from.EyeY, to.EyeY, amount),
            Mix(from.EyeOpen, to.EyeOpen, amount),
            Mix(from.MouthOpen, to.MouthOpen, amount),
            Mix(from.BellRotation, to.BellRotation, amount),
            Mix(from.ShadowScaleX, to.ShadowScaleX, amount),
            Mix(from.ShadowOpacity, to.ShadowOpacity, amount));
    }

    private static double Mix(double from, double to, double amount) => from + ((to - from) * amount);
}

internal sealed class MachineCatAnimator
{
    private const double TransitionSeconds = 0.18;

    private PetState _state = PetState.Idle;
    private double _timeInState;
    private double _transitionElapsed = TransitionSeconds;
    private RigPose _transitionFrom = SampleState(PetState.Idle, 0, Vector2.Zero);
    private RigPose _lastPose = SampleState(PetState.Idle, 0, Vector2.Zero);

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
        if (_transitionElapsed < TransitionSeconds)
        {
            var t = SmoothStep(_transitionElapsed / TransitionSeconds);
            _lastPose = RigPose.Lerp(_transitionFrom, target, t);
        }
        else
        {
            _lastPose = target;
        }

        return _lastPose;
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

        var breathing = Math.Sin(time * Math.PI * 2d * 0.42d);
        var blink = BlinkAmount(time);

        return state switch
        {
            PetState.Walk => SampleWalk(time),
            PetState.Run => SampleRun(time),
            PetState.Turn => SampleTurn(time),
            PetState.Observe => SampleObserve(time, mouseDirection, blink),
            PetState.Raised => SampleRaised(time),
            PetState.Recover => SampleRecover(time, breathing),
            PetState.Sleep => SampleSleep(time),
            _ => SampleIdle(time, breathing, blink)
        };
    }

    private static RigPose SampleIdle(double time, double breathing, double blink)
    {
        var sway = Math.Sin(time * 0.9d) * 0.65d;
        return BasePose() with
        {
            RootY = breathing * 0.55d,
            RootRotation = sway,
            RootScaleX = 1d - (breathing * 0.004d),
            RootScaleY = 1d + (breathing * 0.009d),
            HeadRotation = Math.Sin(time * 0.67d) * 1.2d,
            HeadY = breathing * -0.35d,
            EyeOpen = blink,
            BellRotation = Math.Sin(time * 0.8d) * 1.1d,
            ShadowScaleX = 1d - (breathing * 0.008d)
        };
    }

    private static RigPose SampleWalk(double time)
    {
        const double frequency = 2.15d;
        var phase = time * Math.PI * 2d * frequency;
        var step = Math.Sin(phase);
        var counter = Math.Sin(phase + Math.PI);
        var bob = Math.Abs(Math.Sin(phase)) * -1.75d;
        var settle = Math.Sin(phase * 0.5d) * 1.2d;

        return BasePose() with
        {
            RootY = bob,
            RootRotation = settle,
            RootScaleY = 1d - (Math.Abs(step) * 0.008d),
            HeadRotation = -settle * 0.55d,
            HeadY = bob * 0.35d,
            LeftArmRotation = step * 18d,
            RightArmRotation = counter * 18d,
            LeftLegRotation = counter * 22d,
            RightLegRotation = step * 22d,
            LeftArmY = Math.Max(0d, -step) * -1.3d,
            RightArmY = Math.Max(0d, step) * -1.3d,
            LeftLegY = Math.Max(0d, step) * -2.1d,
            RightLegY = Math.Max(0d, -step) * -2.1d,
            BellRotation = step * 5.5d,
            ShadowScaleX = 0.98d + (Math.Abs(step) * 0.025d)
        };
    }

    private static RigPose SampleRun(double time)
    {
        const double frequency = 4.35d;
        var phase = time * Math.PI * 2d * frequency;
        var step = Math.Sin(phase);
        var counter = -step;
        var compression = Math.Abs(Math.Sin(phase));
        var floatPhase = Math.Max(0d, Math.Sin(phase * 0.5d));

        return BasePose() with
        {
            RootY = -2.2d - (floatPhase * 2.4d),
            RootRotation = 4.5d + (Math.Sin(phase * 0.5d) * 1.4d),
            RootScaleX = 1d + (compression * 0.012d),
            RootScaleY = 1d - (compression * 0.022d),
            HeadRotation = -3.5d,
            HeadY = -1.5d,
            LeftArmRotation = step * 30d,
            RightArmRotation = counter * 30d,
            LeftLegRotation = counter * 36d,
            RightLegRotation = step * 36d,
            LeftArmY = Math.Max(0d, -step) * -2.8d,
            RightArmY = Math.Max(0d, step) * -2.8d,
            LeftLegY = Math.Max(0d, step) * -4.2d,
            RightLegY = Math.Max(0d, -step) * -4.2d,
            MouthOpen = 1.12d,
            BellRotation = step * 10d,
            ShadowScaleX = 0.92d + (compression * 0.08d),
            ShadowOpacity = 0.23d - (floatPhase * 0.06d)
        };
    }

    private static RigPose SampleTurn(double time)
    {
        var phase = time * Math.PI * 2d * 0.72d;
        var turn = Math.Sin(phase);
        var brace = Math.Sin(phase + (Math.PI / 2d));

        return BasePose() with
        {
            RootX = turn * 2.6d,
            RootY = Math.Abs(turn) * -0.8d,
            RootRotation = turn * 16d,
            HeadRotation = turn * 6.5d,
            LeftArmRotation = (-turn * 10d) + (brace * 3d),
            RightArmRotation = (-turn * 10d) - (brace * 3d),
            LeftLegRotation = turn * 9d,
            RightLegRotation = turn * 9d,
            LeftLegY = Math.Max(0d, turn) * -2.2d,
            RightLegY = Math.Max(0d, -turn) * -2.2d,
            EyeX = turn * 1.6d,
            BellRotation = -turn * 7d,
            ShadowScaleX = 1d + (Math.Abs(turn) * 0.035d)
        };
    }

    private static RigPose SampleObserve(double time, Vector2 mouseDirection, double blink)
    {
        var curiosity = 1d - Math.Exp(-Math.Max(0d, time) * 4.2d);
        var micro = Math.Sin(time * 1.7d) * 0.8d;

        return BasePose() with
        {
            RootY = Math.Sin(time * 2.1d) * 0.25d,
            HeadRotation = (mouseDirection.X * 10d * curiosity) + micro,
            HeadY = mouseDirection.Y * 1.2d * curiosity,
            LeftArmRotation = -4d,
            RightArmRotation = 4d,
            EyeX = mouseDirection.X * 3.8d * curiosity,
            EyeY = mouseDirection.Y * 2.6d * curiosity,
            EyeOpen = Math.Max(0.78d, blink),
            MouthOpen = 0.62d,
            BellRotation = -mouseDirection.X * 2.2d * curiosity
        };
    }

    private static RigPose SampleRaised(double time)
    {
        var sway = Math.Sin(time * 2.2d);
        var limb = Math.Sin(time * 3.3d);

        return BasePose() with
        {
            RootY = -18d + (Math.Sin(time * 1.5d) * 1.2d),
            RootRotation = sway * 4.5d,
            RootScaleY = 1.018d,
            HeadRotation = -sway * 2.4d,
            LeftArmRotation = 18d + (limb * 5d),
            RightArmRotation = -18d - (limb * 5d),
            LeftLegRotation = 12d - (limb * 7d),
            RightLegRotation = -12d + (limb * 7d),
            LeftArmY = 5d,
            RightArmY = 5d,
            LeftLegY = 7d,
            RightLegY = 7d,
            EyeY = 1.2d,
            MouthOpen = 0.72d,
            BellRotation = sway * 9d,
            ShadowScaleX = 0.72d,
            ShadowOpacity = 0.13d
        };
    }

    private static RigPose SampleRecover(double time, double breathing)
    {
        var envelope = Math.Exp(-Math.Max(0d, time) * 3.6d);
        var bounce = Math.Cos(time * 11.5d) * envelope;
        var settle = 1d - Math.Exp(-Math.Max(0d, time) * 4.4d);

        return BasePose() with
        {
            RootY = Math.Max(0d, bounce) * 9d,
            RootRotation = Math.Sin(time * 8d) * envelope * 3d,
            RootScaleX = 1d + (Math.Max(0d, bounce) * 0.05d),
            RootScaleY = 1d - (Math.Max(0d, bounce) * 0.065d) + (breathing * 0.005d * settle),
            HeadY = -Math.Max(0d, bounce) * 1.5d,
            LeftArmRotation = -bounce * 8d,
            RightArmRotation = bounce * 8d,
            LeftLegRotation = bounce * 6d,
            RightLegRotation = -bounce * 6d,
            BellRotation = -bounce * 11d,
            ShadowScaleX = 1.12d - (Math.Abs(bounce) * 0.1d)
        };
    }

    private static RigPose SampleSleep(double time)
    {
        var breath = Math.Sin(time * Math.PI * 2d * 0.24d);

        return BasePose() with
        {
            RootX = -5d,
            RootY = 22d,
            RootRotation = -78d,
            RootScaleX = 1.02d + (breath * 0.004d),
            RootScaleY = 0.91d + (breath * 0.012d),
            HeadRotation = 7d,
            HeadY = 3d,
            LeftArmRotation = 24d,
            RightArmRotation = -12d,
            LeftLegRotation = 18d,
            RightLegRotation = -18d,
            EyeOpen = 0.06d,
            MouthOpen = 0.45d,
            BellRotation = 4d,
            ShadowScaleX = 1.22d,
            ShadowOpacity = 0.18d
        };
    }

    private static RigPose BasePose() => new(
        RootX: 0d,
        RootY: 0d,
        RootRotation: 0d,
        RootScaleX: 1d,
        RootScaleY: 1d,
        HeadRotation: 0d,
        HeadY: 0d,
        LeftArmRotation: 0d,
        RightArmRotation: 0d,
        LeftLegRotation: 0d,
        RightLegRotation: 0d,
        LeftArmY: 0d,
        RightArmY: 0d,
        LeftLegY: 0d,
        RightLegY: 0d,
        EyeX: 0d,
        EyeY: 0d,
        EyeOpen: 1d,
        MouthOpen: 1d,
        BellRotation: 0d,
        ShadowScaleX: 1d,
        ShadowOpacity: 0.22d);

    private static double BlinkAmount(double time)
    {
        var local = PositiveModulo(time, 4.65d);
        if (local > 0.15d) return 1d;
        var normalized = local / 0.15d;
        var closeOpen = Math.Abs((normalized * 2d) - 1d);
        return 0.08d + (0.92d * SmoothStep(closeOpen));
    }

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
    public static int Run()
    {
        try
        {
            foreach (var state in Enum.GetValues<PetState>())
            {
                for (var frame = 0; frame < 720; frame++)
                {
                    var time = frame / 120d;
                    var pose = MachineCatAnimator.SampleState(state, time, new Vector2(0.75f, -0.4f));
                    ValidateFinite(state, pose);
                }
            }

            var walkA = MachineCatAnimator.SampleState(PetState.Walk, 0.00d, Vector2.Zero);
            var walkB = MachineCatAnimator.SampleState(PetState.Walk, 0.12d, Vector2.Zero);
            var runA = MachineCatAnimator.SampleState(PetState.Run, 0.00d, Vector2.Zero);
            var runB = MachineCatAnimator.SampleState(PetState.Run, 0.12d, Vector2.Zero);
            if (Math.Abs(runB.LeftLegRotation - runA.LeftLegRotation) <= Math.Abs(walkB.LeftLegRotation - walkA.LeftLegRotation))
                throw new InvalidOperationException("Run 步态变化应明显快于 Walk。 ");

            var sleep = MachineCatAnimator.SampleState(PetState.Sleep, 2d, Vector2.Zero);
            if (sleep.EyeOpen > 0.1d) throw new InvalidOperationException("Sleep 必须闭眼。 ");

            var observe = MachineCatAnimator.SampleState(PetState.Observe, 2d, new Vector2(1f, 1f));
            if (observe.EyeX <= 0d || observe.EyeY <= 0d) throw new InvalidOperationException("Observe 眼神方向映射失败。 ");

            if (MachineCatAnimator.ClampDelta(1d) > 0.050001d)
                throw new InvalidOperationException("deltaTime frame-gap clamp 失败。 ");

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
                // Best-effort diagnostic only.
            }
            return 51;
        }
    }

    private static void ValidateFinite(PetState state, RigPose pose)
    {
        var values = new[]
        {
            pose.RootX, pose.RootY, pose.RootRotation, pose.RootScaleX, pose.RootScaleY,
            pose.HeadRotation, pose.HeadY, pose.LeftArmRotation, pose.RightArmRotation,
            pose.LeftLegRotation, pose.RightLegRotation, pose.LeftArmY, pose.RightArmY,
            pose.LeftLegY, pose.RightLegY, pose.EyeX, pose.EyeY, pose.EyeOpen,
            pose.MouthOpen, pose.BellRotation, pose.ShadowScaleX, pose.ShadowOpacity
        };

        if (values.Any(value => !double.IsFinite(value)))
            throw new InvalidOperationException($"{state} 产生了非有限动画参数。 ");
        if (pose.RootScaleX is < 0.7d or > 1.3d || pose.RootScaleY is < 0.7d or > 1.3d)
            throw new InvalidOperationException($"{state} 根缩放越界。 ");
        if (pose.EyeOpen is < 0d or > 1.2d)
            throw new InvalidOperationException($"{state} 眼睛开合越界。 ");
        if (pose.ShadowOpacity is < 0d or > 1d)
            throw new InvalidOperationException($"{state} 阴影透明度越界。 ");
    }
}
